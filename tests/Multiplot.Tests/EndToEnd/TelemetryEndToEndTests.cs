// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Multiplot.Tests.EndToEnd;

/// <summary>
/// Runs Multiplot.exe /hide against a minimal fake OTLP/HTTP collector on a loopback port (NFR-TEL-001..004):
/// with MULTIPLOT_TELEMETRY=1 it exports traces, metrics and logs; without it, it never connects.
/// </summary>
public class TelemetryEndToEndTests
{
    /// <summary>Accepts HTTP/1.1 POSTs, records path and body, answers 200 with an empty protobuf body.</summary>
    private sealed class FakeCollector : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _cts = new();
        public ConcurrentQueue<(string Path, byte[] Body)> Requests { get; } = new();
        public int Connections;
        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public FakeCollector()
        {
            _listener.Start();
            _ = AcceptLoop();
        }

        private async Task AcceptLoop()
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient c;
                try { c = await _listener.AcceptTcpClientAsync(_cts.Token); }
                catch (Exception) { return; }
                Interlocked.Increment(ref Connections);
                _ = Task.Run(() => Serve(c));
            }
        }

        private async Task Serve(TcpClient client)
        {
            using (client)
            {
                try
                {
                    var s = client.GetStream();
                    while (true)
                    {
                        string? head = await ReadHeaders(s);
                        if (head == null) return;
                        var lines = head.Split("\r\n");
                        string path = lines[0].Split(' ')[1];
                        int len = 0;
                        bool chunked = false;
                        foreach (var l in lines.Skip(1))
                        {
                            int i = l.IndexOf(':');
                            if (i < 0) continue;
                            string k = l[..i].Trim(), v = l[(i + 1)..].Trim();
                            if (k.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) len = int.Parse(v);
                            if (k.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) && v.Contains("chunked", StringComparison.OrdinalIgnoreCase)) chunked = true;
                        }
                        byte[] body = chunked ? await ReadChunked(s) : await ReadExactly(s, len);
                        Requests.Enqueue((path, body));
                        byte[] resp = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/x-protobuf\r\nContent-Length: 0\r\n\r\n");
                        await s.WriteAsync(resp);
                    }
                }
                catch (Exception) { /* client went away */ }
            }
        }

        private static async Task<string?> ReadHeaders(NetworkStream s)
        {
            var sb = new StringBuilder();
            var b = new byte[1];
            while (true)
            {
                if (await s.ReadAsync(b) == 0) return null;
                sb.Append((char)b[0]);
                if (sb.Length >= 4 && sb[^1] == '\n' && sb[^2] == '\r' && sb[^3] == '\n' && sb[^4] == '\r') return sb.ToString(0, sb.Length - 4);
            }
        }

        private static async Task<byte[]> ReadExactly(NetworkStream s, int n)
        {
            var buf = new byte[n];
            await s.ReadExactlyAsync(buf);
            return buf;
        }

        private static async Task<byte[]> ReadChunked(NetworkStream s)
        {
            var ms = new MemoryStream();
            while (true)
            {
                string line = "";
                var b = new byte[1];
                while (!line.EndsWith("\r\n", StringComparison.Ordinal)) { await s.ReadExactlyAsync(b); line += (char)b[0]; }
                int size = Convert.ToInt32(line.Trim().Split(';')[0], 16);
                if (size == 0) { await ReadExactly(s, 2); return ms.ToArray(); }
                ms.Write(await ReadExactly(s, size));
                await ReadExactly(s, 2);
            }
        }

        public string AllText(string path) =>
            string.Concat(Requests.Where(r => r.Path == path).Select(r => Encoding.UTF8.GetString(r.Body)));

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
        }
    }

    private static (bool Exited, int ExitCode) Run(string exe, string dir, string script, IDictionary<string, string?> env)
    {
        var psi = new ProcessStartInfo(exe, "/hide /nosave /noplugins \"" + script + "\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = dir,
        };
        psi.Environment["USERPROFILE"] = dir;
        psi.Environment["MULTIPLOTNET_REGISTRY_ROOT"] = "Software\\MultiplotNetTests\\" + Path.GetFileName(dir);
        foreach (var kv in env)
        {
            if (kv.Value == null) psi.Environment.Remove(kv.Key);
            else psi.Environment[kv.Key] = kv.Value;
        }
        using var p = Process.Start(psi)!;
        bool exited = p.WaitForExit(60000);
        if (!exited) { try { p.Kill(true); } catch (InvalidOperationException) { } }
        try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree("Software\\MultiplotNetTests\\" + Path.GetFileName(dir), false); } catch (UnauthorizedAccessException) { }
        return (exited, exited ? p.ExitCode : -1);
    }

    private static (string Dir, string Sun, string Script) Fixture()
    {
        string dir = Path.Combine(Path.GetTempPath(), "MultiplotNetE2E", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string sun = Path.Combine(dir, "telemetry.sun");
        HiddenScriptTests.WriteFixture(sun);
        string script = Path.Combine(dir, "telemetry.wps");
        File.WriteAllText(script,
            "open " + sun + "\r\n" +
            "plot SINE,RAMP\r\n" +
            "export " + Path.Combine(dir, "out.csv") + "\r\n" +
            "nosuchcommand 42\r\n" +
            "exit\r\n");
        return (dir, sun, script);
    }

    [Fact]
    public void Enabled_exports_traces_metrics_and_logs_to_the_collector()
    {
        string? exe = HiddenScriptTests.FindExe();
        Assert.True(exe != null, "Multiplot.exe not built (the test project builds it via a project reference)");
        using var collector = new FakeCollector();
        var (dir, sun, script) = Fixture();

        var (exited, code) = Run(exe!, dir, script, new Dictionary<string, string?>
        {
            ["MULTIPLOT_TELEMETRY"] = "1",
            ["MULTIPLOT_OTLP_ENDPOINT"] = "http://127.0.0.1:" + collector.Port,
            ["OTEL_SDK_DISABLED"] = null,
        });
        Assert.True(exited, "Multiplot.exe did not exit within 60 s");
        Assert.Equal(0, code);

        var paths = collector.Requests.Select(r => r.Path).Distinct().ToList();
        Assert.Contains("/v1/traces", paths);
        Assert.Contains("/v1/metrics", paths);
        Assert.Contains("/v1/logs", paths);

        // protobuf strings are plain UTF-8, so names and attribute values can be found in the payload
        string traces = collector.AllText("/v1/traces");
        Assert.Contains("Multiplot", traces);                // service.name
        Assert.Contains("service.instance.id", traces);
        Assert.Contains("multiplot.mode", traces);
        Assert.Contains("hidden", traces);
        Assert.Contains("script.run", traces);
        Assert.Contains("telemetry.wps", traces);            // script path (not sensitive per owner decision)
        Assert.Contains("multiplot.command", traces);
        Assert.Contains("plot SINE,RAMP", traces);           // full command text
        Assert.Contains("data.open", traces);
        Assert.Contains(sun, traces);
        Assert.Contains("load.parameters", traces);
        Assert.Contains("export", traces);
        Assert.Contains("unrecognized command", traces);     // error status of the bad command

        string metrics = collector.AllText("/v1/metrics");
        Assert.Contains("multiplot.commands", metrics);
        Assert.Contains("multiplot.load.duration", metrics);
        Assert.Contains("multiplot.script.errors", metrics);

        string logs = collector.AllText("/v1/logs");
        Assert.Contains("Telemetry enabled", logs);
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    [Fact]
    public void Not_requested_makes_no_connection()
    {
        string? exe = HiddenScriptTests.FindExe();
        Assert.True(exe != null, "Multiplot.exe not built (the test project builds it via a project reference)");
        using var collector = new FakeCollector();
        var (dir, _, script) = Fixture();

        var (exited, code) = Run(exe!, dir, script, new Dictionary<string, string?>
        {
            ["MULTIPLOT_TELEMETRY"] = null,
            ["MULTIPLOT_OTLP_ENDPOINT"] = "http://127.0.0.1:" + collector.Port,
        });
        Assert.True(exited, "Multiplot.exe did not exit within 60 s");
        Assert.Equal(0, code);
        Assert.Equal(0, Volatile.Read(ref collector.Connections));
        Assert.True(File.Exists(Path.Combine(dir, "out.csv")));
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    [Fact]
    public void Requested_without_collector_runs_normally()
    {
        string? exe = HiddenScriptTests.FindExe();
        Assert.True(exe != null, "Multiplot.exe not built (the test project builds it via a project reference)");
        int port;
        using (var probe = new FakeCollector()) port = probe.Port; // a port that was free and is now closed
        var (dir, _, script) = Fixture();

        // the diagnostic log is shared with every other run today, so count this exact line before and after
        string endpoint = "http://127.0.0.1:" + port;
        string expected = "no OpenTelemetry collector is listening on " + endpoint + " (process ";
        static int CountLines(string text)
        {
            string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Multiplot", "logs");
            if (!Directory.Exists(logDir)) return 0;
            int n = 0;
            foreach (var f in Directory.GetFiles(logDir, "multiplot-*.log"))
            {
                string all;
                try
                {
                    using var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    all = new StreamReader(fs).ReadToEnd();
                }
                catch (IOException) { continue; }
                for (int i = all.IndexOf(text, StringComparison.Ordinal); i >= 0; i = all.IndexOf(text, i + 1, StringComparison.Ordinal)) n++;
            }
            return n;
        }
        int before = CountLines(expected);

        var (exited, code) = Run(exe!, dir, script, new Dictionary<string, string?>
        {
            ["MULTIPLOT_TELEMETRY"] = "1",
            ["MULTIPLOT_OTLP_ENDPOINT"] = endpoint,
        });
        Assert.True(exited, "Multiplot.exe did not exit within 60 s");
        Assert.Equal(0, code);
        Assert.True(File.Exists(Path.Combine(dir, "out.csv")));
        Assert.True(CountLines(expected) > before, "the diagnostic log does not say that no collector is listening on " + endpoint);
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }
}
