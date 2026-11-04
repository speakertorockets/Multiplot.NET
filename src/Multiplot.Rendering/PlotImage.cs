using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Multiplot.Core.Model;

namespace Multiplot.Rendering;

/// <summary>Raster rendering and image file output of plots (FR-XPT-030; BMP, JPEG, GIF, PNG per OQ-003).</summary>
public static class PlotImage
{
    /// <summary>Supported picture types in menu order.</summary>
    public static readonly string[] Types = { "BMP", "JPEG", "GIF", "PNG" };

    /// <summary>
    /// Renders a plot laid out at <paramref name="width"/>×<paramref name="height"/> DIPs to a bitmap scaled by
    /// <paramref name="dpiScaleX"/>/<paramref name="dpiScaleY"/> (1 = one pixel per DIP). Rasterization happens on the
    /// calling thread, so the WPF render thread only ever receives the finished image regardless of data volume.
    /// </summary>
    public static BitmapSource Render(PlotDocument doc, int width, int height, bool optimizedDraw = true, bool imageCopy = true, double dpiScaleX = 1.0, double dpiScaleY = 1.0)
    {
        width = Math.Max(16, width);
        height = Math.Max(16, height);
        if (!(dpiScaleX > 0)) dpiScaleX = 1.0;
        if (!(dpiScaleY > 0)) dpiScaleY = 1.0;
        // RenderTargetBitmap ignores EdgeMode on the root visual; a DrawingGroup carries it reliably.
        var group = new DrawingGroup();
        RenderOptions.SetEdgeMode(group, EdgeMode.Aliased);
        using (var gc = group.Open())
        {
            new PlotRenderer(doc) { OptimizedDraw = optimizedDraw }.Draw(gc, width, height, imageCopy);
        }
        group.Freeze();
        var dv = new DrawingVisual();
        RenderOptions.SetEdgeMode(dv, EdgeMode.Aliased);
        TextOptions.SetTextFormattingMode(dv, TextFormattingMode.Display);
        using (var dc = dv.RenderOpen())
        {
            dc.DrawDrawing(group);
        }
        int pw = (int)Math.Ceiling(width * dpiScaleX), ph = (int)Math.Ceiling(height * dpiScaleY);
        var rtb = new RenderTargetBitmap(pw, ph, 96 * dpiScaleX, 96 * dpiScaleY, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    /// <summary>Normalises a picture type name (JPG → JPEG); null when unsupported.</summary>
    public static string? NormalizeType(string type)
    {
        string t = type.Trim().ToUpperInvariant();
        if (t == "JPG") t = "JPEG";
        return Array.IndexOf(Types, t) >= 0 ? t : null;
    }

    /// <summary>File extension of a type.</summary>
    public static string Extension(string type) => NormalizeType(type) switch
    {
        "JPEG" => "jpg",
        "GIF" => "gif",
        "BMP" => "bmp",
        _ => "png",
    };

    /// <summary>Saves a bitmap in the given format.</summary>
    public static void Save(BitmapSource bmp, string path, string type)
    {
        BitmapEncoder enc = NormalizeType(type) switch
        {
            "BMP" => new BmpBitmapEncoder(),
            "JPEG" => new JpegBitmapEncoder { QualityLevel = 90 },
            "GIF" => new GifBitmapEncoder(),
            _ => new PngBitmapEncoder(),
        };
        BitmapSource src = bmp;
        if (enc is BmpBitmapEncoder or JpegBitmapEncoder or GifBitmapEncoder)
            src = new FormatConvertedBitmap(bmp, PixelFormats.Bgr24, null, 0);
        enc.Frames.Add(BitmapFrame.Create(src));
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        enc.Save(fs);
    }
}
