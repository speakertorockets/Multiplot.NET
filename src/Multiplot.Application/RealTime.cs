using Multiplot.Core;
using static Multiplot.Core.ExprSyntax;
using Multiplot.Core.Model;
using Multiplot.Expressions;
using Multiplot.Plugins;

namespace Multiplot.Application;

/// <summary>
/// Real-time refresh (chapter 11). The refresh runs on the UI thread from a timer tick,
/// so plot data is never mutated while a plot paints (DEV-RT-001 fix with the same observable results).
/// </summary>
public sealed partial class Session
{
    private int _arDeferred;

    /// <summary>Toggles auto refresh.</summary>
    public void ToggleRefresh()
    {
        if (AutoRefresh == 0)
        {
            Settings.PreviousRefresh = Settings.PreviousRefresh != 0 ? Settings.PreviousRefresh : 2000;
            if (RtLoaded)
            {
                AutoRefresh = Settings.PreviousRefresh;
                Settings.PreviousRefresh = 0;
                StartRefreshTimer();
                SetRefreshPause(false);
            }
        }
        else
        {
            Settings.PreviousRefresh = AutoRefresh;
            AutoRefresh = 0;
            StartRefreshTimer();
        }
        Shell.RefreshCommands();
    }

    /// <summary>Refresh timer script path: starts/stops the timer for <see cref="AutoRefresh"/>.</summary>
    public void StartRefreshTimer()
    {
        if (AutoRefresh == 0) Shell.StopTimer("refresh");
        else Shell.StartTimer("refresh", AutoRefresh, RefreshTick);
        Shell.RefreshCommands();
    }

    /// <summary>Refresh timer interactive dialog.</summary>
    public void RefreshIntervalDialog()
    {
        var r = Shell.Dialogs.Input(new InputRequest("Refresh interval delta (in seconds):", Crt.Sprintf("%g", AutoRefresh / 1000.0), "Real-time Refresh",
            new[] { "OFF", ".1", ".2", ".5", "1", "2", "5", "10" }));
        if (r == null) return;
        Shell.StopTimer("refresh");
        int n = (int)(Crt.Atof(r.Text) * 1000.0);
        Settings.PreviousRefresh = AutoRefresh;
        AutoRefresh = n;
        StartRefreshTimer();
        Record(AutoRefresh == 0 ? "refresh OFF" : Crt.Sprintf("refresh %g", AutoRefresh / 1000.0));
    }

    private void RefreshTick()
    {
        if (ShuttingDown || RefreshPaused) return;
        RefreshNow();
    }

    /// <summary>Refreshes files and plots, then processes AutoRun events.</summary>
    public void RefreshNow()
    {
        if (_files.Count == 0) return;
        if (!Refreshing)
        {
            Refreshing = true;
            try { RtRefresh(); }
            catch (Exception ex) { DiagLog("Refresh failed: " + ex); }
            finally { Refreshing = false; }
        }
        ProcessAutoRun();
    }

    private void ProcessAutoRun()
    {
        int arCount = _arDeferred == -1 ? 0 : AutoRunList.Count;
        for (int i = 0; i < arCount; i++)
        {
            string entry = AutoRunList[i];
            int comma = entry.IndexOf(',');
            string ev = comma >= 0 ? entry[..comma] : entry;
            for (int j = 0; j < _files.Count; j++)
            {
                if (!_files[j].Info.IsDynamic || TimeReference.GetRefGmt(_files, j, ev, true) == null) continue;
                if (ScriptRun == RunState.Running)
                {
                    if (++_arDeferred * AutoRefresh > 6000)
                    {
                        _arDeferred = -1;
                        if (Message("A running script is blocking a triggered AutoRun\nForce cancel?", PromptButtons.YesNo, PromptIcon.Warning, PromptResult.No) == PromptResult.Yes)
                            if (ScriptRun == RunState.Running) ScriptRun = RunState.Aborting;
                        _arDeferred = 0;
                    }
                }
                else
                {
                    string wps = entry[(comma + 1)..];
                    AutoRunList.RemoveAt(i);
                    _arDeferred = 0;
                    _ = Scripts.RunAsync(wps);
                }
                return;
            }
        }
    }

    private int GroupOfFile(int iFile)
    {
        int grp;
        for (grp = _groups.Count - 1; grp >= 0; grp--) if (iFile >= _groups[grp].StartPos) break;
        return grp < 0 ? 0 : grp;
    }

