// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

using Multiplot.Core;
using Multiplot.Core.Model;
using Multiplot.Plugins;

namespace Multiplot.Scripting;

/// <summary>One fixed-scale specification of FIXX/FIXY.</summary>
public readonly record struct FixRange(double Min, double Max, double Grad, bool Fixed, bool Persist);

/// <summary>Parsed FIXX/FIXY argument (FR-SCR-171).</summary>
public sealed class FixRequest
{
    /// <summary>Re-enable autoscale.</summary>
    public bool Auto;
    /// <summary>FIXY MULTI form: one range per Y axis.</summary>
    public bool Multi;
    /// <summary>Ranges (single form: one entry).</summary>
    public List<FixRange> Ranges = new();
    /// <summary>Syntax error.</summary>
    public bool Error;
}

/// <summary>Parsed IMPORT arguments (FR-SCR-150).</summary>
public sealed record ImportRequest(string Source, string? Destination, int IndexColumn, int SkipLines, int TitleRecord, bool GenerateTimeTags, bool StandardFormat, string? Reference);

/// <summary>Parsed INPUT arguments (FR-SCR-105).</summary>
public sealed record InputRequest(string Prompt, string Default, string Caption, string Symbol);

/// <summary>A TIME triplet, optionally for a named group (FR-SCR-131).</summary>
public sealed record TimeTriplet(string Start, string Stop, string Ref, string? GroupName);

/// <summary>Table-driven parsers of the command arguments (pure functions, NFR-SCR-002).</summary>
public static class CommandParsers
{
    private static char At(string s, int i) => i >= 0 && i < s.Length ? s[i] : '\0';

