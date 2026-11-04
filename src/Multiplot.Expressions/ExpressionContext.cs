using Multiplot.Core.Model;
using Multiplot.Plugins;

namespace Multiplot.Expressions;

/// <summary>Services the expression engine needs from the application (data access, options, prompts).</summary>
public interface IExpressionContext
{
    /// <summary>All open files.</summary>
    IReadOnlyList<IDataFile> AllFiles { get; }
    /// <summary>All groups.</summary>
    IReadOnlyList<FileGroup> Groups { get; }
    /// <summary>Reads a float parameter; null on failure (status in <see cref="LastStatus"/>).</summary>
    ParameterData? Read(IDataFile file, string name, double start, double stop, double? refTime, TimeTag? refGmt, bool returnGmt);
    /// <summary>Status of the last failed read.</summary>
    int LastStatus { get; }
    /// <summary>64-bit (GMT based) timing.</summary>
    bool Use64BitTime { get; }
    /// <summary>CALCEVERYTIME.</summary>
    bool CalcEveryInstance { get; }
    /// <summary>ALIGNCLOSEST.</summary>
    bool AlignClosest { get; }
    /// <summary>Default avg()/quantize() interval.</summary>
    float AverageRate { get; }
    /// <summary>Default avg() time stamp mode.</summary>
    int TimeStampMode { get; }
    /// <summary>Dialogs may be shown.</summary>
    bool ShowDialogs { get; }
    /// <summary>A script is running.</summary>
    bool ScriptRunning { get; }
    /// <summary>SCRIPTPROMPT.</summary>
    bool ScriptPrompt { get; }
    /// <summary>Auto refresh is on.</summary>
    bool AutoRefresh { get; }
    /// <summary>Invalid-value filtering of min/max scans (library option 1009/1010).</summary>
    bool FilterInvalid { get; }
    /// <summary>Message boxes.</summary>
    IUserInteraction Ui { get; }
    /// <summary>Status bar message.</summary>
    void SetStatus(string text);
    /// <summary>Expression function plugins (fn(), refprop()).</summary>
    IReadOnlyList<IExpressionFunctionProvider> FunctionProviders { get; }
}

/// <summary>Microsoft C runtime <c>rand()</c> (LCG, seed 1).</summary>
public static class MsvcRand
{
    private static uint _seed = 1;

    /// <summary>Next value 0..32767.</summary>
    public static int Next()
    {
        _seed = unchecked(_seed * 214013u + 2531011u);
        return (int)((_seed >> 16) & 0x7fff);
    }

    /// <summary>srand.</summary>
    public static void Seed(uint s) => _seed = s;
}
