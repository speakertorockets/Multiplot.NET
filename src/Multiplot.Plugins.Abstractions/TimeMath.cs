// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

namespace Multiplot.Plugins;

/// <summary>
/// TimeTag ↔ relative-seconds arithmetic and time-array search
/// (<c>GMT()</c>, <c>TIME()</c>, <c>TIME_location()</c>).
/// </summary>
public static class TimeMath
{
    /// <summary>Time matching tolerance (1 µs).</summary>
    public const double TimeTolerance = 0.000001;

    /// <summary>Relative seconds of <paramref name="t"/> from <paramref name="reference"/> (FR-TIM-001).</summary>
    public static double Time(TimeTag t, TimeTag reference)
    {
        int dayDiff = t.JulianDay - reference.JulianDay;
        if (Math.Abs(dayDiff) >= 180)
            dayDiff += dayDiff > 0 ? -365 : 365;
        double daySeconds = dayDiff * 86400.0;
        return daySeconds
            + (((t.MilliSeconds + (t.MicroSeconds / 1000.0))
                - (reference.MilliSeconds + (reference.MicroSeconds / 1000.0)))
               / 1000.0);
    }

    /// <summary>TimeTag at <paramref name="timeOffset"/> seconds from <paramref name="reference"/> (FR-TIM-002).</summary>
    public static TimeTag Gmt(double timeOffset, TimeTag reference, bool setMicro)
    {
        TimeTag g;
        bool negSign = timeOffset < 0.0;
        double time = Math.Abs(timeOffset) > 15552000.0 ? 15552000.0 : Math.Abs(timeOffset);
        int jdayOffset = (int)(time / 86400.0);
        if (jdayOffset != 0)
        {
            time = CFmod(time, 86400.0);
            if (negSign) jdayOffset = -jdayOffset;
        }
        time += .0000005;
        int tM = (int)(time / .001);
        int tU = (int)(CFmod(time, .001) * 1000000.0);
        if (!setMicro && tU > 500) tM += 1;

        g.JulianDay = reference.JulianDay;
        if (negSign)
        {
            tM = -tM;
            if (Math.Abs(tM) <= (int)reference.MilliSeconds)
                g.MilliSeconds = unchecked((uint)(reference.MilliSeconds + tM));
            else
            {
                g.MilliSeconds = unchecked((uint)((reference.MilliSeconds + 86400000L) + tM));
                g.JulianDay--;
            }
            if (setMicro)
            {
                if (tU <= reference.MicroSeconds)
                    g.MicroSeconds = (ushort)(reference.MicroSeconds - (ushort)tU);
                else
                {
                    g.MicroSeconds = unchecked((ushort)((reference.MicroSeconds + 1000) - (ushort)tU));
                    if (g.MilliSeconds != 0)
                        g.MilliSeconds--;
                    else
                    {
                        g.MilliSeconds += 86400000;
                        g.MilliSeconds--;
                        g.JulianDay--;
                    }
                }
            }
            else
                g.MicroSeconds = 0;
        }
        else
        {
            g.MilliSeconds = unchecked((uint)(reference.MilliSeconds + tM));
            if (setMicro)
            {
                g.MicroSeconds = unchecked((ushort)(reference.MicroSeconds + (ushort)tU));
                if (g.MicroSeconds >= 1000)
                {
                    g.MilliSeconds++;
                    g.MicroSeconds -= 1000;
                }
            }
            else
                g.MicroSeconds = 0;

            if (g.MilliSeconds >= 86400000)
            {
                g.JulianDay++;
                g.MilliSeconds -= 86400000;
            }
        }

        g.JulianDay = unchecked((short)(g.JulianDay + jdayOffset));
        if (g.JulianDay < 1) g.JulianDay += 365;
        if (g.JulianDay > 365) g.JulianDay %= 365;
        return g;
    }

    /// <summary>C <c>fmod</c> (truncated remainder; same as the C# % operator on doubles).</summary>
    public static double CFmod(double x, double y) => x % y;

    /// <summary>
    /// Binary search of a float time array. Mode −1: first point ≥ t,
    /// +1: last point ≤ t, 0: closest. Returns 0 for an empty array.
    /// </summary>
    public static int Location(double tpoint, float[] index, int np, int mode, int offset = 0)
    {
        if (np <= 0) return 0;
        uint lbound = 0, ubound = (uint)np - 1;
        uint i = (uint)np / 2;
        while (true)
        {
            double v = index[offset + i];
            if (Math.Abs(tpoint - v) < TimeTolerance)
            {
                lbound = ubound = i;
                break;
            }
            else if (tpoint > v)
                lbound = i;
            else
                ubound = i;
            if ((ubound - lbound) <= 1) break;
            i = lbound + ((ubound - lbound) / 2);
        }

        switch (ubound - lbound)
        {
            case 0:
                return (int)lbound;
            case 1:
                double lv = index[offset + lbound], uv = index[offset + ubound];
                switch (mode)
                {
                    case 0:
                        return (tpoint >= uv) || (Math.Abs(uv - tpoint) < Math.Abs(lv - tpoint)) ? (int)ubound : (int)lbound;
                    case -1:
                    case -2:
                        if (tpoint <= lv) return (int)lbound;
                        if (Math.Abs(tpoint - lv) < TimeTolerance) return (int)lbound;
                        return (int)ubound;
                    case 1:
                    case 2:
                        if (tpoint >= uv) return (int)ubound;
                        if (Math.Abs(tpoint - uv) < TimeTolerance) return (int)ubound;
                        return (int)lbound;
                    default:
                        return (int)lbound; // otherwise undefined; lower bound
                }
            default:
                return (int)i;
        }
    }

    /// <summary>Binary search of a GMT array relative to <paramref name="refGmt"/>.</summary>
    public static int GmtLocation(double tpoint, TimeTag[] gmt, int offset, int np, TimeTag refGmt, int mode)
    {
        if (np <= 0) return 0;
        uint lbound = 0, ubound = (uint)np - 1;
        uint thePoint = (uint)np / 2;
        while (true)
        {
            double t = Time(gmt[offset + thePoint], refGmt);
            if (tpoint > t)
                lbound = thePoint;
            else if (tpoint < t)
                ubound = thePoint;
            else
                break;

            if ((ubound - lbound) <= 1)
            {
                double tl = Time(gmt[offset + lbound], refGmt);
                double tu = Time(gmt[offset + ubound], refGmt);
                switch (mode)
                {
                    case 0:
                        thePoint = Math.Abs(tu - tpoint) < Math.Abs(tl - tpoint) ? ubound : lbound;
                        break;
                    case -1:
                        if (tpoint <= tl) thePoint = lbound;
                        else if (Math.Abs(tpoint - tl) < TimeTolerance) thePoint = lbound;
                        else thePoint = ubound;
                        break;
                    case 1:
                        if (tpoint >= tu) thePoint = ubound;
                        else if (Math.Abs(tpoint - tu) < TimeTolerance) thePoint = ubound;
                        else thePoint = lbound;
                        break;
                }
                break;
            }
            thePoint = lbound + ((ubound - lbound) / 2);
        }
        return (int)thePoint;
    }
}