    /// <summary>
    /// Nomenclature parsing (FR-SCR-230): the (upper-cased) item list sets lengths in <paramref name="nomLen"/>,
    /// the order in <paramref name="nomOrder"/>, and returns the selected item flags in <paramref name="setNomen"/>.
    /// Returns the number of length errors (message in <paramref name="errTxt"/>).
    /// </summary>
    public static int ParseNomen(string cmdParm, bool yAxis, int[] nomLen, int[] nomOrder, out int setNomen, ref string errTxt)
    {
        const int Invalid = 8192;
        int[] item = Enumerable.Repeat(Invalid, Nomen.ITEMS).ToArray();
        int errors = 0, iPos;
        setNomen = 0;
        string p = cmdParm.Replace(" :", ":").Replace(": ", ":") + "  ";
        int AtoiFrom(int i) => i < p.Length ? Crt.Atoi(p[i..]) : 0;

        if ((iPos = p.IndexOf("DBNAME", StringComparison.Ordinal)) >= 0)
        {
            setNomen |= Nomen.DBNAME; item[0] = iPos;
            if (At(p, iPos + 6) == ':') nomLen[0] = AtoiFrom(iPos + 7);
        }
        if ((iPos = p.IndexOf("FILE", StringComparison.Ordinal)) >= 0 || (iPos = p.IndexOf("FILENAME", StringComparison.Ordinal)) >= 0)
        {
            setNomen |= Nomen.FILENAME; item[1] = iPos;
            if (At(p, iPos + 4) == ':') nomLen[1] = AtoiFrom(iPos + 5);
            else if (At(p, iPos + 8) == ':') nomLen[1] = AtoiFrom(iPos + 9);
        }
        if ((iPos = p.IndexOf(" NAME", StringComparison.Ordinal)) >= 0 || (iPos = p.IndexOf("+NAME", StringComparison.Ordinal)) >= 0
            || (iPos = p.IndexOf("NAME", StringComparison.Ordinal)) == 0)
        {
            setNomen |= Nomen.NAME; item[2] = iPos;
            if (At(p, iPos + 4) == ':' || At(p, ++iPos + 4) == ':') nomLen[2] = AtoiFrom(iPos + 5);
        }
        if ((iPos = p.IndexOf("UNITS", StringComparison.Ordinal)) >= 0)
        {
            setNomen |= Nomen.UNITS; item[3] = iPos;
            if (At(p, iPos + 5) == ':') nomLen[3] = AtoiFrom(iPos + 6);
        }
        if ((iPos = p.IndexOf("DESC", StringComparison.Ordinal)) >= 0)
        {
            setNomen |= Nomen.DESC;
            if (p.IndexOf("DESCRIPTION:", StringComparison.Ordinal) >= 0) iPos += 7;
            item[4] = iPos;
            if (At(p, iPos + 4) == ':') nomLen[4] = AtoiFrom(iPos + 5);
        }
        if ((iPos = p.IndexOf("REFT", StringComparison.Ordinal)) >= 0)
        {
            setNomen |= Nomen.REFT; item[5] = iPos;
            if (At(p, iPos + 4) == ':') nomLen[5] = AtoiFrom(iPos + 5);
        }
        if ((iPos = p.IndexOf("UDEF", StringComparison.Ordinal)) >= 0)
        {
            setNomen |= Nomen.UDEF; item[6] = iPos;
            if (At(p, iPos + 4) == ':') nomLen[6] = AtoiFrom(iPos + 5);
        }
        if ((iPos = p.IndexOf("ALTNAME", StringComparison.Ordinal)) >= 0)
        {
            setNomen |= Nomen.ALTNAME; item[7] = iPos;
            if (At(p, iPos + 7) == ':') nomLen[7] = AtoiFrom(iPos + 8);
        }
        if ((iPos = p.IndexOf("EXTNAME", StringComparison.Ordinal)) >= 0)
        {
            setNomen |= Nomen.EXTNAME; item[8] = iPos;
            if (At(p, iPos + 7) == ':') nomLen[8] = AtoiFrom(iPos + 8);
        }
        if (yAxis)
        {
            if ((iPos = p.IndexOf("SEQNUM", StringComparison.Ordinal)) >= 0)
            {
                setNomen |= Nomen.SEQNUM; item[9] = iPos;
                if (At(p, iPos + 6) == ':') nomLen[9] = AtoiFrom(iPos + 7);
            }
            if ((iPos = p.IndexOf("CURRENTVALUE", StringComparison.Ordinal)) >= 0)
            {
                setNomen |= Nomen.CURVAL; item[10] = iPos;
                if (At(p, iPos + 12) == ':') nomLen[10] = AtoiFrom(iPos + 13);
            }
            if ((iPos = p.IndexOf("CURRENTTIME", StringComparison.Ordinal)) >= 0)
            {
                setNomen |= Nomen.CURTIME; item[11] = iPos;
                if (At(p, iPos + 11) == ':') nomLen[11] = AtoiFrom(iPos + 12);
            }
        }

        for (iPos = 0; iPos < Nomen.ITEMS; iPos++)
        {
            if (nomLen[iPos] < 0 || nomLen[iPos] > 256)
            {
                errTxt = Crt.Sprintf("invalid length found (%d)", nomLen[iPos]);
                nomLen[iPos] = 0;
                errors++;
            }
        }

        int[] defOrd = Nomen.DefaultOrder;
        Array.Copy(defOrd, nomOrder, Nomen.ITEMS);
        bool assigned;
        int orderPos = 0;
        do
        {
            assigned = false;
            int priority = 0;
            for (int i = 0; i < Nomen.ITEMS; i++)
                if (item[i] != Invalid && item[i] <= item[priority]) priority = i;
            if (item[priority] != Invalid)
            {
                assigned = true;
                if (nomOrder[orderPos] != defOrd[priority])
                {
                    int save = nomOrder[orderPos];
                    nomOrder[orderPos++] = defOrd[priority];
                    for (int j = orderPos; j < Nomen.ITEMS; j++)
                        if (nomOrder[j] == defOrd[priority]) { nomOrder[j] = save; break; }
                }
                else orderPos++;
                item[priority] = Invalid;
            }
        } while (assigned);
        return errors;
    }

    /// <summary>FIXX/FIXY argument parsing (FR-SCR-171).</summary>
    public static FixRequest ParseFix(string cmdParm, bool isY)
    {
        var r = new FixRequest();
        string p = cmdParm.ToLowerInvariant();
        if (isY && p.StartsWith("multi", StringComparison.Ordinal))
        {
            r.Multi = true;
            bool persist = p.IndexOf('p') > 0;
            var tok = new Strtok(p);
            tok.Next(" ");
            string? t;
            double low = 0, high = 0, inc;
            while ((t = tok.Next(",")) != null)
            {
                low = char.ToLowerInvariant(t[0]) == 'a' ? DataTypes.FpDropOut : Crt.Atof(t);
                if ((t = tok.Next(",")) != null) high = char.ToLowerInvariant(t[0]) == 'a' ? DataTypes.FpDropOut : Crt.Atof(t);
                if ((t = tok.Next(",")) != null)
                {
                    bool fixedScale;
                    if (t[0] == 'a') { inc = -1; fixedScale = false; }
                    else { inc = Math.Abs(Crt.Atof(t)); fixedScale = true; }
                    r.Ranges.Add(new FixRange(low, high, inc, fixedScale, persist));
                }
                else break;
            }
            if (r.Ranges.Count == 0) r.Error = true;
            return r;
        }
        if (p.StartsWith("auto", StringComparison.Ordinal))
        {
            r.Auto = true;
            return r;
        }
        int c1 = p.IndexOf(',');
        if (c1 < 0) { r.Error = true; return r; }
        double lo = Crt.Atof(p), hi = Crt.Atof(p[(c1 + 1)..]);
        if (lo > hi) (lo, hi) = (hi, lo);
        double increment = -1;
        bool isFixed = false, keep = false;
        int c2 = p.IndexOf(',', c1 + 1);
        if (c2 >= 0)
        {
            string third = p[(c2 + 1)..];
            if (At(third, 0) != 'p') { increment = Math.Abs(Crt.Atof(third)); isFixed = true; }
            else isFixed = false;
            if (third.Contains('p')) keep = true;
        }
        r.Ranges.Add(new FixRange(lo, hi, increment, isFixed, keep));
        return r;
    }

