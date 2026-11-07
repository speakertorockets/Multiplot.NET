// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

using System.Text;
using Multiplot.Core;
using Multiplot.Core.Model;
using Multiplot.DataAccess;
using Multiplot.Expressions;
using Multiplot.Plugins;
using Multiplot.Scripting;
using Multiplot.Settings;

namespace Multiplot.Application;

/// <summary>Script run states (FR-SCR-020).</summary>
public static class RunState
{
    public const int Idle = 0, Running = 1, Paused = 2, TimedPause = 3, Calling = 4, Aborting = -1, EndOfFile = -2, ExitRequested = -3;
}

/// <summary>
/// The application session — main window state and behaviour: open files and groups,
/// plot windows, settings, symbols, markers, script state, recording and logs. All user actions (menus,
/// scripts, command line) go through its methods (ARC-010).
/// </summary>
public sealed partial class Session : IPlotHost, IExpressionContext, IPluginHost, ISystemSymbols
{
    /// <summary>Maximum open files / groups / curves (MAXGROUPS, MAXCURVES).</summary>
    public const int MaxFiles = 1000, MaxGroups = 1000, MaxCurves = 1000, Remembered = 64;

    /// <summary>Product version string.</summary>
    public const string ProductVersion = "Multiplot.NET v1.0.0";
    /// <summary>Product date string.</summary>
    public const string ProductDate = "November 2026";

    private readonly List<IDataFile> _files = new();
    private readonly List<FileGroup> _groups = new();
    private readonly List<PlotDocument> _plots = new();
    private StreamWriter? _recordWriter;
    private StreamWriter? _logWriter;

    /// <summary>Creates a session.</summary>
    public Session(IShell shell, AppSettings settings, RegistryStore registry, DataAccessService data)
    {
        Shell = shell;
        Settings = settings;
        Registry = registry;
        Data = data;
        Symbols = new SymbolTable(settings.UserVars);
        RestoreColors = (int[])settings.Plot.DrawColor.Clone();
        Data.Log = DiagLog;
    }

    // ------------------------------------------------------------------ services

    /// <summary>UI shell.</summary>
    public IShell Shell { get; }
    /// <summary>Settings (INI model).</summary>
    public AppSettings Settings { get; }
    /// <summary>Registry store.</summary>
    public RegistryStore Registry { get; }
    /// <summary>Data access façade.</summary>
    public DataAccessService Data { get; }
    /// <summary>User symbols.</summary>
    public SymbolTable Symbols { get; }

    /// <summary>Default plot configuration.</summary>
    public PlotConfig PlotConfig => Settings.Plot;

    // ------------------------------------------------------------------ files, groups, plots

    /// <summary>Open files.</summary>
    public IReadOnlyList<IDataFile> Files => _files;
    /// <summary>File groups.</summary>
    public IReadOnlyList<FileGroup> Groups => _groups;
    /// <summary>Plot windows in z-order (index 0 = active).</summary>
    public IReadOnlyList<PlotDocument> Plots => _plots;
    /// <summary>The active plot or null.</summary>
    public PlotDocument? ActivePlot => _plots.Count > 0 ? _plots[0] : null;

    /// <summary>Plot window iteration: the active plot, then the others when <paramref name="all"/>.</summary>
    public IEnumerable<PlotDocument> TargetPlots(bool all)
    {
        var snapshot = _plots.ToList();
        for (int i = 0; i < snapshot.Count; i++)
        {
            yield return snapshot[i];
            if (!all) yield break;
        }
    }

    /// <summary>Makes a plot active (z-order front).</summary>
    public void ActivatePlot(PlotDocument p, bool notifyShell = true)
    {
        int i = _plots.IndexOf(p);
        if (i < 0) return;
        if (Align != null && Align.Plot != p) CancelAlign();
        if (i > 0) { _plots.RemoveAt(i); _plots.Insert(0, p); }
        if (notifyShell) Shell.Activate(p);
    }

