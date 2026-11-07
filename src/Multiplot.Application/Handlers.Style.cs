// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

using Multiplot.Core;
using Multiplot.Core.Model;
using Multiplot.Plugins;

namespace Multiplot.Application;

/// <summary>Style and appearance handlers.</summary>
public sealed partial class Session
{
    private static string OnOff(bool v) => v ? "ON" : "OFF";

    /// <summary>
    /// Common pattern of the plot toggles: when <paramref name="value"/> is null (interactive) the new state is the
    /// negation of the active plot's (or default) state and is recorded; the state becomes the default and is applied to
    /// the active plot (all plots with Apply Globally).
    /// </summary>
    private void PlotFlag(bool? value, Func<PlotConfig, bool> get, Action<PlotConfig, bool> set, string record,
        bool recordAlways = false, Action<PlotDocument>? onChange = null, bool? all = null)
    {
        var plot = ActivePlot;
        bool v;
        if (value == null)
        {
            v = !(plot != null ? get(plot.Config) : get(PlotConfig));
            set(PlotConfig, v);
            if (!recordAlways) Record(Crt.Sprintf(record, OnOff(v)));
        }
        else
        {
            v = value.Value;
            set(PlotConfig, v);
        }
        if (recordAlways) Record(Crt.Sprintf(record, OnOff(v)));
        SetRefreshPause(true);
        foreach (var p in TargetPlots(all ?? Settings.ApplyAll))
        {
            if (get(p.Config) != v)
            {
                set(p.Config, v);
                onChange?.Invoke(p);
                Invalidate(p);
            }
        }
        SetRefreshPause(false);
    }

    public void SetClockTime(bool? v) => PlotFlag(v, c => c.ClockTime, (c, x) => c.ClockTime = x, "clocktime %s");
    public void SetMinorTics(bool? v) => PlotFlag(v, c => c.MinorTics, (c, x) => c.MinorTics = x, "minortics %s");
    public void SetBoldFrame(bool? v) => PlotFlag(v, c => c.BoldFrame, (c, x) => c.BoldFrame = x, "boldframe %s");
    public void SetScaleGroup(bool? v) => PlotFlag(v, c => c.GroupId, (c, x) => c.GroupId = x, "SCALEGROUP %s");
    public void SetStairStep(bool? v) => PlotFlag(v, c => c.StairStep, (c, x) => c.StairStep = x, "stairstep %s");
    public void SetTestInfo(bool? v) => PlotFlag(v, c => c.TestInfo, (c, x) => c.TestInfo = x, "testinfo %s");
    public void SetLegendShift(bool? v) => PlotFlag(v, c => c.LegendShift, (c, x) => c.LegendShift = x, "LegendShift %s");
    public void SetLegend(bool? v) => PlotFlag(v, c => c.Legend, (c, x) => c.Legend = x, "legend %s");
    public void SetDropoutMarker(bool? v) => PlotFlag(v, c => c.DropoutMarker, (c, x) => c.DropoutMarker = x, "DropoutMarker %s", true);
    public void SetDropoutIgnore(bool? v) => PlotFlag(v, c => c.DropoutIgnore, (c, x) => c.DropoutIgnore = x, "DropoutIgnore %s", true);
    public void SetDefTabTime(bool? v) => PlotFlag(v, c => c.DefTabTime, (c, x) => c.DefTabTime = x, "DefTabTime %s", true);
    public void SetMarkersTopmost(bool? v) => PlotFlag(v, c => c.MarkersTopmost, (c, x) => c.MarkersTopmost = x, "MARKERSET TOPMOST %s");
    public void SetShowFileEvents(bool? v) => PlotFlag(v, c => c.ShowFileEvents, (c, x) => c.ShowFileEvents = x, "MARKERSET FILEEVENTS %s");
    public void Set64BitTime(bool? v) => PlotFlag(v, c => c.Use64BitTime, (c, x) => c.Use64BitTime = x, "64BITTIME %s");

    /// <summary>Toggles legend colouring.</summary>
    public void SetLegendColor(bool? value)
    {
        var plot = ActivePlot;
        bool v = value ?? !(plot != null ? plot.Config.LegendColor : PlotConfig.LegendColor);
        PlotConfig.LegendColor = v;
        if (value == null) Record("legend " + (v ? "COLOR" : "NOCOLOR"));
        foreach (var p in TargetPlots(Settings.ApplyAll))
            if (p.Config.LegendColor != v) { p.Config.LegendColor = v; Invalidate(p); }
    }

    /// <summary>Also sets every trace's symbol flag.</summary>
    public void SetSymbols(bool? value)
    {
        var plot = ActivePlot;
        bool v = value ?? !(plot != null ? plot.Config.Symbols : PlotConfig.Symbols);
        PlotConfig.Symbols = v;
        if (value == null) Record("symbols " + OnOff(v));
        foreach (var p in TargetPlots(Settings.ApplyAll))
        {
            p.Config.Symbols = v;
            foreach (var c in p.Curves) c.UseSymbols = v;
            Invalidate(p);
        }
    }

