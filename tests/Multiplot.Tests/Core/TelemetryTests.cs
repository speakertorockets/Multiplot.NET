// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Sockets;
using Multiplot.Core.Diagnostics;
using Multiplot.DataAccess;
using Multiplot.Sun;

namespace Multiplot.Tests.Core;

/// <summary>Telemetry gate (NFR-TEL-001) and BCL instrumentation (NFR-TEL-002/003/005), no SDK involved.</summary>
public class TelemetryTests
{
    private static Func<string, string?> Env(params (string Key, string Value)[] vars)
    {
        var d = vars.ToDictionary(v => v.Key, v => (string?)v.Value, StringComparer.OrdinalIgnoreCase);
        return k => d.TryGetValue(k, out var v) ? v : null;
    }

    [Fact]
    public void Gate_is_off_by_default_and_does_not_probe()
    {
        bool probed = false;
        var state = TelemetryOptions.Decide(Env(), _ => probed = true, out var o);
        Assert.Equal(TelemetryState.NotRequested, state);
        Assert.Null(o);
        Assert.False(probed, "no network access may be attempted when telemetry is not requested");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("")]
    [InlineData("enabled")]
    public void Gate_ignores_values_that_are_not_on(string value)
    {
        bool probed = false;
        Assert.Equal(TelemetryState.NotRequested, TelemetryOptions.Decide(Env(("MULTIPLOT_TELEMETRY", value)), _ => probed = true, out _));
        Assert.False(probed);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData(" on ")]
    [InlineData("yes")]
    public void Gate_enables_when_requested_and_collector_runs(string value)
    {
        Uri? probedUri = null;
        var state = TelemetryOptions.Decide(Env(("MULTIPLOT_TELEMETRY", value)), u => { probedUri = u; return true; }, out var o);
        Assert.Equal(TelemetryState.Enabled, state);
        Assert.NotNull(o);
        Assert.Equal("http://127.0.0.1:4318", o!.Endpoint);
        Assert.Equal(new Uri("http://127.0.0.1:4318"), probedUri);
        Assert.Equal(15000, o.MetricIntervalMs);
        Assert.Equal(2000, o.ExportTimeoutMs);
    }

    [Fact]
    public void Gate_is_off_when_no_collector_is_running()
    {
        var state = TelemetryOptions.Decide(Env(("MULTIPLOT_TELEMETRY", "1")), _ => false, out var o);
        Assert.Equal(TelemetryState.CollectorNotRunning, state);
        Assert.Null(o);
    }

    [Fact]
    public void Gate_honours_OTEL_SDK_DISABLED()
    {
        bool probed = false;
        var state = TelemetryOptions.Decide(Env(("MULTIPLOT_TELEMETRY", "1"), ("OTEL_SDK_DISABLED", "true")), _ => probed = true, out _);
        Assert.Equal(TelemetryState.SdkDisabled, state);
        Assert.False(probed);
    }

    [Fact]
    public void Gate_reads_endpoint_and_interval_overrides()
    {
        var state = TelemetryOptions.Decide(Env(("MULTIPLOT_TELEMETRY", "1"), ("MULTIPLOT_OTLP_ENDPOINT", "http://127.0.0.1:14318/"),
            ("MULTIPLOT_OTEL_METRIC_INTERVAL_MS", "5000")), _ => true, out var o);
        Assert.Equal(TelemetryState.Enabled, state);
        Assert.Equal("http://127.0.0.1:14318", o!.Endpoint);
        Assert.Equal(5000, o.MetricIntervalMs);

        TelemetryOptions.Decide(Env(("MULTIPLOT_TELEMETRY", "1"), ("MULTIPLOT_OTEL_METRIC_INTERVAL_MS", "10")), _ => true, out o);
        Assert.Equal(15000, o!.MetricIntervalMs); // below 1 s is ignored
    }

    [Theory]
    [InlineData("http://127.0.0.1:4318", true)]
    [InlineData("http://localhost:4318", true)]
    [InlineData("http://[::1]:4318", true)]
    [InlineData("http://127.0.0.2:4318", true)]
    [InlineData("http://10.1.2.3:4318", false)]
    [InlineData("http://collector.example.com:4318", false)]
    [InlineData("https://127.0.0.1:4318", false)]
    [InlineData("127.0.0.1:4318", false)]
    [InlineData("not a url", false)]
    public void Endpoint_must_be_loopback_http(string endpoint, bool ok)
    {
        Assert.Equal(ok, TelemetryOptions.TryParseLoopback(endpoint, out _));
        var state = TelemetryOptions.Decide(Env(("MULTIPLOT_TELEMETRY", "1"), ("MULTIPLOT_OTLP_ENDPOINT", endpoint)), _ => true, out _);
        Assert.Equal(ok ? TelemetryState.Enabled : TelemetryState.InvalidEndpoint, state);
    }

