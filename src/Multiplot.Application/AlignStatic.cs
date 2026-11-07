// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

using Multiplot.Core;
using Multiplot.Core.Model;
using Multiplot.Plugins;

namespace Multiplot.Application;

/// <summary>Keyboard nudge sizes of the static alignment mode.</summary>
public enum AlignStep
{
    /// <summary>One screen pixel.</summary>
    Pixel,
    /// <summary>A tenth of a pixel.</summary>
    Fine,
    /// <summary>One X grid division.</summary>
    Graduation,
}

/// <summary>State of an active Align Static session (FR-UI-024a).</summary>
public sealed class AlignSession
{
    internal sealed record Entry(Curve Curve, ParameterData Data, float[] Saved, int Points, float SavedShift);

    internal AlignSession(PlotDocument plot, List<Entry> entries, double shift)
    {
        Plot = plot;
        Entries = entries;
        Shift = InitialShift = shift;
    }

    /// <summary>The plot being aligned.</summary>
    public PlotDocument Plot { get; }
    /// <summary>Current shift of the static curves, seconds (positive = later).</summary>
    public double Shift { get; internal set; }
    /// <summary>First snap click (a feature on a static curve) relative to the static curves (X − shift), or null.</summary>
    public double? SnapBase { get; internal set; }
    /// <summary>X position of the pending snap mark (it moves with the static curves), or null.</summary>
    public double? SnapX => SnapBase + Shift;
    internal double InitialShift { get; }
    internal List<Entry> Entries { get; }
}

/// <summary>
/// Align Static mode (FR-UI-024a): drag or nudge every static curve of the active plot along the time axis
/// against the live curves; Enter commits the shift as the static groups' time reference.
/// </summary>
public sealed partial class Session
{
    /// <summary>The active alignment, or null.</summary>
    public AlignSession? Align { get; private set; }

    /// <summary>Starts Align Static on the active plot; false (with a message) when not possible.</summary>
    public bool BeginAlign()
    {
        if (Align != null) return true;
        var plot = ActivePlot;
        if (plot == null || plot.NCurves == 0) { Message("No active plot to align."); return false; }
        var c = plot.Config;
        if (c.Style == PlotStyle.Tabular || c.EndToEnd || c.PidVsPid || c.LogX)
        {
            Message("Align Static needs a time plot (not tabular, End-to-End, parameter vs. parameter or log X).");
            return false;
        }
        if (c.Use64BitTime) { Message("Turn off 64-bit time before using Align Static."); return false; }
        var stat = plot.Curves.Where(x => x.Data.Source != null && !x.IsDynamic && x.Points > 0).ToList();
        if (stat.Count == 0 || !plot.Curves.Any(x => x.IsDynamic))
        {
            Message("Align Static needs a plot with both static (recorded) and live (real-time) curves.");
            return false;
        }
        var entries = stat.Select(x => new AlignSession.Entry(x, x.Data, x.Time[..x.Points], x.Points, x.Data.TimeShift)).ToList();
        // previous Alignment Tool shifts are folded in when they agree, else dropped (the commit reloads unshifted data)
        float first = entries[0].SavedShift;
        double start = entries.All(e => e.SavedShift == first) ? first : 0.0;
        Align = new AlignSession(plot, entries, start);
        ApplyAlignShift(start);
        Shell.RefreshCommands();
        return true;
    }

    /// <summary>Sets the static shift (seconds) and redraws.</summary>
    public void SetAlignShift(double shift)
    {
        if (Align == null) return;
        ApplyAlignShift(shift);
    }

    /// <summary>Moves the static curves one step left (−1) or right (+1).</summary>
    public void NudgeAlign(int direction, AlignStep step)
    {
        var a = Align;
        if (a == null) return;
        var xs = a.Plot.XScale;
        double px = xs.Factor != 0 ? 1.0 / xs.Factor : 0.0;
        double d = step switch
        {
            AlignStep.Graduation => xs.Graduation,
            AlignStep.Fine => px / 10.0,
            _ => px,
        };
        if (d > 0) ApplyAlignShift(a.Shift + Math.Sign(direction) * d);
    }

    /// <summary>
    /// Two-click snap: the first click marks a feature on a static curve, the second the same feature on a live curve;
    /// the shift then moves the first onto the second.
    /// </summary>
    public void AlignSnap(double x)
    {
        var a = Align;
        if (a == null) return;
        if (a.SnapBase is not double from)
        {
            a.SnapBase = x - a.Shift;
            Invalidate(a.Plot);
            AlignStatus("Snap: now click the matching feature on a live curve (right click = cancel snap)");
            return;
        }
        a.SnapBase = null;
        ApplyAlignShift(x - from);
    }

