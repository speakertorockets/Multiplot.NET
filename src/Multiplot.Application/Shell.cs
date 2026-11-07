// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

using Multiplot.Core.Model;
using Multiplot.ImportExport;
using Multiplot.Plugins;

namespace Multiplot.Application;

/// <summary>Plot window arrangement requests.</summary>
public enum WindowArrange
{
    /// <summary>Tile horizontally.</summary>
    TileHorizontal,
    /// <summary>Tile vertically.</summary>
    TileVertical,
    /// <summary>Cascade.</summary>
    Cascade,
    /// <summary>Stack horizontally.</summary>
    Stack,
    /// <summary>Maximise the active plot.</summary>
    MaximizeActive,
    /// <summary>Minimise the active plot.</summary>
    MinimizeActive,
}

/// <summary>Result of the generic input dialog.</summary>
public sealed record InputResult(string Text, bool ApplyAll);

/// <summary>Request for the generic input dialog.</summary>
public sealed record InputRequest(string Prompt, string Default, string Caption, IReadOnlyList<string>? Choices = null,
    bool ShowApplyAll = false, bool ApplyAll = false, bool ListOnly = false);

/// <summary>Parameter Selection dialog modes.</summary>
public enum ParmSelectMode
{
    /// <summary>Full dialog (Y list, X axis, plot checkbox).</summary>
    Normal = 0,
    /// <summary>Add parameter to the active plot.</summary>
    AddParameter = 1,
    /// <summary>Select the X axis parameter.</summary>
    XAxis = 2,
}

/// <summary>Dialogs that operate directly on the session.</summary>
public enum DialogKind
{
    SliceAverage, XNomenclature, YNomenclature, MarkerEdit, MarkerDraw, RedlineEdit, RedlineDraw, Favorites, FilterHelper, FileProperties, ParameterProperties,
    SymbolicNames, FilePaths, ExternalApplications, ManagePlugins, AutoRun, AutoCompact, AutoDiscard, WindowList, About,
    TimeAlign, LabelFont, TitleFont, UserButtonAssign, DataPick, RefreshInterval, WindowPosition, RightButton,
    AverageRate, AverageTimeStamp, Monitors, LayoutGrid,
}

/// <summary>Fix X / Fix Y dialog data.</summary>
public sealed class RangeRequest
{
    public string Prompt = "";
    public string Min = "", Max = "", Increment = "Auto";
    public bool Persist, ApplyAll, DisableApplyAll;
}

/// <summary>
/// Modal dialogs of the application. Every method returns null/false when cancelled; the headless shell
/// cancels everything (scripts never depend on a dialog result unless prompting is enabled).
/// </summary>
public interface IDialogs
{
    /// <summary>Open file dialog; returns one or more paths.</summary>
    string[]? OpenFile(string title, string filter, string? initialDir, bool multiSelect = false);
    /// <summary>Save file dialog.</summary>
    string? SaveFile(string title, string filter, string? defaultName, string? initialDir);
    /// <summary>Generic input dialog.</summary>
    InputResult? Input(InputRequest request);
    /// <summary>Parameter Selection dialog (updates the session's X/Y lists); true on OK, <paramref name="plot"/> = plot checkbox.</summary>
    bool ParameterSelect(Session session, ParmSelectMode mode, out bool plot);
    /// <summary>Time Slice dialog (global or per group).</summary>
    bool TimeSlice(Session session, bool perGroup);
    /// <summary>Fix X / Fix Y dialog.</summary>
    bool Range(RangeRequest request);
    /// <summary>Trace attributes dialog for a plot.</summary>
    bool TraceAttributes(Session session, PlotDocument plot, int trace, out bool applyAll);
    /// <summary>Interactive ASCII import dialog.</summary>
    bool Import(AsciiImporter importer);
    /// <summary>Colour chooser; null when cancelled.</summary>
    int? ChooseColor(int initialColorRef, int[] customColors);
    /// <summary>Any other dialog; false when cancelled or not available.</summary>
    bool Show(Session session, DialogKind kind);
}

/// <summary>UI services required by the session (implemented by the WPF shell and by a headless shell for tests).</summary>
public interface IShell
{
    /// <summary>Message boxes and input (auto-answers in hidden mode).</summary>
    IUserInteraction Ui { get; }
    /// <summary>Dialogs.</summary>
    IDialogs Dialogs { get; }
    /// <summary>A plot was created; the shell creates its window.</summary>
    void PlotAdded(PlotDocument plot, bool maximized);
    /// <summary>A plot was removed.</summary>
    void PlotRemoved(PlotDocument plot);
    /// <summary>Brings a plot to the front.</summary>
    void Activate(PlotDocument plot);
    /// <summary>Requests a redraw of a plot.</summary>
    void Invalidate(PlotDocument plot);
    /// <summary>Updates the caption bar of a plot.</summary>
    void UpdateCaption(PlotDocument plot);
    /// <summary>Client size of a plot window (render size of image exports).</summary>
    (int Width, int Height) PlotSize(PlotDocument plot);
    /// <summary>True when the plot window is maximised.</summary>
    bool IsMaximized(PlotDocument plot);
    /// <summary>Arranges plot windows.</summary>
    void Arrange(WindowArrange mode);
    /// <summary>Main window state: 0 restore, 1 minimise, 2 maximise.</summary>
    void MainWindowState(int state);
    /// <summary>Moves the main window.</summary>
    void MoveMainWindow(int left, int top, int right, int bottom);
    /// <summary>Shows or hides the status bar.</summary>
    void ShowStatusBar(bool show);
    /// <summary>Applies the light or dark appearance to the application windows (FR-UI-046).</summary>
    void SetAppearance(bool dark);
    /// <summary>Presentation mode.</summary>
    void SetPresentationMode(bool on, bool fullScreen);
    /// <summary>Status bar message.</summary>
    void SetStatus(string text);
    /// <summary>Real-time clock pane.</summary>
    void SetClock(string text);
    /// <summary>Copies a plot image to the clipboard.</summary>
    void CopyPlotToClipboard(PlotDocument plot);
    /// <summary>Copies text to the clipboard.</summary>
    void CopyTextToClipboard(string text);
    /// <summary>Clipboard text (null if none).</summary>
    string? GetClipboardText();
    /// <summary>Closes the application (normal shutdown path).</summary>
    void RequestExit();
    /// <summary>Lets the UI process pending messages.</summary>
    Task YieldAsync();
    /// <summary>True while the space bar is held.</summary>
    bool IsSpacePressed();
    /// <summary>True while Shift is held.</summary>
    bool IsShiftPressed();
    /// <summary>Opens a local file in the default web browser; false when no browser could be started.</summary>
    bool OpenInBrowser(string path);
    /// <summary>Menus must be refreshed (labels, check states).</summary>
    void RefreshCommands();
    /// <summary>Menus must be rebuilt (settings loaded, plugins changed).</summary>
    void RebuildCommands();
    /// <summary>Starts or restarts a named timer on the UI thread.</summary>
    void StartTimer(string name, int intervalMs, Action tick);
    /// <summary>Stops a named timer.</summary>
    void StopTimer(string name);
}
