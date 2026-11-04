namespace Multiplot.Plugins;

/// <summary>Generic file information.</summary>
public sealed class DataFileInfo
{
    /// <summary>Full path (or source name) of the file.</summary>
    public string Path { get; set; } = "";
    /// <summary>Signature as a little-endian uint32 value (e.g. 0xF2FFFFFF), or plugin signature.</summary>
    public uint FileSig { get; set; }
    /// <summary>Format description.</summary>
    public string FormatDescription { get; set; } = "";
    /// <summary>Header reference time: year, Julian day, hour, minute, second, millisecond.</summary>
    public uint[] RefTime { get; set; } = new uint[6];
    /// <summary>File reference GMT (header reference, or the FILE_REF event).</summary>
    public TimeTag RefGmt { get; set; }
    /// <summary>Number of parameters.</summary>
    public uint Items { get; set; }
    /// <summary>Points (or update counter for MULTITIME formats).</summary>
    public uint Points { get; set; }
    /// <summary>File is real-time capable.</summary>
    public bool IsDynamic { get; set; }
    /// <summary>Engine cutoff time (relative seconds).</summary>
    public float Cutoff { get; set; }
    /// <summary>Header title.</summary>
    public string Title { get; set; } = " ";
    /// <summary>Header comment.</summary>
    public string Comment { get; set; } = " ";
    /// <summary>Engine serial number.</summary>
    public string SerialNum { get; set; } = " ";
    /// <summary>Test stand.</summary>
    public string TestStand { get; set; } = " ";
    /// <summary>Test position.</summary>
    public string TestPosition { get; set; } = " ";
    /// <summary>Test type.</summary>
    public string TestType { get; set; } = " ";
    /// <summary>Test number.</summary>
    public string TestNumber { get; set; } = " ";
    /// <summary>Optional certification status text (streams).</summary>
    public string? CertStatus { get; set; }
}

/// <summary>Parameter map entry.</summary>
public sealed class ParameterInfo
{
    /// <summary>Map position (0-based).</summary>
    public int MapPosition { get; set; }
    /// <summary>Names: primary, alternate, extended (extended may be null).</summary>
    public string?[] Names { get; } = new string?[3];
    /// <summary>Units.</summary>
    public string Units { get; set; } = " ";
    /// <summary>Descriptions: primary, extended (may be null).</summary>
    public string?[] Descriptions { get; } = new string?[2];
    /// <summary>Two character type id ("f4", "i2", ...).</summary>
    public string TypeId { get; set; } = "f4";
    /// <summary>Type differs from the default f4.</summary>
    public bool ExtendedType { get; set; }
    /// <summary>Data rate: −1 unknown, 0 variable, &gt; 0 Hz.</summary>
    public float Rate { get; set; } = -1f;
    /// <summary>Sequence number (bit 31 = rate packed / rebuildable).</summary>
    public uint SequenceNumber { get; set; }
    /// <summary>Primary name convenience accessor.</summary>
    public string Name => Names[0] ?? " ";
    /// <summary>Alternate name convenience accessor.</summary>
    public string AltName => Names[1] ?? " ";
    /// <summary>Primary description convenience accessor.</summary>
    public string Description => Descriptions[0] ?? " ";
    /// <summary>True when the rate was packed in the sequence number or given in [rates].</summary>
    public bool RateRebuildable => (SequenceNumber & 0x80000000u) != 0;
}

/// <summary>Named GMT event from the comment block <c>[events]</c>.</summary>
public readonly record struct FileEvent(string Name, TimeTag Gmt);

/// <summary>Bit set returned by a refresh (FR-ACC-005).</summary>
[Flags]
public enum RefreshFlags
{
    /// <summary>File unchanged.</summary>
    None = 0,
    /// <summary>New data.</summary>
    NewData = 1,
    /// <summary>Reference changed (map re-read).</summary>
    ReferenceChanged = 2,
    /// <summary>Events added.</summary>
    EventsAdded = 4,
    /// <summary>Dynamic (stream) event.</summary>
    DynamicEvent = 8,
}

/// <summary>Refresh result: flags, or a negative status on error.</summary>
public readonly record struct RefreshResult(int Status)
{
    /// <summary>True when the status is an error.</summary>
    public bool IsError => Status < 0;
    /// <summary>Flags when not an error.</summary>
    public RefreshFlags Flags => Status < 0 ? RefreshFlags.None : (RefreshFlags)Status;
}

/// <summary>Parameter read request (FR-ACC-004).</summary>
public sealed record ParameterRequest(
    string Name,
    double Start,
    double Stop,
    double? RefTime = null,
    TimeTag? RefGmt = null,
    DataType ReturnType = DataType.Float,
    bool ReturnGmt = false);
