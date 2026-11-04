namespace Multiplot.Sun;

/// <summary>
/// SUN library options,
/// set with the generic option ids 1001..1012 (FR-SUN-077). One instance per provider.
/// </summary>
public sealed class SunLibraryOptions
{
    /// <summary>1001/1002: rebuild change-only MULTITIME data to full rate (default off).</summary>
    public bool MakeFullRate { get; set; }
    /// <summary>1003/1004: application script running.</summary>
    public bool ScriptRunning { get; set; }
    /// <summary>1005/1006: dialogs allowed during scripts.</summary>
    public bool AllowDialogs { get; set; } = true;
    /// <summary>1007/1008: MULTITIME lead-in / trailing value extrapolation (default on).</summary>
    public bool AllowExtrapolation { get; set; } = true;
    /// <summary>1009/1010: invalid-value filtering (default on).</summary>
    public bool FilterInvalid { get; set; } = true;
    /// <summary>1011/1012: retained MULTITIME lookup tables (default off).</summary>
    public bool RetainedLookup { get; set; }

    /// <summary>Returns the new flag value or −1 for unknown ids.</summary>
    public int Apply(int id)
    {
        switch (id)
        {
            case 1001: MakeFullRate = true; return 1;
            case 1002: MakeFullRate = false; return 0;
            case 1003: ScriptRunning = true; return 1;
            case 1004: ScriptRunning = false; return 0;
            case 1005: AllowDialogs = true; return 1;
            case 1006: AllowDialogs = false; return 0;
            case 1007: AllowExtrapolation = true; return 1;
            case 1008: AllowExtrapolation = false; return 0;
            case 1009: FilterInvalid = true; return 1;
            case 1010: FilterInvalid = false; return 0;
            case 1011: RetainedLookup = true; return 1;
            case 1012: RetainedLookup = false; return 0;
            default: return -1;
        }
    }
}
