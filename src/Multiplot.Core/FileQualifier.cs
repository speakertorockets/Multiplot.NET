// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

namespace Multiplot.Core;

/// <summary>
/// File-qualified parameter names, <c>qualifier:NAME</c>: only files whose path or group name contains the
/// qualifier (ignoring case) are searched. Used by the loader and by the Parameter Selection dialog to limit a
/// selection to one file (FR-UI-020).
/// </summary>
public static class FileQualifier
{
    /// <summary>Characters a qualifier cannot hold: list separator, expression brackets, quotes, position marker.</summary>
    private const string Forbidden = ",()[]\"'@";

    /// <summary>True when a file with <paramref name="path"/> in group <paramref name="groupName"/> is searched for <paramref name="qualifier"/>.</summary>
    public static bool Matches(string path, string groupName, string qualifier) =>
        path.Contains(qualifier, StringComparison.OrdinalIgnoreCase) || groupName.Contains(qualifier, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Index of the colon that ends the qualifier of a plain name (before any <c>"description"</c>), or −1.
    /// </summary>
    public static int Colon(string name)
    {
        int q = name.IndexOf('"');
        return (q >= 0 ? name[..q] : name).LastIndexOf(':');
    }

    /// <summary>
    /// The qualifier that selects only file <paramref name="file"/>: its file name, else its full path, else the
    /// longest usable piece of either; null when no such text exists.
    /// </summary>
    /// <param name="paths">Paths of the open files.</param>
    /// <param name="groupNames">Group name of each open file.</param>
    /// <param name="file">The file to select.</param>
    public static string? For(IReadOnlyList<string> paths, IReadOnlyList<string> groupNames, int file)
    {
        if (file < 0 || file >= paths.Count) return null;
        string path = paths[file];
        string name = Path.GetFileName(path);
        var candidates = new List<string> { name, path };
        foreach (var whole in new[] { name, path })
            candidates.AddRange(whole.Split(Forbidden.ToCharArray()).Select(x => x.Trim()).Where(x => x.Length > 0).OrderByDescending(x => x.Length));
        foreach (string q in candidates)
        {
            if (q.Length == 0 || q.IndexOfAny(Forbidden.ToCharArray()) >= 0 || q != q.Trim() || LooksLikeConstant(q)) continue;
            bool unique = true;
            for (int j = 0; j < paths.Count && unique; j++)
                if (j != file && Matches(paths[j], groupNames[j], q)) unique = false;
            if (unique) return q;
        }
        return null;
    }

    /// <summary>
    /// Prefixes every plain name of a comma list with <paramref name="qualifier"/>. Expressions, functions and
    /// names that already carry a qualifier are left alone; a trailing <c>@x:y</c> position stays at the end.
    /// </summary>
    public static string Qualify(string list, string qualifier)
    {
        var items = list.Split(',');
        for (int i = 0; i < items.Length; i++)
        {
            string t = items[i].Trim();
            if (t.Length == 0 || ExprSyntax.IsFunction(t) != ExprSyntax.Fn.None || ExprSyntax.IsExpression(t)) continue;
            string head = t;
            int at = head.IndexOf('@');
            if (at > 0 && head.IndexOf(':', at) >= 0) head = head[..at];
            if (Colon(head) >= 0) continue;
            items[i] = qualifier + ":" + t;
        }
        return string.Join(",", items);
    }

    // a leading sign and digit makes the parameter text a constant
    private static bool LooksLikeConstant(string q) =>
        q.Length > 1 && (q[0] == '+' || q[0] == '-') && (char.IsAsciiDigit(q[1]) || (q[1] == '.' && q.Length > 2 && char.IsAsciiDigit(q[2])));
}
