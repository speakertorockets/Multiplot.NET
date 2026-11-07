// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

using Multiplot.Plugins;

namespace MinimalPlugin;

/// <summary>
/// Minimal data-source plugin. Any existing file with the
/// extension ".min" is accepted; its content is not read. Three parameters of 32 points at 1 sample/s
/// are generated on the fly: UPDATA (count up), DOWNDATA (count down) and UPDOWN (0..7 saw tooth).
/// </summary>
public sealed class MinimalDataSource : IDataSourceProvider, IMenuContributor
{
    private IPluginHost? _host;
    private bool _rebuildRate = true;

    /// <inheritdoc/>
    public string Name => "MinimalPlugin";
    /// <inheritdoc/>
    public string Version => "Minimal (demo reader lib) 1.0";
    /// <inheritdoc/>
    public string Description => "Minimal Data Access - example plug-in";
    /// <inheritdoc/>
    public uint Signature => 0x11111111u;
    /// <inheritdoc/>
    public string FormatDescription => "Minimal demo format (*.min)";
    /// <inheritdoc/>
    public int LastStatus { get; private set; }

    /// <inheritdoc/>
    public IReadOnlyList<PluginCommand> Commands => new[]
    {
        new PluginCommand("&About Minimal plugin", () => _host?.Ui.Show("About MinimalPlugin", "Minimal", PromptButtons.Ok, PromptIcon.Information, PromptResult.Ok)),
    };

    /// <inheritdoc/>
    public bool Initialize(IPluginHost host)
    {
        _host = host;
        return true;
    }

    /// <inheritdoc/>
    public void Shutdown() => _host = null;

    /// <inheritdoc/>
    public bool IsSupported(string path) => path.EndsWith(".min", StringComparison.OrdinalIgnoreCase) && File.Exists(path);

    /// <inheritdoc/>
    public IDataFile Open(string path)
    {
        if (!File.Exists(path))
        {
            LastStatus = -1;
            throw new DataAccessException(-1, "File not found: " + path);
        }
        LastStatus = 0;
        return new MinimalFile(path, Signature, FormatDescription);
    }

    /// <inheritdoc/>
    public int ExecuteCommand(string command)
    {
        _host?.Log("MinimalPlugin command: " + command);
        return 0;
    }

    /// <inheritdoc/>
    public int SetOption(int optionId)
    {
        switch (optionId)
        {
            case 1001: _rebuildRate = true; return 0;   // enable rebuild of data rate
            case 1002: _rebuildRate = false; return 0;  // disable rebuild of data rate
            default: return 0;
        }
    }

    internal bool RebuildRate => _rebuildRate;
}

/// <summary>An open ".min" file.</summary>
internal sealed class MinimalFile : IDataFile
{
    private const int NPoints = 32;
    private static readonly (string Name, string Description)[] Items =
    {
        ("UPDATA", "Data goes up"),
        ("DOWNDATA", "Data goes down"),
        ("UPDOWN", "Data goes up and down"),
    };

    private readonly List<ParameterInfo> _parameters = new();

    public MinimalFile(string path, uint signature, string format)
    {
        var now = DateTime.Now;
        Info = new DataFileInfo
        {
            Path = path,
            FileSig = signature,
            FormatDescription = format,
            RefTime = new uint[] { (uint)now.Year, (uint)now.DayOfYear, (uint)now.Hour, (uint)now.Minute, (uint)now.Second, (uint)now.Millisecond },
            Items = (uint)Items.Length,
            Points = NPoints,
            Title = "Pseudo File",
            Comment = "Example data",
            SerialNum = "serial number",
            TestStand = "Test Stand",
            TestPosition = "Test Position",
            TestType = "Demo",
            TestNumber = "0001",
        };
        for (int i = 0; i < Items.Length; i++)
        {
            var p = new ParameterInfo { MapPosition = i, Units = "Count", TypeId = "f4", Rate = 1f, SequenceNumber = (uint)(i + 1) };
            p.Names[0] = Items[i].Name;
            p.Names[1] = Items[i].Name;
            p.Descriptions[0] = Items[i].Description;
            _parameters.Add(p);
        }
    }

    public DataFileInfo Info { get; }
    public IReadOnlyList<ParameterInfo> Parameters => _parameters;
    public IReadOnlyList<FileEvent> Events => Array.Empty<FileEvent>();
    public string? CommentBlock => null;

    public RefreshResult Refresh(bool clearEventFlag) => new(0);

    public int FindParameter(string name, out int nameList)
    {
        string n = name.Trim().Trim('\'');
        for (int i = 0; i < _parameters.Count; i++)
        {
            if (string.Equals(_parameters[i].Names[0], n, StringComparison.OrdinalIgnoreCase)) { nameList = 0; return i; }
        }
        for (int i = 0; i < _parameters.Count; i++)
        {
            if (string.Equals(_parameters[i].Names[1], n, StringComparison.OrdinalIgnoreCase)) { nameList = 1; return i; }
        }
        nameList = 0;
        return -1;
    }

    public ParameterData? Read(ParameterRequest request)
    {
        int loc = FindParameter(request.Name, out _);
        if (loc < 0) return null;
        float offset = (float)(request.RefTime ?? 0);
        var time = new float[NPoints];
        var data = new float[NPoints];
        for (int p = 0; p < NPoints; p++)
        {
            time[p] = offset + p; // 1 sample per second
            data[p] = loc switch
            {
                0 => p,            // count up
                1 => NPoints - p,  // count down
                _ => p % 8,        // up/down
            };
        }
        int iMin = 0, iMax = 0;
        for (int p = 1; p < NPoints; p++)
        {
            if (data[p] < data[iMin]) iMin = p;
            if (data[p] > data[iMax]) iMax = p;
        }
        var info = _parameters[loc];
        return new ParameterData
        {
            Source = this,
            Name = info.Name,
            Units = info.Units,
            Description = info.Description,
            Type = DataType.Float,
            Points = NPoints,
            Time = time,
            Data = data,
            RefTime = request.RefTime,
            MinIndex = iMin,
            MaxIndex = iMax,
            MinValue = data[iMin],
            MaxValue = data[iMax],
            SequenceNumber = info.SequenceNumber,
            Rate = 1f,
            MapPosition = loc,
            TimeName = "TIME",
            TimeUnits = "Seconds",
            TimeDescription = "Time in Seconds",
        };
    }

    public void Dispose() { }
}