    /// <summary>Symbol density (script: density already set).</summary>
    public void SetSymbolDensity(uint? freq)
    {
        bool all = Settings.ApplyAll;
        var plot = ActivePlot;
        uint f;
        if (freq == null)
        {
            var r = Shell.Dialogs.Input(new InputRequest("Number of Symbols to display:", ((plot?.Config.SymFreq) ?? PlotConfig.SymFreq).ToString(System.Globalization.CultureInfo.InvariantCulture),
                "Symbol Density", new[] { "4", "8", "16", "32", "64", "128" }, true, all));
            if (r == null) return;
            all = r.ApplyAll;
            int n = Math.Abs(Crt.Atoi(r.Text));
            f = PlotConfig.SymFreq = n > 0 ? (uint)n : PlotConfig.SymFreq;
            Record(Crt.Sprintf("Symbols %d", (int)f));
        }
        else f = PlotConfig.SymFreq = freq.Value;
        foreach (var p in TargetPlots(all))
            if (p.Config.SymFreq != f) { p.Config.SymFreq = f; Invalidate(p); }
    }

    /// <summary>Symbol size (−1 small, 0 normal, 1 large).</summary>
    public void SetSymbolSize(int size)
    {
        PlotConfig.SymbolSize = size;
        Record("SymbolSize " + (size < 0 ? "SMALL" : size > 0 ? "LARGE" : "NORMAL"));
        foreach (var p in TargetPlots(Settings.ApplyAll))
            if (p.Config.SymbolSize != size) { p.Config.SymbolSize = size; Invalidate(p); }
    }

    /// <summary>Plot style (2D line … tabular).</summary>
    public void SetStyle(int style)
    {
        PlotConfig.Style = style;
        string name = style switch
        {
            PlotStyle.Line2D => "2DLINE",
            PlotStyle.Scatter2D => "2DSCATTER",
            PlotStyle.Line3D => "3DLINE",
            PlotStyle.Area3D => "3DAREA",
            PlotStyle.Logic => "LOGIC",
            _ => "TABULAR",
        };
        Record("style " + name);
        SetRefreshPause(true);
        foreach (var p in TargetPlots(Settings.ApplyAll))
        {
            if (p.Config.Style == style) continue;
            if (style == PlotStyle.Tabular)
            {
                p.TabTime = (float)p.XScale.RMax;
                p.TabDirection = 0;
            }
            else if (p.Config.Style == PlotStyle.Tabular || (p.Config.Style == PlotStyle.Logic && style != PlotStyle.Logic))
                p.ResetMinMax(3, false);
            p.Config.Style = style;
            Invalidate(p);
        }
        SetRefreshPause(false);
    }

    /// <summary>Sets the grid lines.</summary>
    public void SetGridlines(bool show, int style, bool applyAll)
    {
        PlotConfig.GridLines = show;
        if (show) PlotConfig.GridStyle = style;
        if (!show) Record("gridlines OFF");
        else if (style == 0) Record("gridlines SOLID");
        else if (style == 2) Record("gridlines DOTTED");
        else if (style == 1) Record("gridlines DASHED");
        foreach (var p in TargetPlots(applyAll))
        {
            if (p.Config.GridStyle != PlotConfig.GridStyle || p.Config.GridLines != PlotConfig.GridLines)
            {
                p.Config.GridLines = PlotConfig.GridLines;
                if (PlotConfig.GridLines) p.Config.GridStyle = PlotConfig.GridStyle;
                Invalidate(p);
            }
        }
    }

    /// <summary>Grid style dialog.</summary>
    public void GridStyleDialog()
    {
        string[] modes = { "Grid Off", "Grid On", "Solid", "Dot", "Dash" };
        int[] ids = { 0, 1, 0, 2, 1 };
        string current = !PlotConfig.GridLines ? modes[0] : PlotConfig.GridStyle == 0 ? "Solid" : PlotConfig.GridStyle == 2 ? "Dot" : "Dash";
        var r = Shell.Dialogs.Input(new InputRequest("Select grid display mode", current, "Gridlines", modes, true, Settings.ApplyAll, true));
        if (r == null) return;
        int i = Array.IndexOf(modes, r.Text);
        if (i == 0) SetGridlines(false, 0, r.ApplyAll);
        else if (i == 1) SetGridlines(true, PlotConfig.GridStyle, r.ApplyAll);
        else if (i > 1) SetGridlines(true, ids[i], r.ApplyAll);
    }

    /// <summary>Sets the line size (records the previous size).</summary>
    public void SetLineSize(int size, bool applyAll)
    {
        if (size < 0 || size > 3) return;
        Record(Crt.Sprintf("linesize %d", PlotConfig.LineSize));
        PlotConfig.LineSize = size;
        foreach (var p in TargetPlots(applyAll))
        {
            p.Config.LineSize = size;
            foreach (var c in p.Curves) c.LineSize = size;
            Invalidate(p);
        }
    }

    /// <summary>Sets the 3D line size.</summary>
    public void Set3DSize(int size, bool applyAll)
    {
        if (size < 0 || size > 3) return;
        Record(size == 3 ? "linesize 3DTHIN" : size == 2 ? "linesize 3DMEDIUM" : "linesize 3DWIDE");
        PlotConfig.LineSize3D = size;
        foreach (var p in TargetPlots(applyAll))
            if (p.Config.LineSize3D != size) { p.Config.LineSize3D = size; Invalidate(p); }
    }

