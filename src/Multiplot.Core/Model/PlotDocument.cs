// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

using Multiplot.Core.Plotting;
using Multiplot.Plugins;

namespace Multiplot.Core.Model;

/// <summary>Persistent fixed-scale defaults applied when a plot is created.</summary>
public sealed class FixedScaleDefaults
{
    public bool FixXPersist, FixedScaleX;
    public double XSetMin, XSetMax, XSetGrad;
    public readonly bool[] FixYPersist = new bool[1000];
    public readonly bool[] FixedScaleY = new bool[1000];
    public readonly double[] YSetMin = new double[1000], YSetMax = new double[1000], YSetGrad = new double[1000];
}

/// <summary>
/// State and non-drawing behaviour of a plot window: scale limits, Multi-Y groups, curve information,
/// title keywords, nomenclature strings, time strings, saved scales and data statistics.
/// </summary>
public sealed class PlotDocument
{
    private const double ScaleClipMin = -3.4e20, ScaleClipMax = 3.4e20;

    public IPlotHost Host;
    public int PlotNo;
    public List<Curve> Curves = new();
    public int NScales;
    public PlotConfig Config;
    /// <summary>Event markers of this plot (FR-MRK-001).</summary>
    public List<Marker> Markers;
    /// <summary>Redline markers of this plot (FR-MRK-010).</summary>
    public List<RedlineMarker> Redlines = new();
    public string[] XAxisNomen = { "TIME", "Seconds", "Relative Time in Seconds" };
    public string XUdef = "", YUdef = "", XTitleFormat = "", YTitleFormat = "", XTitle = "", YTitle = "";
    public string TReference = "NONE", TSlice = "";
    public string PlotDate = "", ScriptFile = "(Interactive)", UtcFormat = "%03u/%02u:%02u:%02u.%03u", UserCaption = "";
    public string BackgroundFile = "";
    public object? BackgroundImage;
    public Scale XScale = new();
    public List<Scale> YScales = new();
    public double XSetMin, XSetMax, YSetMin, YSetMax;
    public bool ForceReload, IgnoreStatic, UseTitle = true, UseSubTitle = true;
    public int TimeFreeze;
    public int MultiGroupBy = GroupBy.Independent;
    public int PrevXNomen;
    public string PrevXUdef = "";
    public float TabTime = 3e38f;
    public int TabDirection;
    public int SelectedAxis;
    public string Caption = "";
    public bool Frozen => TimeFreeze != 0;

    // layout outputs of the last render (tdxo/tdyo, ya_spacing, sym_size)
    public int GraphLeft, GraphTop, GraphRight, GraphBottom;
    public int Tdxo, Tdyo, YaSpacing = 1, SymSize = 1;
    public int RenderWidth, RenderHeight;

    /// <summary>Number of curves.</summary>
    public int NCurves => Curves.Count;

    /// <summary>Creates a plot: curves get independent scales.</summary>
    public PlotDocument(IPlotHost host, IList<Curve> curves, PlotConfig config, IEnumerable<Marker>? markers,
        string[] xAxis, string xUdef, string yUdef, string xTitle, string yTitle, string tRef, string tSlice)
    {
        Host = host;
        Config = config.Clone();
        Curves.AddRange(curves);
        NScales = Curves.Count;
        TReference = tRef;
        TSlice = tSlice;
        for (int i = 0; i < Curves.Count; i++)
        {
            Curves[i].ScaleIndex = i;
            YScales.Add(new Scale());
        }
        Markers = markers != null ? Marker.CopySet(markers) : new List<Marker>();
        XAxisNomen = (string[])xAxis.Clone();
        XUdef = xUdef;
        YUdef = yUdef;
        PrevXUdef = XUdef;
        PrevXNomen = Config.XNomen;
        XTitleFormat = xTitle;
        YTitleFormat = yTitle;
    }

