using System.Globalization;
using System.Text;

namespace SampleMultiGen;

/// <summary>
/// Converts 1-minute weather and solar data from the NLR (formerly NREL) Measurement and Instrumentation Data Center
/// (MIDC), Solar Radiation Research Laboratory Baseline Measurement System (site BMS, Golden, Colorado), to a
/// SUN_MULTITIME64 file through SunAcc42.dll.
/// </summary>
internal static class Midc
{
    public const string ApiUrl = "https://midcdmz.nlr.gov/apps/data_api.pl";

    // name, units, description, CSV header in the MIDC raw API output
    private static readonly (string Name, string Units, string Descr, string Column)[] Channels =
    [
        ("TEMP",      "degC", "Air temperature (tower)",          "Tower Dry Bulb Temp [deg C]"),
        ("DEWPT",     "degC", "Dew point (tower)",                "Tower Dew Point Temp [deg C]"),
        ("RH",        "%",    "Relative humidity (tower)",        "Tower RH [%]"),
        ("PRESS",     "mbar", "Station pressure",                 "Station Pressure [mBar]"),
        ("SLP",       "mbar", "Sea-level pressure (estimated)",   "Sea-Level Pressure (Est) [mBar]"),
        ("WSPD",      "m/s",  "Avg wind speed @ 19 ft",           "Avg Wind Speed @ 19ft [m/s]"),
        ("WDIR",      "deg",  "Avg wind direction @ 19 ft",       "Avg Wind Direction @ 19ft [deg from N]"),
        ("WGUST",     "m/s",  "Peak wind speed @ 19 ft",          "Peak Wind Speed @ 19ft [m/s]"),
        ("WSPD33",    "m/s",  "Avg wind speed @ 33 ft",           "Avg Wind Speed @ 33ft [m/s]"),
        ("WDIR33",    "deg",  "Avg wind direction @ 33 ft",       "Avg Wind Direction @ 33ft [deg from N]"),
        ("PRECIP",    "mm",   "Precipitation per minute",         "Precipitation [mm]"),
        ("PRECIPACC", "mm",   "Precipitation accumulated",        "Precipitation (Accumulated) [mm]"),
        ("GHI",       "W/m2", "Global horizontal irradiance",     "Global CMP22 (vent/cor) [W/m^2]"),
        ("DNI",       "W/m2", "Direct normal irradiance",         "Direct NIP [W/m^2]"),
        ("DHI",       "W/m2", "Diffuse horizontal irradiance",    "Diffuse CM22-1 (vent/cor) [W/m^2]"),
        ("GHICLEAR",  "W/m2", "Clear-sky GHI model (Bird)",       "Global SAMPA/Bird (calc) [W/m^2]"),
        ("CLOUD",     "%",    "Total cloud cover (daylight)",     "Total Cloud Cover [%]"),
        ("EFIELD",    "kV/m", "Atmospheric electric field",       "Atmospheric Electric Field [kV/m]"),
        ("ZENITH",    "deg",  "Solar zenith angle",               "Zenith Angle [degrees]"),
        ("AZIMUTH",   "deg",  "Solar azimuth angle",              "Azimuth Angle [degrees]"),
    ];

    private const float FpDropOut = -1.40130E-45F; // FP_DROP_OUT from SunAcc42.h

    public static int Run(string[] args)
    {
        string output = Path.Combine("datafiles", "weather.multi");
        string? csv = null;
        string begin = "20250701", end = "20250708";
        for (int a = 0; a < args.Length; a++)
        {
            string Next() => a + 1 < args.Length ? args[++a] : throw new ArgumentException($"{args[a]} needs a value");
            switch (args[a])
            {
                case "--csv": csv = Next(); break;
                case "--begin": begin = Next(); break;
                case "--end": end = Next(); break;
                default:
                    if (args[a].StartsWith('-')) { Console.Error.WriteLine($"unknown option {args[a]}"); return 2; }
                    output = args[a];
                    break;
            }
        }

        string url = $"{ApiUrl}?site=BMS&begin={begin}&end={end}";
        string[] lines;
        if (csv != null)
            lines = File.ReadAllLines(csv);
        else
        {
            Console.WriteLine($"Downloading {url}");
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            lines = http.GetStringAsync(url).GetAwaiter().GetResult().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        }

        var header = lines[0].TrimEnd('\r').Split(',');
        int Col(string name) => Array.IndexOf(header, name) is >= 0 and var i ? i
            : throw new InvalidDataException($"column '{name}' not found in MIDC data");
        int cYear = Col("Year"), cDoy = Col("DOY"), cMst = Col("MST");
        int[] cols = Channels.Select(c => Col(c.Column)).ToArray();
        int maxCol = Math.Max(cols.Max(), Math.Max(cYear, Math.Max(cDoy, cMst)));

        // parse rows: Year, DOY, MST as HHMM (local standard time, UTC-7)
        var times = new List<TimeTag>(lines.Length);
        var data = Channels.Select(_ => new List<float>(lines.Length)).ToArray();
        int year = 0;
        for (int r = 1; r < lines.Length; r++)
        {
            var f = lines[r].TrimEnd('\r').Split(',');
            if (f.Length <= maxCol) continue; // rows may omit trailing empty columns
            int y = int.Parse(f[cYear], CultureInfo.InvariantCulture);
            if (year == 0) year = y;
            int doy = int.Parse(f[cDoy], CultureInfo.InvariantCulture);
            int hhmm = int.Parse(f[cMst], CultureInfo.InvariantCulture);
            if (y != year) doy += DateTime.IsLeapYear(year) ? 366 : 365;
            times.Add(new TimeTag { JulianDay = (short)doy, MilliSeconds = (uint)((hhmm / 100 * 60 + hhmm % 100) * 60_000) });
            for (int c = 0; c < cols.Length; c++)
            {
                string s = f[cols[c]];
                bool ok = float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v);
                // MIDC marks missing values with large negative codes (e.g. -7999); cloud cover uses -1 at night
                if (!ok || v <= -999 || (Channels[c].Name == "CLOUD" && v < 0)) v = FpDropOut;
                data[c].Add(v);
            }
        }
        int n = times.Count;
        if (n == 0) throw new InvalidDataException("no data rows");
        var t0 = times[0];
        Console.WriteLine($"SunAcc write library {SunAcc.Version}");
        Console.WriteLine($"{output}: {Channels.Length} parameters x {n:N0} samples (1/min), {year} day {t0.JulianDay} .. day {times[^1].JulianDay} MST");

