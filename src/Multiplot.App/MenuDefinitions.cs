// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

using Multiplot.Core;

namespace Multiplot.App;

/// <summary>A menu entry of the main menu or a plot popup menu (DEV-UI-002).</summary>
public sealed record MenuNode(string Caption, int Id, MenuNode[]? Children = null)
{
    /// <summary>Popup.</summary>
    public MenuNode(string caption, MenuNode[] children) : this(caption, 0, children) { }
    /// <summary>Separator.</summary>
    public static readonly MenuNode Sep = new("-", -1);
    /// <summary>True for separators.</summary>
    public bool IsSeparator => Id == -1;
}

/// <summary>The main menu (IDR_MAINFRAME) and the plot popup menus (IDR_XHEADING).</summary>
public static class MenuDefinitions
{
    /// <summary>Main menu bar.</summary>
    public static readonly MenuNode[] Main =
    {
        new("&File", new MenuNode[]
        {
            new("&Open", CommandIds.ID_FILE_NEW),
            new("&Add", CommandIds.ID_FILE_OPEN),
            new("&Import Tabular", CommandIds.ID_IMPORT_FILE),
            new("Recent &Files", new MenuNode[]
            {
                new("Empty", CommandIds.ID_RECENT_EMPTY),
            }),
            new("&Close", new MenuNode[]
            {
                new("&Last", CommandIds.ID_CLOSELAST),
                new("&All", CommandIds.ID_DBFCLOSE),
            }),
            new("Pr&operties", CommandIds.ID_FILELIST),
            MenuNode.Sep,
            new("E&xit", CommandIds.ID_APP_EXIT),
        }),
        new("&Edit", new MenuNode[]
        {
            new("Copy to &Clipboard", new MenuNode[]
            {
                new("as &BMP", CommandIds.ID_COPY_AS_BMP),
                new("as &Text", CommandIds.ID_EXPORTCB),
            }),
            MenuNode.Sep,
            new("Copy to &File", new MenuNode[]
            {
                new("as &Picture", CommandIds.ID_COPY_AS_PIC),
                new("as &Text", CommandIds.ID_EXPORT),
                new("as &WPD", CommandIds.ID_SAVE_WPD),
                new("as &Sun", CommandIds.ID_COPY_AS_SUN),
            }),
            MenuNode.Sep,
            new("&Import from Clipboard", CommandIds.ID_IMPORTCB),
            new("&Export to Application", CommandIds.ID_VIEW_EXPORTCURRENT),
            MenuNode.Sep,
            new("&Symbolic Names", CommandIds.ID_VAR_EDIT),
            MenuNode.Sep,
            new("Tabular Display &Layout", new MenuNode[]
            {
                new("&Alignment Grid", CommandIds.ID_TAB_GRID),
                new("&Snap To Grid", CommandIds.ID_SNAPTOGRID),
            }),
        }),
        new("S&cript", new MenuNode[]
        {
            new("E&xecute", CommandIds.ID_SCRIPT_RUN),
            new("&Record", CommandIds.ID_SCRIPT_RECORD),
            new("&Edit", CommandIds.ID_SCRIPT_EDIT),
            MenuNode.Sep,
            new("&GoTo Next", CommandIds.ID_SCRIPT_NEXT),
            new("&Pause", CommandIds.ID_SCRIPT_PAUSE),
            new("&Abort", CommandIds.ID_SCRIPT_ABORT),
            MenuNode.Sep,
            new("Pause Any &Command", CommandIds.ID_SCRIPTING_PAUSEANYCOMMAND),
            new("Allow Prompt and Error &Dialogs", CommandIds.ID_SCRIPT_PROMPT),
            new("&Log Script Errors", CommandIds.ID_SCRIPTLOG),
            new("&Script Placeholder Plot", CommandIds.ID_USEPLACEHOLDER),
            new("Enable File Embedded Scripts", CommandIds.ID_SCRIPT_EMBEDDED),
        }),
        new("&Parameter", new MenuNode[]
        {
            new("&Selection", CommandIds.ID_PARMSELECT),
            new("&Favorites", CommandIds.ID_FAVORITES),
            new("Filter &Helper", CommandIds.ID_FILTERS),
            new("&Add to Plot", CommandIds.ID_ADDPARAMETER),
            new("Select &WPD", CommandIds.ID_WPDSELECT),
            MenuNode.Sep,
            new("Parameter P&roperties", CommandIds.ID_VIEW_INFO),
        }),
        new("&TimeSlice", new MenuNode[]
        {
            new("&Global Time Slice", CommandIds.ID_TIMESLICE),
            new("&Per Database", CommandIds.ID_MULTITIME),
            new("&Aligment Tool", CommandIds.ID_TIMEALIGN),
            new("Align &Static to Live\tCtrl+L", CommandIds.ID_ALIGN_STATIC),
            new("&Freeze Plot", CommandIds.ID_TIMEFREEZE),
        }),
        new("&View", new MenuNode[]
        {
            new("New &Plot", CommandIds.ID_VIEW_ASPLOT),
            MenuNode.Sep,
            new("&Tabulation", CommandIds.ID_VIEW_ASTABULATION),
            MenuNode.Sep,
            new("Presentation &Mode", new MenuNode[]
            {
                new("&Enable", CommandIds.ID_PRESENTATION_MODE),
                new("&Full Screen", CommandIds.ID_MAX_PRES),
            }),
            new("&Appearance", new MenuNode[]
            {
                new("&Light", CommandIds.ID_APPEARANCE_LIGHT),
                new("&Dark", CommandIds.ID_APPEARANCE_DARK),
            }),
        }),
        new("&Axis", new MenuNode[]
        {
            new("&Multi Y", new MenuNode[]
            {
                new("&On", CommandIds.ID_AXIS_MULTIY),
                new("&Group By", new MenuNode[]
                {
                    new("&Independent Scales", CommandIds.ID_GROUPBY_INDEPENDENT),
                    new("&Name", CommandIds.ID_GROUPBY_NAME),
                    new("&Units", CommandIds.ID_GROUPBY_UNITS),
                    new("&Description", CommandIds.ID_GROUPBY_DESCRIPTION),
                    new("U&ser Defined", CommandIds.ID_GROUPBY_UDEF),
                }),
            }),
            new("&End To End", CommandIds.ID_AXIS_ENDTOEND),
            MenuNode.Sep,
            new("&Log X", CommandIds.ID_AXIS_LOGX),
            new("L&og Y", CommandIds.ID_AXIS_LOGY),
            MenuNode.Sep,
            new("&Auto Scale X (Time Axis)", CommandIds.ID_XAXIS_RESCALE),
            new("A&uto Scale Y (Value Axis)", CommandIds.ID_YAXIS_RESCALE),
            new("Auto &Range X/Y Once\tHome Key", CommandIds.ID_XYAXIS_RESCALE),
            new("Fix &X", CommandIds.ID_AXIS_FIXX),
            new("Fix &Y", CommandIds.ID_AXIS_FIXY),
            new("Revert Scales\tBackspace", CommandIds.ID_UNDO_ZOOM),
            MenuNode.Sep,
            new("Zoom In X Axis\tInsert", CommandIds.ID_X_ZOOMIN),
            new("Zoom Out X Axis\tDelete", CommandIds.ID_X_ZOOMOUT),
            new("Zoom In Y Axis\tPage Up", CommandIds.ID_AXIS_ZOOMIN),
            new("Zoom Out Y Axis\tPage Down", CommandIds.ID_AXIS_ZOOMOUT),
            MenuNode.Sep,
            new("Pan Scale to Left\tLeft Arrow", CommandIds.ID_AXIS_SHLX),
            new("Pan Scale to Right\tRight Arrow", CommandIds.ID_AXIS_SHRX),
            new("Pan Scale Up\tUp Arrow", CommandIds.ID_AXIS_SHUY),
            new("Pan Scale Down\tDown Arrow", CommandIds.ID_AXIS_SHDY),
            MenuNode.Sep,
            new("3D &Z Axis Angle", CommandIds.ID_AXIS_ZANGLE),
        }),
        new("&Style", new MenuNode[]
        {
            new("&2D Line", CommandIds.ID_STYLE_2DLINE),
            new("2D &Scatter", CommandIds.ID_STYLE_2DSCATTER),
            new("&3D Line", CommandIds.ID_STYLE_3DLINE),
            new("3D &Area", CommandIds.ID_STYLE_3DAREA),
            new("&Logic", CommandIds.ID_STYLE_LOGIC),
            new("&Tabular", CommandIds.ID_STYLE_TABULAR),
            MenuNode.Sep,
            new("Trace Attri&butes", CommandIds.ID_TRACE_ATTRIB),
            MenuNode.Sep,
            new("Data &Rate", new MenuNode[]
            {
                new("Set &Mode", CommandIds.ID_DATARATE),
                new("&Density", new MenuNode[]
                {
                    new("&Average", CommandIds.ID_STYLE_DENSITY_AVERAGE),
                    new("&Sample", CommandIds.ID_STYLE_DENSITY_SAMPLE),
                    new("&3D Segments", CommandIds.ID_STYLE_DENSITY_3DSEGMENTS),
                }),
                new("Tabular Step", CommandIds.ID_DATARATE_TABULARSTEP),
            }),
            new("&Colors", new MenuNode[]
            {
                new("&Line Colors", CommandIds.ID_LINE_COLOR),
                new("L&abel Color", CommandIds.ID_LABEL_COLOR),
                new("&Title Color", CommandIds.ID_TITLE_COLOR),
                new("&Grid Color", CommandIds.ID_GRID_COLOR),
                new("&Marker Color", CommandIds.ID_MARKER_COLOR),
                new("Re&dline Color", CommandIds.ID_REDLINE_COLOR),
                new("&Background Color", CommandIds.ID_BACKGROUND_COLOR),
                new("&Restore Colors", CommandIds.ID_RESTORE_COLORS),
                new("Load Color &Table", CommandIds.ID_LOADCOLORS),
            }),
            new("2D Li&ne Size", CommandIds.ID_2D_LINESZ),
            new("3D L&ine Size", CommandIds.ID_3D_LINESZ),
            MenuNode.Sep,
            new("&Grid Lines", CommandIds.ID_GRIDSTYLE),
            new("&Minor Tics", CommandIds.ID_MINORTICS),
            new("&Bold Frame", CommandIds.ID_STYLE_BOLDFRAME),
            new("Stair St&ep", CommandIds.ID_STAIRSTEP),
            new("S&ymbols", new MenuNode[]
            {
                new("&Show", CommandIds.ID_STYLE_SYMBOLS),
                new("&Density", CommandIds.ID_STYLE_DENSITY_SYMBOL),
                new("Si&ze", new MenuNode[]
                {
                    new("&Small", CommandIds.ID_SYM_SMALL),
                    new("&Normal", CommandIds.ID_SYM_NORMAL),
                    new("&Large", CommandIds.ID_SYM_LARGE),
                }),
            }),
            new("Legen&d", new MenuNode[]
            {
                new("&Show", CommandIds.ID_STYLE_LEGEND),
                new("Set Colum&ns", CommandIds.ID_SET_LEGEND_COLUMNS),
                new("&Fixed Parameter Space", CommandIds.ID_STYLE_LEGRESERVE),
                new("Minimum &Grid Offset", CommandIds.ID_MINGRIDOFFSET),
                new("Multi-Y Shift &Left", CommandIds.ID_LEGENDSHIFT),
                new("Text in &Curve Color", CommandIds.ID_USECURVECOLOR),
            }),
        }),
        new("&Nomenclature", new MenuNode[]
        {
            new("&Fonts", new MenuNode[]
            {
                new("&Labels...", CommandIds.ID_SETLFONT),
                new("&Titles...", CommandIds.ID_SETTFONT),
            }),
            new("&X Axis/Legend Labeling", CommandIds.ID_AXIS_XNOMEN),
            new("&Y Axis/Legend Labeling", CommandIds.ID_AXIS_YNOMEN),
            new("Set &Title (top center)", CommandIds.ID_AXIS_XTITLE),
            new("Set &Sub-Title (left center)", CommandIds.ID_AXIS_YTITLE),
            new("Show Time as &GMT/Clock", CommandIds.ID_CLOCKTIME),
            new("Set G&MT Format", CommandIds.ID_GMTFORMAT),
            new("Scale G&roup ID", CommandIds.ID_STYLE_SCALEGROUP),
            new("Test &Info (Test Title)", CommandIds.ID_STYLE_TESTINFO),
            new("Set Plot &Window Title...", CommandIds.ID_SETUSERCAPTION),
        }),
        new("&Markers", new MenuNode[]
        {
            new("&Add Event Marker...\tCtrl+D", CommandIds.ID_DRAW_EVENT),
            new("&Edit...", CommandIds.ID_EDIT_MARKERS),
            new("Add &Redline Marker...", CommandIds.ID_DRAW_REDLINE),
            new("Edit Redl&ines...", CommandIds.ID_EDIT_REDLINES),
            new("Show &File Events", CommandIds.ID_SHOW_FILE_EVENTS),
            new("&Topmost", CommandIds.ID_MARKERS_TOPMOST),
            new("&Marker Color...", CommandIds.ID_MARKER_COLOR),
            new("Re&dline Color...", CommandIds.ID_REDLINE_COLOR),
            MenuNode.Sep,
            new("&Clear All", CommandIds.ID_CLEAR_MARKERS),
            new("&Save", CommandIds.ID_MARKER_SAVE),
            new("&Load", CommandIds.ID_MARKER_LOAD),
        }),
        new("&Options", new MenuNode[]
        {
            new("&File Paths", CommandIds.ID_SETPATHS),
            new("&External Applications", new MenuNode[]
            {
                new("&Assign", CommandIds.ID_EXTAPPS),
                new("Include &GMTs", CommandIds.ID_SHOWGMT),
            }),
            MenuNode.Sep,
            new("Tabular Output/Exports", new MenuNode[]
            {
                new("&Data Format", CommandIds.ID_EXTPRECISION),
                new("&Time Format", CommandIds.ID_TIMEPRECISION),
                new("&Shared Time", CommandIds.ID_TAB_SGL_TIME),
            }),
            new("Expressions/&Calculations", new MenuNode[]
            {
                new("&Align to Closest Point", CommandIds.ID_DATA_ALIGNMENT),
                new("&Perform Every Unique Time", CommandIds.ID_CALC_EVERY_INSTANCE),
            }),
            new("&Draw Mode", new MenuNode[]
            {
                new("&Optimized", CommandIds.ID_OPTIMIZED_DRAW),
                new("Off&screen", CommandIds.ID_MEMDRAW),
            }),
            MenuNode.Sep,
            new("&Average Function", new MenuNode[]
            {
                new("Default &Rate", CommandIds.ID_AVGRATE),
                new("&Time Stamp Mode", CommandIds.ID_AVGTIMESTAMP),
            }),
            new("Right &Button Click", CommandIds.ID_RIGHTBUTTON),
            MenuNode.Sep,
            new("Show System &Load", CommandIds.ID_SHOW_SYS_LOAD),
            new("&Show Coordinates", CommandIds.ID_SHOWCOORD),
            new("S&how Status Bar", CommandIds.ID_EDIT_STATUSBAR),
            new("&Maximized Plots", CommandIds.ID_VIEW_MAX),
            new("Load &Symbolic Names", CommandIds.ID_LOADVARS),
            new("&Number of Monitors", CommandIds.ID_MONITORS),
            new("Save &Window Position", CommandIds.ID_POS_SAVE),
            new("Exit Safet&y Prompt", CommandIds.ID_EXIT_SAFETY),
            MenuNode.Sep,
            new("Continue &Titles", CommandIds.ID_STYLE_TITLE),
            new("Continue S&ub-Titles", CommandIds.ID_STYLE_SUBTITLE),
            new("Continue Mar&kers", CommandIds.ID_USEMARKERS),
            MenuNode.Sep,
            new("&X Axis Auto Label", CommandIds.ID_AUTOGEN),
            new("Apply Changes &Globally", CommandIds.ID_APPLY_ALL),
            new("&Window Caption", new MenuNode[]
            {
                new("&Disable", CommandIds.ID_CAPTIONDISABLE),
                new("&Source", CommandIds.ID_WINDOWCAPTION),
            }),
            MenuNode.Sep,
            new("Sa&ve Settings", CommandIds.ID_SAVE_SETTINGS),
            new("&Load Settings", CommandIds.ID_LOAD_SETTINGS),
            new("&Import WinPlot Settings...", CommandIds.ID_IMPORT_WINPLOT),
        }),
        new("&User", new MenuNode[]
        {
            new("&1", CommandIds.ID_USERSCHOICE1),
            new("&2", CommandIds.ID_USERSCHOICE2),
            new("&3", CommandIds.ID_USERSCHOICE3),
            new("&4", CommandIds.ID_USERSCHOICE4),
            new("&5", CommandIds.ID_USERSCHOICE5),
            new("&Assign", CommandIds.ID_USERASSIGN),
        }),
        new("&Data", new MenuNode[]
        {
            new("Rebuild &Full Rate", CommandIds.ID_REBUILD_RATE),
            new("&Extrapolate End Points", CommandIds.ID_EXTRAPOLATE),
            new("&Use 64 Bit Timing", CommandIds.ID_64_BIT_TIMING),
            new("&Dropouts", new MenuNode[]
            {
                new("&Ignore", CommandIds.ID_DROPOUT_IGNORE),
                new("&Show Markers", CommandIds.ID_DROPOUT_MARKER),
            }),
            MenuNode.Sep,
            new("&Real-Time", new MenuNode[]
            {
                new("&Refresh", CommandIds.ID_REFRESH),
                new("&Auto Refresh", CommandIds.ID_REFRESH_TIMER),
                new("&Ignore Static Data", CommandIds.ID_IGNORE_STATIC),
                new("&First Occurence", CommandIds.ID_LOADONCE),
                new("Auto Ru&n", CommandIds.ID_AUTORUN),
                new("Auto Data &Compact", CommandIds.ID_AUTOCOMPACT),
                new("Auto Data &Discard", CommandIds.ID_AUTODISCARD),
                new("Show Stream &Certification", CommandIds.ID_CERTSTATUS),
                new("&Tabular Options", new MenuNode[]
                {
                    new("&Maintain History", CommandIds.ID_TAB_HISTORY),
                    new("&Default Time Stamp", CommandIds.ID_DEF_TAB_TIME),
                }),
            }),
            new("&Manage Plug-ins", CommandIds.ID_MANAGE_PLUGINS),
        }),
        new("&Window", new MenuNode[]
        {
            new("&Cascade", CommandIds.ID_WINDOW_CASCADE),
            new("Tile &Horizontally", CommandIds.ID_WINDOW_TILE_HORZ),
            new("Tile &Vertically", CommandIds.ID_WINDOW_TILE_VERT),
            new("&Stack Horizontally", CommandIds.ID_WINDOW_STACK_HORIZONTAL),
            new("&Maximize", CommandIds.ID_WINDOW_MAXIMIZE),
            new("M&inimize", CommandIds.ID_WINDOW_MINIMIZE),
            new("Mo&ve", CommandIds.ID_MOVE_WINDOW),
            new("&Refresh", CommandIds.ID_WINDOW_REFRESH),
            MenuNode.Sep,
            new("&Previous ", CommandIds.ID_PREV_WINDOW),
            new("&Next", CommandIds.ID_NEXT_WINDOW),
            new("&Select", CommandIds.ID_WINDOWLIST),
            MenuNode.Sep,
            new("&Auto Tab", CommandIds.ID_AUTOTAB),
            MenuNode.Sep,
            new("&Keep on Top", CommandIds.ID_WINDOW_ONTOP),
            MenuNode.Sep,
            new("Cl&ose", CommandIds.ID_WINDOW_CLOSE),
            new("C&lose All", CommandIds.ID_WINDOW_CLOSEALL),
        }),
        new("&Help", new MenuNode[]
        {
            new("&User Guide", CommandIds.ID_HELP_CONTENTS),
            MenuNode.Sep,
            new("&Window Position", CommandIds.ID_WIN_POS),
            new("&About Multiplot...", CommandIds.ID_APP_ABOUT),
        }),
    };