    /// <summary>Cancels a pending snap click; true when one was pending.</summary>
    public bool CancelAlignSnap()
    {
        if (Align?.SnapBase == null) return false;
        Align.SnapBase = null;
        Invalidate(Align.Plot);
        AlignStatus();
        return true;
    }

    /// <summary>Leaves Align Static and restores the curves as they were.</summary>
    public void CancelAlign()
    {
        var a = Align;
        if (a == null) return;
        Align = null;
        foreach (var e in a.Entries)
        {
            if (!ReferenceEquals(e.Curve.Data, e.Data)) continue;
            Array.Copy(e.Saved, e.Data.Time, Math.Min(e.Points, e.Data.Points));
            e.Data.TimeShift = e.SavedShift;
        }
        Invalidate(a.Plot);
        SetStatus("Align Static cancelled");
        Shell.RefreshCommands();
    }

    /// <summary>Leaves Align Static and makes the shift permanent by moving the static groups' time reference.</summary>
    public void AcceptAlign()
    {
        var a = Align;
        if (a == null) return;
        double shift = a.Shift;
        if (Math.Abs(shift) < 1e-7 && a.Entries.All(e => e.SavedShift == 0f))
        {
            CancelAlign();
            SetStatus("Align Static: no change");
            return;
        }
        // one reference change per static group of the plot
        var groups = new SortedDictionary<int, int>();
        foreach (var e in a.Entries)
        {
            int iFile = _files.IndexOf(e.Data.Source!);
            if (iFile < 0) continue;
            int grp = GroupOfFile(iFile);
            if (!groups.ContainsKey(grp)) groups[grp] = iFile - _groups[grp].StartPos;
        }
        var newRefs = new Dictionary<int, string>();
        foreach (var (grp, cur) in groups)
        {
            string? r = TimeReference.ShiftReference(GroupFiles(grp), cur, _groups[grp].TRef, shift);
            if (r == null)
            {
                Message(Crt.Sprintf("Can't align group %s: its time reference \"%s\" can't be shifted.\nSet a different reference (TimeSlice > Per Database) and try again.",
                    _groups[grp].GroupName, _groups[grp].TRef));
                return;
            }
            newRefs[grp] = r;
        }
        Align = null;
        // the alignment now lives in the reference (a frozen plot keeps its preview times, which already match it)
        foreach (var e in a.Entries) if (ReferenceEquals(e.Curve.Data, e.Data)) e.Data.TimeShift = 0f;
        foreach (var (grp, r) in newRefs)
        {
            var g = _groups[grp];
            g.TRef = r;
            g.TStart = TimeReference.ShiftSliceTime(g.TStart, shift);
            g.TStop = TimeReference.ShiftSliceTime(g.TStop, shift);
            Record("TIME " + g.GroupName + ":" + g.TStart + "," + g.TStop + "," + g.TRef);
        }
        // reload every plot that shows data of a changed group
        foreach (var p in _plots)
        {
            if (!p.Curves.Any(c => c.Data.Source is { } src && _files.IndexOf(src) is int f && f >= 0 && newRefs.ContainsKey(GroupOfFile(f)))) continue;
            p.ForceReload = true;
            if (p.TimeFreeze != -1) p.TimeFreeze = 0;
        }
        TimeChange = true;
        Shell.RefreshCommands();
        RefreshNow();
        Invalidate(a.Plot);
        SetStatus(Crt.Sprintf("Static data aligned: shift %+.3f s applied to the time reference", shift));
    }

    private void ApplyAlignShift(double shift)
    {
        var a = Align!;
        a.Shift = shift;
        foreach (var e in a.Entries)
        {
            if (!ReferenceEquals(e.Curve.Data, e.Data)) continue;
            var t = e.Data.Time;
            int n = Math.Min(e.Points, e.Data.Points);
            double delta = shift - e.SavedShift;
            for (int i = 0; i < n; i++) t[i] = (float)(e.Saved[i] + delta);
            e.Data.TimeShift = (float)shift;
        }
        Invalidate(a.Plot);
        AlignStatus();
    }

    private void AlignStatus(string? extra = null)
    {
        if (Align == null) return;
        SetStatus(Crt.Sprintf("Align Static: shift %+.3f s   drag or ←/→ to move, click static then live feature to snap, Enter = accept, Esc = cancel", Align.Shift)
            + (extra != null ? "   " + extra : ""));
    }
}
