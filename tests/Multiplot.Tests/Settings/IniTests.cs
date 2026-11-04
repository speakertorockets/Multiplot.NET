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
        Assert.Equal(100, s.Plot.TitlePointSize);
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
        IniFile.EnsureToolbars(a);
        string first = IniFile.Format(a, Ctx);

        string tmp = Path.Combine(Path.GetTempPath(), "mpnet_ini_" + Guid.NewGuid().ToString("N") + ".ini");
        try
        {
            File.WriteAllText(tmp, first);
            var b = new AppSettings();
            IniFile.Load(b, tmp, Ctx);
            IniFile.EnsureToolbars(b);
            string second = IniFile.Format(b, Ctx);
            Assert.Equal(Strip(first), Strip(second));
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Writer_Quirks()
    {
        var s = new AppSettings { TRef = "CUTOFF" };
        IniFile.EnsureToolbars(s);
        string ini = IniFile.Format(s, Ctx);
        Assert.Contains("DefaultReference = NONE\r\n", ini);
        Assert.Contains(";Saved at 1:05PM  October 16, 2026\r\n", ini);
        Assert.Contains("PrinterMargin = 5.000000\r\n", ini);
        Assert.Contains("AverageRate = 1.000\r\n", ini);
        Assert.Contains("TitleFont = 140 45 0 0 0 0 0 0 0 0 0 0 0 0 Multiplot\r\n", ini);
        Assert.Contains("GridStyle = dotted\r\n", ini);
        Assert.Contains("ButtonSet00 = 1 57600 0 ", ini);
        s.TRef = "Start";
        Assert.Contains("DefaultReference = Start\r\n", IniFile.Format(s, Ctx));
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
}
