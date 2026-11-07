// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

using Multiplot.Plugins;

namespace Multiplot.Core;

/// <summary>
/// Time slice / reference resolution: reference UTC and TIME, cutoff and start/stop times
/// (FR-LOAD-020, FR-LOAD-021).
/// </summary>
public static class TimeReference
{
    /// <summary>UTC for a clock/TimeTag string or event name; null if not a UTC.</summary>
    public static TimeTag? GetRefUtc(IReadOnlyList<IDataFile> files, int current, string ev, bool strict = false)
    {
        var f = files[current];
        if (ev.Contains(':'))
        {
            var t = new TimeTag();
            string p = ev;
            if (ev.Contains('/'))
            {
                t.JulianDay = unchecked((short)Crt.Atoi(p));
                p = p[(p.IndexOf('/') + 1)..];
            }
            else
                t.JulianDay = unchecked((short)f.Info.RefTime[1]);
            t.MilliSeconds = unchecked((uint)(Crt.Atoi(p) * 3600000));
            int c = p.IndexOf(':');
            if (c >= 0)
            {
                p = p[(c + 1)..];
                t.MilliSeconds += unchecked((uint)(Crt.Atoi(p) * 60000));
                c = p.IndexOf(':');
                if (c >= 0)
                {
                    p = p[(c + 1)..];
                    t.MilliSeconds += unchecked((uint)(Crt.Atoi(p) * 1000));
                    c = p.IndexOf('.');
                    if (c >= 0)
                    {
                        p = p[(c + 1)..];
                        t.MilliSeconds += unchecked((uint)Crt.Atoi(p));
                        c = p.IndexOf('.');
                        if (c >= 0) t.MicroSeconds = unchecked((ushort)Crt.Atoi(p[(c + 1)..]));
                    }
                }
            }
            return t;
        }
        if (string.Equals(ev, "NONE", StringComparison.OrdinalIgnoreCase)) return null;
        int slash = ev.IndexOf('/');
        if (slash >= 0)
        {
            var t = new TimeTag(unchecked((short)Crt.Atoi(ev)), unchecked((uint)Crt.Atol(ev[(slash + 1)..])), 0);
            string rest = ev[(slash + 1)..];
            int dot = rest.IndexOf('.');
            if (dot >= 0) t.MicroSeconds = unchecked((ushort)Crt.Atoi(rest[(dot + 1)..]));
            return t;
        }
        foreach (var e in f.Events)
            if (string.Equals(ev, e.Name, StringComparison.OrdinalIgnoreCase)) return e.Utc;
        bool hasTimeTags = f.Parameters.Count > 1 && f.Parameters[1].TypeId == "t8";
        if (!(strict && !hasTimeTags))
            return LastMatch(files, current, ev); // only the inner loop breaks: the last matching file wins
        return null;
    }

    private static TimeTag? LastMatch(IReadOnlyList<IDataFile> files, int current, string ev)
    {
        TimeTag? found = null;
        for (int j = 0; j < files.Count; j++)
        {
            if (j == current) continue;
            foreach (var e in files[j].Events)
                if (string.Equals(ev, e.Name, StringComparison.OrdinalIgnoreCase)) { found = e.Utc; break; }
        }
        return found;
    }

    /// <summary>Cutoff time.</summary>
    public static float GetCutoff(IReadOnlyList<IDataFile> files, int current)
    {
        if (files[current].Info.Cutoff > 0f) return files[current].Info.Cutoff;
        for (int j = 0; j < files.Count; j++)
        {
            if (j == current) continue;
            if (files[j].Info.Cutoff > 0f) return files[j].Info.Cutoff;
        }
        return 0f;
    }

    /// <summary>Relative offset for SHUTDOWN/C/CUTOFF, EOF, START or a constant.</summary>
    public static double? GetRefTime(IReadOnlyList<IDataFile> files, int current, string ev)
    {
        var f = files[current];
        if (ev.Equals("SHUTDOWN", StringComparison.OrdinalIgnoreCase) || ev.Equals("c", StringComparison.OrdinalIgnoreCase) || ev.Equals("cutoff", StringComparison.OrdinalIgnoreCase))
        {
            double d = GetCutoff(files, current);
            if (!(d == 0.0 && f.Info.IsDynamic)) return d;
            return null;
        }
        if (ev.Equals("EOF", StringComparison.OrdinalIgnoreCase))
        {
            uint sig = f.Info.FileSig;
            if (SunSignatures.IsCommonTime(sig) && f is ITimeIndexedFile ti)
                return ti.IndexEndTime() ?? 3e38;
            return 3e38;
        }
        if (ev.Equals("START", StringComparison.OrdinalIgnoreCase)) return 0.0;
        if (ExprSyntax.ValidateConst(ev) == 0) return Crt.Atof(ev);
        return null;
    }