    /// <summary>
    /// TIME triplet loop (FR-SCR-131); a group-prefixed start stops parsing. Fields that a triplet does not
    /// supply keep the previous global value (<paramref name="prevStop"/>, <paramref name="prevRef"/>).
    /// </summary>
    public static List<TimeTriplet> ParseTime(string cmdParm, string prevStop, string prevRef)
    {
        var list = new List<TimeTriplet>();
        int first = 0, last;
        string tstart, tstop = prevStop, tref = prevRef;
        while ((last = cmdParm.IndexOf(',', first)) > 0)
        {
            tstart = ScriptKeywords.TrimC(cmdParm[first..last]);
            first = ++last;
            if ((last = cmdParm.IndexOf(',', first)) > 0)
            {
                tstop = cmdParm[first..last];
                first = ++last;
                if ((last = cmdParm.IndexOf(',', first)) > 0)
                {
                    tref = cmdParm[first..last];
                    first = ++last;
                }
                else tref = cmdParm[first..];
                tref = ScriptKeywords.TrimC(tref);
            }
            else tstop = cmdParm[first..];
            tstop = ScriptKeywords.TrimC(tstop);

            int db = tstart.IndexOf(':');
            if (db != -1)
            {
                list.Add(new TimeTriplet(tstart[(db + 1)..], tstop, tref, tstart[..db]));
                break;
            }
            list.Add(new TimeTriplet(tstart, tstop, tref, null));
        }
        return list;
    }

    /// <summary>IMPORT argument parsing (FR-SCR-150).</summary>
    public static ImportRequest? ParseImport(string cmdParm)
    {
        int iIdx = 1, iSkp = 0, iTrc = 0, i;
        bool gtt = false, std = false;
        string? strRef = null;
        if ((i = cmdParm.IndexOf('/')) != -1)
        {
            string ops = cmdParm[i..].ToLowerInvariant();
            cmdParm = ScriptKeywords.TrimRightC(cmdParm[..i]);
            if ((i = ops.IndexOf("/i", StringComparison.Ordinal)) != -1) iIdx = Crt.Atoi(ops[(i + 2)..]);
            if ((i = ops.IndexOf("/s", StringComparison.Ordinal)) != -1) iSkp = Crt.Atoi(ops[(i + 2)..]);
            if ((i = ops.IndexOf("/t", StringComparison.Ordinal)) != -1) iTrc = Crt.Atoi(ops[(i + 2)..]);
            if (ops.Contains("/g")) gtt = true;
            if (ops.Contains("/f")) std = true;
            if ((i = ops.IndexOf("/r", StringComparison.Ordinal)) != -1)
            {
                string r = ops[(i + 2)..];
                int sl = r.IndexOf('/');
                if (sl != -1) r = r[..sl];
                strRef = ScriptKeywords.TrimC(r);
                if (strRef.Length == 0) strRef = null;
            }
        }
        string? dst = null;
        if ((i = cmdParm.IndexOf(',')) > 0 || (i = cmdParm.IndexOf("\" ", StringComparison.Ordinal)) > 0
            || (i = cmdParm.IndexOf(" \"", StringComparison.Ordinal)) > 0 || (cmdParm.IndexOf('"') == -1 && (i = cmdParm.IndexOf(' ')) > 0))
        {
            dst = ScriptKeywords.TrimC(cmdParm[(i + 1)..].Replace("\"", ""));
            cmdParm = cmdParm[..i];
            if (dst.Length == 0) dst = null;
        }
        string src = ScriptKeywords.TrimC(cmdParm.Replace("\"", ""));
        if (src.Length == 0) return null;
        return new ImportRequest(src, dst, iIdx, iSkp, iTrc, gtt, std, strRef);
    }

