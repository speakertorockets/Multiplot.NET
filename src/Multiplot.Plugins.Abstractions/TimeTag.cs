// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;

namespace Multiplot.Plugins;

/// <summary>
/// Absolute time of day without year: Julian day, milliseconds since midnight and microseconds
/// (8 bytes, field order MilliSeconds, MicroSeconds, JulianDay).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct TimeTag : IEquatable<TimeTag>
{
    /// <summary>Milliseconds since midnight.</summary>
    public uint MilliSeconds;
    /// <summary>Microseconds word.</summary>
    public ushort MicroSeconds;
    /// <summary>Julian day of the time stamp.</summary>
    public short JulianDay;

    /// <summary>Creates a time tag.</summary>
    public TimeTag(short julianDay, uint milliSeconds, ushort microSeconds = 0)
    {
        JulianDay = julianDay;
        MilliSeconds = milliSeconds;
        MicroSeconds = microSeconds;
    }

    /// <inheritdoc />
    public readonly bool Equals(TimeTag other) =>
        MilliSeconds == other.MilliSeconds && MicroSeconds == other.MicroSeconds && JulianDay == other.JulianDay;

    /// <inheritdoc />
    public override readonly bool Equals(object? obj) => obj is TimeTag t && Equals(t);

    /// <inheritdoc />
    public override readonly int GetHashCode() => HashCode.Combine(MilliSeconds, MicroSeconds, JulianDay);

    /// <summary>Equality.</summary>
    public static bool operator ==(TimeTag a, TimeTag b) => a.Equals(b);

    /// <summary>Inequality.</summary>
    public static bool operator !=(TimeTag a, TimeTag b) => !a.Equals(b);

    /// <summary>Display form <c>ddd/hh:mm:ss.mmm</c>.</summary>
    public override readonly string ToString()
    {
        uint ms = MilliSeconds;
        return $"{JulianDay:000}/{ms / 3600000:00}:{ms / 60000 % 60:00}:{ms / 1000 % 60:00}.{ms % 1000:000}";
    }
}