    /// <summary>
    /// The reference that moves data read against <paramref name="refEvent"/> by <paramref name="shift"/> seconds
    /// (positive = later on the time axis), as a literal UTC <c>ddd/msec.usec</c>. NONE, START, CUTOFF and constants
    /// are offsets from the file reference UTC; they become a constant offset instead when the files of the group
    /// have different (or no) reference UTCs. Null when the reference cannot be shifted (EOF or unresolved).
    /// </summary>
    public static string? ShiftReference(IReadOnlyList<IDataFile> files, int current, string refEvent, double shift)
    {
        string r = refEvent.Trim();
        double offset;
        if (r.Length == 0 || r.Equals("NONE", StringComparison.OrdinalIgnoreCase)) offset = 0.0;
        else if (r.Equals("EOF", StringComparison.OrdinalIgnoreCase)) return null;
        else if (GetRefUtc(files, current, r) is TimeTag utc) return EventMarkers.UtcSpec(TimeMath.Utc(-shift, utc, true));
        else if (GetRefTime(files, current, r) is double t && Math.Abs(t) < 1e30) offset = t;
        else return null;
        var fileRef = files[current].Info.RefUtc;
        bool commonRef = fileRef.JulianDay != 0 && files.All(f => f.Info.RefUtc == fileRef);
        return commonRef ? EventMarkers.UtcSpec(TimeMath.Utc(offset - shift, fileRef, true)) : FormatSeconds(offset - shift);
    }

    /// <summary>
    /// A start/stop time after the reference moved by <paramref name="shift"/> seconds: a plain number is relative to
    /// the reference and moves with it; events, UTCs and the ±3.4e38 defaults are unchanged.
    /// </summary>
    public static string ShiftSliceTime(string time, double shift)
    {
        if (!double.TryParse(time.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v)
            || Math.Abs(v) >= 1e30) return time;
        return FormatSeconds(v + shift);
    }

    private static string FormatSeconds(double v)
    {
        string s = Crt.Sprintf("%.6f", Math.Abs(v) < 5e-7 ? 0.0 : v).TrimEnd('0');
        return s.EndsWith('.') ? s[..^1] : s;
    }

    /// <summary>
    /// Converts a start/stop string (base[±offset]) to seconds relative to <paramref name="refEvent"/>.
    /// Returns 0 on success, −1/−2/−4 on failure.
    /// </summary>
    public static int SetTime(IReadOnlyList<IDataFile> files, int current, string offsetEvent, string refEvent, out double time, bool strict = false)
    {
        time = 0;
        int status = 0;
        string buf = offsetEvent.Replace(" ", "").TrimStart();
        int hasOffset = buf.IndexOfAny(new[] { ' ', '+', '-' });
        double baseOffset;
        if (hasOffset < 1)
        {
            baseOffset = 0.0;
            hasOffset = 0;
        }
        else
        {
            baseOffset = Crt.Atof(buf[hasOffset..]);
            buf = buf[..hasOffset];
        }

        var f = files[current];
        TimeTag? pg = GetRefUtc(files, current, buf, strict);
        if (pg.HasValue)
        {
            TimeTag timeUtc = pg.Value;
            TimeTag? refG = string.Equals(refEvent, "NONE", StringComparison.OrdinalIgnoreCase)
                ? f.Info.RefUtc
                : GetRefUtc(files, current, refEvent, strict);
            if (refG.HasValue)
                time = TimeMath.Time(timeUtc, refG.Value);
            else
            {
                if (f is ITimeIndexedFile ti && ti.HasCommonTimeIndex && ti.TryIndexTimeAtUtc(timeUtc, out double t))
                {
                    time = t;
                    if (!string.Equals(refEvent, "NONE", StringComparison.OrdinalIgnoreCase))
                    {
                        double? r = GetRefTime(files, current, refEvent);
                        if (r.HasValue) time -= r.Value;
                        else status = -4;
                    }
                }
                else
                    status = -2;
            }
        }
        else
        {
            double? t = GetRefTime(files, current, buf);
            if (t.HasValue)
            {
                time = t.Value;
                if (!string.Equals(refEvent, "NONE", StringComparison.OrdinalIgnoreCase) && time != Crt.Atof(buf))
                {
                    TimeTag? rg = GetRefUtc(files, current, refEvent, strict);
                    if (rg.HasValue)
                    {
                        TimeTag? start = GetRefUtc(files, current, "START", strict);
                        if (start.HasValue) time -= TimeMath.Time(rg.Value, start.Value);
                    }
                    else
                    {
                        double? r = GetRefTime(files, current, refEvent);
                        if (r.HasValue) time -= r.Value;
                    }
                }
            }
            else
                status = -1;
        }
        if (hasOffset != 0) time += baseOffset;
        return status;
    }
}
