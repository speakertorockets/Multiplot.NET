using System.Runtime.InteropServices;
using System.Text;
using Multiplot.Core;

namespace Multiplot.Tests.Core;

/// <summary>Compares the managed C runtime emulation with the Windows Universal CRT (ucrtbase.dll).</summary>
[Collection("Crt")]
public unsafe class CrtTests
{
    private const ulong StandardRounding = 1UL << 5;

    [DllImport("ucrtbase.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int __stdio_common_vsprintf(ulong options, byte* buffer, nuint count, byte* format, IntPtr locale, ulong* args);

    [DllImport("ucrtbase.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern double atof(string s);

    [DllImport("ucrtbase.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int atoi(string s);

    [DllImport("ucrtbase.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern byte* _gcvt(double v, int digits, byte* buf);

    private static string Ucrt(string fmt, ulong options, params ulong[] args)
    {
        var buf = new byte[512];
        var f = Encoding.ASCII.GetBytes(fmt + "\0");
        fixed (byte* pb = buf, pf = f)
        fixed (ulong* pa = args)
        {
            int n = __stdio_common_vsprintf(options, pb, (nuint)buf.Length, pf, IntPtr.Zero, pa);
            return Encoding.ASCII.GetString(buf, 0, Math.Max(0, n));
        }
    }

    private static ulong D(double v) => (ulong)BitConverter.DoubleToInt64Bits(v);

    private static IEnumerable<double> Values()
    {
        var r = new Random(77);
        double[] fixedVals = { 0, -0.0, 1, -1, 0.5, 1.5, 2.5, 0.125, 0.375, 1e-5, 1.0e-4, 123456789, 1e15, 1e16, 1e21, 1e22, 3.4e38, -3.4e38,
            1.40129846e-45, double.Epsilon, 0.1, 0.2, 0.3, 2.675, 1.005, 9.9995, 99.995, 999999.5, 0.00001234, 100, 1000000, 1e100, 1e-100, 5e-324 };
        foreach (var v in fixedVals) yield return v;
        for (int i = 0; i < 3000; i++)
        {
            double v = (r.NextDouble() - 0.5) * Math.Pow(10, r.Next(-12, 20));
            if (i % 4 == 0) v = Math.Round(v, r.Next(0, 6));
            if (i % 9 == 0) v = (float)v;
            yield return v;
        }
    }

    [Fact]
    public void FloatingFormats_MatchUcrt()
    {
        string[] formats = { "%g", "%.17g", "%.3f", "%f", "%.0f", "%e", "%.3e", "%.9f", "%.*f", "%12.4f", "%-10.2e|", "%+g", "%.1f", "%#g", "%.7g", "%E", "%G" };
        foreach (bool even in new[] { true, false })
        {
            Crt.RoundHalfEven = even;
            ulong opt = even ? StandardRounding : 0;
            foreach (double v in Values())
            {
                foreach (string f in formats)
                {
                    string expected = f == "%.*f" ? Ucrt(f, opt, 9, D(v)) : Ucrt(f, opt, D(v));
                    string actual = f == "%.*f" ? Crt.Sprintf(f, 9, v) : Crt.Sprintf(f, v);
                    Assert.True(expected == actual, $"{f} {v:R} even={even}: ucrt '{expected}' managed '{actual}'");
                }
            }
        }
        Crt.RoundHalfEven = true;
        Assert.Equal("1.000000e+100", Ucrt("%e", StandardRounding, D(1e100)));
        Assert.Equal("2", Ucrt("%.0f", StandardRounding, D(2.5)));
        Assert.Equal("3", Ucrt("%.0f", 0, D(2.5)));
        Assert.Equal("2", Crt.Sprintf("%.0f", 2.5));
        Assert.Equal("-3.4e+38", Crt.Sprintf("%g", -3.4e38));
    }

    [Fact]
    public void IntegerFormats_MatchUcrt()
    {
        string[] formats = { "%d", "%u", "%x", "%X", "%08x", "%03u", "%02u", "%5d", "%-5d|", "%o", "%012o", "%#x", "%+d", "%.3d" };
        int[] vals = { 0, 1, -1, 7, 255, 65535, 123456, -123456, int.MaxValue, int.MinValue, 86399999 };
        foreach (int v in vals)
            foreach (string f in formats)
                Assert.Equal(Ucrt(f, StandardRounding, unchecked((ulong)(long)v)), Crt.Sprintf(f, v));
    }

    [Fact]
    public void Parsing_MatchesUcrt()
    {
        string[] inputs = { "", " ", "12", "  12abc", "-3.5e2x", "1e", "1e+", ".5", "5.", "-.5e-3", "+7", "0x1A", "0x", "inf", "-INFINITY", "nan", "1.#QNAN",
            "3.4e+038", "-3.4e+038", "  \t 42", "1,5", "1e400", "1e-400", "12:00:00", "123/456", "0.000001", "9999999999", "-9999999999" };
        foreach (string s in inputs)
        {
            double e = atof(s), a = Crt.Atof(s);
            Assert.True(e.Equals(a) || (double.IsNaN(e) && double.IsNaN(a)), $"atof('{s}') ucrt {e:R} managed {a:R}");
            Assert.Equal(atoi(s), Crt.Atoi(s));
        }
    }

    [Fact]
    public void Gcvt_RoundsLikeUcrt()
    {
        var buf = new byte[64];
        foreach (double v in Values())
        {
            if (double.IsNaN(v)) continue;
            fixed (byte* pb = buf)
            {
                _gcvt(v, 7, pb);
                string s = Marshal.PtrToStringAnsi((IntPtr)pb)!;
                Assert.Equal(atof(s), Crt.RoundSignificant(v, 7));
            }
        }
    }
}