    /// <summary>Plot window initialisation (captions, titles, initial scaling).</summary>
    public void Initialize(int plotNo, int multiGroupBy, string utcFormat, string userCaption, string? scriptFile,
        bool continueTitle, bool continueSubTitle, bool ignoreStatic, FixedScaleDefaults fix, DateTime now)
    {
        PlotNo = plotNo;
        MultiGroupBy = multiGroupBy;
        UtcFormat = utcFormat;
        PlotDate = ValueFormat.FormatTimeStamp(now);
        UseTitle = continueTitle;
        UseSubTitle = continueSubTitle;
        XTitle = ParseStringKeywords(XTitleFormat, -1, now);
        YTitle = ParseStringKeywords(YTitleFormat, -1, now);
        UserCaption = userCaption;
        ScriptFile = scriptFile ?? "(Interactive)";
        Caption = Config.CaptionSource switch
        {
            CaptionSource.Script => ScriptFile,
            CaptionSource.XTitle => XTitle,
            CaptionSource.YTitle => YTitle,
            CaptionSource.PlotNo => Crt.Sprintf("Plot #%d", PlotNo),
            CaptionSource.User => UserCaption,
            _ => ParseStringKeywords("[parameter]", -1, now),
        };
        IgnoreStatic = ignoreStatic;

        if (fix.FixXPersist)
        {
            XSetMin = fix.XSetMin;
            XSetMax = fix.XSetMax;
            if (XScale.FixedScale = fix.FixedScaleX) XScale.Graduation = fix.XSetGrad;
            XScale.Persist = true;
            Config.AutoScaleX = false; // a persistent Fix X range is a manual range
            if (Config.EndToEnd) GetCurveInfo();
        }
        else
            ResetMinMax(1, false);

        XScale.RMin = XSetMin;
        XScale.RMax = XSetMax;
        if (Config.Style == PlotStyle.Tabular) XScale.GetScale();

        ResetMinMax(2, false);
        for (int i = 0; i < NCurves && i < fix.FixYPersist.Length; i++)
        {
            if (fix.FixYPersist[i])
            {
                Curves[i].VMin = fix.YSetMin[i];
                Curves[i].VMax = fix.YSetMax[i];
                if (i == 0)
                {
                    YSetMin = Curves[0].VMin;
                    YSetMax = Curves[0].VMax;
                }
                if (YScales[i].FixedScale = fix.FixedScaleY[i]) YScales[i].Graduation = fix.YSetGrad[i];
                YScales[i].Persist = true;
                Config.AutoScaleY = false; // a persistent Fix Y range is a manual range
            }
        }
    }

    /// <summary>True when the X axis is time (not parameter-versus-parameter, not tabular): the autoscale toggles apply.</summary>
    public bool HasTimeAxis => !Config.PidVsPid && Config.Style != PlotStyle.Tabular;

    /// <summary>Latest time of all curves (−∞ when there is no data).</summary>
    public double LastTime()
    {
        double m = double.NegativeInfinity;
        foreach (var c in Curves)
            if (c.Points > 0 && c.Time[c.Points - 1] > m) m = c.Time[c.Points - 1];
        return m;
    }

    /// <summary>
    /// Scaling after new data arrived (real-time refresh): with Auto Scale X the time axis spans all data;
    /// without it the visible span is kept and slides along with the newest data, but only while the newest
    /// data was in view (<paramref name="previousLast"/> ≤ right edge), so a view of older data stays put.
    /// With Auto Scale Y the value axes fit the data inside the visible time range; without it they keep
    /// their scale. Plots without a time axis keep their scales.
    /// </summary>
    public void ScaleAfterNewData(double previousLast)
    {
        if (!HasTimeAxis || NCurves == 0) return;
        if (Config.AutoScaleX) ResetMinMax(1, false);
        else
        {
            double span = XSetMax - XSetMin, last = LastTime();
            if (span > 0 && last > XSetMax && previousLast <= XSetMax + span * 1e-6)
            {
                XSetMax = last;
                XSetMin = last - span;
            }
        }
        if (Config.AutoScaleY) ResetMinMax(0x0202, false);
    }

    /// <summary>Builds the Multi-Y groups.</summary>
    public int SetMultiYGroups()
    {
        NScales = 0;
        for (int p = 0; p < NCurves; p++)
        {
            var c = Curves[p];
            if (MultiGroupBy == GroupBy.Independent)
            {
                c.ScaleIndex = NScales++;
                c.ScaleName = c.Name;
                YScales[p].Name = c.Name.Trim(' ');
                YScales[p].UseCount = 1;
            }
            else
            {
                string scaleName = MultiGroupBy switch
                {
                    GroupBy.Name => c.Name,
                    GroupBy.Units => c.Data.Units,
                    GroupBy.Description => ValueFormat.StripGroupSpec(c.Data.Description),
                    GroupBy.UserDefined => c.Data.Description.Contains('\\') ? c.Data.Description[(c.Data.Description.IndexOf('\\') + 1)..] : "undefined",
                    _ => "",
                };
                scaleName = scaleName.Trim(' ');
                bool matched = false;
                for (int s = 0; s < NScales; s++)
                {
                    if (string.Equals(scaleName, YScales[s].Name, StringComparison.OrdinalIgnoreCase))
                    {
                        c.ScaleIndex = s;
                        c.ScaleName = scaleName;
                        YScales[s].UseCount++;
                        matched = true;
                        break;
                    }
                }
                if (!matched)
                {
                    c.ScaleIndex = NScales;
                    c.ScaleName = scaleName;
                    YScales[NScales].Name = scaleName;
                    YScales[NScales].UseCount = 1;
                    NScales++;
                }
            }
        }
        return NScales;
    }

    private static bool IsDrop(float f) => BitConverter.SingleToUInt32Bits(f) == DataTypes.DropOutBits;

