// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Multiplot.Application;
using Multiplot.Core.Model;
using Multiplot.Rendering;

namespace Multiplot.App;

/// <summary>Plot window states (MDI emulation, DEV-UI-003).</summary>
public enum PlotWindowState
{
    /// <summary>Normal.</summary>
    Normal,
    /// <summary>Maximised inside the document area.</summary>
    Maximized,
    /// <summary>Minimised to an icon bar.</summary>
    Minimized,
}

/// <summary>
/// Brushes of the plot window frames, the document area and the plot overlays for the light or dark
/// appearance (FR-UI-046). Light uses the Windows system colours.
/// </summary>
public static class UiTheme
{
    /// <summary>Dark appearance.</summary>
    public static bool Dark { get; set; }

    private static SolidColorBrush B(byte r, byte g, byte b)
    {
        var br = new SolidColorBrush(Color.FromRgb(r, g, b));
        br.Freeze();
        return br;
    }

    private static readonly Brush DWorkspace = B(0x12, 0x12, 0x12), DWindow = B(0x2B, 0x2B, 0x2B), DBorder = B(0x55, 0x55, 0x55),
        DActive = B(0x3C, 0x3C, 0x48), DInactive = B(0x2B, 0x2B, 0x2B), DActiveText = B(0xF0, 0xF0, 0xF0), DInactiveText = B(0xA0, 0xA0, 0xA0),
        DOverlay = B(0xE0, 0xE0, 0xE0), DTip = B(0x33, 0x33, 0x33), DTipText = B(0xF0, 0xF0, 0xF0);

    /// <summary>Document area behind the plot windows.</summary>
    public static Brush Workspace => Dark ? DWorkspace : SystemColors.AppWorkspaceBrush;
    /// <summary>Plot window frame.</summary>
    public static Brush Window => Dark ? DWindow : SystemColors.ControlBrush;
    /// <summary>Plot window border.</summary>
    public static Brush Border => Dark ? DBorder : Brushes.Gray;
    /// <summary>Caption bar background.</summary>
    public static Brush Caption(bool active) => Dark ? (active ? DActive : DInactive) : active ? SystemColors.ActiveCaptionBrush : SystemColors.InactiveCaptionBrush;
    /// <summary>Caption text.</summary>
    public static Brush CaptionText(bool active) => Dark ? (active ? DActiveText : DInactiveText) : active ? SystemColors.ActiveCaptionTextBrush : SystemColors.InactiveCaptionTextBrush;
    /// <summary>Zoom box and cross-hair lines drawn over the plot.</summary>
    public static Brush Overlay => Dark ? DOverlay : Brushes.Black;
    /// <summary>Curve identification pop-up.</summary>
    public static Brush Tip => Dark ? DTip : Brushes.LightYellow;
    /// <summary>Curve identification pop-up text.</summary>
    public static Brush TipText => Dark ? DTipText : Brushes.Black;
}

/// <summary>MDI-like document area hosting plot windows (FR-UI-001).</summary>
public sealed class MdiArea : Panel
{
    private Session? _session;
    private int _cascade;

    /// <summary>Main window.</summary>
    public MainWindow? Main { get; set; }

    /// <summary>Connects the session.</summary>
    public void Attach(Session s) => _session = s;

    /// <summary>Plot windows (z-order not implied).</summary>
    public IEnumerable<PlotWindow> Windows => InternalChildren.OfType<PlotWindow>();

    /// <summary>Finds the window of a plot.</summary>
    public PlotWindow? Find(PlotDocument p) => Windows.FirstOrDefault(w => w.Document == p);

    /// <summary>Applies the current <see cref="UiTheme"/> to the document area and every plot window.</summary>
    public void ApplyTheme()
    {
        Background = UiTheme.Workspace;
        foreach (var w in Windows) w.ApplyTheme();
    }

