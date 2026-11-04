namespace Multiplot.Scripting;

/// <summary>Script command keys (same numeric values).</summary>
public enum Cmd
{
    Unknown = -1,
    Comment = 0, Pause, Run, Call, PrinterName, XNomen, YNomen,
    XUdef, YUdef, XTitle, YTitle, MultiY, GridLines, EndToEnd,
    Rescale, FixX, FixY, Style, FontSize, GridStyle, BoldFrame, Legend,
    TimeStamp, TestInfo, ScriptPrompt, LineSize, Symbols, DataRate,
    Refresh, CopyMeta, Import, Open, New, Plot, XAxis, YAxis,
    Clear, Exit, Window, Print, Time, PrinterMargin, ColorPrinter,
    Landscape, FilePath, DbPath, ScriptPath, LoadSettings, ShowVersion,
    LegendShift, Tile, Export, Caption, ShowScript, SymbolSize,
    ExportPath, ClearList, Tabulate, PauseMode, Marker,
    Enable, Disable, TimeSlide, Toolbar, ClockTime, Tab, AutoRun,
    SaveSettings, ShowUserName, AutoTab, Set, StairStep, ApplyGlobal,
    LogX, LogY, MinorTics, GridOffset, EmfWide, Favorites, AutoLabel,
    Freeze, Zoom, Pan, IgnoreStatic, MarkerSet, StatusBar, PresentationMode,
    AlignClosest, AutoCompact, OptimizedDraw, RetainMtGmt, CopyJpeg,
    GmtFormat, DropoutIgnore, DropoutMarker, UseIniToolbar, SaveParmList,
    SaveFileDetails, SaveParmDetails, AutoDiscard, UsePlaceholder, IncludeGmt,
    ScaleGroup, ShowPageNo, ScriptLog, FixXAutoY, CalcEveryTime,
    WpdPath, PlaySound, LibCommand, Prompt, Input, ExtCmdPolling, StreamCert,
    PresentationFull, Mci, TabHistory, DefTabTime, AddParameter, CopyBmp,
    FirstOccurrence, Extrapolate, SavePic, SystemLoad, SaveSun, EmbeddedScript,
    DataStats, Bit64Time, TabStepRate, ShowCoord, ScaleRedlines, SharedTime,
}

/// <summary>Keyword table and command word lookup (<c>GetCmdKey</c>).</summary>
public static class ScriptKeywords
{
    /// <summary>Keyword spellings indexed by <see cref="Cmd"/>.</summary>
    public static readonly string[] Words =
    {
        ";", "pause", "run", "call", "printername", "xnomen", "ynomen",
        "xudef", "yudef", "xtitle", "ytitle", "multiy", "gridlines", "endtoend",
        "rescale", "fixx", "fixy", "style", "fontsize", "gridstyle", "boldframe", "legend",
        "timestamp", "testinfo", "scriptprompt", "linesize", "symbols", "datarate",
        "refresh", "copymeta", "import", "open", "new", "plot", "xaxis", "yaxis",
        "clear", "exit", "window", "print", "time", "printermargin", "colorprinter",
        "landscape", "filepath", "dbpath", "scriptpath", "loadsettings", "showversion",
        "legendshift", "tile", "export", "caption", "showscript", "symbolsize",
        "exportpath", "clearlist", "tabulate", "pausemode", "marker",
        "enable", "disable", "timeslide", "toolbar", "clocktime", "tab", "autorun",
        "savesettings", "showusername", "autotab", "set", "stairstep", "applyglobal",
        "logx", "logy", "minortics", "gridoffset", "emfwide", "favorites", "autolabel",
        "freeze", "zoom", "pan", "ignorestatic", "markerset", "statusbar", "presentationmode",
        "alignclosest", "autocompact", "optimizeddraw", "retainmtgmt", "copyjpeg",
        "gmtformat", "dropoutignore", "dropoutmarker", "useinitoolbar", "saveparmlist",
        "savefiledetails", "saveparmdetails", "autodiscard", "useplaceholder", "includegmt",
        "scalegroup", "showpageno", "scriptlog", "fixxautoy", "calceverytime",
        "wpdpath", "playsound", "libcommand", "prompt", "input", "extcmdpolling", "streamcert",
        "presentationfull", "mci", "tabhistory", "deftabtime", "addparameter", "copybmp",
        "firstoccurrence", "extrapolate", "savepic", "systemload", "savesun", "embeddedscript",
        "datastats", "64bittime", "tabsteprate", "showcoord", "scaleredlines", "sharedtime",
    };

    /// <summary>
    /// Command keyword lookup: empty or ';'-prefixed text is a comment; otherwise the text before the first
    /// space (trimmed) is matched case-insensitively against the table.
    /// </summary>
    public static Cmd GetCmdKey(string cmd)
    {
        if (cmd.Length == 0 || cmd[0] == ';') return Cmd.Comment;
        int sp = cmd.IndexOf(' ');
        string word = (sp >= 0 ? cmd[..sp] : cmd).Trim();
        for (int i = 0; i < Words.Length; i++)
            if (string.Equals(word, Words[i], StringComparison.OrdinalIgnoreCase)) return (Cmd)i;
        return Cmd.Unknown;
    }

    /// <summary>Argument string: everything after the first space, left-trimmed.</summary>
    public static string GetParm(string cmd)
    {
        int sp = cmd.IndexOf(' ');
        return sp < 0 ? "" : TrimLeftC(cmd[sp..]);
    }

    /// <summary>CString::TrimLeft (whitespace).</summary>
    public static string TrimLeftC(string s)
    {
        int i = 0;
        while (i < s.Length && IsSpace(s[i])) i++;
        return s[i..];
    }

    /// <summary>CString::TrimRight (whitespace).</summary>
    public static string TrimRightC(string s)
    {
        int i = s.Length;
        while (i > 0 && IsSpace(s[i - 1])) i--;
        return s[..i];
    }

    /// <summary>CString::Trim.</summary>
    public static string TrimC(string s) => TrimLeftC(TrimRightC(s));

    /// <summary>C isspace.</summary>
    public static bool IsSpace(char c) => c == ' ' || (c >= '\t' && c <= '\r');
}
