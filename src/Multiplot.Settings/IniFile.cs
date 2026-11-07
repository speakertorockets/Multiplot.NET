// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;
using System.Text;
using Multiplot.Core;
using Multiplot.Core.Model;

namespace Multiplot.Settings;

/// <summary>Context of an INI load/save (version strings and side effects owned by the application).</summary>
public sealed class IniContext
{
    /// <summary>Application version string.</summary>
    public string AppVersion = "Multiplot.NET v1.0.0";
    /// <summary>Application date string.</summary>
    public string AppDate = "";
    /// <summary>Library version lines written as <c>;dll = …</c>.</summary>
    public IReadOnlyList<string> LibraryVersions = Array.Empty<string>();
    /// <summary>Sends a library command to the provider with the given signature (pcgcachesize).</summary>
    public Action<string, uint>? LibCommand;
    /// <summary>Current auto refresh interval (ms, 0 = off) used for <c>AutoRefresh</c> on save.</summary>
    public int AutoRefresh;
    /// <summary>Clock for the "Saved at" stamp.</summary>
    public DateTime? Now;
}

/// <summary>Result of <see cref="IniFile.Load"/>.</summary>
public readonly record struct IniLoadResult(bool Found);

/// <summary>
/// INI reader/writer (FR-CFG-010/011/012).
/// Keys are matched by prefix of the lower-cased text before '=' in a fixed order; sections are ignored.
/// </summary>
public static class IniFile
{
    /// <summary>Loads <paramref name="path"/> into <paramref name="s"/>; returns Found=false when the file cannot be read.</summary>
    public static IniLoadResult Load(AppSettings s, string path, IniContext ctx)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return new(false);
        List<string> lines;
        try { lines = AnsiText.ReadAllLines(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new(false); }

        bool loadSymbols = s.LoadSymbols;
        var p = s.Plot;
        int legacyFontSize = 0;

        foreach (string raw in lines)
        {
            string line = raw.Length > 32766 ? raw[..32766] : raw;
            int eq = line.IndexOf('=');
            string buf = (eq < 0 ? line : line[..eq]).ToLowerInvariant() + (eq < 0 ? "" : line[eq..]);
            string? setting = null;
            if (eq >= 0)
            {
                int k = eq + 1;
                while (k < buf.Length && buf[k] == ' ') k++;
                setting = k < buf.Length ? buf[k..] : null;
            }
            bool B() => setting == null || setting[0] != '0'; // DEV-CFG-001: missing value = true
            bool K(string key) => buf.StartsWith(key, StringComparison.Ordinal);

            if (K("dbpath")) { if (setting != null) s.DbFolder = setting; }
            else if (K("filefilters")) { if (setting != null) s.FileFilters = setting; }
            else if (buf.Length > 1 && buf.AsSpan(1).StartsWith("nomenlen")) ReadInts(buf[0] == 'x' ? p.XNomLen : p.YNomLen, setting);
            else if (buf.Length > 1 && buf.AsSpan(1).StartsWith("nomenorder")) ReadInts(buf[0] == 'x' ? p.XNomOrder : p.YNomOrder, setting);
            else if (K("xnomen")) { if (setting != null) p.XNomen = Crt.Atoi(setting); }
            else if (K("ynomen")) { if (setting != null) p.YNomen = Crt.Atoi(setting); }
            else if (K("yudef")) s.YUdef = setting ?? "";
            else if (K("xudef")) s.XUdef = setting ?? "";
            else if (K("xtitle")) s.XTitle = setting ?? "";
            else if (K("ytitle")) s.YTitle = setting ?? "";
            else if (K("colors"))
            {
                var tok = new Strtok(setting ?? "");
                string? t = tok.Next(" ");
                for (int i = 0; i < ColorIndex.Total; i++)
                {
                    if (t != null) p.DrawColor[i] = Crt.Atoi(t);
                    else
                    {
                        // legacy tables end before the title colour: grid/label/background/mono were at 25..28
                        if (i < ColorIndex.Title)
                        {
                            for (int up = 0; up < 4; up++) p.DrawColor[ColorIndex.Grid + up] = p.DrawColor[25 + up];
                            p.DrawColor[ColorIndex.Title] = p.DrawColor[ColorIndex.Label];
                        }
                        // tables written before the marker colour existed: markers keep the grid colour
                        if (i <= ColorIndex.Marker) p.DrawColor[ColorIndex.Marker] = p.DrawColor[ColorIndex.Grid];
                        break;
                    }
                    t = tok.Next(" ");
                }
            }
            else if (K("3dzangle")) { if (setting != null) p.ZAngle = Crt.Atoi(setting); }
            else if (K("printername")) { if (setting != null) s.UserPrinter = setting; }
            else if (K("textprinter")) { if (setting != null) s.TextPrinter = setting; }
            else if (K("printermargin"))
            {
                if (setting != null)
                {
                    s.MarginSize = (float)Crt.Atof(setting);
                    if (s.MarginSize < 0f || s.MarginSize > 10f) s.MarginSize = 5f;
                }
            }
            else if (K("fontsize")) legacyFontSize = Crt.Atoi(setting); // retired screen scaling factor (see ApplyLegacyFontSize)
            else if (K("titlefont")) { if (setting != null) p.TitlePointSize = ReadFont(p.TitleFont, setting, p.TitlePointSize); }
            else if (K("labelfont")) { if (setting != null) p.LabelPointSize = ReadFont(p.LabelFont, setting, p.LabelPointSize); }
            else if (K("gridstyle"))
            {
                if (setting != null)
                {
                    if (setting.Contains("solid")) p.GridStyle = 0;
                    else if (setting.Contains("dotted")) p.GridStyle = 2;
                    else if (setting.Contains("dashed")) p.GridStyle = 1;
                }
            }
            else if (K("minortics")) p.MinorTics = B();
            else if (K("boldframe")) p.BoldFrame = B();
            else if (K("scalegroup")) p.GroupId = B();
            else if (K("stairstep")) p.StairStep = B();
            else if (K("clocktime")) p.ClockTime = B();
            else if (K("utcformat")) s.UtcFormat = setting ?? "%03u/%02u:%02u:%02u.%03u";
            else if (K("ignorestatic")) s.IgnoreStatic = B();
            else if (K("dropoutmarker")) p.DropoutMarker = B();
            else if (K("dropoutignore")) p.DropoutIgnore = B();
            else if (K("pcgcachesize"))
            {
                ctx.LibCommand?.Invoke("cachesize " + setting, 0xfff0ffffu);
                s.PcgCollectorSize = Crt.Atol(setting);
            }
            else if (K("rbfunction")) s.RbFunction = setting != null ? Crt.Atoi(setting) : 0;
            else if (K("popupsglobal")) s.PopupsGlobal = B();
            else if (K("usetitle")) s.ContinueTitle = B();
            else if (K("usesubtitle")) s.ContinueSubTitle = B();
            else if (K("usemarkers")) s.UseMarkers = B();
            else if (K("emfwideaspect")) s.EmfWide = B();
            else if (K("legendshift")) p.LegendShift = B();
            else if (K("showsymbols")) p.Symbols = B();
            else if (K("symbolsize")) { if (setting != null && Math.Abs(Crt.Atoi(setting)) <= 1) p.SymbolSize = Crt.Atoi(setting); }
            else if (K("testinfo")) p.TestInfo = B();
            else if (K("showversion") || K("showusername") || K("showpageno") || K("showscript")) { } // retired plot texts
            else if (K("captionsource")) { if (setting != null) p.CaptionSource = Crt.Atoi(setting); }
            else if (K("nocaptionbar")) { if (setting != null) s.NoCaptionBar = Crt.Atoi(setting); }
            else if (K("groupby")) s.MultiGroupBy = setting != null ? Crt.Atoi(setting) : GroupBy.Independent;
            else if (K("linesize")) { if (setting != null) p.LineSize = Crt.Atoi(setting); }
            else if (K("3dlinesize")) { if (setting != null) p.LineSize3D = Crt.Atoi(setting); }
            else if (K("symbolfreq")) { if (setting != null) p.SymFreq = unchecked((uint)Crt.Atoi(setting)); }
            else if (K("samplefreq")) { if (setting != null) p.SamFreq = unchecked((uint)Crt.Atoi(setting)); }
            else if (K("showlegend")) p.Legend = B();
            else if (K("legendcolor")) p.LegendColor = B();
            else if (K("reservedlegend")) { if (setting != null) s.ReservedLegend = Crt.Atoi(setting); }
            else if (K("gridoffset")) { if (setting != null) s.MinGridOffset = Math.Abs(Crt.Atoi(setting)) > 90 ? 90 : Math.Abs(Crt.Atoi(setting)); }
            else if (K("legendcolumns")) { if (setting != null) p.LegendColumns = Crt.Atoi(setting) >= 1 ? Crt.Atoi(setting) : 2; }
            else if (K("averagefreq")) { if (setting != null) p.AvgFreq = unchecked((uint)Crt.Atoi(setting)); }
            else if (K("zaxisfreq")) { if (setting != null) p.ZaxFreq = (uint)Math.Max(1, Crt.Atoi(setting)); }
            else if (K("tabsteprate")) { if (setting != null) s.TabStepRate = (float)Crt.Atof(setting); }
            else if (K("timestampmode")) { if (setting != null) s.TimeStampMode = Crt.Atoi(setting); }
            else if (K("averagerate")) { if (setting != null) s.AverageRate = (float)Crt.Atof(setting); }
            else if (K("timestamp")) { } // retired plot time stamp
            else if (K("defaultstarttime")) { if (setting != null) s.TStart = setting; }
            else if (K("defaultstoptime")) { if (setting != null) s.TStop = setting; }
            else if (K("defaultreference")) { if (setting != null) s.TRef = setting; }
            else if (K("editapp")) { if (setting != null) s.EditApp = setting; }
            else if (K("exportapp")) { if (setting != null) s.ExportApp = setting; }
            else if (K("decimalplaces")) { if (setting != null) s.DataPrecision = ClampPrecision(Crt.Atoi(setting)); }
            else if (K("timeprecision")) { if (setting != null) s.TimePrecision = ClampPrecision(Crt.Atoi(setting)); }
            else if (K("sharedtabtime")) { if (setting != null) s.SharedTabTime = B(); }
            else if (K("includeutcs")) { if (setting != null) s.ShowUtc = B(); }
            else if (K("monitors")) { if (setting != null) s.Monitors = Math.Abs(Crt.Atoi(setting)) <= 10 ? Math.Abs(Crt.Atoi(setting)) : s.Monitors; }
            else if (K("optimizeddraw")) { if (setting != null) s.OptimizedDraw = Math.Abs(Crt.Atoi(setting)); }
            else if (K("offscreendraw")) { if (setting != null) s.OffscreenDraw = Math.Abs(Crt.Atoi(setting)); }
            else if (K("autosave")) s.AutoSave = B();
            else if (K("maxpresentation")) s.MaxPresentation = B();
            else if (K("useplaceholder")) s.UsePlaceholder = B();
            else if (K("rebuildrate")) s.RebuildRate = B();
            else if (K("tabhistory")) s.TabHistory = B();
            else if (K("defaulttabtime")) p.DefTabTime = B();
            else if (K("exitsafety")) s.ExitSafety = B();
            else if (K("statusbar")) s.ShowStatusBar = B();
            else if (K("appearance")) s.DarkMode = setting != null && setting.TrimStart().StartsWith("d", StringComparison.OrdinalIgnoreCase);
            else if (K("filespec")) { if (setting != null) s.FileSpec = setting; }
            else if (K("scriptdir")) { if (setting != null) s.ScriptDir = setting; }
            else if (K("wpdpath")) { if (setting != null) s.WpdFolder = setting; }
            else if (K("exportdir")) { if (setting != null) s.ExportDir = setting; }
            else if (K("querydir")) { if (setting != null) s.QueryDir = setting; }
            else if (K("plotmax")) s.PlotMax = B();
            else if (K("xautolabel")) s.AllowAutoLabel = B();
            else if (K("applyall")) s.ApplyAll = B();
            else if (K("extnomen")) s.ExtNomen = B();
            else if (K("altnames")) s.AltNames = B();
            else if (K("gridlines")) p.GridLines = B();
            else if (K("scriptprompt")) s.ScriptPrompt = B();
            else if (K("embeddedscript")) s.EmbeddedScript = B();
            else if (K("pausemode")) s.PauseMode = setting != null && setting[0] == '1' ? PauseMode.NextCommand : PauseMode.NextPlot;
            else if (K("colorprinter")) s.ColorPrinter = B();
            else if (K("landscape")) s.Landscape = B();
            else if (K("alignclosest")) s.AlignClosest = B();
            else if (K("calceveryinstance")) s.CalcEveryInstance = B();
            else if (K("autocompact"))
            {
                var tok = new Strtok(setting ?? "");
                string? t = tok.Next(" ,");
                if (t != null)
                {
                    s.CompactPoints = unchecked((uint)Crt.Atol(t));
                    if ((t = tok.Next(" ")) != null) s.CompactInterval = (float)Crt.Atof(t);
                }
            }
            else if (K("autodiscard")) s.DiscardSeconds = unchecked((uint)Crt.Atol(setting));
            else if (K("autorefresh")) s.PreviousRefresh = Crt.Atoi(setting);
            else if (K("loadsymbols"))
            {
                loadSymbols = B();
                s.UserVars.Clear();
            }
            else if (K("firstoccurrence")) s.FirstOccurrence = B();
            else if (K("extrapolate")) s.Extrapolate = B();
            else if (K("showsystemload")) s.ShowSysLoad = B();
            else if (K("showcursorcoords")) s.ShowCoord = B();
            else if (K("symbolic"))
            {
                if ((loadSymbols || s.LoadSymbols) && setting != null) s.UserVars.Add(setting.Replace(',', '\t'));
            }

            // second lookup chain
            if (K("userbutton"))
            {
                // UserButtonN lines are written for reference only; the registry holds the user buttons
                // (they were applied from the INI only with the retired IncludeToolBar option).
            }
            else if (K("logictxtindex")) s.LogicTxtIdx = Crt.Atoi(setting);
            else if (K("logictxtlabel") && setting != null && Crt.Atoi(setting) >= 0 && Crt.Atoi(setting) <= AppSettings.NLogicLabels - 1)
            {
                var tok = new Strtok(setting);
                string? i = tok.Next(","), tt, ft;
                if (i != null && (tt = tok.Next(",")) != null && (ft = tok.Next(",")) != null)
                {
                    int pos = Crt.Atoi(i);
                    s.LogicTxtTrue[pos] = tt;
                    s.LogicTxtFalse[pos] = ft;
                }
            }
            else if (K("markerstopmost")) p.MarkersTopmost = Crt.Atoi(setting) != 0;
            else if (K("showfileevents")) p.ShowFileEvents = Crt.Atoi(setting) != 0;
            else if (K("autoscalex")) p.AutoScaleX = Crt.Atoi(setting) != 0;
            else if (K("autoscaley")) p.AutoScaleY = Crt.Atoi(setting) != 0;
            else if (K("use64bittime")) p.Use64BitTime = Crt.Atoi(setting) != 0;
        }

        ApplyLegacyFontSize(p, legacyFontSize);
        if ((s.LoadSymbols = loadSymbols)) SortSymbols(s.UserVars);
        return new(true);
    }

