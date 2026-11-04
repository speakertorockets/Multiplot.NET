using Multiplot.Core.Model;
using Multiplot.Scripting;

namespace Multiplot.Tests.Scripting;

public class ParserTests
{
    [Fact]
    public void ParseNomen_OrderAndLengths()
    {
        var len = new int[Nomen.ITEMS];
        var ord = (int[])Nomen.DefaultOrder.Clone();
        string err = "";
        int errors = CommandParsers.ParseNomen("NAME:20 UNITS DESC", true, len, ord, out int set, ref err);
        Assert.Equal(0, errors);
        Assert.Equal(Nomen.NAME | Nomen.UNITS | Nomen.DESC, set);
        Assert.Equal(20, len[Nomen.I_NAME]);
        Assert.Equal(new[] { Nomen.NAME, Nomen.UNITS, Nomen.DESC, Nomen.FILENAME, Nomen.DBNAME, Nomen.REFT, Nomen.UDEF,
            Nomen.ALTNAME, Nomen.EXTNAME, Nomen.SEQNUM, Nomen.CURVAL, Nomen.CURTIME }, ord);
    }

    [Fact]
    public void ParseNomen_InvalidLength()
    {
        var len = new int[Nomen.ITEMS];
        var ord = new int[Nomen.ITEMS];
        string err = "";
        int errors = CommandParsers.ParseNomen("UNITS:300", false, len, ord, out _, ref err);
        Assert.Equal(1, errors);
        Assert.Equal("invalid length found (300)", err);
        Assert.Equal(0, len[Nomen.I_UNITS]);
    }

    [Fact]
    public void ParseFix_SpaceBeforePersistGivesZeroIncrement()
    {
        var r = CommandParsers.ParseFix("0,100, p", true);
        Assert.False(r.Error);
        var f = Assert.Single(r.Ranges);
        Assert.Equal(0.0, f.Min);
        Assert.Equal(100.0, f.Max);
        Assert.Equal(0.0, f.Grad);
        Assert.True(f.Fixed);
        Assert.True(f.Persist);
    }

    [Fact]
    public void ParseFix_SwapsAndPersistOnly()
    {
        var f = CommandParsers.ParseFix("100,0,p", false).Ranges.Single();
        Assert.Equal((0.0, 100.0, -1.0, false, true), (f.Min, f.Max, f.Grad, f.Fixed, f.Persist));
        Assert.True(CommandParsers.ParseFix("AUTO", false).Auto);
        Assert.True(CommandParsers.ParseFix("12", false).Error);
    }

    [Fact]
    public void ParseFix_Multi()
    {
        var r = CommandParsers.ParseFix("multi 0,10,a,5,20,2 p", true);
        Assert.True(r.Multi);
        Assert.Equal(2, r.Ranges.Count);
        Assert.False(r.Ranges[0].Fixed);
        Assert.Equal(2.0, r.Ranges[1].Grad);
        Assert.True(r.Ranges[1].Persist);
        Assert.True(CommandParsers.ParseFix("multi 0,10", true).Error);
    }

    [Fact]
    public void ParseTime_TripletsAndGroupPrefix()
    {
        var t = CommandParsers.ParseTime("1,2,3,4,5,6", "s", "r");
        Assert.Equal(2, t.Count);
        Assert.Equal(("1", "2", "3"), (t[0].Start, t[0].Stop, t[0].Ref));
        Assert.Equal(("4", "5", "6"), (t[1].Start, t[1].Stop, t[1].Ref));

        var g = CommandParsers.ParseTime("grp:10,20,START,99,98,97", "s", "r").Single();
        Assert.Equal(("grp", "10", "20", "START"), (g.GroupName, g.Start, g.Stop, g.Ref));

        var keep = CommandParsers.ParseTime("1,2", "s", "PREV").Single();
        Assert.Equal("PREV", keep.Ref);

        // quirk DEV-SCR-007: a clock start is read as a group name
        Assert.Equal("12", CommandParsers.ParseTime("12:00:00,13:00:00,NONE", "", "").Single().GroupName);
    }

