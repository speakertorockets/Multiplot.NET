// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

using System.Globalization;

namespace Multiplot.Core.Model;

/// <summary>
/// Redline marker (FR-MRK-010): a value on the value (Y) axis and a text. The plot draws a full-width
/// horizontal line at the value, measured on the selected value axis (the first axis unless another one is
/// selected in multiple-Y mode), and the text beside it at the left (default) or right end of the line. The
/// line follows the value scale and is hidden while the value is outside the visible value range.
/// </summary>
public sealed class RedlineMarker
{
    /// <summary>Maximum redline markers per plot.</summary>
    public const int MaxRedlines = 1000;

    /// <summary>The value on the value axis.</summary>
    public double Value;
    /// <summary>Text drawn beside the line.</summary>
    public string Text = "";
    /// <summary>True: text at the right end of the line; false: at the left end.</summary>
    public bool Right;

    /// <summary>Creates a redline marker.</summary>
    public RedlineMarker(double value, string text, bool right = false)
    {
        Value = value;
        Text = text.Trim();
        Right = right;
    }

    /// <summary>Copy.</summary>
    public RedlineMarker Clone() => new(Value, Text, Right);

    /// <summary>True when both redlines have the same value, text and position.</summary>
    public bool SameAs(RedlineMarker r) => r.Value.Equals(Value) && r.Text == Text && r.Right == Right;

    /// <summary>The value spelled with invariant culture and full precision (round-trips).</summary>
    public string ValueText => Value.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>The redline as <c>REDLINE</c> command arguments: <c>&lt;value&gt;,&lt;text&gt;[,RIGHT]</c>.</summary>
    public string ToSpec() => ValueText + "," + Text + (Right ? ",RIGHT" : "");

    /// <summary>The redline as a <c>.markers</c> / <c>.wpd</c> line / recorded script command.</summary>
    public string ToCommand() => "redline " + ToSpec();

    /// <inheritdoc/>
    public override string ToString() => ToSpec();

    /// <summary>Parses a value (invariant culture, any float form); false when it is not a finite number.</summary>
    public static bool TryParseValue(string s, out double value) =>
        double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    /// <summary>
    /// Parses <c>&lt;value&gt;,&lt;text&gt;[,LEFT|RIGHT]</c> (the <c>REDLINE</c> command arguments). The text may
    /// contain commas; a trailing <c>,LEFT</c> / <c>,RIGHT</c> (any case) selects the text position. Returns
    /// null when the value is not a number or the text is empty.
    /// </summary>
    public static RedlineMarker? Parse(string spec)
    {
        string s = spec.Trim();
        int comma = s.IndexOf(',');
        if (comma <= 0) return null;
        if (!TryParseValue(s[..comma], out double v)) return null;
        string text = s[(comma + 1)..].Trim();
        bool right = false;
        int last = text.LastIndexOf(',');
        if (last >= 0)
        {
            string pos = text[(last + 1)..].Trim();
            if (pos.Equals("RIGHT", StringComparison.OrdinalIgnoreCase)) { right = true; text = text[..last].TrimEnd(); }
            else if (pos.Equals("LEFT", StringComparison.OrdinalIgnoreCase)) text = text[..last].TrimEnd();
        }
        if (text.Length == 0) return null;
        return new RedlineMarker(v, text, right);
    }

    /// <summary>
    /// When <paramref name="line"/> is a <c>redline &lt;value&gt;,&lt;text&gt;[,LEFT|RIGHT]</c> line (keyword in any
    /// case), returns true and the parsed redline (null when the arguments are invalid).
    /// </summary>
    public static bool TryParseCommand(string line, out RedlineMarker? redline)
    {
        redline = null;
        string s = line.Trim();
        if (s.Length < 8 || !s[..8].Equals("redline ", StringComparison.OrdinalIgnoreCase)) return false;
        redline = Parse(s[8..]);
        return true;
    }

    /// <summary>Deep copies a redline list.</summary>
    public static List<RedlineMarker> CopySet(IEnumerable<RedlineMarker> src) => src.Select(r => r.Clone()).ToList();

    /// <summary>
    /// Adds <paramref name="r"/> to <paramref name="set"/> unless an identical redline exists or the set is
    /// full; returns false when not added.
    /// </summary>
    public static bool AddTo(List<RedlineMarker> set, RedlineMarker r)
    {
        if (set.Any(x => x.SameAs(r)) || set.Count >= MaxRedlines) return false;
        set.Add(r.Clone());
        return true;
    }
}
