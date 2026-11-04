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

/// <summary>The main frame: menus, toolbars, document area, status bar (FR-UI-001..006).</summary>
public sealed class MainWindow : Window
{
    private readonly Menu _menu = new();
    private readonly ToolBarTray _tray = new();
    private readonly StatusBar _status = new();
    private readonly TextBlock _msg = new(), _cpu = new() { Width = 54 }, _mem = new() { Width = 54 }, _clock = new() { Width = 110 };
    private readonly Dictionary<int, List<ButtonBase>> _toolButtons = new();
    private readonly DispatcherTimer _perfTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Dictionary<string, BitmapSource?> _strips = new();
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
        DockPanel.SetDock(_tray, Dock.Top);
        DockPanel.SetDock(_status, Dock.Bottom);
        _status.Items.Add(new StatusBarItem { Content = _msg, HorizontalContentAlignment = HorizontalAlignment.Left });
        foreach (var t in new[] { _cpu, _mem, _clock })
        {
            _status.Items.Add(new Separator());
            _status.Items.Add(new StatusBarItem { Content = t });
        }
        // first item stretches
        var panel = new DockPanel();
        dock.Children.Add(_menu);
        dock.Children.Add(_tray);
        dock.Children.Add(_status);
        Mdi.Main = this;
        Mdi.Background = SystemColors.AppWorkspaceBrush;
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

    // ------------------------------------------------------------------ toolbars

    private BitmapSource? Strip(string file)
    {
        if (_strips.TryGetValue(file, out var b)) return b;
        BitmapSource? src = null;
        try
        {
            var img = new BitmapImage(new Uri("pack://application:,,,/Resources/" + file));
            var conv = new FormatConvertedBitmap(img, PixelFormats.Bgra32, null, 0);
            int w = conv.PixelWidth, h = conv.PixelHeight;
            var px = new byte[w * h * 4];
            conv.CopyPixels(px, w * 4, 0);
            for (int i = 0; i < px.Length; i += 4)
                if (px[i] == 192 && px[i + 1] == 192 && px[i + 2] == 192) px[i + 3] = 0;
            src = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
            src.Freeze();
        }
        catch (Exception) { src = null; }
        _strips[file] = src;
        return src;
    }

    private UIElement? ButtonImage(int bar, int id, int index)
    {
        if (ToolbarIcons.Create(id) is { } glyph) return glyph;
        var strip = Strip(ToolbarDefaults.Bitmaps[bar]);
        if (strip == null || index < 0 || (index + 1) * 18 > strip.PixelWidth) return null;
        return new Image { Source = new CroppedBitmap(strip, new Int32Rect(index * 18, 0, 18, Math.Min(19, strip.PixelHeight))), Width = 18, Height = Math.Min(19, strip.PixelHeight), Stretch = Stretch.None };
    }

    private void BuildToolbars()
    {
        _tray.ToolBars.Clear();
        _toolButtons.Clear();
        if (_s == null) return;
        var st = _s.Settings;
        int band = 0;
        foreach (int bar in st.ToolbarOrder)
        {
            if (bar < 0 || bar >= Core.Model.AppSettings.NToolbars || !st.ToolbarEnabled[bar]) continue;
            var tb = new ToolBar { Header = null, Band = band / 8, BandIndex = band % 8 };
            band++;
            foreach (var btn in st.ToolbarSets[bar])
            {
                if (btn.EventId == 0) { tb.Items.Add(new Separator()); continue; }
                if (Session.OutOfScopeCommands.Contains(btn.EventId)) continue;
                int id = btn.EventId;
                ButtonBase b = _s.IsChecked(id) != null ? new ToggleButton() : new Button();
                bool isUser = id >= ID_USERSCHOICE1 && id <= ID_USERSCHOICE5;
                var image = isUser ? null : ButtonImage(bar, id, btn.ImageIndex);
                if (image != null) b.Content = image;
                else b.Content = new TextBlock { Text = UserLabel(id) ?? CommandPrompts.Tooltip(id) ?? id.ToString(System.Globalization.CultureInfo.InvariantCulture), Margin = new Thickness(2, 0, 2, 0) };
                b.ToolTip = CommandPrompts.Tooltip(id);
                b.Focusable = false;
                b.Click += (_, _) => Execute(id);
                b.MouseEnter += (_, _) => SetStatus(CommandPrompts.Prompt(id) ?? "");
                tb.Items.Add(b);
                if (!_toolButtons.TryGetValue(id, out var list)) _toolButtons[id] = list = new List<ButtonBase>();
                list.Add(b);
            }
            if (tb.Items.Count > 0) _tray.ToolBars.Add(tb);
        }
        _tray.Visibility = st.ShowToolBar ? Visibility.Visible : Visibility.Collapsed;
    }

    private string? UserLabel(int id)
    {
        if (_s == null || id < ID_USERSCHOICE1 || id > ID_USERSCHOICE5) return null;
        string l = _s.Settings.UserButtons[id - ID_USERSCHOICE1].Label;
        return l.Length > 0 ? l : "User " + (id - ID_USERSCHOICE1 + 1);
    }

    /// <summary>Rebuilds menus and toolbars (start-up, settings load, plugin reload).</summary>
    public void RebuildCommands()
    {
        _menu.Items.Clear();
        FillMenu(_menu.Items, MenuDefinitions.Main);
        AddPluginCommands();
        BuildToolbars();
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
        foreach (var (id, list) in _toolButtons)
        {
            bool? c = _s.IsChecked(id);
            foreach (var b in list) if (b is ToggleButton t) t.IsChecked = c == true;
        }
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
    /// <summary>Toolbar visibility.</summary>
    public void SetToolbarVisible(bool show) => _tray.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    /// <summary>Status bar visibility.</summary>
    public void SetStatusBarVisible(bool show) => _status.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Presentation mode (FR-UI-006).</summary>
    public void SetPresentation(bool on, bool full)
    {
        if (on)
        {
            _savedStyle = WindowStyle;
            _savedState = WindowState;
            _menu.Visibility = _tray.Visibility = _status.Visibility = Visibility.Collapsed;
            WindowStyle = WindowStyle.None;
            if (full) WindowState = WindowState.Maximized;
        }
        else
        {
            _menu.Visibility = Visibility.Visible;
            _tray.Visibility = _s?.Settings.ShowToolBar != false ? Visibility.Visible : Visibility.Collapsed;
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
            return;
        }
        try
        {
            if (GetSystemTimes(out var idle, out var kernel, out var user))
            {
                ulong total = kernel.Value + user.Value;
                ulong dt = total - _lastTotal, di = idle.Value - _lastIdle;
                if (_lastTotal != 0 && dt > 0) _cpu.Text = Crt.Sprintf("CPU %d%%", (int)(100.0 * (dt - di) / dt));
                _lastTotal = total;
                _lastIdle = idle.Value;
            }
            var m = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (GlobalMemoryStatusEx(ref m)) _mem.Text = Crt.Sprintf("MEM %d%%", (int)m.MemoryLoad);
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
        int id = k switch
        {
            Key.Down => ID_AXIS_SHDY,
            Key.Up => ID_AXIS_SHUY,
            Key.Left => ID_AXIS_SHLX,
            Key.Right => ID_AXIS_SHRX,
            Key.PageUp => ID_AXIS_ZOOMIN,
            Key.PageDown => ID_AXIS_ZOOMOUT,
            Key.Escape when ctrl => ID_BREAKOUT,
            Key.Escape => ID_PRESENTATION_MODE,
            Key.D when ctrl => ID_DRAW_MARKERS,
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

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (_s == null || e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
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
            }
        }
        CommandsChanged();
    }
}