    /// <summary>INPUT argument parsing (FR-SCR-105).</summary>
    public static InputRequest ParseInput(string cmdParm)
    {
        string prompt, def = "", caption = "", symbolic = "";
        int i;
        if ((i = cmdParm.IndexOf('$')) != -1)
        {
            symbolic = cmdParm[(i + 1)..];
            cmdParm = ScriptKeywords.TrimRightC(cmdParm[..i]).TrimEnd(',');
        }
        if (symbolic.Length == 0) symbolic = "USER_INPUT";
        if ((i = cmdParm.IndexOf(',')) > 0)
        {
            prompt = cmdParm[..i];
            def = ScriptKeywords.TrimLeftC(cmdParm[(i + 1)..]);
            if ((i = def.IndexOf(',')) > 0)
            {
                caption = ScriptKeywords.TrimLeftC(def[(i + 1)..]);
                def = def[..i];
            }
        }
        else prompt = cmdParm;
        return new InputRequest(prompt, def, caption, symbolic);
    }

    /// <summary>
    /// <c>SET TRACE</c> (FR-SCR-251) applied to <paramref name="c"/>; returns false when the trace number is invalid.
    /// </summary>
    public static bool ApplySetTrace(string setCmd, IReadOnlyList<Curve> curves, out int traceIndex)
    {
        traceIndex = -1;
        bool form2 = setCmd.Contains('=');
        var tok = new Strtok(setCmd);
        tok.Next(" ");
        string? p = tok.Next(",");
        if (p == null) return false;
        int t = Crt.Atoi(p);
        if (t < 1 || t > curves.Count) return false;
        traceIndex = t - 1;
        var c = curves[traceIndex];
        int v;
        bool In(int x, int lo, int hi) => x >= lo && x <= hi;
        if (!form2)
        {
            if ((p = tok.Next(",")) != null && In(v = Crt.Atoi(p), 1, 3)) c.LineSize = v;
            if ((p = tok.Next(",")) != null && In(v = Crt.Atoi(p), 1, 5)) c.LineStyle = v - 1;
            if ((p = tok.Next(",")) != null && In(v = Crt.Atoi(p), 0, 1)) c.UseSymbols = v != 0;
            if ((p = tok.Next(",")) != null && In(v = Crt.Atoi(p), 1, ColorIndex.MaxColors)) c.SymbolId = v - 1;
            if ((p = tok.Next(",")) != null && In(v = Crt.Atoi(p), 0, 1)) c.ScatterPlot = v != 0;
            if ((p = tok.Next(",")) != null && In(v = Crt.Atoi(p), 1, ColorIndex.MaxColors)) c.ColorIndex = v - 1;
            if ((p = tok.Next(",")) != null && In(v = Crt.Atoi(p), 0, 1)) c.TabLegend = v != 0;
            if ((p = tok.Next(",")) != null && In(v = Crt.Atoi(p), TabFormat.Hex2, TabFormat.FixedPoint + 7)) c.TabFormat = v;
            if ((p = tok.Next(",")) != null && In(v = Crt.Atoi(p), 0, 3)) c.LogicTxt = v;
            return true;
        }
        string s = setCmd.ToUpperInvariant().Replace(" ", "");
        int eq;
        int ValAt(int off) => Crt.Atoi(off < s.Length ? s[off..] : "");
        if ((eq = s.IndexOf("LINE_SIZE=", StringComparison.Ordinal)) != -1 && In(v = ValAt(eq + 10), 1, 3)) c.LineSize = v;
        if ((eq = s.IndexOf("LINE_STYLE=", StringComparison.Ordinal)) != -1 && In(v = ValAt(eq + 11), 1, 5)) c.LineStyle = v - 1;
        if ((eq = s.IndexOf("USE_SYMBOLS=", StringComparison.Ordinal)) != -1 && In(v = ValAt(eq + 12), 0, 1)) c.UseSymbols = v != 0;
        if ((eq = s.IndexOf("SYMBOL_ID=", StringComparison.Ordinal)) != -1 && In(v = ValAt(eq + 10), 1, ColorIndex.MaxColors)) c.SymbolId = v - 1;
        if ((eq = s.IndexOf("SCATTER_PLOT=", StringComparison.Ordinal)) != -1 && In(v = ValAt(eq + 13), 0, 1)) c.ScatterPlot = v != 0;
        if ((eq = s.IndexOf("COLOR_INDEX=", StringComparison.Ordinal)) != -1 && In(v = ValAt(eq + 12), 1, ColorIndex.MaxColors)) c.ColorIndex = v - 1;
        if ((eq = s.IndexOf("TAB_LEGEND=", StringComparison.Ordinal)) != -1 && In(v = ValAt(eq + 11), 0, 1)) c.TabLegend = v != 0;
        if ((eq = s.IndexOf("TAB_FORMAT=", StringComparison.Ordinal)) != -1)
        {
            string f = s[(eq + 11)..];
            if (f.Contains("HEX2")) c.TabFormat = TabFormat.Hex2;
            else if (f.Contains("HEX4")) c.TabFormat = TabFormat.Hex4;
            else if (f.Contains("HEX8")) c.TabFormat = TabFormat.Hex8;
            else if (f.Contains("OCT3")) c.TabFormat = TabFormat.Oct3;
            else if (f.Contains("OCT6")) c.TabFormat = TabFormat.Oct6;
            else if (f.Contains("OCT12")) c.TabFormat = TabFormat.Oct12;
            else if (f.Contains("GENERAL")) c.TabFormat = TabFormat.General;
            else if (f.Contains("DEC"))
                c.TabFormat = In(v = ValAt(eq + 14), TabFormat.FixedPoint, TabFormat.FixedPoint + 7) ? v : TabFormat.General;
        }
        if ((eq = s.IndexOf("LOGIC_INDEX=", StringComparison.Ordinal)) != -1 && In(v = ValAt(eq + 12), 1, AppSettings.NLogicLabels)) c.LogicTxt = v - 1;
        return true;
    }