    /// <summary>Creates a window for a new plot.</summary>
    public void AddPlot(PlotDocument p, bool maximized)
    {
        var w = new PlotWindow(p, _session!, this);
        double aw = ActualWidth > 100 ? ActualWidth : 800, ah = ActualHeight > 100 ? ActualHeight : 600;
        int step = 24 * (_cascade++ % 8);
        w.Bounds = new Rect(step, step, Math.Max(200, aw * 0.6), Math.Max(150, ah * 0.6));
        w.State = maximized ? PlotWindowState.Maximized : PlotWindowState.Normal;
        if (maximized)
            foreach (var o in Windows) if (o.State == PlotWindowState.Maximized) o.State = PlotWindowState.Normal;
        InternalChildren.Add(w);
        Activate(p);
        InvalidateArrange();
    }

    /// <summary>Removes a plot window.</summary>
    public void RemovePlot(PlotDocument p)
    {
        var w = Find(p);
        if (w != null) InternalChildren.Remove(w);
        InvalidateArrange();
    }

    /// <summary>Brings a plot window to the front.</summary>
    public void Activate(PlotDocument p)
    {
        var w = Find(p);
        if (w == null) return;
        int max = Windows.Select(Panel.GetZIndex).DefaultIfEmpty(0).Max();
        Panel.SetZIndex(w, max + 1);
        bool anyMax = Windows.Any(o => o.State == PlotWindowState.Maximized);
        if (anyMax && w.State != PlotWindowState.Maximized)
        {
            foreach (var o in Windows) if (o.State == PlotWindowState.Maximized) o.State = PlotWindowState.Normal;
            w.State = PlotWindowState.Maximized;
        }
        foreach (var o in Windows) o.SetActive(o == w);
        InvalidateArrange();
        Main?.CommandsChanged();
    }

    /// <summary>User clicked a window: make it the session's active plot.</summary>
    public void UserActivated(PlotWindow w)
    {
        _session?.ActivatePlot(w.Document, false);
        Activate(w.Document);
    }

    private static int Columns(int count, bool vertical) =>
        vertical ? (count < 2 ? 1 : count < 9 ? 2 : count < 13 ? 3 : count < 25 ? 4 : 5)
                 : (count < 4 ? 1 : count < 9 ? 2 : count < 13 ? 3 : count < 25 ? 4 : 5);

    /// <summary>Window arrangement.</summary>
    public void Arrange(WindowArrange mode)
    {
        var all = (_session?.Plots ?? Array.Empty<PlotDocument>()).Select(Find).Where(w => w != null).Cast<PlotWindow>().ToList();
        int monitors = _session?.Settings.Monitors ?? 0;
        switch (mode)
        {
            case WindowArrange.MaximizeActive:
                if (all.Count > 0) { foreach (var o in all) if (o.State == PlotWindowState.Maximized) o.State = PlotWindowState.Normal; all[0].State = PlotWindowState.Maximized; }
                break;
            case WindowArrange.MinimizeActive:
                if (all.Count > 0) all[0].State = PlotWindowState.Minimized;
                break;
            case WindowArrange.Cascade:
                {
                    int i = 0;
                    foreach (var w in Enumerable.Reverse(all))
                    {
                        w.State = PlotWindowState.Normal;
                        w.Bounds = new Rect(24 * i, 24 * i, Math.Max(200, ActualWidth * 0.6), Math.Max(150, ActualHeight * 0.6));
                        i++;
                    }
                }
                break;
            default:
                {
                    var set = all.Where(w => w.State != PlotWindowState.Minimized).ToList();
                    int cols = mode == WindowArrange.Stack ? (monitors != 0 ? monitors : 1)
                        : monitors != 0 ? monitors : Columns(set.Count, mode == WindowArrange.TileVertical);
                    Tile(set, cols);
                }
                break;
        }
        InvalidateArrange();
    }

