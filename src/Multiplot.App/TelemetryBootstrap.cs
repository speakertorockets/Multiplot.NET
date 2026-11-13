// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

using Microsoft.Extensions.Logging;
using Multiplot.Core.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Multiplot.App;

/// <summary>
/// Starts the OpenTelemetry SDK (NFR-TEL-001). Off by default: the providers are created only when
/// <c>MULTIPLOT_TELEMETRY=1</c> is set and a collector accepts connections on the loopback OTLP/HTTP endpoint;
/// otherwise this object does nothing and the application makes no network connection.
/// </summary>
internal sealed class TelemetryBootstrap : IDisposable
{
    private readonly TracerProvider? _tracer;
    private readonly MeterProvider? _meter;
    private readonly ILoggerFactory? _loggerFactory;
    private int _disposed;
    private string _endpoint = "";

    /// <summary>Outcome of the start-up decision (written to the diagnostic log by the caller).</summary>
    public TelemetryState State { get; }

    /// <summary>Unique id of this process in the telemetry (<c>service.instance.id</c>).</summary>
    public string InstanceId { get; } = Guid.NewGuid().ToString();

    /// <summary>True when telemetry is being exported.</summary>
    public bool Active => _tracer != null;

    public TelemetryBootstrap(bool hidden)
    {
        try
        {
            State = TelemetryOptions.FromEnvironment(out var options);
            if (State != TelemetryState.Enabled || options == null) return;
            _endpoint = options.Endpoint;

            var resource = ResourceBuilder.CreateDefault()
                .AddService(serviceName: "Multiplot", serviceVersion: MultiplotTelemetry.Version, serviceInstanceId: InstanceId)
                .AddAttributes(new KeyValuePair<string, object>[]
                {
                    new("process.pid", Environment.ProcessId),
                    new("host.name", Environment.MachineName),
                    new("user.name", Environment.UserName),
                    new("multiplot.mode", hidden ? "hidden" : "interactive"),
                    new("multiplot.session.id", Environment.GetEnvironmentVariable("SESSIONNAME") ?? ""),
                    new("multiplot.command_line", Environment.CommandLine),
                });

            // With HttpProtobuf and an endpoint set in code, the per-signal path must be included.
            void Otlp(OtlpExporterOptions o, string path)
            {
                o.Protocol = OtlpExportProtocol.HttpProtobuf;
                o.Endpoint = new Uri(options.Endpoint + path);
                o.TimeoutMilliseconds = options.ExportTimeoutMs; // keeps exit and a stopped collector cheap
            }

            _tracer = Sdk.CreateTracerProviderBuilder()
                .SetResourceBuilder(resource)
                .AddSource(MultiplotTelemetry.SourceWildcard)
                .SetSampler(new ParentBasedSampler(new AlwaysOnSampler()))
                .AddOtlpExporter(o => Otlp(o, "/v1/traces"))
                .Build();

            _meter = Sdk.CreateMeterProviderBuilder()
                .SetResourceBuilder(resource)
                .AddMeter(MultiplotTelemetry.SourceWildcard)
                .AddMeter("System.Runtime") // built-in .NET runtime metrics (GC, JIT, thread pool, exceptions)
                // per-file / per-parameter counters: room for many paths and names before the overflow series
                .AddView("multiplot.data.opens", new MetricStreamConfiguration { CardinalityLimit = MultiplotTelemetry.DetailCardinalityLimit })
                .AddView("multiplot.plot.parameters", new MetricStreamConfiguration { CardinalityLimit = MultiplotTelemetry.DetailCardinalityLimit })
                .AddOtlpExporter((o, reader) =>
                {
                    Otlp(o, "/v1/metrics");
                    reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = options.MetricIntervalMs;
                })
                .Build();

            _loggerFactory = LoggerFactory.Create(b => b
                .SetMinimumLevel(LogLevel.Information)
                .AddOpenTelemetry(o =>
                {
                    o.SetResourceBuilder(resource);
                    o.IncludeFormattedMessage = true;
                    o.AddOtlpExporter(e => Otlp(e, "/v1/logs"));
                }));
            var logger = _loggerFactory.CreateLogger("Multiplot");
            MultiplotTelemetry.LogSink = text => logger.LogInformation("{Message}", text);
        }
        catch (Exception ex)
        {
            // Telemetry must never stop Multiplot from starting.
            Console.Error.WriteLine("Multiplot telemetry start-up failed: " + ex.Message);
            DisposeProviders();
        }
    }

    /// <summary>Text for the diagnostic log.</summary>
    public string Describe() => State switch
    {
        TelemetryState.Enabled when Active => "Telemetry enabled, instance " + InstanceId + ", endpoint " + _endpoint,
        TelemetryState.Enabled => "Telemetry requested but the SDK failed to start",
        TelemetryState.CollectorNotRunning => "Telemetry requested but no OpenTelemetry collector is listening on " +
            (Environment.GetEnvironmentVariable(TelemetryOptions.EndpointVariable)?.Trim() is { Length: > 0 } e ? e : TelemetryOptions.DefaultEndpoint) +
            " (process " + Environment.ProcessId + "); telemetry is off",
        TelemetryState.InvalidEndpoint => "Telemetry requested but " + TelemetryOptions.EndpointVariable + " is not a loopback http URL; telemetry is off",
        TelemetryState.SdkDisabled => "Telemetry requested but " + TelemetryOptions.SdkDisabledVariable + "=true; telemetry is off",
        _ => "",
    };

    /// <summary>Pushes buffered telemetry (before an abnormal exit). Bounded by the timeout; never throws.</summary>
    public void Flush(int timeoutMs = 2000)
    {
        if (!Active) return;
        try
        {
            _tracer?.ForceFlush(timeoutMs);
            _meter?.ForceFlush(timeoutMs);
        }
        catch (Exception) { /* ignore */ }
    }

    /// <summary>Shuts the providers down, which flushes; bounded by the exporter timeout.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        DisposeProviders();
    }

    private void DisposeProviders()
    {
        MultiplotTelemetry.LogSink = null;
        try { _tracer?.Dispose(); } catch (Exception) { }
        try { _meter?.Dispose(); } catch (Exception) { }
        try { _loggerFactory?.Dispose(); } catch (Exception) { }
    }
}
