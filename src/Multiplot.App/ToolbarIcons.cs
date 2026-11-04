using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using Shapes = System.Windows.Shapes;
using static Multiplot.Core.CommandIds;

namespace Multiplot.App;

/// <summary>Windows 11 style toolbar icons: Segoe Fluent Icons glyphs keyed by command id (Segoe MDL2 Assets on Windows 10,
/// where glyphs it lacks fall back to the toolbar bitmap).
/// Settings that the toolbar bitmaps spell out as words get a short text label instead; commands without a fitting
/// standard glyph (plot styles, dropouts, ...) are drawn as line art in the same style.</summary>
public static class ToolbarIcons
{
    private static readonly (FontFamily Family, GlyphTypeface Face)? Glyphs = LoadGlyphFont();
    private static readonly FontFamily Badges = new("Segoe UI");

    /// <summary>Glyph and optional axis/variant badge (e.g. "X", "Y") drawn at the lower right; a '\0' glyph shows the badge as a text label.</summary>
    private readonly record struct Icon(char Glyph, string? Badge = null);

    private static Icon Text(string label) => new('\0', label);

    private static readonly Dictionary<int, Icon> Map = new()
    {
        // File
        [ID_FILE_NEW] = new('\uE838'),          // FolderOpen
        [ID_FILE_OPEN] = new('\uEA1A'),         // PageAdd
        [ID_IMPORT_FILE] = new('\uE8B5'),       // Import
        [ID_CLOSELAST] = new('\uE711'),         // Cancel
        [ID_DBFCLOSE] = new('\uEA99'),          // Broom
        [ID_FILELIST] = new('\uEC50'),          // FileExplorer
        [ID_DBFPRINT] = new('\uE749'),          // Print
        [ID_DBFPRINTSETUP] = new('\uE956'),     // PrintfaxPrinterFile
        // Edit
        [ID_COPY_AS_BMP] = new('\uEB9F'),       // Photo2
        [ID_COPYCLIPBOARD] = new('\uE8C8'),     // Copy
        [ID_EXPORTCB] = new('\uF0E3'),          // ClipboardList
        [ID_COPY_AS_PIC] = new('\uEE71'),       // ImageExport
        [ID_EXPORT] = new('\uEDE1'),            // Export
        [ID_SAVE_WPD] = new('\uE74E'),          // Save
        [ID_COPY_AS_SUN] = new('\uE792'),       // SaveAs
        [ID_IMPORTCB] = new('\uE77F'),          // Paste
        [ID_VIEW_EXPORTCURRENT] = new('\uE7AC'), // OpenWith
        [ID_VAR_EDIT] = new('\uE943'),          // Code
        [ID_EMFASPECT] = new('\uE799'),         // AspectRatio
        [ID_TAB_GRID] = new('\uE80A'),          // Grid
        [ID_SNAPTOGRID] = new('\uECA5'),        // Tiles
        // Script
        [ID_SCRIPT_RECORD] = new('\uE7C8'),     // Record
        [ID_SCRIPT_RUN] = new('\uE768'),        // Play
        [ID_SCRIPT_NEXT] = new('\uE893'),       // Next
        [ID_SCRIPT_ABORT] = new('\uE71A'),      // Stop
        [ID_SCRIPT_PAUSE] = new('\uE769'),      // Pause
        [ID_SCRIPT_EDIT] = new('\uE70F'),       // Edit
        [ID_SCRIPTING_PAUSEANYCOMMAND] = new('\uF2D9'), // CirclePause
        [ID_SCRIPT_PROMPT] = new('\uE8BD'),     // Message
        [ID_SCRIPTLOG] = new('\uE7BC'),         // ReadingList
        [ID_USEPLACEHOLDER] = new('\uF2AE'),    // CubeWithDashedOutline
        [ID_EXT_CMD_POLL] = new('\uE756'),      // CommandPrompt
        [ID_SCRIPT_EMBEDDED] = new('\uE723'),   // Attach
        // Parameter
        [ID_PARMSELECT] = new('\uE762'),        // MultiSelect
        [ID_FAVORITES] = new('\uE734'),         // FavoriteStar
        [ID_FILTERS] = new('\uE71C'),           // Filter
        [ID_ADDPARAMETER] = new('\uE710'),      // Add
        [ID_WPDSELECT] = new('\uE8A5'),         // Document
        // Timeslice
        [ID_TIMESLICE] = new('\uE917'),         // Clock
        [ID_MULTITIME] = new('\uE823'),         // Recent
        [ID_TIMEALIGN] = new('\uE916'),         // Stopwatch
        [ID_TIMEFREEZE] = new('\uE9CA'),        // Frigid
        // View
        [ID_VIEW_ASPLOT] = new('\uE9D2'),       // AreaChart
        [ID_VIEW_ASTABULATION] = new('\uF0E2'), // GridView
        [ID_VIEW_INFO] = new('\uE946'),         // Info
        [ID_PRESENTATION_MODE] = new('\uF71C'), // StartPresenting
        [ID_MAX_PRES] = new('\uE740'),          // FullScreen
        // Axis
        [ID_AXIS_MULTIY] = new('\uE9E9'),       // Equalizer
        [ID_GROUPBY_MODE] = new('\uE902'),      // Group
        [ID_AXIS_FIXX] = new('\uE72E', "X"),    // Lock
        [ID_AXIS_FIXY] = new('\uE72E', "Y"),
        [ID_AXIS_LOGX] = Text("logX"),
        [ID_AXIS_LOGY] = Text("logY"),
        [ID_XAXIS_RESCALE] = new('\uE9A6', "X"), // FitPage
        [ID_YAXIS_RESCALE] = new('\uE9A6', "Y"),
        [ID_XYAXIS_RESCALE] = new('\uE9A6'),
        [ID_UNDO_ZOOM] = new('\uE7A7'),         // Undo
        [ID_AXIS_ZANGLE] = new('\uE7AD'),       // Rotate
        // Pan/Zoom
        [ID_X_ZOOMIN] = new('\uE8A3', "X"),     // ZoomIn
        [ID_X_ZOOMOUT] = new('\uE71F', "X"),    // ZoomOut
        [ID_AXIS_ZOOMIN] = new('\uE8A3', "Y"),
        [ID_AXIS_ZOOMOUT] = new('\uE71F', "Y"),
        [ID_AXIS_SHLX] = new('\uE72B'),         // Back
        [ID_AXIS_SHRX] = new('\uE72A'),         // Forward
        [ID_AXIS_SHUY] = new('\uE74A'),         // Up
        [ID_AXIS_SHDY] = new('\uE74B'),         // Down
        // Style
        [ID_STYLE_TABULAR] = new('\uF0E2'),     // GridView
        [ID_TRACE_ATTRIB] = new('\uE713'),      // Settings
        [ID_DATARATE] = new('\uE9D9'),          // Diagnostic
        [ID_STYLE_DENSITY_AVERAGE] = Text("Avg"),
        [ID_STYLE_DENSITY_SAMPLE] = Text("Smp"),
        [ID_STYLE_DENSITY_3DSEGMENTS] = Text("3D"),
        [ID_LINE_COLOR] = new('\uE790'),        // Color
        [ID_LABEL_COLOR] = new('\uE8D3'),       // FontColor
        [ID_TITLE_COLOR] = new('\uE8D3', "T"),
        [ID_GRID_COLOR] = new('\uE790', "G"),   // Color
        [ID_BACKGROUND_COLOR] = new('\uEF1F'),  // BackgroundToggle
        [ID_RESTORE_COLORS] = new('\uE777'),    // UpdateRestore
        [ID_2D_LINESZ] = new('\uEDA8', "2"),    // BrushSize
        [ID_3D_LINESZ] = new('\uEDA8', "3"),
        // Style2
        [ID_GRIDSTYLE] = new('\uE80A'),         // Grid
        [ID_MINORTICS] = new('\uED5E'),         // Ruler
        [ID_STYLE_BOLDFRAME] = new('\uE8DD'),   // Bold
        [ID_STYLE_SYMBOLS] = new('\uE8C1'),     // Characters
        [ID_STYLE_DENSITY_SYMBOL] = new('\uE8C1', "#"),
        [ID_SYM_SMALL] = new('\uEA3A', "S"),      // CircleRing
        [ID_SYM_NORMAL] = new('\uEA3A', "M"),
        [ID_SYM_LARGE] = new('\uEA3A', "L"),
        [ID_STYLE_LEGEND] = new('\uE8FD'),      // BulletedList
        [ID_SET_LEGEND_COLUMNS] = new('\uE89A'), // TwoPage
        [ID_LEGENDSHIFT] = new('\uE8E4'),       // AlignLeft
        // Nomenclature
        [ID_SETLFONT] = new('\uE8D2'),          // Font
        [ID_SETTFONT] = new('\uE8D2', "T"),
        [ID_REDUCE_FONTSZ] = new('\uE8E7'),     // FontDecrease
        [ID_ENLARGE_FONTSZ] = new('\uE8E8'),    // FontIncrease
        [ID_AXIS_XNOMEN] = new('\uE8EC', "X"),  // Tag
        [ID_AXIS_YNOMEN] = new('\uE8EC', "Y"),
        [ID_AXIS_XTITLE] = new('\uE8AC'),       // Rename
        [ID_AXIS_YTITLE] = new('\uE8AC', "Y"),
        [ID_CLOCKTIME] = new('\uEC92'),         // DateTime
        [ID_GMTFORMAT] = new('\uE775'),         // TimeLanguage
        [ID_STYLE_SCALEGROUP] = new('\uE902'),  // Group
        [ID_SHOWVERSION] = new('\uECAA'),       // AppIconDefault
        [ID_SHOWSCRIPTNAME] = new('\uE943'),    // Code
        [ID_SHOWUSERNAME] = new('\uE77B'),      // Contact
        [ID_SHOWPAGENUMBER] = new('\uE7C3'),    // Page
        [ID_STYLE_TIMESTAMP] = new('\uE787'),   // Calendar
        [ID_STYLE_TESTINFO] = new('\uE9F9'),    // ReportDocument
        [ID_SETUSERCAPTION] = new('\uE932'),    // Label
        [ID_GROUPNAME] = new('\uE8EC'),         // Tag
        // Markers
        [ID_DRAW_TEXT] = new('\uF683'),         // TextSelect
        [ID_DRAW_LINE] = new('\uEC87'),         // Draw
        [ID_DRAW_BOX] = new('\uEF20'),          // Marquee
        [ID_DRAW_CIRCLE] = new('\uEA3A'),       // CircleRing
        [ID_DRAW_EVENT] = new('\uE7C1'),        // Flag
        [ID_DRAW_IMAGE] = new('\uE91B'),        // Photo
        [ID_EDIT_MARKERS] = new('\uED64'),      // Marker
        [ID_CLEAR_MARKERS] = new('\uED62'),     // ClearAllInk
        [ID_MARKER_SAVE] = new('\uE74E'),       // Save
        [ID_MARKER_LOAD] = new('\uE8E5'),       // OpenFile
        // Options
        [ID_SETPATHS] = new('\uE8B7'),          // Folder
        [ID_EXTAPPS] = new('\uE71D'),           // AllApps
        [ID_SHOWGMT] = new('\uE774'),           // Globe
        [ID_EXTPRECISION] = Text(".00"),
        [ID_TIMEPRECISION] = new('\uE917', ".0"),   // Clock
        [ID_DATA_ALIGNMENT] = new('\uE8EF', "~"), // Calculator
        [ID_CALC_EVERY_INSTANCE] = new('\uE8EF'), // Calculator
        [ID_TAB_SGL_TIME] = new('\uE917', "1"),    // Clock
        [ID_OPTIMIZED_DRAW] = new('\uEC4A'),    // SpeedHigh
        [ID_MEMDRAW] = new('\uEEA0'),           // RAM
        [ID_AVGRATE] = Text("Hz"),
        [ID_AVGTIMESTAMP] = new('\uE917', "A"),   // Clock
        [ID_RIGHTBUTTON] = new('\uE962'),       // Mouse
        [ID_SHOWCOORD] = new('\uF272'),         // Bullseye
        [ID_CUSTOMIZE_TOOLBAR] = new('\uE771'), // Personalize
        [ID_VIEW_MAX] = new('\uE922'),          // ChromeMaximize
        [ID_MONITORS] = new('\uE7F4'),          // TVMonitor
        [ID_POS_SAVE] = new('\uE78C'),          // SaveLocal
        [ID_EXIT_SAFETY] = new('\uEA18'),       // Shield
        [ID_SHOW_SYS_LOAD] = new('\uEEA1'),     // CPU
        // Options2
        [ID_APPLY_ALL] = new('\uE8B3'),         // SelectAll
        [ID_STYLE_TITLE] = new('\uE8AC', "T"),    // Rename
        [ID_STYLE_SUBTITLE] = new('\uE8AC', "S"),
        [ID_USEMARKERS] = new('\uED64', "+"),     // Marker
        [ID_AUTOGEN] = new('\uE8AC', "A"),        // Rename
        [ID_PRINTLAND] = new('\uEF6B'),         // LandscapeOrientation
        [ID_PRINTMARGINS] = new('\uF578'),      // PageMarginLandscapeNormal
        [ID_COLORPRINT] = new('\uE749', "C"),     // Print
        [ID_SAVE_SETTINGS] = new('\uE74E'),     // Save
        [ID_LOAD_SETTINGS] = new('\uE8E5'),     // OpenFile
        // User
        [ID_USERASSIGN] = new('\uE71B'),        // Link
        // Window
        [ID_WINDOW_CASCADE] = new('\uEB91'),    // TaskViewExpanded
        [ID_WINDOW_TILE_HORZ] = new('\uED6E'),  // Snaptop
        [ID_WINDOW_TILE_VERT] = new('\uED71'),  // Snapleft
        [ID_WINDOW_TILE_GROUP] = new('\uEE4F'), // Snap4
        [ID_WINDOW_MAXIMIZE] = new('\uE922'),   // ChromeMaximize
        [ID_WINDOW_MINIMIZE] = new('\uE921'),   // ChromeMinimize
        [ID_WINDOW_REFRESH] = new('\uE72C'),    // Refresh
        [ID_PREV_WINDOW] = new('\uE892'),       // Previous
        [ID_NEXT_WINDOW] = new('\uE893'),       // Next
        [ID_WINDOWLIST] = new('\uE7C4'),        // TaskView
        [ID_AUTOTAB] = new('\uE8EE'),           // RepeatAll
        [ID_AUTOTAB_GROUP] = new('\uE8EE', "G"),
        [ID_WINDOW_ONTOP] = new('\uE718'),      // Pin
        [ID_WINDOW_CLOSE] = new('\uE8BB'),      // ChromeClose
        [ID_WINDOW_CLOSEALL] = new('\uED6A'),   // CloseForeground
        // Data
        [ID_QUERY] = new('\uE721'),             // Search
        [ID_MANAGE_PLUGINS] = new('\uEA86'),    // Puzzle
        [ID_64_BIT_TIMING] = Text("64"),
        // Realtime
        [ID_REFRESH] = new('\uE72C'),           // Refresh
        [ID_REFRESH_TIMER] = new('\uE895'),     // Sync
        [ID_AUTORUN] = new('\uE945'),           // LightningBolt
        [ID_AUTODISCARD] = new('\uE74D'),       // Delete
        [ID_TAB_HISTORY] = new('\uE81C'),       // History
        [ID_DEF_TAB_TIME] = new('\uE787', "T"),   // Calendar
        [ID_LOADONCE] = Text("1st"),
        [ID_CERTSTATUS] = new('\uEB95'),        // Certificate
        // Help
        [ID_HELP_CONTENTS] = new('\uE897'),     // Help
        [ID_WIN_POS] = new('\uE7C2'),           // Move
        [ID_APP_ABOUT] = new('\uE946'),         // Info
        [ID_APP_EXIT] = new('\uF3B1'),          // SignOut
    };