    /// <summary>
    /// Resets the scale limits: 1 = X, 2 = Y, 3 = both; 0x200 = Y min/max within the X range.
    /// </summary>
    public void ResetMinMax(int whichAxis, bool unZoom, bool justSelectedY = false)
    {
        bool ignoreStatic = false;
        if (IgnoreStatic && (whichAxis & 1) != 0)
        {
            for (int i = 0; i < NCurves; i++)
            {
                if (Curves[i].IsDynamic)
                {
                    ignoreStatic = true;
                    XSetMin = Curves[i].Time[0];
                    XSetMax = Curves[i].Time[Curves[i].Points - 1];
                    break;
                }
            }
        }

        int yRangeUpdated = 0;
        for (int i = 0; i < NCurves; i++)
        {
            var c = Curves[i];
            if ((whichAxis & 1) != 0)
            {
                if (i == 0)
                {
                    XScale.FixedScale = false;
                    XScale.Persist = false;
                    XScale.Graduation = -1.0;
                }
                if (unZoom && XScale.IsZoomed != 0)
                {
                    if (i == 0)
                    {
                        XScale.GetZoomLevel(out XSetMin, out XSetMax, out bool persist);
                        XScale.Persist = persist;
                    }
                }
                else
                {
                    if (Config.PidVsPid || c.Name.StartsWith("xypair", StringComparison.OrdinalIgnoreCase))
                    {
                        if (i == 0)
                        {
                            if (c.Points > 0 && IsDrop(c.Time[0])) { XSetMin = 3e7; XSetMax = -3e7; }
                            else if (c.Points > 0) XSetMin = XSetMax = c.Time[0];
                        }
                        for (int j = 0; j < c.Points; j++)
                        {
                            float t = c.Time[j];
                            if (!IsDrop(t))
                            {
                                if (t < XSetMin) XSetMin = t;
                                if (t > XSetMax) XSetMax = t;
                            }
                        }
                    }
                    else if (c.Points > 0)
                    {
                        if (i == 0)
                        {
                            if (Config.EndToEnd)
                            {
                                XSetMin = 0.0;
                                XSetMax = GetCurveInfo();
                            }
                            else if (!ignoreStatic)
                            {
                                XSetMin = c.Time[0];
                                XSetMax = c.Time[c.Points - 1];
                            }
                        }
                        else if (!Config.EndToEnd)
                        {
                            if (!ignoreStatic || c.IsDynamic)
                            {
                                if (c.Time[0] < XSetMin) XSetMin = c.Time[0];
                                if (c.Time[c.Points - 1] > XSetMax) XSetMax = c.Time[c.Points - 1];
                            }
                        }
                    }
                }
            }

            if ((whichAxis & 2) != 0)
            {
                YScales[i].FixedScale = false;
                YScales[i].Persist = false;
                YScales[i].Graduation = -1.0;
                if (unZoom && YScales[i].IsZoomed != 0)
                {
                    if (!Config.Multi)
                    {
                        if (i != 0) YScales[i].GetZoomLevel(out _, out _, out _);
                        else
                        {
                            YScales[i].GetZoomLevel(out YSetMin, out YSetMax, out bool persist);
                            YScales[i].Persist = persist;
                        }
                    }
                    else
                    {
                        YScales[i].GetZoomLevel(out double mn, out double mx, out bool persist);
                        YScales[i].Persist = persist;
                        c.VMin = mn;
                        c.VMax = mx;
                        if (i == 0) { YSetMin = c.VMin; YSetMax = c.VMax; }
                        else
                        {
                            YSetMin = YSetMin < c.VMin ? YSetMin : c.VMin;
                            YSetMax = YSetMax > c.VMax ? YSetMax : c.VMax;
                        }
                    }
                }
                else
                {
                    bool inSubRange = false;
                    if (Config.Multi && justSelectedY && SelectedAxis != c.ScaleIndex) continue;
                    if (c.Points == 0) continue;
                    var v = c.Values;
                    if ((whichAxis & 0x0200) != 0)
                    {
                        bool firstVal = true;
                        for (int p = 0; p < c.Points; p++)
                        {
                            if (c.Time[p] >= (XSetMin - .0005) && c.Time[p] <= (XSetMax + .0005) && !IsDrop(v[p]))
                            {
                                inSubRange = true;
                                if (firstVal)
                                {
                                    firstVal = false;
                                    yRangeUpdated++;
                                    c.VMin = c.VMax = v[p];
                                }
                                else
                                {
                                    if (c.VMin > v[p]) c.VMin = v[p];
                                    if (c.VMax < v[p]) c.VMax = v[p];
                                }
                            }
                        }
                        if (yRangeUpdated == 0)
                        {
                            if (c.Time[0] <= XSetMax && c.Time[c.Points - 1] >= XSetMin)
                            {
                                int yPnt = TimeMath.Location((XSetMax + XSetMin) / 2.0, c.Time, c.Points, 0);
                                c.VMin = c.VMax = v[yPnt];
                                inSubRange = true;
                                yRangeUpdated = 1;
                            }
                        }
                    }
                    else
                    {
                        c.VMin = v[c.Data.MinIndex];
                        c.VMax = v[c.Data.MaxIndex];
                        yRangeUpdated++;
                        inSubRange = true;
                    }

                    if (yRangeUpdated != 0 && inSubRange)
                    {
                        if (yRangeUpdated == 1)
                        {
                            YSetMin = c.VMin;
                            YSetMax = c.VMax;
                        }
                        else
                        {
                            YSetMin = YSetMin < c.VMin ? YSetMin : c.VMin;
                            YSetMax = YSetMax > c.VMax ? YSetMax : c.VMax;
                        }
                    }
                }
            }
        }

        if (XSetMin < ScaleClipMin) XSetMin = ScaleClipMin;
        if (XSetMax > ScaleClipMax) XSetMax = ScaleClipMax;
        if (YSetMin < ScaleClipMin) YSetMin = ScaleClipMin;
        if (YSetMax > ScaleClipMax) YSetMax = ScaleClipMax;
        if ((whichAxis & 1) != 0) TabTime = 3e38f;
        SetMultiYGroups();
    }