    /// <summary>Real-time refresh (FR-RT-020).</summary>
    private void RtRefresh()
    {
        bool fileRefresh = false, gmtChanged = false, eventAdded = false;
        var updated = new bool[_files.Count];
        int whyRefreshed = 0;
        for (int iFile = 0; iFile < _files.Count; iFile++)
        {
            bool clrStream = true;
            for (int k = iFile + 1; k < _files.Count; k++)
                if (_files[iFile].Info.FileSig == SunSignatures.PcGoalRealtime && _files[k].Info.FileSig == SunSignatures.PcGoalRealtime) clrStream = false;
            RefreshResult rr;
            try { rr = _files[iFile].Refresh(clrStream); }
            catch (Exception ex) when (ex is IOException or DataAccessException) { DiagLog("Refresh: " + ex.Message); continue; }
            whyRefreshed = rr.Status;
            if (whyRefreshed >= 1)
            {
                updated[iFile] = true;
                fileRefresh = true;
                if ((whyRefreshed & 2) != 0) gmtChanged = true;
                if ((whyRefreshed & 4) != 0) eventAdded = true;
                if ((whyRefreshed & 8) != 0 && _groups.Any(g => g.TRef.Equals("TIMEREF", StringComparison.OrdinalIgnoreCase))) gmtChanged = true;
            }
        }
        if (!(fileRefresh || TimeChange)) return;
        ShowCurrentTime();
        var engine = new ExpressionEngine(this) { ErrPrompt = false };
        foreach (var plot in _plots.ToList())
        {
            if (RefreshPaused) break;
            bool plotRefresh = false;
            bool stopPlot = false;
            for (int iCurve = plot.NCurves - 1; iCurve >= 0 && !stopPlot; iCurve--)
            {
                var curve = plot.Curves[iCurve];
                int iFile = _files.IndexOf(curve.Data.Source!);
                if (iFile >= 0 && (updated[iFile] || TimeChange))
                {
                    int grp = GroupOfFile(iFile);
                    var g = _groups[grp];
                    var gfiles = GroupFiles(grp);
                    int cur = iFile - g.StartPos;
                    double[] slice = new double[2];
                    bool noAppend;
                    if (plot.Config.Style == PlotStyle.Tabular && !plot.ForceReload && !Settings.TabHistory)
                    {
                        slice[0] = curve.Time[curve.Points - 1];
                        noAppend = true;
                    }
                    else if (!plot.Config.PidVsPid && !plot.ForceReload && !g.TRef.Equals("EOF", StringComparison.OrdinalIgnoreCase) && !gmtChanged)
                    {
                        slice[0] = curve.Time[curve.Points - 1];
                        noAppend = false;
                    }
                    else
                    {
                        TimeReference.SetTime(gfiles, cur, g.TStart, g.TRef, out slice[0]);
                        noAppend = true;
                    }
                    if (g.TStop.Equals("EOF", StringComparison.OrdinalIgnoreCase)) slice[1] = 3e38;
                    else TimeReference.SetTime(gfiles, cur, g.TStop, g.TRef, out slice[1]);

                    if (plot.TimeFreeze != 0 || (!TimeChange && slice[1] - slice[0] < .250))
                    {
                        if (plot.TimeFreeze == 0 && !g.TStop.Equals("EOF", StringComparison.OrdinalIgnoreCase))
                        {
                            plot.TimeFreeze = 1;
                            plotRefresh = true;
                        }
                    }
                    else
                    {
                        string parmName = curve.Name;
                        SymbolSubstitute(ref parmName, false);
                        string err = "";
                        ParameterData? refreshed;
                        if (ExprSyntax.IsFunction(parmName) != Fn.None) refreshed = engine.DoFunction(gfiles, cur, -1, parmName, slice, g.TRef, ref err);
                        else if (ExprSyntax.IsExpression(parmName)) refreshed = engine.Parse(gfiles, cur, grp, parmName, slice, g.TRef, ref err);
                        else
                        {
                            TimeTag? refGmt = null;
                            double? refTime = null;
                            if (!g.TRef.Equals("NONE", StringComparison.Ordinal))
                            {
                                refGmt = TimeReference.GetRefGmt(gfiles, cur, g.TRef);
                                if (refGmt == null) refTime = TimeReference.GetRefTime(gfiles, cur, g.TRef);
                            }
                            refreshed = Read(_files[iFile], parmName, slice[0], slice[1], refTime, refGmt, plot.Config.Use64BitTime);
                        }
                        if (plot.Config.PidVsPid && refreshed != null)
                        {
                            string xi = plot.XAxisNomen[0];
                            ParameterData? xp;
                            if (ExprSyntax.IsFunction(xi) != Fn.None) xp = engine.DoFunction(gfiles, cur, grp, xi, slice, g.TRef, ref err);
                            else if (ExprSyntax.IsExpression(xi)) xp = engine.Parse(gfiles, cur, grp, xi, slice, g.TRef, ref err);
                            else
                            {
                                TimeTag? refGmt = null;
                                double? refTime = null;
                                if (!g.TRef.Equals("NONE", StringComparison.Ordinal))
                                {
                                    refGmt = TimeReference.GetRefGmt(gfiles, cur, g.TRef);
                                    if (refGmt == null) refTime = TimeReference.GetRefTime(gfiles, cur, g.TRef);
                                }
                                xp = Read(_files[iFile], xi, slice[0], slice[1], refTime, refGmt, false);
                            }
                            if (xp != null)
                            {
                                curve.OriginalTime = refreshed.Clone();
                                engine.SyncXaxis(refreshed, xp);
                                noAppend = true;
                            }
                            else refreshed = null;
                        }
                        if (refreshed != null)
                        {
                            int scale = curve.ScaleIndex < plot.YScales.Count ? curve.ScaleIndex : 0;
                            if (noAppend)
                            {
                                var old = curve.Data;
                                if ((whyRefreshed & 2) != 0) refreshed.Name = old.Name;
                                else { refreshed.Name = old.Name; refreshed.Units = old.Units; refreshed.Description = old.Description; }
                                curve.Data = refreshed;
                                if (plot.YScales[scale].Persist) { curve.VMin = old.MinValue; curve.VMax = old.MaxValue; }
                                else { plot.YScales[scale].RMin = curve.VMin; plot.YScales[scale].RMax = curve.VMax; }
                                if (iCurve == 0)
                                {
                                    if (!plot.XScale.Persist) plot.ResetMinMax(1, false);
                                    if (!plot.Config.Multi && !plot.YScales[0].Persist) plot.ResetMinMax(2, false);
                                }
                                plotRefresh = true;
                            }
                            else
                            {
                                bool retain = plot.YScales[plot.Config.Multi ? scale : 0].Persist || plot.TimeSlide;
                                int added = ParameterOps.Append(curve.Data, refreshed, retain);
                                if (added > 0)
                                {
                                    if (plot.TimeSlide && !plot.XScale.Persist)
                                    {
                                        double span = plot.XSetMax - plot.XSetMin;
                                        double last = curve.Time[curve.Points - 1];
                                        if (last > plot.XSetMax) { plot.XSetMax = last; plot.XSetMin = last - span; }
                                    }
                                    else if (!plot.XScale.Persist) plot.ResetMinMax(1, false);
                                    if (!retain && !plot.Config.Multi && plot.YScales.Count > 0 && !plot.YScales[0].Persist) plot.ResetMinMax(2, false);
                                    plotRefresh = true;
                                }
                            }
                        }
                        else if (TimeChange)
                        {
                            curve.Data.Points = 1;
                            curve.Data.Time[0] = (float)slice[0];
                            if (curve.Data.Data is float[] fv) fv[0] = DataTypes.FpDropOut;
                        }
                        else
                        {
                            stopPlot = true;
                            plotRefresh = false;
                            break;
                        }
                    }
                }
                if (!plot.Config.PidVsPid)
                {
                    int scale = curve.ScaleIndex < plot.YScales.Count ? curve.ScaleIndex : 0;
                    if (Settings.DiscardSeconds != 0) DiscardData(curve, plot.YScales[scale].Persist);
                    else if (Settings.CompactPoints != 0 && curve.Points - curve.PackPosition >= Settings.CompactPoints)
                    {
                        ParameterOps.CompactData(curve.Data, Settings.CompactInterval, false, curve.PackPosition, Data.Sun.Options.FilterInvalid, out int newPack);
                        curve.PackPosition = newPack;
                    }
                }
            }
            plot.ForceReload = false;
            if (plotRefresh || eventAdded) Invalidate(plot);
        }
        TimeChange = false;
    }

