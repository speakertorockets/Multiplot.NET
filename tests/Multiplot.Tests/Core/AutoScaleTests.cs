// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

using Multiplot.Core.Model;
using Multiplot.Plugins;

namespace Multiplot.Tests.Core;

/// <summary>Auto Scale X / Auto Scale Y behaviour when real-time data arrives (PlotDocument.ScaleAfterNewData).</summary>
public class AutoScaleTests
{
    private sealed class Host : IPlotHost
    {
        public IReadOnlyList<IDataFile> Files => Array.Empty<IDataFile>();
        public IReadOnlyList<FileGroup> Groups => Array.Empty<FileGroup>();
        public string[] LogicTxtTrue => new[] { "ON" };
        public string[] LogicTxtFalse => new[] { "OFF" };
        public string Version => "test";
        public bool PresentationMode => false;
        public int LayoutGridSpacing => 0;
        public bool SnapToGrid => false;
        public bool AutoRefreshOn => true;
        public bool StreamCert => false;
        public int DataPrecision => 3;
        public int TimePrecision => 3;
        public int MinGridOffset => 0;
        public int ReservedLegend => 0;
        public bool RtLoaded => true;
        public bool ResolveTime(int group, string spec, string tRef, out double time) { time = 0; return false; }
        public void ShowMessage(string text) { }
    }

    /// <summary>A ramp: value = 10 × time, one point per second from 0 to <paramref name="last"/>.</summary>
    private static ParameterData Ramp(int last)
    {
        int n = last + 1;
        var t = new float[n];
        var v = new float[n];
        for (int i = 0; i < n; i++) { t[i] = i; v[i] = 10f * i; }
        var p = ParameterData.FromFloat("RAMP", t, v, n);
        p.MinIndex = 0;
        p.MaxIndex = n - 1;
        p.MinValue = v[0];
        p.MaxValue = v[n - 1];
        return p;
    }

    private static PlotDocument NewPlot(bool autoX, bool autoY, bool pidVsPid = false)
    {
        var cfg = new PlotConfig { AutoScaleX = autoX, AutoScaleY = autoY, PidVsPid = pidVsPid };
        var plot = new PlotDocument(new Host(), new List<Curve> { new(Ramp(100)) }, cfg, null,
            new[] { "TIME", "Seconds", "Relative Time in Seconds" }, "", "", "", "", "NONE", "");
        plot.Initialize(1, 0, "%03u/%02u:%02u:%02u.%03u", "", null, true, true, false, new FixedScaleDefaults { XSetGrad = -1 }, DateTime.Now);
        return plot;
    }

    private static void Grow(PlotDocument plot, int last)
    {
        double previous = plot.LastTime();
        plot.Curves[0].Data = Ramp(last);
        plot.ScaleAfterNewData(previous);
    }

    [Fact]
    public void AutoScaleX_SpansAllData_AutoScaleY_FitsVisibleRange()
    {
        var plot = NewPlot(true, true);
        Assert.Equal((0, 100), (plot.XSetMin, plot.XSetMax));
        Grow(plot, 150);
        Assert.Equal((0, 150), (plot.XSetMin, plot.XSetMax));
        Assert.Equal((0, 1500), (plot.YSetMin, plot.YSetMax));
    }

    [Fact]
    public void AutoScaleXOff_SlidesWithNewestData_YOff_KeepsScale()
    {
        var plot = NewPlot(false, false);
        // after the initial fit (or Home) the window shows 0..100 including the newest point
        Grow(plot, 130);
        Assert.Equal((30, 130), (plot.XSetMin, plot.XSetMax));
        Assert.Equal((0, 1000), (plot.YSetMin, plot.YSetMax)); // value axis keeps its scale
    }

    [Fact]
    public void AutoScaleXOff_SlidingWindow_AutoScaleYOn_FollowsVisibleValues()
    {
        var plot = NewPlot(false, true);
        Grow(plot, 130);
        Assert.Equal((30, 130), (plot.XSetMin, plot.XSetMax));
        Assert.Equal((300, 1300), (plot.YSetMin, plot.YSetMax));
    }

    [Fact]
    public void AutoScaleXOff_ViewOfOlderData_StaysPut()
    {
        var plot = NewPlot(false, false);
        plot.XSetMin = 10;
        plot.XSetMax = 40; // zoomed into older data: the newest point (100) is not in view
        Grow(plot, 130);
        Assert.Equal((10, 40), (plot.XSetMin, plot.XSetMax));
    }

    [Fact]
    public void PlotWithoutTimeAxis_KeepsScales()
    {
        var plot = NewPlot(true, true, pidVsPid: true);
        double xmin = plot.XSetMin, xmax = plot.XSetMax, ymin = plot.YSetMin, ymax = plot.YSetMax;
        Grow(plot, 150);
        Assert.Equal((xmin, xmax, ymin, ymax), (plot.XSetMin, plot.XSetMax, plot.YSetMin, plot.YSetMax));
    }
}
