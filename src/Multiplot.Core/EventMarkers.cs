// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

using Multiplot.Core.Model;
using Multiplot.Plugins;

namespace Multiplot.Core;

/// <summary>A marker resolved to a time on the plot's time axis (seconds relative to the plot reference).</summary>
public readonly record struct ResolvedMarker(double Time, string Text, bool Bottom);

/// <summary>
/// Event marker resolution (FR-MRK-002): the plot's own markers plus, optionally, the <c>[events]</c> of the
/// data file headers (SUN comment block <c>[events]</c> section, parquet <c>events</c> metadata — both are
/// exposed as <see cref="IDataFile.Events"/>), resolved against the plot's time reference. Markers whose
/// timestamp cannot be resolved (for example a GMT not reached yet by a real-time file) are left out; they
/// appear once the timestamp resolves.
/// </summary>
public static class EventMarkers
{
    /// <summary>Files of group <paramref name="g"/>.</summary>
    public static IReadOnlyList<IDataFile> GroupFiles(IReadOnlyList<IDataFile> files, FileGroup g)
    {
        int start = Math.Clamp(g.StartPos, 0, files.Count);
        int n = Math.Clamp(g.NFiles, 0, files.Count - start);
        var list = new List<IDataFile>(n);
        for (int i = 0; i < n; i++) list.Add(files[start + i]);
        return list;
    }

    /// <summary>Exact <c>ddd/msec.usec</c> spelling of a GMT (parsed by <see cref="TimeReference.GetRefGmt"/>).</summary>
    public static string GmtSpec(TimeTag t) => Crt.Sprintf("%d/%u.%u", (int)t.JulianDay, t.MilliSeconds, (uint)t.MicroSeconds);

    /// <summary>
    /// Resolves the markers of a plot. <paramref name="plotGroups"/> are the group indexes the plot shows
    /// (tried first for user markers; file events are taken from these groups only). Returns markers in
    /// draw order: user markers, then file events (identical name and GMT listed once).
    /// </summary>
    public static List<ResolvedMarker> Resolve(IReadOnlyList<Marker> markers, bool includeFileEvents,
        IReadOnlyList<IDataFile> files, IReadOnlyList<FileGroup> groups, IReadOnlyCollection<int> plotGroups, string tRef)
    {
        var result = new List<ResolvedMarker>();
        if (groups.Count == 0 || files.Count == 0) return result;
        var order = plotGroups.Where(g => g >= 0 && g < groups.Count).Distinct().ToList();
        var groupFiles = new Dictionary<int, IReadOnlyList<IDataFile>>();
        IReadOnlyList<IDataFile> Files(int g) => groupFiles.TryGetValue(g, out var f) ? f : groupFiles[g] = GroupFiles(files, groups[g]);

        var search = order.Concat(Enumerable.Range(0, groups.Count).Where(g => !order.Contains(g))).ToList();
        foreach (var m in markers)
        {
            foreach (int g in search)
            {
                var gf = Files(g);
                if (gf.Count == 0) continue;
                if (TimeReference.SetTime(gf, 0, m.Time, tRef, out double t) == 0)
                {
                    result.Add(new ResolvedMarker(t, m.Text, m.Bottom));
                    break;
                }
            }
        }

        if (!includeFileEvents) return result;
        var seen = new HashSet<(string, short, uint, ushort)>();
        foreach (int g in order)
        {
            var gf = Files(g);
            if (gf.Count == 0) continue;
            foreach (var f in gf)
            {
                foreach (var e in f.Events)
                {
                    if (Marker.IsReservedEvent(e.Name)) continue;
                    if (!seen.Add((e.Name.ToUpperInvariant(), e.Gmt.JulianDay, e.Gmt.MilliSeconds, e.Gmt.MicroSeconds))) continue;
                    if (TimeReference.SetTime(gf, 0, GmtSpec(e.Gmt), tRef, out double t) == 0)
                        result.Add(new ResolvedMarker(t, e.Name, false));
                }
            }
        }
        return result;
    }
}
