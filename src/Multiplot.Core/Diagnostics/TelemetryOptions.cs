// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

using System.Net.Sockets;

namespace Multiplot.Core.Diagnostics;

/// <summary>Result of the telemetry start-up decision (NFR-TEL-001).</summary>
public enum TelemetryState
{
    /// <summary><c>MULTIPLOT_TELEMETRY</c> is not set: no telemetry, no network access (the default).</summary>
    NotRequested,
    /// <summary><c>OTEL_SDK_DISABLED=true</c> overrides the request.</summary>
    SdkDisabled,
    /// <summary>The endpoint is not a loopback <c>http</c> URL.</summary>
    InvalidEndpoint,
    /// <summary>Requested, but no collector accepts connections on the endpoint.</summary>
    CollectorNotRunning,
    /// <summary>Requested and the collector is reachable: telemetry is exported.</summary>
    Enabled,
}

/// <summary>
/// Telemetry configuration from environment variables. Telemetry is active only when
/// <c>MULTIPLOT_TELEMETRY</c> is <c>1</c>/<c>true</c>/<c>on</c>/<c>yes</c>, <c>OTEL_SDK_DISABLED</c> is not <c>true</c>,
/// the endpoint is a loopback address, and a collector is listening there.
/// </summary>
public sealed record TelemetryOptions(string Endpoint, int ExportTimeoutMs, int MetricIntervalMs)
{
    /// <summary>Switch that turns telemetry on.</summary>
    public const string EnableVariable = "MULTIPLOT_TELEMETRY";
    /// <summary>Optional OTLP/HTTP base endpoint override.</summary>
    public const string EndpointVariable = "MULTIPLOT_OTLP_ENDPOINT";
    /// <summary>Optional metric export interval override in milliseconds.</summary>
    public const string MetricIntervalVariable = "MULTIPLOT_OTEL_METRIC_INTERVAL_MS";
    /// <summary>Standard OpenTelemetry kill switch, honoured as well.</summary>
    public const string SdkDisabledVariable = "OTEL_SDK_DISABLED";
    /// <summary>Default endpoint: the local collector's OTLP/HTTP receiver (127.0.0.1, not localhost, to avoid an IPv6 fallback delay).</summary>
    public const string DefaultEndpoint = "http://127.0.0.1:4318";

    /// <summary>True when the variable value means "on".</summary>
    public static bool IsTrue(string? value) =>
        value?.Trim().ToLowerInvariant() is "1" or "true" or "on" or "yes";

    /// <summary>Reads the options; returns the state ignoring the collector probe (never <see cref="TelemetryState.CollectorNotRunning"/>).</summary>
    public static TelemetryState Read(Func<string, string?> env, out TelemetryOptions? options)
    {
        options = null;
        if (!IsTrue(env(EnableVariable))) return TelemetryState.NotRequested;
        if (IsTrue(env(SdkDisabledVariable))) return TelemetryState.SdkDisabled;
        string endpoint = env(EndpointVariable)?.Trim() is { Length: > 0 } e ? e : DefaultEndpoint;
        if (!TryParseLoopback(endpoint, out _)) return TelemetryState.InvalidEndpoint;
        int interval = int.TryParse(env(MetricIntervalVariable), System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out int iv) && iv >= 1000 ? iv : 15000;
        options = new TelemetryOptions(endpoint.TrimEnd('/'), 2000, interval);
        return TelemetryState.Enabled;
    }

    /// <summary>
    /// Full start-up decision: reads the environment and, only when telemetry is requested, probes the collector.
    /// </summary>
    public static TelemetryState Decide(Func<string, string?> env, Func<Uri, bool> collectorProbe, out TelemetryOptions? options)
    {
        var state = Read(env, out options);
        if (state != TelemetryState.Enabled) return state;
        if (!collectorProbe(new Uri(options!.Endpoint))) { options = null; return TelemetryState.CollectorNotRunning; }
        return TelemetryState.Enabled;
    }

    /// <summary>Decision against the process environment and the real collector probe.</summary>
    public static TelemetryState FromEnvironment(out TelemetryOptions? options) =>
        Decide(Environment.GetEnvironmentVariable, uri => ProbeCollector(uri, 300), out options);

    /// <summary>
    /// Accepts only <c>http://</c> URLs whose host is a loopback address (127.x.x.x, ::1 or localhost): the exception to
    /// NFR-SEC-001 covers the local collector only; forwarding off the machine is the collector's job.
    /// </summary>
    public static bool TryParseLoopback(string endpoint, out Uri? uri)
    {
        uri = null;
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttp) return false;
        if (!u.IsLoopback) return false;
        uri = u;
        return true;
    }

    /// <summary>True when something accepts a TCP connection on the endpoint's host and port within the timeout.</summary>
    public static bool ProbeCollector(Uri endpoint, int timeoutMs)
    {
        try
        {
            using var client = new TcpClient(endpoint.HostNameType == UriHostNameType.IPv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork);
            string host = endpoint.IsLoopback && endpoint.HostNameType == UriHostNameType.Dns ? "127.0.0.1" : endpoint.Host.Trim('[', ']');
            var connect = client.ConnectAsync(host, endpoint.Port);
            return connect.Wait(timeoutMs) && client.Connected;
        }
        catch (Exception ex) when (ex is SocketException or AggregateException or ObjectDisposedException) { return false; }
    }
}
