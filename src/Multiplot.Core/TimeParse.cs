// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

using Multiplot.Plugins;

namespace Multiplot.Core;

/// <summary>Time string conversions.</summary>
public static class TimeParse
{
    private static bool InRange(int v, int lo, int hi) => v >= lo && v <= hi;

    /// <summary>
    /// Clock string to TimeTag: <c>ddd/hr:mn:sc.mil.mic</c>; parsing stops at the first
    /// missing or out-of-range field. A day outside 1..366 yields an all-zero TimeTag.
    /// </summary>
    public static TimeTag ClockString(string s)
    {
        var t = new TimeTag();
        int day = Crt.Atoi(s);
        if (!InRange(day, 1, 366)) return t;
        t.JulianDay = (short)day;
        int p = s.IndexOf('/');
        if (p < 0) return t;
        string r = s[(p + 1)..];
        int v = Crt.Atoi(r);
        if (!InRange(v, 0, 59)) return t;
        t.MilliSeconds = unchecked((uint)(v * 3600000));
        p = r.IndexOf(':');
        if (p < 0) return t;
        r = r[(p + 1)..];
        v = Crt.Atoi(r);
        if (!InRange(v, 0, 59)) return t;
        t.MilliSeconds += unchecked((uint)(v * 60000));
        p = r.IndexOf(':');
        if (p < 0) return t;
        r = r[(p + 1)..];
        v = Crt.Atoi(r);
        if (!InRange(v, 0, 59)) return t;
        t.MilliSeconds += unchecked((uint)(v * 1000));
        p = r.IndexOf('.');
        if (p < 0) return t;
        r = r[(p + 1)..];
        v = Crt.Atoi(r);
        if (!InRange(v, 0, 999)) return t;
        t.MilliSeconds += (uint)v;
        p = r.IndexOf('.');
        if (p < 0) return t;
        r = r[(p + 1)..];
        v = Crt.Atoi(r);
        if (InRange(v, 0, 999)) t.MicroSeconds = (ushort)v;
        return t;
    }

    /// <summary>
    /// Event value parsing of <c>getEvents</c>: clock format if it contains ':', else
    /// <c>ddd/msec[.usec]</c>.
    /// </summary>
    public static TimeTag EventValue(string p)
    {
        if (p.Contains(':')) return ClockString(p);
        string m, u;
        int slash = p.IndexOf('/');
        if (slash >= 0)
        {
            m = p[(slash + 1)..];
            int dot = m.IndexOf('.');
            u = dot >= 0 ? m[(dot + 1)..] : "0";
        }
        else
        {
            m = "0";
            u = "0";
        }
        return new TimeTag(unchecked((short)Crt.Atoi(p)), unchecked((uint)Crt.Atol(m)), unchecked((ushort)Crt.Atoi(u)));
    }

    /// <summary>Formats a UTC as <c>%03u/%02u:%02u:%02u.%03u</c> (the default UTC format).</summary>
    public static string FormatUtc(TimeTag t) => Crt.Sprintf("%03u/%02u:%02u:%02u.%03u",
        (uint)t.JulianDay, t.MilliSeconds / 3600000, t.MilliSeconds / 60000 % 60, t.MilliSeconds / 1000 % 60, t.MilliSeconds % 1000);
}
