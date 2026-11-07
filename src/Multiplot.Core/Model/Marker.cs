// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

namespace Multiplot.Core.Model;

/// <summary>
/// Event marker (FR-MRK-001): a timestamp and a text. The plot draws a full-height vertical line at the
/// timestamp and the text beside it at the top (default) or bottom of the plot area.
/// </summary>
public sealed class Marker
{
    /// <summary>Maximum markers per plot.</summary>
    public const int MaxMarkers = 1000;

    /// <summary>
    /// Timestamp: a GMT (<c>ddd/hh:mm:ss[.mmm]</c>, <c>hh:mm:ss[.mmm]</c>, <c>ddd/msec[.usec]</c>), a time in
    /// seconds on the plot's time axis, or the name of a file <c>[events]</c> entry / START, EOF, CUTOFF,
    /// each optionally followed by a <c>+/-seconds</c> offset. Resolved against the plot's time reference.
    /// </summary>
    public string Time = "";
    /// <summary>Text drawn beside the line.</summary>
    public string Text = "";
    /// <summary>True: text at the bottom of the plot area; false: at the top.</summary>
    public bool Bottom;

    /// <summary>Creates a marker.</summary>
    public Marker(string time, string text, bool bottom = false)
    {
        Time = time.Trim();
        Text = text.Trim();
        Bottom = bottom;
    }

    /// <summary>Copy.</summary>
    public Marker Clone() => new(Time, Text, Bottom);

    /// <summary>True when both markers have the same timestamp, text and position.</summary>
    public bool SameAs(Marker m) => string.Equals(m.Time, Time, StringComparison.OrdinalIgnoreCase) && m.Text == Text && m.Bottom == Bottom;

    /// <summary>The marker as <c>MARKER</c> command arguments / <c>.markers</c> file line.</summary>
    public string ToSpec() => Time + "," + Text + (Bottom ? ",BOTTOM" : "");

    /// <inheritdoc/>
    public override string ToString() => ToSpec();

    /// <summary>
    /// True when <paramref name="time"/> is syntactically a timestamp: non-empty, no white space, no
    /// <c>=</c> and no <c>,</c>.
    /// </summary>
    public static bool IsValidTime(string time)
    {
        string t = time.Trim();
        if (t.Length == 0) return false;
        foreach (char c in t)
            if (char.IsWhiteSpace(c) || c == '=' || c == ',' || c == '"') return false;
        return true;
    }

    /// <summary>
    /// Parses <c>&lt;timestamp&gt;,&lt;text&gt;[,TOP|BOTTOM]</c> (the <c>MARKER</c> command arguments and the
    /// <c>.markers</c> line format). The text may contain commas; a trailing <c>,TOP</c> / <c>,BOTTOM</c>
    /// (any case) selects the position. Returns null when the timestamp is missing or invalid or the text
    /// is empty.
    /// </summary>
    public static Marker? Parse(string spec)
    {
        string s = spec.Trim();
        int comma = s.IndexOf(',');
        if (comma <= 0) return null;
        if (IsLegacyFormat(s)) return null;
        string time = s[..comma].Trim();
        string text = s[(comma + 1)..].Trim();
        bool bottom = false;
        int last = text.LastIndexOf(',');
        if (last >= 0)
        {
            string pos = text[(last + 1)..].Trim();
            if (pos.Equals("BOTTOM", StringComparison.OrdinalIgnoreCase)) { bottom = true; text = text[..last].TrimEnd(); }
            else if (pos.Equals("TOP", StringComparison.OrdinalIgnoreCase)) text = text[..last].TrimEnd();
        }
        if (!IsValidTime(time) || text.Length == 0) return null;
        return new Marker(time, text, bottom);
    }

    private static readonly string[] LegacyTypes = { "TEXT", "LINE", "BOX", "CIRCLE", "EVENT", "REDLINE", "IMAGE" };

    /// <summary>
    /// True for the retired <c>MARKER n,type,x1,y1,x2,y2,color,label</c> form (second field a marker type,
    /// followed by four coordinates), which is rejected rather than read as a marker with odd text.
    /// </summary>
    private static bool IsLegacyFormat(string s)
    {
        var f = s.Split(',');
        if (f.Length < 7) return false;
        if (!LegacyTypes.Contains(f[1].Trim().ToUpperInvariant())) return false;
        for (int i = 2; i <= 5; i++)
            if (!double.TryParse(f[i].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _)) return false;
        return true;
    }

    /// <summary>Deep copies a marker list.</summary>
    public static List<Marker> CopySet(IEnumerable<Marker> src) => src.Select(m => m.Clone()).ToList();

    /// <summary>
    /// Adds <paramref name="m"/> to <paramref name="set"/> unless an identical marker exists or the set is
    /// full; returns false when not added.
    /// </summary>
    public static bool AddTo(List<Marker> set, Marker m)
    {
        if (set.Any(x => x.SameAs(m)) || set.Count >= MaxMarkers) return false;
        set.Add(m.Clone());
        return true;
    }

    /// <summary>
    /// True for <c>[events]</c> entries that are not drawn as markers: the time reference entries
    /// <c>FILE_REF</c> and <c>EOF</c> and entries flagged <c>(invalid)</c> by the reader.
    /// </summary>
    public static bool IsReservedEvent(string name) =>
        name.Equals("FILE_REF", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("EOF", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith("(invalid)", StringComparison.Ordinal);
}