        var comment = new StringBuilder("[events]\n");
        for (int i = 0; i < n; i++)
            if (times[i].MilliSeconds == 0 || i == 0)
                comment.Append(CultureInfo.InvariantCulture, $"DAY_{times[i].JulianDay:000}={times[i].JulianDay:000}/{times[i].MilliSeconds}.0\n");
        comment.Append("[rates]\n");
        for (int c = 0; c < Channels.Length; c++)
            comment.Append(CultureInfo.InvariantCulture, $"{c},{Channels[c].Name},0.016667\n");
        comment.Append("[source]\n")
            .Append("NLR (formerly NREL) MIDC, SRRL Baseline Measurement System, Golden, Colorado\n")
            .Append("Times are MST (UTC-7). DOI 10.7799/1052221\n")
            .Append(url).Append('\n');
        uint commentSize = (uint)(Math.Ceiling((comment.Length + 1) / 1024.0) * 1024 + 1024);

        string? dir = Path.GetDirectoryName(Path.GetFullPath(output));
        if (dir != null) Directory.CreateDirectory(dir);
        IntPtr w = SunAcc.createSunFile(Path.GetFullPath(output), SunAcc.OVERWRITE, commentSize, (uint)Channels.Length, 0, SunAcc.SUN_MULTITIME64);
        if (w == IntPtr.Zero) throw new InvalidOperationException($"createSunFile failed: wrtStatus {SunAcc.wrtStatus()}");
        try
        {
            // header items before any data (see README: h_setRefTime position bug in 64-bit mode)
            uint ms0 = t0.MilliSeconds;
            SunAcc.Check(SunAcc.h_setRefTime(w, (uint)year, (uint)t0.JulianDay, ms0 / 3_600_000, ms0 / 60_000 % 60, ms0 / 1000 % 60, ms0 % 1000), "h_setRefTime");
            SunAcc.Check(SunAcc.h_setTitle(w, "SRRL BMS weather, Golden CO (NLR MIDC)"), "h_setTitle");
            SunAcc.Check(SunAcc.h_setComment(w, $"1-minute data {begin}-{end}, times MST (UTC-7)"), "h_setComment");
            SunAcc.Check(SunAcc.h_setSerialNum(w, "BMS"), "h_setSerialNum");
            SunAcc.Check(SunAcc.h_setTestStand(w, "SRRL"), "h_setTestStand");
            SunAcc.Check(SunAcc.h_setTestPos(w, "Golden CO"), "h_setTestPos");
            SunAcc.Check(SunAcc.h_setTestType(w, "Weather"), "h_setTestType");
            SunAcc.Check(SunAcc.h_setTestNumb(w, begin), "h_setTestNumb");
            SunAcc.Check(SunAcc.setCommentText(w, comment.ToString()), "setCommentText");

            var timeArr = times.ToArray();
            var dataArr = data.Select(d => d.ToArray()).ToArray();
            const int block = 1440; // one day per block
            for (int b0 = 0; b0 < n; b0 += block)
            {
                int cnt = Math.Min(block, n - b0);
                for (int c = 0; c < Channels.Length; c++)
                {
                    var ch = Channels[c];
                    unsafe
                    {
                        fixed (float* pv = &dataArr[c][b0])
                        fixed (TimeTag* pt = &timeArr[b0])
                            SunAcc.Check(SunAcc.addSunParm(w, ch.Name, ch.Name.ToLowerInvariant(), ch.Units, ch.Descr, c + 1,
                                "f4", pv, "t8", pt, (uint)cnt, SunAcc.LZH_COMPRESS, null), $"addSunParm({ch.Name})");
                    }
                }
            }
        }
        finally
        {
            SunAcc.closeSunFile(w);
        }
        Console.WriteLine($"Wrote {new FileInfo(output).Length / 1024.0:F0} KB");
        foreach (var c in Channels) Console.WriteLine($"  {c.Name,-10} {c.Units,-5} {c.Descr}");
        return 0;
    }
}
