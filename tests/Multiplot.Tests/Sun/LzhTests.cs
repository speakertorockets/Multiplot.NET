using Multiplot.Plugins;
using Multiplot.Sun;

namespace Multiplot.Tests.Sun;

public class LzhTests
{
    private static byte[] MakeBuffer(Random r, int size, int kind)
    {
        var b = new byte[size];
        switch (kind)
        {
            case 0: r.NextBytes(b); break; // incompressible
            case 1: for (int i = 0; i < size; i++) b[i] = (byte)(i % 7); break; // repetitive
            case 2: Array.Fill(b, (byte)0x41); break; // single symbol
            default:
                {
                    // float-like data: slowly varying values
                    var f = new float[size / 4 + 1];
                    double v = 0;
                    for (int i = 0; i < f.Length; i++) { v += r.NextDouble() - 0.5; f[i] = (float)v; }
                    Buffer.BlockCopy(f, 0, b, 0, size);
                    break;
                }
        }
        return b;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RoundTrip_VariousSizes(int kind)
    {
        var r = new Random(1234 + kind);
        int[] sizes = { 0, 1, 2, 3, 7, 255, 256, 257, 4095, 8191, 8192, 8193, 20000, 65536, 300001 };
        foreach (int size in sizes)
        {
            var src = MakeBuffer(r, size, kind);
            var comp = LzhEncoder.EncodeAll(src);
            if (size == 0) continue;
            var back = LzhDecoder.DecodeAll(comp, size);
            Assert.Equal(src, back);
        }
    }

    [Fact]
    public void RoundTrip_Random1000()
    {
        var r = new Random(42);
        for (int t = 0; t < 1000; t++)
        {
            int size = r.Next(1, t < 950 ? 5000 : 200000);
            var src = MakeBuffer(r, size, r.Next(4));
            var comp = LzhEncoder.EncodeAll(src);
            Assert.Equal(src, LzhDecoder.DecodeAll(comp, size));
        }
    }

    [Fact]
    public void WindowedDecode_ReturnsRequestedSlice()
    {
        var r = new Random(7);
        var src = MakeBuffer(r, 50000, 3);
        var comp = LzhEncoder.EncodeAll(src);
        foreach (var (a, b) in new[] { (0, 10), (100, 200), (8190, 8200), (49990, 50000), (12345, 40000) })
        {
            var dst = new byte[b - a];
            using var ms = new MemoryStream(comp);
            Assert.Equal(SunError.Success, new LzhDecoder().Decode(ms, comp.Length, dst, 0, a, b));
            Assert.Equal(src.AsSpan(a, b - a).ToArray(), dst);
        }
    }

    [Fact]
    public void Fuzz_CorruptStreamsNeverThrowUnexpected()
    {
        var r = new Random(99);
        for (int t = 0; t < 2000; t++)
        {
            var src = MakeBuffer(r, r.Next(1, 20000), r.Next(4));
            var comp = LzhEncoder.EncodeAll(src);
            int mode = r.Next(3);
            if (mode == 0 && comp.Length > 1) Array.Resize(ref comp, r.Next(1, comp.Length));
            else if (mode == 1) for (int k = 0; k < 1 + r.Next(10); k++) comp[r.Next(comp.Length)] ^= (byte)(1 << r.Next(8));
            else r.NextBytes(comp);
            var dst = new byte[src.Length];
            using var ms = new MemoryStream(comp);
            var err = new LzhDecoder().Decode(ms, comp.Length, dst, 0, 0, dst.Length);
            Assert.True(err is SunError.Success or SunError.LzwDict or SunError.LzwReadFail);
        }
    }
}