    /// <summary>One layer of a drawn icon: path data in a 20x20 box, stroked (or filled) with the inherited foreground
    /// unless a color is given. Axis-aligned 1 px strokes sit on .5 coordinates so they stay crisp at 100 %.</summary>
    private readonly record struct Layer(string Data, bool Fill = false, double Opacity = 1, Color? Color = null, double Thickness = 1);

    private static Layer S(string data, double opacity = 1) => new(data, false, opacity);
    private static Layer F(string data, double opacity = 1) => new(data, true, opacity);
    private static Layer SC(string data, Color color, double thickness = 1) => new(data, false, 1, color, thickness);
    private static Layer FC(string data, Color color) => new(data, true, 1, color);

    private static readonly Color Red = Color.FromRgb(0xC4, 0x2B, 0x1C);
    private static readonly Color Green = Color.FromRgb(0x10, 0x7C, 0x10);
    private static readonly Color Blue = Color.FromRgb(0x00, 0x5F, 0xB8);

    /// <summary>X/Y axes of a 2D chart.</summary>
    private const string Axes = "M3.5,3 V16.5 H17.5";
    /// <summary>Axes of a 3D chart: Y axis over an oblique floor that recedes toward the viewer.</summary>
    private const string Axes3D = "M3.5,2.5 V12.5 H14.5 L17.5,16.5 H6.5 L3.5,12.5";

