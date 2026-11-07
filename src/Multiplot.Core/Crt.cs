// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Numerics;
using System.Text;

namespace Multiplot.Core;

/// <summary>
/// Culture-invariant emulation of the MSVC (UCRT, v142) C runtime number parsing and
/// formatting functions (<c>atof</c>, <c>atoi</c>, <c>atol</c>,
/// <c>sprintf</c> %d %u %x %o %f %e %g %s %c, <c>_gcvt</c>) — NFR-COMP-002.
/// Floating point output is exact and rounds half away from zero like the UCRT.
/// </summary>
public static class Crt
{
    /// <summary>
    /// Exact-tie rounding of floating point output. The UCRT shipped with VS2019 16.2+ (toolset v142)
    /// rounds exactly representable halfway cases to even; set false for half-away-from-zero.
    /// </summary>
    public static bool RoundHalfEven { get; set; } = true;

    private static bool IsCSpace(char c) => c == ' ' || c == '\t' || c == '\n' || c == '\v' || c == '\f' || c == '\r';

    /// <summary>C <c>atof</c>: longest valid numeric prefix after leading white space, else 0.</summary>
    public static double Atof(string? s) => Atof(s, 0, out _);

    /// <summary>C <c>strtod</c> starting at <paramref name="start"/>; <paramref name="end"/> receives the index after the number.</summary>
    public static double Atof(string? s, int start, out int end)
    {
        end = start;
        if (s == null) return 0;
        int i = start, n = s.Length;
        while (i < n && IsCSpace(s[i])) i++;
        int signPos = i;
        bool neg = false;
        if (i < n && (s[i] == '+' || s[i] == '-')) { neg = s[i] == '-'; i++; }

        // inf / nan
        if (i < n && (s[i] == 'i' || s[i] == 'I' || s[i] == 'n' || s[i] == 'N'))
        {
            if (MatchCI(s, i, "infinity")) { end = i + 8; return neg ? double.NegativeInfinity : double.PositiveInfinity; }
            if (MatchCI(s, i, "inf")) { end = i + 3; return neg ? double.NegativeInfinity : double.PositiveInfinity; }
            if (MatchCI(s, i, "nan"))
            {
                int j = i + 3;
                if (j < n && s[j] == '(')
                {
                    int k = j + 1;
                    while (k < n && (char.IsAsciiLetterOrDigit(s[k]) || s[k] == '_')) k++;
                    if (k < n && s[k] == ')') j = k + 1;
                }
                end = j;
                return neg ? -double.NaN : double.NaN;
            }
            return 0;
        }

        // hexadecimal floating point (UCRT strtod accepts it)
        if (i + 1 < n && s[i] == '0' && (s[i + 1] == 'x' || s[i + 1] == 'X'))
        {
            int j = i + 2;
            BigInteger mant = BigInteger.Zero;
            int exp2 = 0;
            bool any = false;
            while (j < n && Uri.IsHexDigit(s[j])) { mant = mant * 16 + HexVal(s[j]); j++; any = true; }
            if (j < n && s[j] == '.')
            {
                j++;
                while (j < n && Uri.IsHexDigit(s[j])) { mant = mant * 16 + HexVal(s[j]); exp2 -= 4; j++; any = true; }
            }
            if (any)
            {
                if (j < n && (s[j] == 'p' || s[j] == 'P'))
                {
                    int k = j + 1;
                    bool eneg = false;
                    if (k < n && (s[k] == '+' || s[k] == '-')) { eneg = s[k] == '-'; k++; }
                    if (k < n && char.IsAsciiDigit(s[k]))
                    {
                        int e = 0;
                        while (k < n && char.IsAsciiDigit(s[k])) { if (e < 100000) e = e * 10 + (s[k] - '0'); k++; }
                        exp2 += eneg ? -e : e;
                        j = k;
                    }
                }
                end = j;
                double hv = (double)mant * Math.Pow(2, exp2);
                return neg ? -hv : hv;
            }
            // "0x" without digits: the "0" is the number
            end = i + 1;
            return neg ? -0.0 : 0.0;
        }

        int digStart = i;
        int intDigits = 0, fracDigits = 0;
        while (i < n && char.IsAsciiDigit(s[i])) { i++; intDigits++; }
        if (i < n && s[i] == '.')
        {
            i++;
            while (i < n && char.IsAsciiDigit(s[i])) { i++; fracDigits++; }
        }
        if (intDigits + fracDigits == 0) { end = start; return 0; }
        int mantEnd = i;
        if (i < n && (s[i] == 'e' || s[i] == 'E'))
        {
            int k = i + 1;
            if (k < n && (s[k] == '+' || s[k] == '-')) k++;
            if (k < n && char.IsAsciiDigit(s[k]))
            {
                while (k < n && char.IsAsciiDigit(s[k])) k++;
                i = k;
            }
        }
        end = i;
        string num = s.Substring(digStart, i - digStart);
        if (num.Length > 0 && num[0] == '.') num = "0" + num;
        if (num.EndsWith('.')) num += "0";
        num = num.Replace(".e", ".0e").Replace(".E", ".0E");
        if (!double.TryParse(num, NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out double v))
            v = 0;
        _ = mantEnd;
        _ = signPos;
        return neg ? -v : v;
    }

