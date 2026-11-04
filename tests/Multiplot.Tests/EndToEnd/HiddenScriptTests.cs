using System.Diagnostics;
using Multiplot.Sun;

namespace Multiplot.Tests.EndToEnd;

/// <summary>Runs the built Multiplot.exe with /hide on a script and checks its outputs.</summary>
public class HiddenScriptTests
{
    private static string? FindExe()
    {
        string dir = AppContext.BaseDirectory;
        string config = dir.Contains("\\Release\\", StringComparison.OrdinalIgnoreCase) ? "Release" : "Debug";
        string root = Path.GetFullPath(Path.Combine(dir, "..", "..", "..", "..", ".."));
        string exe = Path.Combine(root, "src", "Multiplot.App", "bin", config, "net10.0-windows", "Multiplot.exe");
        return File.Exists(exe) ? exe : null;
    }

    private static void WriteFixture(string path)
    {
        const int n = 200;
        var t = new float[n];
        var a = new float[n];
        var b = new float[n];
        for (int i = 0; i < n; i++)
        {
            t[i] = i * 0.05f;
            a[i] = (float)Math.Sin(t[i]);
            b[i] = 100f + i;
        }
        using var w = SunWriter.Create(path, SunCreateMode.Overwrite, 64, 3, n, 0xF3FFFFFFu);
        w.SetRefTime(2024, 100, 12, 0, 0, 0);
        w.SetTitle("E2E fixture");
        w.AddParm("TIME", "T", "SEC", "Time", 0, "f4", t, null, null, 0, 0, null);
        w.AddParm("SINE", "S", "V", "Sine wave", 1, "f4", a, null, null, 0, 0, null);
        w.AddParm("RAMP", "R", "CNT", "Ramp", 2, "f4", b, null, null, 0, 0, null);
        w.Close();
    }

    [Fact]
    public void HiddenScript_ProducesPictureExportAndParmList()
    {
        string? exe = FindExe();
        Assert.True(exe != null, "Multiplot.exe not built (the test project builds it via a project reference)");

        string dir = Path.Combine(Path.GetTempPath(), "MultiplotNetE2E", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string sun = Path.Combine(dir, "fixture.sun");
        WriteFixture(sun);
        string png = Path.Combine(dir, "plot.png");
        string csv = Path.Combine(dir, "plot.csv");
        string parms = Path.Combine(dir, "parms.txt");
        string script = Path.Combine(dir, "test.wps");
        File.WriteAllText(script,
            "open " + sun + "\r\n" +
            "plot SINE,RAMP\r\n" +
            "savepic PNG " + png + "\r\n" +
            "export " + csv + "\r\n" +
            "saveparmlist 1," + parms + "\r\n" +
            "exit\r\n");

        var psi = new ProcessStartInfo(exe!, "/hide /nosave /noplugins \"" + script + "\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = dir,
        };
        psi.Environment["USERPROFILE"] = dir;
        psi.Environment["MULTIPLOTNET_REGISTRY_ROOT"] = "Software\\MultiplotNetTests\\" + Path.GetFileName(dir);
        using var p = Process.Start(psi)!;
        bool exited = p.WaitForExit(60000);
        if (!exited) { try { p.Kill(true); } catch (InvalidOperationException) { } }
        try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree("Software\\MultiplotNetTests\\" + Path.GetFileName(dir), false); } catch (UnauthorizedAccessException) { }

        Assert.True(exited, "Multiplot.exe did not exit within 60 s");
        Assert.Equal(0, p.ExitCode);
        Assert.True(File.Exists(png), "PNG not written");
        var head = File.ReadAllBytes(png).Take(4).ToArray();
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, head);
        Assert.True(File.Exists(csv), "CSV not written");
        var lines = File.ReadAllLines(csv);
        Assert.True(lines.Length > 200, "CSV has " + lines.Length + " lines");
        Assert.Contains(lines.Take(5), l => l.Contains("SINE") && l.Contains("RAMP"));
        Assert.True(File.Exists(parms), "parameter list not written");
        string pl = File.ReadAllText(parms);
        Assert.Contains("SINE", pl);
        Assert.Contains("RAMP", pl);
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }
}
