// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

using Multiplot.Core.Model;
using Multiplot.Settings;

namespace Multiplot.Tests.Settings;

public class IniTests
{
    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "fixtures", "Multiplot.ini");

    private static readonly IniContext Ctx = new() { AppVersion = "Multiplot.NET v1.0.0", AppDate = "November 2026", Now = new DateTime(2026, 10, 16, 13, 5, 0) };

    private static string Strip(string ini) => string.Join("\n", ini.Split("\r\n").Where(l =>
        !l.StartsWith(";Saved at") && !l.StartsWith(";OS =") && !l.StartsWith(";memory") && !l.StartsWith(";owner")));

    [Fact]
    public void SampleIni_LoadsValues()
    {
        var s = new AppSettings();
        var r = IniFile.Load(s, Fixture, Ctx);
        Assert.True(r.Found);
        Assert.Equal(3, s.Plot.LegendColumns);
        Assert.Equal(58037, s.MultiGroupBy);
        Assert.Equal("Arial", s.Plot.TitleFont.FaceName);
        Assert.Equal(-13, s.Plot.TitleFont.Height);
        // old INI (FontSize = 2, title height -13, label height -11): the sizes that factor produced
        Assert.Equal((100, 118), (s.Plot.LabelPointSize, s.Plot.TitlePointSize));
        Assert.Equal(PauseMode.NextCommand, s.PauseMode);
        Assert.Equal(150u, s.Plot.AvgFreq);
        Assert.Equal("write /p", s.TextPrinter);
        Assert.Equal(3f, s.MarginSize);
        Assert.Equal(100, s.PreviousRefresh);
        Assert.StartsWith("Multiplot.NET\\nEdit", s.XTitle);
        Assert.True(s.CalcEveryInstance);
        Assert.False(s.AlignClosest);
        Assert.Equal(3, s.DataPrecision);
    }

    [Fact]
    public void SaveLoadSave_IsIdempotent()
    {
        var a = new AppSettings();
        IniFile.Load(a, Fixture, Ctx);
        string first = IniFile.Format(a, Ctx);

        string tmp = Path.Combine(Path.GetTempPath(), "mpnet_ini_" + Guid.NewGuid().ToString("N") + ".ini");
        try
        {
            File.WriteAllText(tmp, first);
            var b = new AppSettings();
            IniFile.Load(b, tmp, Ctx);
            string second = IniFile.Format(b, Ctx);
            Assert.Equal(Strip(first), Strip(second));
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Writer_Quirks()
    {
        var s = new AppSettings { TRef = "CUTOFF" };
        string ini = IniFile.Format(s, Ctx);
        Assert.Contains("DefaultReference = NONE\r\n", ini);
        Assert.Contains(";Saved at 1:05PM  October 16, 2026\r\n", ini);
        Assert.Contains("PrinterMargin = 5.000000\r\n", ini);
        Assert.Contains("AverageRate = 1.000\r\n", ini);
        Assert.Contains("TitleFont = 120 45 0 0 0 0 0 0 0 0 0 0 0 0 Multiplot\r\n", ini);
        Assert.DoesNotContain("FontSize", ini);
        Assert.Contains("GridStyle = dotted\r\n", ini);
        Assert.Contains("[interface]\r\nStatusBar = 1\r\n", ini);
        Assert.Contains("ShowFileEvents = 1\r\n", ini);
        Assert.DoesNotContain("ButtonSet", ini);
        Assert.DoesNotContain("ToolBar", ini);
        s.TRef = "Start";
        Assert.Contains("DefaultReference = Start\r\n", IniFile.Format(s, Ctx));
    }

    [Fact]
    public void Reader_IgnoresLegacyToolbarKeysAndUserButtons()
    {
        string tmp = Path.Combine(Path.GetTempPath(), "mpnet_ini_" + Guid.NewGuid().ToString("N") + ".ini");
        try
        {
            File.WriteAllText(tmp, "[toolbar]\r\nStatusBar = 0\r\nToolBar = 1\r\nIncludeToolBar = 0\r\nButtonSet00 = 1 57600 0 57601 1\r\n" +
                "UserButton0 = Go,run go.wps\r\nShowFileEvents = 0\r\nScaleRedlines = 1\r\n");
            var s = new AppSettings();
            Assert.True(IniFile.Load(s, tmp, Ctx).Found);
            Assert.False(s.ShowStatusBar);
            Assert.Equal(("", ""), (s.UserButtons[0].Label, s.UserButtons[0].Command)); // registry is authoritative
            Assert.False(s.Plot.ShowFileEvents);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Reader_EmptyBooleanIsTrue()
    {
        string tmp = Path.Combine(Path.GetTempPath(), "mpnet_ini_" + Guid.NewGuid().ToString("N") + ".ini");
        try
        {
            File.WriteAllText(tmp, "MinorTics =\r\nColors = 1 2 3\r\n");
            var s = new AppSettings();
            IniFile.Load(s, tmp, Ctx);
            Assert.True(s.Plot.MinorTics);
            Assert.Equal(1, s.Plot.DrawColor[0]);
            // short colour table: grid/label/background/mono copied from 25..28
            Assert.Equal(s.Plot.DrawColor[25], s.Plot.DrawColor[ColorIndex.Grid]);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Reader_ColorTableWithoutMarkerColors_KeepsGridAndUsesItForMarkers()
    {
        string tmp = Path.Combine(Path.GetTempPath(), "mpnet_ini_" + Guid.NewGuid().ToString("N") + ".ini");
        try
        {
            // 105-entry table (before the marker and redline colours): grid 11, label 22, background 33, mono 44, title 55
            var vals = Enumerable.Range(0, ColorIndex.MaxColors).Select(i => (i + 1000).ToString()).Concat(new[] { "11", "22", "33", "44", "55" });
            File.WriteAllText(tmp, "Colors = " + string.Join(" ", vals) + " \r\n");
            var s = new AppSettings();
            IniFile.Load(s, tmp, Ctx);
            Assert.Equal(1025, s.Plot.DrawColor[25]);
            Assert.Equal((11, 22, 33, 44, 55), (s.Plot.DrawColor[ColorIndex.Grid], s.Plot.DrawColor[ColorIndex.Label],
                s.Plot.DrawColor[ColorIndex.Background], s.Plot.DrawColor[ColorIndex.Mono], s.Plot.DrawColor[ColorIndex.Title]));
            Assert.Equal(11, s.Plot.DrawColor[ColorIndex.Marker]);
            Assert.Equal(ColorIndex.Defaults()[ColorIndex.Redline], s.Plot.DrawColor[ColorIndex.Redline]);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Fonts_PointSizesRoundTripAndLegacyFontSizeConverts()
    {
        string tmp = Path.Combine(Path.GetTempPath(), "mpnet_ini_" + Guid.NewGuid().ToString("N") + ".ini");
        try
        {
            // new INI: no FontSize key, point sizes are used as written
            File.WriteAllText(tmp, "[fonts]\r\nTitleFont = 180 -24 0 0 0 400 1 0 0 0 3 2 1 34 Georgia\r\nLabelFont = 90 -12 0 0 0 700 0 0 0 0 3 2 1 34 Segoe UI\r\n");
            var s = new AppSettings();
            IniFile.Load(s, tmp, Ctx);
            Assert.Equal((180, 90), (s.Plot.TitlePointSize, s.Plot.LabelPointSize));
            Assert.Equal(("Georgia", "Segoe UI", (byte)1), (s.Plot.TitleFont.FaceName, s.Plot.LabelFont.FaceName, s.Plot.TitleFont.Italic));
            string ini = IniFile.Format(s, Ctx);
            Assert.Contains("TitleFont = 180 -24 ", ini);
            Assert.Contains("LabelFont = 90 -12 ", ini);
            Assert.DoesNotContain("FontSize", ini);

            // old INI: FontSize 4 = the largest screen scaling
            File.WriteAllText(tmp, "[fonts]\r\nFontSize = 4\r\nTitleFont = 100 -15 0 0 0 700 0 0 0 0 3 2 1 34 Arial\r\nLabelFont = 80 -10 0 0 0 700 0 0 0 0 3 2 1 34 Arial\r\n");
            s = new AppSettings();
            IniFile.Load(s, tmp, Ctx);
            Assert.Equal((167, 250), (s.Plot.LabelPointSize, s.Plot.TitlePointSize));
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Appearance_RoundTrips()
    {
        string tmp = Path.Combine(Path.GetTempPath(), "mpnet_ini_" + Guid.NewGuid().ToString("N") + ".ini");
        try
        {
            var s = new AppSettings { DarkMode = true };
            string ini = IniFile.Format(s, Ctx);
            Assert.Contains("Appearance = Dark\r\n", ini);
            Assert.DoesNotContain("TimeStamp =", ini);
            Assert.DoesNotContain("ShowVersion", ini);
            File.WriteAllText(tmp, ini);
            var r = new AppSettings();
            IniFile.Load(r, tmp, Ctx);
            Assert.True(r.DarkMode);
            Assert.False(new AppSettings().DarkMode);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Colors_RoundTripIncludesMarkerAndRedline()
    {
        string tmp = Path.Combine(Path.GetTempPath(), "mpnet_ini_" + Guid.NewGuid().ToString("N") + ".ini");
        try
        {
            var s = new AppSettings();
            s.Plot.DrawColor[ColorIndex.Marker] = 0x123456;
            s.Plot.DrawColor[ColorIndex.Redline] = 0x00FF00;
            File.WriteAllText(tmp, IniFile.Format(s, Ctx));
            var r = new AppSettings();
            IniFile.Load(r, tmp, Ctx);
            Assert.Equal((0x123456, 0x00FF00), (r.Plot.DrawColor[ColorIndex.Marker], r.Plot.DrawColor[ColorIndex.Redline]));
        }
        finally { File.Delete(tmp); }
    }
}
