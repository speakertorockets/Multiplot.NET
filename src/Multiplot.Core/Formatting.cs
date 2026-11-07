// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

using Multiplot.Plugins;

namespace Multiplot.Core;

/// <summary>Tabular / export value formats: HEX2=-7 … GENERAL=-1, 0..7 fixed point.</summary>
public static class TabFormat
{
    /// <summary>HEX2.</summary>
    public const int Hex2 = -7;
    /// <summary>HEX4.</summary>
    public const int Hex4 = -6;
    /// <summary>HEX8.</summary>
    public const int Hex8 = -5;
    /// <summary>OCT3.</summary>
    public const int Oct3 = -4;
    /// <summary>OCT6.</summary>
    public const int Oct6 = -3;
    /// <summary>OCT12.</summary>
    public const int Oct12 = -2;
    /// <summary>GENERAL.</summary>
    public const int General = -1;
    /// <summary>FIXEDPOINT (0 decimals; 0..7).</summary>
    public const int FixedPoint = 0;

    /// <summary>Tabular format names indexed by format + 7.</summary>
    public static readonly string[] Names = { "HEX2", "HEX4", "HEX8", "OCT3", "OCT6", "OCT12", "GENERAL", "0", "1", "2", "3", "4", "5", "6", "7" };

    /// <summary>Name of a format value.</summary>
    public static string NameOf(int f) => f >= -7 && f <= 7 ? Names[f + 7] : "GENERAL";

    /// <summary>Parses a format name (HEX2…GENERAL, 0..7); null when unknown.</summary>
    public static int? Parse(string s)
    {
        s = s.Trim().ToUpperInvariant();
        for (int i = 0; i < Names.Length; i++)
            if (Names[i] == s) return i - 7;
        return null;
    }
}

/// <summary>Value and time formatting shared by export, tabular display and statistics (FR-XPT-001).</summary>
public static class ValueFormat
{
    private static uint CULong(float f)
    {
        // (ULONG)float on MSVC x64: conversion through int64 then truncation to 32 bits
        if (float.IsNaN(f) || float.IsInfinity(f) || f >= 9.2233720368547758E18f || f <= -9.2233720368547758E18f) return 0;
        return unchecked((uint)(long)f);
    }

    /// <summary>Formats a value with a tabular format.</summary>
    public static string FormatValue(double dVal, int iFormat, bool bExp = false)
    {
        float fVal = (float)dVal;
        if (BitConverter.SingleToUInt32Bits(fVal) == DataTypes.DropOutBits) return "DROP_OUT";
        return iFormat switch
        {
            TabFormat.Hex2 => Crt.Sprintf("0x%02x", CULong(fVal) & 0xff),
            TabFormat.Hex4 => Crt.Sprintf("0x%04x", CULong(fVal) & 0xffff),
            TabFormat.Hex8 => Crt.Sprintf("0x%08x", CULong(fVal)),
            TabFormat.Oct3 => Crt.Sprintf("0d%03o", CULong(fVal) & 0xff),
            TabFormat.Oct6 => Crt.Sprintf("0d%06o", CULong(fVal) & 0xffff),
            TabFormat.Oct12 => Crt.Sprintf("0d%012o", CULong(fVal)),
            >= 0 and <= 7 => Crt.Sprintf(bExp ? "%.*e" : "%.*f", iFormat, dVal),
            _ => Crt.Sprintf("%.*g", 17, dVal),
        };
    }

    /// <summary>Float overload (value widened to double).</summary>
    public static string FormatValue(float fVal, int iFormat, bool bExp = false) => FormatValue((double)fVal, iFormat, bExp);

    /// <summary>Null/empty format gives <c>%03d/%02u:%02u:%02u.%03u</c>.</summary>
    public static string ClockString(TimeTag t, string? format = null)
    {
        uint ms = t.MilliSeconds;
        if (!string.IsNullOrEmpty(format))
        {
            if (format.Contains('/'))
                return Crt.Sprintf(format, (int)t.JulianDay, (ms / 3600000U) % 24, (ms % 3600000U) / 60000U, (ms % 60000U) / 1000U, ms % 1000U, (uint)t.MicroSeconds);
            return Crt.Sprintf(format, (ms / 3600000U) % 24, (ms % 3600000U) / 60000U, (ms % 60000U) / 1000U, ms % 1000U, (uint)t.MicroSeconds);
        }
        return Crt.Sprintf("%03d/%02u:%02u:%02u.%03u", (int)t.JulianDay, (ms / 3600000U) % 24, (ms % 3600000U) / 60000U, (ms % 60000U) / 1000U, ms % 1000U);
    }

    /// <summary>Clock string to TimeTag (requires <c>ddd/hr[:mn[:sc[.mil]]]</c>); null on failure.</summary>
    public static TimeTag? ParseClock(string s)
    {
        int slash = s.IndexOf('/');
        if (slash < 0) return null;
        int day = Crt.Atoi(s);
        if (day < 1 || day > 366) return null;
        var t = new TimeTag((short)day, 0, 0);
        string r = s[(slash + 1)..];
        t.MilliSeconds = unchecked((uint)(Crt.Atoi(r) * 3600000));
        int p = r.IndexOf(':');
        if (p >= 0)
        {
            r = r[(p + 1)..];
            t.MilliSeconds += unchecked((uint)(Crt.Atoi(r) * 60000));
            p = r.IndexOf(':');
            if (p >= 0)
            {
                r = r[(p + 1)..];
                t.MilliSeconds += unchecked((uint)(Crt.Atoi(r) * 1000));
                p = r.IndexOf('.');
                if (p >= 0) t.MilliSeconds += unchecked((uint)Crt.Atoi(r[(p + 1)..]));
            }
        }
        return t;
    }

    /// <summary>Time stamp text: <c>h:mm:ssAM 10/16/2026</c> (plot creation stamp).</summary>
    public static string FormatTimeStamp(DateTime now)
    {
        string s = now.Hour > 11
            ? Crt.Sprintf("%d:%02d:%02dPM", now.Hour == 12 ? 12 : now.Hour - 12, now.Minute, now.Second)
            : Crt.Sprintf("%d:%02d:%02dAM", now.Hour != 0 ? now.Hour : 12, now.Minute, now.Second);
        return s + now.ToString(" MM/dd/yyyy", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Strips a <c>\GROUP</c> scale group specifier.</summary>
    public static string StripGroupSpec(string s)
    {
        int i = s.IndexOf('\\');
        return i >= 0 ? s[..i] : s;
    }
}