    /// <summary>File index lookup (loop down to 1, default 0).</summary>
    public int FileIndexOf(IDataFile? f)
    {
        var files = Host.Files;
        int fileNo;
        for (fileNo = files.Count - 1; fileNo > 0; fileNo--)
            if (ReferenceEquals(files[fileNo], f)) break;
        return Math.Max(fileNo, 0);
    }

    /// <summary>Group lookup for a file index.</summary>
    public int GroupIndexOfFile(int fileNo)
    {
        var groups = Host.Groups;
        int g;
        for (g = groups.Count - 1; g > 0; g--)
            if (fileNo >= groups[g].StartPos) break;
        return Math.Max(g, 0);
    }

    /// <summary>End-to-end durations and offsets; returns the total.</summary>
    public float GetCurveInfo()
    {
        for (int i = 0; i < NCurves; i++)
        {
            var c = Curves[i];
            c.Duration = c.Points > 1 ? Math.Max(0f, c.Time[c.Points - 1] - c.Time[0]) : 0f;
            c.FileNo = FileIndexOf(c.Data.Source);
            c.GroupNo = GroupIndexOfFile(c.FileNo);
        }
        for (int i = 0; i < NCurves; i++)
            for (int j = 0; j < NCurves; j++)
                if (Curves[j].GroupNo == Curves[i].GroupNo && Curves[j].Duration > Curves[i].Duration)
                    Curves[i].Duration = Curves[j].Duration;
        for (int i = 0; i < NCurves; i++)
        {
            var ci = Curves[i];
            ci.Offset = 0f;
            for (int j = 0; j < ci.GroupNo; j++)
            {
                for (int n = 0; n < NCurves; n++)
                {
                    var cn = Curves[n];
                    if (cn.GroupNo == j)
                    {
                        if (cn.Points > 1) ci.Offset += cn.Duration + (cn.Time[1] - cn.Time[0]);
                        else
                        {
                            float rate = cn.Data.Rate;
                            ci.Offset += rate > 0f ? cn.Duration + (1f / rate) : cn.Duration + 1f;
                        }
                        break;
                    }
                }
            }
        }
        int jj = 0;
        for (int i = 1; i < NCurves; i++)
            if (Curves[i].Offset > Curves[jj].Offset) jj = i;
        return NCurves > 0 ? Curves[jj].Offset + Curves[jj].Duration : 0f;
    }

    /// <summary>Saves the current scales.</summary>
    public void SaveScales()
    {
        for (int i = 0; i < NCurves; i++)
        {
            if (Config.Multi) YScales[i].AddZoomLevel(Curves[i].VMin, Curves[i].VMax, YScales[i].Persist);
            else YScales[i].AddZoomLevel(YSetMin, YSetMax, YScales[0].Persist);
        }
        XScale.AddZoomLevel(XSetMin, XSetMax, XScale.Persist);
    }

    /// <summary>Appends curves with default attributes.</summary>
    public void AddCurves(IList<Curve> add, PlotConfig defaults)
    {
        int n = add.Count;
        while (NCurves + n > 1000) n--;
        for (int i = 0; i < n; i++)
        {
            var s = YScales.Count > 0 ? YScales[0].Clone() : new Scale();
            s.ResetZoomLevel();
            s.FixedScale = false;
            YScales.Add(s);
            var c = add[i];
            c.LineSize = defaults.LineSize;
            c.LineStyle = 0;
            c.UseSymbols = defaults.Symbols;
            c.ScatterPlot = false;
            c.SymbolId = (i + NCurves) % ColorIndex.MaxColors;
            c.ColorIndex = (i + NCurves) % ColorIndex.MaxColors;
            c.ScaleIndex = 0;
            Curves.Add(c);
        }
    }

