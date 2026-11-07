// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Multiplot.Application;
using Multiplot.Core;
using static Multiplot.Core.CommandIds;

namespace Multiplot.App;

/// <summary>The main frame: menus, document area, status bar (FR-UI-001..006).</summary>
public sealed class MainWindow : Window
{
    private readonly Menu _menu = new();
    private readonly StatusBar _status = new();
    private readonly TextBlock _msg = new(), _cpu = new() { Width = 54 }, _mem = new() { Width = 54 }, _clock = new() { Width = 110 };
    private readonly LoadMeter _cpuBar = new(), _memBar = new();
    private readonly DispatcherTimer _perfTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private Session? _s;
    private WindowStyle _savedStyle;
    private WindowState _savedState;

    /// <summary>Document area.</summary>
    public MdiArea Mdi { get; } = new();

    /// <summary>Set by the application to allow the window to close.</summary>
    public bool AllowClose { get; set; }

    /// <summary>Creates the window.</summary>
    public MainWindow()
    {
        Title = "Multiplot.NET";
        Width = 1100;
        Height = 780;
        try { Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Resources/Multiplot.ico")); } catch (Exception) { }
        var dock = new DockPanel();
        DockPanel.SetDock(_menu, Dock.Top);
        DockPanel.SetDock(_status, Dock.Bottom);
        _status.Items.Add(new StatusBarItem { Content = _msg, HorizontalContentAlignment = HorizontalAlignment.Left });
        foreach (var (t, bar) in new (TextBlock, LoadMeter?)[] { (_cpu, _cpuBar), (_mem, _memBar), (_clock, null) })
        {
            _status.Items.Add(new Separator());
            object content = t;
            if (bar != null)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(t);
                row.Children.Add(bar);
                content = row;
            }
            _status.Items.Add(new StatusBarItem { Content = content });
        }
        // first item stretches
        var panel = new DockPanel();
        dock.Children.Add(_menu);
        dock.Children.Add(_status);
        Mdi.Main = this;
        Mdi.Background = UiTheme.Workspace;
        Mdi.ClipToBounds = true;
        dock.Children.Add(Mdi);
        Content = dock;
        _ = panel;
        AllowDrop = true;
        Drop += OnDrop;
        PreviewKeyDown += OnKey;
        Closing += (_, e) =>
        {
            if (AllowClose) return;
            e.Cancel = true;
            _s?.Shell.RequestExit();
        };
        _perfTimer.Tick += (_, _) => UpdatePerformance();
    }

    /// <summary>Connects the session.</summary>
    public void Attach(Session s, WpfShell shell)
    {
        _s = s;
        shell.SetSession(s);
        Mdi.Attach(s);
        s.Scripts.StateChanged += CommandsChanged;
        RebuildCommands();
        _perfTimer.Start();
    }

    // ------------------------------------------------------------------ menus

    private static string AccessText(string caption)
    {
        string c = caption;
        int tab = c.IndexOf("\\t", StringComparison.Ordinal);
        if (tab >= 0) c = c[..tab];
        tab = c.IndexOf('\t');
        if (tab >= 0) c = c[..tab];
        return c.Replace("_", "__").Replace("&&", "\u0001").Replace('&', '_').Replace("\u0001", "&");
    }

    private static string? Gesture(string caption)
    {
        int tab = caption.IndexOf("\\t", StringComparison.Ordinal);
        if (tab >= 0) return caption[(tab + 2)..];
        tab = caption.IndexOf('\t');
        return tab >= 0 ? caption[(tab + 1)..] : null;
    }

    /// <summary>Builds menu items from menu definition nodes.</summary>
    public void FillMenu(ItemCollection items, IEnumerable<MenuNode> nodes, Action<int>? execute = null)
    {
        foreach (var n in nodes)
        {
            if (n.IsSeparator) { items.Add(new Separator()); continue; }
            var mi = new MenuItem { Header = AccessText(n.Caption), InputGestureText = Gesture(n.Caption) ?? "" };
            if (n.Children != null)
            {
                if (n.Caption == "Recent &Files")
                {
                    mi.Items.Add(new MenuItem { Header = "Empty", IsEnabled = false });
                    mi.SubmenuOpened += (_, e) => { if (e.OriginalSource == mi) FillRecent(mi, execute); };
                }
                else FillMenu(mi.Items, n.Children, execute);
                mi.SubmenuOpened += (_, e) => { if (e.OriginalSource == mi) RefreshChecks(mi.Items); };
            }
            else
            {
                int id = n.Id;
                mi.Tag = id;
                mi.Click += (_, _) => (execute ?? Execute)(id);
                mi.MouseEnter += (_, _) => SetStatus(CommandPrompts.Prompt(id) ?? "");
            }
            items.Add(mi);
        }
    }

