using Multiplot.Plugins;

namespace Multiplot.Core;

/// <summary>
/// Time slice / reference resolution: reference GMT and TIME, cutoff and start/stop times
/// (FR-LOAD-020, FR-LOAD-021).
/// </summary>
public static class TimeReference
{
    /// <summary>GMT for a clock/TimeTag string or event name; null if not a GMT.</summary>
    public static TimeTag? GetRefGmt(IReadOnlyList<IDataFile> files, int current, string ev, bool strict = false)
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
            if (string.Equals(ev, e.Name, StringComparison.OrdinalIgnoreCase)) return e.Gmt;
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
                if (string.Equals(ev, e.Name, StringComparison.OrdinalIgnoreCase)) { found = e.Gmt; break; }
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
        TimeTag? pg = GetRefGmt(files, current, buf, strict);
        if (pg.HasValue)
        {
            TimeTag timeGmt = pg.Value;
            TimeTag? refG = string.Equals(refEvent, "NONE", StringComparison.OrdinalIgnoreCase)
                ? f.Info.RefGmt
                : GetRefGmt(files, current, refEvent, strict);
            if (refG.HasValue)
                time = TimeMath.Time(timeGmt, refG.Value);
            else
            {
                if (f is ITimeIndexedFile ti && ti.HasCommonTimeIndex && ti.TryIndexTimeAtGmt(timeGmt, out double t))
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
                    TimeTag? rg = GetRefGmt(files, current, refEvent, strict);
                    if (rg.HasValue)
                    {
                        TimeTag? start = GetRefGmt(files, current, "START", strict);
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
