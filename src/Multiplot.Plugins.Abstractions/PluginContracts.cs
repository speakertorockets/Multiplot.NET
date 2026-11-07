// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

namespace Multiplot.Plugins;

/// <summary>Base contract of every plugin; discovered by reflection (public parameterless constructor).</summary>
public interface IMultiplotPlugin
{
    /// <summary>Short display name.</summary>
    string Name { get; }
    /// <summary>Version shown in Manage Plugins / About.</summary>
    string Version { get; }
    /// <summary>Description.</summary>
    string Description { get; }
    /// <summary>Initialise; return false to be unloaded.</summary>
    bool Initialize(IPluginHost host);
    /// <summary>Stop threads and release files.</summary>
    void Shutdown();
}

/// <summary>A data-source plugin.</summary>
public interface IDataSourceProvider : IMultiplotPlugin
{
    /// <summary>4-byte format id; must not collide with SUN ids or other plugins.</summary>
    uint Signature { get; }
    /// <summary>Format description.</summary>
    string FormatDescription { get; }
    /// <summary>Last status.</summary>
    int LastStatus { get; }
    /// <summary>True when the provider can open the file (may inspect content).</summary>
    bool IsSupported(string path);
    /// <summary>Opens the file; throws <see cref="DataAccessException"/> on failure.</summary>
    IDataFile Open(string path);
    /// <summary>Executes a library command text.</summary>
    int ExecuteCommand(string command);
    /// <summary>Generic library option 1001..1012 (id).</summary>
    int SetOption(int optionId);
}

/// <summary>An open data file.</summary>
public interface IDataFile : IDisposable
{
    /// <summary>File information.</summary>
    DataFileInfo Info { get; }
    /// <summary>Parameter map.</summary>
    IReadOnlyList<ParameterInfo> Parameters { get; }
    /// <summary>File events.</summary>
    IReadOnlyList<FileEvent> Events { get; }
    /// <summary>Comment block text or null.</summary>
    string? CommentBlock { get; }
    /// <summary>Refreshes a dynamic file.</summary>
    RefreshResult Refresh(bool clearEventFlag);
    /// <summary>Reads a parameter; throws <see cref="DataAccessException"/> on failure.</summary>
    ParameterData? Read(ParameterRequest request);
    /// <summary>Finds the map position of a name (−1 if not found) and which name list matched (0 primary, 1 alternate, −1 extended).</summary>
    int FindParameter(string name, out int nameList);
}

/// <summary>A command contributed to the Data menu.</summary>
public sealed record PluginCommand(string Caption, Action Execute, Func<bool>? IsChecked = null);

/// <summary>Optional: contributes up to 20 commands to the Data menu.</summary>
public interface IMenuContributor
{
    /// <summary>Commands (max 20).</summary>
    IReadOnlyList<PluginCommand> Commands { get; }
}

/// <summary>Expression functions <c>fn(name …)</c> / <c>refprop(fn …)</c>.</summary>
public interface IExpressionFunctionProvider : IMultiplotPlugin
{
    /// <summary>Function names served.</summary>
    IReadOnlyList<string> FunctionNames { get; }
    /// <summary>Evaluates the function; returns null and an error message on failure.</summary>
    ParameterData? Invoke(string function, string options, IReadOnlyList<ParameterData> args, out string? errorMessage);
}

/// <summary>Message box buttons used by <see cref="IUserInteraction"/>.</summary>
public enum PromptButtons
{
    /// <summary>OK.</summary>
    Ok,
    /// <summary>OK / Cancel.</summary>
    OkCancel,
    /// <summary>Yes / No.</summary>
    YesNo,
    /// <summary>Yes / No / Cancel.</summary>
    YesNoCancel,
    /// <summary>Abort / Retry / Ignore.</summary>
    AbortRetryIgnore,
}

/// <summary>Message box icon.</summary>
public enum PromptIcon
{
    /// <summary>None.</summary>
    None,
    /// <summary>Information.</summary>
    Information,
    /// <summary>Warning / exclamation.</summary>
    Warning,
    /// <summary>Error / stop.</summary>
    Error,
    /// <summary>Question.</summary>
    Question,
}

/// <summary>Result of a prompt.</summary>
public enum PromptResult
{
    /// <summary>OK.</summary>
    Ok,
    /// <summary>Cancel.</summary>
    Cancel,
    /// <summary>Yes.</summary>
    Yes,
    /// <summary>No.</summary>
    No,
    /// <summary>Abort.</summary>
    Abort,
    /// <summary>Retry.</summary>
    Retry,
    /// <summary>Ignore.</summary>
    Ignore,
}

/// <summary>Message boxes and simple input routed through the host (hidden mode answers automatically).</summary>
public interface IUserInteraction
{
    /// <summary>Shows a message; returns the button pressed.</summary>
    PromptResult Show(string message, string caption, PromptButtons buttons, PromptIcon icon, PromptResult defaultResult);
    /// <summary>Asks for a text value; null when cancelled.</summary>
    string? Input(string prompt, string caption, string defaultValue);
}

/// <summary>Services offered by the host application to plugins.</summary>
public interface IPluginHost
{
    /// <summary>User folder (<c>%USERPROFILE%\My Multiplot</c>).</summary>
    string UserFolder { get; }
    /// <summary>Temp folder.</summary>
    string TempFolder { get; }
    /// <summary>A script is running.</summary>
    bool ScriptRunning { get; }
    /// <summary>Dialogs are allowed.</summary>
    bool DialogsAllowed { get; }
    /// <summary>Appends a line to the script being recorded.</summary>
    void RecordScriptLine(string line);
    /// <summary>Writes to the script log / diagnostics.</summary>
    void Log(string message);
    /// <summary>User interaction.</summary>
    IUserInteraction Ui { get; }
}

/// <summary>
/// Optional: files with a common time index;
/// used for EOF references and GMT → index time conversion.
/// </summary>
public interface ITimeIndexedFile
{
    /// <summary>True when parameters share one time index (COMPRESSED, COMMONTIME, UNCOMPRESSED, CIRCULAR).</summary>
    bool HasCommonTimeIndex { get; }
    /// <summary>Last time value of the TIME index, null if unavailable.</summary>
    double? IndexEndTime();
    /// <summary>TIME index value at the first point whose GMT is at/after <paramref name="gmt"/>; false if no GMT index.</summary>
    bool TryIndexTimeAtGmt(TimeTag gmt, out double time);
    /// <summary>File data rate (forces the TIME index load when unknown).</summary>
    float FileRate { get; }
}
