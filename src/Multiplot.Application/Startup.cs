// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

using Multiplot.Core.Model;
using Multiplot.DataAccess;
using Multiplot.Plugins;
using Multiplot.Settings;

namespace Multiplot.Application;

/// <summary>Parsed command line (FR-SCR-300/301).</summary>
public sealed class CommandLine
{
    /// <summary>/noplugins.</summary>
    public bool NoPlugins;
    /// <summary>/default.</summary>
    public bool Default;
    /// <summary>/hide.</summary>
    public bool Hide;
    /// <summary>/nosave.</summary>
    public bool NoSave;
    /// <summary>Remaining file arguments in order.</summary>
    public List<string> Files { get; } = new();

    /// <summary>Parses the raw command line (without the program name).</summary>
    public static CommandLine Parse(string raw)
    {
        var cl = new CommandLine();
        string cmd = raw;
        bool Strip(string sw)
        {
            int p = cmd.ToLowerInvariant().IndexOf(sw, StringComparison.Ordinal);
            if (p < 0) return false;
            cmd = cmd.Remove(p, sw.Length);
            return true;
        }
        cl.NoPlugins = Strip("/noplugins");
        cl.Default = Strip("/default");
        cl.Hide = Strip("/hide");
        if (Strip("/nosave")) { cl.NoSave = true; cmd = cmd.Replace("  ", " "); }
        int c = cmd.ToLowerInvariant().IndexOf("/comm/", StringComparison.Ordinal);
        if (c >= 0)
        {
            int end = cmd.IndexOf(' ', c);
            cmd = (end < 0 ? cmd[..c] : cmd[..c] + cmd[end..]).Replace("  ", " ");
        }
        var chars = cmd.ToCharArray();
        bool quote = false;
        for (int i = 0; i < chars.Length; i++)
        {
            if (chars[i] == '"') quote = !quote;
            else if (chars[i] == ' ' && !quote) chars[i] = ',';
        }
        foreach (var part in new string(chars).Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            string p = part.Replace("\"", "").Trim();
            if (p.Length > 0) cl.Files.Add(p);
        }
        return cl;
    }
}

/// <summary>Application start-up and shutdown sequence (FR-CFG-001..004).</summary>
public static class Startup
{
    /// <summary>Environment variable overriding the registry root (tests, side-by-side runs).</summary>
    public const string RegistryRootVariable = "MULTIPLOTNET_REGISTRY_ROOT";

