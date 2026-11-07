// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

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

    private static void WriteFixture(string path, string? comment = null)
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
        if (comment != null) w.SetCommentText(comment);
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

    private static (bool Exited, int ExitCode) RunHidden(string exe, string dir, string script)
    {
        var psi = new ProcessStartInfo(exe, "/hide /nosave /noplugins \"" + script + "\"")
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
        return (exited, exited ? p.ExitCode : -1);
    }

    /// <summary>
    /// Event markers end to end: MARKER commands and the file's [events] are drawn (pictures change), the
    /// FILEEVENTS switch and CLEAR MARKERS ALL remove them again, and .markers files round-trip.
    /// </summary>
    [Fact]
    public void HiddenScript_EventMarkers()
    {
        string? exe = FindExe();
        Assert.True(exe != null, "Multiplot.exe not built (the test project builds it via a project reference)");

        string dir = Path.Combine(Path.GetTempPath(), "MultiplotNetE2E", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string sun = Path.Combine(dir, "events.sun");
        WriteFixture(sun, "[events]\nFILE_REF=100/43200000.0\nSTEP=100/12:00:03.000\n");
        string P(string n) => Path.Combine(dir, n);
        string script = P("markers.wps");
        File.WriteAllText(script,
            "open " + sun + "\r\n" +
            "timestamp off\r\n" +
            "plot SINE\r\n" +
            "markerset fileevents off\r\n" +
            "savepic PNG " + P("none.png") + "\r\n" +
            "markerset fileevents on\r\n" +
            "savepic PNG " + P("file.png") + "\r\n" +
            "marker 2.5,Two and a half\r\n" +
            "marker STEP+4,After step,BOTTOM   ; trailing comment\r\n" +
            "marker 2.5,Two and a half\r\n" +
            "savepic PNG " + P("user.png") + "\r\n" +
            "markerset save " + P("a.markers") + "\r\n" +
            "clear markers all\r\n" +
            "markerset fileevents off\r\n" +
            "savepic PNG " + P("cleared.png") + "\r\n" +
            "markerset load " + P("a.markers") + "\r\n" +
            "markerset save " + P("b.markers") + "\r\n" +
            "clear marker 1\r\n" +
            "markerset save " + P("c.markers") + "\r\n" +
            "exit\r\n");

        var (exited, code) = RunHidden(exe!, dir, script);
        Assert.True(exited, "Multiplot.exe did not exit within 60 s");
        Assert.Equal(0, code);
        foreach (var f in new[] { "none.png", "file.png", "user.png", "cleared.png", "a.markers", "b.markers", "c.markers" })
            Assert.True(File.Exists(P(f)), f + " not written");
        byte[] none = File.ReadAllBytes(P("none.png")), file = File.ReadAllBytes(P("file.png"));
        byte[] user = File.ReadAllBytes(P("user.png")), cleared = File.ReadAllBytes(P("cleared.png"));
        Assert.False(none.SequenceEqual(file), "file [events] marker not drawn");
        Assert.False(file.SequenceEqual(user), "MARKER commands not drawn");
        Assert.True(none.SequenceEqual(cleared), "markers not removed by CLEAR MARKERS ALL / FILEEVENTS OFF");

        var a = File.ReadAllLines(P("a.markers")).Where(l => !l.StartsWith(';')).ToArray();
        Assert.Equal(new[] { "2.5,Two and a half", "STEP+4,After step,BOTTOM" }, a);
        Assert.Equal(a, File.ReadAllLines(P("b.markers")).Where(l => !l.StartsWith(';')).ToArray());
        Assert.Equal(new[] { "STEP+4,After step,BOTTOM" }, File.ReadAllLines(P("c.markers")).Where(l => !l.StartsWith(';')).ToArray());
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    /// <summary>
    /// Appearance and nomenclature end to end: APPEARANCE DARK changes the picture and APPEARANCE LIGHT
    /// restores it exactly; TESTINFO draws the file's test title; the retired commands (TIMESTAMP,
    /// SHOWVERSION, SHOWUSERNAME, SHOWPAGENO, SHOWSCRIPT, SET GROUPNAME, FONTSIZE, TILE name) are accepted
    /// and change nothing.
    /// </summary>
    [Fact]
    public void HiddenScript_AppearanceAndRetiredNomenclature()
    {
        string? exe = FindExe();
        Assert.True(exe != null, "Multiplot.exe not built (the test project builds it via a project reference)");

        string dir = Path.Combine(Path.GetTempPath(), "MultiplotNetE2E", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string sun = Path.Combine(dir, "look.sun");
        WriteFixture(sun);
        string P(string n) => Path.Combine(dir, n);
        string script = P("look.wps");
        File.WriteAllText(script,
            "open " + sun + "\r\n" +
            "plot SINE,RAMP\r\n" +
            "savepic PNG " + P("light.png") + "\r\n" +
            "timestamp on\r\nshowversion on\r\nshowusername on\r\nshowpageno on\r\nshowscript on\r\n" +
            "set groupname all=Group A\r\nfontsize 4\r\ntile Group A\r\nautotab 0 Group A\r\n" +
            "savepic PNG " + P("retired.png") + "\r\n" +
            "testinfo on\r\n" +
            "savepic PNG " + P("testinfo.png") + "\r\n" +
            "testinfo off\r\n" +
            "appearance dark\r\n" +
            "savepic PNG " + P("dark.png") + "\r\n" +
            "appearance light\r\n" +
            "savepic PNG " + P("light2.png") + "\r\n" +
            "exit\r\n");

        var (exited, code) = RunHidden(exe!, dir, script);
        Assert.True(exited, "Multiplot.exe did not exit within 60 s");
        Assert.Equal(0, code);
        foreach (var f in new[] { "light.png", "retired.png", "testinfo.png", "dark.png", "light2.png" })
            Assert.True(File.Exists(P(f)), f + " not written");
        byte[] R(string f) => File.ReadAllBytes(P(f));
        Assert.True(R("light.png").SequenceEqual(R("retired.png")), "a retired nomenclature command changed the plot");
        Assert.False(R("light.png").SequenceEqual(R("testinfo.png")), "TESTINFO did not draw the test title");
        Assert.False(R("light.png").SequenceEqual(R("dark.png")), "APPEARANCE DARK did not change the plot");
        Assert.True(R("light.png").SequenceEqual(R("light2.png")), "APPEARANCE LIGHT did not restore the plot");
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    /// <summary>
    /// Redline markers end to end: a REDLINE inside the value range is drawn, one outside it is not, the line
    /// follows a change of the value scale, SET COLOR MARKER/REDLINE change the pictures, CLEAR REDLINES
    /// removes them, and redlines round-trip through .markers files next to event markers.
    /// </summary>
    [Fact]
    public void HiddenScript_RedlineMarkers()
    {
        string? exe = FindExe();
        Assert.True(exe != null, "Multiplot.exe not built (the test project builds it via a project reference)");

        string dir = Path.Combine(Path.GetTempPath(), "MultiplotNetE2E", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string sun = Path.Combine(dir, "redline.sun");
        WriteFixture(sun);
        string P(string n) => Path.Combine(dir, n);
        string script = P("redlines.wps");
        File.WriteAllText(script,
            "open " + sun + "\r\n" +
            "timestamp off\r\n" +
            "plot SINE\r\n" +
            "fixy -2,2,1\r\n" +
            "savepic PNG " + P("none.png") + "\r\n" +
            "redline 50,Out of range\r\n" +
            "savepic PNG " + P("outside.png") + "\r\n" +
            "redline 0.5,Upper limit,RIGHT\r\n" +
            "savepic PNG " + P("inside.png") + "\r\n" +
            "set color REDLINE,0,0,255\r\n" +
            "savepic PNG " + P("recolored.png") + "\r\n" +
            "fixy 1,3,1\r\n" +
            "savepic PNG " + P("shifted.png") + "\r\n" +
            "marker 2.5,Event\r\n" +
            "savepic PNG " + P("event.png") + "\r\n" +
            "set color MARKER,0,128,0\r\n" +
            "savepic PNG " + P("eventcolor.png") + "\r\n" +
            "markerset save " + P("a.markers") + "\r\n" +
            "clear redlines all\r\n" +
            "clear markers all\r\n" +
            "savepic PNG " + P("shiftednone.png") + "\r\n" +
            "fixy -2,2,1\r\n" +
            "savepic PNG " + P("cleared.png") + "\r\n" +
            "markerset load " + P("a.markers") + "\r\n" +
            "clear redline 1\r\n" +
            "markerset save " + P("b.markers") + "\r\n" +
            "exit\r\n");

        var (exited, code) = RunHidden(exe!, dir, script);
        Assert.True(exited, "Multiplot.exe did not exit within 60 s");
        Assert.Equal(0, code);
        foreach (var f in new[] { "none.png", "outside.png", "inside.png", "recolored.png", "shifted.png", "event.png", "eventcolor.png", "shiftednone.png", "cleared.png", "a.markers", "b.markers" })
            Assert.True(File.Exists(P(f)), f + " not written");
        byte[] R(string f) => File.ReadAllBytes(P(f));
        Assert.True(R("none.png").SequenceEqual(R("outside.png")), "redline outside the value range drawn");
        Assert.False(R("outside.png").SequenceEqual(R("inside.png")), "redline inside the value range not drawn");
        Assert.False(R("inside.png").SequenceEqual(R("recolored.png")), "SET COLOR REDLINE had no effect");
        Assert.True(R("shifted.png").SequenceEqual(R("shiftednone.png")), "redline drawn after the value scale moved past it");
        Assert.False(R("event.png").SequenceEqual(R("eventcolor.png")), "SET COLOR MARKER had no effect");
        Assert.True(R("none.png").SequenceEqual(R("cleared.png")), "redlines/markers not removed by CLEAR REDLINES/MARKERS ALL");

        var a = File.ReadAllLines(P("a.markers")).Where(l => !l.StartsWith(';')).ToArray();
        Assert.Equal(new[] { "2.5,Event", "redline 50,Out of range", "redline 0.5,Upper limit,RIGHT" }, a);
        Assert.Equal(new[] { "2.5,Event", "redline 0.5,Upper limit,RIGHT" }, File.ReadAllLines(P("b.markers")).Where(l => !l.StartsWith(';')).ToArray());
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    /// <summary>
    /// Issue #1: with two open files that share parameter names, a file-qualified name (what the Parameter Selection
    /// dialog now produces when one file is selected) loads from that file only, for PLOT and ADDPARAMETER, with a
    /// file-name or a full-path qualifier.
    /// </summary>
    [Fact]
    public void HiddenScript_FileQualifiedNamesLoadOneFile()
    {
        string? exe = FindExe();
        Assert.True(exe != null, "Multiplot.exe not built (the test project builds it via a project reference)");

        string dir = Path.Combine(Path.GetTempPath(), "MultiplotNetE2E", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string P(string n) => Path.Combine(dir, n);
        WriteFixture(P("rt_feed.sun"));
        WriteFixture(P("static_run.sun"));
        File.WriteAllText(P("qualified.wps"),
            "open " + P("rt_feed.sun") + "\r\n" +
            "open " + P("static_run.sun") + "\r\n" +
            "plot SINE\r\n" +
            "export " + P("both.csv") + "\r\n" +
            "plot rt_feed.sun:SINE\r\n" +
            "addparameter static_run.sun:SINE\r\n" +
            "export " + P("added.csv") + "\r\n" +
            "plot " + P("static_run.sun") + ":RAMP\r\n" +
            "export " + P("path.csv") + "\r\n" +
            "exit\r\n");

        var (exited, code) = RunHidden(exe!, dir, P("qualified.wps"));
        Assert.True(exited, "Multiplot.exe did not exit within 60 s");
        Assert.Equal(0, code);
        int Curves(string csv, string name)
        {
            Assert.True(File.Exists(P(csv)), csv + " not written");
            string header = File.ReadLines(P(csv)).First(l => l.Contains(name));
            return header.Split(',', '\t').Count(c => c.Trim().Trim('"').Equals(name, StringComparison.Ordinal));
        }
        Assert.Equal(2, Curves("both.csv", "SINE"));   // unqualified: one curve per file
        Assert.Equal(2, Curves("added.csv", "SINE"));  // one from each file, not four
        Assert.Equal(1, Curves("path.csv", "RAMP"));
        // the first export line names the source file of each curve
        string Sources(string csv) => File.ReadLines(P(csv)).First();
        Assert.Contains("rt_feed.sun", Sources("added.csv"));
        Assert.Contains("static_run.sun", Sources("added.csv"));
        Assert.DoesNotContain("rt_feed.sun", Sources("path.csv"));
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }
}
