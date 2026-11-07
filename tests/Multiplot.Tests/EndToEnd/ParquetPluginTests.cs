// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using Multiplot.Core;
using Multiplot.DataAccess;
using Multiplot.Plugins;
using Multiplot.Sun;
using MultiToParquet;
using Parquet;
using Parquet.Schema;

namespace Multiplot.Tests.EndToEnd;

/// <summary>
/// The bundled Parquet plugin: loads from plugins\Multiplot.Parquet\, reads converter output exactly like the
/// SUN reader reads the source .multi file, reads generic TIMESTAMP files, and works in the real application.
/// </summary>
public class ParquetPluginTests
{
    private sealed class Host : IPluginHost, IUserInteraction
    {
        public List<string> Messages { get; } = new();
        public string UserFolder => Path.GetTempPath();
        public string TempFolder => Path.GetTempPath();
        public bool ScriptRunning => false;
        public bool DialogsAllowed => false;
        public void RecordScriptLine(string line) { }
        public void Log(string message) => Messages.Add(message);
        public IUserInteraction Ui => this;
        public PromptResult Show(string message, string caption, PromptButtons buttons, PromptIcon icon, PromptResult defaultResult) => defaultResult;
        public string? Input(string prompt, string caption, string defaultValue) => null;
    }

    private static string Config => AppContext.BaseDirectory.Contains("\\Release\\", StringComparison.OrdinalIgnoreCase) ? "Release" : "Debug";
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    private static string AppDir => Path.Combine(Root, "src", "Multiplot.App", "bin", Config, "net10.0-windows");
    private static string PluginsDir => Path.Combine(AppDir, "plugins");

    private static string NewDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "MultiplotNetParquet", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>MULTITIME64 file with t8, d8 and f4 indexes, a dropout, an empty parameter and an EOF event.</summary>
    private static void WriteMulti(string path)
    {
        const int n = 500;
        var t8 = new TimeTag[n];
        var d8t = new double[n];
        var f4t = new float[n];
        var press = new double[n];
        var temp = new float[n];
        var count = new short[n];
        for (int i = 0; i < n; i++)
        {
            long us = 12L * 3600 * 1_000_000 + i * 100_003L; // 12:00:00 + i * 100.003 ms
            t8[i] = new TimeTag(100, (uint)(us / 1000), (ushort)(us % 1000));
            d8t[i] = i * 0.1 + 0.05;
            f4t[i] = i * 0.2f;
            press[i] = 1000.0 + Math.Sin(i / 20.0) * 50.0;
            temp[i] = 20f + i * 0.01f;
            count[i] = (short)(i % 17 - 8);
        }
        temp[42] = DataTypes.FpDropOut;
        using var w = SunWriter.Create(path, SunCreateMode.Overwrite, 1024, 4, 0, SunSignatures.MultiTime64);
        w.SetRefTime(2023, 100, 12, 0, 0, 0);
        w.SetTitle("Parquet fixture");
        w.SetTestStand("Stand 3");
        w.AddParm("PRESS", "P1", "kPa", "Pressure", 1, "d8", press, "t8", t8, n, SunWriteOptions.LzhCompress, null);
        w.AddParm("TEMP", "T1", "C", "Temperature", 2, "f4", temp, "d8", d8t, n, SunWriteOptions.None, null);
        w.AddParm("COUNT", "C1", "cnt", "Counter", 3, "i2", count, "f4", f4t, n, SunWriteOptions.LzhCompress, null);
        w.AddParm("NODATA", "N1", "", "Declared only", 4, "f4", Array.Empty<float>(), "f4", Array.Empty<float>(), 0, SunWriteOptions.None, null);
        w.SetCommentText("[events]\nEOF=100/43300000.0\n[coef]\nMSID=PRESS\n");
        w.Close();
    }

    private static async Task ConvertAsync(string multi, string parquet)
    {
        using var a = MultiTimeArchive.Open(multi);
        var all = Enumerable.Range(0, a.File.Parameters.Count).Select(a.ReadParameter).ToList();
        await ParquetExport.WriteAsync(a, all, parquet, CompressionMethod.Zstd, 128); // several row groups
    }

