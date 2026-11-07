// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

namespace Multiplot.Core;

/// <summary>
/// SUN file signatures as little-endian uint32 values (bytes 0..3 read on a PC), §3.1.
/// </summary>
public static class SunSignatures
{
    /// <summary>Format mask (on disk FF FF FF F0).</summary>
    public const uint SunFormat = 0xF0FFFFFF;
    /// <summary>SUN_COMPRESSED.</summary>
    public const uint Compressed = 0xFFFFFFFF;
    /// <summary>SUN_UNCOMPRESSED.</summary>
    public const uint Uncompressed = 0xF0FFFFFF;
    /// <summary>SUN_CIRCULAR.</summary>
    public const uint Circular = 0xF1FFFFFF;
    /// <summary>SUN_MULTITIME.</summary>
    public const uint MultiTime = 0xF2FFFFFF;
    /// <summary>SUN_COMMONTIME.</summary>
    public const uint CommonTime = 0xF3FFFFFF;
    /// <summary>SUN_MULTITIME64.</summary>
    public const uint MultiTime64 = 0xF4FFFFFF;
    /// <summary>SUN_MULTITIME_SRT.</summary>
    public const uint MultiTimeSrt = 0xF5FFFFFF;
    /// <summary>SUN_MULTITIME64_SRT.</summary>
    public const uint MultiTime64Srt = 0xF6FFFFFF;

    /// <summary>PC GOAL real-time stream signature (plugin provided).</summary>
    public const uint PcGoalRealtime = 0xFFF0FFFF;

    /// <summary>True for any SUN signature.</summary>
    public static bool IsSun(uint sig) => (sig & SunFormat) == SunFormat;

    /// <summary>True for MULTITIME formats (incl. 64-bit and _SRT).</summary>
    public static bool IsMultiTime(uint sig) => sig is MultiTime or MultiTime64 or MultiTimeSrt or MultiTime64Srt;

    /// <summary>True for 64-bit link formats.</summary>
    public static bool Is64(uint sig) => sig is MultiTime64 or MultiTime64Srt;

    /// <summary>True for common-time formats whose parameters share the file TIME index.</summary>
    public static bool IsCommonTime(uint sig) => sig is Compressed or CommonTime or Uncompressed or Circular;

    /// <summary>File type description.</summary>
    public static string Describe(uint sig) => sig switch
    {
        Compressed => "SUN_COMPRESSED",
        Uncompressed => "SUN_NONCOMPRESSED",
        Circular => "SUN_CIRCULAR",
        MultiTime => "SUN_MULTITIME",
        MultiTime64 => "SUN_MULTITIME64",
        CommonTime => "SUN_COMMONTIME",
        _ => "unknown",
    };
}