    private void Tile(List<PlotWindow> set, int cols)
    {
        int n = set.Count;
        if (n == 0 || cols <= 0) return;
        double fw = ActualWidth / cols;
        int per = n / cols, rem = n % cols, k = 0;
        for (int c = 0; c < cols && k < n; c++)
        {
            int rows = per + (c < rem ? 1 : 0);
            if (rows == 0) continue;
            double fh = ActualHeight / rows;
            for (int r = 0; r < rows && k < n; r++, k++)
            {
                set[k].State = PlotWindowState.Normal;
                set[k].Bounds = new Rect((int)(fw * c), (int)(fh * r), (int)(fw * (c + 1)) - (int)(fw * c), (int)(fh * (r + 1)) - (int)(fh * r));
            }
        }
    }

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (UIElement c in InternalChildren) c.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return new Size(double.IsInfinity(availableSize.Width) ? 800 : availableSize.Width, double.IsInfinity(availableSize.Height) ? 600 : availableSize.Height);
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize)
    {
        int minIdx = 0;
        foreach (var w in Windows)
        {
            switch (w.State)
            {
                case PlotWindowState.Maximized:
                    w.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
                    break;
                case PlotWindowState.Minimized:
                    w.Arrange(new Rect(minIdx++ * 164 % Math.Max(164, finalSize.Width), finalSize.Height - 26, 160, 26));
                    break;
                default:
                    w.Arrange(w.Bounds);
                    break;
            }
        }
        return finalSize;
    }
}

/// <summary>A plot window: caption bar, plot view and resize grip.</summary>
public sealed class PlotWindow : Border
{
    private readonly Session _s;
    private readonly MdiArea _mdi;
    private readonly TextBlock _caption = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Grid _header = new() { Height = 22 };
    private PlotWindowState _state;
    private Button? _maxButton;
    private bool _active;

    /// <summary>The plot.</summary>
    public PlotDocument Document { get; }
    /// <summary>The main window.</summary>
    public MainWindow? Main => _mdi.Main;
    /// <summary>The drawing surface.</summary>
    public PlotView View { get; }
    /// <summary>Normal bounds.</summary>
    public Rect Bounds { get; set; }

    /// <summary>Window state.</summary>
    public PlotWindowState State
    {
        get => _state;
        set
        {
            _state = value;
            if (_maxButton != null) _maxButton.Content = value == PlotWindowState.Maximized ? "\uE923" : "\uE922";
            View.Visibility = value == PlotWindowState.Minimized ? Visibility.Collapsed : Visibility.Visible;
            _mdi.InvalidateArrange();
        }
    }