    private static int HexVal(char c) => c <= '9' ? c - '0' : (c | 0x20) - 'a' + 10;

    private static bool MatchCI(string s, int i, string word) =>
        i + word.Length <= s.Length && string.Compare(s, i, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) == 0;

    /// <summary>C <c>atoi</c> (32-bit; overflow clamps like strtol).</summary>
    public static int Atoi(string? s) => (int)Strtol(s, 0, out _);

    /// <summary>C <c>atoi</c> starting at an index.</summary>
    public static int Atoi(string? s, int start) => (int)Strtol(s, start, out _);

    /// <summary>C <c>atol</c> (32-bit long on Windows).</summary>
    public static int Atol(string? s) => (int)Strtol(s, 0, out _);

    /// <summary>C <c>strtol</c> base 10, clamped to the int32 range.</summary>
    public static long Strtol(string? s, int start, out int end)
    {
        end = start;
        if (s == null) return 0;
        int i = start, n = s.Length;
        while (i < n && IsCSpace(s[i])) i++;
        bool neg = false;
        if (i < n && (s[i] == '+' || s[i] == '-')) { neg = s[i] == '-'; i++; }
        if (i >= n || !char.IsAsciiDigit(s[i])) return 0;
        long v = 0;
        while (i < n && char.IsAsciiDigit(s[i]))
        {
            if (v < 10000000000L) v = v * 10 + (s[i] - '0');
            i++;
        }
        end = i;
        if (neg) v = -v;
        if (v > int.MaxValue) return int.MaxValue;
        if (v < int.MinValue) return int.MinValue;
        return v;
    }

    /// <summary>C <c>strtoul</c> with base detection (base 0) or a given base; returns 0 when nothing parsed.</summary>
    public static uint Strtoul(string? s, int radix = 0)
    {
        if (s == null) return 0;
        int i = 0, n = s.Length;
        while (i < n && IsCSpace(s[i])) i++;
        bool neg = false;
        if (i < n && (s[i] == '+' || s[i] == '-')) { neg = s[i] == '-'; i++; }
        if ((radix == 0 || radix == 16) && i + 1 < n && s[i] == '0' && (s[i + 1] == 'x' || s[i + 1] == 'X')) { i += 2; radix = 16; }
        else if (radix == 0 && i < n && s[i] == '0') radix = 8;
        else if (radix == 0) radix = 10;
        ulong v = 0;
        bool overflow = false;
        while (i < n)
        {
            int d = char.IsAsciiDigit(s[i]) ? s[i] - '0' : char.IsAsciiLetter(s[i]) ? (s[i] | 0x20) - 'a' + 10 : 99;
            if (d >= radix) break;
            v = v * (ulong)radix + (ulong)d;
            if (v > uint.MaxValue) overflow = true;
            i++;
        }
        if (overflow) return uint.MaxValue;
        uint r = (uint)v;
        return neg ? unchecked((uint)-(int)r) : r;
    }