    private static int ClampPrecision(int v) => v <= 12 ? (v >= -1 ? v : -1) : 12;

    private static void ReadInts(int[] dst, string? setting)
    {
        var tok = new Strtok(setting ?? "");
        string? t = tok.Next(" ");
        for (int j = 0; j < Nomen.ITEMS; j++)
        {
            if (t == null) break;
            dst[j] = Crt.Atoi(t);
            t = tok.Next(" ");
        }
    }

    /// <summary>Label point sizes (tenths) that reproduce the retired screen scaling factors 1..4.</summary>
    private static readonly int[] LegacyLabelSizes = { 83, 100, 125, 167 };

    /// <summary>
    /// INI files written before the font dialog chose point sizes have a <c>FontSize</c> key (screen scaling
    /// factor 1..4) and point sizes that were never used for drawing. Once, while loading such a file, the
    /// label size is set to the size the factor produced and the title size keeps the old title/label height
    /// ratio, so existing settings look the same. <c>FontSize</c> is no longer written.
    /// </summary>
    private static void ApplyLegacyFontSize(PlotConfig p, int fontSize)
    {
        if (fontSize < 1 || fontSize > 4) return;
        p.LabelPointSize = LegacyLabelSizes[fontSize - 1];
        int lh = p.LabelFont.Height, th = p.TitleFont.Height;
        double ratio = lh != 0 && th != 0 && Math.Sign(lh) == Math.Sign(th) ? (double)th / lh : 1.5;
        p.TitlePointSize = (int)Math.Round(p.LabelPointSize * Math.Clamp(ratio, 0.5, 4.0));
    }

