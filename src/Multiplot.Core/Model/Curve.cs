using Multiplot.Plugins;

namespace Multiplot.Core.Model;

/// <summary>
/// A plotted curve: retrieved data plus the application-owned attribute slots
/// (REDLINE, PACK_POSITION, TAB_POSITION, ATTRIBUTES).
/// </summary>
public sealed class Curve
{
    /// <summary>Retrieved data (float).</summary>
    public ParameterData Data;

    /// <summary>Line size 1..3.</summary>
    public int LineSize = 1;
    /// <summary>Line style 0..4 (PS_SOLID … PS_DASHDOTDOT).</summary>
    public int LineStyle;
    /// <summary>Draw symbols.</summary>
    public bool UseSymbols;
    /// <summary>Scatter rendering for this trace.</summary>
    public bool ScatterPlot;
    /// <summary>Symbol id.</summary>
    public int SymbolId;
    /// <summary>Colour index 0..99.</summary>
    public int ColorIndex;
    /// <summary>Tabular value format −7..7.</summary>
    public int TabFormat = Core.TabFormat.General;
    /// <summary>Show nomenclature in tabular style.</summary>
    public bool TabLegend = true;
    /// <summary>Logic label set 0..3.</summary>
    public int LogicTxt;

    /// <summary>Redline flag: 0 none, 1 MAX/HI (upper), 2 MIN/LO (lower), 3 generic.</summary>
    public int Redline;
    /// <summary>Position of packed data (real-time compaction).</summary>
    public int PackPosition;
    /// <summary>Tabular position: low word x, high word y (0 = undefined).</summary>
    public uint TabPosition;

    /// <summary>Assigned Y scale.</summary>
    public int ScaleIndex;
    /// <summary>Assigned scale name.</summary>
    public string ScaleName = "";

    /// <summary>End-to-end info.</summary>
    public float Duration, Offset;
    /// <summary>Group and file indexes of the source.</summary>
    public int GroupNo, FileNo;

    /// <summary>The list item that produced the curve (expression, function or name; symbolic form kept).</summary>
    public string SourceItem = "";
    /// <summary>The X item (parameter-vs-parameter).</summary>
    public string? XItem;
    /// <summary>The time stamp parameter of a parameter-vs-parameter curve.</summary>
    public ParameterData? OriginalTime;

    /// <summary>Creates a curve.</summary>
    public Curve(ParameterData data) => Data = data;

    /// <summary>Shortcut: number of points.</summary>
    public int Points => Data.Points;
    /// <summary>Shortcut: time array.</summary>
    public float[] Time => Data.Time;
    /// <summary>Shortcut: values.</summary>
    public float[] Values => Data.Values;
    /// <summary>Shortcut: name.</summary>
    public string Name => Data.Name;

    /// <summary>Min value used for scaling.</summary>
    public double VMin { get => Data.MinValue; set => Data.MinValue = value; }
    /// <summary>Max value used for scaling.</summary>
    public double VMax { get => Data.MaxValue; set => Data.MaxValue = value; }

    /// <summary>Tab X (low word).</summary>
    public int TabX => (int)(TabPosition & 0xffff);
    /// <summary>Tab Y (high word).</summary>
    public int TabY => (int)(TabPosition >> 16);

    /// <summary>Copies display attributes from another curve.</summary>
    public void CopyAttributes(Curve c)
    {
        LineSize = c.LineSize; LineStyle = c.LineStyle; UseSymbols = c.UseSymbols; ScatterPlot = c.ScatterPlot;
        SymbolId = c.SymbolId; ColorIndex = c.ColorIndex; TabFormat = c.TabFormat; TabLegend = c.TabLegend;
        LogicTxt = c.LogicTxt; Redline = c.Redline; TabPosition = c.TabPosition; ScaleIndex = c.ScaleIndex; ScaleName = c.ScaleName;
    }

    /// <summary>True when the source file is dynamic.</summary>
    public bool IsDynamic => Data.Source?.Info.IsDynamic ?? false;
}

/// <summary>A group of files loaded together.</summary>
public sealed class FileGroup
{
    /// <summary>Definition file.</summary>
    public string FileName = "";
    /// <summary>Display name.</summary>
    public string GroupName = "";
    /// <summary>Engine serial.</summary>
    public string EngineNo = "unknown";
    /// <summary>Engine cutoff.</summary>
    public string EngineCO = "unknown";
    /// <summary>Time reference.</summary>
    public string TRef = "NONE";
    /// <summary>Start time.</summary>
    public string TStart = "-3.4e+038";
    /// <summary>Stop time.</summary>
    public string TStop = "+3.4e+038";
    /// <summary>Number of files.</summary>
    public int NFiles;
    /// <summary>First file index.</summary>
    public int StartPos;
}

/// <summary>Services of the application used by plot documents and the renderer.</summary>
public interface IPlotHost
{
    /// <summary>Open files in order.</summary>
    IReadOnlyList<IDataFile> Files { get; }
    /// <summary>Groups in order.</summary>
    IReadOnlyList<FileGroup> Groups { get; }
    /// <summary>Logic "true" labels.</summary>
    string[] LogicTxtTrue { get; }
    /// <summary>Logic "false" labels.</summary>
    string[] LogicTxtFalse { get; }
    /// <summary>Include redline markers in autoscale.</summary>
    bool ScaleRedlines { get; }
    /// <summary>Version string.</summary>
    string Version { get; }
    /// <summary>Presentation mode.</summary>
    bool PresentationMode { get; }
    /// <summary>Tabular layout grid spacing (0 off).</summary>
    int LayoutGridSpacing { get; }
    /// <summary>Snap to grid.</summary>
    bool SnapToGrid { get; }
    /// <summary>Auto refresh running.</summary>
    bool AutoRefreshOn { get; }
    /// <summary>Show stream certification.</summary>
    bool StreamCert { get; }
    /// <summary>Data precision.</summary>
    int DataPrecision { get; }
    /// <summary>Time precision.</summary>
    int TimePrecision { get; }
    /// <summary>Minimum grid offset percent.</summary>
    int MinGridOffset { get; }
    /// <summary>Reserved legend rows.</summary>
    int ReservedLegend { get; }
    /// <summary>Real-time data loaded.</summary>
    bool RtLoaded { get; }
    /// <summary>Resolves an event/GMT/time label against the files of group <paramref name="group"/>.</summary>
    bool ResolveTime(int group, string spec, string tRef, out double time);
    /// <summary>Shows a message.</summary>
    void ShowMessage(string text);
    /// <summary>Disables a marker of the master set.</summary>
    void DisableGlobalMarker(int markerNo);
}
