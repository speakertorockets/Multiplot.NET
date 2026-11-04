using Multiplot.Core;
using Multiplot.Core.Model;
using static Multiplot.Core.CommandIds;

namespace Multiplot.Application;

/// <summary>
/// Dispatch of command ids (menus, toolbars, accelerators, popups) to the session handlers — the single
/// command layer of ARC-010. Ids keep fixed values so INI/registry toolbar layouts round-trip (FR-CFG-040).
/// </summary>
public sealed partial class Session
{
    /// <summary>Command ids that are out of scope (DEV-UI-002); toolbars skip them.</summary>
    public static readonly HashSet<int> OutOfScopeCommands = new()
    {
        ID_DBFPRINT, ID_DBFPRINTSETUP, ID_COPYCLIPBOARD, ID_EMFASPECT, ID_EXT_CMD_POLL, ID_QUERY, ID_HELP_CONTENTS,
        ID_COLORPRINT, ID_PRINTLAND, ID_PRINTMARGINS, ID_SAVEMETA,
    };

    private bool ApplyAllGlobal => Settings.ApplyAll;

    /// <summary>Executes a command id; returns false when the id is unknown.</summary>
    public async Task<bool> ExecuteCommandAsync(int id)
    {
        var plot = ActivePlot;
        switch (id)
        {
            // File
            case ID_FILE_NEW: OpenFileDialog(true); break;
            case ID_FILE_OPEN: OpenFileDialog(false); break;
            case ID_IMPORT_FILE: ImportDialog(); break;
            case ID_CLOSELAST: CloseLast(); break;
            case ID_DBFCLOSE: CloseAllFiles(); break;
            case ID_FILELIST: Shell.Dialogs.Show(this, DialogKind.FileProperties); break;
            case ID_APP_EXIT: Shell.RequestExit(); break;

            // Edit
            case ID_COPY_AS_BMP: CopyPlotToClipboard(); break;
            case ID_EXPORTCB: ExportClipboard(); break;
            case ID_COPY_AS_PIC: SavePictureDialog(); break;
            case ID_EXPORT: ExportFileDialog(); break;
            case ID_SAVE_WPD:
                if (plot != null)
                {
                    string? fn = Shell.Dialogs.SaveFile("Save WPD file to:", "WPD display file (*.wpd)|*.wpd", Crt.Sprintf("plot%04d.wpd", plot.PlotNo), Settings.WpdFolder);
                    if (fn != null) SaveWpd(fn);
                }
                break;
            case ID_COPY_AS_SUN: SaveSunDialog(); break;
            case ID_IMPORTCB: ImportClipboard(); break;
            case ID_VIEW_EXPORTCURRENT: ExportToApplication(); break;
            case ID_VAR_EDIT: Shell.Dialogs.Show(this, DialogKind.SymbolicNames); break;
            case ID_TAB_GRID: Shell.Dialogs.Show(this, DialogKind.LayoutGrid); RefreshAllPlots(); break;
            case ID_SNAPTOGRID:
                SnapToGridValue = !SnapToGridValue;
                if (SnapToGridValue && LayoutGridSpacingValue == 0)
                {
                    Shell.Dialogs.Show(this, DialogKind.LayoutGrid);
                    if (LayoutGridSpacingValue == 0) SnapToGridValue = false;
                }
                RefreshAllPlots();
                break;

            // Script
            case ID_SCRIPT_RUN:
                if (Recording) Message("Can't execute script during record.\nCancel recording first.");
                else if (IsScriptActive) Message("Can't execute script.\nCancel or pause current script first.");
                else if (Scripts.StackDepth == 0 || Shell.IsShiftPressed())
                {
                    var f = Shell.Dialogs.OpenFile("Execute Script File", "Multiplot Script Files (*.wps)|*.wps|All Files (*.*)|*.*", Settings.ScriptDir.Split(';')[0]);
                    if (f != null && f.Length > 0) await Scripts.RunFromUserAsync(f[0]);
                }
                else await Scripts.ResumeAsync();
                break;
            case ID_SCRIPT_RECORD:
                if (Recording) SetRecording(null);
                else if (ScriptRun != 0) SetRecording("x");
                else
                {
                    string? fn = Shell.Dialogs.SaveFile("Open Script Recording File", "Multiplot Script Files (*.wps)|*.wps|All Files (*.*)|*.*", null, Settings.ScriptDir.Split(';')[0]);
                    if (fn != null) SetRecording(fn);
                }
                break;
            case ID_SCRIPT_EDIT:
                {
                    var f = Shell.Dialogs.OpenFile("Edit Script File", "Multiplot Script Files (*.wps)|*.wps|All Files (*.*)|*.*", Settings.ScriptDir.Split(';')[0]);
                    if (f != null && f.Length > 0) RunProgram((Settings.EditApp.Length > 0 ? Settings.EditApp : "notepad") + " \"" + f[0] + "\"");
                }
                break;
            case ID_SCRIPT_NEXT: await Scripts.NextAsync(); break;
            case ID_SCRIPT_PAUSE: await Scripts.PauseToggleAsync(); break;
            case ID_SCRIPT_RESUME: await Scripts.ResumeAsync(); break;
            case ID_SCRIPT_ABORT: AbortScript(true); break;
            case ID_SCRIPTING_PAUSEANYCOMMAND: SetPauseMode(null); break;
            case ID_SCRIPT_PROMPT: SetScriptPrompt(null); break;
            case ID_SCRIPTLOG: ToggleScriptLog(); break;
            case ID_USEPLACEHOLDER: Settings.UsePlaceholder = !Settings.UsePlaceholder; Record("usePlaceholder " + OnOff(Settings.UsePlaceholder)); break;
            case ID_SCRIPT_EMBEDDED: Settings.EmbeddedScript = !Settings.EmbeddedScript; Record("embeddedscript " + OnOff(Settings.EmbeddedScript)); break;

            // Parameter
            case ID_PARMSELECT: SelectParameter(ParmSelectMode.Normal); break;
            case ID_FAVORITES: Shell.Dialogs.Show(this, DialogKind.Favorites); break;
            case ID_FILTERS: Shell.Dialogs.Show(this, DialogKind.FilterHelper); break;
            case ID_ADDPARAMETER: AddParameterToPlot(); break;
            case ID_WPDSELECT:
                {
                    var f = Shell.Dialogs.OpenFile("Select Multiplot Display File:", "WPD display file (*.wpd)|*.wpd", Settings.WpdFolder);
                    if (f != null && f.Length > 0)
                    {
                        YAxis = f[0];
                        Record("yaxis " + YAxis);
                        SaveParm(YAxis);
                        Plot();
                    }
                }
                break;

            // TimeSlice
            case ID_TIMESLICE: Shell.Dialogs.TimeSlice(this, false); break;
            case ID_MULTITIME: Shell.Dialogs.TimeSlice(this, true); break;
            case ID_TIMEALIGN: Shell.Dialogs.Show(this, DialogKind.TimeAlign); break;
            case ID_TIMEFREEZE:
                if (plot != null && RtLoaded)
                {
                    plot.TimeFreeze = plot.TimeFreeze != 0 ? 0 : -1;
                    Record("freeze " + OnOff(plot.TimeFreeze != 0));
                    Invalidate(plot);
                }
                break;

            // View
            case ID_VIEW_ASPLOT: Plot(); break;
            case ID_VIEW_ASTABULATION: Tabulate(); break;
            case ID_VIEW_INFO: Shell.Dialogs.Show(this, DialogKind.ParameterProperties); break;
            case ID_PRESENTATION_MODE: TogglePresentationMode(); break;
            case ID_MAX_PRES: Settings.MaxPresentation = !Settings.MaxPresentation; break;

            // Axis
            case ID_AXIS_MULTIY: SetMultiY(null); break;
            case ID_GROUPBY_INDEPENDENT: SetGroupBy(GroupBy.Independent); break;
            case ID_GROUPBY_NAME: SetGroupBy(GroupBy.Name); break;
            case ID_GROUPBY_UNITS: SetGroupBy(GroupBy.Units); break;
            case ID_GROUPBY_DESCRIPTION: SetGroupBy(GroupBy.Description); break;
            case ID_GROUPBY_UDEF: SetGroupBy(GroupBy.UserDefined); break;
            case ID_GROUPBY_MODE: GroupByDialog(); break;
            case ID_AXIS_ENDTOEND: SetEndToEnd(null); break;
            case ID_AXIS_LOGX: SetLog(false, null); break;
            case ID_AXIS_LOGY: SetLog(true, null); break;
            case ID_FIXXAUTOY: Settings.FixXAutoY = !Settings.FixXAutoY; Record("FIXXAUTOY " + OnOff(Settings.FixXAutoY)); break;
            case ID_AXIS_AUTOSCALEREDLINES: Settings.ScaleRedlines = !Settings.ScaleRedlines; Record("SCALEREDLINES " + OnOff(Settings.ScaleRedlines)); break;
            case ID_AXIS_FIXX: FixXDialog(); break;
            case ID_AXIS_FIXY: FixYDialog(); break;
            case ID_XAXIS_RESCALE: RescaleX(); break;
            case ID_YAXIS_RESCALE: RescaleY(); break;
            case ID_XYAXIS_RESCALE: RescaleXY(); break;
            case ID_UNDO_ZOOM: UndoZoom(); break;
            case ID_X_ZOOMIN: ZoomX(true); break;
            case ID_X_ZOOMOUT: ZoomX(false); break;
            case ID_AXIS_ZOOMIN: ZoomY(true); break;
            case ID_AXIS_ZOOMOUT: ZoomY(false); break;
            case ID_AXIS_SHLX: PanXCommand(false, Shell.IsShiftPressed() ? ShiftMode.Page : ShiftMode.Increment); break;
            case ID_AXIS_SHRX: PanXCommand(true, Shell.IsShiftPressed() ? ShiftMode.Page : ShiftMode.Increment); break;
            case ID_AXIS_SHUY: ShiftY(true); break;
            case ID_AXIS_SHDY: ShiftY(false); break;
            case ID_AXIS_ZANGLE: ZAngleDialog(); break;

            // Style
            case ID_STYLE_2DLINE: SetStyle(PlotStyle.Line2D); break;
            case ID_STYLE_2DSCATTER: SetStyle(PlotStyle.Scatter2D); break;
            case ID_STYLE_3DLINE: SetStyle(PlotStyle.Line3D); break;
            case ID_STYLE_3DAREA: SetStyle(PlotStyle.Area3D); break;
            case ID_STYLE_LOGIC: SetStyle(PlotStyle.Logic); break;
            case ID_STYLE_TABULAR: SetStyle(PlotStyle.Tabular); break;
            case ID_TRACE_ATTRIB:
                if (plot != null && Shell.Dialogs.TraceAttributes(this, plot, 0, out bool all))
                {
                    Invalidate(plot);
                    if (all)
                    {
                        foreach (var p in _plots.Skip(1))
                        {
                            for (int t = 0; t < plot.NCurves && t < p.NCurves; t++)
                            {
                                var s = plot.Curves[t]; var c = p.Curves[t];
                                c.LineSize = s.LineSize; c.LineStyle = s.LineStyle; c.ScatterPlot = s.ScatterPlot; c.ColorIndex = s.ColorIndex;
                                c.SymbolId = s.SymbolId; c.UseSymbols = s.UseSymbols; c.LogicTxt = s.LogicTxt;
                            }
                            Invalidate(p);
                        }
                    }
                }
                break;
            case ID_DATARATE: SetDataRate(null); break;
            case ID_STYLE_DENSITY_AVERAGE: SetDensity(0, null); break;
            case ID_STYLE_DENSITY_SAMPLE: SetDensity(1, null); break;
            case ID_STYLE_DENSITY_3DSEGMENTS: SetDensity(2, null); break;
            case ID_DATARATE_TABULARSTEP:
                {
                    var r = Shell.Dialogs.Input(new InputRequest("Time stepping increment:", Crt.Sprintf("%g", (double)Settings.TabStepRate), "Tabular Display Scroll",
                        new[] { ".001", ".010", ".100", "1.000", "10.000", "100.000" }));
                    if (r != null)
                    {
                        double v = Math.Abs(Crt.Atof(r.Text));
                        Settings.TabStepRate = (float)(v > 0 ? v : 1.0);
                        Record(Crt.Sprintf("TabStepRate %g", (double)Settings.TabStepRate));
                    }
                }
                break;
            case ID_LINE_COLOR: LineColorDialog(); break;
            case ID_LABEL_COLOR: SetColor(ColorIndex.Label, ApplyAllGlobal); break;
            case ID_TITLE_COLOR: SetColor(ColorIndex.Title, ApplyAllGlobal); break;
            case ID_GRID_COLOR: SetColor(ColorIndex.Grid, ApplyAllGlobal); break;
            case ID_BACKGROUND_COLOR: SetColor(ColorIndex.Background, ApplyAllGlobal); break;
            case ID_RESTORE_COLORS: RestoreColorTable(); break;
            case ID_LOADCOLORS: await LoadColorTableAsync(); break;
            case ID_2D_LINESZ: LineSizeDialog(false); break;
            case ID_3D_LINESZ: LineSizeDialog(true); break;
            case ID_GRIDSTYLE: GridStyleDialog(); break;
            case ID_MINORTICS: SetMinorTics(null); break;
            case ID_STYLE_BOLDFRAME: SetBoldFrame(null); break;
            case ID_STAIRSTEP: SetStairStep(null); break;
            case ID_STYLE_SYMBOLS: SetSymbols(null); break;
            case ID_STYLE_DENSITY_SYMBOL: SetSymbolDensity(null); break;
            case ID_SYM_SMALL: SetSymbolSize(-1); break;
            case ID_SYM_NORMAL: SetSymbolSize(0); break;
            case ID_SYM_LARGE: SetSymbolSize(1); break;
            case ID_STYLE_LEGEND: SetLegend(null); break;
            case ID_SET_LEGEND_COLUMNS: SetLegendColumns(null); break;
            case ID_STYLE_LEGRESERVE: ReserveLegendDialog(); break;
            case ID_MINGRIDOFFSET: GridOffsetDialog(); break;
            case ID_LEGENDSHIFT: SetLegendShift(null); break;
            case ID_USECURVECOLOR: SetLegendColor(null); break;

            // Nomenclature
            case ID_SETLFONT: Shell.Dialogs.Show(this, DialogKind.LabelFont); break;
            case ID_SETTFONT: Shell.Dialogs.Show(this, DialogKind.TitleFont); break;
            case ID_EDIT_FONTSZ1: SetFontSize(1); break;
            case ID_EDIT_FONTSZ2: SetFontSize(2); break;
            case ID_EDIT_FONTSZ3: SetFontSize(3); break;
            case ID_EDIT_FONTSZ4: SetFontSize(4); break;
            case ID_REDUCE_FONTSZ: StepFontSize(-1); break;
            case ID_ENLARGE_FONTSZ: StepFontSize(1); break;
            case ID_AXIS_XNOMEN: Shell.Dialogs.Show(this, DialogKind.XNomenclature); break;
            case ID_AXIS_YNOMEN: Shell.Dialogs.Show(this, DialogKind.YNomenclature); break;
            case ID_AXIS_XTITLE: ApplyTitle(false, true); break;
            case ID_AXIS_YTITLE: ApplyTitle(true, true); break;
            case ID_CLOCKTIME: SetClockTime(null); break;
            case ID_GMTFORMAT: SetGmtFormat(null); break;
            case ID_STYLE_SCALEGROUP: SetScaleGroup(null); break;
            case ID_SHOWVERSION: SetShowVersion(null); break;
            case ID_SHOWSCRIPTNAME: SetShowScript(null); break;
            case ID_SHOWUSERNAME: SetShowUserName(null); break;
            case ID_SHOWPAGENUMBER: SetShowPageNo(null); break;
            case ID_STYLE_TIMESTAMP: SetTimeStamp(null); break;
            case ID_STYLE_TESTINFO: SetTestInfo(null); break;
            case ID_SETUSERCAPTION: SetUserCaption(null); break;
            case ID_GROUPNAME: ApplyGroupName(null, true); break;

            // Markers
            case ID_DRAW_TEXT: case ID_DRAW_LINE: case ID_DRAW_BOX: case ID_DRAW_CIRCLE:
            case ID_DRAW_EVENT: case ID_DRAW_REDLINE: case ID_DRAW_IMAGE: case ID_DRAW_MARKERS:
                PendingMarkerType = id switch
                {
                    ID_DRAW_LINE => MarkerType.Line,
                    ID_DRAW_BOX => MarkerType.Box,
                    ID_DRAW_CIRCLE => MarkerType.Circle,
                    ID_DRAW_EVENT => MarkerType.Event,
                    ID_DRAW_REDLINE => MarkerType.Redline,
                    ID_DRAW_IMAGE => MarkerType.Image,
                    _ => MarkerType.Text,
                };
                Shell.Dialogs.Show(this, DialogKind.MarkerDraw);
                break;
            case ID_EDIT_MARKERS: Shell.Dialogs.Show(this, DialogKind.MarkerEdit); break;
            case ID_MARKERS_TOPMOST: SetMarkersTopmost(null); break;
            case ID_CLEAR_MARKERS: ClearAllMarkers(); break;
            case ID_MARKER_SAVE:
                {
                    string? fn = Shell.Dialogs.SaveFile("Save Marker File", "Multiplot Marker Files (*.markers)|*.markers|All Files (*.*)|*.*", null, Settings.ScriptDir.Split(';')[0]);
                    if (fn != null) { MarkersToFile(fn); Record("markerset save " + fn); }
                }
                break;
            case ID_MARKER_LOAD:
                {
                    var f = Shell.Dialogs.OpenFile("Load Marker File", "Multiplot Marker Files (*.markers)|*.markers|All Files (*.*)|*.*", Settings.ScriptDir.Split(';')[0]);
                    if (f != null && f.Length > 0) { MarkersFromFile(f[0]); Record("markerset load " + f[0]); }
                }
                break;

            // Options
            case ID_SETPATHS: Shell.Dialogs.Show(this, DialogKind.FilePaths); break;
            case ID_EXTAPPS: Shell.Dialogs.Show(this, DialogKind.ExternalApplications); break;
            case ID_SHOWGMT: Settings.ShowGmt = !Settings.ShowGmt; Record("INCLUDEGMT " + OnOff(Settings.ShowGmt)); break;
            case ID_EXTPRECISION:
                if (PrecisionDialog(Settings.DataPrecision) is int dp) { Settings.DataPrecision = dp; ApplyDataPrecision(true); }
                break;
            case ID_TIMEPRECISION:
                if (PrecisionDialog(Settings.TimePrecision) is int tp) { Settings.TimePrecision = tp; RecordPrecision(); }
                break;
            case ID_TAB_SGL_TIME: Settings.SharedTabTime = !Settings.SharedTabTime; Record("SHAREDTIME " + OnOff(Settings.SharedTabTime)); break;
            case ID_DATA_ALIGNMENT: Settings.AlignClosest = !Settings.AlignClosest; Record("AlignClosest " + OnOff(Settings.AlignClosest)); break;
            case ID_CALC_EVERY_INSTANCE: Settings.CalcEveryInstance = !Settings.CalcEveryInstance; Record("CalcEveryTime " + OnOff(Settings.CalcEveryInstance)); break;
            case ID_OPTIMIZED_DRAW: Settings.OptimizedDraw = Settings.OptimizedDraw != 0 ? 0 : 1; RefreshAllPlots(); Record("OptimizedDraw " + OnOff(Settings.OptimizedDraw != 0)); break;
            case ID_MEMDRAW: Settings.OffscreenDraw = Settings.OffscreenDraw != 0 ? 0 : 1; break;
            case ID_AVGRATE: Shell.Dialogs.Show(this, DialogKind.AverageRate); break;
            case ID_AVGTIMESTAMP: Shell.Dialogs.Show(this, DialogKind.AverageTimeStamp); break;
            case ID_RIGHTBUTTON: Shell.Dialogs.Show(this, DialogKind.RightButton); break;
            case ID_SHOW_SYS_LOAD: SetSystemLoad(null); break;
            case ID_SHOWCOORD: Settings.ShowCoord = !Settings.ShowCoord; Record("SHOWCOORD " + OnOff(Settings.ShowCoord)); break;
            case ID_EDIT_STATUSBAR: SetStatusBarVisible(null); break;
            case ID_EDIT_TOOLBAR: SetToolbarVisible(null); break;
            case ID_CUSTOMIZE_TOOLBAR: Shell.Dialogs.Show(this, DialogKind.ToolbarEdit); break;
            case ID_VIEW_MAX: Settings.PlotMax = !Settings.PlotMax; break;
            case ID_LOADVARS: Settings.LoadSymbols = !Settings.LoadSymbols; break;
            case ID_MONITORS: Shell.Dialogs.Show(this, DialogKind.Monitors); break;
            case ID_POS_SAVE: Settings.AutoSave = !Settings.AutoSave; break;
            case ID_EXIT_SAFETY: Settings.ExitSafety = !Settings.ExitSafety; break;
            case ID_STYLE_TITLE: Settings.ContinueTitle = !Settings.ContinueTitle; Record((Settings.ContinueTitle ? "enable" : "disable") + " titles"); break;
            case ID_STYLE_SUBTITLE: Settings.ContinueSubTitle = !Settings.ContinueSubTitle; Record((Settings.ContinueSubTitle ? "enable" : "disable") + " subtitles"); break;
            case ID_USEMARKERS: Settings.UseMarkers = !Settings.UseMarkers; Record(Settings.UseMarkers ? "enable markers" : "disable markers"); break;
            case ID_AUTOGEN: Settings.AllowAutoLabel = !Settings.AllowAutoLabel; Record("autolabel " + OnOff(Settings.AllowAutoLabel)); break;
            case ID_APPLY_ALL: SetApplyAll(null); break;
            case ID_CAPTIONDISABLE: SetNoCaptionBar(null); break;
            case ID_WINDOWCAPTION: CaptionSourceDialog(); break;
            case ID_INCLUDE_TOOLBARS: Settings.IncludeToolbar = !Settings.IncludeToolbar; Record("useINItoolbar " + OnOff(Settings.IncludeToolbar)); break;
            case ID_SAVE_SETTINGS: SaveSettings(); break;
            case ID_LOAD_SETTINGS: LoadSettingsDialog(); break;
            case ID_IMPORT_WINPLOT: ImportWinPlotSettingsDialog(); break;

            // User
            case ID_USERSCHOICE1: case ID_USERSCHOICE2: case ID_USERSCHOICE3: case ID_USERSCHOICE4: case ID_USERSCHOICE5:
                await UserButtonAsync(id - ID_USERSCHOICE1);
                break;
            case ID_USERASSIGN: Shell.Dialogs.Show(this, DialogKind.UserButtonAssign); break;

            // Data
            case ID_REBUILD_RATE: ToggleRebuildRate(); break;
            case ID_EXTRAPOLATE: ToggleExtrapolate(); break;
            case ID_64_BIT_TIMING: Set64BitTime(null); break;
            case ID_DROPOUT_IGNORE: SetDropoutIgnore(null); break;
            case ID_DROPOUT_MARKER: SetDropoutMarker(null); break;
            case ID_REFRESH: if (!Refreshing) RefreshNow(); break;
            case ID_REFRESH_TIMER: RefreshIntervalDialog(); break;
            case ID_REFRESHTOGGLE: ToggleRefresh(); break;
            case ID_TIMESLIDE: SetTimeSlide(null); break;
            case ID_IGNORE_STATIC: SetIgnoreStatic(null); break;
            case ID_LOADONCE: Settings.FirstOccurrence = !Settings.FirstOccurrence; Record("FirstOccurrence " + OnOff(Settings.FirstOccurrence)); break;
            case ID_AUTORUN: Shell.Dialogs.Show(this, DialogKind.AutoRun); break;
            case ID_AUTOCOMPACT: AutoCompactDialog(); break;
            case ID_AUTODISCARD: AutoDiscardDialog(); break;
            case ID_CERTSTATUS: SetStreamCert(null); break;
            case ID_TAB_HISTORY: SetTabHistory(null); break;
            case ID_DEF_TAB_TIME: SetDefTabTime(null); break;
            case ID_MANAGE_PLUGINS: Shell.Dialogs.Show(this, DialogKind.ManagePlugins); break;

            // Window
            case ID_WINDOW_CASCADE: Arrange(WindowArrange.Cascade); break;
            case ID_WINDOW_TILE_HORZ: Arrange(WindowArrange.TileHorizontal); break;
            case ID_WINDOW_TILE_VERT: Arrange(WindowArrange.TileVertical); break;
            case ID_WINDOW_TILE_GROUP: Arrange(WindowArrange.TileGroup); break;
            case ID_WINDOW_STACK_HORIZONTAL: Arrange(WindowArrange.Stack); break;
            case ID_WINDOW_MAXIMIZE: Shell.Arrange(WindowArrange.MaximizeActive, null); break;
            case ID_WINDOW_MINIMIZE: Shell.Arrange(WindowArrange.MinimizeActive, null); break;
            case ID_MOVE_WINDOW: break;
            case ID_WINDOW_REFRESH: RefreshAllPlots(); break;
            case ID_PREV_WINDOW: PreviousWindow(); break;
            case ID_NEXT_WINDOW: NextWindow(); break;
            case ID_WINDOWLIST: Shell.Dialogs.Show(this, DialogKind.WindowList); break;
            case ID_AUTOTAB: AutoTabDialog(false); break;
            case ID_AUTOTAB_GROUP: AutoTabDialog(true); break;
            case ID_WINDOW_ONTOP: KeepOnTop = !KeepOnTop; Shell.RefreshCommands(); break;
            case ID_WINDOW_CLOSE: if (plot != null) RemovePlot(plot); break;
            case ID_WINDOW_CLOSEALL: CloseAllPlots(); break;
            case ID_WINDOW_BRINGHOME: case ID_WIN_POS: Shell.Dialogs.Show(this, DialogKind.WindowPosition); break;

            // Help
            case ID_APP_ABOUT: Shell.Dialogs.Show(this, DialogKind.About); break;

            // misc accelerators
            case ID_SAVEJPEG: AutoSaveJpeg(); break;
            case ID_TAB_BACKGROUND: SetTabBackground(true); break;
            case ID_CLEARREMEMBERED: Array.Fill(PastParms, ""); Record("ClearList"); break;
            case ID_BREAKOUT: if (ScriptRun > 0) ScriptRun = RunState.Aborting; break;

            default:
                if (id >= ID_RECENT0 && id <= ID_RECENT9)
                {
                    int n = id - ID_RECENT0;
                    if (n < RecentFiles.Count)
                    {
                        if (LoadFileGroup(RecentFiles[n]) == 0) Record("OPEN " + RecentFiles[n].Replace(";", "\\;"));
                    }
                    break;
                }
                return false;
        }
        Shell.RefreshCommands();
        return true;
    }

