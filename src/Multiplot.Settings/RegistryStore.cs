using Microsoft.Win32;
using Multiplot.Core;
using Multiplot.Core.Model;

namespace Multiplot.Settings;

/// <summary>Main window placement (registry <c>Window\position</c>).</summary>
public readonly record struct WindowPlacement(int Left, int Top, int Right, int Bottom, bool Maximized)
{
    /// <summary>"Not set" flag: top == bottom.</summary>
    public bool IsSet => Top != Bottom;
}

/// <summary>
/// HKCU\Software\Multiplot values (FR-CFG-030). The root can be redirected for tests (DEV-CFG-003).
/// </summary>
public sealed class RegistryStore
{
    /// <summary>Registry root under HKCU.</summary>
    public string Root { get; }

    /// <summary>Creates a store (default root <c>Software\Multiplot</c>).</summary>
    public RegistryStore(string root = "Software\\Multiplot") => Root = root;

    private RegistryKey? Open(string sub, bool write) =>
        write ? Registry.CurrentUser.CreateSubKey(Root + "\\" + sub, true) : Registry.CurrentUser.OpenSubKey(Root + "\\" + sub, false);

    private static T Safe<T>(Func<T> f, T fallback)
    {
        try { return f(); }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return fallback; }
    }

    private static void Safe(Action a)
    {
        try { a(); }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
    }

    /// <summary>Recent files in menu order.</summary>
    public List<string> ReadRecentFiles() => Safe(() =>
    {
        var l = new List<string>();
        using var k = Open("Recent Files", false);
        if (k == null) return l;
        for (int i = 0; i < 10; i++)
            if (k.GetValue("file" + (10 - i)) is string s) l.Add(s);
        return l;
    }, new List<string>());

    /// <summary>Recent files registry part: the first unset fileN, else overwrite slot (menuPos % 10) + 1.</summary>
    public void AddRecentFile(string name, int menuPos) => Safe(() =>
    {
        using var k = Open("Recent Files", true);
        if (k == null) return;
        for (int i = 0; i < 10; i++)
        {
            string key = "file" + (i + 1);
            if (k.GetValue(key) == null) { k.SetValue(key, name, RegistryValueKind.String); return; }
        }
        k.SetValue("file" + (menuPos % 10 + 1), name, RegistryValueKind.String);
    });

    /// <summary>Favourites file path.</summary>
    public string? ReadFavoritesFile() => Safe(() =>
    {
        using var k = Open("Favorites File", false);
        return k?.GetValue("favorites") as string;
    }, null);

    /// <summary>Writes the favourites file path.</summary>
    public void WriteFavoritesFile(string path) => Safe(() =>
    {
        using var k = Open("Favorites File", true);
        k?.SetValue("favorites", path, RegistryValueKind.String);
    });

    /// <summary>Reads the window placement ("left top right bottom maximized").</summary>
    public WindowPlacement ReadWindowPosition() => Safe(() =>
    {
        using var k = Open("Window", false);
        if (k?.GetValue("position") is not string s) return new WindowPlacement();
        var t = new Strtok(s);
        int l = 0, top = 0, r = 0, b = 0; bool max = false;
        string? p;
        if ((p = t.Next(" ")) != null) l = Crt.Atol(p);
        if ((p = t.Next(" ")) != null) top = Crt.Atol(p);
        if ((p = t.Next(" ")) != null) r = Crt.Atol(p);
        if ((p = t.Next(" ")) != null) b = Crt.Atol(p);
        else top = b = 0;
        if ((p = t.Next(" ")) != null) max = Crt.Atol(p) != 0;
        return new WindowPlacement(l, top, r, b, max);
    }, new WindowPlacement());

    /// <summary>Writes the window placement.</summary>
    public void WriteWindowPosition(WindowPlacement w) => Safe(() =>
    {
        using var k = Open("Window", true);
        k?.SetValue("position", Crt.Sprintf("%d %d %d %d %d", w.Left, w.Top, w.Right, w.Bottom, w.Maximized ? 1 : 0), RegistryValueKind.String);
    });

    /// <summary>Remembered parameter lists, most recent first.</summary>
    public List<string> ReadRecentParameters() => Safe(() =>
    {
        using var k = Open("Recent Parameters", false);
        return k?.GetValue("parmset") is string[] a ? a.Where(x => x.Length > 0).ToList() : new List<string>();
    }, new List<string>());

    /// <summary>Writes remembered lists.</summary>
    public void WriteRecentParameters(IEnumerable<string> lists) => Safe(() =>
    {
        using var k = Open("Recent Parameters", true);
        k?.SetValue("parmset", lists.Where(x => x.Length > 0 && x.Length <= 64).ToArray(), RegistryValueKind.MultiString);
    });

    /// <summary>Reads toolbar sets, user buttons and band order into the settings.</summary>
    public bool ReadToolbars(AppSettings s) => Safe(() =>
    {
        using var k = Open("Toolbars", false);
        if (k == null) return false;
        for (int bar = 0; bar < AppSettings.NToolbars; bar++)
        {
            if (k.GetValue(ToolbarDefaults.Names[bar]) is not string[] a || a.Length == 0) continue;
            s.ToolbarEnabled[bar] = Crt.Atoi(a[0]) != 0;
            var set = new List<ToolbarButton>();
            for (int i = 1; i < a.Length; i++)
            {
                if (a[i].Length == 0) break;
                var t = new Strtok(a[i]);
                string? id = t.Next(" "), img = t.Next(" ");
                if (id != null && img != null) set.Add(new ToolbarButton(Crt.Atoi(id), Crt.Atoi(img)));
            }
            if (set.Count > 0)
            {
                s.ToolbarSets[bar].Clear();
                s.ToolbarSets[bar].AddRange(set);
            }
        }
        for (int i = 0; i < AppSettings.NUserButtons; i++)
        {
            if (k.GetValue("UserButton " + (i + 1)) is string[] u && u.Length >= 2)
            {
                s.UserButtons[i].Label = u[0];
                s.UserButtons[i].Command = u[1];
            }
        }
        if (k.GetValue("Band Order") is string[] order)
        {
            var used = new List<int>();
            for (int i = 0; i < order.Length && i < AppSettings.NToolbars; i++)
            {
                int v = Crt.Atoi(order[i]);
                if (v >= 0 && v < AppSettings.NToolbars && !used.Contains(v)) used.Add(v);
            }
            for (int v = 0; v < AppSettings.NToolbars; v++) if (!used.Contains(v)) used.Add(v);
            for (int i = 0; i < AppSettings.NToolbars; i++) s.ToolbarOrder[i] = used[i];
        }
        return true;
    }, false);

    /// <summary>Saves the toolbars and user buttons.</summary>
    public void WriteToolbars(AppSettings s) => Safe(() =>
    {
        using var k = Open("Toolbars", true);
        if (k == null) return;
        for (int bar = 0; bar < AppSettings.NToolbars; bar++)
        {
            var a = new List<string> { s.ToolbarEnabled[bar] ? "1" : "0" };
            a.AddRange(s.ToolbarSets[bar].Select(b => $"{b.EventId} {b.ImageIndex}"));
            k.SetValue(ToolbarDefaults.Names[bar], a.ToArray(), RegistryValueKind.MultiString);
        }
        k.SetValue("Band Order", s.ToolbarOrder.Select(x => x.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray(), RegistryValueKind.MultiString);
        for (int i = 0; i < AppSettings.NUserButtons; i++)
        {
            if (s.UserButtons[i].Label.Length > 0 || s.UserButtons[i].Command.Length > 0)
                k.SetValue("UserButton " + (i + 1), new[] { s.UserButtons[i].Label, s.UserButtons[i].Command }, RegistryValueKind.MultiString);
        }
    });

    /// <summary>
    /// Copies every value and sub-key of <c>HKCU\<paramref name="sourceRoot"/></c> into this store's root,
    /// overwriting existing values; sub-keys named in <paramref name="skip"/> are left out. Returns the
    /// number of values copied (0 when the source key does not exist).
    /// </summary>
    public int CopyFrom(string sourceRoot, params string[] skip) => Safe(() =>
    {
        using var src = Registry.CurrentUser.OpenSubKey(sourceRoot, false);
        if (src == null) return 0;
        using var dst = Registry.CurrentUser.CreateSubKey(Root, true);
        return CopyKey(src, dst, skip);
    }, 0);

    private static int CopyKey(RegistryKey src, RegistryKey dst, string[] skip)
    {
        int n = 0;
        foreach (string name in src.GetValueNames())
        {
            object? v = src.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (v == null) continue;
            dst.SetValue(name, v, src.GetValueKind(name));
            n++;
        }
        foreach (string sub in src.GetSubKeyNames())
        {
            if (skip.Contains(sub, StringComparer.OrdinalIgnoreCase)) continue;
            using var s = src.OpenSubKey(sub, false);
            using var d = dst.CreateSubKey(sub, true);
            if (s != null && d != null) n += CopyKey(s, d, skip);
        }
        return n;
    }

    /// <summary>Blocked plugin names (value names of Plugins\Blocked).</summary>
    public HashSet<string> ReadBlockedPlugins() => Safe(() =>
    {
        using var k = Open("Plugins\\Blocked", false);
        return new HashSet<string>(k?.GetValueNames() ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
    }, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    /// <summary>Blocks a plugin (REG_BINARY 1).</summary>
    public void BlockPlugin(string name) => Safe(() =>
    {
        using var k = Open("Plugins\\Blocked", true);
        k?.SetValue(name, new byte[] { 1 }, RegistryValueKind.Binary);
    });

    /// <summary>Unblocks a plugin.</summary>
    public void UnblockPlugin(string name) => Safe(() =>
    {
        using var k = Registry.CurrentUser.OpenSubKey(Root + "\\Plugins\\Blocked", true);
        k?.DeleteValue(name, false);
    });

    /// <summary>Replaces the blocked list.</summary>
    public void WriteBlockedPlugins(IEnumerable<string> names) => Safe(() =>
    {
        Registry.CurrentUser.DeleteSubKeyTree(Root + "\\Plugins\\Blocked", false);
        using var k = Open("Plugins\\Blocked", true);
        foreach (var n in names) k?.SetValue(n, new byte[] { 1 }, RegistryValueKind.Binary);
    });

    /// <summary>Use-user-plugin-folder flag (default DWORD of Plugins\UserFolder).</summary>
    public bool ReadUserPluginFolder() => Safe(() =>
    {
        using var k = Open("Plugins\\UserFolder", false);
        return k?.GetValue("") is int v && v != 0;
    }, false);

    /// <summary>Writes the user-folder flag.</summary>
    public void WriteUserPluginFolder(bool on) => Safe(() =>
    {
        using var k = Open("Plugins\\UserFolder", true);
        k?.SetValue("", on ? 1 : 0, RegistryValueKind.DWord);
    });

    /// <summary>Time Slice dialog expanded state.</summary>
    public bool ReadTimeSliceExpanded() => Safe(() =>
    {
        using var k = Open("TimeSlice", false);
        return k?.GetValue("Expanded") is byte[] b && b.Length > 0 && b.Any(x => x != 0);
    }, false);

    /// <summary>Writes the Time Slice dialog expanded state (BOOL, 4 bytes).</summary>
    public void WriteTimeSliceExpanded(bool on) => Safe(() =>
    {
        using var k = Open("TimeSlice", true);
        k?.SetValue("Expanded", BitConverter.GetBytes(on ? 1 : 0), RegistryValueKind.Binary);
    });

    /// <summary>Additional values (DEV-CFG-003) under <c>NET\</c>.</summary>
    public string? ReadNet(string name) => Safe(() =>
    {
        using var k = Open("NET", false);
        return k?.GetValue(name) as string;
    }, null);

    /// <summary>Writes an additional value.</summary>
    public void WriteNet(string name, string value) => Safe(() =>
    {
        using var k = Open("NET", true);
        k?.SetValue(name, value, RegistryValueKind.String);
    });

    /// <summary>Deletes the whole root (tests).</summary>
    public void DeleteAll() => Safe(() => Registry.CurrentUser.DeleteSubKeyTree(Root, false));
}
