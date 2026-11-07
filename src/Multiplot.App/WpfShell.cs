// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Multiplot.Application;
using Multiplot.Core.Model;
using Multiplot.Plugins;
using Multiplot.Rendering;

namespace Multiplot.App;

/// <summary>Message boxes; in hidden mode every prompt is logged and answered with its default (DEV-SCR-003).</summary>
public sealed class WpfUi : IUserInteraction
{
    private readonly MainWindow _main;
    private readonly bool _hidden;

    /// <summary>Session for logging (set after creation).</summary>
    public Session? Session { get; set; }

    /// <summary>Creates the UI service.</summary>
    public WpfUi(MainWindow main, bool hidden)
    {
        _main = main;
        _hidden = hidden;
    }

    /// <inheritdoc/>
    public PromptResult Show(string message, string caption, PromptButtons buttons, PromptIcon icon, PromptResult defaultResult)
    {
        if (_hidden)
        {
            Session?.DiagLog("[hidden] " + message.Replace("\n", " | ") + " -> " + defaultResult);
            return defaultResult;
        }
        var b = buttons switch
        {
            PromptButtons.OkCancel => MessageBoxButton.OKCancel,
            PromptButtons.YesNo => MessageBoxButton.YesNo,
            PromptButtons.YesNoCancel => MessageBoxButton.YesNoCancel,
            PromptButtons.AbortRetryIgnore => MessageBoxButton.YesNoCancel,
            _ => MessageBoxButton.OK,
        };
        var i = icon switch
        {
            PromptIcon.Information => MessageBoxImage.Information,
            PromptIcon.Warning => MessageBoxImage.Warning,
            PromptIcon.Error => MessageBoxImage.Error,
            PromptIcon.Question => MessageBoxImage.Question,
            _ => MessageBoxImage.None,
        };
        string text = buttons == PromptButtons.AbortRetryIgnore ? message + "\n\nYes = Abort, No = Retry, Cancel = Ignore" : message;
        var owner = _main.IsVisible ? _main : null;
        var r = owner != null ? MessageBox.Show(owner, text, caption, b, i) : MessageBox.Show(text, caption, b, i);
        if (buttons == PromptButtons.AbortRetryIgnore)
            return r == MessageBoxResult.Yes ? PromptResult.Abort : r == MessageBoxResult.No ? PromptResult.Retry : PromptResult.Ignore;
        return r switch
        {
            MessageBoxResult.OK => PromptResult.Ok,
            MessageBoxResult.Cancel => PromptResult.Cancel,
            MessageBoxResult.Yes => PromptResult.Yes,
            MessageBoxResult.No => PromptResult.No,
            _ => defaultResult,
        };
    }

    /// <inheritdoc/>
    public string? Input(string prompt, string caption, string defaultValue)
    {
        if (_hidden) return defaultValue;
        var r = InputDialog.Ask(_main, new InputRequest(prompt, defaultValue, caption.Length == 0 ? "Input" : caption));
        return r?.Text;
    }
}

/// <summary>The WPF implementation of the session's UI services.</summary>
public sealed class WpfShell : IShell
{
    private readonly MainWindow _main;
    private readonly Action _exit;
    private readonly Dictionary<string, DispatcherTimer> _timers = new();
    private readonly WpfUi _ui;

    /// <summary>Hidden mode (/hide).</summary>
    public bool Hidden { get; }

    /// <summary>Creates the shell.</summary>
    public WpfShell(MainWindow main, bool hidden, Action exit)
    {
        _main = main;
        _exit = exit;
        Hidden = hidden;
        _ui = new WpfUi(main, hidden);
        Dialogs = new WpfDialogs(main, hidden);
    }

    /// <summary>Connects the session (logging of hidden-mode answers).</summary>
    public void SetSession(Session s)
    {
        _ui.Session = s;
        ((WpfDialogs)Dialogs).Session = s;
    }

    /// <inheritdoc/>
    public IUserInteraction Ui => _ui;
    /// <inheritdoc/>
    public IDialogs Dialogs { get; }

    /// <inheritdoc/>
    public void PlotAdded(PlotDocument plot, bool maximized) => _main.Mdi.AddPlot(plot, maximized);
    /// <inheritdoc/>
    public void PlotRemoved(PlotDocument plot) => _main.Mdi.RemovePlot(plot);
    /// <inheritdoc/>
    public void Activate(PlotDocument plot) => _main.Mdi.Activate(plot);
    /// <inheritdoc/>
    public void Invalidate(PlotDocument plot) => _main.Mdi.Find(plot)?.View.InvalidateVisual();
    /// <inheritdoc/>
    public void UpdateCaption(PlotDocument plot) => _main.Mdi.Find(plot)?.UpdateCaption();

    /// <inheritdoc/>
    public (int Width, int Height) PlotSize(PlotDocument plot)
    {
        var w = _main.Mdi.Find(plot);
        if (w != null && w.View.ActualWidth > 16 && w.View.ActualHeight > 16 && !Hidden)
        {
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(w.View);
            return ((int)Math.Round(w.View.ActualWidth * dpi.DpiScaleX), (int)Math.Round(w.View.ActualHeight * dpi.DpiScaleY));
        }
        return (1024, 768);
    }