    /// <summary>Activates the plot with the next higher plot number (optionally within the auto-tab group).</summary>
    public void NextWindow()
    {
        var plot = ActivePlot;
        if (plot == null) return;
        int plotNo = plot.PlotNo < PlotNo ? plot.PlotNo : 0;
        int nearest = 32767, loPage = plot.PlotNo;
        PlotDocument? found = null, lo = null;
        foreach (var p in _plots.Skip(1))
        {
            if (p.PlotNo > plotNo && p.PlotNo < nearest) { nearest = p.PlotNo; found = p; }
            if (p.PlotNo < loPage) { loPage = p.PlotNo; lo = p; }
        }
        found ??= lo;
        if (found != null) ActivatePlot(found);
        if (AutoTab == 0) Record("TAB NEXT");
    }

    /// <summary>Activates the previous plot window.</summary>
    public void PreviousWindow()
    {
        if (AutoTab != 0) { SetAutoTab(0); return; }
        var plot = ActivePlot;
        if (plot == null) return;
        int plotNo = plot.PlotNo > 1 ? plot.PlotNo : PlotNo + 1;
        int nearest = 0;
        PlotDocument? found = null;
        foreach (var p in _plots.Skip(1))
            if (p.PlotNo < plotNo && p.PlotNo > nearest) { nearest = p.PlotNo; found = p; }
        if (nearest == 0)
        {
            plotNo = PlotNo + 1;
            foreach (var p in _plots)
                if (p.PlotNo < plotNo && p.PlotNo > nearest) { nearest = p.PlotNo; found = p; }
        }
        if (found != null) ActivatePlot(found);
        Record("TAB PREVIOUS");
    }

    /// <summary>Removes a plot window (closed by the user or by CLEAR PLOTS).</summary>
    public void RemovePlot(PlotDocument p)
    {
        if (Align?.Plot == p) { Align = null; Shell.RefreshCommands(); }
        if (_plots.Remove(p)) Shell.PlotRemoved(p);
        if (ActivePlot != null) Shell.Activate(ActivePlot);
    }

    /// <summary>Requests a redraw of a plot.</summary>
    public void Invalidate(PlotDocument p) => Shell.Invalidate(p);

    // ------------------------------------------------------------------ frame state

    public string XAxis = "(default)", YAxis = "";
    public readonly string[] PastParms = new string[Remembered];
    /// <summary>Master marker set: applied to new plots when Continue Markers is on.</summary>
    public readonly List<Marker> MasterMarkers = new();
    /// <summary>Master redline set: applied to new plots when Continue Markers is on.</summary>
    public readonly List<RedlineMarker> MasterRedlines = new();
    public readonly FixedScaleDefaults Fix = new() { XSetGrad = -1 };
    public readonly int[] RestoreColors;
    public int PlotNo, ImportCnt;
    /// <summary>
    /// File last chosen in the Parameter Selection dialog (−1 = All Files); only remembers the dialog choice.
    /// Loading is limited to one file by file-qualified names (<see cref="QualifierForFile"/>), not by this.
    /// </summary>
    public int CurrentFile = -1;
    public bool DataLoading, FileLoading, RtLoaded, TimeChange, RefreshPaused, Refreshing;
    public int AutoRefresh;
    public bool RunHidden, NoPlugins, UseUserPluginFolder, ExtCmdPolling, PresentationMode, KeepOnTop;
    public string BackgroundFn = "", UserCaption = "", UDefBuf = "";
    public int SetNomen, AutoTab, LayoutGridSpacingValue;
    public bool SnapToGridValue, ShuttingDown;
    public readonly List<string> AutoRunList = new();
    public string LogFileName = "";
    public string? IniVersion;

    // script state
    public int ScriptRun;
    public string ScriptFile = "";
    public int ScriptLineNo;
    public int PauseNext;
    public string ScriptCall = "";
    public bool Recording => _recordWriter != null;
    public string RecordFile = "";

    /// <summary>True when no script is running.</summary>
    public bool IsInteractive => ScriptRun != RunState.Running;
    /// <summary>True while a script is active.</summary>
    public bool IsScriptActive => ScriptRun == RunState.Running;
    /// <summary>Dialogs may be shown.</summary>
    public bool ShowDialogs => !RunHidden && ((ScriptRun != 0 && Settings.ScriptPrompt) || ScriptRun == 0);

