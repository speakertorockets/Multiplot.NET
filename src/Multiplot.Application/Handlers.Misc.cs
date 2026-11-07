// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

using Multiplot.Core;
using Multiplot.Core.Model;
using Multiplot.ImportExport;
using Multiplot.Plugins;
using Multiplot.Rendering;
using Multiplot.Scripting;
using Multiplot.Settings;

namespace Multiplot.Application;

/// <summary>Markers, windows, import/export, settings and plugin handlers.</summary>
public sealed partial class Session
{
    // ------------------------------------------------------------------ event markers (FR-MRK, FR-LOAD-060/061)

    /// <summary>
    /// MARKER command / Add Event Marker: adds the marker to the master set and to the active plot (all plots
    /// with Apply Changes Globally). An identical marker is not added twice.
    /// </summary>
    public void AddMarker(Marker marker)
    {
        SetRefreshPause(true);
        foreach (var p in TargetPlots(Settings.ApplyAll))
            if (Marker.AddTo(p.Markers, marker)) Invalidate(p);
        SetRefreshPause(false);
        Marker.AddTo(MasterMarkers, marker);
    }

    /// <summary>
    /// Replaces the marker list of the active plot (all plots with Apply Changes Globally) and the master set
    /// (Markers → Edit). Recorded as <c>clear markers all</c> followed by one <c>marker</c> line per marker.
    /// </summary>
    public void ReplaceMarkers(IReadOnlyList<Marker> markers)
    {
        SetRefreshPause(true);
        foreach (var p in TargetPlots(Settings.ApplyAll))
        {
            p.Markers = Marker.CopySet(markers);
            Invalidate(p);
        }
        SetRefreshPause(false);
        MasterMarkers.Clear();
        MasterMarkers.AddRange(Marker.CopySet(markers));
        Record("clear markers all");
        foreach (var m in markers) Record(MarkerCommand(m));
    }

    /// <summary>Clears all markers (master set and the active plot, or all plots with Apply Changes Globally).</summary>
    public void ClearAllMarkers()
    {
        MasterMarkers.Clear();
        foreach (var p in TargetPlots(Settings.ApplyAll))
        {
            if (p.Markers.Count == 0) continue;
            p.Markers.Clear();
            Invalidate(p);
        }
        Record("clear markers all");
    }

    /// <summary>Markers → Clear All: clears the event markers and the redline markers.</summary>
    public void ClearAllMarkersAndRedlines()
    {
        ClearAllMarkers();
        ClearAllRedlines();
    }

    /// <summary>CLEAR MARKERS n[,m…]: removes markers by 1-based position from the active plot and the master set.</summary>
    public void ClearMarkers(IEnumerable<int> numbers)
    {
        var plot = ActivePlot;
        foreach (int m in numbers.Distinct().OrderByDescending(x => x))
        {
            if (m >= 1 && m <= MasterMarkers.Count) MasterMarkers.RemoveAt(m - 1);
            if (plot != null && m >= 1 && m <= plot.Markers.Count) plot.Markers.RemoveAt(m - 1);
        }
        if (plot != null) Invalidate(plot);
    }

    // ------------------------------------------------------------------ redline markers (FR-MRK-010)

    /// <summary>
    /// REDLINE command / Add Redline Marker: adds the redline to the master set and to the active plot (all
    /// plots with Apply Changes Globally). An identical redline is not added twice.
    /// </summary>
    public void AddRedline(RedlineMarker redline)
    {
        SetRefreshPause(true);
        foreach (var p in TargetPlots(Settings.ApplyAll))
            if (RedlineMarker.AddTo(p.Redlines, redline)) Invalidate(p);
        SetRefreshPause(false);
        RedlineMarker.AddTo(MasterRedlines, redline);
    }