    /// <inheritdoc/>
    public bool IsMaximized(PlotDocument plot) => _main.Mdi.Find(plot)?.State == PlotWindowState.Maximized;
    /// <inheritdoc/>
    public void Arrange(WindowArrange mode) => _main.Mdi.Arrange(mode);

    /// <inheritdoc/>
    public void MainWindowState(int state)
    {
        if (Hidden) return;
        _main.WindowState = state == 1 ? WindowState.Minimized : state == 2 ? WindowState.Maximized : WindowState.Normal;
    }

    /// <inheritdoc/>
    public void MoveMainWindow(int left, int top, int right, int bottom)
    {
        _main.WindowState = WindowState.Normal;
        _main.Left = left;
        _main.Top = top;
        _main.Width = Math.Max(200, right - left);
        _main.Height = Math.Max(150, bottom - top);
    }

    /// <inheritdoc/>
    public void ShowStatusBar(bool show) => _main.SetStatusBarVisible(show);
    /// <inheritdoc/>
    public void SetAppearance(bool dark)
    {
        UiTheme.Dark = dark;
        if (!Hidden) _main.ApplyAppearance(dark);
    }
    /// <inheritdoc/>
    public void SetPresentationMode(bool on, bool fullScreen) => _main.SetPresentation(on, fullScreen);
    /// <inheritdoc/>
    public void SetStatus(string text) => _main.SetStatus(text);
    /// <inheritdoc/>
    public void SetClock(string text) => _main.SetClock(text);

    /// <inheritdoc/>
    public void CopyPlotToClipboard(PlotDocument plot)
    {
        var (w, h) = PlotSize(plot);
        try { Clipboard.SetImage(PlotImage.Render(plot, w, h)); }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException) { }
    }

    /// <inheritdoc/>
    public void CopyTextToClipboard(string text)
    {
        try { Clipboard.SetText(text); }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException) { }
    }

    /// <inheritdoc/>
    public string? GetClipboardText()
    {
        try { return Clipboard.ContainsText() ? Clipboard.GetText() : null; }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException) { return null; }
    }

    /// <inheritdoc/>
    public void RequestExit() => _main.Dispatcher.BeginInvoke(_exit, DispatcherPriority.Background);

    /// <inheritdoc/>
    public async Task YieldAsync() => await Dispatcher.Yield(DispatcherPriority.Background);

    /// <inheritdoc/>
    public bool IsSpacePressed() => !Hidden && _main.IsActive && Keyboard.IsKeyDown(Key.Space);
    /// <inheritdoc/>
    public bool IsShiftPressed() => !Hidden && (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift));
    /// <inheritdoc/>
    public void RefreshCommands() => _main.CommandsChanged();
    public void RebuildCommands() => _main.RebuildCommands();

    /// <inheritdoc/>
    /// <remarks>
    /// Opens the file URI with the user's default browser (the http protocol handler), so documents such as
    /// Markdown are shown in the browser rather than in the application associated with their extension.
    /// Falls back to Microsoft Edge, then to the shell association of the file.
    /// </remarks>
    public bool OpenInBrowser(string path)
    {
        string uri = new Uri(Path.GetFullPath(path)).AbsoluteUri;
        string? browser = DefaultBrowser();
        if (browser != null && TryStart(browser, "\"" + uri + "\"")) return true;
        if (TryStart("msedge", "\"" + uri + "\"")) return true;
        return TryStart(path, null);
    }

    private static bool TryStart(string file, string? args)
    {
        try
        {
            var psi = args != null ? new System.Diagnostics.ProcessStartInfo(file, args) : new System.Diagnostics.ProcessStartInfo(file);
            psi.UseShellExecute = true;
            return System.Diagnostics.Process.Start(psi) != null || args == null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException) { return false; }
    }

    /// <summary>Executable of the default http handler, or null.</summary>
    private static string? DefaultBrowser()
    {
        try
        {
            using var choice = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\http\UserChoice");
            if (choice?.GetValue("ProgId") is not string progId || progId.Length == 0) return null;
            using var cmd = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(progId + @"\shell\open\command");
            if (cmd?.GetValue(null) is not string line || line.Length == 0) return null;
            line = line.Trim();
            string exe = line.StartsWith('"') ? line[1..Math.Max(1, line.IndexOf('"', 1))] : line.Split(' ')[0];
            return File.Exists(exe) ? exe : null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return null; }
    }

    /// <inheritdoc/>
    public void StartTimer(string name, int intervalMs, Action tick)
    {
        StopTimer(name);
        var t = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(Math.Max(10, intervalMs)) };
        t.Tick += (_, _) => tick();
        _timers[name] = t;
        t.Start();
    }

    /// <inheritdoc/>
    public void StopTimer(string name)
    {
        if (_timers.Remove(name, out var t)) t.Stop();
    }
}