    // ------------------------------------------------------------------ messages, logging, recording

    /// <summary>Shows a message box (OK, exclamation by default).</summary>
    public PromptResult Message(string text, PromptButtons buttons = PromptButtons.Ok, PromptIcon icon = PromptIcon.Warning, PromptResult defaultResult = PromptResult.Ok)
    {
        if (RunHidden) ScriptLog("[auto] " + text.Replace("\n", " | "));
        return Shell.Ui.Show(text, "Multiplot", buttons, icon, defaultResult);
    }

    /// <summary>Status bar text.</summary>
    public void SetStatus(string text) => Shell.SetStatus(text);

    /// <summary>Diagnostic log (NFR-LOG-001): %LOCALAPPDATA%\Multiplot\logs.</summary>
    public void DiagLog(string text)
    {
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Multiplot", "logs");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "multiplot-" + DateTime.Now.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture) + ".log"),
                DateTime.Now.ToString("HH:mm:ss.fff ", System.Globalization.CultureInfo.InvariantCulture) + text + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Time-stamped entry in the user script log (FR-SCR-029).</summary>
    public void ScriptLog(string text)
    {
        DiagLog(text);
        if (_logWriter == null) return;
        try
        {
            _logWriter.Write(FormatLogTime(DateTime.Now) + "  " + text + "\r\n");
            _logWriter.Flush();
        }
        catch (IOException) { }
    }

    private static string FormatLogTime(DateTime t)
    {
        string s = t.Hour > 11
            ? Crt.Sprintf("%d:%02d:%02dPM", t.Hour == 12 ? 12 : t.Hour - 12, t.Minute, t.Second)
            : Crt.Sprintf("%d:%02d:%02dAM", t.Hour != 0 ? t.Hour : 12, t.Minute, t.Second);
        return s + t.ToString(" MM/dd/yyyy", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>True when the script log is open.</summary>
    public bool LogOpen => _logWriter != null;

    /// <summary>Toggles the script log (interactive: asks for a file).</summary>
    public void ToggleScriptLog()
    {
        if (_logWriter != null)
        {
            _logWriter.Dispose();
            _logWriter = null;
            Record("SCRIPTLOG OFF");
            return;
        }
        bool cancel = false;
        if (IsInteractive)
        {
            string start = LogFileName.Length == 0 || LogFileName.LastIndexOf('\\') < 0 ? Settings.TempDir : LogFileName[..LogFileName.LastIndexOf('\\')];
            string? fn = Shell.Dialogs.SaveFile("Set Script Log File", "Multiplot Script Log (*.log)|*.log|All Files (*.*)|*.*", null, start);
            if (fn != null)
            {
                LogFileName = fn;
                Record("SCRIPTLOG " + LogFileName);
            }
            else cancel = true;
        }
        if (cancel) return;
        try
        {
            _logWriter = new StreamWriter(new FileStream(LogFileName, FileMode.Create, FileAccess.Write, FileShare.Read), AnsiText.Ansi);
            ScriptLog("Multiplot script log started");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Message("Failed opening log file", PromptButtons.Ok, PromptIcon.Warning);
        }
    }

    /// <summary>Appends a command line (CRLF) to the recording.</summary>
    public void Record(string line)
    {
        if (_recordWriter == null) return;
        try
        {
            _recordWriter.Write(line + "\r\n");
            _recordWriter.Flush();
        }
        catch (IOException) { }
    }

    /// <summary>Starts recording to <paramref name="file"/> or stops (null).</summary>
    public void SetRecording(string? file)
    {
        if (ScriptRun != 0)
        {
            Message("Can't record and execute simultaneously.\nCancel script execution first.");
            return;
        }
        if (_recordWriter != null)
        {
            _recordWriter.Dispose();
            _recordWriter = null;
            ScriptFile = "";
            RecordFile = "";
            Shell.RefreshCommands();
            return;
        }
        if (file == null) return;
        try
        {
            _recordWriter = new StreamWriter(new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.Read), AnsiText.Ansi);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Message("Failed opening record file");
            return;
        }
        RecordFile = file.ToLowerInvariant();
        ScriptFile = RecordFile[(RecordFile.LastIndexOf('\\') + 1)..];
        Record(";Multiplot Script file (Multiplot generated)");
        Record(";" + ProductVersion);
        Shell.RefreshCommands();
    }

    /// <summary>Remembered parameter lists.</summary>
    public void SaveParm(string name)
    {
        if (name.Length == 0 || name.Equals("(default)", StringComparison.OrdinalIgnoreCase) || name.Equals("TIME", StringComparison.OrdinalIgnoreCase)) return;
        for (int i = 0; i < Remembered; i++) if (PastParms[i] == name) PastParms[i] = "";
        for (int i = Remembered - 1; i > 0; i--) PastParms[i] = PastParms[i - 1];
        PastParms[0] = name;
    }

    /// <summary>Pauses real-time refresh (FR-RT-002): refresh runs on the UI thread here, so pausing is a flag.</summary>
    public void SetRefreshPause(bool pause) => RefreshPaused = pause;

    // ------------------------------------------------------------------ IPlotHost

    IReadOnlyList<IDataFile> IPlotHost.Files => _files;
    IReadOnlyList<FileGroup> IPlotHost.Groups => _groups;
    string[] IPlotHost.LogicTxtTrue => Settings.LogicTxtTrue;
    string[] IPlotHost.LogicTxtFalse => Settings.LogicTxtFalse;
    string IPlotHost.Version => ProductVersion;
    bool IPlotHost.PresentationMode => PresentationMode;
    bool IPlotHost.DarkAppearance => Settings.DarkMode;
    int IPlotHost.LayoutGridSpacing => LayoutGridSpacingValue;
    bool IPlotHost.SnapToGrid => SnapToGridValue;
    bool IPlotHost.AutoRefreshOn => AutoRefresh != 0;
    bool IPlotHost.StreamCert => Settings.StreamCert;
    int IPlotHost.DataPrecision => Settings.DataPrecision;
    int IPlotHost.TimePrecision => Settings.TimePrecision;
    int IPlotHost.MinGridOffset => Settings.MinGridOffset;
    int IPlotHost.ReservedLegend => Settings.ReservedLegend;
    bool IPlotHost.RtLoaded => RtLoaded;

    bool IPlotHost.ResolveTime(int group, string spec, string tRef, out double time)
    {
        time = 0;
        if (group < 0 || group >= _groups.Count) return false;
        return TimeReference.SetTime(GroupFiles(group), 0, spec, tRef, out time) == 0;
    }

    void IPlotHost.ShowMessage(string text) => Message(text);

    /// <summary>Files of a group.</summary>
    public IReadOnlyList<IDataFile> GroupFiles(int g) => _files.GetRange(_groups[g].StartPos, _groups[g].NFiles);

    // ------------------------------------------------------------------ IExpressionContext

    private int _lastReadStatus;
    private bool _lastReadNoData;

    IReadOnlyList<IDataFile> IExpressionContext.AllFiles => _files;
    IReadOnlyList<FileGroup> IExpressionContext.Groups => _groups;

    /// <summary>Reads a float parameter; null on failure with <see cref="LastStatus"/>.</summary>
    public ParameterData? Read(IDataFile file, string name, double start, double stop, double? refTime, TimeTag? refGmt, bool returnGmt)
    {
        _lastReadNoData = false;
        try
        {
            var p = file.Read(new ParameterRequest(name, start, stop, refTime, refGmt, DataType.Float, returnGmt));
            _lastReadStatus = p == null ? (int)SunError.PidNotFound : p.Warning;
            return p;
        }
        catch (DataAccessException ex)
        {
            _lastReadStatus = ex.Code;
            _lastReadNoData = ex is NoDataException;
            return null;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException or IndexOutOfRangeException)
        {
            DiagLog("Read failed: " + ex);
            _lastReadStatus = (int)SunError.ComReadFail;
            return null;
        }
    }

    /// <summary>Status of the last failed read.</summary>
    public int LastStatus => _lastReadStatus;

    /// <summary>The last failed read found the parameter in the file map but no data blocks.</summary>
    public bool LastReadNoData => _lastReadNoData;
    bool IExpressionContext.Use64BitTime => Settings.Plot.Use64BitTime;
    bool IExpressionContext.CalcEveryInstance => Settings.CalcEveryInstance;
    bool IExpressionContext.AlignClosest => Settings.AlignClosest;
    float IExpressionContext.AverageRate => Settings.AverageRate;
    int IExpressionContext.TimeStampMode => Settings.TimeStampMode;
    bool IExpressionContext.ShowDialogs => ShowDialogs;
    bool IExpressionContext.ScriptRunning => ScriptRun != 0;
    bool IExpressionContext.ScriptPrompt => Settings.ScriptPrompt;
    bool IExpressionContext.AutoRefresh => AutoRefresh != 0;
    bool IExpressionContext.FilterInvalid => Data.Sun.Options.FilterInvalid;
    IUserInteraction IExpressionContext.Ui => Shell.Ui;
    void IExpressionContext.SetStatus(string text) => SetStatus(text);
    IReadOnlyList<IExpressionFunctionProvider> IExpressionContext.FunctionProviders => Data.FunctionProviders;

    // ------------------------------------------------------------------ IPluginHost

    string IPluginHost.UserFolder => Settings.UserFolder;
    string IPluginHost.TempFolder => Settings.TempDir;
    bool IPluginHost.ScriptRunning => ScriptRun != 0;
    bool IPluginHost.DialogsAllowed => ShowDialogs;
    void IPluginHost.RecordScriptLine(string line) => Record(line);
    void IPluginHost.Log(string message) => ScriptLog(message);
    IUserInteraction IPluginHost.Ui => Shell.Ui;

    // ------------------------------------------------------------------ ISystemSymbols (FR-SCR-052)

    /// <summary>Value of a system symbol.</summary>
    public string GetValue(string name)
    {
        var plot = ActivePlot;
        switch (name)
        {
            case "PRINT_APP": return Settings.TextPrinter;
            case "VIEW_APP": return Settings.EditApp;
            case "EXPORT_APP": return Settings.ExportApp;
            case "MAILSLOT_NAME": return "Auto";
            case "SCRIPT_NAME": return ScriptFile;
            case "SCRIPT_LINE": return ScriptLineNo.ToString(System.Globalization.CultureInfo.InvariantCulture);
            case "CURRENT_FILE": return _files.Count > 0 ? _files[0].Info.Path : "<CURRENT_FILE>";
            case "FILE_LIST": return _files.Count > 0 ? string.Join(",", _files.Select(f => f.Info.Path)) : "<FILE_LIST>";
            case "FILE_REFERENCE":
                return _files.Count > 0 ? string.Join(",", _files.Select(f => ValueFormat.ClockString(f.Info.RefGmt, "%03u/%02u:%02u:%02u.%03u"))) : "<FILE_REFERENCE>";
            case "EVENT_REFERENCE":
                return _groups.Count > 1 ? string.Join(",", _groups.Select(g => g.TRef)) : Settings.TRef;
            case "GMT_REFERENCE":
                if (_groups.Count == 0) return Settings.TRef;
                return string.Join(",", Enumerable.Range(0, _groups.Count).Select(g =>
                {
                    var t = TimeReference.GetRefGmt(GroupFiles(g), 0, _groups[g].TRef);
                    return t.HasValue ? ValueFormat.ClockString(t.Value, "%03u/%02u:%02u:%02u.%03u.%03u") : "<GMT_REFERENCE>";
                }));
            case "SCALE_XMIN": return plot != null ? Crt.Sprintf("%g", plot.XScale.RMin) : "<SCALE_XMIN>";
            case "SCALE_XMAX": return plot != null ? Crt.Sprintf("%g", plot.XScale.RMax) : "<SCALE_XMAX>";
            case "SCALE_YMIN":
            case "SCALE_YMAX":
                if (plot == null) return "<" + name + ">";
                {
                    var sb = new StringBuilder();
                    for (int n = 0; n < plot.NScales && n < plot.YScales.Count; n++)
                    {
                        if (n > 0) sb.Append(',');
                        sb.Append(Crt.Sprintf("%g", name == "SCALE_YMIN" ? plot.YScales[n].RMin : plot.YScales[n].RMax));
                        if (!plot.Config.Multi) break;
                    }
                    return sb.ToString();
                }
            case "PLOT_X_EXTENTS": return plot != null ? Crt.Sprintf("%g,%g", plot.XScale.RMin, plot.XScale.RMax) : "<PLOT_X_EXTENTS>";
            case "PLOT_Y_EXTENTS": return plot != null && plot.YScales.Count > 0 ? Crt.Sprintf("%g,%g", plot.YScales[0].RMin, plot.YScales[0].RMax) : "<PLOT_Y_EXTENTS>";
            case "PLOT_PARMS": return plot != null ? string.Join(",", plot.Curves.Select(c => c.Name)) : "<PLOT_PARMS>";
            case "PLOT_SOURCE": return plot != null ? string.Join(",", plot.Curves.Select(c => (c.Data.Source?.Info.Path ?? "") + ";" + c.Name)) : "<PLOT_SOURCE>";
            case "SYSTEM_TIME":
                {
                    var t = DateTime.Now;
                    return Crt.Sprintf("%03d/%02d:%02d:%02d.000", t.DayOfYear, t.Hour, t.Minute, t.Second);
                }
            case "USERNAME": return Environment.UserName;
            case "USERFOLDER": return Settings.UserFolder.Length > 0 ? Settings.UserFolder : "<USERFOLDER>";
            case "COMPUTER": return Environment.MachineName;
            case "LAST_FILE_TIME":
                {
                    if (_files.Count == 0 || _files[0].Parameters.Count == 0) return "<LAST_FILE_TIME>";
                    var f = _files[0];
                    var p = Read(f, f.Parameters[0].Name, -3e38, 3e38, null, null, false);
                    if (p == null || p.Points == 0) return "<LAST_FILE_TIME>";
                    var rt = f.Info.RefTime;
                    short day = (short)rt[1];
                    if (day == 0) day++;
                    var gmt = new TimeTag(day, unchecked(rt[2] * 3600000U + rt[3] * 60000U + rt[4] * 1000U + rt[5]), 0);
                    return ValueFormat.ClockString(TimeMath.Gmt(p.Time[p.Points - 1], gmt, true), "%03u/%02u:%02u:%02u.%03u");
                }
        }
        return "";
    }

    /// <summary>Substitutes symbols in a string; returns true when it contained '$'.</summary>
    public bool SymbolSubstitute(ref string text, bool report = true) =>
        Symbols.Substitute(ref text, this, report ? m => Message(m, PromptButtons.Ok, PromptIcon.Information) : null);

    // ------------------------------------------------------------------ helpers

    /// <summary>First readable <paramref name="fn"/> along a ';'-separated path list.</summary>
    public static string? WhereIs(string fn, string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        foreach (string dir in path.Split(';'))
        {
            string d = dir.Length > 0 && dir[^1] != '\\' ? dir + "\\" : dir;
            string candidate = d + fn;
            try
            {
                if (File.Exists(candidate)) return candidate;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { }
        }
        return null;
    }

    /// <summary>Opens a text file for reading as given or along a search path; null if not found.</summary>
    public static string? Locate(string name, string? searchPath)
    {
        if (File.Exists(name)) return Path.GetFullPath(name);
        return WhereIs(name[(name.LastIndexOf('\\') + 1)..], searchPath);
    }

    /// <summary>Releases files and writers.</summary>
    public void Shutdown()
    {
        _recordWriter?.Dispose();
        _recordWriter = null;
        _logWriter?.Dispose();
        _logWriter = null;
        foreach (var f in _files)
        {
            try { f.Dispose(); } catch (Exception) { }
        }
        _files.Clear();
        _groups.Clear();
        Data.UnloadPlugins();
    }
}