    /// <summary>Discards old data (FR-RT-030).</summary>
    private void DiscardData(Curve c, bool yPersist)
    {
        var p = c.Data;
        if (p.Points == 0) return;
        int start = TimeMath.Location(p.Time[p.Points - 1] - (float)Settings.DiscardSeconds, p.Time, p.Points, -1);
        if (start <= 1024) return;
        int n = p.Points - start;
        Array.Copy(p.Time, start, p.Time, 0, n);
        Array.Copy(p.Data, start, p.Data, 0, n);
        if (p.CommonTimeIndex)
        {
            if (p.GmtOffsetValid) p.GmtOffset += start;
        }
        else if (p.Gmt != null && p.GmtPoints >= p.Points)
        {
            Array.Copy(p.Gmt, p.GmtOffset + start, p.Gmt, p.GmtOffset, n);
            p.GmtPoints = n;
        }
        p.Points = n;
        p.MinIndex = ParameterOps.DataMin(p, Data.Sun.Options.FilterInvalid);
        p.MaxIndex = ParameterOps.DataMax(p, Data.Sun.Options.FilterInvalid);
        if (!yPersist)
        {
            p.MinValue = p.Values[p.MinIndex];
            p.MaxValue = p.Values[p.MaxIndex];
        }
    }

    /// <summary>Real-time clock pane.</summary>
    public void ShowCurrentTime()
    {
        TimeTag? current = null;
        for (int i = 0; i < _files.Count && current == null; i++)
        {
            var f = _files[i];
            if (!f.Info.IsDynamic) continue;
            current = TimeReference.GetRefGmt(_files, i, "EOF", true);
            if (current == null && f is ITimeIndexedFile ti && ti.IndexEndTime() is double end)
                current = TimeMath.Gmt(end, f.Info.RefGmt, true);
        }
        if (current != null) Shell.SetClock(ValueFormat.ClockString(current.Value));
    }

