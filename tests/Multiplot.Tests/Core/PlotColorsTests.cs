// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

using Multiplot.Core.Model;

namespace Multiplot.Tests.Core;

public class PlotColorsTests
{
    [Fact]
    public void DefaultPalette_HasNoBlackWhiteOrDuplicatesAndCycles()
    {
        var p = PlotColors.Palette;
        Assert.Equal(25, p.Length);
        Assert.DoesNotContain(0x000000, p);
        Assert.DoesNotContain(0xFFFFFF, p);
        Assert.Equal(p.Length, p.Distinct().Count());
        var d = ColorIndex.Defaults();
        Assert.DoesNotContain(d[ColorIndex.Redline], p);
        for (int i = 0; i < ColorIndex.MaxColors; i++) Assert.Equal(p[i % p.Length], d[i]);
        Assert.Equal(0x0000FF, d[0]);   // red, blue, green, ... as in the shipped Multiplot.ini
        Assert.Equal(0xFF0000, d[1]);
    }

    [Fact]
    public void LightAppearance_IsUnchanged()
    {
        foreach (int c in new[] { 0, 0xFFFFFF, 0x123456 })
            for (int i = 0; i < ColorIndex.Total; i++) Assert.Equal(c, PlotColors.Map(c, i, false));
    }

    [Fact]
    public void DarkAppearance_DarkensLightBackgroundAndLightensDarkForegrounds()
    {
        Assert.Equal(0x1E1E1E, PlotColors.Map(0xFFFFFF, ColorIndex.Background, true));
        Assert.Equal(0x202020, PlotColors.Map(0x202020, ColorIndex.Background, true)); // already dark
        Assert.True(PlotColors.Luminance(PlotColors.Map(0, ColorIndex.Label, true)) >= 0.55);   // black text -> light
        Assert.True(PlotColors.Luminance(PlotColors.Map(0x400000, 3, true)) >= 0.18);           // dark blue curve -> lighter
        Assert.Equal(0x00FF00, PlotColors.Map(0x00FF00, 3, true));                              // bright curve kept
        int lifted = PlotColors.Map(0x000080, 6, true);                                          // maroon keeps its hue
        Assert.True((lifted & 0xff) > ((lifted >> 16) & 0xff));
    }
}
