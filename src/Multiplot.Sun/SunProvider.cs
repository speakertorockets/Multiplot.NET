using Multiplot.Core;
using Multiplot.Plugins;

namespace Multiplot.Sun;

/// <summary>The built-in SUN data source.</summary>
public sealed class SunProvider
{
    /// <summary>Library version string reported in About / Manage Plugins.</summary>
    public const string Version = "SunAcc.NET(x64) v1.0.0";

    /// <summary>Library options (generic ids 1001..1012).</summary>
    public SunLibraryOptions Options { get; } = new();

    /// <summary>Status of the last operation.</summary>
    public SunError LastStatus { get; private set; }

    /// <summary>Returns the SUN signature or 0; sets <see cref="LastStatus"/>.</summary>
    public uint IsSupportedType(string path)
    {
        try
        {
            uint sig = SunFile.ProbeSignature(path);
            LastStatus = SunError.Success;
            return sig;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            LastStatus = SunError.FileOpen;
            return 0;
        }
    }

    /// <summary>Opens a SUN file.</summary>
    public SunFile Open(string path)
    {
        try
        {
            var f = SunFile.Open(path, Options);
            LastStatus = SunError.Success;
            return f;
        }
        catch (DataAccessException ex)
        {
            LastStatus = ex.SunCode;
            throw;
        }
    }

    /// <summary>The text is an option id.</summary>
    public int ExecuteCommand(string command) => Options.Apply(Crt.Atoi(command));

    /// <summary>Library function by id.</summary>
    public int SetOption(int id) => Options.Apply(id);
}
