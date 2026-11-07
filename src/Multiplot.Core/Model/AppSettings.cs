// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

namespace Multiplot.Core.Model;

/// <summary>A user defined function button.</summary>
public sealed class UserButton
{
    /// <summary>Label.</summary>
    public string Label = "";
    /// <summary>Script command.</summary>
    public string Command = "";
}

/// <summary>Script pause modes.</summary>
public static class PauseMode
{
    /// <summary>Pause after the next command.</summary>
    public const int NextCommand = 1;
    /// <summary>Pause after the next PLOT.</summary>
    public const int NextPlot = 2;
}

/// <summary>
/// Application wide settings with their defaults. Everything the INI file
/// (chapter 09) reads or writes lives here.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Number of user buttons.</summary>
    public const int NUserButtons = 5;
    /// <summary>Number of logic label sets.</summary>
    public const int NLogicLabels = 4;

    /// <summary>Default plot configuration for new plots.</summary>
    public PlotConfig Plot = new();

    // [paths] and folders
    public string FileSpec = "", DbFolder = "", ScriptDir = "", WpdFolder = "", ExportDir = "", QueryDir = "";
    public string TempDir = "", UserFolder = "", StartupPath = "", IniFile = "", FavFile = "";
    /// <summary>Runtime only (never saved): the user INI was created during this start-up.</summary>
    public bool FirstRun;

    // [access time]
    public string TStart = "-3.4e+038", TStop = "+3.4e+038", TRef = "NONE";

    // [options]
    public string FileFilters = "All Files(*.*)|*.*|Parquet Files (*.parquet)|*.parquet|025 Files|*.025|050 Files|*.050|250 Files|*.250|Filter Files (*.flt)|*.flt||";
    public bool AutoSave = true, AlignClosest = true, CalcEveryInstance, UsePlaceholder, ExitSafety, RebuildRate = true;
    public bool TabHistory = true, MaxPresentation = true, FirstOccurrence, Extrapolate, ShowSysLoad = true, ShowCoord = true;
    public int Monitors, OptimizedDraw = 1, OffscreenDraw = 1;

    // titles / nomenclature
    public string XTitle = "Multiplot", YTitle = "Parameter", XUdef = "", YUdef = "";
    public string UtcFormat = "%03u/%02u:%02u:%02u.%03u";

    // [printer] (stored only)
    public string UserPrinter = "", TextPrinter = "notepad /p";
    public float MarginSize = 5f;
    public bool ColorPrinter = true, Landscape = true;

    // [realtime]
    public bool IgnoreStatic;
    public uint CompactPoints, DiscardSeconds;
    public float CompactInterval;
    public int PreviousRefresh;
    public int PcgCollectorSize = 16384;

    // [mouse]
    public int RbFunction;
    public bool PopupsGlobal;

    // [plot style] frame members
    public bool ContinueTitle = true, ContinueSubTitle = true, UseMarkers, EmfWide, AllowAutoLabel = true, ApplyAll;
    public bool PlotMax = true, ExtNomen, AltNames;
    public int ReservedLegend, MinGridOffset, MultiGroupBy = GroupBy.Independent, NoCaptionBar, LogicTxtIdx;
    public float TabStepRate = 1f;
    public readonly string[] LogicTxtTrue = { "ON", "OPEN", "HIGH", "TRUE" };
    public readonly string[] LogicTxtFalse = { "OFF", "CLOSED", "LOW", "FALSE" };

    // [functions]
    public int TimeStampMode = 1;
    public float AverageRate = 1f;

    // [external applications]
    public string EditApp = "", ExportApp = "";
    public int DataPrecision = -1, TimePrecision = -1;
    public bool SharedTabTime, ShowUtc;

    // [scripting]
    public bool ScriptPrompt = true, EmbeddedScript;
    public int PauseMode = Model.PauseMode.NextPlot;

    // [interface]
    public bool ShowStatusBar = true;
    /// <summary>Dark appearance (application and plots, FR-UI-046); false = light.</summary>
    public bool DarkMode;
    public readonly UserButton[] UserButtons = Enumerable.Range(0, NUserButtons).Select(_ => new UserButton()).ToArray();

    // [symbolics]
    public bool LoadSymbols;
    /// <summary>User symbols as "NAME\tvalue" strings.</summary>
    public readonly List<string> UserVars = new();

    // session flags that are not stored in the INI
    public bool StreamCert = true, PcgHistory = true, ShowUtcDefault;
}
