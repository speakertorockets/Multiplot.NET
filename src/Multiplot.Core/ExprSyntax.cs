namespace Multiplot.Core;

/// <summary>
/// Expression / function string classification: expressions, functions, enclosures, constants and
/// operator search.
/// </summary>
public static class ExprSyntax
{
    /// <summary>Operator characters in rank order.</summary>
    public const string MathOps = "^/*%+-&|~<>=?#";

    /// <summary>Function ids.</summary>
    public enum Fn
    {
        None = 0, Avg, Abs, Acos, Asin, Atan, Atan2, Ceil, Cos, Cosh, Exp, Floor, Log, Log10, Rand, Sin, Sinh, Sqrt, Tan, Tanh,
        Spline, DeltaT, Delta, Fit, Stale, ZShift, Redline, Compact, XyPair, RelTime, PolyFit, Clip, Quantize, Int,
        Density, Enthalpy, Entropy, Pressure, Temperature, Rho, RefProp, External,
    }

    /// <summary>Function name prefixes indexed by <see cref="Fn"/>.</summary>
    public static readonly string[] ValidFunc =
    {
        "____", "avg(", "abs(", "acos(", "asin(", "atan(", "atan2(", "ceil(", "cos(", "cosh(", "exp(", "floor(", "log(", "log10(",
        "rand(", "sin(", "sinh(", "sqrt(", "tan(", "tanh(", "spline(", "deltat(", "delta(", "fit(", "stale(", "zshift(", "redline(",
        "compact(", "xypair(", "reltime(", "polyfit(", "clip(", "quantize(", "int(", "density(", "enthalpy(", "entropy(", "pressure(",
        "temperature(", "rho(", "refprop(", "fn(",
    };

    /// <summary>True when the text is an expression (optionally checking enclosures).</summary>
    public static bool IsExpression(string s, bool checkEnclosures = true)
    {
        bool quoted = false, aConstant = false;
        int ops = 0, encl = 0;
        for (int j = 0; j < s.Length; j++)
        {
            char c = s[j];
            if (c == '\0') break;
            if (c == '"') break;
            if (c == '\'') quoted = !quoted;
            if (!quoted && checkEnclosures && "[]()".IndexOf(c) >= 0)
            {
                encl++;
                break;
            }
            if (!quoted && MathOps.IndexOf(c) >= 0)
            {
                ops++;
                if (encl != 0) break;
                if (j == 0 && (c == '+' || c == '-'))
                {
                    char n1 = j + 1 < s.Length ? s[j + 1] : '\0';
                    char n2 = j + 2 < s.Length ? s[j + 2] : '\0';
                    if (char.IsAsciiDigit(n1) || (n1 == '.' && char.IsAsciiDigit(n2)))
                    {
                        aConstant = true;
                        break;
                    }
                }
            }
        }
        return aConstant || encl != 0;
    }

    /// <summary>Function id, or 0.</summary>
    public static Fn IsFunction(string s)
    {
        string funcBuf = s.ToLowerInvariant();
        int f = funcBuf.IndexOf('"');
        if (f > 0) funcBuf = funcBuf[..f];
        funcBuf = funcBuf.TrimEnd(' ');
        for (int k = 0; k < ValidFunc.Length; k++)
        {
            if (!funcBuf.StartsWith(ValidFunc[k], StringComparison.Ordinal)) continue;
            Fn func = (Fn)k;
            int groupLevel = 0;
            for (int i = funcBuf.IndexOf('('); i >= 0 && i < s.Length; i++)
            {
                if (s[i] == '(') groupLevel++;
                if (s[i] == ')') groupLevel--;
                if (groupLevel == 0)
                {
                    string rest = i + 1 < funcBuf.Length ? funcBuf[(i + 1)..] : "";
                    string next = rest.Length > 0 ? rest[..1] : "";
                    if (next != "]" && IsExpression(rest)) func = Fn.None;
                    break;
                }
            }
            return func;
        }
        return Fn.None;
    }

    /// <summary>Index of the next operator at position ≥ 1 not preceded by e/E, or −1.</summary>
    public static int FindOperator(string expr, int start = 0)
    {
        for (int i = start + 1; i < expr.Length; i++)
        {
            if (expr[i] == '\0') break;
            if (MathOps.IndexOf(expr[i]) >= 0 && char.ToUpperInvariant(expr[i - 1]) != 'E') return i;
        }
        return -1;
    }

    /// <summary>0 when the text up to the next operator is a valid constant.</summary>
    public static int ValidateConst(string szConst)
    {
        string c = szConst.TrimStart(' ').ToLowerInvariant();
        int z = c.IndexOf('\0');
        if (z >= 0) c = c[..z];
        int cEnd = FindOperator(c);
        string e = cEnd >= 0 ? c[..cEnd] : c;
        e = e.TrimEnd(' ', ')'); // CString::TrimRight scans to the inserted terminator
        int colon = e.IndexOf(':');
        if (colon >= 0) e = e[(colon + 1)..];
        int err = 0;
        if (e.Length >= 2 && e[0] == '0' && e[1] == 'x')
        {
            if (e.Length <= 6)
            {
                for (int i = 2; i < e.Length; i++)
                    if (!Uri.IsHexDigit(e[i])) { err = 6; break; }
            }
            else
                err = 7;
        }
        else
        {
            bool dot = false;
            for (int i = 0; i < e.Length; i++)
            {
                char ch = e[i];
                if (i == 0 && (ch == '-' || ch == '+')) continue;
                if (char.IsAsciiDigit(ch)) continue;
                if (ch == '.')
                {
                    if (!dot) dot = true;
                    else { err = 5; break; }
                }
                else if (char.ToUpperInvariant(ch) == 'E')
                {
                    if (i + 1 < e.Length)
                    {
                        if (Crt.Atof(e[(i + 1)..]) == 0.0 && !char.IsAsciiDigit(e[i + 1])) { err = 3; break; }
                        else if (e[i + 1] == '-' || e[i + 1] == '+') i++;
                    }
                    else { err = 2; break; }
                }
                else { err = 4; break; }
            }
        }
        return err;
    }

    /// <summary>0 OK, else 1/2/10/11/12/20/21/22/30/31.</summary>
    public static int CheckEnclosures(string expression)
    {
        int openParen = 0, openBracket = 0, status = 0;
        bool hasExpression = false;
        foreach (char c in expression)
        {
            switch (c)
            {
                case '(': openParen++; break;
                case ')': openParen--; break;
                case '[': openBracket++; break;
                case ']': openBracket--; break;
            }
        }
        if (openParen != 0) status = openParen > 0 ? 10 : 20;
        if (openBracket != 0) status += openBracket > 0 ? 1 : 2;
        if (status == 0)
        {
            for (int j = 0; j < expression.Length; j++)
            {
                char c = expression[j];
                if (c == ')') openParen--;
                else if (c == '(') openParen++;
                else if (c == ']')
                {
                    openBracket--;
                    if (openBracket < 0) { status = 30; break; }
                }
                else if (c == '[')
                {
                    openBracket++;
                    if (openBracket > 1 && !hasExpression) { status = 31; break; }
                    string rest = expression[(j + 1)..];
                    if (IsFunction(rest) != Fn.None || IsExpression(rest)) hasExpression = true;
                }
            }
        }
        return status;
    }

    /// <summary>Contains '=' that is not part of "==".</summary>
    public static bool HasAssignmentOp(string expression) => expression.Replace("==", "--").Contains('=');
}
