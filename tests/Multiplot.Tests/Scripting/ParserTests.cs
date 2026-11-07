// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

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
    public void ParseRedline_ValueTextPosition()
    {
        var r = RedlineMarker.Parse("450, Max temperature")!;
        Assert.Equal((450.0, "Max temperature", false), (r.Value, r.Text, r.Right));
        r = RedlineMarker.Parse("-1.5e3,Low limit, stage 2,right")!;
        Assert.Equal((-1500.0, "Low limit, stage 2", true), (r.Value, r.Text, r.Right));
        r = RedlineMarker.Parse("0.25,Limit,LEFT")!;
        Assert.Equal((0.25, "Limit", false), (r.Value, r.Text, r.Right));
        Assert.Equal("0.25,Limit", r.ToSpec());
        Assert.Equal("redline 12.5,a,RIGHT", new RedlineMarker(12.5, "a", true).ToCommand());
        Assert.Equal("RIGHT", RedlineMarker.Parse("1,RIGHT")!.Text); // a lone word is the text, not a position

        Assert.Null(RedlineMarker.Parse(""));
        Assert.Null(RedlineMarker.Parse("12.5"));          // no text
        Assert.Null(RedlineMarker.Parse("12.5,"));         // empty text
        Assert.Null(RedlineMarker.Parse("abc,text"));      // value not a number
        Assert.Null(RedlineMarker.Parse("NaN,text"));      // value not finite

        Assert.True(RedlineMarker.TryParseCommand("REDLINE 3,Three", out var c));
        Assert.Equal((3.0, "Three"), (c!.Value, c.Text));
        Assert.True(RedlineMarker.TryParseCommand("redline x,bad", out c));
        Assert.Null(c);
        Assert.False(RedlineMarker.TryParseCommand("100/12:00:05,Ignition", out _));
        Assert.Equal(Cmd.Redline, ScriptKeywords.GetCmdKey("REDLINE 1,a"));
    }

    [Fact]
    public void ParseMarker_TimestampTextPosition()
    {
        var m = Marker.Parse("100/12:00:05.250, Ignition")!;
        Assert.Equal(("100/12:00:05.250", "Ignition", false), (m.Time, m.Text, m.Bottom));
        m = Marker.Parse("IGNITION+2.5,Valve open, stage 2,bottom")!;
        Assert.Equal(("IGNITION+2.5", "Valve open, stage 2", true), (m.Time, m.Text, m.Bottom));
        m = Marker.Parse("12.5,Shutdown,TOP")!;
        Assert.Equal(("12.5", "Shutdown", false), (m.Time, m.Text, m.Bottom));
        Assert.Equal("12.5,Shutdown", m.ToSpec());
        Assert.Equal("1,a,BOTTOM", new Marker("1", "a", true).ToSpec());

        Assert.Null(Marker.Parse(""));
        Assert.Null(Marker.Parse("12.5"));                 // no text
        Assert.Null(Marker.Parse("12.5,"));                // empty text
        Assert.Null(Marker.Parse(",text"));                // no timestamp
        Assert.Equal("BOTTOM", Marker.Parse("12.5,BOTTOM")!.Text); // a lone word is the text, not a position
        Assert.Null(Marker.Parse("Marker = 1 1 4 0 0 0 0 1 EOF")); // old .markers format is rejected
        Assert.Null(Marker.Parse("1,EVENT,0,0,0,0,GRID,IGN"));  // old MARKER command format is rejected
        Assert.Null(Marker.Parse("x y,z"));                // white space in timestamp
    }

    [Fact]
    public void Marker_SetsIgnoreDuplicatesAndReservedEvents()
    {
        var set = new List<Marker>();
        Assert.True(Marker.AddTo(set, new Marker("10", "A")));
        Assert.False(Marker.AddTo(set, new Marker("10", "A")));
        Assert.True(Marker.AddTo(set, new Marker("10", "A", true)));
        Assert.Equal(2, set.Count);
        Assert.True(Marker.IsReservedEvent("FILE_REF"));
        Assert.True(Marker.IsReservedEvent("eof"));
        Assert.True(Marker.IsReservedEvent("BAD(invalid)"));
        Assert.False(Marker.IsReservedEvent("IGNITION"));
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
        Assert.True(CommandParsers.ParseSetColor(" MARKER,0,128,0", out no, out rgb, out g));
        Assert.Equal((ColorIndex.Marker, 0x008000, false), (no, rgb, g));
        Assert.True(CommandParsers.ParseSetColor(" redline,255,0,0 GLOBAL", out no, out rgb, out g));
        Assert.Equal((ColorIndex.Redline, 0x0000FF, true), (no, rgb, g));
    }
}
