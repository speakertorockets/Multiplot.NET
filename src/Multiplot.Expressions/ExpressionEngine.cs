using Multiplot.Core;
using Multiplot.Core.Model;
using Multiplot.Plugins;
using static Multiplot.Core.ExprSyntax;

namespace Multiplot.Expressions;

/// <summary>
/// Expression evaluator and function library, chapter 06.
/// One instance per load operation (holds the "ignore further prompts" flags).
/// </summary>
public sealed class ExpressionEngine
{
    private const int MaxExpIds = 128;
    private const int EStackSize = 512;
    private const double Filtered = +1.7E308;
    private const double ExprInvalid = -1.7E308;

    private readonly IExpressionContext _ctx;

    /// <summary>Divide-by-zero prompt suppressed for this load.</summary>
    public bool IgnoreDbz;
    /// <summary>Negative power prompt suppressed.</summary>
    public bool IgnoreNpow;
    /// <summary>Filter prompt suppressed.</summary>
    public bool IgnoreFilt;
    /// <summary>Reference error prompt enabled.</summary>
    public bool ErrPrompt = true;

    public ExpressionEngine(IExpressionContext ctx) => _ctx = ctx;

    private float DropOut => DataTypes.FpDropOut;

    private static bool IsDrop(float f) => BitConverter.SingleToUInt32Bits(f) == DataTypes.DropOutBits;

    private string GroupName(int g) => g >= 0 && g < _ctx.Groups.Count ? _ctx.Groups[g].GroupName : "";

    private void Message(string text, PromptIcon icon = PromptIcon.Information) =>
        _ctx.Ui.Show(text, "Multiplot", PromptButtons.Ok, icon, PromptResult.Ok);

    private static void ResetNomen(ParameterData p, string? name, string? units, string? descr)
    {
        if (name != null) p.Name = name;
        if (units != null) p.Units = units;
        if (descr != null) p.Description = descr;
    }

    private static int FindParameter(string name, IDataFile f) => f.FindParameter(name, out _);

    private ParameterData? Read(IDataFile f, string name, double[] slice, double? refTime, TimeTag? refGmt) =>
        _ctx.Read(f, name, slice[0], slice[1], refTime, refGmt, _ctx.Use64BitTime);

    private static float FileRate(IDataFile? f) => f is ITimeIndexedFile ti ? ti.FileRate : -1f;

    private static bool IsCommonTimeSig(IDataFile? f) => f != null && SunSignatures.IsCommonTime(f.Info.FileSig);

    // ------------------------------------------------------------------ evaluation primitives

    private struct Op
    {
        public char C0, C1;
        public int Rank;
    }

    /// <summary>Text to float.</summary>
    private static double Xtof(string s)
    {
        double dVal = 0;
        if (s.Length > 2)
        {
            int xEnd = 2;
            while (xEnd < s.Length && "0123456789abcdefABCDEF".IndexOf(s[xEnd]) >= 0) xEnd++;
            int nib = 0;
            for (int i = xEnd - 1; i > 1; i--)
            {
                int b = char.IsAsciiDigit(s[i]) ? s[i] - '0' : (s[i] & 0x1f) + 9;
                dVal += b * Math.Pow(2.0, nib);
                nib += 4;
            }
        }
        return dVal;
    }

    /// <summary>
    /// Builds the operator stack: returns the operator count (≥ 0) or a negative error. Fills constants,
    /// operators and the value source list (true = parameter).
    /// </summary>
    private static int BuildStack(string expression, List<double> cStack, List<Op> oStack, List<bool> sStack)
    {
        char[] buf = (expression + "\0").ToCharArray();
        int e = 0;
        int opLevel = 0;
        int error = 0;
        int Len(int from) { int k = from; while (buf[k] != '\0') k++; return k - from; }
        string Str(int from) => new string(buf, from, Len(from));

        while (Len(e) > 0 && error == 0)
        {
            while (buf[e] == ' ') e++;
            while (buf[e] == '(')
            {
                opLevel++;
                e++;
                while (buf[e] == ' ') e++;
            }
            if (buf[e] == '[')
                sStack.Add(true);
            else
            {
                sStack.Add(false);
                string rest = Str(e);
                if (ValidateConst(rest) == 0)
                    cStack.Add(rest.Length > 1 && (rest[1] == 'x' || rest[1] == 'X') ? Xtof(rest) : Crt.Atof(rest));
                else
                    error = -1;
            }
            int opRel = FindOperator(Str(e));
            if (opRel >= 0)
            {
                int oP = e + opRel;
                for (int t = oP - 1; t > e; t--)
                {
                    if (buf[t] == ')')
                    {
                        if (opLevel != 0) opLevel--;
                        else { error = -2; break; }
                    }
                }
                int rank = 14 - MathOps.IndexOf(buf[oP]);
                if (rank == 9) rank++;
                if (rank == 12) rank++;
                oStack.Add(new Op { C0 = buf[oP], C1 = buf[oP + 1], Rank = rank + (14 * opLevel) });
                if (buf[oP] == '~')
                {
                    buf[oP] = '0';
                    e = oP;
                }
                else
                {
                    if ("<>&|=".IndexOf(buf[oP]) >= 0 && buf[oP + 1] != '\0' && "<>&|=".IndexOf(buf[oP + 1]) >= 0) oP++;
                    e = oP + 1;
                }
                if (oStack.Count >= EStackSize || cStack.Count >= EStackSize)
                {
                    error = -3;
                    break;
                }
            }
            else
            {
                int sp = Array.IndexOf(buf, ' ', e);
                if (sp >= 0 && sp < e + Len(e))
                {
                    int t = sp;
                    while (buf[t] == ' ') t++;
                    if (Len(t) > 0 && opLevel <= 0 && buf[t] != ')') error = -4;
                }
                break;
            }
        }
        return error != 0 ? error : oStack.Count;
    }

    /// <summary>Applies an operator to values[i], values[i+1].</summary>
    private static int DoOperation(double[] w, int i, Op op, List<double> deltaStore, ref int deltaPtr)
    {
        double v1 = w[i], v2 = w[i + 1];
        int status = 0;
        double r;
        switch (op.C0)
        {
            case '?': r = v1 != 0 ? v2 : ExprInvalid; break;
            case '#': r = v1 != ExprInvalid ? v1 : v2; break;
            case '^':
                if (v1 < 0.0 && v2 != (double)(int)v2)
                {
                    w[i] = 0.0;
                    return -2;
                }
                r = Math.Pow(v1, v2);
                break;
            case '*': r = v1 * v2; break;
            case '/':
                if (v2 != 0.0) r = v1 / v2;
                else { r = 0.0; status = -1; }
                break;
            case '%': r = v1 % v2; break;
            case '+': r = v1 + v2; break;
            case '-': r = v1 - v2; break;
            case '~':
                {
                    while (deltaStore.Count <= deltaPtr) deltaStore.Add(0.0);
                    double last = deltaStore[deltaPtr];
                    deltaStore[deltaPtr++] = v1;
                    r = v1 - last;
                    break;
                }
            case '&': r = op.C1 == '&' ? (U(v1) != 0 && U(v2) != 0 ? 1.0 : 0.0) : (double)(U(v1) & U(v2)); break;
            case '|': r = op.C1 == '|' ? (U(v1) != 0 || U(v2) != 0 ? 1.0 : 0.0) : (double)(U(v1) | U(v2)); break;
            case '=': r = op.C1 == '=' ? (Math.Abs(v1 - v2) < .0001 ? 1.0 : 0.0) : (CInt(v1) != 0 ? v2 : Filtered); break;
            case '<':
                r = op.C1 switch
                {
                    '=' => v1 <= v2 ? 1.0 : 0.0,
                    '<' => (double)(U(v1) << (int)(U(v2) & 31)),
                    '>' => v1 != v2 ? 1.0 : 0.0,
                    '|' => v1 < v2 ? v1 : v2,
                    _ => v1 < v2 ? 1.0 : 0.0,
                };
                break;
            case '>':
                r = op.C1 switch
                {
                    '=' => v1 >= v2 ? 1.0 : 0.0,
                    '>' => (double)(U(v1) >> (int)(U(v2) & 31)),
                    '|' => v1 > v2 ? v1 : v2,
                    _ => v1 > v2 ? 1.0 : 0.0,
                };
                break;
            default:
                return 1;
        }
        w[i] = r;
        return status;
    }