    private static int ReadFont(LogFont lf, string setting, int pointSize)
    {
        var tok = new Strtok(setting);
        string? t;
        if ((t = tok.Next(" ")) != null)
        {
            int v = Crt.Atol(t);
            if (v > 0)
            {
                pointSize = v;
                if ((t = tok.Next(" ")) != null) lf.Height = Crt.Atol(t);
            }
            else lf.Height = v;
        }
        if ((t = tok.Next(" ")) != null) lf.Width = Crt.Atol(t);
        if ((t = tok.Next(" ")) != null) lf.Escapement = Crt.Atol(t);
        if ((t = tok.Next(" ")) != null) lf.Orientation = Crt.Atol(t);
        if ((t = tok.Next(" ")) != null) lf.Weight = Crt.Atol(t);
        if ((t = tok.Next(" ")) != null) lf.Italic = (byte)Crt.Atoi(t);
        if ((t = tok.Next(" ")) != null) lf.Underline = (byte)Crt.Atoi(t);
        if ((t = tok.Next(" ")) != null) lf.StrikeOut = (byte)Crt.Atoi(t);
        if ((t = tok.Next(" ")) != null) lf.CharSet = (byte)Crt.Atoi(t);
        if ((t = tok.Next(" ")) != null) lf.OutPrecision = (byte)Crt.Atoi(t);
        if ((t = tok.Next(" ")) != null) lf.ClipPrecision = (byte)Crt.Atoi(t);
        if ((t = tok.Next(" ")) != null) lf.Quality = (byte)Crt.Atoi(t);
        if ((t = tok.Next(" ")) != null) lf.PitchAndFamily = (byte)Crt.Atoi(t);
        if ((t = tok.Next("")) != null) lf.FaceName = t.Length > 31 ? t[..31] : t;
        return pointSize;
    }