    /// <summary>Marker type chosen by Markers → Draw (consumed by the plot window's marker placement).</summary>
    public int PendingMarkerType = MarkerType.Text;

    /// <summary>Runs a User's Choice entry.</summary>
    public async Task UserButtonAsync(int n)
    {
        if (n < 0 || n >= AppSettings.NUserButtons) return;
        string cmd = Settings.UserButtons[n].Command;
        if (cmd.Length == 0)
        {
            UserButtonToAssign = n;
            Shell.Dialogs.Show(this, DialogKind.UserButtonAssign);
            return;
        }
        await Scripts.ExecuteInternalAsync(cmd);
    }

    /// <summary>User button being assigned (dialog input).</summary>
    public int UserButtonToAssign;

    /// <summary>Check state of a command for the active plot or the defaults (null = not a toggle).</summary>
    public bool? IsChecked(int id)
    {
        var c = ActivePlot?.Config ?? PlotConfig;
        return id switch
        {
            ID_SCRIPTING_PAUSEANYCOMMAND => Settings.PauseMode == PauseMode.NextCommand,
            ID_SCRIPT_PROMPT => Settings.ScriptPrompt,
            ID_SCRIPTLOG => LogOpen,
            ID_USEPLACEHOLDER => Settings.UsePlaceholder,
            ID_SCRIPT_EMBEDDED => Settings.EmbeddedScript,
            ID_SCRIPT_RECORD => Recording,
            ID_TIMEFREEZE => ActivePlot != null && ActivePlot.TimeFreeze != 0,
            ID_PRESENTATION_MODE => PresentationMode,
            ID_MAX_PRES => Settings.MaxPresentation,
            ID_AXIS_MULTIY => c.Multi,
            ID_GROUPBY_INDEPENDENT => Settings.MultiGroupBy == GroupBy.Independent,
            ID_GROUPBY_NAME => Settings.MultiGroupBy == GroupBy.Name,
            ID_GROUPBY_UNITS => Settings.MultiGroupBy == GroupBy.Units,
            ID_GROUPBY_DESCRIPTION => Settings.MultiGroupBy == GroupBy.Description,
            ID_GROUPBY_UDEF => Settings.MultiGroupBy == GroupBy.UserDefined,
            ID_AXIS_ENDTOEND => c.EndToEnd,
            ID_AXIS_LOGX => c.LogX,
            ID_AXIS_LOGY => c.LogY,
            ID_FIXXAUTOY => Settings.FixXAutoY,
            ID_AXIS_AUTOSCALEREDLINES => Settings.ScaleRedlines,
            ID_STYLE_2DLINE => c.Style == PlotStyle.Line2D,
            ID_STYLE_2DSCATTER => c.Style == PlotStyle.Scatter2D,
            ID_STYLE_3DLINE => c.Style == PlotStyle.Line3D,
            ID_STYLE_3DAREA => c.Style == PlotStyle.Area3D,
            ID_STYLE_LOGIC => c.Style == PlotStyle.Logic,
            ID_STYLE_TABULAR => c.Style == PlotStyle.Tabular,
            ID_GRIDSTYLE => c.GridLines,
            ID_MINORTICS => c.MinorTics,
            ID_STYLE_BOLDFRAME => c.BoldFrame,
            ID_STAIRSTEP => c.StairStep,
            ID_STYLE_SYMBOLS => c.Symbols,
            ID_SYM_SMALL => c.SymbolSize < 0,
            ID_SYM_NORMAL => c.SymbolSize == 0,
            ID_SYM_LARGE => c.SymbolSize > 0,
            ID_STYLE_LEGEND => c.Legend,
            ID_LEGENDSHIFT => c.LegendShift,
            ID_USECURVECOLOR => c.LegendColor,
            ID_EDIT_FONTSZ1 => c.FontSize == 1,
            ID_EDIT_FONTSZ2 => c.FontSize == 2,
            ID_EDIT_FONTSZ3 => c.FontSize == 3,
            ID_EDIT_FONTSZ4 => c.FontSize == 4,
            ID_CLOCKTIME => c.ClockTime,
            ID_STYLE_SCALEGROUP => c.GroupId,
            ID_SHOWVERSION => c.ShowVersion,
            ID_SHOWSCRIPTNAME => c.ShowScript,
            ID_SHOWUSERNAME => c.ShowUserName,
            ID_SHOWPAGENUMBER => c.ShowPageNo,
            ID_STYLE_TIMESTAMP => c.TimeStamp,
            ID_STYLE_TESTINFO => c.TestInfo,
            ID_MARKERS_TOPMOST => c.MarkersTopmost,
            ID_SHOWGMT => Settings.ShowGmt,
            ID_TAB_SGL_TIME => Settings.SharedTabTime,
            ID_DATA_ALIGNMENT => Settings.AlignClosest,
            ID_CALC_EVERY_INSTANCE => Settings.CalcEveryInstance,
            ID_OPTIMIZED_DRAW => Settings.OptimizedDraw != 0,
            ID_MEMDRAW => Settings.OffscreenDraw != 0,
            ID_SHOW_SYS_LOAD => Settings.ShowSysLoad,
            ID_SHOWCOORD => Settings.ShowCoord,
            ID_EDIT_STATUSBAR => Settings.ShowStatusBar,
            ID_EDIT_TOOLBAR => Settings.ShowToolBar,
            ID_VIEW_MAX => Settings.PlotMax,
            ID_LOADVARS => Settings.LoadSymbols,
            ID_POS_SAVE => Settings.AutoSave,
            ID_EXIT_SAFETY => Settings.ExitSafety,
            ID_STYLE_TITLE => Settings.ContinueTitle,
            ID_STYLE_SUBTITLE => Settings.ContinueSubTitle,
            ID_USEMARKERS => Settings.UseMarkers,
            ID_AUTOGEN => Settings.AllowAutoLabel,
            ID_APPLY_ALL => Settings.ApplyAll,
            ID_CAPTIONDISABLE => Settings.NoCaptionBar != 0,
            ID_INCLUDE_TOOLBARS => Settings.IncludeToolbar,
            ID_REBUILD_RATE => Settings.RebuildRate,
            ID_EXTRAPOLATE => Settings.Extrapolate,
            ID_64_BIT_TIMING => c.Use64BitTime,
            ID_DROPOUT_IGNORE => c.DropoutIgnore,
            ID_DROPOUT_MARKER => c.DropoutMarker,
            ID_REFRESH_TIMER => AutoRefresh != 0,
            ID_TIMESLIDE => ActivePlot?.TimeSlide ?? Settings.TimeSlide,
            ID_IGNORE_STATIC => ActivePlot?.IgnoreStatic ?? Settings.IgnoreStatic,
            ID_LOADONCE => Settings.FirstOccurrence,
            ID_CERTSTATUS => Settings.StreamCert,
            ID_TAB_HISTORY => Settings.TabHistory,
            ID_DEF_TAB_TIME => c.DefTabTime,
            ID_SNAPTOGRID => SnapToGridValue,
            ID_TAB_GRID => LayoutGridSpacingValue > 0,
            ID_WINDOW_ONTOP => KeepOnTop,
            ID_AUTOTAB => AutoTab != 0,
            _ => null,
        };
    }

    /// <summary>Display label overrides (record menu text, user buttons).</summary>
    public string? CommandLabel(int id)
    {
        if (id == ID_SCRIPT_RECORD && Recording) return "&Stop Recording [" + ScriptFile + "]";
        if (id >= ID_USERSCHOICE1 && id <= ID_USERSCHOICE5)
        {
            var b = Settings.UserButtons[id - ID_USERSCHOICE1];
            return b.Label.Length > 0 ? b.Label : null;
        }
        return null;
    }
}
