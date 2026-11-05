// samplemultigen - writes a synthetic SUN_MULTITIME64 test file through the native SunAcc42.dll.
//
//   samplemultigen [output.multi] [--date yyyy-MM-dd] [--start HH:mm:ss] [--hours H] [--rate Hz]
//                  [--block seconds] [--seed N] [--no-compress] [--32]
//
// Defaults: datafiles/sample.multi, today's date, 12:00:00, 4 hours, 100 samples/s, 60 s blocks, seed 12345.
//
//   samplemultigen midc [output.multi] [--begin yyyymmdd] [--end yyyymmdd] [--csv file]
//
// Converts NLR/NREL MIDC SRRL BMS 1-minute weather data (downloaded, or from --csv) to datafiles/weather.multi.
using System.Diagnostics;
using System.Globalization;
using System.Text;
using SampleMultiGen;

if (args.Length > 0 && args[0] == "midc")
    return Midc.Run(args[1..]);

string output = Path.Combine("datafiles", "sample.multi");
DateTime date = DateTime.Today;
TimeSpan start = TimeSpan.FromHours(12);
double hours = 4, rate = 100, blockSeconds = 60;
int seed = 12345;
bool compress = true, bits32 = false;

for (int a = 0; a < args.Length; a++)
{
    string Next() => a + 1 < args.Length ? args[++a] : throw new ArgumentException($"{args[a]} needs a value");
    switch (args[a])
    {
        case "--date": date = DateTime.ParseExact(Next(), "yyyy-MM-dd", CultureInfo.InvariantCulture); break;
        case "--start": start = TimeSpan.ParseExact(Next(), @"hh\:mm\:ss", CultureInfo.InvariantCulture); break;
        case "--hours": hours = double.Parse(Next(), CultureInfo.InvariantCulture); break;
        case "--rate": rate = double.Parse(Next(), CultureInfo.InvariantCulture); break;
        case "--block": blockSeconds = double.Parse(Next(), CultureInfo.InvariantCulture); break;
        case "--seed": seed = int.Parse(Next(), CultureInfo.InvariantCulture); break;
        case "--no-compress": compress = false; break;
        case "--32": bits32 = true; break;
        case "-h" or "--help" or "/?":
            Console.WriteLine("samplemultigen [output.multi] [--date yyyy-MM-dd] [--start HH:mm:ss] [--hours H] [--rate Hz] [--block s] [--seed N] [--no-compress] [--32]");
            return 0;
        default:
            if (args[a].StartsWith('-')) { Console.Error.WriteLine($"unknown option {args[a]}"); return 2; }
            output = args[a];
            break;
    }
}

long total = (long)Math.Round(hours * 3600 * rate);
int blockPts = (int)Math.Round(blockSeconds * rate);
double dt = 1.0 / rate;
int year = date.Year, day = date.DayOfYear;
long startUs = (long)Math.Round(start.TotalMilliseconds * 1000);

var signals = Signals.Create(seed, rate);
Console.WriteLine($"SunAcc write library {SunAcc.Version}");
Console.WriteLine($"{output}: {signals.Length} parameters x {total:N0} samples, {rate} Hz from {date:yyyy-MM-dd} (day {day}) {start:hh\\:mm\\:ss}, {blockPts:N0}-sample blocks");

string? dir = Path.GetDirectoryName(Path.GetFullPath(output));
if (dir != null) Directory.CreateDirectory(dir);

// comment block: hourly events plus a [rates] table
var comment = new StringBuilder("[events]\n");
for (int h = 0; h <= (int)Math.Floor(hours); h++)
{
    long us = startUs + h * 3_600_000_000L;
    var (d, ms, _) = Split(day, us);
    comment.Append(h == 0 ? "START" : $"HOUR_{h}").Append(CultureInfo.InvariantCulture, $"={d:000}/{ms}.0\n");
}
comment.Append("[rates]\n");
for (int i = 0; i < signals.Length; i++)
    comment.Append(CultureInfo.InvariantCulture, $"{i},{signals[i].Name},{rate}\n"); // first field is the zero-based map position
uint commentSize = (uint)(Math.Ceiling((comment.Length + 1) / 1024.0) * 1024 + 1024);

