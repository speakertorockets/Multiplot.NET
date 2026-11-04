using System.Text;
using Multiplot.Core;

namespace Multiplot.Scripting;

/// <summary>Lexical rules of the script language (FR-SCR-010..014).</summary>
public static class ScriptLexer
{
    /// <summary>Maximum characters per physical line.</summary>
    public const int MaxLine = 16382;

    /// <summary>
    /// Comment stripping: repeatedly truncate at the last ';' while it is not at index 0 and not
    /// preceded by '\', right-trimming each time (FR-SCR-012). The text is not trimmed first.
    /// </summary>
    public static string StripComments(string cmd)
    {
        int nPos;
        while ((nPos = cmd.LastIndexOf(';')) > 0 && cmd[nPos - 1] != '\\')
            cmd = ScriptKeywords.TrimRightC(cmd[..nPos]);
        return cmd;
    }

    /// <summary>Command pre-processing without symbol substitution: trim, strip comments, unescape <c>\;</c>.</summary>
    public static string Clean(string command)
    {
        string cmd = StripComments(ScriptKeywords.TrimC(command));
        return cmd.Replace("\\;", ";");
    }

    /// <summary>
    /// Command pre-processing: <see cref="Clean"/> then symbol substitution unless the command uses symbol
    /// names literally (clear…, plot, yaxis, xaxis, set user…); INPUT with three commas keeps its 4th argument.
    /// Returns true when the line contained '$'.
    /// </summary>
    public static bool PreProcess(string command, out string cmdBuf, Func<string, string> substitute)
    {
        string buf = cmdBuf = Clean(command);
        if (buf.Length > 0 && buf[0] == ';' || buf.IndexOf('$') < 0) return false;
        bool Left(int n, string s) => string.Equals(buf.Length >= n ? buf[..n] : buf, s, StringComparison.OrdinalIgnoreCase);
        if (!Left(5, "clear") && !(Left(5, "plot ") || Left(6, "yaxis ") || Left(6, "xaxis ") || Left(8, "set user")))
        {
            if (Left(6, "input ") && cmdBuf.Count(c => c == ',') == 3)
            {
                int c = cmdBuf.LastIndexOf(',');
                string parm = cmdBuf[c..];
                cmdBuf = substitute(cmdBuf[..c]) + parm;
            }
            else
                cmdBuf = substitute(cmdBuf);
        }
        return true;
    }

    /// <summary>Cleans a parameter set (FR-SCR-130).</summary>
    public static string ParmSetClean(string parmSet)
    {
        string s = ScriptKeywords.TrimC(parmSet);
        string Rep(string x, string a, string b) { while (x.Contains(a)) x = x.Replace(a, b); return x; }
        s = Rep(s, " ,", ",");
        s = Rep(s, ", ", ",");
        s = Rep(s, "[ ", "[");
        s = Rep(s, " ]", "]");
        return s;
    }
}

/// <summary>
/// Byte-oriented script file reader with fixed line reading rules: <c>fgets</c> with
/// 16382 characters, look-ahead continuation lines (<c>+</c>/<c>&amp;</c>), interlaced comment lines, and
/// <c>ftell</c>-style byte offsets for pause/resume (FR-SCR-010/011).
/// </summary>
public sealed class ScriptReader
{
    private readonly byte[] _data;
    private int _pos;
    private bool _eof;

    /// <summary>Creates a reader over the file bytes starting at a byte offset.</summary>
    public ScriptReader(byte[] data, int startAt = 0)
    {
        _data = data;
        _pos = Math.Clamp(startAt, 0, data.Length);
    }

    /// <summary>Opens a file.</summary>
    public static ScriptReader Open(string path, int startAt = 0) => new(File.ReadAllBytes(path), startAt);

    /// <summary>Current byte offset.</summary>
    public int Position => _pos;

    /// <summary>Set after a read hit the end of data; cleared by a seek.</summary>
    public bool Eof => _eof;