    /// <summary>
    /// Replaces the redline list of the active plot (all plots with Apply Changes Globally) and the master set
    /// (Markers → Edit Redlines). Recorded as <c>clear redlines all</c> followed by one <c>redline</c> line each.
    /// </summary>
    public void ReplaceRedlines(IReadOnlyList<RedlineMarker> redlines)
    {
        SetRefreshPause(true);
        foreach (var p in TargetPlots(Settings.ApplyAll))
        {
            p.Redlines = RedlineMarker.CopySet(redlines);
            Invalidate(p);
        }
        SetRefreshPause(false);
        MasterRedlines.Clear();
        MasterRedlines.AddRange(RedlineMarker.CopySet(redlines));
        Record("clear redlines all");
        foreach (var r in redlines) Record(r.ToCommand());
    }

    /// <summary>Clears all redlines (master set and the active plot, or all plots with Apply Changes Globally).</summary>
    public void ClearAllRedlines()
    {
        MasterRedlines.Clear();
        foreach (var p in TargetPlots(Settings.ApplyAll))
        {
            if (p.Redlines.Count == 0) continue;
            p.Redlines.Clear();
            Invalidate(p);
        }
        Record("clear redlines all");
    }

    /// <summary>CLEAR REDLINES n[,m…]: removes redlines by 1-based position from the active plot and the master set.</summary>
    public void ClearRedlines(IEnumerable<int> numbers)
    {
        var plot = ActivePlot;
        foreach (int m in numbers.Distinct().OrderByDescending(x => x))
        {
            if (m >= 1 && m <= MasterRedlines.Count) MasterRedlines.RemoveAt(m - 1);
            if (plot != null && m >= 1 && m <= plot.Redlines.Count) plot.Redlines.RemoveAt(m - 1);
        }
        if (plot != null) Invalidate(plot);
    }