    /// <summary>Line size dialogs (2D / 3D).</summary>
    public void LineSizeDialog(bool threeD)
    {
        var plot = ActivePlot;
        if (!threeD)
        {
            string cur = ((plot?.Config.LineSize) ?? PlotConfig.LineSize).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var r = Shell.Dialogs.Input(new InputRequest("Select line size", cur, "2D Line Size", new[] { "1", "2", "3" }, true, Settings.ApplyAll, true));
            if (r != null && r.Text is "1" or "2" or "3") SetLineSize(Crt.Atoi(r.Text), r.ApplyAll);
        }
        else
        {
            string[] modes = { "Thin", "Medium", "Wide" };
            int[] sizes = { 3, 2, 1 };
            int cur = (plot?.Config.LineSize3D) ?? PlotConfig.LineSize3D;
            var r = Shell.Dialogs.Input(new InputRequest("Select line size", modes[Math.Max(0, Array.IndexOf(sizes, cur))], "3D Line Size", modes, true, Settings.ApplyAll, true));
            if (r != null && Array.IndexOf(modes, r.Text) is int i && i >= 0) Set3DSize(sizes[i], r.ApplyAll);
        }
    }

    /// <summary>
    /// Sets the label (<paramref name="title"/> false) or title font of the default configuration and of the
    /// active plot (all plots when <paramref name="global"/>): face, size in tenths of a point, bold, italic.
    /// </summary>
    public void SetPlotFont(bool title, string face, int pointSizeTenths, bool bold, bool italic, bool global)
    {
        pointSizeTenths = Math.Clamp(pointSizeTenths, 40, 2000);
        void Apply(PlotConfig cfg)
        {
            var lf = (title ? cfg.TitleFont : cfg.LabelFont).Clone();
            lf.FaceName = face;
            lf.Weight = bold ? 700 : 400;
            lf.Italic = (byte)(italic ? 1 : 0);
            lf.Height = -(int)Math.Round(pointSizeTenths * 96.0 / 720.0);
            if (title) { cfg.TitleFont = lf; cfg.TitlePointSize = pointSizeTenths; }
            else { cfg.LabelFont = lf; cfg.LabelPointSize = pointSizeTenths; }
        }
        Apply(PlotConfig);
        foreach (var p in TargetPlots(global)) { Apply(p.Config); Invalidate(p); }
    }

    /// <summary>Data rate (interactive when <paramref name="mode"/> is null).</summary>
    public void SetDataRate(int? mode)
    {
        bool all = Settings.ApplyAll;
        if (mode == null)
        {
            string[] names = { "Full", "Average", "Sample" };
            int[] ids = { DataRateMode.Full, DataRateMode.Averaged, DataRateMode.Sampled };
            var r = Shell.Dialogs.Input(new InputRequest("Select 'Screen View' data rate", names[Math.Max(0, Array.IndexOf(ids, PlotConfig.DataRate))],
                "Screen View Data Rate", names, true, all, true));
            if (r == null) return;
            int i = Array.IndexOf(names, r.Text);
            if (i >= 0) PlotConfig.DataRate = ids[i];
            all = r.ApplyAll;
            Record(PlotConfig.DataRate == DataRateMode.Full ? "datarate FULL" : PlotConfig.DataRate == DataRateMode.Averaged
                ? Crt.Sprintf("datarate AVERAGE %d", (int)PlotConfig.AvgFreq) : Crt.Sprintf("datarate SAMPLE %d", (int)PlotConfig.SamFreq));
        }
        else PlotConfig.DataRate = mode.Value;
        foreach (var p in TargetPlots(all))
        {
            p.Config.DataRate = PlotConfig.DataRate;
            if (PlotConfig.DataRate == DataRateMode.Averaged) p.Config.AvgFreq = PlotConfig.AvgFreq;
            else if (PlotConfig.DataRate == DataRateMode.Sampled) p.Config.SamFreq = PlotConfig.SamFreq;
            Invalidate(p);
        }
    }