    private void FillRecent(MenuItem parent, Action<int>? execute)
    {
        parent.Items.Clear();
        if (_s == null || _s.RecentFiles.Count == 0)
        {
            parent.Items.Add(new MenuItem { Header = "Empty", IsEnabled = false });
            return;
        }
        for (int i = 0; i < _s.RecentFiles.Count; i++)
        {
            int id = ID_RECENT0 + i;
            var mi = new MenuItem { Header = _s.RecentFiles[i].Replace("_", "__") };
            mi.Click += (_, _) => (execute ?? Execute)(id);
            parent.Items.Add(mi);
        }
    }

    private void RefreshChecks(ItemCollection items)
    {
        if (_s == null) return;
        foreach (var o in items)
        {
            if (o is not MenuItem mi || mi.Tag is not int id) continue;
            bool? c = _s.IsChecked(id);
            mi.IsCheckable = false;
            mi.IsChecked = c == true;
            string? label = _s.CommandLabel(id);
            if (label != null) mi.Header = AccessText(label);
        }
    }

    /// <summary>Executes a command id on the session.</summary>
    public async void Execute(int id)
    {
        if (_s == null) return;
        try { await _s.ExecuteCommandAsync(id); }
        catch (Exception ex)
        {
            _s.DiagLog("Command " + id + " failed: " + ex);
            MessageBox.Show(this, ex.Message, "Multiplot", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        CommandsChanged();
    }

    /// <summary>Rebuilds the menus (start-up, settings load, plugin reload).</summary>
    public void RebuildCommands()
    {
        _menu.Items.Clear();
        FillMenu(_menu.Items, MenuDefinitions.Main);
        AddPluginCommands();
        CommandsChanged();
    }

    private void AddPluginCommands()
    {
        if (_s == null) return;
        var data = _menu.Items.OfType<MenuItem>().FirstOrDefault(m => (m.Header as string) == "_Data");
        if (data == null) return;
        int insertAt = Math.Max(0, data.Items.Count - 1);
        bool any = false;
        foreach (var lp in _s.Data.Plugins)
        {
            if (lp.Plugin is not Plugins.IMenuContributor mc || mc.Commands.Count == 0) continue;
            var sub = new MenuItem { Header = lp.Plugin.Name };
            foreach (var c in mc.Commands.Take(20))
            {
                var mi = new MenuItem { Header = c.Caption };
                mi.Click += (_, _) =>
                {
                    try { c.Execute(); }
                    catch (Exception ex) { _s.DiagLog("Plugin command failed: " + ex.Message); }
                };
                if (c.IsChecked != null) sub.SubmenuOpened += (_, _) => mi.IsChecked = c.IsChecked();
                sub.Items.Add(mi);
            }
            data.Items.Insert(insertAt++, sub);
            any = true;
        }
        if (any) data.Items.Insert(insertAt, new Separator());
    }

    /// <summary>Refreshes check states, title and user labels.</summary>
    public void CommandsChanged()
    {
        if (_s == null) return;
        string title = "Multiplot.NET";
        if (_s.Recording) title += " - recording [" + _s.ScriptFile + "]";
        else if (_s.ScriptRun != 0) title += " - " + System.IO.Path.GetFileName(_s.ScriptFile);
        Title = title;
        Topmost = _s.KeepOnTop;
    }

    // ------------------------------------------------------------------ status bar

    /// <summary>Status message.</summary>
    public void SetStatus(string text) => _msg.Text = text;
    /// <summary>Real-time clock pane.</summary>
    public void SetClock(string text) => _clock.Text = text;
    /// <summary>
    /// Light or dark appearance (FR-UI-046): the WPF Fluent theme (light or dark) for the menus, dialogs
    /// and window frames, and the plot window frames.
    /// </summary>
    public void ApplyAppearance(bool dark)
    {
        UiTheme.Dark = dark;
#pragma warning disable WPF0001 // ThemeMode is marked experimental
        if (System.Windows.Application.Current is { } app) app.ThemeMode = dark ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001
        UseCompactBars();
        Mdi.ApplyTheme();
    }

    /// <summary>
    /// Keeps the menu bar and status bar one text line high: the Fluent theme pads them for touch, which
    /// roughly doubles their height. The styles are based on the current theme styles (call after a theme change).
    /// </summary>
    private void UseCompactBars()
    {
        Style Based(Type t, params Setter[] setters)
        {
            var style = new Style(t, TryFindResource(t) as Style);
            foreach (var x in setters) style.Setters.Add(x);
            return style;
        }
        _menu.Resources.Clear();
        _menu.Style = Based(typeof(Menu), new Setter(PaddingProperty, new Thickness(0)), new Setter(MinHeightProperty, 0.0));
        var item = Based(typeof(MenuItem));
        item.Setters.Add(new EventSetter(LoadedEvent, new RoutedEventHandler((o, _) => CompactTopLevel(o as MenuItem))));
        _menu.Resources[typeof(MenuItem)] = item;
        // items already shown get the new theme template without a new Loaded event
        Dispatcher.BeginInvoke(() => { foreach (var mi in _menu.Items.OfType<MenuItem>()) CompactTopLevel(mi); }, DispatcherPriority.Loaded);
        _status.Resources.Clear();
        _status.Style = Based(typeof(StatusBar), new Setter(PaddingProperty, new Thickness(0)), new Setter(MinHeightProperty, 0.0));
        _status.Resources[typeof(StatusBarItem)] = Based(typeof(StatusBarItem), new Setter(PaddingProperty, new Thickness(6, 1, 6, 1)), new Setter(MinHeightProperty, 0.0));
    }

    /// <summary>
    /// The Fluent menu bar item template has fixed 4 px outer and 10 px inner margins (not template bound), so they
    /// are reduced on the template instance.
    /// </summary>
    private static void CompactTopLevel(MenuItem? mi)
    {
        if (mi == null || mi.Role != MenuItemRole.TopLevelHeader) return;
        mi.ApplyTemplate();
        if (mi.Template?.FindName("Border", mi) is not Border border) return;
        border.Margin = new Thickness(1, 1, 1, 1);
        if (border.Child is Grid outer && outer.Children.OfType<Grid>().FirstOrDefault() is Grid inner)
            inner.Margin = new Thickness(8, 3, 8, 3);
    }

    /// <summary>Status bar visibility.</summary>
    public void SetStatusBarVisible(bool show) => _status.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Presentation mode (FR-UI-006).</summary>
    public void SetPresentation(bool on, bool full)
    {
        if (on)
        {
            _savedStyle = WindowStyle;
            _savedState = WindowState;
            _menu.Visibility = _status.Visibility = Visibility.Collapsed;
            WindowStyle = WindowStyle.None;
            if (full) WindowState = WindowState.Maximized;
        }
        else
        {
            _menu.Visibility = Visibility.Visible;
            _status.Visibility = _s?.Settings.ShowStatusBar != false ? Visibility.Visible : Visibility.Collapsed;
            WindowStyle = _savedStyle == WindowStyle.None ? WindowStyle.SingleBorderWindow : _savedStyle;
            WindowState = _savedState;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime { public uint Low, High; public ulong Value => ((ulong)High << 32) | Low; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx m);

    private ulong _lastIdle, _lastTotal;

    private void UpdatePerformance()
    {
        if (_s == null || !_s.Settings.ShowSysLoad)
        {
            _cpu.Text = _mem.Text = "";
            _cpuBar.Visibility = _memBar.Visibility = Visibility.Collapsed;
            return;
        }
        try
        {
            if (GetSystemTimes(out var idle, out var kernel, out var user))
            {
                ulong total = kernel.Value + user.Value;
                ulong dt = total - _lastTotal, di = idle.Value - _lastIdle;
                if (_lastTotal != 0 && dt > 0)
                {
                    int cpu = (int)(100.0 * (dt - di) / dt);
                    _cpu.Text = Crt.Sprintf("CPU %d%%", cpu);
                    _cpuBar.SetPercent(cpu);
                }
                _lastTotal = total;
                _lastIdle = idle.Value;
            }
            var m = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (GlobalMemoryStatusEx(ref m))
            {
                _mem.Text = Crt.Sprintf("MEM %d%%", (int)m.MemoryLoad);
                _memBar.SetPercent((int)m.MemoryLoad);
            }
        }
        catch (Exception) { }
    }

    // ------------------------------------------------------------------ keyboard and drag-drop

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (_s == null) return;
        var mod = Keyboard.Modifiers;
        bool ctrl = (mod & ModifierKeys.Control) != 0, shift = (mod & ModifierKeys.Shift) != 0, alt = (mod & ModifierKeys.Alt) != 0;
        Key k = e.Key == Key.System ? e.SystemKey : e.Key;
        if (_s.Align != null && AlignKey(k, ctrl, shift))
        {
            e.Handled = true;
            return;
        }
        int id = k switch
        {
            Key.L when ctrl => ID_ALIGN_STATIC,
            Key.Down => ID_AXIS_SHDY,
            Key.Up => ID_AXIS_SHUY,
            Key.Left => ID_AXIS_SHLX,
            Key.Right => ID_AXIS_SHRX,
            Key.PageUp => ID_AXIS_ZOOMIN,
            Key.PageDown => ID_AXIS_ZOOMOUT,
            Key.Escape when ctrl => ID_BREAKOUT,
            Key.Escape => ID_PRESENTATION_MODE,
            Key.D when ctrl => ID_DRAW_EVENT,
            Key.O when ctrl => ID_FILE_NEW,
            Key.Tab when ctrl => ID_NEXT_WINDOW,
            Key.Tab when shift => ID_PREV_WINDOW,
            Key.Return => ID_PARMSELECT,
            Key.J when ctrl => ID_SAVEJPEG,
            Key.Space when ctrl => ID_SCRIPT_RESUME,
            Key.Space => ID_SCRIPT_PAUSE,
            Key.Back => ID_UNDO_ZOOM,
            Key.F1 when alt => ID_USERSCHOICE1,
            Key.F2 when alt => ID_USERSCHOICE2,
            Key.F3 when alt => ID_USERSCHOICE3,
            Key.F4 when alt => ID_USERSCHOICE4,
            Key.F5 when alt => ID_USERSCHOICE5,
            Key.Home when ctrl && alt => ID_WINDOW_BRINGHOME,
            Key.R when ctrl => ID_WINDOW_REFRESH,
            Key.Insert => ID_X_ZOOMIN,
            Key.Delete => ID_X_ZOOMOUT,
            Key.Home => ID_XYAXIS_RESCALE,
            _ => 0,
        };
        if (id == 0) return;
        if ((id == ID_AXIS_SHLX || id == ID_AXIS_SHRX) && ctrl)
        {
            _s.PanXCommand(id == ID_AXIS_SHRX, ShiftMode.MinMax);
            e.Handled = true;
            return;
        }
        Execute(id);
        e.Handled = true;
    }

    /// <summary>Align Static keys: arrows nudge, Enter accepts, Esc cancels the snap click or the alignment.</summary>
    private bool AlignKey(Key k, bool ctrl, bool shift)
    {
        switch (k)
        {
            case Key.Left:
            case Key.Right:
                _s!.NudgeAlign(k == Key.Right ? 1 : -1, shift ? AlignStep.Graduation : ctrl ? AlignStep.Fine : AlignStep.Pixel);
                return true;
            case Key.Return:
                _s!.AcceptAlign();
                return true;
            case Key.Escape:
                if (!_s!.CancelAlignSnap()) _s.CancelAlign();
                return true;
            default:
                return false;
        }
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (_s == null || e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        bool openedData = false;
        foreach (var name in files)
        {
            string lower = name.ToLowerInvariant();
            if (lower.EndsWith(".wps", StringComparison.Ordinal))
            {
                if (_s.Recording) _s.Message("Can't execute script during record.\nCancel recording first.");
                else { await _s.Scripts.RunAsync(name); _s.ScriptRun = 0; }
            }
            else if (lower.EndsWith(".csv", StringComparison.Ordinal))
            {
                if (_s.ImportAscii(name, false) != 0) _s.Message("Import failed.");
            }
            else if (lower.EndsWith(".ini", StringComparison.Ordinal))
            {
                _s.LoadSettings(name);
                _s.Record("loadsettings " + name);
                RebuildCommands();
            }
            else if (lower.EndsWith(".wpd", StringComparison.Ordinal))
            {
                _s.YAxis = name;
                _s.SaveParm(name);
                _s.Plot();
                _s.Record("PLOT " + name);
            }
            else if (lower.EndsWith(".markers", StringComparison.Ordinal))
            {
                _s.MarkersFromFile(name);
                _s.Record("markerset load " + name);
            }
            else
            {
                if (_s.LoadFileGroup(name) != 0) break;
                _s.Record("OPEN " + name.Replace(";", "\\;"));
                openedData = true;
            }
        }
        if (openedData) _s.PromptFirstPlot();
        CommandsChanged();
    }
}

/// <summary>
/// Small horizontal load bar of the status bar (CPU, memory): green, yellow above 90 %, red above 95 %.
/// Shorter than the status text so it does not change the status bar height.
/// </summary>
internal sealed class LoadMeter : Border
{
    private const double BarWidth = 40;
    private static readonly Brush Normal = Frozen(Color.FromRgb(0x3C, 0xB3, 0x4A)), Warn = Frozen(Color.FromRgb(0xF2, 0xC2, 0x0F)),
        Alarm = Frozen(Color.FromRgb(0xE0, 0x30, 0x30));
    private readonly Border _fill = new() { HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };

    public LoadMeter()
    {
        Width = BarWidth + 2;
        Height = 10;
        Margin = new Thickness(4, 0, 0, 0);
        VerticalAlignment = VerticalAlignment.Center;
        BorderThickness = new Thickness(1);
        SetResourceReference(BorderBrushProperty, SystemColors.GrayTextBrushKey);
        Child = _fill;
        Visibility = Visibility.Collapsed;
    }

    /// <summary>Shows the load, 0..100 %.</summary>
    public void SetPercent(int percent)
    {
        int p = Math.Clamp(percent, 0, 100);
        _fill.Width = BarWidth * p / 100.0;
        _fill.Background = p > 95 ? Alarm : p > 90 ? Warn : Normal;
        Visibility = Visibility.Visible;
    }

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