    private static DataAccessService LoadService(Host host)
    {
        Assert.True(File.Exists(Path.Combine(PluginsDir, "Multiplot.Parquet", "Multiplot.Parquet.dll")), "plugin not deployed beside Multiplot.exe");
        var svc = new DataAccessService();
        var r = svc.LoadPlugins(new[] { PluginsDir }, new HashSet<string>(StringComparer.OrdinalIgnoreCase), host);
        Assert.Empty(r.Warnings);
        Assert.Contains(svc.Plugins, p => p.FileName == "Multiplot.Parquet.dll");
        return svc;
    }

    [Fact]
    public async Task ConvertedFile_ReadsLikeTheSourceMultiFile()
    {
        string dir = NewDir();
        string multi = Path.Combine(dir, "fixture.multi"), parquet = Path.Combine(dir, "fixture.parquet");
        WriteMulti(multi);
        await ConvertAsync(multi, parquet);

        var svc = LoadService(new Host());
        try
        {
            Assert.Equal(0x31524150u, svc.IsSupportedType(parquet));
            using var sun = svc.Open(multi)!;
            using var pq = svc.Open(parquet)!;
            Assert.NotNull(pq);
            Assert.False(pq.Info.IsDynamic);
            Assert.Equal(sun.Info.Title, pq.Info.Title);
            Assert.Equal(sun.Info.TestStand, pq.Info.TestStand);
            Assert.Equal(sun.Info.RefUtc, pq.Info.RefUtc);
            Assert.Equal(sun.Info.RefTime, pq.Info.RefTime);
            Assert.Equal(sun.Events, pq.Events);
            Assert.Contains("[coef]", pq.CommentBlock);
            Assert.Equal(sun.Parameters.Select(p => p.Name), pq.Parameters.Select(p => p.Name));
            Assert.Equal(sun.Parameters.Select(p => p.Units), pq.Parameters.Select(p => p.Units));
            Assert.Equal(1, pq.FindParameter("t1", out int list)); // alternate name
            Assert.Equal(1, list);

            foreach (var (start, stop) in new[] { (-1e6, 1e6), (10.0, 20.0), (0.0, 0.0), (49.0, 49.95) })
            {
                foreach (string name in new[] { "PRESS", "TEMP", "COUNT" })
                {
                    var req = new ParameterRequest(name, start, stop, null, null, DataType.Float, true);
                    string ctx = $"{name} [{start},{stop}]";
                    ParameterData a;
                    try { a = sun.Read(req)!; }
                    catch (DataAccessException ex)
                    {
                        var ex2 = Assert.Throws<DataAccessException>(() => pq.Read(req));
                        Assert.True(ex.Code == ex2.Code, $"{ctx}: error {ex.Code} vs {ex2.Code}");
                        continue;
                    }
                    var b = pq.Read(req)!;
                    // The SUN reader skips the EOF hold point for f4-indexed parameters (no UTC);
                    // Parquet time is always absolute, so the plugin applies it to every parameter.
                    bool eofExtra = name == "COUNT" && stop > 100;
                    Assert.True(a.Points + (eofExtra ? 1 : 0) == b.Points, $"{ctx}: points {a.Points} vs {b.Points}");
                    if (eofExtra) Assert.Equal(100f, b.Time[b.Points - 1]);
                    for (int i = 0; i < a.Points; i++)
                    {
                        Assert.True(BitConverter.SingleToUInt32Bits(a.Values[i]) == BitConverter.SingleToUInt32Bits(b.Values[i]), $"{ctx}: value {i}");
                        Assert.True(Math.Abs(a.Time[i] - b.Time[i]) <= 1e-5 * Math.Max(1, Math.Abs(a.Time[i])), $"{ctx}: time {i} {a.Time[i]} vs {b.Time[i]}");
                        // samples match exactly; a window end point clamped by the SUN reader can differ by 1 us (TimeMath.Utc rounding)
                        if (a.HasValidUtc) Assert.True(Math.Abs(TimeMath.Time(a.UtcAt(i), b.UtcAt(i))) <= 1.5e-6, $"{ctx}: utc {i} {a.UtcAt(i)}.{a.UtcAt(i).MicroSeconds} vs {b.UtcAt(i)}.{b.UtcAt(i).MicroSeconds}");
                    }
                    // the SUN reader scans min/max before replacing the clamped trailing value; the plugin scans the
                    // returned points, so they agree when nothing is clamped
                    if (start < -1e5)
                    {
                        Assert.Equal(a.MinValue, b.MinValue);
                        Assert.Equal(a.MaxValue, b.MaxValue);
                    }
                }
            }
            Assert.True(pq.Read(new ParameterRequest("TEMP", -1e6, 1e6))!.IsDropOut(42));
            Assert.Throws<NoDataException>(() => pq.Read(new ParameterRequest("NODATA", -1e6, 1e6)));
            var nf = Assert.Throws<DataAccessException>(() => pq.Read(new ParameterRequest("MISSING", -1e6, 1e6)));
            Assert.Equal(SunError.PidNotFound, nf.SunCode);
            var ts = Assert.Throws<DataAccessException>(() => pq.Read(new ParameterRequest("PRESS", 5000, 6000)));
            Assert.Equal(SunError.TimeSlice, ts.SunCode);

            // reading relative to another reference (UTC and relative offset)
            var shifted = pq.Read(new ParameterRequest("PRESS", -1e6, 1e6, 2.5, new TimeTag(100, 12 * 3600000 + 1000, 0)))!;
            var plain = sun.Read(new ParameterRequest("PRESS", -1e6, 1e6, 2.5, new TimeTag(100, 12 * 3600000 + 1000, 0)))!;
            Assert.Equal(plain.Time[3], shifted.Time[3], 4);
        }
        finally
        {
            svc.UnloadPlugins();
        }
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    /// <summary>
    /// Event markers resolve identically from the SUN [events] comment section and the parquet "events"
    /// metadata: file events are drawn (except FILE_REF / EOF), user markers resolve UTC, seconds and
    /// event-name timestamps against the plot reference.
    /// </summary>
    [Fact]
    public async Task EventMarkers_ResolveFromSunAndParquetHeaders()
    {
        string dir = NewDir();
        string multi = Path.Combine(dir, "events.multi"), parquet = Path.Combine(dir, "events.parquet");
        const int n = 100;
        var t8 = new TimeTag[n];
        var v = new double[n];
        for (int i = 0; i < n; i++) { t8[i] = new TimeTag(100, (uint)(12 * 3600000 + i * 1000), 0); v[i] = i; }
        using (var w = SunWriter.Create(multi, SunCreateMode.Overwrite, 1024, 4, 0, SunSignatures.MultiTime64))
        {
            w.SetRefTime(2023, 100, 12, 0, 0, 0);
            w.SetTitle("Events fixture");
            w.AddParm("PRESS", "P1", "kPa", "Pressure", 1, "d8", v, "t8", t8, n, SunWriteOptions.None, null);
            w.SetCommentText("[events]\nFILE_REF=100/43200000.0\nIGNITION=100/12:00:10.000\nVALVE=100/43230500.0\nEOF=100/43299000.0\n");
            w.Close();
        }
        await ConvertAsync(multi, parquet);

        var svc = LoadService(new Host());
        try
        {
            foreach (string path in new[] { multi, parquet })
            {
                using var f = svc.Open(path)!;
                var files = new List<IDataFile> { f };
                var groups = new List<Multiplot.Core.Model.FileGroup> { new() { StartPos = 0, NFiles = 1 } };
                var user = new List<Multiplot.Core.Model.Marker>
                {
                    new("100/12:00:05", "five"),
                    new("20", "twenty", true),
                    new("IGNITION+2", "after ignition"),
                    new("NOSUCHEVENT", "unresolved"),
                };
                var r = EventMarkers.Resolve(user, true, files, groups, new[] { 0 }, "NONE");
                string ctx = Path.GetExtension(path);
                Assert.True(r.Count == 5, ctx + ": " + string.Join("; ", r));
                Assert.Equal(new ResolvedMarker(5, "five", false), r[0]);
                Assert.Equal(new ResolvedMarker(20, "twenty", true), r[1]);
                Assert.Equal("after ignition", r[2].Text);
                Assert.Equal(12, r[2].Time, 6);
                Assert.Equal("IGNITION", r[3].Text);
                Assert.Equal(10, r[3].Time, 6);
                Assert.Equal("VALVE", r[4].Text);
                Assert.Equal(30.5, r[4].Time, 6);

                // against a reference event the axis shifts (file events follow their group's reference);
                // without file events only user markers remain
                groups[0].TRef = "IGNITION";
                var shifted = EventMarkers.Resolve(user.Take(1).ToList(), true, files, groups, new[] { 0 }, "IGNITION");
                Assert.Equal(-5, shifted[0].Time, 6);
                Assert.Equal(0, shifted[1].Time, 6);
                Assert.Single(EventMarkers.Resolve(user.Take(1).ToList(), false, files, groups, new[] { 0 }, "NONE"));
            }
        }
        finally
        {
            svc.UnloadPlugins();
        }
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    [Fact]
    public async Task GenericTimestampFile_IsReadable()
    {
        string dir = NewDir();
        string path = Path.Combine(dir, "generic.parquet");
        var ts = new DataField<DateTime>("ts");
        var temp = new DataField<float?>("temp");
        var cnt = new DataField<int>("count");
        var name = new DataField<string>("label");
        var t0 = new DateTime(2025, 3, 1, 8, 30, 0, DateTimeKind.Utc);
        await using (var fs = File.Create(path))
        await using (var w = await ParquetWriter.CreateAsync(new ParquetSchema(ts, temp, cnt, name), fs))
        {
            using var rg = w.CreateRowGroup();
            await rg.WriteAsync(ts, new ReadOnlyMemory<DateTime>(new[] { t0, t0.AddSeconds(1), t0.AddSeconds(2.5), t0.AddSeconds(4) }));
            await rg.WriteAsync(temp, new ReadOnlyMemory<float?>(new float?[] { 1.5f, null, 3.5f, float.NaN }));
            await rg.WriteAsync(cnt, new ReadOnlyMemory<int>(new[] { 10, 20, 30, 40 }));
            await rg.WriteAsync(name, new[] { "a", "b", "c", "d" });
        }

        var svc = LoadService(new Host());
        try
        {
            using var f = svc.Open(path)!;
            Assert.NotNull(f);
            Assert.Equal(new[] { "temp", "count" }, f.Parameters.Select(p => p.Name)); // strings are not parameters
            Assert.Equal(new TimeTag((short)t0.DayOfYear, 8 * 3600000 + 30 * 60000, 0), f.Info.RefUtc);
            Assert.Equal(2025u, f.Info.RefTime[0]);
            var p = f.Read(new ParameterRequest("temp", -100, 100, null, null, DataType.Float, true))!;
            Assert.Equal(3, p.Points); // null row skipped, NaN kept as a dropout
            Assert.Equal(new[] { 0f, 2.5f, 4f }, p.Time.Take(3));
            Assert.Equal(1.5f, p.Values[0]);
            Assert.True(p.IsDropOut(2));
            Assert.Equal(new TimeTag((short)t0.DayOfYear, 8 * 3600000 + 30 * 60000 + 2500, 0), p.UtcAt(1));
            var c = f.Read(new ParameterRequest("count", 0.5, 3))!;
            Assert.Equal(new[] { 0.5f, 1f, 2.5f, 3f }, c.Time.Take(c.Points)); // lead-in and trailing points clamped to the window
        }
        finally
        {
            svc.UnloadPlugins();
        }
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TimestampAndSecondsColumns_WindowedReadsAcrossRowGroups(bool seconds)
    {
        string dir = NewDir();
        string path = Path.Combine(dir, seconds ? "seconds.parquet" : "timestamps.parquet");
        var t0 = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        DataField timeField = seconds ? new DataField<double>("time") : new DataField<DateTime>("ts");
        var val = new DataField<double?>("val");
        await using (var fs = File.Create(path))
        await using (var w = await ParquetWriter.CreateAsync(new ParquetSchema(timeField, val), fs))
        {
            if (seconds) w.CustomMetadata = new Dictionary<string, string> { ["multiplot.reference.iso"] = "2025-06-01T00:00:00Z" };
            for (int g = 0; g < 5; g++) // 5 row groups of 100 rows, 10 ms apart
            {
                using var rg = w.CreateRowGroup();
                var idx = Enumerable.Range(g * 100, 100).ToArray();
                if (seconds) await rg.WriteAsync((DataField<double>)timeField, new ReadOnlyMemory<double>(idx.Select(i => i * 0.01).ToArray()));
                else await rg.WriteAsync((DataField<DateTime>)timeField, new ReadOnlyMemory<DateTime>(idx.Select(i => t0.AddTicks(i * 100_000L)).ToArray()));
                await rg.WriteAsync(val, new ReadOnlyMemory<double?>(idx.Select(i => (double?)i).ToArray()));
            }
        }

        var svc = LoadService(new Host());
        try
        {
            using var f = svc.Open(path)!;
            Assert.Equal(new TimeTag(152, 0, 0), f.Info.RefUtc); // 1 June 2025 = day 152
            var all = f.Read(new ParameterRequest("val", -1e6, 1e6))!;
            Assert.Equal(500, all.Points);
            Assert.Equal(4.99f, all.Time[499], 4);
            var win = f.Read(new ParameterRequest("val", 2.505, 2.535, null, null, DataType.Float, true))!; // middle row group
            Assert.Equal(new[] { 250f, 251f, 252f, 253f, 253f }, win.Values.Take(win.Points));
            Assert.Equal(new TimeTag(152, 2510, 0), win.UtcAt(1));
        }
        finally
        {
            svc.UnloadPlugins();
        }
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    [Fact]
    public async Task HiddenScript_PlotsAndExportsParquetData()
    {
        string exe = Path.Combine(AppDir, "Multiplot.exe");
        Assert.True(File.Exists(exe), "Multiplot.exe not built");
        string dir = NewDir();
        string multi = Path.Combine(dir, "fixture.multi"), parquet = Path.Combine(dir, "fixture.parquet");
        WriteMulti(multi);
        await ConvertAsync(multi, parquet);
        string csv = Path.Combine(dir, "plot.csv");
        string script = Path.Combine(dir, "test.wps");
        File.WriteAllText(script, "open " + parquet + "\r\nplot PRESS,TEMP\r\nexport " + csv + "\r\nexit\r\n");

        var psi = new ProcessStartInfo(exe, "/hide /nosave \"" + script + "\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = dir,
        };
        string reg = "Software\\MultiplotNetTests\\" + Path.GetFileName(dir);
        psi.Environment["USERPROFILE"] = dir;
        psi.Environment["MULTIPLOTNET_REGISTRY_ROOT"] = reg;
        using var p = Process.Start(psi)!;
        bool exited = p.WaitForExit(60000);
        if (!exited) { try { p.Kill(true); } catch (InvalidOperationException) { } }
        try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(reg, false); } catch (UnauthorizedAccessException) { }

        Assert.True(exited, "Multiplot.exe did not exit within 60 s");
        Assert.Equal(0, p.ExitCode);
        Assert.True(File.Exists(csv), "CSV not written");
        var lines = File.ReadAllLines(csv);
        Assert.True(lines.Length > 400, "CSV has " + lines.Length + " lines");
        Assert.Contains(lines.Take(5), l => l.Contains("PRESS") && l.Contains("TEMP"));
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }
}