    /// <summary>Creates the window.</summary>
    public PlotWindow(PlotDocument doc, Session s, MdiArea mdi)
    {
        Document = doc;
        _s = s;
        _mdi = mdi;
        BorderThickness = new Thickness(1);
        BorderBrush = UiTheme.Border;
        Background = UiTheme.Window;
        View = new PlotView(doc, s, this);
        var dock = new DockPanel();
        _header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < 3; i++) _header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _header.Children.Add(_caption);
        AddButton("\uE921", 1, () => State = PlotWindowState.Minimized);   // ChromeMinimize
        _maxButton = AddButton("\uE922", 2, ToggleMaximize);                   // ChromeMaximize / ChromeRestore
        AddButton("\uE8BB", 3, () => _s.RemovePlot(Document));               // ChromeClose
        var thumb = new Thumb { Opacity = 0, Cursor = Cursors.SizeAll };
        Grid.SetColumnSpan(thumb, 1);
        thumb.DragDelta += (_, e) =>
        {
            if (State != PlotWindowState.Normal) return;
            Bounds = new Rect(Bounds.X + e.HorizontalChange, Bounds.Y + e.VerticalChange, Bounds.Width, Bounds.Height);
            _mdi.InvalidateArrange();
        };
        thumb.MouseDoubleClick += (_, _) => ToggleMaximize();
        _header.Children.Insert(0, thumb);
        DockPanel.SetDock(_header, Dock.Top);
        dock.Children.Add(_header);
        var grip = new Thumb { Width = 12, Height = 12, Cursor = Cursors.SizeNWSE, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Opacity = 0.3 };
        grip.DragDelta += (_, e) =>
        {
            if (State != PlotWindowState.Normal) return;
            Bounds = new Rect(Bounds.X, Bounds.Y, Math.Max(120, Bounds.Width + e.HorizontalChange), Math.Max(80, Bounds.Height + e.VerticalChange));
            _mdi.InvalidateArrange();
        };
        var body = new Grid();
        body.Children.Add(View);
        body.Children.Add(View.Overlay);
        body.Children.Add(grip);
        dock.Children.Add(body);
        Child = dock;
        UpdateCaption();
        PreviewMouseDown += (_, _) => _mdi.UserActivated(this);
    }

    private Button AddButton(string glyph, int col, Action click)
    {
        var b = new Button { Content = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 10, Width = 24, Height = 18, Margin = new Thickness(1), Padding = new Thickness(0), Focusable = false };
        b.Click += (_, _) => click();
        Grid.SetColumn(b, col);
        _header.Children.Add(b);
        return b;
    }

    private void ToggleMaximize() => State = State == PlotWindowState.Maximized ? PlotWindowState.Normal : PlotWindowState.Maximized;

    /// <summary>Highlights the active window's caption.</summary>
    public void SetActive(bool active)
    {
        _active = active;
        _header.Background = UiTheme.Caption(active);
        _caption.Foreground = UiTheme.CaptionText(active);
    }

    /// <summary>Applies the current <see cref="UiTheme"/> (frame, caption, overlays) and redraws the plot.</summary>
    public void ApplyTheme()
    {
        BorderBrush = UiTheme.Border;
        Background = UiTheme.Window;
        SetActive(_active);
        View.ApplyTheme();
        View.InvalidateVisual();
    }

    /// <summary>Caption text and caption bar visibility (NoCaptionBar).</summary>
    public void UpdateCaption()
    {
        _caption.Text = Document.Caption;
        _header.Visibility = _s.Settings.NoCaptionBar != 0 && State != PlotWindowState.Minimized ? Visibility.Collapsed : Visibility.Visible;
    }
}

/// <summary>The plot drawing surface and its mouse interactions (FR-PLT-030..036).</summary>
public sealed class PlotView : FrameworkElement
{
    private readonly PlotDocument _doc;
    private readonly Session _s;
    private readonly PlotWindow _win;
    private readonly Canvas _overlay = new() { IsHitTestVisible = false };
    private readonly Rectangle _box = new() { Stroke = UiTheme.Overlay, StrokeDashArray = new DoubleCollection { 2, 2 }, Visibility = Visibility.Collapsed };
    private readonly Line _line1 = new() { Stroke = UiTheme.Overlay, Visibility = Visibility.Collapsed };
    private readonly Line _line2 = new() { Stroke = UiTheme.Overlay, Visibility = Visibility.Collapsed };

    /// <summary>Overlay line colour for the current <see cref="UiTheme"/>.</summary>
    public void ApplyTheme() => _box.Stroke = _line1.Stroke = _line2.Stroke = UiTheme.Overlay;
    private Point? _leftStart, _rightStart, _midAnchor;
    private int _rbFunction;
    private bool _moved;

    /// <summary>Overlay for rubber bands.</summary>
    public Canvas Overlay => _overlay;

    /// <summary>Creates the view.</summary>
    public PlotView(PlotDocument doc, Session s, PlotWindow win)
    {
        _doc = doc;
        _s = s;
        _win = win;
        _overlay.Children.Add(_box);
        _overlay.Children.Add(_line1);
        _overlay.Children.Add(_line2);
        Cursor = Cursors.Cross;
        ClipToBounds = true;
        Focusable = true;
        SnapsToDevicePixels = true;
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
    }

    /// <summary>The last rendered plot image (device pixels) and its DPI scale; also used for curve identification.</summary>
    private BitmapSource? _image;
    private DpiScale _imageDpi = new(1.0, 1.0);