    /// <summary>Index of the curve with the lowest start time.</summary>
    public int MinTimeParm()
    {
        int low = 0;
        for (int p = 1; p < NCurves; p++) if (Curves[p].Time[0] < Curves[low].Time[0]) low = p;
        return low;
    }

    /// <summary>Index of the curve with the highest end time.</summary>
    public int MaxTimeParm()
    {
        int high = 0;
        for (int p = 1; p < NCurves; p++)
            if (Curves[p].Time[Curves[p].Points - 1] > Curves[high].Time[Curves[high].Points - 1]) high = p;
        return high;
    }

    /// <summary>Header reference UTC of a curve's file.</summary>
    public static TimeTag HeaderUtc(ParameterData p)
    {
        if (p.RefUtc.HasValue) return p.RefUtc.Value;
        var rt = p.Source?.Info.RefTime ?? new uint[6];
        short day = (short)rt[1];
        if (day == 0) day++;
        return new TimeTag(day, unchecked((rt[2] * 3600000U) + (rt[3] * 60000U) + (rt[4] * 1000U) + rt[5]), 0);
    }

    /// <summary>Builds the time string.</summary>
    public string MakeTimeString(ParameterData p, int tPoint)
    {
        if (Config.ClockTime)
        {
            var g = HeaderUtc(p);
            double dTime = p.Time[tPoint] + (p.RefTime ?? 0.0);
            return ValueFormat.ClockString(TimeMath.Utc(dTime, g, true));
        }
        return Crt.Sprintf("%.*f", Host.TimePrecision == -1 ? 3 : Host.TimePrecision, p.Time[tPoint]);
    }

    private static readonly string[] Keys =
    {
        "[title]", "[comment]", "[serialnum]", "[teststand]", "[testposition]", "[testtype]", "[testnumber]", "[date]", "[time]",
        "[parameter]", "[pidname]", "[pidunits]", "[piddescription]", "[dbname]", "[timeslice]", "[timestart]", "[timestop]",
        "[timeref]", "[filename]", "[scriptname]", "[currentvalue]", "[currenttime]", "[groupname]",
    };

    /// <summary>Expands title keywords (FR-PLT-002).</summary>
    public string ParseStringKeywords(string formatString, int parmSource, DateTime? nowOpt = null)
    {
        DateTime now = nowOpt ?? DateTime.Now;
        int iParm = parmSource < 0 ? 0 : parmSource;
        string formatted = formatString, titleBuf = formatString, tempStr = formatString;
        bool keyFound;
        int guard = 0;
        do
        {
            keyFound = false;
            tempStr = tempStr.ToLowerInvariant();
            for (int k = 0; k < Keys.Length; k++)
            {
                int keyStart = tempStr.IndexOf(Keys[k], StringComparison.Ordinal);
                if (keyStart < 0) continue;
                int keyEnd = keyStart + Keys[k].Length;
                formatted = formatted[..keyStart];
                string add = KeywordValue(k, iParm, now);
                add = add.Replace('\\', '/').Trim(' ');
                formatted += add;
                formatted += titleBuf.Length > keyEnd ? titleBuf[keyEnd..] : "";
                titleBuf = tempStr = formatted;
                keyFound = true;
            }
        } while (keyFound && ++guard < 1000);
        return formatted.Length == 0 ? " " : formatted;
    }

    private string KeywordValue(int k, int iParm, DateTime now)
    {
        ParameterData? p = iParm < NCurves ? Curves[iParm].Data : null;
        DataFileInfo? fi = p?.Source?.Info;
        switch (k)
        {
            case 0: return fi?.Title ?? "";
            case 1: return fi?.Comment ?? "";
            case 2: return fi?.SerialNum ?? "";
            case 3: return fi?.TestStand ?? "";
            case 4: return fi?.TestPosition ?? "";
            case 5: return fi?.TestType ?? "";
            case 6: return fi?.TestNumber ?? "";
            case 7: return now.ToString("  MMMM dd, yyyy", System.Globalization.CultureInfo.InvariantCulture);
            case 8:
                return now.Hour > 11
                    ? Crt.Sprintf("%d:%02dPM", now.Hour == 12 ? 12 : now.Hour - 12, now.Minute)
                    : Crt.Sprintf("%d:%02dAM", now.Hour != 0 ? now.Hour : 12, now.Minute);
            case 9:
                {
                    if (NCurves == 0) return "";
                    string s = Curves[0].Name;
                    for (int i = 1; i < NCurves; i++)
                        if (!s.Contains(Curves[i].Name, StringComparison.Ordinal)) s = s + "," + Curves[i].Name;
                    return s;
                }
            case 10: return p?.Name ?? "";
            case 11: return p?.Units ?? "";
            case 12: return p != null ? ValueFormat.StripGroupSpec(p.Description) : "";
            case 13: return Host.Groups.Count > 0 ? Host.Groups[0].GroupName : "";
            case 14: return TSlice;
            case 15: { int c = TSlice.IndexOf(','); return c >= 0 ? TSlice[..c] : TSlice; }
            case 16:
                {
                    int c = TSlice.IndexOf(',');
                    string s = TSlice[(c + 1)..];
                    int i = s.IndexOf(',');
                    return i > 0 ? s[..i] : s;
                }
            case 17: return TSlice[(TSlice.LastIndexOf(',') + 1)..];
            case 18:
                {
                    string s = p?.Source?.Info.Path ?? "";
                    s = s[(s.LastIndexOf('\\') + 1)..];
                    int i = s.IndexOf(".~", StringComparison.Ordinal);
                    return i >= 0 ? s[..i] : s;
                }
            case 19: return ScriptFile[(ScriptFile.LastIndexOf('\\') + 1)..];
            case 20:
                {
                    if (p == null || p.Points == 0) return "";
                    char dFormat = (Config.YNomen & Nomen.EFORMAT) != 0 ? 'e' : (Host.DataPrecision == -1 ? 'g' : 'f');
                    string fmt = Host.DataPrecision == -1 ? "%" + dFormat : Crt.Sprintf("%%.%d%c", Host.DataPrecision, dFormat);
                    return Crt.Sprintf(fmt, (double)p.Values[p.Points - 1]);
                }
            case 21:
                {
                    if (p == null || p.Points == 0) return "";
                    string fmt = Host.TimePrecision == -1 ? "%f" : Crt.Sprintf("%%.%df", Host.TimePrecision);
                    return Crt.Sprintf(fmt, (double)p.Time[p.Points - 1]);
                }
            case 22: return ""; // [groupname]: plot group names were removed
            default: return "";
        }
    }