    /// <summary>FREEZE ON|OFF (FR-RT-021).</summary>
    public bool Freeze(bool on)
    {
        if (!RtLoaded) return false;
        var plot = ActivePlot;
        if (plot != null)
        {
            plot.TimeFreeze = on ? -1 : 0;
            Invalidate(plot);
        }
        return true;
    }

    /// <summary>Real-time plots reload after a TIME command.</summary>
    public void TimeReferenceChanged()
    {
        if (!RtLoaded) return;
        SetRefreshPause(true);
        foreach (var p in _plots)
        {
            if (p.TimeFreeze == -1) continue;
            p.TReference = Settings.TRef;
            p.ForceReload = true;
            p.TimeFreeze = 0;
        }
        TimeChange = true;
        SetRefreshPause(false);
        if (AutoRefresh == 0) RefreshNow();
    }

    /// <summary>Auto compact dialog.</summary>
    public void AutoCompactDialog()
    {
        var r = Shell.Dialogs.Input(new InputRequest("Point count threshold, interval (seconds):",
            (Settings.CompactPoints == 0 ? "0 (disable)" : Settings.CompactPoints.ToString(System.Globalization.CultureInfo.InvariantCulture)) + "," + Crt.Sprintf("%g", (double)Settings.CompactInterval),
            "Auto Data Compact", new[] { "0 (disable)", "10000,1.0", "25000,1.0", "50000,2.0", "100000,5.0" }));
        if (r == null) return;
        Settings.CompactPoints = (uint)Crt.Atol(r.Text);
        int c = r.Text.IndexOf(',');
        if (c >= 0) Settings.CompactInterval = (float)Crt.Atof(r.Text[(c + 1)..]);
        Record(Crt.Sprintf("autocompact %u %g", Settings.CompactPoints, (double)Settings.CompactInterval));
    }

    /// <summary>Auto discard dialog.</summary>
    public void AutoDiscardDialog()
    {
        var r = Shell.Dialogs.Input(new InputRequest("Maximum number of seconds maintained:", Settings.DiscardSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture), "Auto Discard",
            new[] { "0 (ALL)", "300 (5 min.)", "900 (15 min.)", "1800 (30 min)", "3600 (1 hour)" }));
        if (r == null) return;
        Settings.DiscardSeconds = (uint)Crt.Atol(r.Text);
        Record(Crt.Sprintf("autodiscard %u", Settings.DiscardSeconds));
    }
}