    /// <summary>Data density: 0 = average, 1 = sample, 2 = 3D segments (interactive when <paramref name="value"/> is null).</summary>
    public void SetDensity(int which, uint? value)
    {
        bool all = Settings.ApplyAll;
        uint cur = which == 0 ? PlotConfig.AvgFreq : which == 1 ? PlotConfig.SamFreq : PlotConfig.ZaxFreq;
        uint v;
        if (value == null)
        {
            string prompt = which == 0 ? "Number of averaged points to display:" : which == 1 ? "Number of sampled points to display:" : "Number of 3D Segments to display:";
            string[] list = which == 2 ? new[] { "4", "8", "16", "32", "64", "128" } : new[] { "10", "25", "50", "100", "500", "1000" };
            var r = Shell.Dialogs.Input(new InputRequest(prompt, cur.ToString(System.Globalization.CultureInfo.InvariantCulture), which == 2 ? "3D Segments" : "Display Points", list, true, all));
            if (r == null) return;
            all = r.ApplyAll;
            int n = which == 2 ? Math.Abs(Crt.Atoi(r.Text)) : Crt.Atoi(r.Text);
            v = (uint)Math.Max(1, n);
            Record(which == 0 ? Crt.Sprintf("datarate AVERAGE %d", (int)v) : which == 1 ? Crt.Sprintf("datarate SAMPLE %d", (int)v) : Crt.Sprintf("DATARATE 3D %d", (int)v));
        }
        else v = value.Value;
        if (which == 0) PlotConfig.AvgFreq = v; else if (which == 1) PlotConfig.SamFreq = v; else PlotConfig.ZaxFreq = v;
        foreach (var p in TargetPlots(all))
        {
            if (which == 0) p.Config.AvgFreq = v; else if (which == 1) p.Config.SamFreq = v; else p.Config.ZaxFreq = v;
            Invalidate(p);
        }
    }

    /// <summary>Sets the number of legend columns.</summary>
    public void SetLegendColumns(int? columns)
    {
        bool all = Settings.ApplyAll;
        var plot = ActivePlot;
        int cols;
        if (columns != null) cols = PlotConfig.LegendColumns = columns.Value;
        else
        {
            cols = (plot?.Config.LegendColumns) ?? PlotConfig.LegendColumns;
            var r = Shell.Dialogs.Input(new InputRequest("Number of legend columns:", cols.ToString(System.Globalization.CultureInfo.InvariantCulture), "Legend Columns",
                Enumerable.Range(1, 10).Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray(), true, all));
            if (r == null) return;
            all = r.ApplyAll;
            cols = PlotConfig.LegendColumns = Crt.Atoi(r.Text) >= 1 ? Crt.Atoi(r.Text) : PlotConfig.LegendColumns;
            Record(Crt.Sprintf("LEGEND COLUMNS %d", cols));
        }
        foreach (var p in TargetPlots(all))
            if (p.Config.LegendColumns != cols) { p.Config.LegendColumns = cols; Invalidate(p); }
    }

    /// <summary>Toggles reserved legend space.</summary>
    public void ReserveLegendDialog()
    {
        var r = Shell.Dialogs.Input(new InputRequest("Minimum number of legend rows:", Settings.ReservedLegend.ToString(System.Globalization.CultureInfo.InvariantCulture), "Reserve Legend",
            new[] { "0 (auto)", "2", "4", "6", "8", "10", "12" }));
        if (r == null) return;
        int n = Math.Abs(Crt.Atoi(r.Text));
        if (n <= 32)
        {
            Settings.ReservedLegend = n;
            Record(Crt.Sprintf("legend reserve %d", n));
        }
        else Message("Value out of range");
        RefreshAllPlots();
    }

    /// <summary>Sets the minimum grid offset.</summary>
    public void GridOffsetDialog()
    {
        var r = Shell.Dialogs.Input(new InputRequest("Percent of window for minimum graph left offset:", Settings.MinGridOffset.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "Left Grid Offset", new[] { "0", "10", "20", "30", "40", "50" }));
        if (r == null) return;
        Settings.MinGridOffset = Math.Min(90, Math.Abs(Crt.Atoi(r.Text)));
        Record(Crt.Sprintf("GridOffset %d", Settings.MinGridOffset));
        RefreshAllPlots();
    }

    /// <summary>Redraws every plot.</summary>
    public void RefreshAllPlots()
    {
        foreach (var p in _plots) Invalidate(p);
    }