    /// <summary>Plot region popups (X Heading, Y Heading, Footing, Plot, Plot3D, …).</summary>
    public static readonly MenuNode[] PlotPopups =
    {
        new("X Heading", new MenuNode[]
        {
            new("&Set Title", CommandIds.ID_AXIS_XTITLE),
            new("&Y Axis Labeling", CommandIds.ID_AXIS_YNOMEN),
            new("&Font", new MenuNode[]
            {
                new("&Title/Subtitle...", CommandIds.ID_SETTFONT),
                new("&Legend/Scale...", CommandIds.ID_SETLFONT),
            }),
            new("&Colors", new MenuNode[]
            {
                new("&Line Colors", CommandIds.ID_LINE_COLOR),
                new("&Label Color", CommandIds.ID_LABEL_COLOR),
                new("&Title Color", CommandIds.ID_TITLE_COLOR),
                new("&Grid Color", CommandIds.ID_GRID_COLOR),
                new("&Marker Color", CommandIds.ID_MARKER_COLOR),
                new("Re&dline Color", CommandIds.ID_REDLINE_COLOR),
                new("&Background Color", CommandIds.ID_BACKGROUND_COLOR),
            }),
            new("&Legend", new MenuNode[]
            {
                new("&Show", CommandIds.ID_STYLE_LEGEND),
                new("&Columns", CommandIds.ID_SET_LEGEND_COLUMNS),
                new("Minimum &Grid Offset", CommandIds.ID_MINGRIDOFFSET),
                new("Text in Curve Color", CommandIds.ID_USECURVECOLOR),
            }),
            new("Presentation &Mode", CommandIds.ID_PRESENTATION_MODE),
            new("&Window", new MenuNode[]
            {
                new("&Maximize", CommandIds.ID_WINDOW_MAXIMIZE),
                new("M&inimize", CommandIds.ID_WINDOW_MINIMIZE),
                new("&Move", CommandIds.ID_MOVE_WINDOW),
                new("&Tile", CommandIds.ID_WINDOW_TILE_HORZ),
                new("&Set Plot Window Title...", CommandIds.ID_SETUSERCAPTION),
                new("&Close", CommandIds.ID_WINDOW_CLOSE),
            }),
            MenuNode.Sep,
            new("&Properties", CommandIds.ID_VIEW_INFO),
        }),
        new("Y Heading", new MenuNode[]
        {
            new("Set &Sub-Title", CommandIds.ID_AXIS_YTITLE),
            new("&Font", new MenuNode[]
            {
                new("&Title/Subtitle...", CommandIds.ID_SETTFONT),
                new("&Legend/Scale...", CommandIds.ID_SETLFONT),
            }),
            new("&Multi-Y", CommandIds.ID_AXIS_MULTIY),
            new("Multi-Y Shift &Left", CommandIds.ID_LEGENDSHIFT),
            new("&Colors", new MenuNode[]
            {
                new("&Line Colors", CommandIds.ID_LINE_COLOR),
                new("&Label Color", CommandIds.ID_LABEL_COLOR),
                new("&Title Color", CommandIds.ID_TITLE_COLOR),
                new("&Grid Color", CommandIds.ID_GRID_COLOR),
                new("&Marker Color", CommandIds.ID_MARKER_COLOR),
                new("Re&dline Color", CommandIds.ID_REDLINE_COLOR),
                new("&Background Color", CommandIds.ID_BACKGROUND_COLOR),
            }),
            new("Minimum &Grid Offset", CommandIds.ID_MINGRIDOFFSET),
            new("Presentation &Mode", CommandIds.ID_PRESENTATION_MODE),
            new("&Window", new MenuNode[]
            {
                new("&Maximize", CommandIds.ID_WINDOW_MAXIMIZE),
                new("M&inimize", CommandIds.ID_WINDOW_MINIMIZE),
                new("&Move", CommandIds.ID_MOVE_WINDOW),
                new("&Tile", CommandIds.ID_WINDOW_TILE_HORZ),
                new("&Set Plot Window Title...", CommandIds.ID_SETUSERCAPTION),
                new("&Close", CommandIds.ID_WINDOW_CLOSE),
            }),
            MenuNode.Sep,
            new("&Properties", CommandIds.ID_VIEW_INFO),
        }),
        new("Footing", new MenuNode[]
        {
            new("&X Axis Labeling", CommandIds.ID_AXIS_XNOMEN),
            new("&Font", new MenuNode[]
            {
                new("&Title/Subtitle...", CommandIds.ID_SETTFONT),
                new("&Legend/Scale...", CommandIds.ID_SETLFONT),
            }),
            new("Show Time as &GMT", CommandIds.ID_CLOCKTIME),
            new("GMT/Clock Format", CommandIds.ID_GMTFORMAT),
            new("Test &Info (Test Title)", CommandIds.ID_STYLE_TESTINFO),
            new("&Colors", new MenuNode[]
            {
                new("&Line Colors", CommandIds.ID_LINE_COLOR),
                new("&Label Color", CommandIds.ID_LABEL_COLOR),
                new("&Title Color", CommandIds.ID_TITLE_COLOR),
                new("&Grid Color", CommandIds.ID_GRID_COLOR),
                new("&Marker Color", CommandIds.ID_MARKER_COLOR),
                new("Re&dline Color", CommandIds.ID_REDLINE_COLOR),
                new("&Background Color", CommandIds.ID_BACKGROUND_COLOR),
            }),
            new("Presentation &Mode", CommandIds.ID_PRESENTATION_MODE),
            new("&Window", new MenuNode[]
            {
                new("&Maximize", CommandIds.ID_WINDOW_MAXIMIZE),
                new("M&inimize", CommandIds.ID_WINDOW_MINIMIZE),
                new("&Move", CommandIds.ID_MOVE_WINDOW),
                new("&Tile", CommandIds.ID_WINDOW_TILE_HORZ),
                new("&Set Plot Window Title...", CommandIds.ID_SETUSERCAPTION),
                new("&Close", CommandIds.ID_WINDOW_CLOSE),
            }),
            MenuNode.Sep,
            new("&Properties", CommandIds.ID_VIEW_INFO),
        }),
        new("Plot", new MenuNode[]
        {
            new("&Symbols", new MenuNode[]
            {
                new("&Show", CommandIds.ID_STYLE_SYMBOLS),
                new("&Density", CommandIds.ID_STYLE_DENSITY_SYMBOL),
                new("Si&ze", new MenuNode[]
                {
                    new("&Small", CommandIds.ID_SYM_SMALL),
                    new("&Normal", CommandIds.ID_SYM_NORMAL),
                    new("&Large", CommandIds.ID_SYM_LARGE),
                }),
            }),
            new("Markers", new MenuNode[]
            {
                new("&Add Event Marker...", CommandIds.ID_DRAW_EVENT),
                new("&Edit...", CommandIds.ID_EDIT_MARKERS),
                new("Add &Redline Marker...", CommandIds.ID_DRAW_REDLINE),
                new("Edit Redl&ines...", CommandIds.ID_EDIT_REDLINES),
                new("Show &File Events", CommandIds.ID_SHOW_FILE_EVENTS),
                new("&Clear All", CommandIds.ID_CLEAR_MARKERS),
                MenuNode.Sep,
                new("&Save", CommandIds.ID_MARKER_SAVE),
                new("&Load", CommandIds.ID_MARKER_LOAD),
            }),
            new("Align S&tatic to Live", CommandIds.ID_ALIGN_STATIC),
            new("&Colors", new MenuNode[]
            {
                new("&Line Colors", CommandIds.ID_LINE_COLOR),
                new("&Label Color", CommandIds.ID_LABEL_COLOR),
                new("&Title Color", CommandIds.ID_TITLE_COLOR),
                new("&Grid Color", CommandIds.ID_GRID_COLOR),
                new("&Marker Color", CommandIds.ID_MARKER_COLOR),
                new("Re&dline Color", CommandIds.ID_REDLINE_COLOR),
                new("&Background Color", CommandIds.ID_BACKGROUND_COLOR),
            }),
            new("&End to End", CommandIds.ID_AXIS_ENDTOEND),
            new("Scales", new MenuNode[]
            {
                new("Auto Scale X (Time Axis)", CommandIds.ID_XAXIS_RESCALE),
                new("Auto Scale Y (Value Axis)", CommandIds.ID_YAXIS_RESCALE),
                new("Auto Range X/Y Once", CommandIds.ID_XYAXIS_RESCALE),
                new("Fix &X", CommandIds.ID_AXIS_FIXX),
                new("Fix &Y", CommandIds.ID_AXIS_FIXY),
                new("&Revert", CommandIds.ID_UNDO_ZOOM),
            }),
            new("&Font", new MenuNode[]
            {
                new("&Title/Subtitle...", CommandIds.ID_SETTFONT),
                new("&Legend/Scale...", CommandIds.ID_SETLFONT),
            }),
            MenuNode.Sep,
            new("&Add Parameter", CommandIds.ID_ADDPARAMETER),
            MenuNode.Sep,
            new("View &Tabulation", CommandIds.ID_VIEW_ASTABULATION),
            new("Presentation &Mode", CommandIds.ID_PRESENTATION_MODE),
            new("&Window", new MenuNode[]
            {
                new("&Maximize", CommandIds.ID_WINDOW_MAXIMIZE),
                new("M&inimize", CommandIds.ID_WINDOW_MINIMIZE),
                new("&Move", CommandIds.ID_MOVE_WINDOW),
                new("&Tile", CommandIds.ID_WINDOW_TILE_HORZ),
                new("&Set Plot Window Title...", CommandIds.ID_SETUSERCAPTION),
                new("&Close", CommandIds.ID_WINDOW_CLOSE),
            }),
            MenuNode.Sep,
            new("&Properties", CommandIds.ID_VIEW_INFO),
        }),
        new("Plot3D", new MenuNode[]
        {
            new("3D Li&ne Size", CommandIds.ID_3D_LINESZ),
            new("3D &Z Axis Angle", CommandIds.ID_AXIS_ZANGLE),
            new("&Show Symbols", CommandIds.ID_STYLE_SYMBOLS),
            new("Markers", new MenuNode[]
            {
                new("&Add Event Marker...", CommandIds.ID_DRAW_EVENT),
                new("&Edit...", CommandIds.ID_EDIT_MARKERS),
                new("Show &File Events", CommandIds.ID_SHOW_FILE_EVENTS),
                new("&Clear All", CommandIds.ID_CLEAR_MARKERS),
                MenuNode.Sep,
                new("&Save", CommandIds.ID_MARKER_SAVE),
                new("&Load", CommandIds.ID_MARKER_LOAD),
            }),
            new("&End to End", CommandIds.ID_AXIS_ENDTOEND),
            new("&Colors", new MenuNode[]
            {
                new("&Line Colors", CommandIds.ID_LINE_COLOR),
                new("&Label Color", CommandIds.ID_LABEL_COLOR),
                new("&Title Color", CommandIds.ID_TITLE_COLOR),
                new("&Grid Color", CommandIds.ID_GRID_COLOR),
                new("&Marker Color", CommandIds.ID_MARKER_COLOR),
                new("Re&dline Color", CommandIds.ID_REDLINE_COLOR),
                new("&Background Color", CommandIds.ID_BACKGROUND_COLOR),
            }),
            new("Scales", new MenuNode[]
            {
                new("Auto Scale X (Time Axis)", CommandIds.ID_XAXIS_RESCALE),
                new("Auto Scale Y (Value Axis)", CommandIds.ID_YAXIS_RESCALE),
                new("Auto Range X/Y Once", CommandIds.ID_XYAXIS_RESCALE),
                new("Fix &X", CommandIds.ID_AXIS_FIXX),
                new("Fix &Y", CommandIds.ID_AXIS_FIXY),
                new("&Revert", CommandIds.ID_UNDO_ZOOM),
            }),
            new("&Font", new MenuNode[]
            {
                new("&Title/Subtitle...", CommandIds.ID_SETTFONT),
                new("&Legend/Scale...", CommandIds.ID_SETLFONT),
            }),
            MenuNode.Sep,
            new("&Add Parameter", CommandIds.ID_ADDPARAMETER),
            MenuNode.Sep,
            new("View &Tabulation", CommandIds.ID_VIEW_ASTABULATION),
            new("Presentation &Mode", CommandIds.ID_PRESENTATION_MODE),
            new("&Window", new MenuNode[]
            {
                new("&Maximize", CommandIds.ID_WINDOW_MAXIMIZE),
                new("M&inimize", CommandIds.ID_WINDOW_MINIMIZE),
                new("&Move", CommandIds.ID_MOVE_WINDOW),
                new("&Tile", CommandIds.ID_WINDOW_TILE_HORZ),
                new("&Set Plot Window Title...", CommandIds.ID_SETUSERCAPTION),
                new("&Close", CommandIds.ID_WINDOW_CLOSE),
            }),
            MenuNode.Sep,
            new("&Properties", CommandIds.ID_VIEW_INFO),
        }),
        new("Tabular", new MenuNode[]
        {
            new("&Nomenclature", CommandIds.ID_AXIS_YNOMEN),
            new("&Font", new MenuNode[]
            {
                new("&Title/Subtitle...", CommandIds.ID_SETTFONT),
                new("&Legend/Scale...", CommandIds.ID_SETLFONT),
            }),
            new("Text in &Curve Color", CommandIds.ID_USECURVECOLOR),
            new("Set Background &Bitmap", CommandIds.ID_TAB_BACKGROUND),
            new("&Decimal Places", CommandIds.ID_EXTPRECISION),
            new("&Default Time Stamp", CommandIds.ID_DEF_TAB_TIME),
            new("Show Time as &GMT", CommandIds.ID_CLOCKTIME),
            MenuNode.Sep,
            new("Presentation &Mode", CommandIds.ID_PRESENTATION_MODE),
            new("&Window", new MenuNode[]
            {
                new("&Maximize", CommandIds.ID_WINDOW_MAXIMIZE),
                new("M&inimize", CommandIds.ID_WINDOW_MINIMIZE),
                new("&Move", CommandIds.ID_MOVE_WINDOW),
                new("&Tile", CommandIds.ID_WINDOW_TILE_HORZ),
                new("&Set Plot Window Title...", CommandIds.ID_SETUSERCAPTION),
                new("&Close", CommandIds.ID_WINDOW_CLOSE),
            }),
            MenuNode.Sep,
            new("&Add Parameter", CommandIds.ID_ADDPARAMETER),
            MenuNode.Sep,
            new("&Maintain History", CommandIds.ID_TAB_HISTORY),
            new("&Properties", CommandIds.ID_VIEW_INFO),
            new("&Save As WPD", CommandIds.ID_SAVE_WPD),
        }),
        new("Left Click Plot", new MenuNode[]
        {
            new("ParmName", CommandIds.ID_VIEW_INFO),
        }),
    };
}