    // ------------------------------------------------------------------ printf

    /// <summary>C <c>sprintf</c> with MSVC UCRT semantics.</summary>
    public static string Sprintf(string format, params object?[] args)
    {
        var sb = new StringBuilder(format.Length + 16);
        int ai = 0;
        int i = 0, n = format.Length;
        while (i < n)
        {
            char c = format[i];
            if (c != '%') { sb.Append(c); i++; continue; }
            i++;
            if (i >= n) break;
            if (format[i] == '%') { sb.Append('%'); i++; continue; }

            bool left = false, plus = false, space = false, alt = false, zero = false;
            for (; i < n; i++)
            {
                char f = format[i];
                if (f == '-') left = true;
                else if (f == '+') plus = true;
                else if (f == ' ') space = true;
                else if (f == '#') alt = true;
                else if (f == '0') zero = true;
                else break;
            }
            int width = -1;
            if (i < n && format[i] == '*')
            {
                width = Convert.ToInt32(ai < args.Length ? args[ai++] : 0, CultureInfo.InvariantCulture);
                if (width < 0) { left = true; width = -width; }
                i++;
            }
            else
            {
                while (i < n && char.IsAsciiDigit(format[i])) { width = (width < 0 ? 0 : width) * 10 + (format[i] - '0'); i++; }
            }
            int prec = -1;
            if (i < n && format[i] == '.')
            {
                i++;
                prec = 0;
                if (i < n && format[i] == '*')
                {
                    prec = Convert.ToInt32(ai < args.Length ? args[ai++] : 0, CultureInfo.InvariantCulture);
                    if (prec < 0) prec = -1;
                    i++;
                }
                else
                    while (i < n && char.IsAsciiDigit(format[i])) { prec = prec * 10 + (format[i] - '0'); i++; }
            }
            // length modifiers
            bool is64 = false;
            while (i < n)
            {
                char l = format[i];
                if (l == 'h' || l == 'l' || l == 'L' || l == 'z' || l == 'j' || l == 't' || l == 'w') { if (l == 'l' && i + 1 < n && format[i + 1] == 'l') { is64 = true; i++; } i++; }
                else if (l == 'I' && i + 2 < n && format[i + 1] == '6' && format[i + 2] == '4') { is64 = true; i += 3; }
                else if (l == 'I' && i + 2 < n && format[i + 1] == '3' && format[i + 2] == '2') { i += 3; }
                else break;
            }
            if (i >= n) break;
            char conv = format[i++];
            object? arg = ai < args.Length ? args[ai++] : null;
            string body;
            string sign = "";
            bool numeric = true;
            switch (conv)
            {
                case 'd':
                case 'i':
                    {
                        long v = ToInt64(arg, is64);
                        if (v < 0) { sign = "-"; body = v == long.MinValue ? "9223372036854775808" : (-v).ToString(CultureInfo.InvariantCulture); }
                        else { sign = plus ? "+" : space ? " " : ""; body = v.ToString(CultureInfo.InvariantCulture); }
                        body = ApplyIntPrecision(body, prec);
                        break;
                    }
                case 'u':
                    body = ApplyIntPrecision(ToUInt64(arg, is64).ToString(CultureInfo.InvariantCulture), prec);
                    break;
                case 'x':
                case 'X':
                    {
                        ulong v = ToUInt64(arg, is64);
                        body = ApplyIntPrecision(v.ToString(conv == 'x' ? "x" : "X", CultureInfo.InvariantCulture), prec);
                        if (alt && v != 0) sign = conv == 'x' ? "0x" : "0X";
                        break;
                    }
                case 'o':
                    {
                        ulong v = ToUInt64(arg, is64);
                        body = ApplyIntPrecision(ToOctal(v), prec);
                        if (alt && body[0] != '0') body = "0" + body;
                        break;
                    }
                case 'f':
                case 'F':
                case 'e':
                case 'E':
                case 'g':
                case 'G':
                case 'a':
                case 'A':
                    {
                        double v = ToDouble(arg);
                        bool negv = double.IsNegative(v);
                        if (negv) sign = "-";
                        else sign = plus ? "+" : space ? " " : "";
                        if (double.IsNaN(v) || double.IsInfinity(v))
                        {
                            body = NonFinite(v, char.IsUpper(conv));
                            if (double.IsNaN(v)) sign = "";
                            zero = false;
                        }
                        else
                        {
                            double a = Math.Abs(v);
                            body = conv switch
                            {
                                'f' or 'F' => FormatFixed(a, prec < 0 ? 6 : prec, alt),
                                'e' or 'E' => FormatExp(a, prec < 0 ? 6 : prec, alt, conv == 'E'),
                                'a' or 'A' => FormatExp(a, prec < 0 ? 13 : prec, alt, conv == 'A'),
                                _ => FormatGeneral(a, prec < 0 ? 6 : prec, alt, conv == 'G'),
                            };
                        }
                        break;
                    }
                case 'c':
                    numeric = false;
                    body = arg is char ch ? ch.ToString() : arg is string cs && cs.Length > 0 ? cs[..1] : ((char)ToInt64(arg, false)).ToString();
                    break;
                case 's':
                case 'S':
                    numeric = false;
                    body = arg?.ToString() ?? "(null)";
                    if (prec >= 0 && body.Length > prec) body = body[..prec];
                    break;
                case 'p':
                    body = ToUInt64(arg, true).ToString("X16", CultureInfo.InvariantCulture);
                    break;
                default:
                    numeric = false;
                    body = conv.ToString();
                    break;
            }

            int len = sign.Length + body.Length;
            if (width > len)
            {
                if (left)
                    sb.Append(sign).Append(body).Append(' ', width - len);
                else if (zero && numeric && !(prec >= 0 && conv is 'd' or 'i' or 'u' or 'x' or 'X' or 'o'))
                    sb.Append(sign).Append('0', width - len).Append(body);
                else
                    sb.Append(' ', width - len).Append(sign).Append(body);
            }
            else
                sb.Append(sign).Append(body);
        }
        return sb.ToString();
    }