var sw = Stopwatch.StartNew();
IntPtr f = SunAcc.createSunFile(Path.GetFullPath(output), SunAcc.OVERWRITE, commentSize, (uint)signals.Length, 0,
    bits32 ? SunAcc.SUN_MULTITIME : SunAcc.SUN_MULTITIME64);
if (f == IntPtr.Zero) throw new InvalidOperationException($"createSunFile failed: wrtStatus {SunAcc.wrtStatus()}");
try
{
    // header items first: h_setRefTime restores the file position incorrectly in 64-bit mode, and
    // addSunParm re-seeks before every block, so doing these before any data keeps the file safe.
    var (rd, rms, _) = Split(day, startUs);
    SunAcc.Check(SunAcc.h_setRefTime(f, (uint)year, (uint)rd, (uint)(rms / 3_600_000), (uint)(rms / 60_000 % 60),
        (uint)(rms / 1000 % 60), (uint)(rms % 1000)), "h_setRefTime");
    SunAcc.Check(SunAcc.h_setTitle(f, "Multiplot.NET synthetic sample data"), "h_setTitle");
    SunAcc.Check(SunAcc.h_setComment(f, $"samplemultigen seed {seed}, {rate} Hz, {hours} h"), "h_setComment");
    SunAcc.Check(SunAcc.h_setSerialNum(f, "SAMPLE-001"), "h_setSerialNum");
    SunAcc.Check(SunAcc.h_setTestStand(f, "SIM"), "h_setTestStand");
    SunAcc.Check(SunAcc.h_setTestPos(f, "A"), "h_setTestPos");
    SunAcc.Check(SunAcc.h_setTestType(f, "Synthetic"), "h_setTestType");
    SunAcc.Check(SunAcc.h_setTestNumb(f, "1"), "h_setTestNumb");
    SunAcc.Check(SunAcc.setCommentText(f, comment.ToString()), "setCommentText");

    var times = new TimeTag[blockPts];
    var values = new float[blockPts];
    byte options = compress ? SunAcc.LZH_COMPRESS : (byte)0;
    // blocks are interleaved across parameters, the way a real-time recorder appends them
    for (long b0 = 0; b0 < total; b0 += blockPts)
    {
        int n = (int)Math.Min(blockPts, total - b0);
        for (int i = 0; i < n; i++)
        {
            long us = startUs + (long)Math.Round((b0 + i) * 1_000_000.0 / rate);
            var (d, ms, micro) = Split(day, us);
            times[i] = new TimeTag { JulianDay = (short)d, MilliSeconds = (uint)ms, MicroSeconds = (ushort)micro };
        }
        for (int s = 0; s < signals.Length; s++)
        {
            var sig = signals[s];
            for (int i = 0; i < n; i++) values[i] = sig.Next((b0 + i) * dt);
            unsafe
            {
                fixed (float* pv = values)
                fixed (TimeTag* pt = times)
                    SunAcc.Check(SunAcc.addSunParm(f, sig.Name, sig.Name.ToLowerInvariant(), sig.Units, sig.Description,
                        s + 1, "f4", pv, "t8", pt, (uint)n, options, null), $"addSunParm({sig.Name})");
            }
        }
        if ((b0 / blockPts) % 30 == 0)
            Console.Write($"\r  {100.0 * (b0 + n) / total,5:F1}%");
    }
    Console.WriteLine("\r  100.0%");
}
finally
{
    SunAcc.closeSunFile(f);
}
Console.WriteLine($"Wrote {new FileInfo(output).Length / 1048576.0:F1} MB in {sw.Elapsed.TotalSeconds:F1} s");
foreach (var s in signals) Console.WriteLine($"  {s.Name,-9} {s.Units,-6} {s.Description}");
return 0;

// splits microseconds since midnight of 'day' into (julian day, ms of day, us) with day rollover
static (int Day, long Ms, int Micro) Split(int day, long us)
{
    long dayUs = 86_400_000_000L;
    int d = day + (int)(us / dayUs);
    us %= dayUs;
    return (d, us / 1000, (int)(us % 1000));
}