    /// <summary>Descending byte order of the "NAME\tvalue" strings.</summary>
    public static void SortSymbols(List<string> vars) => vars.Sort((a, b) => string.CompareOrdinal(b, a));

    /// <summary>Saved settings file layout.</summary>
    public static string Format(AppSettings s, IniContext ctx)
    {
        var o = new StringBuilder();
        void W(string fmt, params object?[] a) => o.Append(Crt.Sprintf(fmt, a).Replace("\n", "\r\n"));
        var p = s.Plot;
        DateTime now = ctx.Now ?? DateTime.Now;

        W(";*** Multiplot Initialization File ***\n\n");
        string dt = now.Hour > 11
            ? Crt.Sprintf("%d:%02dPM", now.Hour == 12 ? 12 : now.Hour - 12, now.Minute)
            : Crt.Sprintf("%d:%02dAM", now.Hour != 0 ? now.Hour : 12, now.Minute);
        dt += "  " + now.ToString("MMMM dd, yyyy", System.Globalization.CultureInfo.InvariantCulture);
        W(";Saved at %s\n\n", dt);

        W("[version]\n");
        W(";%s\n", ctx.AppVersion);
        W(";%s\n", ctx.AppDate);
        foreach (var v in ctx.LibraryVersions) W(";dll = %s\n", v);
        W("\n");

        W("[workstation]\n");
        var ver = Environment.OSVersion.Version;
        W(";OS =  %s %d.%d.%d %s\n", Environment.GetEnvironmentVariable("OS") ?? "(null)", ver.Major, ver.Minor, ver.Build, Environment.OSVersion.ServicePack);
        W(";memory installed = %lld gigabytes\n", InstalledMemoryKb() / 1024 / 1024);
        W(";owner = %s\n", Environment.UserName);
        W("\n");

        W("[interface]\n");
        W("StatusBar = %d\n", s.ShowStatusBar ? 1 : 0);
        W("Appearance = %s\n", s.DarkMode ? "Dark" : "Light");
        for (int j = 0; j < AppSettings.NUserButtons; j++)
            W("UserButton%d = %s,%s\n", j, s.UserButtons[j].Label, s.UserButtons[j].Command);
        W("\n");

        W("[symbolics]\n");
        W("LoadSymbols = %d\n", s.LoadSymbols ? 1 : 0);
        foreach (var v in s.UserVars) W("symbolic = %s\n", v.Replace('\t', ','));
        W("\n");

        W("[options]\n");
        W("FileFilters = %s\n", s.FileFilters);
        W("AutoSave = %d\n", s.AutoSave ? 1 : 0);
        W("Monitors = %d\n", s.Monitors);
        W("AlignClosest = %d\n", s.AlignClosest ? 1 : 0);
        W("CalcEveryInstance = %d\n", s.CalcEveryInstance ? 1 : 0);
        W("OptimizedDraw = %d\n", s.OptimizedDraw);
        W("OffscreenDraw = %d\n", s.OffscreenDraw);
        W("UsePlaceholder = %d\n", s.UsePlaceholder ? 1 : 0);
        W("ExitSafety = %d\n", s.ExitSafety ? 1 : 0);
        W("RebuildRate = %d\n", s.RebuildRate ? 1 : 0);
        W("TabHistory = %d\n", s.TabHistory ? 1 : 0);
        W("DefaultTabTime = %d\n", p.DefTabTime ? 1 : 0);
        W("MaxPresentation = %d\n", s.MaxPresentation ? 1 : 0);
        W("FirstOccurrence = %d\n", s.FirstOccurrence ? 1 : 0);
        W("Extrapolate = %d\n", s.Extrapolate ? 1 : 0);
        W("ShowSystemLoad = %d\n", s.ShowSysLoad ? 1 : 0);
        W("ShowCursorCoords = %d\n", s.ShowCoord ? 1 : 0);
        W("Use64bitTime = %d\n", p.Use64BitTime ? 1 : 0);
        W("\n");

        W("[paths]\n");
        W("FileSpec = %s\n", s.FileSpec);
        W("DBPATH = %s\n", s.DbFolder);
        W("ScriptDir = %s\n", s.ScriptDir);
        W("WPDpath = %s\n", s.WpdFolder);
        W("ExportDir = %s\n", s.ExportDir);
        W("QueryDir = %s\n", s.QueryDir);
        W("\n");

        W("[realtime]\n");
        W("IgnoreStatic = %d\n", s.IgnoreStatic ? 1 : 0);
        W("AutoCompact = %d,%g\n", (int)s.CompactPoints, (double)s.CompactInterval);
        W("AutoDiscard = %d\n", (int)s.DiscardSeconds);
        W("AutoRefresh = %d\n", ctx.AutoRefresh != 0 ? ctx.AutoRefresh : s.PreviousRefresh);
        W("DropoutMarker = %d\n", p.DropoutMarker ? 1 : 0);
        W("DropoutIgnore = %d\n", p.DropoutIgnore ? 1 : 0);
        W("PcgCacheSize = %d\n", s.PcgCollectorSize);
        W("\n");

        W("[printer]\n");
        W("PrinterName = %s\n", s.UserPrinter);
        W("PrinterMargin = %f\n", (double)s.MarginSize);
        W("ColorPrinter = %d\n", s.ColorPrinter ? 1 : 0);
        W("Landscape = %d\n", s.Landscape ? 1 : 0);
        W("TextPrinter = %s\n", s.TextPrinter);
        W("\n");

        W("[access time]\n");
        W("DefaultStartTime = %s\n", s.TStart);
        W("DefaultStopTime = %s\n", s.TStop);
        if (string.Equals(s.TRef, "start", StringComparison.OrdinalIgnoreCase)) W("DefaultReference = %s\n", s.TRef);
        else W("DefaultReference = NONE\n");
        W("\n");

        W("[functions]\n");
        W("AverageRate = %.3f\n", (double)s.AverageRate);
        W("TimeStampMode = %d\n", s.TimeStampMode);
        W("\n");

        W("[mouse]\n");
        W("RBfunction = %d\n", s.RbFunction);
        W("PopupsGlobal = %d\n", s.PopupsGlobal ? 1 : 0);
        W("\n");

        W("[external applications]\n");
        W("ExportApp = %s\n", s.ExportApp);
        W("EditApp = %s\n", s.EditApp);
        W("DecimalPlaces = %d\n", s.DataPrecision);
        W("TimePrecision = %d\n", s.TimePrecision);
        W("SharedTabTime = %d\n", s.SharedTabTime ? 1 : 0);
        W("IncludeUTCs = %d\n", s.ShowUtc ? 1 : 0);
        W("\n");

        W("[scripting]\n");
        W("ScriptPrompt = %d\n", s.ScriptPrompt ? 1 : 0);
        W("PauseMode = %d\n", s.PauseMode);
        W("EmbeddedScript = %d\n", s.EmbeddedScript ? 1 : 0);
        W("\n");

        W("[fonts]\n");
        WriteFont(W, "TitleFont", p.TitlePointSize, p.TitleFont);
        WriteFont(W, "LabelFont", p.LabelPointSize, p.LabelFont);
        W("\n");

        W("[plot style]\n");
        W("GroupBy = %d\n", s.MultiGroupBy);
        W("ExtNomen = %d\n", s.ExtNomen ? 1 : 0);
        W("AltNames = %d\n", s.AltNames ? 1 : 0);
        W("SymbolFreq = %d\n", (int)p.SymFreq);
        W("SampleFreq = %d\n", (int)p.SamFreq);
        W("AverageFreq = %d\n", (int)p.AvgFreq);
        W("ZaxisFreq = %d\n", (int)p.ZaxFreq);
        W("TabStepRate = %g\n", (double)s.TabStepRate);
        W("Xtitle = %s\n", s.XTitle);
        W("Ytitle = %s\n", s.YTitle);
        W("Xnomen = %d\n", p.XNomen);
        W("Ynomen = %d\n", p.YNomen);
        W("Xudef = %s\n", s.XUdef);
        W("Yudef = %s\n", s.YUdef);
        W("Xautolabel = %d\n", s.AllowAutoLabel ? 1 : 0);
        W("UseTitle = %d\n", s.ContinueTitle ? 1 : 0);
        W("UseSubTitle = %d\n", s.ContinueSubTitle ? 1 : 0);
        W("UseMarkers = %d\n", s.UseMarkers ? 1 : 0);
        W("MarkersTopmost = %d\n", p.MarkersTopmost ? 1 : 0);
        W("ShowFileEvents = %d\n", p.ShowFileEvents ? 1 : 0);
        W("AutoScaleX = %d\n", p.AutoScaleX ? 1 : 0);
        W("AutoScaleY = %d\n", p.AutoScaleY ? 1 : 0);
        W("LegendColumns = %d\n", p.LegendColumns);
        W("ReservedLegend = %d\n", s.ReservedLegend);
        W("GridOffset = %d\n", s.MinGridOffset);
        W("ShowLegend = %d\n", p.Legend ? 1 : 0);
        W("LegendColor = %d\n", p.LegendColor ? 1 : 0);
        W("ShowSymbols = %d\n", p.Symbols ? 1 : 0);
        W("SymbolSize = %d\n", p.SymbolSize);
        W("3DZangle = %d\n", p.ZAngle);
        W("PlotMax = %d\n", s.PlotMax ? 1 : 0);
        W("ApplyAll = %d\n", s.ApplyAll ? 1 : 0);
        W("LineSize = %d\n", p.LineSize);
        W("3DLineSize = %d\n", p.LineSize3D);
        W("MinorTics = %d\n", p.MinorTics ? 1 : 0);
        W("BoldFrame = %d\n", p.BoldFrame ? 1 : 0);
        W("ScaleGroup = %d\n", p.GroupId ? 1 : 0);
        W("StairStep = %d\n", p.StairStep ? 1 : 0);
        W("ClockTime = %d\n", p.ClockTime ? 1 : 0);
        W("UTCformat = %s\n", s.UtcFormat);
        W("GridLines = %d\n", p.GridLines ? 1 : 0);
        W("TestInfo = %d\n", p.TestInfo ? 1 : 0);
        W("LegendShift = %d\n", p.LegendShift ? 1 : 0);
        W("CaptionSource = %d\n", p.CaptionSource);
        W("EMFwideAspect = %d\n", s.EmfWide ? 1 : 0);
        W("NoCaptionBar = %d\n", s.NoCaptionBar);
        W("LogicTxtIndex = %d\n", s.LogicTxtIdx);
        for (int i = 0; i < AppSettings.NLogicLabels; i++)
            W("LogicTxtLabel = %d,%s,%s\n", i, s.LogicTxtTrue[i], s.LogicTxtFalse[i]);
        WriteInts(W, "XNomenLen", p.XNomLen);
        WriteInts(W, "YNomenLen", p.YNomLen);
        WriteInts(W, "XNomenOrder", p.XNomOrder);
        WriteInts(W, "YNomenOrder", p.YNomOrder);
        switch (p.GridStyle)
        {
            case 0: W("GridStyle = solid\n"); break;
            case 2: W("GridStyle = dotted\n"); break;
            case 1: W("GridStyle = dashed\n"); break;
        }
        W("Colors = ");
        for (int i = 0; i < ColorIndex.Total; i++) W("%ld ", p.DrawColor[i]);
        W("\n\n");
        return o.ToString();
    }