    private static string Rect(double l, double t, double r, double b, double rad = 1) =>
        FormattableString.Invariant($"M{l + rad},{t} H{r - rad} A{rad},{rad} 0 0 1 {r},{t + rad} V{b - rad} A{rad},{rad} 0 0 1 {r - rad},{b} H{l + rad} A{rad},{rad} 0 0 1 {l},{b - rad} V{t + rad} A{rad},{rad} 0 0 1 {l + rad},{t} Z");

    private static string Window => Rect(2.5, 3.5, 17.5, 16.5);

    private static string Dots(double r, params (double X, double Y)[] at) =>
        string.Concat(at.Select(p => FormattableString.Invariant($"M{p.X - r},{p.Y} A{r},{r} 0 1 0 {p.X + r},{p.Y} A{r},{r} 0 1 0 {p.X - r},{p.Y} Z ")));

    private static readonly Dictionary<int, Layer[]> Drawn = new()
    {
        // Axis
        [ID_AXIS_ENDTOEND] = new[] { S(Axes), S("M5.5,14 L7.5,10.5 L9,12"), S("M12,9 L14,5.5 L16.5,7"), S("M10.5,4 v2 M10.5,8 v2 M10.5,12 v2", 0.6) },
        [ID_FIXXAUTOY] = new[] { S(Axes), S("M6,14 H16 M7.5,12.5 L6,14 L7.5,15.5 M14.5,12.5 L16,14 L14.5,15.5"), S("M11,3.5 V10.5 M9.5,5 L11,3.5 L12.5,5 M9.5,9 L11,10.5 L12.5,9") },
        [ID_AXIS_AUTOSCALEREDLINES] = new[] { S(Axes), SC("M5.5,5.5 h2.5 M10,5.5 h2.5 M14.5,5.5 h2.5", Red, 1.5), S("M5,14 C7.5,8 9.5,8 11.5,11 S15,13.5 17,9") },
        // Style
        [ID_STYLE_2DLINE] = new[] { S(Axes), S("M5.5,13.5 L8.5,9 L11.5,11 L16.5,5") },
        [ID_STYLE_2DSCATTER] = new[] { S(Axes), F(Dots(1.25, (6.5, 12.5), (8.5, 8.5), (11, 11.5), (13, 7), (15.5, 9.5), (15.5, 4.5))) },
        [ID_STYLE_3DLINE] = new[] { S(Axes3D), S("M5,10 L7.5,6 L10,8 L13.5,4", 0.55), S("M8,14 L10.5,10 L13,12 L16.5,8") },
        [ID_STYLE_3DAREA] = new[] { S(Axes3D), F("M5,12.5 V10 L7.5,6 L10,8 L13.5,4 V12.5 Z", 0.35), F("M8,16.5 V14 L10.5,10 L13,12 L16.5,8 V16.5 Z") },
        [ID_STYLE_LOGIC] = new[] { S("M2.5,8.5 H5.5 V4.5 H9.5 V8.5 H12.5 V4.5 H17.5"), S("M2.5,15.5 H4.5 V11.5 H7.5 V15.5 H11.5 V11.5 H14.5 V15.5 H17.5") },
        // Style2
        [ID_STAIRSTEP] = new[] { S(Axes), S("M5,14.5 H8.5 V11.5 H11.5 V8.5 H14.5 V5.5 H17") },
        [ID_STYLE_LEGRESERVE] = new[] { S(Rect(8.5, 3.5, 17.5, 16.5)), S("M10.5,13.5 L12.5,9.5 L14,11 L16,6.5"), S("M2.5,6.5 H6 M2.5,9.5 H6 M2.5,12.5 H6") },
        [ID_MINGRIDOFFSET] = new[] { S(Rect(9.5, 3.5, 17.5, 16.5)), S("M2.5,5.5 V14.5"), S("M2.5,10 H7.5 M6,8.5 L7.5,10 L6,11.5") },
        [ID_USECURVECOLOR] = new[] { SC("M2.5,6.5 H6.5", Red, 2), SC("M2.5,10.5 H6.5", Blue, 2), SC("M2.5,14.5 H6.5", Green, 2), S("M9,6.5 H17.5 M9,10.5 H15.5 M9,14.5 H16.5") },
        // Markers
        [ID_DRAW_REDLINE] = new[] { SC("M2.5,16.5 H17.5", Red, 1.5), S("M6,12 L13,5 L15,7 L8,14 L5.5,14.5 Z M11.5,6.5 L13.5,8.5") },
        [ID_MARKERS_TOPMOST] = new[] { S(Rect(3.5, 3.5, 11.5, 11.5), 0.55), F(Rect(8, 8, 17, 17)) },
        // Options
        [ID_EDIT_STATUSBAR] = new[] { S(Window), F("M4.5,13 H15.5 V14.5 H4.5 Z") },
        [ID_EDIT_TOOLBAR] = new[] { S(Window), S("M2.5,8.5 H17.5"), F("M4.5,5 h2 v2 h-2 Z M7.5,5 h2 v2 h-2 Z M10.5,5 h2 v2 h-2 Z") },
        [ID_LOADVARS] = new[] { S("M6.5,3 C5,3 5,4 5,5.5 V8 C5,9 4.5,10 3.5,10 C4.5,10 5,11 5,12 V14.5 C5,16 5,17 6.5,17"), S("M13.5,3 C15,3 15,4 15,5.5 V8 C15,9 15.5,10 16.5,10 C15.5,10 15,11 15,12 V14.5 C15,16 15,17 13.5,17"), S("M10,5.5 V13.5 M7.5,11 L10,13.5 L12.5,11") },
        // Options2
        [ID_CAPTIONDISABLE] = new[] { S(Window), S("M2.5,7.5 H17.5"), S("M3,18 L17,2", 0.85) },
        [ID_WINDOWCAPTION] = new[] { S(Window), F("M3.5,4 H16.5 V7.5 H3.5 Z"), S("M5.5,10.5 H14.5 M5.5,13.5 H11.5") },
        [ID_LOADCOLORS] = new[] { S("M10,2.5 V8.5 M7.5,6 L10,8.5 L12.5,6"), FC(Rect(2.5, 11, 6.5, 17), Red), FC(Rect(8, 11, 12, 17), Green), FC(Rect(13.5, 11, 17.5, 17), Blue) },
        [ID_INCLUDE_TOOLBARS] = new[] { S("M4.5,2.5 H11.5 L15.5,6.5 V17.5 H4.5 Z M11.5,2.5 V6.5 H15.5"), F("M6.5,8.5 h2 v2 h-2 Z M9.5,8.5 h2 v2 h-2 Z"), S("M6.5,13.5 H13.5 M6.5,15.5 H11.5") },
        // Window
        [ID_WINDOW_STACK_HORIZONTAL] = new[] { S(Rect(3.5, 2.5, 16.5, 6.5)), S(Rect(3.5, 8, 16.5, 12)), S(Rect(3.5, 13.5, 16.5, 17.5)) },
        // Data
        [ID_REBUILD_RATE] = new[] { F(Dots(1.25, (4, 4.5), (16, 4.5))), S("M7,4.5 h1.5 M11.5,4.5 h1.5", 0.6), S("M10,6.5 V11.5 M8.5,10 L10,11.5 L11.5,10"), F(Dots(1.25, (3.5, 15), (6.75, 15), (10, 15), (13.25, 15), (16.5, 15))) },
        [ID_EXTRAPOLATE] = new[] { S(Axes), S("M6,14 L11,9.5"), F(Dots(1.25, (6, 14), (11, 9.5))), S("M12.8,7.9 l1.5,-1.35 M15.5,5.5 l1.5,-1.35") },
        // Realtime
        [ID_TIMESLIDE] = new[] { S(Axes), S("M6,6.5 H15 M13,4.5 L15,6.5 L13,8.5"), S("M8,12 H17 M15,10 L17,12 L15,14") },
        [ID_IGNORE_STATIC] = new[] { S(Axes), S("M5,11 C7,4 9.5,4 11.5,8 S15,12 17,5.5"), S("M5.5,14 h2 M9.5,14 h2 M13.5,14 h2", 0.45) },
        [ID_AUTOCOMPACT] = new[] { S("M2.5,10 H8 M6,8 L8,10 L6,12"), S("M17.5,10 H12 M14,8 L12,10 L14,12"), S("M10,4.5 V15.5") },
        [ID_DROPOUT_IGNORE] = new[] { S("M2.5,15.5 L5.5,10.5 L8,13 H12 L14.5,9 L17.5,14"), S("M8.5,3.5 H12.5 L10.5,6.5 Z", 0.7) },
        [ID_DROPOUT_MARKER] = new[] { S("M2.5,15.5 L5.5,10.5 L8,13"), S("M12,13 L14.5,9 L17.5,14"), FC("M8,3 H13 L10.5,7 Z", Red) },
    };

