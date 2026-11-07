// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

namespace Multiplot.Core.Model;

/// <summary>
/// Colours as they are displayed (FR-UI-046): in the light appearance the colour table is used as it is;
/// in the dark appearance a light plot background is drawn dark and the colours drawn on it (curves, grid,
/// text, markers) are lightened until they are readable. The colour tables themselves are never changed,
/// so switching back to the light appearance restores the original look.
/// </summary>
public static class PlotColors
{
    /// <summary>Default curve palette (COLORREF 0x00BBGGRR), repeated for curve colours 1..100. No black or white.</summary>
    public static readonly int[] Palette =
    {
        0x0000FF, 0xFF0000, 0x008000, 0x4080FF, 0xFF00FF, 0xC08000, 0x000080, 0x0099CC, 0x00FF00, 0x400080,
        0x808040, 0xFFFF00, 0x8080FF, 0x8000FF, 0xFF0080, 0xFF8000, 0xFF8080, 0x008080, 0x800000, 0x000040,
        0x004000, 0x400000, 0x004040, 0x404000, 0x400040,
    };

    /// <summary>Background drawn in the dark appearance for a light background (0x1E1E1E for white).</summary>
    private const double DarkBackgroundScale = 30.0 / 255.0;

    /// <summary>Relative luminance (sRGB, 0..1) of a COLORREF.</summary>
    public static double Luminance(int colorRef)
    {
        static double Lin(int c)
        {
            double v = c / 255.0;
            return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        return (0.2126 * Lin(colorRef & 0xff)) + (0.7152 * Lin((colorRef >> 8) & 0xff)) + (0.0722 * Lin((colorRef >> 16) & 0xff));
    }

    /// <summary>
    /// Minimum luminance of a colour drawn on the dark background, by colour-table index: text (labels,
    /// titles, monochrome) is the brightest, markers next, curves and redlines next, the grid the dimmest.
    /// </summary>
    private static double MinLuminance(int index) => index switch
    {
        ColorIndex.Label or ColorIndex.Title or ColorIndex.Mono => 0.55,
        ColorIndex.Marker => 0.25,
        ColorIndex.Grid => 0.10,
        _ => 0.18,
    };

    /// <summary>
    /// The colour displayed for colour-table entry <paramref name="index"/> whose value is
    /// <paramref name="colorRef"/>, in the light (<paramref name="dark"/> false) or dark appearance.
    /// </summary>
    public static int Map(int colorRef, int index, bool dark)
    {
        if (!dark) return colorRef;
        colorRef &= 0xFFFFFF;
        if (index == ColorIndex.Background)
        {
            if (Luminance(colorRef) < 0.18) return colorRef; // already dark
            return Scale(colorRef, DarkBackgroundScale);
        }
        double min = MinLuminance(index);
        if (Luminance(colorRef) >= min) return colorRef;
        // mix towards white until the colour is light enough
        double lo = 0, hi = 1;
        for (int i = 0; i < 20; i++)
        {
            double t = (lo + hi) / 2;
            if (Luminance(Mix(colorRef, t)) >= min) hi = t; else lo = t;
        }
        return Mix(colorRef, hi);
    }

    /// <summary>Displayed colour of entry <paramref name="index"/> of <paramref name="table"/>.</summary>
    public static int Display(int[] table, int index, bool dark) => Map(table[index], index, dark);

    private static int Scale(int c, double k) =>
        (int)Math.Round((c & 0xff) * k) | ((int)Math.Round(((c >> 8) & 0xff) * k) << 8) | ((int)Math.Round(((c >> 16) & 0xff) * k) << 16);

    private static int Mix(int c, double t)
    {
        int M(int v) => (int)Math.Round(v + ((255 - v) * t));
        return M(c & 0xff) | (M((c >> 8) & 0xff) << 8) | (M((c >> 16) & 0xff) << 16);
    }
}
