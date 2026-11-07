// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Multiplot.Rendering;

/// <summary>A font for <see cref="GdiCanvas"/> (GDI LOGFONT semantics).</summary>
public sealed class GdiFont
{
    public Typeface Typeface { get; }
    public double EmSize { get; }
    public bool Vertical { get; }

    public GdiFont(string face, int lfHeight, int weight, bool italic, bool vertical)
    {
        var family = Resolve(face);
        Typeface = new Typeface(family, italic ? FontStyles.Italic : FontStyles.Normal,
            FontWeight.FromOpenTypeWeight(Math.Clamp(weight <= 0 ? 400 : weight, 1, 999)), FontStretches.Normal);
        double ls = family.LineSpacing > 0 ? family.LineSpacing : 1.15;
        int h = lfHeight == 0 ? 12 : lfHeight;
        EmSize = Math.Max(1.0, h > 0 ? h / ls : -h);
        Vertical = vertical;
    }

    private static readonly Dictionary<string, FontFamily> s_families = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Face lookup; faces that are not installed map to Arial like GDI font mapping.</summary>
    private static FontFamily Resolve(string face)
    {
        if (string.IsNullOrWhiteSpace(face)) face = "Arial";
        lock (s_families)
        {
            if (s_families.TryGetValue(face, out var f)) return f;
            bool installed = Fonts.SystemFontFamilies.Any(x => string.Equals(x.Source, face, StringComparison.OrdinalIgnoreCase));
            f = new FontFamily(installed ? face : "Arial");
            s_families[face] = f;
            return f;
        }
    }
}

/// <summary>A GDI style pen.</summary>
public readonly record struct GdiPen(int Style, int Width, int ColorRef)
{
    public const int Solid = 0, Dash = 1, Dot = 2, DashDot = 3, DashDotDot = 4;
}

/// <summary>
/// Minimal GDI-like drawing surface over a WPF <see cref="DrawingContext"/>: integer pixel coordinates,
/// MoveTo/LineTo with the current pen, TextOut at the top-left reference point, clip rectangles.
/// Consecutive line segments are batched into geometries for performance (DEV-UI-001).
/// </summary>
public sealed class GdiCanvas
{
    private readonly DrawingContext _dc;
    private readonly double _pixelsPerDip;
    private GdiPen _pen = new(GdiPen.Solid, 1, 0);
    private Pen _wpfPen;
    private readonly Dictionary<GdiPen, Pen> _penCache = new();
    private readonly Dictionary<int, Brush> _brushCache = new();
    private StreamGeometry? _geom;
    private StreamGeometryContext? _ctx;
    private bool _figureOpen;
    private int _curX, _curY;
    private int _clipDepth;
    public GdiFont? Font { get; set; }
    public int TextColor { get; set; }
    public Brush? FillBrush { get; set; }

    public GdiCanvas(DrawingContext dc, double pixelsPerDip = 1.0)
    {
        _dc = dc;
        _pixelsPerDip = pixelsPerDip;
        _wpfPen = MakePen(_pen);
    }

    public static Color ToColor(int colorRef) => Color.FromRgb((byte)(colorRef & 0xff), (byte)((colorRef >> 8) & 0xff), (byte)((colorRef >> 16) & 0xff));

    public Brush BrushOf(int colorRef)
    {
        if (!_brushCache.TryGetValue(colorRef, out var b))
        {
            b = new SolidColorBrush(ToColor(colorRef));
            b.Freeze();
            _brushCache[colorRef] = b;
        }
        return b;
    }