    /// <summary>Writes the INI file (Windows-1252, CRLF).</summary>
    public static void Save(AppSettings s, string path, IniContext ctx) => AnsiText.WriteAllText(path, Format(s, ctx));

    private static void WriteFont(Action<string, object?[]> w, string key, int pointSize, LogFont lf) =>
        w(key + " = %d %d %d %d %d %d %d %d %d %d %d %d %d %d %s\n", new object?[]
        {
            pointSize, lf.Height, lf.Width, lf.Escapement, lf.Orientation, lf.Weight, (int)lf.Italic, (int)lf.Underline,
            (int)lf.StrikeOut, (int)lf.CharSet, (int)lf.OutPrecision, (int)lf.ClipPrecision, (int)lf.Quality, (int)lf.PitchAndFamily, lf.FaceName,
        });

    private static void WriteInts(Action<string, object?[]> w, string key, int[] v)
    {
        w(key + " =", Array.Empty<object?>());
        foreach (int x in v) w(" %d", new object?[] { x });
        w("\n", Array.Empty<object?>());
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPhysicallyInstalledSystemMemory(out ulong totalMemoryInKilobytes);

    private static long InstalledMemoryKb()
    {
        try { return GetPhysicallyInstalledSystemMemory(out ulong kb) ? (long)kb : 0; }
        catch (Exception) { return 0; }
    }
}