    private static string NonFinite(double v, bool upper)
    {
        string s;
        if (double.IsInfinity(v)) s = double.IsNegative(v) ? "-inf" : "inf";
        else
        {
            ulong bits = (ulong)BitConverter.DoubleToInt64Bits(v);
            bool neg = (bits >> 63) != 0;
            ulong mant = bits & 0x000FFFFFFFFFFFFFUL;
            if (neg && mant == 0x0008000000000000UL) s = "-nan(ind)";
            else if ((mant & 0x0008000000000000UL) == 0) s = neg ? "-nan(snan)" : "nan(snan)";
            else s = neg ? "-nan" : "nan";
        }
        return upper ? s.ToUpperInvariant() : s;
    }

    private static string ApplyIntPrecision(string digits, int prec)
    {
        if (prec < 0) return digits;
        if (prec == 0 && digits == "0") return "";
        return digits.Length < prec ? new string('0', prec - digits.Length) + digits : digits;
    }

    private static string ToOctal(ulong v)
    {
        if (v == 0) return "0";
        var sb = new StringBuilder();
        while (v != 0) { sb.Insert(0, (char)('0' + (int)(v & 7))); v >>= 3; }
        return sb.ToString();
    }

    private static long ToInt64(object? arg, bool is64)
    {
        long v = arg switch
        {
            null => 0,
            int x => x,
            uint x => x,
            long x => x,
            ulong x => unchecked((long)x),
            short x => x,
            ushort x => x,
            byte x => x,
            sbyte x => x,
            char x => x,
            bool b => b ? 1 : 0,
            double d => ValueTruncate(d),
            float f => ValueTruncate(f),
            _ => Convert.ToInt64(arg, CultureInfo.InvariantCulture),
        };
        return is64 || arg is long or ulong ? v : unchecked((int)v);
    }