    private string? Fgets()
    {
        if (_pos >= _data.Length) { _eof = true; return null; }
        int start = _pos;
        int max = ScriptLexer.MaxLine;
        while (_pos < _data.Length && _pos - start < max)
        {
            byte b = _data[_pos++];
            if (b == (byte)'\n') break;
        }
        if (_pos >= _data.Length && (_pos == start || _data[_pos - 1] != (byte)'\n')) _eof = true;
        return AnsiText.DecodeAnsi(_data.AsSpan(start, _pos - start));
    }

    private void Seek(int pos) { _pos = pos; _eof = false; }

    /// <summary>
    /// Reads the next command (with continuations). <paramref name="lines"/> receives the number of physical lines
    /// consumed. Returns the raw command text (may include CR/LF).
    /// </summary>
    public string ReadCommand(out int lines)
    {
        lines = 1;
        string cmdBuf = Fgets() ?? "";
        while (cmdBuf.Length > 0)
        {
            int cmdPos = _pos;
            string? next = Fgets();
            if (next == null) { Seek(cmdPos); break; }
            string add = ScriptKeywords.TrimC(next);
            char first = add.Length > 0 ? add[0] : '\0';
            if (first == '+' || first == '&')
            {
                cmdBuf = ScriptLexer.StripComments(cmdBuf);
                add = add[1..];
                lines++;
                cmdBuf = ScriptKeywords.TrimC(cmdBuf) + " " + add;
            }
            else if (first == ';')
                lines++;
            else
            {
                Seek(cmdPos);
                break;
            }
        }
        return cmdBuf;
    }

    /// <summary>Reads all commands (for tests): (command, line number after reading).</summary>
    public IEnumerable<(string Command, int LineNo)> ReadAll()
    {
        int lineNo = 0;
        while (true)
        {
            string c = ReadCommand(out int n);
            lineNo += n;
            yield return (c, lineNo);
            if (Eof) yield break;
        }
    }
}

/// <summary>Provider of system symbol values (FR-SCR-052).</summary>
public interface ISystemSymbols
{
    /// <summary>Returns the value of a system symbol (name upper-case, from the table), or null to use the table length rule only.</summary>
    string GetValue(string name);
}

/// <summary>User symbol table and substitution (<c>SymbolSubstitute</c>, FR-SCR-050..053).</summary>
public sealed class SymbolTable
{
    /// <summary>System symbols in test order with the number of characters consumed (including '$').</summary>
    public static readonly (string Name, int Length)[] SystemSymbols =
    {
        ("PRINT_APP", 10), ("VIEW_APP", 9), ("EXPORT_APP", 11), ("MAILSLOT_NAME", 14), ("SCRIPT_NAME", 12),
        ("SCRIPT_LINE", 12), ("CURRENT_FILE", 13), ("FILE_LIST", 10), ("FILE_REFERENCE", 15), ("EVENT_REFERENCE", 16),
        ("GMT_REFERENCE", 16), ("SCALE_XMIN", 11), ("SCALE_XMAX", 11), ("SCALE_YMIN", 11), ("SCALE_YMAX", 11),
        ("PLOT_X_EXTENTS", 15), ("PLOT_Y_EXTENTS", 15), ("PLOT_PARMS", 11), ("PLOT_SOURCE", 12), ("SYSTEM_TIME", 12),
        ("USERNAME", 9), ("USERFOLDER", 11), ("COMPUTER", 9), ("LAST_FILE_TIME", 15),
    };

    /// <summary>Maximum substitutions per line (DEV-SCR-004).</summary>
    public const int SubstitutionLimit = 1000;

    /// <summary>Entries "NAME\tvalue" sorted descending by name.</summary>
    public List<string> Entries { get; }

    /// <summary>Creates a table over an existing list (shared with settings).</summary>
    public SymbolTable(List<string>? entries = null) => Entries = entries ?? new List<string>();

    /// <summary>Name part of an entry.</summary>
    public static string NameOf(string entry) { int t = entry.IndexOf('\t'); return t < 0 ? entry : entry[..t]; }