    /// <summary>Formats the REFT item.</summary>
    public static string RefTString(ParameterData t)
    {
        if (t.RefUtc.HasValue) return ValueFormat.ClockString(t.RefUtc.Value, "(T-Ref: %03u/%02u:%02u:%02u.%03u)");
        if (t.RefTime.HasValue) return Crt.Sprintf("(T-Ref: %.3f)", t.RefTime.Value);
        var rt = t.Source?.Info.RefTime ?? new uint[6];
        return Crt.Sprintf("(T-Ref: %02ld %03ld %02ld:%02ld:%02ld.%03ld)", (int)rt[0], (int)rt[1], (int)rt[2], (int)rt[3], (int)rt[4], (int)rt[5]);
    }

    /// <summary>One nomenclature item for display (text and its item index).</summary>
    public readonly record struct NomenItem(string Text, int Item);

    /// <summary>
    /// Builds the displayed nomenclature items of curve <paramref name="pidNo"/> (−1 = X axis) in configured
    /// order.
    /// </summary>
    public List<NomenItem> NomenItems(int nomItems, int pidNo, string udef, int[] order, int[] len, int? tPoint = null)
    {
        var list = new List<NomenItem>();
        for (int i = 0; i < Nomen.ITEMS; i++)
        {
            int flag = order[i];
            if ((nomItems & flag) == 0) continue;
            Curve? c = pidNo >= 0 && pidNo < NCurves ? Curves[pidNo] : null;
            switch (flag)
            {
                case Nomen.DBNAME:
                    list.Add(new(c != null ? GroupNameOf(c) : FirstGroupName(), Nomen.I_DBNAME));
                    break;
                case Nomen.FILENAME:
                    list.Add(new(c != null ? FileNameOf(c) : FirstGroupName(), Nomen.I_FILENAME));
                    break;
                case Nomen.NAME:
                    list.Add(new((c != null ? c.Name : XAxisNomen[0]).Replace("$", ""), Nomen.I_NAME));
                    break;
                case Nomen.ALTNAME:
                    if (c != null && MapInfo(c) is { } ai && ai.Names[1] != null) list.Add(new(ai.Names[1]!, Nomen.I_ALTNAME));
                    break;
                case Nomen.EXTNAME:
                    if (c != null && MapInfo(c) is { } ei && ei.Names[2] != null) list.Add(new(ei.Names[2]!, Nomen.I_EXTNAME));
                    break;
                case Nomen.UNITS:
                    list.Add(new(c != null ? c.Data.Units : XAxisNomen[1], Nomen.I_UNITS));
                    break;
                case Nomen.DESC:
                    list.Add(new(c != null ? ValueFormat.StripGroupSpec(c.Data.Description) : XAxisNomen[2], Nomen.I_DESC));
                    break;
                case Nomen.REFT:
                    if (NCurves > 0) list.Add(new(RefTString(Curves[pidNo >= 0 ? pidNo : 0].Data), Nomen.I_REFT));
                    break;
                case Nomen.UDEF:
                    list.Add(new(ParseStringKeywords(udef, pidNo), Nomen.I_UDEF));
                    break;
                case Nomen.SEQNUM:
                    list.Add(new(Crt.Sprintf("%d", c != null ? (int)c.Data.SequenceNumber : 0), Nomen.I_SEQNUM));
                    break;
                case Nomen.CURVAL:
                    if (c != null && c.Points > 0)
                    {
                        int tp = tPoint ?? c.Points - 1;
                        string s = ValueFormat.FormatValue(c.Values[tp], c.TabFormat, (Config.YNomen & Nomen.EFORMAT) != 0);
                        if (Config.Style == PlotStyle.Tabular && len[Nomen.I_CURVAL] != 0 && len[Nomen.I_CURVAL] > s.Length)
                            s = new string(' ', Math.Min(40, len[Nomen.I_CURVAL] - s.Length)) + s;
                        list.Add(new(s, Nomen.I_CURVAL));
                    }
                    break;
                case Nomen.CURTIME:
                    if (c != null && c.Points > 0)
                    {
                        string s = MakeTimeString(c.Data, tPoint ?? c.Points - 1);
                        if (Config.Style == PlotStyle.Tabular && len[Nomen.I_CURTIME] != 0 && len[Nomen.I_CURTIME] > s.Length)
                            s = new string(' ', Math.Min(40, len[Nomen.I_CURTIME] - s.Length)) + s;
                        list.Add(new(s, Nomen.I_CURTIME));
                    }
                    break;
            }
        }
        return list;
    }