    private static long ValueTruncate(double d) => double.IsNaN(d) || double.IsInfinity(d) ? 0 : (long)d;

    private static ulong ToUInt64(object? arg, bool is64)
    {
        ulong v = arg switch
        {
            null => 0,
            int x => unchecked((uint)x),
            uint x => x,
            long x => unchecked((ulong)x),
            ulong x => x,
            short x => unchecked((uint)x),
            ushort x => x,
            byte x => x,
            char x => x,
            bool b => b ? 1UL : 0UL,
            double d => unchecked((ulong)ValueTruncate(d)),
            float f => unchecked((ulong)ValueTruncate(f)),
            _ => Convert.ToUInt64(arg, CultureInfo.InvariantCulture),
        };
        return is64 || arg is long or ulong ? v : (uint)v;
    }

    private static double ToDouble(object? arg) => arg switch
    {
        null => 0,
        double d => d,
        float f => f,
        int x => x,
        uint x => x,
        long x => x,
        ulong x => x,
        short x => x,
        ushort x => x,
        byte x => x,
        decimal m => (double)m,
        _ => Convert.ToDouble(arg, CultureInfo.InvariantCulture),
    };

    // ------------------------------------------------------- exact decimal conversion

    private static readonly BigInteger[] Pow10Cache = BuildPow10(400);

    private static BigInteger[] BuildPow10(int n)
    {
        var a = new BigInteger[n];
        a[0] = BigInteger.One;
        for (int i = 1; i < n; i++) a[i] = a[i - 1] * 10;
        return a;
    }

    private static BigInteger Pow10(int k) => k < Pow10Cache.Length ? Pow10Cache[k] : BigInteger.Pow(10, k);

    /// <summary>Splits a finite positive double into mantissa * 2^exp.</summary>
    private static void Decompose(double a, out BigInteger mant, out int exp)
    {
        long bits = BitConverter.DoubleToInt64Bits(a);
        int e = (int)((bits >> 52) & 0x7FF);
        long m = bits & 0xFFFFFFFFFFFFFL;
        if (e == 0) e = 1; else m |= 1L << 52;
        exp = e - 1075;
        mant = m;
    }

    /// <summary>round(a * 10^p) half away from zero, exact.</summary>
    private static BigInteger ScaleRound(double a, int p)
    {
        Decompose(a, out var m, out int e);
        BigInteger num = m, den = BigInteger.One;
        if (e >= 0) num <<= e; else den <<= -e;
        if (p >= 0) num *= Pow10(p); else den *= Pow10(-p);
        BigInteger q = BigInteger.DivRem(num, den, out var r);
        int c = (r * 2).CompareTo(den);
        if (c > 0 || (c == 0 && (!RoundHalfEven || !q.IsEven))) q += 1;
        return q;
    }

    /// <summary>Decimal exponent X with 10^X ≤ a &lt; 10^(X+1) (a &gt; 0).</summary>
    private static int DecimalExponent(double a)
    {
        int x = (int)Math.Floor(Math.Log10(a));
        // correct the estimate exactly
        Decompose(a, out var m, out int e);
        BigInteger num = m, den = BigInteger.One;
        if (e >= 0) num <<= e; else den <<= -e;
        for (int guard = 0; guard < 4; guard++)
        {
            // compare a with 10^x
            int c = x >= 0 ? num.CompareTo(den * Pow10(x)) : (num * Pow10(-x)).CompareTo(den);
            if (c < 0) { x--; continue; }
            int c2 = x + 1 >= 0 ? num.CompareTo(den * Pow10(x + 1)) : (num * Pow10(-(x + 1))).CompareTo(den);
            if (c2 >= 0) { x++; continue; }
            break;
        }
        return x;
    }