    /// <summary>Value part of an entry.</summary>
    public static string ValueOf(string entry) { int t = entry.IndexOf('\t'); return t < 0 ? "" : entry[(t + 1)..]; }

    /// <summary>Descending by name (strcmp).</summary>
    public void Sort()
    {
        var e = Entries;
        for (int i = 0; i < e.Count - 1; i++)
            for (int j = i + 1; j < e.Count; j++)
                if (string.CompareOrdinal(NameOf(e[j]), NameOf(e[i])) > 0) (e[i], e[j]) = (e[j], e[i]);
    }

    /// <summary>
    /// <c>SET NAME=value</c>: name = text before '=', '$' removed, right-trimmed, upper-cased; value truncated
    /// at the first ';' and trimmed (DEV-SCR-008).
    /// </summary>
    public void Set(string assignment)
    {
        int i = assignment.IndexOf('=');
        string name = ScriptKeywords.TrimRightC(assignment[..i].Replace("$", "")).ToUpperInvariant();
        Remove(name);
        string value = assignment[(i + 1)..];
        int sc = value.IndexOf(';');
        if (sc >= 0) value = value[..sc];
        value = ScriptKeywords.TrimC(value);
        Entries.Add(name + "\t" + value);
        Sort();
    }

    /// <summary>Removes a symbol by name (case-insensitive); true if found.</summary>
    public bool Remove(string name)
    {
        for (int j = 0; j < Entries.Count; j++)
            if (string.Equals(name, NameOf(Entries[j]), StringComparison.OrdinalIgnoreCase)) { Entries.RemoveAt(j); return true; }
        return false;
    }

    /// <summary>Value of a user symbol or null.</summary>
    public string? Get(string name)
    {
        foreach (var e in Entries)
            if (string.Equals(name, NameOf(e), StringComparison.OrdinalIgnoreCase)) return ValueOf(e);
        return null;
    }

    /// <summary>
    /// Symbol substitution. <paramref name="report"/> receives "Undefined symbol: …" messages and the
    /// substitution-limit error. Returns true when the text contained '$'.
    /// </summary>
    public bool Substitute(ref string vString, ISystemSymbols system, Action<string>? report)
    {
        int tag = vString.IndexOf('$');
        if (tag < 0) return false;
        string sub = vString;
        int count = 0;
        do
        {
            if (++count > SubstitutionLimit)
            {
                report?.Invoke("Symbol substitution limit exceeded");
                break;
            }
            bool replaced = false;
            foreach (var entry in Entries)
            {
                string vName = NameOf(entry);
                int avail = Math.Max(0, Math.Min(vName.Length, sub.Length - tag - 1));
                string sBuf = ScriptKeywords.TrimRightC(sub.Substring(tag + 1, avail));
                if (string.Equals(sBuf, vName, StringComparison.OrdinalIgnoreCase))
                {
                    int after = Math.Min(sub.Length, tag + 1 + vName.Length);
                    sub = sub[..tag] + ValueOf(entry) + sub[after..];
                    replaced = true;
                    break;
                }
            }
            if (!replaced)
            {
                string rest = sub[(tag + 1)..].ToUpperInvariant();
                foreach (var (name, len) in SystemSymbols)
                {
                    if (rest.StartsWith(name, StringComparison.Ordinal))
                    {
                        string value = system.GetValue(name);
                        int after = Math.Min(sub.Length, tag + len);
                        sub = sub[..tag] + value + sub[after..];
                        replaced = true;
                        break;
                    }
                }
            }
            if (!replaced)
            {
                // Replace(' ', NUL) then "%s": the reported text ends at the first space
                string symbol = vString[vString.IndexOf('$')..];
                int sp = symbol.IndexOf(' ');
                if (sp >= 0) symbol = symbol[..sp];
                report?.Invoke("Undefined symbol: " + symbol);
                var sb = new StringBuilder(sub) { [tag] = '!' };
                sub = sb.ToString();
            }
        } while ((tag = sub.IndexOf('$')) != -1);
        vString = sub;
        return true;
    }
}