    /// <summary>
    /// Renders the plot into a bitmap on the UI thread and draws only that image, so the WPF render thread never
    /// receives per-sample geometry (large data sets exhausted it: UCEERR_RENDERTHREADFAILURE).
    /// </summary>
    protected override void OnRender(DrawingContext dc)
    {
        int w = (int)ActualWidth, h = (int)ActualHeight;
        _image = null;
        if (w < 16 || h < 16) return;
        try
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            _image = PlotImage.Render(_doc, w, h, _s.Settings.OptimizedDraw != 0, false, dpi.DpiScaleX, dpi.DpiScaleY);
            _imageDpi = dpi;
            dc.DrawImage(_image, new Rect(0, 0, w, h));
        }
        catch (Exception ex)
        {
            _s.DiagLog("Render failed: " + ex);
            dc.DrawRectangle(UiTheme.Dark ? Brushes.Black : Brushes.White, null, new Rect(0, 0, w, h));
        }
    }

    private static (int X, int Y) P(Point p) => ((int)p.X, (int)p.Y);

    private bool InGraph(int x, int y) => x >= _doc.GraphLeft && x <= _doc.GraphRight && y >= _doc.GraphTop && y <= _doc.GraphBottom;

    private Point Clamp(Point p) => _doc.Config.Style == PlotStyle.Tabular ? p
        : new Point(Math.Clamp(p.X, _doc.GraphLeft, _doc.GraphRight), Math.Clamp(p.Y, _doc.GraphTop, _doc.GraphBottom));

    /// <inheritdoc/>
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        var (x, y) = P(e.GetPosition(this));
        if (_doc.Config.Style != PlotStyle.Tabular)
        {
            if (_s.SelectAxisAt(_doc, x, y)) return;
            if (!(x >= _doc.GraphLeft - 2 && x <= _doc.GraphRight + 2 && y >= _doc.GraphTop - 2 && y <= _doc.GraphBottom + 2)) return;
        }
        _leftStart = Clamp(e.GetPosition(this));
        _moved = false;
        CaptureMouse();
        _s.SetStatus("Right click to cancel zoom");
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_leftStart == null) return;
        var start = _leftStart.Value;
        var end = Clamp(e.GetPosition(this));
        _leftStart = null;
        ReleaseMouseCapture();
        _box.Visibility = Visibility.Collapsed;
        _s.SetStatus("");
        var (x0, y0) = P(start);
        var (x1, y1) = P(end);
        bool shift = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
        if (_doc.Config.Style == PlotStyle.Tabular)
        {
            if (Math.Abs(x1 - x0) > 2 && Math.Abs(y1 - y0) > 2) _s.TabularZoom(_doc, x0, y0, x1, y1, (int)ActualWidth, (int)ActualHeight);
            return;
        }
        if (Math.Abs(x1 - x0) > 2 && Math.Abs(y1 - y0) > 2) _s.ZoomBoxPixels(_doc, x0, y0, x1, y1, shift);
        else Identify(start);
        e.Handled = true;
    }

    private void Identify(Point p)
    {
        int w = (int)ActualWidth, h = (int)ActualHeight;
        if (w < 16 || h < 16) return;
        var bmp = _image;
        var dpi = _imageDpi;
        if (bmp == null)
        {
            dpi = new DpiScale(1.0, 1.0);
            bmp = PlotImage.Render(_doc, w, h, _s.Settings.OptimizedDraw != 0);
        }
        int Pixel(int x, int y)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return -1;
            int px0 = Math.Min(bmp.PixelWidth - 1, (int)(x * dpi.DpiScaleX)), py0 = Math.Min(bmp.PixelHeight - 1, (int)(y * dpi.DpiScaleY));
            var px = new byte[4];
            bmp.CopyPixels(new Int32Rect(px0, py0, 1, 1), px, 4, 0);
            return px[2] | (px[1] << 8) | (px[0] << 16);
        }
        int bg = PlotColors.Display(_doc.Config.DrawColor, ColorIndex.Background, _s.Settings.DarkMode);
        var (cx, cy) = P(p);
        int c = Pixel(cx, cy);
        if (c == bg) c = Pixel(cx + 1, cy);
        if (c == bg) c = Pixel(cx - 1, cy);
        if (c == bg) c = Pixel(cx, cy + 1);
        if (c == bg) c = Pixel(cx, cy - 1);
        string? text = Session.IdentifyCurve(_doc, c);
        if (text == null) return;
        var popup = new Popup
        {
            Placement = PlacementMode.Mouse,
            StaysOpen = false,
            Child = new Border
            {
                Background = UiTheme.Tip,
                BorderBrush = new SolidColorBrush(Color.FromRgb((byte)(c & 0xff), (byte)((c >> 8) & 0xff), (byte)((c >> 16) & 0xff))),
                BorderThickness = new Thickness(2),
                Padding = new Thickness(4),
                Child = new TextBlock { Text = text, Foreground = UiTheme.TipText },
            },
            IsOpen = true,
        };
        _ = popup;
    }

    /// <inheritdoc/>
    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        if (_leftStart != null)
        {
            _leftStart = null;
            ReleaseMouseCapture();
            _box.Visibility = Visibility.Collapsed;
            e.Handled = true;
            return;
        }
        var pos = e.GetPosition(this);
        var (x, y) = P(pos);
        bool shift = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
        bool ctrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
        bool alt = Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt);
        int rb = _s.Settings.RbFunction;
        if (!InGraph(x, y) || alt || (rb == 1 && !shift && !ctrl) || _doc.Config.Style == PlotStyle.Tabular)
        {
            ShowContextMenu(x, y);
            e.Handled = true;
            return;
        }
        if (shift && ctrl) rb = 3;
        else if (shift) rb = 0;
        else if (ctrl) rb = 2;
        if (rb == 2 && _doc.Config.EndToEnd)
        {
            _s.Message("Slice Average not available in End-to-End mode");
            return;
        }
        _rbFunction = rb;
        _rightStart = pos;
        _moved = false;
        CaptureMouse();
        UpdateRightBand(pos);
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        if (_rightStart == null) return;
        var start = _rightStart.Value;
        var end = Clamp(e.GetPosition(this));
        _rightStart = null;
        ReleaseMouseCapture();
        _line1.Visibility = _line2.Visibility = Visibility.Collapsed;
        if (_rbFunction == 2)
        {
            var (x0, _) = P(start);
            var (x1, _) = P(end);
            if (_moved && x0 != x1)
            {
                _s.SliceRange = (Session.XValueAt(_doc, x0), Session.XValueAt(_doc, x1));
                _s.Shell.Dialogs.Show(_s, DialogKind.SliceAverage);
            }
            else
            {
                _s.PickTime = Session.XValueAt(_doc, x0);
                _s.Shell.Dialogs.Show(_s, DialogKind.DataPick);
            }
        }
        e.Handled = true;
    }

    private void UpdateRightBand(Point p)
    {
        p = Clamp(p);
        var start = _rightStart!.Value;
        if (_rbFunction == 0)
        {
            SetLine(_line1, start.X, start.Y, p.X, p.Y);
            _line2.Visibility = Visibility.Collapsed;
            var (x0, y0) = P(start);
            var (x1, y1) = P(p);
            _s.SetStatus(_s.DeltaText(_doc, x0, y0, x1, y1));
        }
        else if (_rbFunction == 3)
        {
            SetLine(_line1, p.X, _doc.GraphTop, p.X, _doc.GraphBottom);
            _line2.Visibility = Visibility.Collapsed;
            _s.SetStatus(_s.TrackText(_doc, (int)p.X));
        }
        else
        {
            SetLine(_line1, start.X, _doc.GraphTop, start.X, _doc.GraphBottom);
            SetLine(_line2, p.X, _doc.GraphTop, p.X, _doc.GraphBottom);
            _s.SetStatus(_s.TimeLabelAt(_doc, (int)start.X) + "  to  " + _s.TimeLabelAt(_doc, (int)p.X));
        }
    }

    private static void SetLine(Line l, double x1, double y1, double x2, double y2)
    {
        l.X1 = x1; l.Y1 = y1; l.X2 = x2; l.Y2 = y2;
        l.Visibility = Visibility.Visible;
    }

    /// <inheritdoc/>
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle)
        {
            var (x, y) = P(e.GetPosition(this));
            if (InGraph(x, y))
            {
                _midAnchor = e.GetPosition(this);
                _savedRate = (_doc.Config.DataRate, _doc.Config.SamFreq);
                _doc.Config.DataRate = DataRateMode.Sampled;
                _doc.Config.SamFreq = 1000;
                Cursor = Cursors.SizeAll;
                CaptureMouse();
                e.Handled = true;
            }
        }
        base.OnMouseDown(e);
    }

    private (int Rate, uint Sam) _savedRate;

    /// <inheritdoc/>
    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && _midAnchor != null)
        {
            _midAnchor = null;
            _doc.Config.DataRate = _savedRate.Rate;
            _doc.Config.SamFreq = _savedRate.Sam;
            Cursor = Cursors.Cross;
            ReleaseMouseCapture();
            InvalidateVisual();
        }
        base.OnMouseUp(e);
    }

    /// <inheritdoc/>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        var pos = e.GetPosition(this);
        if (_leftStart != null)
        {
            var p = Clamp(pos);
            var s = _leftStart.Value;
            Canvas.SetLeft(_box, Math.Min(s.X, p.X));
            Canvas.SetTop(_box, Math.Min(s.Y, p.Y));
            _box.Width = Math.Abs(p.X - s.X);
            _box.Height = Math.Abs(p.Y - s.Y);
            _box.Visibility = Visibility.Visible;
            _moved = true;
            return;
        }
        if (_rightStart != null)
        {
            _moved = true;
            UpdateRightBand(pos);
            return;
        }
        if (_midAnchor != null)
        {
            var a = _midAnchor.Value;
            int dx = (int)(pos.X - a.X), dy = (int)(pos.Y - a.Y);
            if (Math.Abs(dx) > 20 || Math.Abs(dy) > 20)
            {
                _s.MousePan(_doc, dx, dy, Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift));
                _midAnchor = new Point(Math.Abs(dx) > 20 ? pos.X : a.X, Math.Abs(dy) > 20 ? pos.Y : a.Y);
            }
            return;
        }
        var (x, y) = P(pos);
        string? t = _s.CoordinatesText(_doc, x, y);
        if (_s.Settings.ShowCoord) _s.SetStatus(t ?? "");
    }

    /// <inheritdoc/>
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        var (x, y) = P(e.GetPosition(this));
        _s.MouseWheel(_doc, x, y, e.Delta, Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift));
        e.Handled = true;
    }

    private void ShowContextMenu(int x, int y)
    {
        var region = Session.RegionAt(_doc, x, y);
        int idx = (int)region - 1;
        if (idx < 0 || idx >= MenuDefinitions.PlotPopups.Length) return;
        var cm = new ContextMenu();
        _win.Main?.FillMenu(cm.Items, MenuDefinitions.PlotPopups[idx].Children ?? Array.Empty<MenuNode>(), id => ExecutePopup(id, x, y));
        cm.PlacementTarget = this;
        cm.IsOpen = true;
    }

    private async void ExecutePopup(int id, int x, int y)
    {
        bool save = _s.Settings.ApplyAll;
        _s.Settings.ApplyAll = _doc.Config.PidVsPid || _doc.Config.EndToEnd ? false : _s.Settings.PopupsGlobal && save;
        _s.ActivatePlot(_doc);
        try
        {
            if (id == Core.CommandIds.ID_ADDPARAMETER) _s.AddParameterToPlot(x, y, (int)ActualWidth, (int)ActualHeight);
            else await _s.ExecuteCommandAsync(id);
        }
        finally { _s.Settings.ApplyAll = save; }
    }
}

internal static class PlotWindowExtensions
{
    public static MainWindow? MainOf(this PlotWindow w) => Window.GetWindow(w) as MainWindow;
}
