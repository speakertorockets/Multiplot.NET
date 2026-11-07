// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

using System.Text;
using Multiplot.Scripting;

namespace Multiplot.Tests.Scripting;

public class LexerTests
{
    [Theory]
    [InlineData("PLOT a;b ;comment", "PLOT a")]
    [InlineData("a;b\\;c", "a;b;c")]
    [InlineData("x\\;y ;note", "x;y")]
    [InlineData("  ;comment only", ";comment only")]
    [InlineData("OPEN file.sun   ", "OPEN file.sun")]
    public void Clean_FollowsCommentRules(string input, string expected)
    {
        Assert.Equal(expected, ScriptLexer.Clean(input));
    }

    [Theory]
    [InlineData("", Cmd.Comment)]
    [InlineData(";x", Cmd.Comment)]
    [InlineData("PLOT a", Cmd.Plot)]
    [InlineData("plot", Cmd.Plot)]
    [InlineData("64BITTIME ON", Cmd.Bit64Time)]
    [InlineData("SharedTime", Cmd.SharedTime)]
    [InlineData("PLO a", Cmd.Unknown)]
    [InlineData("plot\tx", Cmd.Unknown)]
    public void GetCmdKey_MatchesTable(string input, Cmd expected)
    {
        Assert.Equal(expected, ScriptKeywords.GetCmdKey(input));
    }

    [Fact]
    public void Keywords_TableMatchesEnum()
    {
        Assert.Equal((int)Cmd.Appearance + 1, ScriptKeywords.Words.Length);
        Assert.Equal("64bittime", ScriptKeywords.Words[(int)Cmd.Bit64Time]);
        Assert.Equal("savesun", ScriptKeywords.Words[(int)Cmd.SaveSun]);
    }

    [Fact]
    public void Reader_JoinsContinuationsAndSkipsInterlacedComments()
    {
        byte[] data = Encoding.ASCII.GetBytes("PLOT a,\r\n+ b ;c\r\n; comment\r\n& c\r\nEXIT\r\n");
        var r = new ScriptReader(data);
        var cmds = r.ReadAll().ToList();
        Assert.Equal(3, cmds.Count);
        Assert.Equal("PLOT a,  b  c", ScriptLexer.Clean(cmds[0].Command));
        Assert.Equal(4, cmds[0].LineNo);
        Assert.Equal("EXIT", ScriptLexer.Clean(cmds[1].Command));
        Assert.Equal(5, cmds[1].LineNo);
        Assert.Equal("", cmds[2].Command);
    }

    [Fact]
    public void Reader_LastLineWithoutNewline()
    {
        var r = new ScriptReader(Encoding.ASCII.GetBytes("A\nB"));
        var cmds = r.ReadAll().Select(c => ScriptLexer.Clean(c.Command)).ToList();
        Assert.Equal(new[] { "A", "B", "" }, cmds);
    }

    [Fact]
    public void Reader_ResumesAtByteOffset()
    {
        byte[] data = Encoding.ASCII.GetBytes("ONE\r\nTWO\r\nTHREE\r\n");
        var r = new ScriptReader(data);
        r.ReadCommand(out _);
        int pos = r.Position;
        var r2 = new ScriptReader(data, pos);
        Assert.Equal("TWO", ScriptLexer.Clean(r2.ReadCommand(out _)));
    }

    [Fact]
    public void ParmSetClean_RemovesSpacesAroundSeparators()
    {
        Assert.Equal("a,b,[c]", ScriptLexer.ParmSetClean("  a , b ,  [ c ]  "));
    }

    private sealed class Sys : ISystemSymbols
    {
        public string GetValue(string name) => name == "UTC_REFERENCE" ? "X" : "<" + name + ">";
    }

    [Fact]
    public void Substitute_UserAndSystemSymbols()
    {
        var t = new SymbolTable();
        t.Set("$foo = bar ;comment");
        Assert.Equal("bar", t.Get("FOO"));
        string s = "plot $FOO baz";
        Assert.True(t.Substitute(ref s, new Sys(), null));
        Assert.Equal("plot bar baz", s);

        string g = "$UTC_REFERENCE12rest";
        t.Substitute(ref g, new Sys(), null);
        Assert.Equal("Xrest", g); // consumes 16 characters (DEV-SCR-005)
    }

    [Fact]
    public void Substitute_UndefinedSymbolReportsAndMarks()
    {
        var t = new SymbolTable();
        var msgs = new List<string>();
        string s = "a $NOPE x";
        t.Substitute(ref s, new Sys(), msgs.Add);
        Assert.Equal("a !NOPE x", s);
        Assert.Equal("Undefined symbol: $NOPE", msgs.Single());
    }

    [Fact]
    public void Substitute_SelfReferenceStopsAtLimit()
    {
        var t = new SymbolTable();
        t.Set("A=$A");
        var msgs = new List<string>();
        string s = "$A";
        t.Substitute(ref s, new Sys(), msgs.Add);
        Assert.Contains("Symbol substitution limit exceeded", msgs);
    }

    [Fact]
    public void Symbols_SortedDescendingAndReplaced()
    {
        var t = new SymbolTable();
        t.Set("AB=1");
        t.Set("ABC=2");
        t.Set("ab=3");
        Assert.Equal(new[] { "ABC\t2", "AB\t3" }, t.Entries);
        string s = "$ABC $AB";
        t.Substitute(ref s, new Sys(), null);
        Assert.Equal("2 3", s);
    }

    [Fact]
    public void PreProcess_SkipsLiteralCommands()
    {
        string Sub(string x) => x.Replace("$X", "sub");
        ScriptLexer.PreProcess("PLOT $X", out var p, Sub);
        Assert.Equal("PLOT $X", p);
        ScriptLexer.PreProcess("clear $X", out p, Sub);
        Assert.Equal("clear $X", p);
        ScriptLexer.PreProcess("XTITLE $X", out p, Sub);
        Assert.Equal("XTITLE sub", p);
        ScriptLexer.PreProcess("INPUT a,$X,c,$SYM", out p, s => s.Replace("$X", "sub").Replace("$SYM", "bad"));
        Assert.Equal("INPUT a,sub,c,$SYM", p);
    }
}