    [Fact]
    public void Probe_detects_a_listening_collector()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            Assert.True(TelemetryOptions.ProbeCollector(new Uri("http://127.0.0.1:" + port), 2000));
            Assert.True(TelemetryOptions.ProbeCollector(new Uri("http://localhost:" + port), 2000));
        }
        finally { listener.Stop(); }
        Assert.False(TelemetryOptions.ProbeCollector(new Uri("http://127.0.0.1:" + port), 500));
    }

    [Fact]
    public void Log_forwards_to_sink_and_never_throws()
    {
        var prev = MultiplotTelemetry.LogSink;
        try
        {
            var lines = new List<string>();
            MultiplotTelemetry.LogSink = lines.Add;
            MultiplotTelemetry.Log("hello");
            Assert.Contains("hello", lines);
            MultiplotTelemetry.LogSink = _ => throw new InvalidOperationException("broken sink");
            MultiplotTelemetry.Log("ignored"); // must not throw
            MultiplotTelemetry.LogSink = null;
            MultiplotTelemetry.Log("no sink");
        }
        finally { MultiplotTelemetry.LogSink = prev; }
    }

    [Fact]
    public void No_activity_is_created_without_a_listener()
    {
        // "Multiplot" may be listened to by a parallel test; a source nobody listens to shows the disabled-path contract.
        using var unheard = new ActivitySource("Multiplot.Tests.Unheard");
        Assert.Null(unheard.StartActivity("x"));
    }

    [Fact]
    public void Data_open_emits_span_and_counter()
    {
        string dir = Path.Combine(Path.GetTempPath(), "MultiplotNetTel", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string ok = Path.Combine(dir, "fixture.sun");
        string missing = Path.Combine(dir, "missing.sun");
        EndToEnd.HiddenScriptTests.WriteFixture(ok);

        var spans = new ConcurrentBag<Activity>();
        using var al = new ActivityListener
        {
            ShouldListenTo = s => s.Name == MultiplotTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a => { if (a.OperationName == "data.open" && (a.GetTagItem("multiplot.file") as string)?.StartsWith(dir, StringComparison.Ordinal) == true) spans.Add(a); },
        };
        ActivitySource.AddActivityListener(al);

        var opens = new ConcurrentBag<(long Value, string Provider, string Result)>();
        using var ml = new MeterListener();
        ml.InstrumentPublished = (inst, l) => { if (inst.Meter.Name == MultiplotTelemetry.SourceName && inst.Name == "multiplot.data.opens") l.EnableMeasurementEvents(inst); };
        ml.SetMeasurementEventCallback<long>((inst, v, tags, _) =>
        {
            string p = "", r = "";
            foreach (var t in tags)
            {
                if (t.Key == "multiplot.provider") p = t.Value as string ?? "";
                if (t.Key == "multiplot.result") r = t.Value as string ?? "";
            }
            opens.Add((v, p, r));
        });
        ml.Start();

        using (var svc = new DataAccessService())
        {
            using var f = svc.Open(ok);
            Assert.NotNull(f);
            Assert.Null(svc.Open(missing));
        }

        var okSpan = Assert.Single(spans, a => (string?)a.GetTagItem("multiplot.file") == ok);
        Assert.Equal("SUN", okSpan.GetTagItem("multiplot.provider"));
        Assert.Equal(ActivityStatusCode.Unset, okSpan.Status);
        var badSpan = Assert.Single(spans, a => (string?)a.GetTagItem("multiplot.file") == missing);
        Assert.Equal(ActivityStatusCode.Error, badSpan.Status);
        Assert.Contains(opens, o => o.Value == 1 && o.Provider == "SUN" && o.Result == "ok");
        Assert.Contains(opens, o => o.Value == 1 && o.Result == "error");
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    [Theory]
    [InlineData("Multiplot.Plugins.Abstractions", true)]
    [InlineData("System.Diagnostics.DiagnosticSource", true)]
    [InlineData("OpenTelemetry", true)]
    [InlineData("OpenTelemetry.Api", true)]
    [InlineData("OpenTelemetryX", false)]
    [InlineData("Parquet.Net", false)]
    [InlineData(null, false)]
    public void Plugins_share_telemetry_assemblies_with_host(string? name, bool shared)
    {
        Assert.Equal(shared, DataAccessService.IsSharedWithHost(name));
    }
}
