using System.Runtime.InteropServices;

namespace SampleMultiGen;

/// <summary>TimeTag from SunAcc42.h (8 bytes): milliseconds since midnight, microseconds, Julian day.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct TimeTag
{
    public uint MilliSeconds;
    public ushort MicroSeconds;
    public short JulianDay;
}

/// <summary>P/Invoke declarations for the write side of the native SunAcc42.dll (v4.2.x, x64, cdecl).</summary>
internal static class SunAcc
{
    private const string Dll = "SunAcc42.dll";

    public const uint SUN_MULTITIME = 0xf2ffffff;
    public const uint SUN_MULTITIME64 = 0xf4ffffff;

    public const int CREATE = 0x00;
    public const int OVERWRITE = 0x01;

    public const byte LZH_COMPRESS = 0x01;
    public const byte PACK_TIMETAG = 0x04;

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr wrtVersion();

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int wrtStatus();

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, BestFitMapping = false)]
    public static extern IntPtr createSunFile(string fileName, int writeMode, uint commentBlockSZ, uint nItems, uint nPoints, uint formatType);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, BestFitMapping = false)]
    public static extern unsafe int addSunParm(IntPtr pSunWrt, string n1, string n2, string u, string d, int sn,
        string dataID, void* dataBuf, string indexID, void* indexBuf, uint pCount, byte options, string? indexNomen);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int closeSunFile(IntPtr pSunWrt);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, BestFitMapping = false)]
    public static extern int setCommentText(IntPtr pSunWrt, string commentText);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, BestFitMapping = false)]
    public static extern int h_setTitle(IntPtr pSunWrt, string text);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, BestFitMapping = false)]
    public static extern int h_setComment(IntPtr pSunWrt, string text);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, BestFitMapping = false)]
    public static extern int h_setSerialNum(IntPtr pSunWrt, string text);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, BestFitMapping = false)]
    public static extern int h_setTestStand(IntPtr pSunWrt, string text);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, BestFitMapping = false)]
    public static extern int h_setTestType(IntPtr pSunWrt, string text);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, BestFitMapping = false)]
    public static extern int h_setTestNumb(IntPtr pSunWrt, string text);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, BestFitMapping = false)]
    public static extern int h_setTestPos(IntPtr pSunWrt, string text);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int h_setRefTime(IntPtr pSunWrt, uint yr, uint day, uint hr, uint min, uint sec, uint mil);

    public static string Version => Marshal.PtrToStringAnsi(wrtVersion()) ?? "?";

    /// <summary>Throws when a SunAcc write call returned a nonzero status.</summary>
    public static void Check(int status, string what)
    {
        if (status != 0)
            throw new InvalidOperationException($"SunAcc {what} failed: status {status} (wrtStatus {wrtStatus()})");
    }
}