    /// <summary>
    /// Reads a <c>.markers</c> file (FR-LOAD-060): one marker per line as <c>&lt;timestamp&gt;,&lt;text&gt;[,TOP|BOTTOM]</c>
    /// or one redline per line as <c>redline &lt;value&gt;,&lt;text&gt;[,LEFT|RIGHT]</c>; blank lines and lines starting
    /// with <c>;</c> or <c>#</c> are ignored. Markers and redlines are added to the master sets and the active
    /// plot (all plots with Apply Changes Globally). Returns the number of markers and redlines read or −1.
    /// </summary>
    public int MarkersFromFile(string fileName)
    {
        List<string> lines;
        try { lines = AnsiText.ReadAllLines(fileName.Trim()); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Message("Failed to open 'Marker' file");
            return -1;
        }
        var read = new List<Marker>();
        var readRedlines = new List<RedlineMarker>();
        int bad = 0, firstBad = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
            if (RedlineMarker.TryParseCommand(line, out var rl))
            {
                if (rl == null) { if (bad++ == 0) firstBad = i + 1; }
                else readRedlines.Add(rl);
                continue;
            }
            var m = Marker.Parse(line);
            if (m == null) { if (bad++ == 0) firstBad = i + 1; continue; }
            read.Add(m);
        }
        if (bad > 0)
            Message(Crt.Sprintf("%d invalid line(s) in marker file skipped (first: line %d).\nExpected: <timestamp>,<text>[,TOP|BOTTOM]\nor: redline <value>,<text>[,LEFT|RIGHT]", bad, firstBad));
        if (read.Count > 0)
        {
            foreach (var m in read) Marker.AddTo(MasterMarkers, m);
            foreach (var p in TargetPlots(Settings.ApplyAll))
            {
                foreach (var m in read) Marker.AddTo(p.Markers, m);
                Invalidate(p);
            }
        }
        if (readRedlines.Count > 0)
        {
            foreach (var r in readRedlines) RedlineMarker.AddTo(MasterRedlines, r);
            foreach (var p in TargetPlots(Settings.ApplyAll))
            {
                foreach (var r in readRedlines) RedlineMarker.AddTo(p.Redlines, r);
                Invalidate(p);
            }
        }
        return read.Count + readRedlines.Count;
    }

    /// <summary>
    /// Writes the markers and redlines of the active plot (or the master sets) as a <c>.markers</c> file
    /// (FR-LOAD-061); returns the number written or −1.
    /// </summary>
    public int MarkersToFile(string fileName)
    {
        var set = ActivePlot?.Markers ?? MasterMarkers;
        var redlines = ActivePlot?.Redlines ?? MasterRedlines;
        var sb = new System.Text.StringBuilder();
        sb.Append("; Multiplot.NET event markers: <timestamp>,<text>[,TOP|BOTTOM]\r\n");
        sb.Append("; redline markers: redline <value>,<text>[,LEFT|RIGHT]\r\n");
        foreach (var m in set) sb.Append(m.ToSpec()).Append("\r\n");
        foreach (var r in redlines) sb.Append(r.ToCommand()).Append("\r\n");
        try
        {
            AnsiText.WriteAllText(fileName.Trim(), sb.ToString());
            return set.Count + redlines.Count;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Message("Failed to open 'Marker' file");
            return -1;
        }
    }

    /// <summary>Script text that records a marker.</summary>
    public static string MarkerCommand(Marker m) => "marker " + m.ToSpec();

    // ------------------------------------------------------------------ windows

    /// <summary>Tiling commands.</summary>
    public void Arrange(WindowArrange mode)
    {
        if (AutoTab != 0 && mode != WindowArrange.MaximizeActive && mode != WindowArrange.MinimizeActive)
        {
            SetAutoTab(0);
            return;
        }
        switch (mode)
        {
            case WindowArrange.TileHorizontal: Record("tile horizontal"); break;
            case WindowArrange.TileVertical: Record("tile vertical"); break;
            case WindowArrange.Cascade: Record("tile cascade"); break;
            case WindowArrange.Stack: Record("tile stacked"); break;
        }
        Shell.Arrange(mode);
    }

    /// <summary>Auto tabulation, script path (0 = off).</summary>
    public void SetAutoTab(int seconds)
    {
        AutoTab = seconds;
        if (seconds > 0)
        {
            Shell.Arrange(WindowArrange.MaximizeActive);
            Shell.StartTimer("autotab", Math.Min(seconds, 60) * 1000, NextWindow);
        }
        else
        {
            Shell.StopTimer("autotab");
            AutoTab = 0;
        }
    }

    /// <summary>Auto tabulation, interactive path.</summary>
    public void AutoTabDialog()
    {
        if (AutoTab != 0)
        {
            SetAutoTab(0);
            Record("AutoTab 0");
            return;
        }
        var r = Shell.Dialogs.Input(new InputRequest("Tab interval in seconds (1..60):", "5", "Auto Tab", new[] { "1", "2", "3", "5", "10", "15", "20", "30", "60" }));
        if (r == null) return;
        int s = Math.Abs(Crt.Atoi(r.Text));
        Record(Crt.Sprintf("AutoTab %d", s));
        SetAutoTab(s);
    }

    /// <summary>Toggles presentation mode.</summary>
    public void TogglePresentationMode()
    {
        PresentationMode = !PresentationMode;
        Shell.SetPresentationMode(PresentationMode, Settings.MaxPresentation);
        var plot = ActivePlot;
        if (plot == null || !Shell.IsMaximized(plot))
            Shell.Arrange(WindowArrange.TileHorizontal);
        Record("PresentationMode " + OnOff(PresentationMode));
    }


    /// <summary>Status bar visibility.</summary>
    public void SetStatusBarVisible(bool? v)
    {
        Settings.ShowStatusBar = v ?? !Settings.ShowStatusBar;
        Shell.ShowStatusBar(Settings.ShowStatusBar);
        Record("STATUSBAR " + OnOff(Settings.ShowStatusBar));
    }

    // ------------------------------------------------------------------ export

    private ExportOptions ExportOpts() => new()
    {
        ShowGmt = Settings.ShowGmt,
        SharedTabTime = Settings.SharedTabTime,
        TimePrecision = Settings.TimePrecision,
        DataPrecision = Settings.DataPrecision,
        ExportDir = Settings.ExportDir,
        TempDir = Settings.TempDir,
    };

    /// <summary>Returns the written file or null.</summary>
    public string? ToAscii(char delimiter, string? fileName = null)
    {
        var plot = ActivePlot;
        if (plot == null) return null;
        if (plot.Config.EndToEnd && Message("Tabulation of End-to-End plots will include full parameter data sets with actual time stamps.\n\nUse standard axis mode for zooming sub-sections of data.",
            PromptButtons.OkCancel, PromptIcon.Information) == PromptResult.Cancel) return null;
        SetRefreshPause(true);
        try { return Exporter.ToAscii(plot, delimiter, fileName, ExportOpts()); }
        finally { SetRefreshPause(false); }
    }

    /// <summary>File → Export (save dialog).</summary>
    public void ExportFileDialog()
    {
        if (!ShowDialogs) return;
        var plot = ActivePlot;
        if (plot == null) { Message("Nothing to export"); return; }
        string? fn = Shell.Dialogs.SaveFile("Save tabular data to:", "Comma Separated Variable (*.csv)|*.csv|All files (*.*)|*.*", Crt.Sprintf("Plot%04d.csv", plot.PlotNo), Settings.ExportDir);
        if (fn == null) return;
        SetStatus("Tabulating...");
        if (ToAscii(',', fn) == null) Message("Export to file failed");
        Record("export " + fn);
        SetStatus("");
    }

    /// <summary>Exports plot data to the clipboard.</summary>
    public void ExportClipboard()
    {
        string tmp = Settings.TempDir + "\\CB_export." + (++_exportNo).ToString("000", System.Globalization.CultureInfo.InvariantCulture) + ".";
        string? fn = ToAscii('\t', tmp);
        if (fn == null) Message("Failed to generate TABULAR data");
        else
        {
            try
            {
                Shell.CopyTextToClipboard(AnsiText.ReadAllText(fn));
                File.Delete(fn);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Message("Failed Copy to Clipboard"); }
        }
        Record("export CLIPBOARD");
    }
    private int _exportNo;

    /// <summary>TABULATE: tab-delimited file opened with the view application.</summary>
    public void Tabulate()
    {
        if (Settings.EditApp.Length == 0)
        {
            Message("Define Tab Viewer application (Options->External Applilcations)");
            return;
        }
        string? fn = ToAscii('\t');
        if (fn != null) RunProgram(Settings.EditApp + " " + fn);
        Record("tabulate");
    }

    /// <summary>Exports plot data and opens it with the export application.</summary>
    public void ExportToApplication()
    {
        if (Settings.ExportApp.Length == 0)
        {
            Message("Define Export Application (Options->External Applilcations)");
            return;
        }
        string? fn = ToAscii(',');
        if (fn != null) RunProgram(Settings.ExportApp + " " + fn);
    }

    /// <summary>Starts an external program (RUN command).</summary>
    public void RunProgram(string commandLine)
    {
        string cmd = commandLine.Trim();
        if (cmd.Length == 0) return;
        try
        {
            string file, args;
            if (cmd[0] == '"')
            {
                int q = cmd.IndexOf('"', 1);
                file = q > 0 ? cmd[1..q] : cmd[1..];
                args = q > 0 ? cmd[(q + 1)..].TrimStart() : "";
            }
            else
            {
                int sp = cmd.IndexOf(' ');
                file = sp > 0 ? cmd[..sp] : cmd;
                args = sp > 0 ? cmd[(sp + 1)..] : "";
            }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file, args) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            ScriptLog("RUN failed: " + cmd + " (" + ex.Message + ")");
        }
    }

    /// <summary>Saves plot data as a SUN file.</summary>
    public int SaveSun(string fileName)
    {
        var plot = ActivePlot;
        if (plot == null) return -1;
        SetRefreshPause(true);
        try
        {
            Exporter.ToSun(plot, fileName);
            return 0;
        }
        catch (DataAccessException ex)
        {
            ScriptLog("SAVESUN failed: " + ex.Message);
            return -1;
        }
        finally { SetRefreshPause(false); }
    }

    /// <summary>Edit → Copy to file → as Sun.</summary>
    public void SaveSunDialog()
    {
        if (!ShowDialogs) return;
        var plot = ActivePlot;
        if (plot == null) { Message("Nothing to save"); return; }
        string? fn = Shell.Dialogs.SaveFile("Save Data to SUN format:", "SUN format (*.sun)|*.sun|All files (*.*)|*.*", Crt.Sprintf("Plot%04d.sun", plot.PlotNo), Settings.ExportDir);
        if (fn == null) return;
        SetStatus("Saving data to Sun format file...");
        if (SaveSun(fn) != 0) Message("Save failed");
        Record("SAVESUN " + fn);
        SetStatus("");
    }

    /// <summary>Renders and saves a plot picture; returns false on failure.</summary>
    public bool SavePicture(PlotDocument plot, string type, string fileName)
    {
        var (w, h) = Shell.PlotSize(plot);
        try
        {
            var bmp = PlotImage.Render(plot, w, h, Settings.OptimizedDraw != 0);
            PlotImage.Save(bmp, fileName, type);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException)
        {
            ScriptLog("Picture save failed: " + fileName + " (" + ex.Message + ")");
            return false;
        }
    }

    /// <summary>Save Picture dialog.</summary>
    public void SavePictureDialog()
    {
        var plot = ActivePlot;
        if (plot == null) return;
        string filter = string.Join("|", PlotImage.Types.Select(t => $"{t} image file (*.{PlotImage.Extension(t)})|*.{PlotImage.Extension(t)}"));
        string? fn = Shell.Dialogs.SaveFile("Save as Picture", filter, Crt.Sprintf("plot%04d", plot.PlotNo), Settings.ExportDir);
        if (fn == null) return;
        string ext = Path.GetExtension(fn).TrimStart('.');
        string type = PlotImage.NormalizeType(ext) ?? "PNG";
        if (!ext.Equals(PlotImage.Extension(type), StringComparison.OrdinalIgnoreCase) && PlotImage.NormalizeType(ext) == null) fn += "." + PlotImage.Extension(type);
        SetStatus("Copying plot to bitmap file");
        SavePicture(plot, type, fn);
        SetStatus("");
        Record("savepic " + type + " " + fn);
    }

    /// <summary>Automatic JPEG save.</summary>
    public void AutoSaveJpeg()
    {
        var plot = ActivePlot;
        if (plot == null) { Message("Nothing to copy"); return; }
        if (!IsInteractive) return;
        long stamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() % 1167609600;
        string fn = CommandParsers.ValidateFileName("", Settings.ExportDir, (int)stamp, "jpg");
        if (!SavePicture(plot, "JPEG", fn)) Message("Copy to JPEG file failed");
        Record("savepic JPEG " + fn);
    }

    /// <summary>Copies the plot to the clipboard as a bitmap.</summary>
    public void CopyPlotToClipboard()
    {
        var plot = ActivePlot;
        if (plot == null) return;
        Shell.CopyPlotToClipboard(plot);
        Record("copybmp CLIPBOARD");
    }

    /// <summary>DATASTATS file (FR-XPT-041); no file = Slice Average dialog.</summary>
    public void DataStats(string fileName)
    {
        var plot = ActivePlot;
        if (plot == null) return;
        if (fileName.Trim().Length == 0)
        {
            Shell.Dialogs.Show(this, DialogKind.SliceAverage);
            return;
        }
        string fn = fileName.Trim();
        if (!fn.Contains('\\')) fn = Settings.ExportDir + "\\" + fn;
        if (!Exporter.WriteDataStats(plot, fn, Settings.DataPrecision)) Message("Slice Average failed to open " + fn);
    }

    /// <summary>Saves a WPD display file (file chosen by the caller).</summary>
    public void SaveWpd(string fileName)
    {
        var plot = ActivePlot;
        if (plot == null) return;
        var sb = new System.Text.StringBuilder();
        sb.Append("coloredtext ").Append(plot.Config.LegendColor ? "ON" : "OFF").Append('\n');
        if (plot.BackgroundFile.Length > 0) sb.Append("bitmap ").Append(plot.BackgroundFile).Append('\n');
        bool legend = false;
        foreach (var c in plot.Curves)
        {
            string fmt = c.TabFormat < TabFormat.FixedPoint ? TabFormat.NameOf(c.TabFormat) : "DEC" + TabFormat.NameOf(c.TabFormat);
            sb.Append(Crt.Sprintf("display %s@%d,%d COLOR_INDEX=%d TAB_LEGEND=%d TAB_FORMAT=%s\n", c.Name, c.TabX, c.TabY, c.ColorIndex + 1, c.TabLegend ? 1 : 0, fmt));
            if (c.TabLegend) legend = true;
        }
        if (legend)
        {
            string cmd = NomenCommand(true, plot.Config.YNomen & ~Nomen.EFORMAT, plot.Config.YNomOrder, plot.Config.YNomLen);
            sb.Append("ynomen ").Append(cmd["YNOMEN".Length..]).Append('\n');
        }
        foreach (var m in plot.Markers) sb.Append("marker ").Append(m.ToSpec()).Append('\n');
        foreach (var r in plot.Redlines) sb.Append(r.ToCommand()).Append('\n');
        try { AnsiText.WriteAllText(fileName, sb.ToString().Replace("\n", "\r\n")); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { Message("Failed to open file"); }
    }

    // ------------------------------------------------------------------ import

    /// <summary>
    /// ASCII import. Scripted imports run <see cref="AsciiImporter.Initialize"/> (the dialog's initial evaluation)
    /// and honour /R (DEV-IMP-001 fix). Returns 0 on success.
    /// </summary>
    public int ImportAscii(string source, bool interactive, string? dest = null, int index = 1, int skip = 0, int title = 0, bool genTime = false, bool standard = false, string? reference = null)
    {
        FileLoading = true;
        var imp = new AsciiImporter
        {
            SourceFile = source,
            FileIndex = index,
            SkipLines = skip,
            TitleRecord = title,
            GenTimeTag = genTime,
            StandardFormat = standard,
            AppVersion = ProductVersion,
            Message = (m, cancel) => Message(m, cancel ? PromptButtons.OkCancel : PromptButtons.Ok) != PromptResult.Cancel,
            Progress = v => SetStatus(Crt.Sprintf("Importing %d%%", v)),
        };
        imp.DestFile = dest ?? Crt.Sprintf("%s\\%s.~%03d", Settings.TempDir, source[(source.LastIndexOf('\\') + 1)..], ImportCnt++);
        int status = 0;
        try
        {
            if (!imp.Initialize())
            {
                Message("Error opening selected import file.\nMake sure file is not open elsewhere.");
                return -1;
            }
            if (interactive)
            {
                if (!Shell.Dialogs.Import(imp)) status = -1;
                else if (!imp.FileReady)
                {
                    status = -2;
                    Message(imp.StatusMsg);
                }
            }
            else
            {
                imp.GenerateFile(reference);
                if (!imp.FileReady) status = -2;
            }
        }
        finally
        {
            FileLoading = false;
            SetStatus("");
        }
        Record(imp.RecordText());
        if (status == 0) LoadFileGroup(imp.DestFile);
        else if (imp.Abort) status = 0;
        return status;
    }

    /// <summary>Imports ASCII data from the clipboard.</summary>
    public void ImportClipboard()
    {
        string? text = Shell.GetClipboardText();
        if (!string.IsNullOrEmpty(text))
        {
            string fn = Crt.Sprintf("%s\\ClipBoard.%03d", Settings.TempDir, ++_importNo);
            try
            {
                AnsiText.WriteAllText(fn, text);
                if (ImportAscii(fn, ShowDialogs) != 0) Message("Import Failed.");
                File.Delete(fn);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Message("Import Failed."); }
        }
        Record("import CLIPBOARD");
    }
    private int _importNo;

    /// <summary>File → Import Tabular.</summary>
    public void ImportDialog()
    {
        var f = Shell.Dialogs.OpenFile("Import Tabular (Text Format) File", "Comma Delimited (*.csv)|*.csv|Text Files (*.txt)|*.txt|All Files (*.*)|*.*", Settings.ExportDir.Split(';')[0]);
        if (f == null || f.Length == 0) return;
        if (ImportAscii(f[0], true) != 0) Message("Import Failed.");
    }

    // ------------------------------------------------------------------ settings

    /// <summary>INI context with the application's version strings.</summary>
    public IniContext IniContext() => new()
    {
        AppVersion = ProductVersion,
        AppDate = ProductDate,
        LibraryVersions = Data.VersionLines(),
        LibCommand = (cmd, sig) => Data.ExecuteCommand(cmd, sig),
        AutoRefresh = AutoRefresh,
    };

    /// <summary>Loads the INI settings from <paramref name="file"/> (the current INI name is not changed); false when not found.</summary>
    public bool LoadSettings(string file)
    {
        var r = IniFile.Load(Settings, file, IniContext());
        if (!r.Found) return false;
        Data.SetOption(Settings.RebuildRate ? 1001 : 1002);
        Data.SetOption(Settings.Extrapolate ? 1007 : 1008);
        Data.SetOption(Settings.ScriptPrompt ? 1005 : 1006);
        Shell.ShowStatusBar(Settings.ShowStatusBar);
        Shell.SetAppearance(Settings.DarkMode);
        RefreshAllPlots();
        Shell.RefreshCommands();
        return true;
    }

    /// <summary>Interactive asks for the file.</summary>
    public void SaveSettings()
    {
        if (IsInteractive)
        {
            string? fn = Shell.Dialogs.SaveFile("Save Multiplot INI file", "Multiplot INI files (*.ini)|*.ini|All files (*.*)|*.*", Settings.IniFile, null);
            if (fn == null) return;
            Settings.IniFile = fn;
        }
        else if (Settings.IniFile.Length == 0) return;
        try { IniFile.Save(Settings, Settings.IniFile, IniContext()); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { Message("Error opening INI file"); }
    }

    /// <summary>Options → Load Settings.</summary>
    public void LoadSettingsDialog()
    {
        var f = Shell.Dialogs.OpenFile("Read settings from INI file", "Multiplot INI files (*.ini)|*.ini", Path.GetDirectoryName(Settings.IniFile));
        if (f == null || f.Length == 0) return;
        LoadSettings(f[0]);
        Record("LoadSettings " + f[0]);
    }

    // ------------------------------------------------------------------ settings import (FR-CFG-050)

    /// <summary>Registry root (under HKCU) of a WinPlot installation whose settings can be imported.</summary>
    public const string WinPlotRegistryRoot = "Software\\WinPlot";

    /// <summary>The default WinPlot INI, <c>%USERPROFILE%\My WinPlot\WinPlot.ini</c>; null without a profile.</summary>
    public static string? DefaultWinPlotIni()
    {
        string? profile = Environment.GetEnvironmentVariable("USERPROFILE");
        return string.IsNullOrEmpty(profile) ? null : Path.Combine(profile, "My WinPlot", "WinPlot.ini");
    }

    /// <summary>Options → Import WinPlot Settings: asks for the WinPlot INI, confirms, then imports.</summary>
    public void ImportWinPlotSettingsDialog()
    {
        string? dir = Path.GetDirectoryName(DefaultWinPlotIni() ?? "");
        var f = Shell.Dialogs.OpenFile("Import settings from a WinPlot INI file", "WinPlot INI files (*.ini)|*.ini|All files (*.*)|*.*",
            !string.IsNullOrEmpty(dir) && Directory.Exists(dir) ? dir : null);
        if (f == null || f.Length == 0) return;
        if (Message("Replace the current Multiplot.NET settings with the WinPlot settings?", PromptButtons.YesNo, PromptIcon.Question, PromptResult.No) != PromptResult.Yes)
            return;
        ImportWinPlotSettings(f[0]);
    }

    /// <summary>
    /// Imports the settings of a WinPlot installation: the INI file <paramref name="iniFile"/>, the favourites
    /// file next to it and the <c>HKCU\Software\WinPlot</c> registry values (recent files, recent parameter
    /// lists, favourites path, window position, user buttons, plugin settings). The result is saved
    /// as the Multiplot.NET settings. Returns false when the INI file cannot be read.
    /// </summary>
    public bool ImportWinPlotSettings(string iniFile)
    {
        if (!LoadSettings(iniFile))
        {
            Message("WinPlot INI file not found:\n" + iniFile);
            return false;
        }

        // WinPlot's stock default title becomes the Multiplot.NET default
        if (System.Text.RegularExpressions.Regex.IsMatch(Settings.XTitle, @"^WinPlot [0-9.x]+\\n"))
            Settings.XTitle = "Multiplot.NET" + Settings.XTitle[Settings.XTitle.IndexOf("\\n", StringComparison.Ordinal)..];

        // registry values (binary dialog layouts are not used, DEV-CFG-003)
        int values = Registry.CopyFrom(WinPlotRegistryRoot, "ParmSelect461", "Defaults");

        // favourites: a file inside the WinPlot folder is copied into the Multiplot.NET user folder;
        // a favourites file elsewhere (for example a shared one) stays in use where it is
        string winDir = Path.GetDirectoryName(Path.GetFullPath(iniFile)) ?? "";
        string myFav = Settings.UserFolder.Length > 0 ? Path.Combine(Settings.UserFolder, "Multiplot.favorites") : Settings.FavFile;
        string? regFav = values > 0 ? Registry.ReadFavoritesFile() : null;
        bool sharedFav = regFav != null && regFav.Length > 0 &&
            !string.Equals(Path.GetDirectoryName(Path.GetFullPath(regFav)), winDir, StringComparison.OrdinalIgnoreCase);
        if (sharedFav) Settings.FavFile = regFav!;
        else if (myFav.Length > 0)
        {
            string winFav = regFav is { Length: > 0 } ? regFav : Path.Combine(winDir, "WinPlot.favorites");
            try { if (File.Exists(winFav)) File.Copy(winFav, myFav, true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { DiagLog("Import: favourites not copied: " + ex.Message); }
            Settings.FavFile = myFav;
            Registry.WriteFavoritesFile(myFav);
        }

        if (values > 0)
        {
            foreach (var r in Registry.ReadRecentFiles()) AddRecentFile(r, false);
            var parms = Registry.ReadRecentParameters();
            for (int i = parms.Count - 1; i >= 0; i--) SaveParm(parms[i]);
            Registry.ReadUserButtons(Settings);
            UseUserPluginFolder = Registry.ReadUserPluginFolder();
        }

        if (Settings.IniFile.Length > 0)
        {
            try { IniFile.Save(Settings, Settings.IniFile, IniContext()); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { Message("Error opening INI file"); }
        }
        DiagLog("Imported WinPlot settings from " + iniFile + " (" + values + " registry values)");
        Shell.RebuildCommands();
        Message("WinPlot settings imported.\n\nBlocked plugins and the window position take effect the next time Multiplot.NET starts.",
            PromptButtons.Ok, PromptIcon.Information, PromptResult.Ok);
        return true;
    }

    // ------------------------------------------------------------------ plugins

    /// <summary>Plugin folders (FR-PLG-010).</summary>
    public IEnumerable<string> PluginFolders()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "plugins");
        if (UseUserPluginFolder && Settings.UserFolder.Length > 0) yield return Path.Combine(Settings.UserFolder, "plugins");
    }

    /// <summary>Plugin setup dialog.</summary>
    public void SetupPlugins()
    {
        Data.UnloadPlugins();
        if (!NoPlugins)
        {
            var r = Data.LoadPlugins(PluginFolders(), Registry.ReadBlockedPlugins(), this);
            foreach (var w in r.Warnings) DiagLog(w);
        }
        else
        {
            Data.BlockedPlugins.Clear();
            Data.BlockedPlugins.AddRange(Registry.ReadBlockedPlugins());
        }
        Shell.RefreshCommands();
    }

    /// <summary>ENABLE/DISABLE PLUGIN name.</summary>
    public void SetPluginBlocked(string name, bool blocked)
    {
        if (blocked) Registry.BlockPlugin(name); else Registry.UnblockPlugin(name);
        SetupPlugins();
    }
}