    /// <summary>Sets a colour: <paramref name="rgb"/> null = colour dialog.</summary>
    public void SetColor(int colorIndex, bool global, int? rgb = null)
    {
        int color;
        if (rgb == null)
        {
            var c = Shell.Dialogs.ChooseColor(PlotConfig.DrawColor[colorIndex < ColorIndex.Total ? colorIndex : colorIndex % ColorIndex.MaxColors], PlotConfig.DrawColor);
            if (c == null) return;
            color = c.Value;
            string name = colorIndex switch
            {
                ColorIndex.Grid => "GRID",
                ColorIndex.Label => "LABEL",
                ColorIndex.Title => "TITLE",
                ColorIndex.Background => "BACKGROUND",
                ColorIndex.Marker => "MARKER",
                ColorIndex.Redline => "REDLINE",
                _ => (colorIndex + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
            };
            Record(Crt.Sprintf("set color %s,%d,%d,%d %s", name, color & 0xff, (color >> 8) & 0xff, (color >> 16) & 0xff, global ? "GLOBAL" : ""));
        }
        else color = rgb.Value;
        RestoreColors[colorIndex] = PlotConfig.DrawColor[colorIndex];
        PlotConfig.DrawColor[colorIndex] = color;
        foreach (var p in TargetPlots(global))
            if (p.Config.DrawColor[colorIndex] != color) { p.Config.DrawColor[colorIndex] = color; Invalidate(p); }
    }

    /// <summary>
    /// Light or dark appearance (FR-UI-046; <paramref name="dark"/> null = toggle): the application theme
    /// and the displayed plot colours. Saved in the INI (<c>Appearance</c>).
    /// </summary>
    public void SetAppearance(bool? dark)
    {
        Settings.DarkMode = dark ?? !Settings.DarkMode;
        Record("APPEARANCE " + (Settings.DarkMode ? "DARK" : "LIGHT"));
        Shell.SetAppearance(Settings.DarkMode);
        RefreshAllPlots();
        Shell.RefreshCommands();
    }

    /// <summary>Line colour dialog loop.</summary>
    public void LineColorDialog()
    {
        while (true)
        {
            var r = Shell.Dialogs.Input(new InputRequest(Crt.Sprintf("Set color [1 .. %d]:", ColorIndex.MaxColors), "1", "Line Color",
                Enumerable.Range(1, ColorIndex.MaxColors).Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray(), true, Settings.ApplyAll));
            if (r == null) return;
            int n = Crt.Atoi(r.Text);
            if (n > 0) SetColor((n - 1) % ColorIndex.MaxColors, r.ApplyAll);
            else Message("Invalid color number.");
        }
    }

    /// <summary>Restores the startup colours.</summary>
    public void RestoreColorTable()
    {
        Array.Copy(RestoreColors, PlotConfig.DrawColor, ColorIndex.Total);
        Record("SET COLOR RESTORE");
        foreach (var p in _plots)
        {
            Array.Copy(PlotConfig.DrawColor, p.Config.DrawColor, ColorIndex.Total);
            Invalidate(p);
        }
    }

    /// <summary>Runs ColorPrinter.wps from the user folder or the installation folder.</summary>
    public async Task LoadColorTableAsync()
    {
        string fn = Path.Combine(Settings.UserFolder, "ColorPrinter.wps");
        if (!File.Exists(fn)) fn = Path.Combine(AppContext.BaseDirectory, "ColorPrinter.wps");
        if (!File.Exists(fn))
        {
            Message("ColorPrinter.wps was not found.");
            return;
        }
        Array.Copy(PlotConfig.DrawColor, RestoreColors, ColorIndex.Total);
        await Scripts.RunNestedAsync(fn);
        Record("SET COLOR PRINTERTABLE");
    }

    /// <summary>UTC format (script: format already converted).</summary>
    public void SetUtcFormat(string? cFormat)
    {
        bool all = Settings.ApplyAll;
        if (cFormat == null)
        {
            string[] f = { "HR:MN:SC.MIL", "HR:MN:SC", "HR:MN", "HR", "DDD/HR:MN:SC.MIL", "DDD/HR:MN:SC", "DDD/HR:MN", "DDD/HR", "DDD" };
            string cur = Core.Plotting.Scale.MakeUserFormat((ActivePlot?.UtcFormat) ?? Settings.UtcFormat);
            var r = Shell.Dialogs.Input(new InputRequest("UTC/Clock display format:", cur, "Time Display Format", f, true, all));
            if (r == null) return;
            all = r.ApplyAll;
            Record("UTCFORMAT " + r.Text);
            Settings.UtcFormat = Core.Plotting.Scale.MakeCFormat(r.Text);
        }
        else Settings.UtcFormat = cFormat;
        foreach (var p in TargetPlots(all))
            if (p.UtcFormat != Settings.UtcFormat) { p.UtcFormat = Settings.UtcFormat; Invalidate(p); }
    }

    /// <summary>Sets the window caption source.</summary>
    public void SetCaptionSource(int caption, bool applyAll)
    {
        PlotConfig.CaptionSource = caption;
        Record(caption switch
        {
            CaptionSource.XTitle => "Caption TITLE",
            CaptionSource.YTitle => "Caption SUBTITLE",
            CaptionSource.PlotNo => "Caption PLOTNO",
            CaptionSource.ParmSet => "Caption PARMSET",
            CaptionSource.User => "Caption USER",
            _ => "Caption SCRIPT",
        });
        foreach (var p in TargetPlots(applyAll))
        {
            p.Config.CaptionSource = caption;
            UpdateCaption(p);
        }
    }

    /// <summary>Recomputes and applies the caption of a plot.</summary>
    public void UpdateCaption(PlotDocument p)
    {
        p.Caption = p.Config.CaptionSource switch
        {
            CaptionSource.XTitle => p.XTitle,
            CaptionSource.YTitle => p.YTitle,
            CaptionSource.PlotNo => Crt.Sprintf("Plot #%d", p.PlotNo),
            CaptionSource.User => p.UserCaption,
            CaptionSource.Script => p.ScriptFile,
            _ => p.ParseStringKeywords("[parameter]", -1),
        };
        Shell.UpdateCaption(p);
    }

    /// <summary>Window caption dialog.</summary>
    public void CaptionSourceDialog()
    {
        string[] names = { "Plot Window Title", "X Title", "Y Title", "Plot Number", "Parameter Set", "Script Name" };
        int[] ids = { CaptionSource.User, CaptionSource.XTitle, CaptionSource.YTitle, CaptionSource.PlotNo, CaptionSource.ParmSet, CaptionSource.Script };
        int cur = (ActivePlot?.Config.CaptionSource) ?? PlotConfig.CaptionSource;
        var r = Shell.Dialogs.Input(new InputRequest("Select plot window caption source", names[Math.Max(0, Array.IndexOf(ids, cur))], "Window Caption", names, true, Settings.ApplyAll, true));
        if (r != null && Array.IndexOf(names, r.Text) is int i && i >= 0) SetCaptionSource(ids[i], r.ApplyAll);
    }

    /// <summary>Sets the plot window title (the user-defined caption; script <c>CAPTION USER text</c>).</summary>
    public void SetUserCaption(string? text)
    {
        var plot = ActivePlot;
        if (text == null)
        {
            var r = Shell.Dialogs.Input(new InputRequest("Plot window title:", plot?.UserCaption ?? UserCaption, "Plot Window Title"));
            if (r == null) return;
            UserCaption = r.Text;
            Record("CAPTION USER " + UserCaption);
        }
        else UserCaption = text;
        if (plot != null)
        {
            plot.UserCaption = UserCaption;
            plot.Config.CaptionSource = CaptionSource.User;
            UpdateCaption(plot);
        }
    }

    /// <summary>Toggles the caption bars of plot windows.</summary>
    public void SetNoCaptionBar(bool? value)
    {
        Settings.NoCaptionBar = (value ?? Settings.NoCaptionBar == 0) ? 1 : 0;
        Record("Caption " + (Settings.NoCaptionBar != 0 ? "OFF" : "ON"));
        foreach (var p in _plots) Shell.UpdateCaption(p);
    }

    /// <summary>Tabular background bitmap (script: <see cref="BackgroundFn"/> already set).</summary>
    public void SetTabBackground(bool interactive)
    {
        var plot = ActivePlot;
        if (plot == null) return;
        string name;
        if (interactive)
        {
            var f = Shell.Dialogs.OpenFile("Select Image Bitmap", "Bitmap Files (*.bmp)|*.bmp|Image files|*.bmp;*.png;*.jpg;*.gif", Settings.WpdFolder);
            if (f == null || f.Length == 0)
            {
                if (plot.BackgroundFile.Length > 0 && Message("Remove current background?", PromptButtons.YesNo, PromptIcon.Question, PromptResult.No) == PromptResult.Yes)
                {
                    plot.BackgroundFile = "";
                    plot.BackgroundImage = null;
                    Invalidate(plot);
                }
                return;
            }
            name = f[0];
            Record("SET BACKGROUND " + name);
        }
        else name = BackgroundFn;
        if (name.Length == 0) { plot.BackgroundFile = ""; plot.BackgroundImage = null; }
        else LoadBackground(plot, name);
        Invalidate(plot);
    }

    /// <summary>Interactive when <paramref name="interactive"/>; script sets the title text first.</summary>
    public void ApplyTitle(bool sub, bool interactive)
    {
        bool all = Settings.ApplyAll;
        bool cont = sub ? Settings.ContinueSubTitle : Settings.ContinueTitle;
        bool changed = !interactive;
        if (interactive)
        {
            var plot0 = ActivePlot;
            string cur = sub ? (plot0?.YTitleFormat ?? Settings.YTitle) : (plot0?.XTitleFormat ?? Settings.XTitle);
            string[] keys = { "(blank)", "[title]", "[comment]", "[serialnum]", "[dbname]", "[teststand]", "[testposition]", "[testtype]", "[testnumber]", "[date]", "[time]",
                "[parameter]", "[pidname]", "[pidunits]", "[piddescription]", "[timestart]", "[timestop]", "[timeref]", "[timeslice]", "[filename]", "[scriptname]", "[groupname]" };
            var r = Shell.Dialogs.Input(new InputRequest(sub ? "Y axis sub-title text (blank to remove):" : "Plot title text (blank to remove):", cur, sub ? "Plot Sub-title" : "Plot Title", keys, true, all));
            if (r == null) return;
            all = r.ApplyAll;
            changed = true;
            if (r.Text.Length > 0 && !r.Text.Equals("(blank)", StringComparison.OrdinalIgnoreCase))
            {
                if (sub) Settings.YTitle = r.Text; else Settings.XTitle = r.Text;
                cont = true;
                Record((sub ? "ytitle " : "xtitle ") + r.Text);
            }
            else
            {
                cont = false;
                Record(sub ? "ytitle" : "xtitle");
            }
            if (all) { if (sub) Settings.ContinueSubTitle = cont; else Settings.ContinueTitle = cont; }
        }
        if (!changed) return;
        foreach (var p in TargetPlots(all))
        {
            string title = sub ? Settings.YTitle : Settings.XTitle;
            if (sub)
            {
                p.UseSubTitle = title.Length != 0 && cont;
                p.YTitleFormat = title;
                p.YTitle = p.ParseStringKeywords(title, -1);
                if (PlotConfig.CaptionSource == CaptionSource.YTitle) UpdateCaption(p);
            }
            else
            {
                p.UseTitle = title.Length != 0 && cont;
                p.XTitleFormat = title;
                p.XTitle = p.ParseStringKeywords(title, -1);
                if (PlotConfig.CaptionSource == CaptionSource.XTitle) UpdateCaption(p);
            }
            Invalidate(p);
        }
    }

    /// <summary>
    /// X/Y nomenclature script path: applies <see cref="SetNomen"/> (or UDEF text when
    /// <see cref="SetNomen"/> &lt; 0) to the active plot (all with Apply Globally).
    /// </summary>
    public void ApplyNomen(bool yAxis)
    {
        bool all = Settings.ApplyAll;
        var plot = ActivePlot;
        string uDef;
        int nomen;
        if (SetNomen < 0)
        {
            uDef = UDefBuf;
            nomen = plot != null ? (yAxis ? plot.Config.YNomen : plot.Config.XNomen) : (yAxis ? PlotConfig.YNomen : PlotConfig.XNomen);
        }
        else
        {
            nomen = SetNomen;
            uDef = UDefBuf;
        }
        if (yAxis) PlotConfig.YNomen = nomen; else PlotConfig.XNomen = nomen;
        if (plot == null || (all && (nomen & Nomen.UDEF) != 0)) { if (yAxis) Settings.YUdef = uDef; else Settings.XUdef = uDef; }
        foreach (var p in TargetPlots(all))
        {
            if (yAxis)
            {
                p.Config.YNomen = nomen;
                Array.Copy(PlotConfig.YNomLen, p.Config.YNomLen, Nomen.ITEMS);
                Array.Copy(PlotConfig.YNomOrder, p.Config.YNomOrder, Nomen.ITEMS);
                p.YUdef = uDef;
            }
            else
            {
                p.Config.XNomen = nomen;
                Array.Copy(PlotConfig.XNomLen, p.Config.XNomLen, Nomen.ITEMS);
                Array.Copy(PlotConfig.XNomOrder, p.Config.XNomOrder, Nomen.ITEMS);
                p.XUdef = uDef;
            }
            Invalidate(p);
        }
    }

    /// <summary>Recording text of the nomenclature dialogs.</summary>
    public static string NomenCommand(bool yAxis, int nomen, int[] order, int[] len)
    {
        var sb = new System.Text.StringBuilder(yAxis ? "YNOMEN" : "XNOMEN");
        foreach (int o in order)
        {
            if ((nomen & o) == 0) continue;
            string? name = o switch
            {
                Nomen.NAME => "NAME",
                Nomen.ALTNAME => "ALTNAME",
                Nomen.EXTNAME => "EXTNAME",
                Nomen.UNITS => "UNITS",
                Nomen.DESC => "DESC",
                Nomen.REFT => "REFT",
                Nomen.FILENAME => "FILENAME",
                Nomen.DBNAME => "DBNAME",
                Nomen.UDEF => "UDEF",
                Nomen.SEQNUM when yAxis => "SEQNUM",
                Nomen.CURVAL when yAxis => "CURRENTVALUE",
                Nomen.CURTIME when yAxis => "CURRENTTIME",
                _ => null,
            };
            if (name != null) sb.Append(" + ").Append(name).Append(':').Append(len[Nomen.IndexOf(o)]);
        }
        if ((nomen & Nomen.EFORMAT) != 0) sb.Append(" + EFORMAT");
        return sb.ToString();
    }

    /// <summary>Applies the result of the Nomenclature dialog (interactive path of OnXNomen/OnYNomen).</summary>
    public void ApplyNomenDialog(bool yAxis, int nomen, int[] order, int[] len, string uDef, bool applyAll, int logicIdx)
    {
        var plot = ActivePlot;
        bool newIdx = yAxis && logicIdx != Settings.LogicTxtIdx;
        if (newIdx)
        {
            Settings.LogicTxtIdx = logicIdx;
            Record(Crt.Sprintf("SET LOGIC INDEX %d", logicIdx + 1));
        }
        Record(NomenCommand(yAxis, nomen, order, len));
        if ((nomen & Nomen.UDEF) != 0) Record((yAxis ? "YUDEF " : "XUDEF ") + uDef);
        var cfgLen = yAxis ? PlotConfig.YNomLen : PlotConfig.XNomLen;
        var cfgOrd = yAxis ? PlotConfig.YNomOrder : PlotConfig.XNomOrder;
        Array.Copy(len, cfgLen, Nomen.ITEMS);
        Array.Copy(order, cfgOrd, Nomen.ITEMS);
        if (yAxis) PlotConfig.YNomen = nomen; else PlotConfig.XNomen = nomen;
        if (plot == null || (applyAll && (nomen & Nomen.UDEF) != 0)) { if (yAxis) Settings.YUdef = uDef; else Settings.XUdef = uDef; }
        foreach (var p in TargetPlots(applyAll))
        {
            if (yAxis)
            {
                p.Config.YNomen = nomen;
                Array.Copy(cfgLen, p.Config.YNomLen, Nomen.ITEMS);
                Array.Copy(cfgOrd, p.Config.YNomOrder, Nomen.ITEMS);
                if ((nomen & Nomen.UDEF) != 0) p.YUdef = uDef;
                if (newIdx) foreach (var c in p.Curves) c.LogicTxt = Settings.LogicTxtIdx;
            }
            else
            {
                p.Config.XNomen = nomen;
                Array.Copy(cfgLen, p.Config.XNomLen, Nomen.ITEMS);
                Array.Copy(cfgOrd, p.Config.XNomOrder, Nomen.ITEMS);
                if ((nomen & Nomen.UDEF) != 0) p.XUdef = uDef;
            }
            Invalidate(p);
        }
    }

    /// <summary>Applies the export/time precision after the precision changed.</summary>
    public void ApplyDataPrecision(bool recordIt)
    {
        foreach (var p in TargetPlots(Settings.ApplyAll))
        {
            foreach (var c in p.Curves) c.TabFormat = Settings.DataPrecision;
            Invalidate(p);
        }
        if (recordIt) RecordPrecision();
    }

    /// <summary>Records <c>SET PRECISION t d</c>.</summary>
    public void RecordPrecision() =>
        Record(Crt.Sprintf("SET PRECISION %s %s", TabFormat.NameOf(Settings.TimePrecision), TabFormat.NameOf(Settings.DataPrecision)));

    /// <summary>Decimal places dialog; returns the new value or null.</summary>
    public int? PrecisionDialog(int current)
    {
        var r = Shell.Dialogs.Input(new InputRequest("Output Format [HEX, OCT or DEC prec.]:", TabFormat.NameOf(current), "Tabular Format", TabFormat.Names, false, false, true));
        if (r == null) return null;
        foreach (var n in new[] { "HEX2", "HEX4", "HEX8", "OCT3", "OCT6", "OCT12", "GENERAL" })
            if (r.Text.Contains(n)) return TabFormat.Parse(n);
        return Crt.Atoi(r.Text);
    }

    /// <summary>Toggles the system load display.</summary>
    public void SetSystemLoad(bool? v)
    {
        Settings.ShowSysLoad = v ?? !Settings.ShowSysLoad;
        Record("SYSTEMLOAD " + OnOff(Settings.ShowSysLoad));
        Shell.RefreshCommands();
    }

    /// <summary>Toggles stream certification.</summary>
    public void SetStreamCert(bool? v)
    {
        Settings.StreamCert = v ?? !Settings.StreamCert;
        RefreshAllPlots();
        Record("STREAMCERT " + OnOff(Settings.StreamCert));
    }

    /// <summary>Toggles tabular history.</summary>
    public void SetTabHistory(bool? v)
    {
        Settings.TabHistory = v ?? !Settings.TabHistory;
        Record("TABHISTORY " + OnOff(Settings.TabHistory));
    }

    /// <summary>Toggles Ignore Static.</summary>
    public void SetIgnoreStatic(bool? value)
    {
        var plot = ActivePlot;
        Settings.IgnoreStatic = value ?? !(plot?.IgnoreStatic ?? Settings.IgnoreStatic);
        Record("IgnoreStatic " + OnOff(Settings.IgnoreStatic));
        foreach (var p in TargetPlots(Settings.ApplyAll))
        {
            if (p.IgnoreStatic == Settings.IgnoreStatic) continue;
            p.IgnoreStatic = Settings.IgnoreStatic;
            p.ResetMinMax(1, false);
            Invalidate(p);
        }
    }

    /// <summary>Toggles Rebuild Rate.</summary>
    public void ToggleRebuildRate()
    {
        Settings.RebuildRate = !Settings.RebuildRate;
        Data.SetOption(Settings.RebuildRate ? 1001 : 1002);
        Record("DATARATE REBUILD " + OnOff(Settings.RebuildRate));
    }

    /// <summary>Toggles Extrapolate.</summary>
    public void ToggleExtrapolate()
    {
        Settings.Extrapolate = !Settings.Extrapolate;
        Data.SetOption(Settings.Extrapolate ? 1007 : 1008);
        Record("extrapolate " + OnOff(Settings.Extrapolate));
    }

    /// <summary>Toggles the script prompt.</summary>
    public void SetScriptPrompt(bool? value)
    {
        if (value != null) Settings.ScriptPrompt = value.Value;
        else if (IsInteractive) Settings.ScriptPrompt = !Settings.ScriptPrompt;
        Record("scriptprompt " + OnOff(Settings.ScriptPrompt));
        Data.SetOption(Settings.ScriptPrompt ? 1005 : 1006);
    }

    /// <summary>Sets the script pause mode.</summary>
    public void SetPauseMode(int? mode)
    {
        if (mode != null) Settings.PauseMode = mode.Value;
        else if (IsInteractive) Settings.PauseMode = Settings.PauseMode == PauseMode.NextCommand ? PauseMode.NextPlot : PauseMode.NextCommand;
        Record("pausemode " + (Settings.PauseMode == PauseMode.NextPlot ? "NEXTPLOT" : "NEXTCOMMAND"));
    }

    /// <summary>Toggles Apply Globally.</summary>
    public void SetApplyAll(bool? value)
    {
        if (value != null) Settings.ApplyAll = value.Value;
        else if (IsInteractive)
        {
            Settings.ApplyAll = !Settings.ApplyAll;
            Record("applyglobal " + OnOff(Settings.ApplyAll));
        }
        Shell.RefreshCommands();
    }
}