    /// <summary>Builds the settings defaults that depend on the machine (user folder, temp, INI names).</summary>
    public static AppSettings CreateSettings()
    {
        var s = new AppSettings();
        string exeDir = AppContext.BaseDirectory.TrimEnd('\\');
        s.StartupPath = exeDir;
        Environment.SetEnvironmentVariable("MULTIPLOTPATH", exeDir);
        s.TempDir = Path.GetTempPath().TrimEnd('\\');
        s.ExportDir = s.TempDir;
        string installIni = Path.Combine(exeDir, "Multiplot.ini");
        string installFav = Path.Combine(exeDir, "Multiplot.favorites");
        s.IniFile = installIni;
        s.FavFile = installFav;
        string? profile = Environment.GetEnvironmentVariable("USERPROFILE");
        if (!string.IsNullOrEmpty(profile))
        {
            s.UserFolder = Path.Combine(profile, "My Multiplot");
            try
            {
                Directory.CreateDirectory(s.UserFolder);
                string userIni = Path.Combine(s.UserFolder, "Multiplot.ini");
                if (!File.Exists(userIni))
                {
                    s.FirstRun = true;
                    if (File.Exists(installIni)) File.Copy(installIni, userIni, false);
                    string userFav = Path.Combine(s.UserFolder, "Multiplot.favorites");
                    if (File.Exists(installFav) && !File.Exists(userFav)) File.Copy(installFav, userFav, false);
                }
                Directory.CreateDirectory(Path.Combine(s.UserFolder, "plugins"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            s.IniFile = Path.Combine(s.UserFolder, "Multiplot.ini");
            s.FavFile = Path.Combine(s.UserFolder, "Multiplot.favorites");
        }
        s.QueryDir = s.UserFolder;
        return s;
    }

    /// <summary>Creates the session: registry, plugins, INI.</summary>
    public static Session CreateSession(IShell shell, CommandLine cl)
    {
        var settings = CreateSettings();
        string root = Environment.GetEnvironmentVariable(RegistryRootVariable) is { Length: > 0 } r ? r : "Software\\Multiplot";
        var reg = new RegistryStore(root);
        var data = new DataAccessService();
        var s = new Session(shell, settings, reg, data) { RunHidden = cl.Hide, NoPlugins = cl.NoPlugins };
        string? fav = reg.ReadFavoritesFile();
        if (fav != null) settings.FavFile = fav;
        foreach (var f in reg.ReadRecentFiles()) s.AddRecentFile(f, false);
        var parms = reg.ReadRecentParameters();
        for (int i = parms.Count - 1; i >= 0; i--) s.SaveParm(parms[i]);
        reg.ReadUserButtons(settings);
        s.UseUserPluginFolder = reg.ReadUserPluginFolder();
        s.SetupPlugins();
        return s;
    }

    /// <summary>Loads the INI (or the installation INI with /default)</summary>
    public static void LoadInitialSettings(Session s, CommandLine cl)
    {
        string saveIni = s.Settings.IniFile;
        if (cl.Default) s.Settings.IniFile = Path.Combine(AppContext.BaseDirectory, "Multiplot.ini");
        if (!s.LoadSettings(s.Settings.IniFile))
            s.Message("Multiplot.ini not found\nDefaults used");
        else
        {
            if (!Directory.Exists(s.Settings.ExportDir))
            {
                s.Message("Export path is invalid (see Options->File Paths)");
                s.Settings.ExportDir = s.Settings.TempDir;
            }
            if (cl.NoSave) s.Settings.AutoSave = false;
        }
        if (cl.Default) s.Settings.IniFile = saveIni;
        if (s.Settings.FirstRun && !cl.Hide && !cl.Default) OfferWinPlotImport(s);
    }

    /// <summary>
    /// First start-up only: offers to import the settings of an existing WinPlot installation (FR-CFG-050).
    /// </summary>
    private static void OfferWinPlotImport(Session s)
    {
        string? ini = Session.DefaultWinPlotIni();
        if (ini == null || !File.Exists(ini)) return;
        if (s.Message("Settings of an existing WinPlot installation were found in\n" + Path.GetDirectoryName(ini) +
                "\n\nImport them into Multiplot.NET?", PromptButtons.YesNo, PromptIcon.Question, PromptResult.No) == PromptResult.Yes)
            s.ImportWinPlotSettings(ini);
    }

    /// <summary>Processes the file arguments in order (FR-SCR-301).</summary>
    public static async Task ProcessArgumentsAsync(Session s, CommandLine cl)
    {
        foreach (string arg in cl.Files)
        {
            string ext = arg.Length >= 4 ? arg[^4..].ToLowerInvariant() : "";
            switch (ext)
            {
                case ".wps":
                    await s.Scripts.RunAsync(arg);
                    s.ScriptRun = RunState.Idle;
                    break;
                case ".ini":
                    if (!s.LoadSettings(arg)) s.Message("Multiplot.ini not found\nDefaults used");
                    break;
                case ".csv":
                    s.ImportAscii(arg, false);
                    break;
                default:
                    s.LoadFileGroup(arg);
                    break;
            }
        }
    }

    /// <summary>Exit checks; returns false when the exit must be refused.</summary>
    public static bool QueryExit(Session s)
    {
        if (s.DataLoading)
        {
            s.Message("Can't exit while data is loading");
            return false;
        }
        if (s.Settings.ExitSafety && s.Message("Are you sure you want to exit?", PromptButtons.YesNo, PromptIcon.Question, PromptResult.Yes) == PromptResult.No)
            return false;
        if (s.ScriptRun > 0)
        {
            string name = s.ScriptFile[(s.ScriptFile.LastIndexOf('\\') + 1)..];
            if (s.Message("Script(" + name + ") is active, exit anyway?", PromptButtons.YesNo, PromptIcon.Warning, PromptResult.Yes) == PromptResult.Yes)
                s.ScriptRun = RunState.ExitRequested;
            return false;
        }
        return true;
    }

    /// <summary>Stop refresh, close files, stop recording, save registry state.</summary>
    public static void Shutdown(Session s, WindowPlacement? placement)
    {
        s.ShuttingDown = true;
        if (s.AutoRefresh != 0)
        {
            s.AutoRefresh = 0;
            s.Settings.PreviousRefresh = 0;
            s.Shell.StopTimer("refresh");
        }
        if (s.AutoTab != 0) s.Shell.StopTimer("autotab");
        s.CloseAllFiles();
        if (s.Recording) s.SetRecording(null);
        s.Registry.WriteUserButtons(s.Settings);
        s.Registry.WriteRecentParameters(s.PastParms.Where(p => !string.IsNullOrEmpty(p)));
        if (s.Settings.FavFile.Length > 0) s.Registry.WriteFavoritesFile(s.Settings.FavFile);
        if (s.Settings.AutoSave && placement != null) s.Registry.WriteWindowPosition(placement.Value);
        s.Shutdown();
    }
}
