// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using Multiplot.DataAccess;
using Multiplot.Plugins;

namespace Multiplot.Tests.EndToEnd;

/// <summary>TST-PLG-001: the sample plugin loads, is listed, reads demo_data.min and can be blocked.</summary>
public class SamplePluginTests
{
    private sealed class Host : IPluginHost, IUserInteraction
    {
        public string UserFolder => Path.GetTempPath();
        public string TempFolder => Path.GetTempPath();
        public bool ScriptRunning => false;
        public bool DialogsAllowed => false;
        public void RecordScriptLine(string line) { }
        public void Log(string message) { }
        public IUserInteraction Ui => this;
        public PromptResult Show(string message, string caption, PromptButtons buttons, PromptIcon icon, PromptResult defaultResult) => defaultResult;
        public string? Input(string prompt, string caption, string defaultValue) => null;
    }

    private static string Config => AppContext.BaseDirectory.Contains("\\Release\\", StringComparison.OrdinalIgnoreCase) ? "Release" : "Debug";

    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static string? PluginOutput()
    {
        string dir = Path.Combine(Root, "samples", "MinimalPlugin", "bin", Config, "net10.0");
        return File.Exists(Path.Combine(dir, "MinimalPlugin.dll")) ? dir : null;
    }

    private static string StagePlugin(string pluginDir, string target)
    {
        Directory.CreateDirectory(target);
        File.Copy(Path.Combine(pluginDir, "MinimalPlugin.dll"), Path.Combine(target, "MinimalPlugin.dll"), true);
        return target;
    }

    [Fact]
    public void SamplePlugin_LoadsReadsAndCanBeBlocked()
    {
        string? output = PluginOutput();
        Assert.NotNull(output);
        string dir = StagePlugin(output!, Path.Combine(Path.GetTempPath(), "MultiplotNetPlg", Guid.NewGuid().ToString("N")));
        string demo = Path.Combine(output!, "demo_data.min");

        var svc = new DataAccessService();
        var r = svc.LoadPlugins(new[] { dir }, new HashSet<string>(StringComparer.OrdinalIgnoreCase), new Host());
        Assert.Empty(r.Warnings);
        var loaded = Assert.Single(svc.Plugins);
        Assert.Equal("MinimalPlugin.dll", loaded.FileName);
        Assert.Contains(svc.VersionLines(), l => l.Contains("MinimalPlugin"));

        Assert.Equal(0x11111111u, svc.IsSupportedType(demo));
        using (var f = svc.Open(demo))
        {
            Assert.NotNull(f);
            Assert.Equal(3, f!.Parameters.Count);
            var up = f.Read(new ParameterRequest("UPDATA", 0, 1e9));
            Assert.NotNull(up);
            Assert.Equal(32, up!.Points);
            Assert.Equal(31f, up.Values[31]);
            var ud = f.Read(new ParameterRequest("updown", 0, 1e9));
            Assert.Equal(7f, ud!.Values[15]);
        }

        svc.LoadPlugins(new[] { dir }, new HashSet<string>(new[] { "MinimalPlugin.dll" }, StringComparer.OrdinalIgnoreCase), new Host());
        Assert.Empty(svc.Plugins);
        Assert.Contains("MinimalPlugin.dll", svc.BlockedPlugins);
        Assert.Equal(0u, svc.IsSupportedType(demo));
        svc.UnloadPlugins();
    }

    [Fact]
    public void HiddenScript_PlotsAndExportsPluginData()
    {
        string? output = PluginOutput();
        string exe = Path.Combine(Root, "src", "Multiplot.App", "bin", Config, "net10.0-windows", "Multiplot.exe");
        Assert.NotNull(output);
        Assert.True(File.Exists(exe), "Multiplot.exe not built: " + exe);

        string dir = Path.Combine(Path.GetTempPath(), "MultiplotNetE2E", Guid.NewGuid().ToString("N"));
        StagePlugin(output!, Path.Combine(dir, "My Multiplot", "plugins"));
        string csv = Path.Combine(dir, "min.csv");
        string script = Path.Combine(dir, "plugin.wps");
        File.WriteAllText(script,
            "open " + Path.Combine(output!, "demo_data.min") + "\r\n" +
            "plot UPDATA,DOWNDATA\r\n" +
            "export " + csv + "\r\n" +
            "exit\r\n");
        string regRoot = "Software\\MultiplotNetTests\\" + Path.GetFileName(dir);
        using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(regRoot + "\\Plugins\\UserFolder"))
            k.SetValue("", 1, Microsoft.Win32.RegistryValueKind.DWord);

        var psi = new ProcessStartInfo(exe, "/hide /nosave \"" + script + "\"") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = dir };
        psi.Environment["USERPROFILE"] = dir;
        psi.Environment["MULTIPLOTNET_REGISTRY_ROOT"] = regRoot;
        using var p = Process.Start(psi)!;
        bool exited = p.WaitForExit(60000);
        if (!exited) { try { p.Kill(true); } catch (InvalidOperationException) { } }
        Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(regRoot, false);

        Assert.True(exited, "Multiplot.exe did not exit within 60 s");
        Assert.Equal(0, p.ExitCode);
        Assert.True(File.Exists(csv), "CSV not written");
        var lines = File.ReadAllLines(csv);
        Assert.Contains(lines.Take(5), l => l.Contains("UPDATA") && l.Contains("DOWNDATA"));
        Assert.True(lines.Length >= 32 + 4, "CSV has " + lines.Length + " lines");
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }
}