    /// <summary>C <c>(unsigned long)double</c> as decided in DEV-EXP-002: via int64, NaN/∞ → 0.</summary>
    private static uint U(double v) => double.IsNaN(v) || double.IsInfinity(v) ? 0u : unchecked((uint)(long)Math.Truncate(Math.Clamp(v, -9.2e18, 9.2e18)));

    private static int CInt(double v) => double.IsNaN(v) || double.IsInfinity(v) ? int.MinValue : (v >= 2147483648.0 || v <= -2147483649.0 ? int.MinValue : (int)v);

    // ------------------------------------------------------------------ parse()

    /// <summary>
    /// Evaluates an expression for file <paramref name="currentFile"/> of a group.
    /// Returns null when no curve results; <paramref name="errMsg"/> may be set to "!abort"/"!break" prefixes.
    /// </summary>
    public ParameterData? Parse(IReadOnlyList<IDataFile> pFile, int currentFile, int fileGroup, string exp, double[] slice, string tref, ref string errMsg)
    {
        int status = CheckEnclosures(exp);
        if (status != 0)
        {
            if (!_ctx.ScriptRunning || _ctx.ScriptPrompt)
            {
                errMsg = status switch
                {
                    1 => "Open bracket missing close",
                    2 => "Close bracket missing open",
                    10 => "Open parenthesis missing close",
                    11 => "Invalid combination of ( and [",
                    12 => "Invalid combination of ( and ]",
                    20 => "Close parenthesis missing open",
                    21 => "Invalid combination of ) and [",
                    22 => "Invalid combination of ) and ]",
                    _ => "Invalid syntax for function or grouping operation",
                };
                errMsg += "\n" + exp;
                Message(errMsg);
                errMsg = "!abort";
            }
            return null;
        }

        status = 0;
        string expression = exp;
        string? expDesc = null;
        int q = expression.IndexOf('"');
        if (q >= 0)
        {
            expDesc = expression[(q + 1)..];
            int q2 = expDesc.IndexOf('"');
            if (q2 >= 0) expDesc = expDesc[..q2];
            expression = expression[..q];
        }

        var exprPid = new List<string>();
        var pid = new List<ParameterData?>();
        ParameterData? calculated = null;

        if (CheckEnclosures(expression) == 0)
        {
            // extract bracketed names
            int p1 = 0;
            while ((p1 = expression.IndexOf('[', p1)) >= 0)
            {
                int start = ++p1;
                if (exprPid.Count + 1 >= MaxExpIds)
                {
                    status = 3;
                    Message("Too many parameters in expression");
                    errMsg = "!abort";
                    break;
                }
                int end = -1;
                if (IsFunction(expression[start..]) != Fn.None)
                {
                    int openBrak = 0;
                    int o = start;
                    while (o < expression.Length)
                    {
                        if (expression[o] == '[') openBrak++;
                        o++;
                        if (o < expression.Length && expression[o] == ']')
                        {
                            if (openBrak != 0) openBrak--;
                            else { end = o; break; }
                        }
                    }
                }
                else
                    end = expression.IndexOf(']', start);
                if (end < 0)
                {
                    status = 2;
                    Message("Mismatched brackets in Expression");
                    break;
                }
                exprPid.Add(expression[start..end]);
                p1 = end + 1;
            }
            for (int k = 0; k < exprPid.Count; k++) pid.Add(null);
            int nExpr = exprPid.Count;

            if (status == 0 && nExpr != 0)
            {
                bool skipFile = true;
                string pBuf = exprPid[0].Length > 255 ? exprPid[0][..255] : exprPid[0];
                string pName;
                if (IsFunction(pBuf) != Fn.None)
                {
                    string work = pBuf;
                    if (work.StartsWith("fn(", StringComparison.OrdinalIgnoreCase) || work.StartsWith("refprop(", StringComparison.OrdinalIgnoreCase))
                    {
                        var toks = work.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        int idx = char.ToUpperInvariant(work[0]) == 'R' ? 3 : 2;
                        string tmp = toks.Length > idx ? toks[idx] : "";
                        if (tmp.Length > 0) work = "(" + tmp;
                    }
                    int ps = work.IndexOf('(') + 1;
                    int openParen = 0, openTick = 0, k = ps;
                    for (; k < work.Length; k++)
                    {
                        char c = work[k];
                        if (c == '(') openParen++;
                        else if (c == '\'') openTick++;
                        if (c == ' ' || c == ')')
                        {
                            if (openParen > 0) openParen--;
                            else if (openTick > 0) openTick--;
                            else break;
                        }
                    }
                    pName = work[ps..k];
                }
                else
                    pName = pBuf;

                if ((pName.Length > 0 && (pName[0] == '[' || pName[0] == '(')) || IsExpression(pName))
                    skipFile = false;
                else
                {
                    int colon = pName.LastIndexOf(':');
                    if (colon >= 0)
                    {
                        string s1 = pFile[currentFile].Info.Path.ToLowerInvariant();
                        string s2 = pName[..colon].ToLowerInvariant();
                        string s3 = fileGroup != -1 ? GroupName(fileGroup).ToLowerInvariant() : " ";
                        if ((s1.Contains(s2) || s3.Contains(s2)) && FindParameter(pName[(colon + 1)..], pFile[currentFile]) >= 0)
                            pName = pName[(colon + 1)..];
                    }
                    if (FindParameter(pName, pFile[currentFile]) >= 0)
                        skipFile = false;
                    else if (fileGroup == -1 || ((pName.Length == 0 || pName[0] == '+' || pName[0] == '-') && (Crt.Atof(pName) != 0.0 || (pName.Length > 0 && pName[0] == '0'))))
                        skipFile = false;
                    else if (errMsg.Length == 0)
                        errMsg = pName;
                }
                if (skipFile) status = 9;
            }
            else if (status == 0 && nExpr == 0 && currentFile != 0)
                status = 20;

            if (status == 0)
            {
                int basePidSource = -1, basePid = 0;
                for (int j = 0; j < nExpr; j++)
                {
                    for (int n = 0; n < pFile.Count; n++)
                    {
                        double? refTime = null;
                        TimeTag? refGmt = null;
                        pid[j] = null;
                        int desiredFile = currentFile;
                        int colon = exprPid[j].LastIndexOf(':');
                        if (colon >= 0 && IsFunction(exprPid[j]) == Fn.None)
                        {
                            string s1 = pFile[n].Info.Path.ToLowerInvariant();
                            string s2 = exprPid[j][..colon].ToLowerInvariant();
                            string s3 = fileGroup != -1 ? GroupName(fileGroup).ToLowerInvariant() : " ";
                            if ((s1.Contains(s2) || s3.Contains(s2)) && FindParameter(exprPid[j][(colon + 1)..], pFile[n]) >= 0)
                            {
                                exprPid[j] = exprPid[j][(colon + 1)..];
                                desiredFile = n;
                            }
                            else
                                continue;
                        }

                        if (IsFunction(exprPid[j]) != Fn.None)
                        {
                            if ((pid[j] = DoFunction(pFile, desiredFile, fileGroup, exprPid[j], slice, tref, ref errMsg)) != null)
                            {
                                if (basePidSource < 0) basePidSource = desiredFile;
                            }
                            else if (!exprPid[j].Contains(':'))
                            {
                                if ((pid[j] = DoFunction(pFile, n, fileGroup, exprPid[j], slice, tref, ref errMsg)) != null)
                                    if (basePidSource < 0) basePidSource = n;
                            }
                        }
                        else if (FindParameter(exprPid[j], pFile[n]) >= 0)
                        {
                            if (!string.Equals(tref, "NONE", StringComparison.OrdinalIgnoreCase))
                            {
                                refGmt = TimeReference.GetRefGmt(pFile, n, tref);
                                if (refGmt == null) refTime = TimeReference.GetRefTime(pFile, n, tref);
                                if (refTime.HasValue || refGmt.HasValue)
                                {
                                    if ((pid[j] = Read(pFile[desiredFile], exprPid[j], slice, refTime, refGmt)) != null)
                                    {
                                        if (basePidSource < 0) basePidSource = desiredFile;
                                    }
                                    else if ((pid[j] = Read(pFile[n], exprPid[j], slice, refTime, refGmt)) != null)
                                    {
                                        if (basePidSource < 0) basePidSource = n;
                                    }
                                }
                                else
                                {
                                    string msg = Crt.Sprintf("Reference Event, %s, not found in parse()", tref);
                                    if (n == 0) Message(msg, PromptIcon.Warning);
                                    errMsg = "!break" + msg;
                                    break;
                                }
                            }
                            else
                            {
                                if ((pid[j] = Read(pFile[desiredFile], exprPid[j], slice, null, null)) != null)
                                {
                                    if (basePidSource < 0) basePidSource = desiredFile;
                                }
                                else if ((pid[j] = Read(pFile[n], exprPid[j], slice, null, null)) != null)
                                {
                                    if (basePidSource < 0) basePidSource = n;
                                }
                            }
                        }
                        if (pid[j] != null) break;
                    }

                    if (pid[j] == null && fileGroup != -1)
                    {
                        int colon = exprPid[j].LastIndexOf(':');
                        if (colon >= 0)
                        {
                            string prefix = exprPid[j][..colon];
                            string name = exprPid[j][(colon + 1)..];
                            var all = _ctx.AllFiles;
                            for (int f = 0; f < all.Count; f++)
                            {
                                int fg = GroupOfFile(f);
                                string s1 = all[f].Info.Path.ToLowerInvariant();
                                string s2 = prefix.ToLowerInvariant();
                                string s3 = GroupName(fg).ToLowerInvariant();
                                if ((s1.Contains(s2) || s3.Contains(s2)) && FindParameter(name, all[f]) >= 0)
                                {
                                    exprPid[j] = name;
                                    var grp = _ctx.Groups[fg];
                                    var gFiles = GroupFiles(fg);
                                    if (!string.Equals(grp.TRef, "NONE", StringComparison.OrdinalIgnoreCase))
                                    {
                                        TimeTag? rg = TimeReference.GetRefGmt(gFiles, f - grp.StartPos, grp.TRef);
                                        double? rt = rg == null ? TimeReference.GetRefTime(gFiles, f - grp.StartPos, grp.TRef) : null;
                                        if (rt.HasValue || rg.HasValue)
                                            pid[j] = Read(all[f], exprPid[j], slice, rt, rg);
                                        else
                                            Message(Crt.Sprintf("Reference Event, %s, not found in parse()", grp.TRef), PromptIcon.Warning);
                                    }
                                    else
                                        pid[j] = _ctx.Read(all[f], exprPid[j], slice[0], slice[1], null, null, false);
                                    break;
                                }
                            }
                            if (pid[j] == null)
                            {
                                if (errMsg.Length == 0) errMsg = exprPid[j];
                                status = 6;
                                break;
                            }
                        }
                        else
                        {
                            if (errMsg.Length == 0) errMsg = exprPid[j];
                            status = 6;
                            break;
                        }
                    }
                    else if (pid[j] == null)
                    {
                        if (errMsg.Length == 0) errMsg = exprPid[j];
                        status = 6;
                    }

                    if (fileGroup != -1 && j == 0 && pid[j] != null && !ReferenceEquals(pid[j]!.Source, pFile[currentFile]))
                    {
                        pid[j] = null;
                        status = 6;
                        break;
                    }
                }

                if (basePidSource < 0) basePidSource = 0;

                if (status == 0)
                {
                    if (!_ctx.CalcEveryInstance)
                    {
                        for (int j = 0; j < nExpr; j++)
                        {
                            float r = FileRate(pid[j]?.Source);
                            if (pid[j] != null && r > 0f && r < FileRate(pFile[basePidSource]))
                            {
                                for (int n = 0; n < pFile.Count; n++)
                                {
                                    if (ReferenceEquals(pid[j]!.Source, pFile[n]))
                                    {
                                        basePidSource = n;
                                        basePid = j;
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    if (nExpr != 0)
                        calculated = _ctx.CalcEveryInstance && nExpr > 1 ? CreateTimeLineParm(pid!) : pid[basePid]!.Clone();
                    else
                    {
                        string timeName = pFile[0].Parameters.Count > 0 ? pFile[0].Parameters[0].Name : "@0";
                        if (!string.Equals(tref, "NONE", StringComparison.OrdinalIgnoreCase))
                        {
                            TimeTag? rg = TimeReference.GetRefGmt(pFile, currentFile, tref);
                            double? rt = rg == null ? TimeReference.GetRefTime(pFile, currentFile, tref) : null;
                            if (rt.HasValue || rg.HasValue)
                                calculated = Read(pFile[basePidSource], timeName, slice, rt, rg);
                            else
                            {
                                _ctx.Ui.Show(Crt.Sprintf("Reference Event, %s, not found in parse()", tref), "Time Reference", PromptButtons.Ok, PromptIcon.Warning, PromptResult.Ok);
                                calculated = null;
                            }
                        }
                        else
                            calculated = Read(pFile[basePidSource], timeName, slice, null, null);
                    }

                    if (calculated != null)
                        status = Evaluate(calculated, pid!, exp, expression, fileGroup, ref errMsg);
                }
            }
        }
        else
            status = 1;

        if (calculated != null)
        {
            if (status != 0) return null;
            string baseExpr = expression;
            if (exprPid.Count == 0)
            {
                int c = expression.LastIndexOf(':');
                if (c >= 0) baseExpr = expression[(c + 1)..];
            }
            if (baseExpr.Length > 256) baseExpr = baseExpr[..252] + "...";
            if (expDesc != null)
            {
                if (expDesc.StartsWith('\\'))
                    ResetNomen(calculated, baseExpr, null, calculated.Description + expDesc);
                else
                {
                    string descr = expDesc.Replace("/U", "/u");
                    string units = "";
                    int j = descr.IndexOf("/u", StringComparison.Ordinal);
                    if (j != -1)
                    {
                        units = descr[j..];
                        int b = units.IndexOf('\\');
                        if (b != -1) units = units[..b];
                        descr = descr.Replace(units, "");
                        units = units.Replace("/u", "");
                    }
                    ResetNomen(calculated, baseExpr, units.Length == 0 ? null : units, descr.Length == 0 ? null : descr);
                }
            }
            else
                ResetNomen(calculated, baseExpr, null, null);
        }
        return calculated;
    }

    private int GroupOfFile(int f)
    {
        int g;
        for (g = _ctx.Groups.Count - 1; g > 0; g--)
            if (f >= _ctx.Groups[g].StartPos) break;
        return Math.Max(0, g);
    }

    private IReadOnlyList<IDataFile> GroupFiles(int g)
    {
        var grp = _ctx.Groups[g];
        var list = new List<IDataFile>(grp.NFiles);
        for (int i = 0; i < grp.NFiles && grp.StartPos + i < _ctx.AllFiles.Count; i++) list.Add(_ctx.AllFiles[grp.StartPos + i]);
        return list;
    }

    private int Evaluate(ParameterData calculated, List<ParameterData> pid, string exp, string expression, int fileGroup, ref string errMsg)
    {
        int status = 0;
        bool hrTime = _ctx.Use64BitTime && calculated.HasValidGmt;
        bool filter = HasAssignmentOp(exp);
        bool hasDelta = exp.Contains('~');
        int n = pid.Count;
        var syncTime = new bool[n];
        for (int j = 0; j < n; j++)
        {
            syncTime[j] = _ctx.Use64BitTime || calculated.Points != pid[j].Points || !ReferenceEquals(calculated.Source, pid[j].Source)
                || !IsCommonTimeSig(calculated.Source) || IsFunction(pid[j].Name) != Fn.None;
        }

        // overlay bracketed contents with 'P'
        var eb = expression.ToCharArray();
        int overlay = 0;
        for (int i = 0; i < eb.Length; i++)
        {
            switch (eb[i])
            {
                case '[':
                    if (overlay != 0) eb[i] = 'P';
                    overlay++;
                    break;
                case ']':
                    if (overlay > 1) eb[i] = 'P';
                    overlay = overlay > 0 ? overlay - 1 : 0;
                    break;
                default:
                    if (overlay != 0) eb[i] = 'P';
                    break;
            }
        }
        string eBuf = new string(eb);
        var cStack = new List<double>();
        var oStack = new List<Op>();
        var sStack = new List<bool>();
        int sPtr = BuildStack(eBuf, cStack, oStack, sStack);
        if (sPtr < 0)
        {
            if (!(_ctx.ScriptRunning && !_ctx.ScriptPrompt))
            {
                errMsg = sPtr switch
                {
                    -1 => eBuf.Contains(":[", StringComparison.Ordinal)
                        ? "Invalid file override\nFile specification must be within brackets\n"
                        : "Invalid constant in expression\nIf parameter or function, enclose with brackets\n",
                    -2 => "Unbalanced grouping of constants/parameters\n",
                    -3 => "Stack overflow--expression too large\n",
                    -4 => "Unidentified trailing characters\n",
                    _ => "Failed to parse expression components\nParameters AND Functions used in expressions\nMUST have enclosing brackets\n",
                };
                errMsg += eBuf;
                Message(errMsg);
                errMsg = "!abort";
            }
            return 7;
        }

        var w = new double[sPtr + 2];
        var t = new Op[Math.Max(1, sPtr)];
        var deltaStore = new List<double>();
        var cv = calculated.Values;
        var ct = calculated.Time;
        int filterPnts = 0;
        int points = calculated.Points;
        var cGmt = calculated.Gmt;
        int cGo = calculated.GmtOffset;
        for (int pnt = 0; pnt < points; pnt++)
        {
            bool hasDropout = false;
            int pPtr = 0, cPtr = 0;
            for (int i = 0; i <= sPtr; i++)
            {
                if (!sStack[i])
                {
                    w[i] = cStack[cPtr++];
                    continue;
                }
                var pp = pid[pPtr];
                bool gmtValid = pp.HasValidGmt;
                double baseTime = hrTime ? TimeMath.Time(cGmt![cGo + pnt], calculated.RefGmt!.Value) - (calculated.RefTime ?? 0.0) : ct[pnt];
                double parmStart = gmtValid ? TimeMath.Time(pp.GmtAt(0), pp.RefGmt!.Value) - (pp.RefTime ?? 0.0) : pp.Time[0];
                double parmEnd = gmtValid ? TimeMath.Time(pp.GmtAt(pp.Points - 1), pp.RefGmt!.Value) - (pp.RefTime ?? 0.0) : pp.Time[pp.Points - 1];
                int pLoc;
                if (baseTime < parmStart || baseTime > parmEnd)
                {
                    hasDropout = true;
                    w[0] = DropOut;
                    break;
                }
                if (syncTime[pPtr])
                {
                    if (hrTime && gmtValid)
                    {
                        double d = TimeMath.Time(cGmt![cGo + pnt], calculated.RefGmt!.Value);
                        pLoc = TimeMath.GmtLocation(d, pp.Gmt!, pp.GmtOffset, pp.Points, pp.RefGmt!.Value, _ctx.AlignClosest ? 0 : 1);
                    }
                    else
                        pLoc = TimeMath.Location(ct[pnt], pp.Time, pp.Points, _ctx.AlignClosest ? 0 : 1);
                }
                else
                    pLoc = pnt;
                if (ct[pnt] < pp.Time[0] || ct[pnt] > pp.Time[pp.Points - 1])
                {
                    hasDropout = true;
                    w[0] = DropOut;
                    break;
                }
                float v = pp.Values[pLoc];
                if (IsDrop(v))
                {
                    hasDropout = true;
                    w[0] = DropOut;
                    break;
                }
                w[i] = v;
                pPtr++;
            }

            if (!hasDropout)
            {
                int deltaPtr = 0;
                int pp2 = sPtr;
                for (int isp = 0; isp < pp2; isp++) t[isp] = oStack[isp];
                while (--pp2 >= 0)
                {
                    int rankingOp = pp2, highest = 0;
                    for (int i = pp2; i >= 0; i--)
                    {
                        if (highest <= t[i].Rank)
                        {
                            highest = t[i].Rank;
                            rankingOp = i;
                        }
                    }
                    int r = DoOperation(w, rankingOp, t[rankingOp], deltaStore, ref deltaPtr);
                    if (r == -1 && !IgnoreDbz)
                    {
                        if (_ctx.ShowDialogs)
                        {
                            string fEx = filter ? " (may be removed by filter)\n" : "\n";
                            if (_ctx.Ui.Show(GroupName(fileGroup) + "\nDivide by zero!" + fEx + exp + "\nIgnore and continue?", "Expression Parser",
                                    PromptButtons.YesNo, PromptIcon.Question, PromptResult.No) != PromptResult.Yes)
                                status = 30;
                            else
                                IgnoreDbz = true;
                        }
                        else
                            status = 30;
                        if (status != 0) { pnt = points; break; }
                    }
                    else if (r == -2 && !IgnoreNpow)
                    {
                        if (_ctx.ShowDialogs)
                        {
                            if (_ctx.Ui.Show(GroupName(fileGroup) + "\n-X ^ non-integer Y!\n" + exp + "\nIgnore and continue?", "Expression Parser",
                                    PromptButtons.YesNo, PromptIcon.Question, PromptResult.No) != PromptResult.Yes)
                                status = 31;
                            else
                                IgnoreNpow = true;
                        }
                        else
                            status = 31;
                        if (status != 0) { pnt = points; break; }
                    }
                    for (int i = rankingOp; i < pp2; i++)
                    {
                        t[i] = t[i + 1];
                        w[i + 1] = w[i + 2];
                    }
                }
            }
            if (status != 0) break;

            if (double.IsNaN(w[0])) w[0] = DropOut; // DEV-EXP-002: NaN results are stored as dropouts
            if (filter)
            {
                if (w[0] == Filtered) continue;
                cv[filterPnts] = (float)w[0];
                ct[filterPnts] = ct[pnt];
            }
            else
                cv[pnt] = (float)w[0];

            if (filterPnts == 0)
            {
                if (hasDelta) cv[filterPnts] = 0.0f;
                calculated.MinValue = calculated.MaxValue = cv[filterPnts];
                calculated.MinIndex = calculated.MaxIndex = filterPnts;
            }
            else
            {
                float f = cv[filterPnts];
                if (f != DropOut)
                {
                    if ((float)calculated.MinValue == DropOut || f < (float)calculated.MinValue)
                    {
                        calculated.MinValue = f;
                        calculated.MinIndex = filterPnts;
                    }
                    if (calculated.MaxValue == DropOut || f > (float)calculated.MaxValue)
                    {
                        calculated.MaxValue = f;
                        calculated.MaxIndex = filterPnts;
                    }
                }
            }
            filterPnts++;
        }

        if (status == 0 && filter)
        {
            if (filterPnts != 0)
            {
                calculated.Points = filterPnts;
                calculated.GmtOffsetValid = false;
            }
            else
            {
                status = 8;
                if (!IgnoreFilt && (!_ctx.ScriptRunning || _ctx.ScriptPrompt) && !_ctx.AutoRefresh)
                {
                    string fmsg = GroupName(fileGroup) + "\nFilter removed all points for\n" + expression + "\nIgnore additional occurances?";
                    if (_ctx.Ui.Show(fmsg, "Multiplot", PromptButtons.YesNo, PromptIcon.Information, PromptResult.Yes) == PromptResult.Yes)
                        IgnoreFilt = true;
                }
            }
        }

        if (sPtr > 0 && oStack[sPtr - 1].C0 == '=')
            ResetNomen(calculated, null, pid[^1].Units, pid[^1].Description);
        else
            ResetNomen(calculated, null, " ", " ");
        return status;
    }

    /// <summary>Union of all time stamps (duplicates within 1 µs removed).</summary>
    private ParameterData? CreateTimeLineParm(List<ParameterData> pParm)
    {
        int tPoints = pParm.Sum(p => p.Points);
        if (tPoints == 0) return null;
        var timeParm = pParm[0].Clone();
        int bottom;
        if (_ctx.Use64BitTime)
        {
            var arr = new double[tPoints];
            int k = 0;
            foreach (var p in pParm)
            {
                bool hr = p.HasValidGmt;
                for (int i = 0; i < p.Points; i++)
                    arr[k++] = hr ? TimeMath.Time(p.GmtAt(i), p.RefGmt!.Value) : p.Time[i];
            }
            Array.Sort(arr);
            var sorted = new double[tPoints];
            sorted[0] = arr[0];
            bottom = 0;
            for (int i = 0; i < tPoints; i++)
                if ((arr[i] - sorted[bottom]) >= TimeMath.TimeTolerance) sorted[++bottom] = arr[i];
            bottom++;
            timeParm.Time = new float[bottom];
            timeParm.Data = new float[bottom];
            bool gmt = timeParm.RefGmt.HasValue;
            timeParm.Gmt = gmt ? new TimeTag[bottom] : null;
            timeParm.GmtOffset = 0;
            timeParm.GmtPoints = gmt ? bottom : 0;
            timeParm.CommonTimeIndex = false;
            timeParm.GmtOffsetValid = gmt;
            for (int i = 0; i < bottom; i++)
            {
                timeParm.Time[i] = (float)sorted[i];
                if (gmt) timeParm.Gmt![i] = TimeMath.Gmt(sorted[i], timeParm.RefGmt!.Value, true);
            }
        }
        else
        {
            var arr = new float[tPoints];
            int k = 0;
            foreach (var p in pParm)
                for (int i = 0; i < p.Points; i++) arr[k++] = p.Time[i];
            Array.Sort(arr);
            var sorted = new float[tPoints];
            sorted[0] = arr[0];
            bottom = 0;
            for (int i = 0; i < tPoints; i++)
                if ((arr[i] - sorted[bottom]) >= TimeMath.TimeTolerance) sorted[++bottom] = arr[i];
            bottom++;
            timeParm.Time = new float[bottom];
            Array.Copy(sorted, timeParm.Time, bottom);
            timeParm.Data = new float[bottom];
            timeParm.GmtOffsetValid = false;
        }
        timeParm.Points = bottom;
        return timeParm;
    }

    /// <summary>Replaces Y time values by the X value at the closest time.</summary>
    public int SyncXaxis(ParameterData? y, ParameterData? x)
    {
        if (y == null || x == null) return -1;
        for (int tp = 0; tp < y.Points; tp++)
        {
            int dp = TimeMath.Location(y.Time[tp], x.Time, x.Points, 0);
            y.Time[tp] = x.Values[dp];
        }
        y.MinIndex = ParameterOps.DataMin(y, _ctx.FilterInvalid);
        y.MinValue = y.Values[y.MinIndex];
        y.MaxIndex = ParameterOps.DataMax(y, _ctx.FilterInvalid);
        y.MaxValue = y.Values[y.MaxIndex];
        return 0;
    }

    // ------------------------------------------------------------------ functions

    /// <summary>Next function argument over a NUL-terminated working buffer.</summary>
    private static string GetNextFuncArg(char[] buf, ref int iStart)
    {
        int arg = iStart;
        while (arg < buf.Length && buf[arg] == ' ') arg++;
        int Len(int from) { int k = from; while (k < buf.Length && buf[k] != '\0') k++; return k - from; }
        if (arg < buf.Length && Len(arg) > 0)
        {
            string rest = new string(buf, arg, Len(arg));
            if (IsFunction(rest) != Fn.None)
            {
                int openParen = 0;
                int ptr = Array.IndexOf(buf, '(', arg);
                while (ptr >= 0 && Len(ptr) > 0)
                {
                    if (buf[ptr++] == '(') openParen++;
                    if (ptr < buf.Length && buf[ptr++] == ')')
                    {
                        if (openParen > 0) openParen--;
                        else
                        {
                            // the terminator is written at the argument start (yielding an empty argument)
                            buf[arg] = '\0';
                            break;
                        }
                    }
                }
            }
            else if (buf[arg] == '\'')
            {
                arg++;
                int ptr = Array.IndexOf(buf, '\'', arg);
                if (ptr >= 0 && ptr < arg + Len(arg)) buf[ptr] = '\0';
            }
            else
            {
                int openParen = 0;
                int ptr = arg;
                while (ptr < buf.Length && buf[ptr] != '\0')
                {
                    if (buf[ptr] == '(') openParen++;
                    else if (buf[ptr] == ')')
                    {
                        openParen--;
                        if (openParen < 0) break;
                    }
                    else if (buf[ptr] == ' ' && openParen <= 0) break;
                    ptr++;
                }
                if (ptr < buf.Length) buf[ptr] = '\0';
            }
        }
        int l = arg < buf.Length ? Len(arg) : 0;
        if (l > 0) iStart = arg + l + 1;
        return l > 0 ? new string(buf, arg, l) : "";
    }

    /// <summary>Evaluates a function.</summary>
    public ParameterData? DoFunction(IReadOnlyList<IDataFile> pFile, int currentFile, int fileGroup, string funcExpr, double[] slice, string tref, ref string errMsg)
    {
        string work = funcExpr;
        string? expDesc = null;
        int q = work.IndexOf('"');
        if (q >= 0)
        {
            expDesc = work[(q + 1)..];
            int q2 = expDesc.IndexOf('"');
            if (q2 >= 0) expDesc = expDesc[..q2];
            work = work[..q];
        }
        string pName = work;
        Fn funcNum = IsFunction(funcExpr);
        var buf = new char[work.Length + 2];
        work.CopyTo(0, buf, 0, work.Length);
        int iStart = ValidFunc[(int)funcNum].Length;
        var parmNm = new string?[8];
        parmNm[0] = GetNextFuncArg(buf, ref iStart);

        float timeDelta = _ctx.AverageRate, avgTime = 0f, clipMin = -3.4e38f, clipMax = 3.4e38f;
        int timeMode = _ctx.TimeStampMode, splineFactor = 2, polyDegree = 2;
        double tStart = 0, tStop = 0;
        string refpropFn = "", refpropFluid = "", refpropCode = "", extFn = "", extOpt = "";
        string a;
        switch (funcNum)
        {
            case Fn.Quantize:
                a = GetNextFuncArg(buf, ref iStart);
                timeDelta = a.Length > 0 ? (Crt.Atof(a) >= TimeMath.TimeTolerance ? (float)Crt.Atof(a) : _ctx.AverageRate) : _ctx.AverageRate;
                a = GetNextFuncArg(buf, ref iStart);
                timeMode = a.Length > 0 ? Crt.Atoi(a) : 0;
                break;
            case Fn.Avg:
                {
                    string tail = new string(buf, iStart, Math.Max(0, Array.IndexOf(buf, '\0', iStart) - iStart));
                    int ib = IndexOfAnyChar(tail, 'b', 'B', 'e', 'E', 'a', 'A');
                    timeMode = ib >= 0 ? (char.ToLowerInvariant(tail[ib]) == 'b' ? ParameterOps.TsFirstPoint : char.ToLowerInvariant(tail[ib]) == 'e' ? ParameterOps.TsLastPoint : ParameterOps.TsAveraged) : _ctx.TimeStampMode;
                    a = GetNextFuncArg(buf, ref iStart);
                    timeDelta = a.Length > 0 ? (Crt.Atof(a) >= TimeMath.TimeTolerance ? (float)Crt.Atof(a) : _ctx.AverageRate) : _ctx.AverageRate;
                    a = GetNextFuncArg(buf, ref iStart);
                    avgTime = a.Length > 0 && !char.IsAsciiLetter(a[0]) ? (float)Crt.Atof(a) : timeDelta;
                    break;
                }
            case Fn.Clip:
                a = GetNextFuncArg(buf, ref iStart);
                clipMin = a.Length > 0 ? (float)Crt.Atof(a) : -3.4e38f;
                a = GetNextFuncArg(buf, ref iStart);
                clipMax = a.Length > 0 ? (float)Crt.Atof(a) : 3.4e38f;
                break;
            case Fn.Spline:
                a = GetNextFuncArg(buf, ref iStart);
                splineFactor = a.Length > 0 ? Math.Abs(Crt.Atoi(a)) : 2;
                break;
            case Fn.PolyFit:
                a = GetNextFuncArg(buf, ref iStart);
                polyDegree = a.Length > 0 ? Math.Abs(Crt.Atoi(a)) : 2;
                break;
            case Fn.Compact:
                a = GetNextFuncArg(buf, ref iStart);
                timeDelta = a.Length > 0 ? (float)Crt.Atof(a) : 1f;
                break;
            case Fn.Stale:
                parmNm[1] = GetNextFuncArg(buf, ref iStart);
                a = GetNextFuncArg(buf, ref iStart);
                timeDelta = a.Length > 0 ? (Crt.Atof(a) >= TimeMath.TimeTolerance ? (float)Crt.Atof(a) : 0f) : 0f;
                break;
            case Fn.RefProp:
                refpropFn = parmNm[0]!;
                refpropFluid = GetNextFuncArg(buf, ref iStart);
                refpropCode = GetNextFuncArg(buf, ref iStart);
                parmNm[0] = GetNextFuncArg(buf, ref iStart);
                parmNm[1] = GetNextFuncArg(buf, ref iStart);
                break;
            case Fn.Density:
            case Fn.Entropy:
            case Fn.Enthalpy:
            case Fn.Rho:
                parmNm[1] = GetNextFuncArg(buf, ref iStart);
                refpropFluid = FluidName(GetNextFuncArg(buf, ref iStart));
                refpropCode = GetNextFuncArg(buf, ref iStart);
                if (ValidateConst(refpropCode) == 0)
                    refpropCode = Crt.Atoi(refpropCode) switch { 1 => "TP", 2 => "PS", _ => refpropCode };
                refpropFn = ValidFunc[(int)funcNum].TrimEnd('(');
                funcNum = Fn.RefProp;
                break;
            case Fn.Temperature:
            case Fn.Pressure:
                refpropFluid = FluidName(GetNextFuncArg(buf, ref iStart));
                refpropCode = funcNum == Fn.Temperature ? "P" : "T";
                refpropFn = ValidFunc[(int)funcNum].TrimEnd('(');
                funcNum = Fn.RefProp;
                break;
            case Fn.Atan2:
            case Fn.XyPair:
                parmNm[1] = GetNextFuncArg(buf, ref iStart);
                break;
            case Fn.ZShift:
                a = GetNextFuncArg(buf, ref iStart);
                tStart = a.Length > 0 ? Crt.Atof(a) : 0.0;
                a = GetNextFuncArg(buf, ref iStart);
                tStop = a.Length > 0 ? Crt.Atof(a) : tStart;
                break;
            case Fn.External:
                extFn = parmNm[0]!;
                extOpt = GetNextFuncArg(buf, ref iStart);
                for (int k = 0; k < 8; k++) parmNm[k] = GetNextFuncArg(buf, ref iStart);
                break;
        }

        string p0 = parmNm[0] ?? "";
        int colon = p0.LastIndexOf(':');
        if (colon >= 0)
        {
            int fgroup;
            for (fgroup = _ctx.Groups.Count - 1; fgroup > 0; fgroup--)
                if (currentFile >= _ctx.Groups[fgroup].StartPos) break;
            string s1 = pFile[currentFile].Info.Path.ToLowerInvariant();
            string s2 = p0[..colon].ToLowerInvariant();
            string s3 = fgroup >= 0 ? GroupName(fgroup).ToLowerInvariant() : " ";
            if (s1.Contains(s2) || (s3.Contains(s2) && FindParameter(p0[(colon + 1)..], pFile[currentFile]) >= 0))
                p0 = p0[(colon + 1)..];
            parmNm[0] = p0;
        }

        double? refTime = null;
        TimeTag? refGmt = null;
        if (!string.Equals(tref, "NONE", StringComparison.OrdinalIgnoreCase))
        {
            refGmt = TimeReference.GetRefGmt(pFile, currentFile, tref);
            if (refGmt == null) refTime = TimeReference.GetRefTime(pFile, currentFile, tref);
            if (!(refTime.HasValue || refGmt.HasValue))
            {
                if (ErrPrompt && FindParameter(p0, pFile[currentFile]) >= 0)
                {
                    Message(Crt.Sprintf("Reference Event, %s, not found in doFunction()", tref), PromptIcon.Warning);
                    ErrPrompt = false;
                }
            }
        }

        var pParm = new ParameterData?[8];
        if (IsFunction(p0) != Fn.None) pParm[0] = DoFunction(pFile, currentFile, fileGroup, p0, slice, tref, ref errMsg);
        else if (IsExpression(p0)) pParm[0] = Parse(pFile, currentFile, fileGroup, p0, slice, tref, ref errMsg);
        else pParm[0] = Read(pFile[currentFile], p0, slice, refTime, refGmt);

        if (pParm[0] != null)
        {
            for (int k = 1; k < 8; k++)
            {
                string? nm = parmNm[k];
                if (string.IsNullOrEmpty(nm)) continue;
                if (IsFunction(nm) != Fn.None) pParm[k] = DoFunction(pFile, currentFile, fileGroup, nm, slice, tref, ref errMsg);
                else if (IsExpression(nm)) pParm[k] = Parse(pFile, currentFile, fileGroup, nm, slice, tref, ref errMsg);
                else
                {
                    pParm[k] = Read(pFile[currentFile], nm, slice, refTime, refGmt);
                    if (pParm[k] == null)
                    {
                        for (int i = 0; i < pFile.Count; i++)
                        {
                            if (i == currentFile) continue;
                            if ((pParm[k] = Read(pFile[i], nm, slice, refTime, refGmt)) != null) break;
                        }
                    }
                }
                if (pParm[k] == null)
                {
                    pParm[0] = null;
                    break;
                }
            }
        }

        int nPnts = 0;
        var p = pParm[0];
        if (p != null)
        {
            switch (funcNum)
            {
                case Fn.Quantize:
                    if ((nPnts = ParameterOps.Quantize(p, timeDelta, timeMode != 0, _ctx.FilterInvalid)) > 0) ResetNomen(p, pName, null, expDesc);
                    break;
                case Fn.Avg:
                    if (ParameterOps.Average(p, timeDelta, avgTime, timeMode) != 0)
                    {
                        nPnts = p.Points;
                        ResetNomen(p, pName, null, expDesc);
                    }
                    break;
                case Fn.Spline:
                    if (Spline(p, splineFactor))
                    {
                        nPnts = p.Points;
                        ResetNomen(p, pName, null, expDesc);
                    }
                    break;
                case Fn.Fit:
                    if (LeastSquares(p))
                    {
                        nPnts = p.Points;
                        ResetNomen(p, pName, null, p.Description + (expDesc ?? ""));
                    }
                    break;
                case Fn.PolyFit:
                    _ = polyDegree; // PolyFit.dll capability not provided (OQ-006)
                    break;
                case Fn.Stale:
                    if (Stale(p, pParm[1], timeDelta))
                    {
                        nPnts = p.Points;
                        ResetNomen(p, pName, null, expDesc);
                    }
                    break;
                case Fn.XyPair:
                    if (SyncXaxis(p, pParm[1]) == 0)
                    {
                        nPnts = p.Points;
                        ResetNomen(p, pName, null, expDesc);
                    }
                    break;
                case Fn.ZShift:
                    if (ZShift(p, tStart, tStop))
                    {
                        nPnts = p.Points;
                        ResetNomen(p, pName, null, expDesc);
                    }
                    break;
                case Fn.Redline:
                    nPnts = p.Points;
                    ResetNomen(p, pName, null, expDesc);
                    break;
                case Fn.Compact:
                    nPnts = ParameterOps.CompactData(p, timeDelta, true, 0, _ctx.FilterInvalid, out _);
                    ResetNomen(p, pName, null, expDesc);
                    break;
                case Fn.RefProp:
                    {
                        var prov = _ctx.FunctionProviders.FirstOrDefault(fp => fp.FunctionNames.Any(n => string.Equals(n, "refprop", StringComparison.OrdinalIgnoreCase) || string.Equals(n, refpropFn, StringComparison.OrdinalIgnoreCase)));
                        if (prov != null)
                        {
                            var args = pParm.Where(x => x != null).Cast<ParameterData>().ToList();
                            var res = SafeInvoke(prov, "refprop", refpropFn + " " + refpropFluid + " " + refpropCode, args, ref errMsg);
                            if (res != null) { pParm[0] = p = res; nPnts = p.Points; ResetNomen(p, pName, null, expDesc); }
                        }
                        else
                            errMsg = "RefProp() function not available (DLL not loaded)";
                        break;
                    }
                case Fn.External:
                    {
                        var prov = _ctx.FunctionProviders.FirstOrDefault(fp => fp.FunctionNames.Any(n => string.Equals(n, extFn, StringComparison.OrdinalIgnoreCase)));
                        if (prov != null)
                        {
                            var args = pParm.Where(x => x != null).Cast<ParameterData>().ToList();
                            var res = SafeInvoke(prov, extFn, extOpt, args, ref errMsg);
                            if (res != null) { pParm[0] = p = res; nPnts = p.Points; ResetNomen(p, pName, null, expDesc); }
                        }
                        else
                            errMsg = "Externally defined fn() not available (DLL not loaded)";
                        break;
                    }
                default:
                    nPnts = PointFunction(p, pParm[1], funcNum, clipMin, clipMax);
                    ResetNomen(p, pName, null, expDesc);
                    break;
            }
            if (funcNum == Fn.Redline && p != null)
            {
                string up = pName.ToUpperInvariant();
                p.Redline = up.Contains("HI)") || up.Contains("MAX)") ? 1 : up.Contains("LO)") || up.Contains("MIN)") ? 2 : 3;
            }
        }
        if (nPnts == 0) return null;
        return p;
    }

    private ParameterData? SafeInvoke(IExpressionFunctionProvider prov, string fn, string options, List<ParameterData> args, ref string errMsg)
    {
        try
        {
            var res = prov.Invoke(fn, options, args, out string? err);
            if (err != null) errMsg = err;
            return res;
        }
        catch (Exception ex)
        {
            errMsg = prov.Name + ": " + ex.Message;
            return null;
        }
    }

    private static int IndexOfAnyChar(string s, params char[] chars)
    {
        foreach (char c in chars)
        {
            int i = s.IndexOf(c);
            if (i >= 0) return i;
        }
        return -1;
    }

    private static string FluidName(string f)
    {
        if (ValidateConst(f) != 0) return f;
        return Crt.Atoi(f) switch
        {
            2 => "NITROGEN",
            3 => "OXYGEN",
            8 => "HELIUM",
            10 => "HYDROGEN",
            11 => "PARAHYD",
            _ => f,
        };
    }

    private int PointFunction(ParameterData p, ParameterData? p1, Fn funcNum, float clipMin, float clipMax)
    {
        var s = p.Values;
        int n = p.Points;
        float preVal = 0;
        for (int i = 0; i < n; i++)
        {
            if (funcNum == Fn.Delta)
            {
                if (i == 0)
                {
                    preVal = s[0];
                    s[0] = DropOut;
                }
                else if (preVal != DropOut && s[i] != DropOut)
                {
                    float tmp = s[i];
                    s[i] -= preVal;
                    preVal = tmp;
                }
            }
            else if (funcNum == Fn.DeltaT)
            {
                if (i == 0) s[0] = DropOut;
                else if (p.HasValidGmt) s[i] = (float)TimeMath.Time(p.GmtAt(i), p.GmtAt(i - 1));
                else s[i] = p.Time[i] - p.Time[i - 1];
            }
            else if (!IsDrop(s[i]))
            {
                float x = s[i];
                switch (funcNum)
                {
                    case Fn.RelTime: s[i] = p.Time[i]; break;
                    case Fn.Abs: s[i] = (float)Math.Abs((double)x); break;
                    case Fn.Int: s[i] = (int)x; break;
                    case Fn.Acos: s[i] = Math.Abs(x) <= 1.0 ? (float)Math.Acos(x) : DropOut; break;
                    case Fn.Asin: s[i] = Math.Abs(x) <= 1.0 ? (float)Math.Asin(x) : DropOut; break;
                    case Fn.Atan: s[i] = (float)Math.Atan(x); break;
                    case Fn.Atan2:
                        {
                            int loc2 = TimeMath.Location(p.Time[i], p1!.Time, p1.Points, 1);
                            s[i] = !IsDrop(p1.Values[loc2]) ? (float)Math.Atan2(x, p1.Values[loc2]) : p1.Values[loc2];
                            break;
                        }
                    case Fn.Ceil: s[i] = (float)Math.Ceiling((double)x); break;
                    case Fn.Cos: s[i] = (float)Math.Cos(x); break;
                    case Fn.Cosh: s[i] = Math.Abs(x) <= 85.0 ? (float)Math.Cosh(x) : DropOut; break;
                    case Fn.Exp: s[i] = Math.Abs(x) <= 85.0 ? (float)Math.Exp(x) : DropOut; break;
                    case Fn.Floor: s[i] = (float)Math.Floor((double)x); break;
                    case Fn.Log: s[i] = x > 0.0f ? (float)Math.Log(x) : DropOut; break;
                    case Fn.Log10: s[i] = x > 0.0f ? (float)Math.Log10(x) : DropOut; break;
                    case Fn.Rand: s[i] = MsvcRand.Next(); break;
                    case Fn.Sin: s[i] = (float)Math.Sin(x); break;
                    case Fn.Sinh: s[i] = Math.Abs(x) <= 85.0 ? (float)Math.Sinh(x) : DropOut; break;
                    case Fn.Sqrt: s[i] = x > 0.0f ? (float)Math.Sqrt(x) : DropOut; break;
                    case Fn.Tan: s[i] = Math.Abs(x) <= 85.0 ? (float)Math.Tan(x) : DropOut; break;
                    case Fn.Tanh: s[i] = Math.Abs(x) <= 85.0 ? (float)Math.Tanh(x) : DropOut; break;
                    case Fn.Clip:
                        if (x < clipMin) s[i] = clipMin;
                        else if (x > clipMax) s[i] = clipMax;
                        break;
                }
            }
        }
        p.MinIndex = ParameterOps.DataMin(p, _ctx.FilterInvalid);
        p.MinValue = s[p.MinIndex];
        p.MaxIndex = ParameterOps.DataMax(p, _ctx.FilterInvalid);
        p.MaxValue = s[p.MaxIndex];
        return n;
    }

    private bool LeastSquares(ParameterData pid)
    {
        var x = pid.Time;
        var y = pid.Values;
        double sumX = 0, sumX2 = 0, sumY = 0, sumY2 = 0, sumXy = 0;
        for (int i = 0; i < pid.Points; ++i)
        {
            sumY += y[i];
            sumY2 += (double)y[i] * y[i];
            sumX += x[i];
            sumX2 += (double)x[i] * x[i];
            sumXy += (double)x[i] * y[i];
        }
        double delta = (double)pid.Points * sumX2 - sumX * sumX;
        if (delta == 0.0) return false;
        double a = (sumX2 * sumY - sumX * sumXy) / delta;
        double b = (pid.Points * sumXy - sumX * sumY) / delta;
        for (int i = 0; i < pid.Points; ++i) y[i] = (float)(a + b * x[i]);
        ResetNomen(pid, null, null, Crt.Sprintf("Slope=%g Intercept=%g", b, a));
        pid.GmtOffsetValid = false;
        pid.MinIndex = ParameterOps.DataMin(pid, _ctx.FilterInvalid);
        pid.MinValue = y[pid.MinIndex];
        pid.MaxIndex = ParameterOps.DataMax(pid, _ctx.FilterInvalid);
        pid.MaxValue = y[pid.MaxIndex];
        return true;
    }

    /// <summary>Natural cubic spline (Burden &amp; Faires Alg. 3.4) in float arithmetic.</summary>
    private bool Spline(ParameterData pid, int splineFactor)
    {
        if (pid.Points < 2 || pid.Points > 50000)
        {
            if (_ctx.ShowDialogs && !_ctx.ScriptRunning)
            {
                if (_ctx.Ui.Show(Crt.Sprintf("Perform SPLINE() with %d points?\n*Long process", pid.Points), "Multiplot", PromptButtons.OkCancel, PromptIcon.Warning, PromptResult.Ok) == PromptResult.Cancel)
                    return false;
            }
            if (pid.Points < 2) return false;
        }
        int numx = pid.Points;
        int sz = numx + 1;
        var x = new float[sz]; var aa = new float[sz]; var b = new float[sz]; var c = new float[sz]; var d = new float[sz];
        var alpha = new float[sz]; var l = new float[sz]; var mu = new float[sz]; var z = new float[sz]; var h = new float[sz];
        int nxs = numx - 1;
        for (int i = 0; i <= nxs; i++) { x[i] = pid.Time[i]; aa[i] = pid.Values[i]; }
        for (int i = 0; i <= nxs - 1; i++) h[i] = x[i + 1] - x[i];
        x[nxs + 1] = 40000f;
        for (int i = 1; i <= nxs - 1; i++)
            alpha[i] = 3.0f * (aa[i + 1] * h[i - 1] - aa[i] * (x[i + 1] - x[i - 1]) + aa[i - 1] * h[i]) / (h[i - 1] * h[i]);
        l[0] = 1.0f; mu[0] = 0.0f; z[0] = 0.0f;
        for (int i = 1; i <= nxs - 1; i++)
        {
            l[i] = 2.0f * (x[i + 1] - x[i - 1]) - h[i - 1] * mu[i - 1];
            mu[i] = h[i] / l[i];
            z[i] = (alpha[i] - h[i - 1] * z[i - 1]) / l[i];
        }
        l[nxs] = 1.0f; z[nxs] = 0.0f; c[nxs] = 0.0f;
        int k = nxs;
        do
        {
            k--;
            c[k] = z[k] - mu[k] * c[k + 1];
            b[k] = (aa[k + 1] - aa[k]) / h[k] - h[k] * (c[k + 1] + 2.0f * c[k]) / 3.0f;
            d[k] = (c[k + 1] - c[k]) / (3.0f * h[k]);
        } while (k > 0);

        if (splineFactor < 2) splineFactor = 2;
        int newSize = pid.Points * splineFactor;
        var time = new float[newSize];
        var data = new float[newSize];
        double range = pid.Time[pid.Points - 1] - pid.Time[0];
        double delta = range / ((double)pid.Points * splineFactor);
        double xval = pid.Time[0];
        int seg = 0;
        for (int p = 0; p < newSize; p++)
        {
            if ((p % 100000) == 0) _ctx.SetStatus(Crt.Sprintf("calculating spline (%%%d)", (int)((float)p / newSize * 100f)));
            int i;
            // linear scan; segments are monotonic so resume from the last one when still valid
            if (!(seg < nxs && x[seg] <= xval && x[seg + 1] > xval)) seg = 0;
            for (i = seg; i < nxs; i++)
                if (x[i] <= xval && x[i + 1] > xval) break;
            if (i >= nxs)
            {
                for (i = 0; i < nxs; i++) if (x[i] <= xval && x[i + 1] > xval) break;
            }
            seg = i;
            float term = (float)xval - x[i];
            data[p] = aa[i] + b[i] * term + c[i] * term * term + d[i] * term * term * term;
            if (p == 0)
            {
                pid.MinIndex = pid.MaxIndex = 0;
                pid.MinValue = pid.MaxValue = data[0];
            }
            else
            {
                if (data[p] < pid.MinValue) { pid.MinValue = data[p]; pid.MinIndex = p; }
                if (data[p] > pid.MaxValue) { pid.MaxValue = data[p]; pid.MaxIndex = p; }
            }
            time[p] = (float)xval;
            xval += delta;
        }
        pid.Data = data;
        pid.Time = time;
        pid.Points = newSize;
        pid.GmtOffsetValid = false;
        return true;
    }

    private bool Stale(ParameterData primary, ParameterData? stale, float tDelta)
    {
        if (stale == null || primary.Points != stale.Points) return false;
        float timeDelta;
        if (tDelta == 0f)
        {
            float rate = FileRate(primary.Source);
            timeDelta = rate > 0.0 ? (1.0f / rate) / 2.0f : .02f;
        }
        else
            timeDelta = tDelta;
        int n = primary.Points;
        var time = new float[n * 2];
        var data = new float[n * 2];
        for (int p = 0; p < n; p++)
        {
            time[p * 2] = primary.Time[p] - timeDelta;
            data[p * 2] = stale.Values[p];
            time[p * 2 + 1] = primary.Time[p];
            data[p * 2 + 1] = primary.Values[p];
        }
        primary.Data = data;
        primary.Time = time;
        primary.Points = n * 2;
        primary.GmtOffsetValid = false;
        primary.MinIndex = ParameterOps.DataMin(primary, _ctx.FilterInvalid);
        primary.MinValue = data[primary.MinIndex];
        primary.MaxIndex = ParameterOps.DataMax(primary, _ctx.FilterInvalid);
        primary.MaxValue = data[primary.MaxIndex];
        return true;
    }

    private bool ZShift(ParameterData pid, double tStart, double tStop)
    {
        if (pid.Source == null) return false;
        ParameterData? shift = null;
        float shiftValue = 0;
        if (Math.Abs(tStart - tStop) < .001)
        {
            shift = _ctx.Read(pid.Source, pid.Name, tStart - 10.0, tStart + 10.0, pid.RefTime, pid.RefGmt, false);
            if (shift != null) shiftValue = shift.Values[TimeMath.Location(tStart, shift.Time, shift.Points, 0)];
        }
        else if (tStart < tStop)
        {
            shift = _ctx.Read(pid.Source, pid.Name, tStart, tStop, pid.RefTime, pid.RefGmt, false);
            if (shift != null)
            {
                shiftValue = 0;
                for (int p = 0; p < shift.Points; p++) shiftValue += shift.Values[p];
                shiftValue /= shift.Points;
            }
        }
        if (shift == null) return false;
        var v = pid.Values;
        for (int p = 0; p < pid.Points; p++) v[p] -= shiftValue;
        return true;
    }
}