    private Pen MakePen(GdiPen p)
    {
        if (_penCache.TryGetValue(p, out var pen)) return pen;
        pen = new Pen(BrushOf(p.ColorRef), Math.Max(1, p.Width)) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat, LineJoin = PenLineJoin.Miter };
        if (p.Width <= 1)
        {
            pen.DashStyle = p.Style switch
            {
                GdiPen.Dash => new DashStyle(new double[] { 6, 2 }, 0),
                GdiPen.Dot => new DashStyle(new double[] { 1, 2 }, 0),
                GdiPen.DashDot => new DashStyle(new double[] { 6, 2, 1, 2 }, 0),
                GdiPen.DashDotDot => new DashStyle(new double[] { 6, 2, 1, 2, 1, 2 }, 0),
                _ => DashStyles.Solid,
            };
        }
        pen.Freeze();
        _penCache[p] = pen;
        return pen;
    }

    public GdiPen Pen
    {
        get => _pen;
        set
        {
            if (value == _pen) return;
            Flush();
            _pen = value;
            _wpfPen = MakePen(value);
        }
    }

    private double Off => (Math.Max(1, _pen.Width) % 2 == 1) ? 0.5 : 0.0;

    public void MoveTo(int x, int y)
    {
        _curX = x;
        _curY = y;
        _figureOpen = false;
    }

    public void LineTo(int x, int y)
    {
        if (_geom == null)
        {
            _geom = new StreamGeometry();
            _ctx = _geom.Open();
        }
        double o = Off;
        if (!_figureOpen)
        {
            _ctx!.BeginFigure(new Point(_curX + o, _curY + o), false, false);
            _figureOpen = true;
        }
        _ctx!.LineTo(new Point(x + o, y + o), true, false);
        _curX = x;
        _curY = y;
    }

    public void Flush()
    {
        if (_geom == null) return;
        _ctx!.Close();
        _geom.Freeze();
        _dc.DrawGeometry(null, _wpfPen, _geom);
        _geom = null;
        _ctx = null;
        _figureOpen = false;
    }

    public void SetPixel(int x, int y, int colorRef)
    {
        Flush();
        _dc.DrawRectangle(BrushOf(colorRef), null, new Rect(x, y, 1, 1));
        _figureOpen = false;
    }

    public void FillRect(int x, int y, int w, int h, int colorRef)
    {
        Flush();
        _dc.DrawRectangle(BrushOf(colorRef), null, new Rect(x, y, Math.Max(0, w), Math.Max(0, h)));
    }

    private FormattedText Format(string text, int colorRef)
    {
        var f = Font ?? new GdiFont("Arial", 12, 400, false, false);
        return new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, f.Typeface, f.EmSize, BrushOf(colorRef), _pixelsPerDip);
    }

    /// <summary>GDI GetTextExtent (cx, cy); cy is the font cell height even for empty text.</summary>
    public (int cx, int cy) TextExtent(string text)
    {
        var ft = Format(text.Length == 0 ? " " : text, 0);
        double w = text.Length == 0 ? 0 : ft.WidthIncludingTrailingWhitespace;
        var f = Font ?? new GdiFont("Arial", 12, 400, false, false);
        double h = f.EmSize * f.Typeface.FontFamily.LineSpacing;
        return ((int)Math.Ceiling(w), (int)Math.Ceiling(h));
    }

    public void TextOut(int x, int y, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        Flush();
        var ft = Format(text, TextColor);
        if (Font?.Vertical == true)
        {
            _dc.PushTransform(new RotateTransform(-90, x, y));
            _dc.DrawText(ft, new Point(x, y));
            _dc.Pop();
        }
        else
            _dc.DrawText(ft, new Point(x, y));
        _figureOpen = false;
    }

    public void PushClip(int left, int top, int right, int bottom)
    {
        Flush();
        _dc.PushClip(new RectangleGeometry(new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top))));
        _clipDepth++;
    }

    public void PopClip()
    {
        if (_clipDepth == 0) return;
        Flush();
        _dc.Pop();
        _clipDepth--;
    }

    public void PopAllClips()
    {
        while (_clipDepth > 0) PopClip();
    }

    public void Polygon(ReadOnlySpan<(int x, int y)> pts, Brush? fill)
    {
        Flush();
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(pts[0].x, pts[0].y), fill != null, true);
            for (int i = 1; i < pts.Length; i++) c.LineTo(new Point(pts[i].x, pts[i].y), true, false);
        }
        g.Freeze();
        _dc.DrawGeometry(fill, _wpfPen, g);
    }

    public void Ellipse(int l, int t, int r, int b)
    {
        Flush();
        _dc.DrawEllipse(null, _wpfPen, new Point((l + r) / 2.0, (t + b) / 2.0), Math.Abs(r - l) / 2.0, Math.Abs(b - t) / 2.0);
    }

    public void DrawImage(ImageSource img, int x, int y, int w, int h)
    {
        Flush();
        _dc.DrawImage(img, new Rect(x, y, Math.Max(0, w), Math.Max(0, h)));
    }

    /// <summary>Loads an image file into a frozen bitmap (null on failure).</summary>
    public static BitmapSource? LoadImage(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.UriSource = new Uri(Path.GetFullPath(path));
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Finish()
    {
        Flush();
        PopAllClips();
    }
}