    private static (FontFamily, GlyphTypeface)? LoadGlyphFont()
    {
        foreach (string name in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
        {
            var f = new FontFamily(name);
            if (new Typeface(f, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal).TryGetGlyphTypeface(out var gt)
                && gt.FamilyNames.Values.Contains(name)) return (f, gt);
        }
        return null;
    }

    /// <summary>Builds the 20x20 glyph element for a command, or null when it has none.
    /// Foreground is inherited so the toolbar's disabled styling still applies.</summary>
    public static UIElement? Create(int id)
    {
        if (Drawn.TryGetValue(id, out var layers)) return Draw(layers);
        if (!Map.TryGetValue(id, out var icon)) return null;
        if (icon.Glyph == '\0')
            return new TextBlock
            {
                Text = icon.Badge,
                FontFamily = Badges,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                MinWidth = 20,
                Height = 20,
                LineHeight = 20,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                TextAlignment = TextAlignment.Center,
            };
        if (Glyphs is not { } glyphs || !glyphs.Face.CharacterToGlyphMap.ContainsKey(icon.Glyph)) return null;
        var g = new Grid { Width = 20, Height = 20, SnapsToDevicePixels = true };
        bool badge = icon.Badge != null;
        g.Children.Add(new TextBlock
        {
            Text = icon.Glyph.ToString(),
            FontFamily = glyphs.Family,
            FontSize = badge ? 14 : 16,
            HorizontalAlignment = badge ? HorizontalAlignment.Left : HorizontalAlignment.Center,
            VerticalAlignment = badge ? VerticalAlignment.Top : VerticalAlignment.Center,
        });
        if (badge)
            g.Children.Add(new TextBlock
            {
                Text = icon.Badge,
                FontFamily = Badges,
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, -2),
            });
        return g;
    }

    private static UIElement Draw(Layer[] layers)
    {
        var g = new Grid { Width = 20, Height = 20 };
        bool colored = false;
        foreach (var l in layers)
        {
            var p = new Shapes.Path
            {
                Data = Geometry.Parse(l.Data),
                Opacity = l.Opacity,
                StrokeThickness = l.Thickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
            };
            var target = l.Fill ? Shapes.Shape.FillProperty : Shapes.Shape.StrokeProperty;
            if (l.Color is { } c)
            {
                var brush = new SolidColorBrush(c);
                brush.Freeze();
                p.SetValue(target, brush);
                colored = true;
            }
            else p.SetBinding(target, new Binding { Path = new PropertyPath(TextElement.ForegroundProperty), RelativeSource = RelativeSource.Self });
            g.Children.Add(p);
        }
        // fixed colors ignore the toolbar's gray disabled foreground, so fade them instead
        if (colored) g.IsEnabledChanged += (_, _) => g.Opacity = g.IsEnabled ? 1 : 0.4;
        return g;
    }
}
