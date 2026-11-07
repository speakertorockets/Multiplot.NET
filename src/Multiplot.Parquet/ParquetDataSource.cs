// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

using Multiplot.Plugins;

namespace Multiplot.Parquet;

/// <summary>
/// Data-source plugin for Apache Parquet time-series files: the Multiplot archive convention
/// (<c>multiplot.timeseries.wide/1</c>, written by <c>multi2parquet</c>) and generic files with a TIMESTAMP,
/// INT64 ticks or relative-seconds time column. Parquet files are static: no real-time refresh.
/// </summary>
public sealed class ParquetDataSource : IDataSourceProvider
{
    /// <summary>"PAR1" (the Parquet magic) read as a little-endian uint32.</summary>
    public const uint ParquetSignature = 0x31524150;

    private IPluginHost? _host;
    internal ParquetReadOptions Options { get; } = new();

    /// <inheritdoc/>
    public string Name => "Parquet";
    /// <inheritdoc/>
    public string Version => "Parquet data access 1.0.0 (Parquet.Net 6.1.0)";
    /// <inheritdoc/>
    public string Description => "Apache Parquet time-series archives (*.parquet)";
    /// <inheritdoc/>
    public uint Signature => ParquetSignature;
    /// <inheritdoc/>
    public string FormatDescription => "Apache Parquet";
    /// <inheritdoc/>
    public int LastStatus { get; private set; }

    /// <inheritdoc/>
    public bool Initialize(IPluginHost host)
    {
        _host = host;
        return true;
    }

    /// <inheritdoc/>
    public void Shutdown() => _host = null;

    /// <summary>True when the file starts and ends with the Parquet magic "PAR1".</summary>
    public bool IsSupported(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (fs.Length < 12) return false;
            Span<byte> b = stackalloc byte[4];
            fs.ReadExactly(b);
            if (!IsMagic(b)) return false;
            fs.Seek(-4, SeekOrigin.End);
            fs.ReadExactly(b);
            return IsMagic(b);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool IsMagic(ReadOnlySpan<byte> b) => b[0] == (byte)'P' && b[1] == (byte)'A' && b[2] == (byte)'R' && b[3] == (byte)'1';

    /// <inheritdoc/>
    public IDataFile Open(string path)
    {
        try
        {
            var f = ParquetDataFile.Open(path, this);
            LastStatus = 0;
            return f;
        }
        catch (DataAccessException ex)
        {
            LastStatus = ex.Code;
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastStatus = (int)SunError.FileOpen;
            throw new DataAccessException(SunError.FileOpen);
        }
        catch (Exception ex)
        {
            // corrupt / unsupported Parquet content
            LastStatus = (int)SunError.UnknownFormat;
            _host?.Log($"Parquet: cannot open {path}: {ex.Message}");
            throw new DataAccessException((int)SunError.UnknownFormat, "Parquet file not readable: " + ex.Message);
        }
    }

    /// <inheritdoc/>
    public int ExecuteCommand(string command)
    {
        _host?.Log("Parquet plugin command: " + command);
        return 0;
    }

    /// <summary>Generic options: 1007/1008 extrapolation (lead-in/trailing point) as for MULTITIME files.</summary>
    public int SetOption(int optionId)
    {
        switch (optionId)
        {
            case 1007: Options.AllowExtrapolation = true; return 1;
            case 1008: Options.AllowExtrapolation = false; return 0;
            default: return 0;
        }
    }

    internal void Log(string message) => _host?.Log(message);
}

/// <summary>Options shared by the files of one provider.</summary>
internal sealed class ParquetReadOptions
{
    public bool AllowExtrapolation { get; set; } = true;
}