    [Fact]
    public void ParseImport_OptionsAndDestination()
    {
        var r = CommandParsers.ParseImport("data.csv out.sun /I2 /S3 /G /R2024 001 00:00:00.000.000")!;
        Assert.Equal("data.csv", r.Source);
        Assert.Equal("out.sun", r.Destination);
        Assert.Equal(2, r.IndexColumn);
        Assert.Equal(3, r.SkipLines);
        Assert.True(r.GenerateTimeTags);
        Assert.False(r.StandardFormat);
        Assert.Equal("2024 001 00:00:00.000.000", r.Reference);

        var q = CommandParsers.ParseImport("\"C:\\a b\\x.csv\" \"C:\\o.sun\"")!;
        Assert.Equal("C:\\a b\\x.csv", q.Source);
        Assert.Equal("C:\\o.sun", q.Destination);

        var single = CommandParsers.ParseImport("\"C:\\a b\\x.csv\"")!;
        Assert.Null(single.Destination);
        Assert.Null(CommandParsers.ParseImport("  /F"));
    }

    [Fact]
    public void ParseInput_Fields()
    {
        var i = CommandParsers.ParseInput("Enter value, 12, My caption, $VAL");
        Assert.Equal(("Enter value", "12", "My caption", "VAL"), (i.Prompt, i.Default, i.Caption, i.Symbol));
        Assert.Equal("USER_INPUT", CommandParsers.ParseInput("Hello").Symbol);
    }

    [Fact]
    public void ParseMarker_RequiresColor()
    {
        var m = new Marker();
        Assert.Equal(-1, CommandParsers.ParseMarker("1,TEXT,1,2,3,4", m, out _));
        Assert.Equal(2, CommandParsers.ParseMarker("3,redline,0,10,100,10,LABEL,MAX\\g", m, out bool g));
        Assert.True(g);
        Assert.Equal(MarkerType.Redline, m.Type);
        Assert.Equal(ColorIndex.Label, m.Color);
        Assert.Equal("MAX", m.Label);
        Assert.Equal(0, CommandParsers.ParseMarker("1,9,1,2,3,4,5", m, out _));
        Assert.Equal(MarkerType.Text, m.Type);
        Assert.Equal(4, m.Color);
        Assert.Equal("", m.Label);
    }

    [Fact]
    public void SetTrace_BothForms()
    {
        var c = new Curve(Multiplot.Plugins.ParameterData.FromFloat("x", new float[1], new float[1], 1));
        var curves = new List<Curve> { c };
        Assert.True(CommandParsers.ApplySetTrace("TRACE 1,2,3,1,5,0,7,0,2,3", curves, out _));
        Assert.Equal((2, 2, true, 4, false, 6, false, 2, 3), (c.LineSize, c.LineStyle, c.UseSymbols, c.SymbolId, c.ScatterPlot, c.ColorIndex, c.TabLegend, c.TabFormat, c.LogicTxt));
        Assert.True(CommandParsers.ApplySetTrace("TRACE 1, line_size=3, TAB_FORMAT=DEC5, LOGIC_INDEX=1", curves, out _));
        Assert.Equal((3, 5, 0), (c.LineSize, c.TabFormat, c.LogicTxt));
        Assert.False(CommandParsers.ApplySetTrace("TRACE 2,1", curves, out _));
    }

    [Fact]
    public void Precision_AndColor()
    {
        Assert.Equal((-1, 3), CommandParsers.ParsePrecision("GENERAL 3"));
        Assert.Equal(((int?)-7, (int?)null), CommandParsers.ParsePrecision("hex2,xyz"));
        Assert.True(CommandParsers.ParseSetColor(" GRID,255,0,1 GLOBAL", out int no, out int rgb, out bool g));
        Assert.Equal((ColorIndex.Grid, 0x0100FF, true), (no, rgb, g));
        Assert.False(CommandParsers.ParseSetColor(" 3,1,2", out _, out _, out _));
    }
}