    private string FirstGroupName() => Host.Groups.Count > 0 ? Host.Groups[0].GroupName : "";

    /// <summary>Group name of a curve's source file.</summary>
    public string GroupNameOf(Curve c)
    {
        int f = FileIndexOf(c.Data.Source);
        int g = GroupIndexOfFile(f);
        return g < Host.Groups.Count ? Host.Groups[g].GroupName : "";
    }

    /// <summary>File name (no path, import suffix removed).</summary>
    public static string FileNameOf(Curve c)
    {
        string s = c.Data.Source?.Info.Path ?? "";
        s = s[(s.LastIndexOf('\\') + 1)..];
        int dot = s.IndexOf(".~", StringComparison.Ordinal);
        if (dot > 1) s = s[..dot];
        return s;
    }

    private static ParameterInfo? MapInfo(Curve c)
    {
        var src = c.Data.Source;
        if (src == null) return null;
        var parms = src.Parameters;
        int m = c.Data.MapPosition;
        return m >= 0 && m < parms.Count ? parms[m] : null;
    }

    /// <summary>Nomenclature string (X axis label / string form with padding).</summary>
    public string SetNomenString(int nomItems, Curve? parm, string udef, int[] order, int[] len)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < Nomen.ITEMS; i++)
        {
            int flag = order[i];
            if ((nomItems & flag) == 0) continue;
            string? item = null;
            int padIndex = -1;
            switch (flag)
            {
                case Nomen.DBNAME: item = parm != null ? GroupNameOf(parm) : FirstGroupName(); padIndex = Nomen.I_DBNAME; break;
                case Nomen.FILENAME: item = parm != null ? FileNameOf(parm) : FirstGroupName(); padIndex = Nomen.I_FILENAME; break;
                case Nomen.NAME: item = (parm != null ? parm.Name : XAxisNomen[0]).Replace("$", ""); padIndex = Nomen.I_NAME; break;
                case Nomen.ALTNAME: if (parm != null && MapInfo(parm) is { } a && a.Names[1] != null) { item = a.Names[1]; padIndex = Nomen.I_ALTNAME; } break;
                case Nomen.EXTNAME: if (parm != null && MapInfo(parm) is { } e && e.Names[2] != null) { item = e.Names[2]; padIndex = Nomen.I_EXTNAME; } break;
                case Nomen.UNITS: item = parm != null ? parm.Data.Units : XAxisNomen[1]; padIndex = Nomen.I_UNITS; break;
                case Nomen.DESC: item = parm != null ? ValueFormat.StripGroupSpec(parm.Data.Description) : XAxisNomen[2]; padIndex = Nomen.I_DESC; break;
                case Nomen.REFT:
                    if (NCurves > 0) { item = RefTString((parm ?? Curves[0]).Data); padIndex = Nomen.I_DESC; }
                    break;
                case Nomen.UDEF: item = ParseStringKeywords(udef, -1); padIndex = Nomen.I_UDEF; break;
                case Nomen.SEQNUM: item = Crt.Sprintf("%d", parm != null ? (int)parm.Data.SequenceNumber : 0); padIndex = Nomen.I_SEQNUM; break;
            }
            if (item == null) continue;
            if (item.Length < len[padIndex]) item = item.PadRight(len[padIndex]);
            sb.Append(item).Append("  ");
        }
        return sb.ToString();
    }

    /// <summary>The X axis label.</summary>
    public string XLegend()
    {
        string x;
        if (Config.ClockTime && !Config.PidVsPid)
            x = (Config.XNomen & Nomen.UDEF) == Nomen.UDEF ? ParseStringKeywords(XUdef, 0) : "Time";
        else if (NCurves > 0 && (Curves[0].Data.RefTime.HasValue || Curves[0].Data.RefUtc.HasValue))
        {
            x = SetNomenString(Config.XNomen & ~Nomen.REFT, null, XUdef, Config.XNomOrder, Config.XNomLen);
            if ((Config.XNomen & Nomen.REFT) != 0) x += " Seconds from " + TReference;
        }
        else
            x = SetNomenString(Config.XNomen, null, XUdef, Config.XNomOrder, Config.XNomLen);
        if (TimeFreeze != 0) x += " (frozen - specified 'Time Slice' complete)";
        return x;
    }

    /// <summary>Statistics of one curve over [xmin, xmax] (FR-XPT-040).</summary>
    public sealed record CurveStats(int Points, double Min, double Max, double Avg, double Sum, double Sigma, bool SigmaValid, double TI);

    /// <summary>Computes curve statistics with fixed arithmetic rules (TI uses the raw previous element, float math).</summary>
    public static CurveStats ComputeStats(Curve c, double xmin, double xmax)
    {
        int points = 0;
        double dMin = 0, dMax = 0, sum = 0, sum2 = 0, ti = 0;
        var t = c.Time;
        var v = c.Values;
        for (int n = 0; n < c.Points; n++)
        {
            if (t[n] >= xmin && t[n] <= xmax && !IsDrop(v[n]))
            {
                sum += v[n];
                sum2 += Math.Pow(v[n], 2.0);
                if (points != 0)
                {
                    dMax = v[n] > dMax ? v[n] : dMax;
                    dMin = v[n] < dMin ? v[n] : dMin;
                    float a = (v[n - 1] + v[n]) / 2;
                    float d = t[n] - t[n - 1];
                    ti += a * d;
                }
                else
                {
                    dMax = v[n];
                    dMin = v[n];
                }
                points++;
            }
        }
        double avg = points != 0 ? sum / points : 0;
        double sigma = 0;
        if (points >= 2)
        {
            sigma = (sum2 - points * Math.Pow(avg, 2.0)) / (points - 1.0);
            sigma = (float)Math.Sqrt(0.0 > sigma ? 0.0 : sigma);
        }
        return new CurveStats(points, dMin, dMax, avg, sum, sigma, points >= 2, ti);
    }

    /// <summary>Data statistics text (FR-XPT-041).</summary>
    public string DataStatsText(int dataPrecision)
    {
        var sb = new System.Text.StringBuilder();
        if (Config.ClockTime && NCurves > 0)
        {
            var g = HeaderUtc(Curves[0].Data);
            var s = TimeMath.Utc(XScale.RMin, g, true);
            var e = TimeMath.Utc(XScale.RMax, g, true);
            sb.Append(Crt.Sprintf("Time range:  %03d/%02u:%02u:%02u.%03u   to   %03d/%02u:%02u:%02u.%03u\n\n",
                (int)s.JulianDay, (s.MilliSeconds / 3600000U) % 24, (s.MilliSeconds % 3600000U) / 60000U, (s.MilliSeconds % 60000U) / 1000U, s.MilliSeconds % 1000U,
                (int)e.JulianDay, (e.MilliSeconds / 3600000U) % 24, (e.MilliSeconds % 3600000U) / 60000U, (e.MilliSeconds % 60000U) / 1000U, e.MilliSeconds % 1000U));
        }
        else
            sb.Append(Crt.Sprintf("Time range:  %.3f   to   %.3f\n\n", XScale.RMin, XScale.RMax));
        sb.Append("Source,Parameter,Min,Max,Average,Range,Sigma,CV,TI\n");
        foreach (var c in Curves)
        {
            var st = ComputeStats(c, XScale.RMin, XScale.RMax);
            string path = c.Data.Source?.Info.Path ?? "";
            sb.Append(path[(path.LastIndexOf('\\') + 1)..]).Append(',');
            sb.Append(c.Name).Append(',');
            if (st.Points != 0)
            {
                sb.Append(ValueFormat.FormatValue((float)st.Min, dataPrecision)).Append(',');
                sb.Append(ValueFormat.FormatValue((float)st.Max, dataPrecision)).Append(',');
                sb.Append(ValueFormat.FormatValue((float)st.Avg, dataPrecision)).Append(',');
                sb.Append(ValueFormat.FormatValue((float)(st.Max - st.Min), dataPrecision)).Append(',');
                if (st.SigmaValid)
                {
                    sb.Append(ValueFormat.FormatValue((float)st.Sigma, dataPrecision)).Append(',');
                    sb.Append(st.Avg != 0.0 ? ValueFormat.FormatValue((float)(st.Sigma / st.Avg), dataPrecision) : "n/a").Append(',');
                    sb.Append(ValueFormat.FormatValue((float)st.TI, dataPrecision)).Append(',');
                }
                else
                    sb.Append("n/a,n/a,n/a,");
            }
            else
                sb.Append("No Data,*,*,*,*,*,*,");
            sb.Append('\n');
        }
        return sb.ToString();
    }
}