    /// <summary>%.pf of a non-negative finite value.</summary>
    public static string FormatFixed(double a, int p, bool alt = false)
    {
        string digits;
        if (a == 0) digits = "0";
        else
        {
            if (a < 1e15 && a == Math.Floor(a) && p <= 20)
                digits = ((long)a).ToString(CultureInfo.InvariantCulture) + new string('0', p);
            else
                digits = ScaleRound(a, p).ToString(CultureInfo.InvariantCulture);
        }
        if (a == 0) digits = new string('0', p + 1);
        if (digits.Length <= p) digits = new string('0', p - digits.Length + 1) + digits;
        string ip = digits[..^p];
        if (p == 0) return alt ? ip + "." : ip;
        return ip + "." + digits[^p..];
    }

    /// <summary>Rounded mantissa digits (p+1 of them) and exponent for %e.</summary>
    private static void ExpDigits(double a, int p, out string digits, out int x)
    {
        if (a == 0) { digits = new string('0', p + 1); x = 0; return; }
        x = DecimalExponent(a);
        BigInteger q = ScaleRound(a, p - x);
        if (q >= Pow10(p + 1)) { x++; q = ScaleRound(a, p - x); }
        digits = q.ToString(CultureInfo.InvariantCulture);
        if (digits.Length < p + 1) digits = digits.PadLeft(p + 1, '0');
    }

    private static string ExpSuffix(int x, bool upper)
    {
        string ex = Math.Abs(x).ToString(CultureInfo.InvariantCulture);
        if (ex.Length < 2) ex = "0" + ex;
        return (upper ? "E" : "e") + (x < 0 ? "-" : "+") + ex;
    }

    /// <summary>%.pe of a non-negative finite value (UCRT: at least two exponent digits).</summary>
    public static string FormatExp(double a, int p, bool alt = false, bool upper = false)
    {
        ExpDigits(a, p, out string digits, out int x);
        string mant = p == 0 ? (alt ? digits + "." : digits) : digits[..1] + "." + digits[1..];
        return mant + ExpSuffix(x, upper);
    }

    /// <summary>%.pg of a non-negative finite value.</summary>
    public static string FormatGeneral(double a, int p, bool alt = false, bool upper = false)
    {
        if (p == 0) p = 1;
        ExpDigits(a, p - 1, out string digits, out int x);
        string s;
        if (x < p && x >= -4)
        {
            s = FormatFixed(a, p - 1 - x, alt);
            if (!alt && s.Contains('.')) s = s.TrimEnd('0').TrimEnd('.');
        }
        else
        {
            string mant = digits.Length > 1 ? digits[..1] + "." + digits[1..] : (alt ? digits + "." : digits);
            if (!alt && mant.Contains('.')) mant = mant.TrimEnd('0').TrimEnd('.');
            s = mant + ExpSuffix(x, upper);
        }
        return s;
    }

    /// <summary>
    /// MSVC <c>_gcvt(value, digits)</c>: <paramref name="digits"/> significant digits, %f style when the
    /// exponent fits, else exponential; a decimal point is always present in %f style ("1." for 1).
    /// </summary>
    public static string Gcvt(double value, int digits)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return NonFinite(value, false);
        string sign = value < 0 ? "-" : "";
        double a = Math.Abs(value);
        ExpDigits(a, digits - 1, out string dg, out int x);
        if (a == 0) return "0.";
        if (x >= -1 && x < digits)
        {
            string f = FormatFixed(a, Math.Max(0, digits - 1 - x));
            if (f.Contains('.')) f = f.TrimEnd('0');
            if (!f.Contains('.')) f += ".";
            return sign + f;
        }
        string m = dg[..1] + "." + dg[1..].TrimEnd('0');
        string ex = Math.Abs(x).ToString(CultureInfo.InvariantCulture).PadLeft(2, '0');
        return sign + m + "e" + (x < 0 ? "-" : "+") + ex;
    }

    /// <summary>Rounds to <paramref name="digits"/> significant digits as <c>atof(_gcvt(v, digits))</c>.</summary>
    public static double RoundSignificant(double value, int digits) => Atof(Gcvt(value, digits));
}