    /// <summary>Returns (time, data) precision (null = unchanged).</summary>
    public static (int? Time, int? Data) ParsePrecision(string arg)
    {
        string s = ScriptKeywords.TrimC(arg.ToUpperInvariant());
        int i = s.IndexOfAny(new[] { ',', ' ' });
        if (i <= 0) return (null, null);
        int? Lookup(string t)
        {
            for (int j = 0; j < 15; j++) if (t == TabFormat.Names[j]) return j - 7;
            return null;
        }
        return (Lookup(s[..i]), Lookup(ScriptKeywords.TrimLeftC(s[(i + 1)..])));
    }

    /// <summary>
    /// <c>SET COLOR n|GRID|REDLINE|MARKER|LABEL|TITLE|BACKGROUND,r,g,b [GLOBAL]</c> target/value parsing.
    /// Returns false on syntax error.
    /// </summary>
    public static bool ParseSetColor(string arg, out int colorNo, out int rgb, out bool global)
    {
        string s = arg.ToUpperInvariant();
        colorNo = -1; rgb = 0; global = s.Contains("GLOBAL");
        if (s.Contains("GRID")) colorNo = ColorIndex.Grid;
        else if (s.Contains("REDLINE")) colorNo = ColorIndex.Redline;
        else if (s.Contains("MARKER")) colorNo = ColorIndex.Marker;
        else if (s.Contains("LABEL")) colorNo = ColorIndex.Label;
        else if (s.Contains("TITLE")) colorNo = ColorIndex.Title;
        else if (s.Contains("BACKGROUND")) colorNo = ColorIndex.Background;
        else if ((colorNo = Crt.Atoi(s) - 1) >= 0 && colorNo < ColorIndex.MaxColors) { }
        else return false;
        int c1 = s.IndexOf(',');
        if (c1 < 0) return false;
        int r = Crt.Atoi(s[(c1 + 1)..]) % 256;
        int c2 = s.IndexOf(',', c1 + 1);
        if (c2 < 0) return false;
        int g = Crt.Atoi(s[(c2 + 1)..]) % 256;
        int c3 = s.IndexOf(',', c2 + 1);
        if (c3 < 0) return false;
        int b = Crt.Atoi(s[(c3 + 1)..]) % 256;
        rgb = (r & 0xff) | ((g & 0xff) << 8) | ((b & 0xff) << 16);
        return true;
    }

    /// <summary>File name validation.</summary>
    public static string ValidateFileName(string name, string exportDir, int plotNo, string ext)
    {
        if (name.Length == 0) return Crt.Sprintf("%s\\plot%04d.%s", exportDir, plotNo, ext);
        if (name.IndexOf('\\') == -1 && Directory.Exists(exportDir)) return exportDir + "\\" + name;
        return name;
    }

    /// <summary>User UTC format tokens to a C format.</summary>
    public static string MakeCFormat(string user) => Core.Plotting.Scale.MakeCFormat(user);
}
