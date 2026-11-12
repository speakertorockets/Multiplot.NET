// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Multiplot.Core.Diagnostics;

/// <summary>
/// Names and instruments shared by all Multiplot telemetry (NFR-TEL-001..006). BCL only: no OpenTelemetry SDK
/// dependency. Without a listener (telemetry disabled, the default) <see cref="ActivitySource.StartActivity(string, ActivityKind)"/>
/// returns null and the instruments record nothing, so the cost is negligible.
/// </summary>
/// <remarks>
/// The static source, meter and instruments are an accepted exception to ARC-012: they are created once and
/// never change. <see cref="LogSink"/> is set once by the host at start-up.
/// </remarks>
public static class MultiplotTelemetry
{
    /// <summary>Name of the activity source and meter; plugins use <c>Multiplot.&lt;PluginName&gt;</c>.</summary>
    public const string SourceName = "Multiplot";

    /// <summary>Wildcard the host subscribes to (covers plugin sources too).</summary>
    public const string SourceWildcard = "Multiplot*";

    /// <summary>Assembly version reported as <c>service.version</c>.</summary>
    public static readonly string Version = typeof(MultiplotTelemetry).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    /// <summary>Trace source.</summary>
    public static readonly ActivitySource Source = new(SourceName, Version);

    /// <summary>Metric meter.</summary>
    public static readonly Meter Meter = new(SourceName, Version);

    /// <summary>Time to render one plot window.</summary>
    public static readonly Histogram<double> PlotDrawMs =
        Meter.CreateHistogram<double>("multiplot.plot.draw.duration", "ms", "Time to render one plot window");

    /// <summary>Time to load the curves of one plot.</summary>
    public static readonly Histogram<double> LoadMs =
        Meter.CreateHistogram<double>("multiplot.load.duration", "ms", "Time to load the curves of one plot");

    /// <summary>Samples loaded from data files.</summary>
    public static readonly Counter<long> PointsLoaded =
        Meter.CreateCounter<long>("multiplot.load.points", "{point}", "Samples loaded from data files");

    /// <summary>Data files opened (tag <c>multiplot.result</c> = ok / error).</summary>
    public static readonly Counter<long> FilesOpened =
        Meter.CreateCounter<long>("multiplot.data.opens", "{file}", "Data file open attempts");

    /// <summary>Commands executed (tag <c>multiplot.command</c>).</summary>
    public static readonly Counter<long> Commands =
        Meter.CreateCounter<long>("multiplot.commands", "{command}", "Commands executed");

    /// <summary>Script or command errors.</summary>
    public static readonly Counter<long> ScriptErrors =
        Meter.CreateCounter<long>("multiplot.script.errors", "{error}", "Script and command errors");

    /// <summary>Real-time refresh cycles.</summary>
    public static readonly Counter<long> RealtimeRefreshes =
        Meter.CreateCounter<long>("multiplot.realtime.refreshes", "{refresh}", "Real-time refresh cycles");

    /// <summary>Duration of one real-time refresh cycle.</summary>
    public static readonly Histogram<double> RealtimeRefreshMs =
        Meter.CreateHistogram<double>("multiplot.realtime.refresh.duration", "ms", "Time for one real-time refresh cycle");

    /// <summary>Diagnostic log forwarder, set by the host when telemetry is active (null otherwise).</summary>
    public static volatile Action<string>? LogSink;

    /// <summary>Forwards a diagnostic log line to the telemetry log pipeline, if one is active. Never throws.</summary>
    public static void Log(string text)
    {
        var sink = LogSink;
        if (sink == null) return;
        try { sink(text); }
        catch (Exception) { /* telemetry must never affect the application */ }
    }

    /// <summary>Elapsed milliseconds since a <see cref="Stopwatch.GetTimestamp"/> value.</summary>
    public static double ElapsedMs(long startTimestamp) => Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

    /// <summary>Marks an activity as failed and records the exception.</summary>
    public static void Fail(Activity? act, Exception ex)
    {
        if (act == null) return;
        act.SetStatus(ActivityStatusCode.Error, ex.Message);
        act.AddException(ex);
    }
}
