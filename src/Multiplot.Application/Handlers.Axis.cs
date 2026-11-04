using Multiplot.Core;
using Multiplot.Core.Model;
using Multiplot.Core.Plotting;
using Multiplot.Plugins;
using Multiplot.Scripting;

namespace Multiplot.Application;

/// <summary>Pan/zoom/fix/rescale shift modes.</summary>
public enum ShiftMode
{
    /// <summary>One graduation.</summary>
    Increment = 0,
    /// <summary>One page.</summary>
    Page = 1,
    /// <summary>To the data min/max.</summary>
    MinMax = 2,
}

/// <summary>Axis handlers.</summary>
public sealed partial class Session
{
    /// <summary>Toggles Multi-Y mode.</summary>
    public void SetMultiY(bool? value)
    {
        var plot = ActivePlot;
        if (value == null) PlotConfig.Multi = !(plot?.Config.Multi ?? PlotConfig.Multi);
        else PlotConfig.Multi = value.Value;
        Record("multiy " + OnOff(PlotConfig.Multi));
        if (plot != null && plot.Config.Multi != PlotConfig.Multi)
        {
            if (plot.NScales <= 10)
            {
                plot.Config.Multi = PlotConfig.Multi;
                plot.ResetMinMax(2, false);
                Invalidate(plot);
            }
            else if (PlotConfig.Multi) Message("Multi Y disabled for plot\n10 scale limit exceeded");
        }
    }

    /// <summary>Sets the Multi-Y grouping (also turns Multi-Y on).</summary>
    public void SetGroupBy(int groupBy)
    {
        Settings.MultiGroupBy = groupBy;
        PlotConfig.Multi = true;
        Record("multiy " + (groupBy switch
        {
            GroupBy.Name => "GROUPBY_NAME",
            GroupBy.Units => "GROUPBY_UNITS",
            GroupBy.Description => "GROUPBY_DESCRIPTION",
            GroupBy.UserDefined => "GROUPBY_UDEF",
            _ => "GROUPBY_INDEPENDENT",
        }));
        var plot = ActivePlot;
        if (plot != null && (plot.MultiGroupBy != groupBy || !plot.Config.Multi))
        {
            plot.Config.Multi = true;
            plot.MultiGroupBy = groupBy;
            plot.ResetMinMax(2, false);
            Invalidate(plot);
        }
    }

    /// <summary>Multi-Y grouping dialog.</summary>
    public void GroupByDialog()
    {
        string[] names = { "Independent", "Name", "Units", "Description", "User" };
        int[] ids = { GroupBy.Independent, GroupBy.Name, GroupBy.Units, GroupBy.Description, GroupBy.UserDefined };
        var r = Shell.Dialogs.Input(new InputRequest("Select grouping mode", names[Math.Max(0, Array.IndexOf(ids, Settings.MultiGroupBy))], "Multi-Y Groupby Mode", names, false, false, true));
        if (r != null && Array.IndexOf(names, r.Text) is int i && i >= 0) SetGroupBy(ids[i]);
    }

    /// <summary>Log X / Log Y toggles (interactive toggles the default, not the plot's state).</summary>
    public void SetLog(bool y, bool? value)
    {
        if (value == null)
        {
            if (y) PlotConfig.LogY = !PlotConfig.LogY; else PlotConfig.LogX = !PlotConfig.LogX;
            Record((y ? "logY " : "logX ") + OnOff(y ? PlotConfig.LogY : PlotConfig.LogX));
        }
        else if (y) PlotConfig.LogY = value.Value; else PlotConfig.LogX = value.Value;
        bool v = y ? PlotConfig.LogY : PlotConfig.LogX;
        foreach (var p in TargetPlots(Settings.ApplyAll))
        {
            if ((y ? p.Config.LogY : p.Config.LogX) == v) continue;
            if (y) p.Config.LogY = v; else p.Config.LogX = v;
            Invalidate(p);
        }
    }

    /// <summary>Toggles end-to-end X scaling.</summary>
    public void SetEndToEnd(bool? value)
    {
        if (value == null)
        {
            PlotConfig.EndToEnd = !PlotConfig.EndToEnd;
            Record("endtoend " + OnOff(PlotConfig.EndToEnd));
        }
        else PlotConfig.EndToEnd = value.Value;
        foreach (var p in TargetPlots(Settings.ApplyAll))
        {
            if (!p.Config.PidVsPid)
            {
                if (p.Config.EndToEnd == PlotConfig.EndToEnd) continue;
                p.Config.EndToEnd = PlotConfig.EndToEnd;
                if (PlotConfig.EndToEnd && Settings.AllowAutoLabel && _groups.Count > 0)
                {
                    p.PrevXNomen = p.Config.XNomen;
                    p.PrevXUdef = p.XUdef;
                    p.XUdef = "Accumulated Time Seconds (" + _groups[0].TStart + " to " + _groups[0].TStop + ")";
                    p.Config.XNomen = Nomen.UDEF | (PlotConfig.XNomen & Nomen.EFORMAT);
                }
                else
                {
                    p.XUdef = p.PrevXUdef;
                    p.Config.XNomen = p.PrevXNomen;
                }
                p.ResetMinMax(3, false);
                Invalidate(p);
            }
            else if (PlotConfig.EndToEnd)
            {
                if (Message("End-to-End only available with default X-axis", PromptButtons.OkCancel, PromptIcon.Warning) == PromptResult.Cancel) break;
            }
        }
    }

    private bool SelectedYOnly(PlotDocument p) => p.Config.Multi && !Shell.IsShiftPressed();

    /// <summary>Zooms the X axis in.</summary>
    public void ZoomX(bool zoomIn)
    {
        var plot = ActivePlot;
        if (plot == null) return;
        var xs = plot.XScale;
        if (!zoomIn && plot.Config.ClockTime && xs.RMax - xs.RMin >= 8000000.0) return;
        plot.SaveScales();
        xs.FixedScale = false;
        if (plot.Config.LogX)
        {
            plot.XSetMin = xs.RMin = Math.Pow(10.0, ++xs.MinExp);
            plot.XSetMax = xs.RMax = Math.Pow(10.0, --xs.MaxExp);
        }
        else if (!zoomIn)
        {
            plot.XSetMin = xs.RMin - xs.Graduation;
            plot.XSetMax = xs.RMax + xs.Graduation;
        }
        else if (xs.RMax - xs.RMin > .010)
        {
            if (plot.Config.ClockTime)
            {
                xs.FixedScale = true;
                if (xs.Divisions > 5) xs.Divisions = -2; // intentionally "=- 2"
                else
                    while (xs.Divisions < 5 && xs.Divisions > 0) { xs.Divisions *= 2; xs.Graduation /= 2.0; }
                plot.XSetMin = xs.RMin + xs.Graduation;
                plot.XSetMax = xs.RMax - xs.Graduation;
            }
            else
            {
                double lastMin = xs.RMin, lastMax = xs.RMax;
                plot.XSetMin = xs.RMin + xs.Graduation;
                plot.XSetMax = xs.RMax - xs.Graduation;
                var m = new Scale(plot.XSetMin, plot.XSetMax, 10.0, false, false, false, false);
                if (lastMin == m.RMin && lastMax == m.RMax)
                {
                    plot.XSetMin += xs.Graduation;
                    plot.XSetMax += xs.Graduation;
                }
            }
        }
        Invalidate(plot);
        Record(zoomIn ? "ZOOM IN X" : "ZOOM OUT X");
    }

    private void SetScaleCurves(PlotDocument plot, int i)
    {
        if (plot.Config.Multi && plot.NCurves != plot.NScales)
        {
            foreach (var c in plot.Curves)
                if (c.ScaleIndex == i) { c.VMin = plot.YSetMin; c.VMax = plot.YSetMax; }
        }
        else if (i < plot.NCurves)
        {
            plot.Curves[i].VMin = plot.YSetMin;
            plot.Curves[i].VMax = plot.YSetMax;
        }
    }

    /// <summary>Zooms the Y axis in or out.</summary>
    public void ZoomY(bool zoomIn)
    {
        var plot = ActivePlot;
        if (plot == null) return;
        plot.SaveScales();
        bool selected = SelectedYOnly(plot);
        int first = plot.Config.Multi ? (plot.SelectedAxis > plot.NScales - 1 ? 0 : plot.SelectedAxis) : 0;
        first = selected ? first : 0;
        for (int i = first; i < plot.NScales; i++)
        {
            var ys = plot.YScales[i];
            if (plot.Config.LogY)
            {
                if (zoomIn ? ys.MaxExp - ys.MinExp < 32 : ys.MaxExp - ys.MinExp >= 2)
                {
                    if (zoomIn)
                    {
                        plot.YSetMin = ys.RMin = Math.Pow(10.0, --ys.MinExp);
                        plot.YSetMax = ys.RMax = Math.Pow(10.0, ++ys.MaxExp);
                    }
                    else
                    {
                        plot.YSetMin = ys.RMin = Math.Pow(10.0, ++ys.MinExp);
                        plot.YSetMax = ys.RMax = Math.Pow(10.0, --ys.MaxExp);
                    }
                }
            }
            else if (zoomIn)
            {
                double lastMin = ys.RMin, lastMax = ys.RMax;
                ys.FixedScale = false;
                plot.YSetMin = ys.RMin + ys.Graduation;
                plot.YSetMax = ys.RMax - ys.Graduation;
                var m = new Scale(plot.YSetMin, plot.YSetMax, 10.0, false, false, false, false);
                if (lastMin == m.RMin && lastMax == m.RMax)
                {
                    plot.YSetMin += ys.Graduation;
                    plot.YSetMax += ys.Graduation;
                }
            }
            else
            {
                ys.FixedScale = false;
                plot.YSetMin = ys.RMin - ys.Graduation;
                plot.YSetMax = ys.RMax + ys.Graduation;
            }
            SetScaleCurves(plot, i);
            if (selected || !plot.Config.Multi) break;
        }
        Invalidate(plot);
        Record(zoomIn ? "ZOOM IN Y" : "ZOOM OUT Y");
    }

    /// <summary>Shifts the X axis right or left.</summary>
    public void ShiftX(bool right, ShiftMode mode)
    {
        SetRefreshPause(true);
        foreach (var plot in TargetPlots(Settings.ApplyAll))
        {
            if (plot.Config.Style == PlotStyle.Tabular)
            {
                plot.TabDirection = right ? -1 : 1;
                switch (mode)
                {
                    case ShiftMode.MinMax: plot.TabTime = right ? 3e38f : -3e38f; break;
                    case ShiftMode.Page: plot.TabTime += (float)((plot.XScale.RMax - plot.XScale.RMin) / 10.0) * (right ? 1 : -1); break;
                    default: plot.TabTime += right ? Settings.TabStepRate : -Settings.TabStepRate; break;
                }
            }
            else if (plot.NCurves > 0)
            {
                plot.SaveScales();
                var xs = plot.XScale;
                int factor;
                if (right)
                {
                    int high = plot.MaxTimeParm();
                    var hc = plot.Curves[high];
                    float last = hc.Time[hc.Points - 1];
                    factor = mode == ShiftMode.MinMax ? 1 + (int)((last - plot.XSetMax) / xs.Graduation) : mode == ShiftMode.Page ? xs.Divisions : 1;
                    if (!(plot.Config.EndToEnd || last > plot.XSetMin + xs.Graduation * factor)) { Invalidate(plot); continue; }
                    if (plot.Config.LogX)
                    {
                        plot.XSetMin = xs.RMin = Math.Pow(10.0, xs.MinExp += factor);
                        plot.XSetMax = xs.RMax = Math.Pow(10.0, xs.MinExp += factor); // intentionally uses minExp twice
                    }
                    else
                    {
                        xs.FixedScale = true;
                        plot.XSetMin = xs.RMin = xs.RMin + xs.Graduation * factor;
                        plot.XSetMax = xs.RMax = xs.RMax + xs.Graduation * factor;
                    }
                }
                else
                {
                    int low = plot.MinTimeParm();
                    float first = plot.Curves[low].Time[0];
                    factor = mode == ShiftMode.MinMax ? 1 + (int)((plot.XSetMin - first) / xs.Graduation) : mode == ShiftMode.Page ? xs.Divisions : 1;
                    if (!(plot.Config.EndToEnd || first < plot.XSetMax - xs.Graduation * factor)) { Invalidate(plot); continue; }
                    if (plot.Config.LogX)
                    {
                        plot.XSetMin = xs.RMin = Math.Pow(10.0, xs.MinExp -= factor);
                        plot.XSetMax = xs.RMax = Math.Pow(10.0, xs.MaxExp -= factor);
                    }
                    else
                    {
                        xs.FixedScale = true;
                        plot.XSetMin = xs.RMin = xs.RMin - xs.Graduation * factor;
                        plot.XSetMax = xs.RMax = xs.RMax - xs.Graduation * factor;
                    }
                }
                var c0 = plot.Curves[0];
                xs.Persist = !(c0.Time[c0.Points - 1] <= plot.XSetMax);
                if (Settings.FixXAutoY && !plot.YScales.Take(plot.NScales).Any(s => s.Persist)) plot.ResetMinMax(0x0202, false);
                if (!Settings.ApplyAll) Record(Crt.Sprintf("fixx %g,%g,%g", plot.XSetMin, plot.XSetMax, xs.Graduation));
            }
            Invalidate(plot);
        }
        SetRefreshPause(false);
    }

    /// <summary>Pans X right or left (records the PAN command first).</summary>
    public void PanXCommand(bool right, ShiftMode mode)
    {
        Record((right ? "PAN RIGHT" : "PAN LEFT") + (mode == ShiftMode.Increment ? "" : mode == ShiftMode.Page ? " page" : " full"));
        ShiftX(right, mode);
    }

    /// <summary>Pans Y up or down.</summary>
    public void ShiftY(bool up)
    {
        var plot = ActivePlot;
        if (plot == null)
        {
            Message(up ? "No active plot for shift" : "No active plot for pan");
            return;
        }
        plot.SaveScales();
        bool selected = SelectedYOnly(plot);
        int first = plot.Config.Multi ? (plot.SelectedAxis > plot.NScales - 1 ? 0 : plot.SelectedAxis) : 0;
        first = selected ? first : 0;
        for (int i = first; i < plot.NScales; i++)
        {
            var ys = plot.YScales[i];
            int d = up ? 1 : -1;
            if (plot.Config.LogY)
            {
                ys.MinExp += d;
                ys.MaxExp += d;
                plot.YSetMin = ys.RMin = Math.Pow(10.0, ys.MinExp);
                plot.YSetMax = ys.RMax = Math.Pow(10.0, ys.MaxExp);
            }
            else
            {
                ys.FixedScale = true;
                plot.YSetMin = ys.RMin + ys.Graduation * d;
                plot.YSetMax = ys.RMax + ys.Graduation * d;
            }
            if (!up && plot.Config.Multi)
            {
                foreach (var c in plot.Curves) if (c.ScaleIndex == i) { c.VMin = plot.YSetMin; c.VMax = plot.YSetMax; }
            }
            else SetScaleCurves(plot, i);
            if (selected || !plot.Config.Multi) break;
        }
        Invalidate(plot);
        Record(up ? "PAN UP" : "PAN DOWN");
    }

    /// <summary>FIXX/FIXY script path (FR-SCR-171).</summary>
    public void ApplyFix(bool isY, FixRequest r)
    {
        if (r.Auto)
        {
            if (isY) RescaleY(); else RescaleX();
            return;
        }
        var f = Fix;
        if (!isY)
        {
            var x = r.Ranges[0];
            f.XSetMin = x.Min; f.XSetMax = x.Max; f.XSetGrad = x.Grad; f.FixXPersist = x.Persist; f.FixedScaleX = x.Fixed;
            FixXApply(Settings.ApplyAll, true);
            return;
        }
        if (r.Multi)
        {
            for (int i = 0; i < r.Ranges.Count && i < MaxCurves; i++)
            {
                var y = r.Ranges[i];
                f.YSetMin[i] = y.Min; f.YSetMax[i] = y.Max; f.FixYPersist[i] = y.Persist; f.YSetGrad[i] = y.Grad; f.FixedScaleY[i] = y.Fixed;
            }
        }
        else
        {
            var y = r.Ranges[0];
            for (int i = 0; i < MaxCurves; i++)
            {
                f.YSetMin[i] = y.Min; f.YSetMax[i] = y.Max; f.FixYPersist[i] = y.Persist; f.YSetGrad[i] = y.Grad; f.FixedScaleY[i] = y.Fixed;
            }
        }
        FixYScript();
    }

    private void FixXApply(bool toAll, bool dataChanged)
    {
        if (!(dataChanged || toAll)) return;
        var f = Fix;
        SetRefreshPause(true);
        foreach (var plot in TargetPlots(toAll))
        {
            plot.SaveScales();
            plot.XSetMin = f.XSetMin;
            plot.XSetMax = f.XSetMax;
            plot.XScale.Graduation = f.XSetGrad;
            plot.XScale.FixedScale = f.FixedScaleX;
            plot.XScale.Persist = f.FixXPersist;
            plot.TabTime = (float)f.XSetMax;
            if (Settings.FixXAutoY && plot.YScales.Count > 0 && !plot.YScales[0].Persist) plot.ResetMinMax(0x0202, false);
            Invalidate(plot);
        }
        SetRefreshPause(false);
    }

    private void FixYScript()
    {
        var plot = ActivePlot;
        if (plot == null) return;
        var f = Fix;
        float drop = DataTypes.FpDropOut;
        plot.SaveScales();
        if (plot.Config.Multi)
        {
            for (int i = 0; i < plot.NScales; i++)
            {
                var ys = plot.YScales[i];
                ys.FixedScale = f.FixedScaleY[i];
                ys.Graduation = f.YSetGrad[i];
                ys.Persist = f.FixYPersist[i];
                if (f.YSetMin[i] == drop || f.YSetMax[i] == drop)
                {
                    int cur = plot.SelectedAxis;
                    plot.SelectedAxis = i;
                    plot.ResetMinMax(0x0202, false, true);
                    plot.SelectedAxis = cur;
                }
                ys.RMin = f.YSetMin[i] == drop ? plot.YSetMin : f.YSetMin[i];
                ys.RMax = f.YSetMax[i] == drop ? plot.YSetMax : f.YSetMax[i];
                foreach (var c in plot.Curves)
                {
                    if (c.ScaleIndex != i) continue;
                    if (f.YSetMin[i] != drop) c.VMin = f.YSetMin[i];
                    if (f.YSetMax[i] != drop) c.VMax = f.YSetMax[i];
                }
            }
        }
        else
        {
            plot.YSetMin = f.YSetMin[0];
            plot.YSetMax = f.YSetMax[0];
            plot.YScales[0].FixedScale = f.FixedScaleY[0];
            plot.YScales[0].Graduation = f.YSetGrad[0];
            plot.YScales[0].Persist = f.FixYPersist[0];
        }
        Invalidate(plot);
    }

    /// <summary>Fix X interactive dialog.</summary>
    public void FixXDialog()
    {
        var plot = ActivePlot;
        var f = Fix;
        var req = new RangeRequest { Prompt = "X Axis Range" };
        if (plot != null)
        {
            req.Min = Crt.Sprintf("%g", plot.XSetMin);
            req.Max = Crt.Sprintf("%g", plot.XSetMax);
            if (plot.XScale.FixedScale) req.Increment = Crt.Sprintf("%g", plot.XScale.Graduation);
            req.Persist = plot.XScale.Persist;
            req.ApplyAll = Settings.ApplyAll;
        }
        else
        {
            req.Min = Crt.Sprintf("%g", f.XSetMin);
            req.Max = Crt.Sprintf("%g", f.XSetMax);
            if (f.FixedScaleX) req.Increment = Crt.Sprintf("%g", f.XSetGrad);
            req.Persist = f.FixXPersist;
            req.DisableApplyAll = true;
        }
        if (!Shell.Dialogs.Range(req)) return;
        double a = Crt.Atof(req.Min), b = Crt.Atof(req.Max);
        if (a < b) { f.XSetMin = a; f.XSetMax = b; }
        else { f.XSetMax = a; f.XSetMin = b; }
        f.FixXPersist = req.Persist;
        f.XSetGrad = -1;
        if (!req.Increment.Equals("Auto", StringComparison.OrdinalIgnoreCase) && req.Increment.Length > 0)
        {
            f.XSetGrad = Math.Abs(Crt.Atof(req.Increment));
            f.FixedScaleX = true;
        }
        else f.FixedScaleX = false;
        string cmd = f.FixedScaleX ? Crt.Sprintf("fixx %g,%g,%g", f.XSetMin, f.XSetMax, f.XSetGrad) : Crt.Sprintf("fixx %g,%g", f.XSetMin, f.XSetMax);
        if (f.FixXPersist) cmd += ",p";
        Record(cmd);
        FixXApply(req.ApplyAll, true);
    }

    /// <summary>Fix Y interactive dialog(s).</summary>
    public void FixYDialog()
    {
        var plot = ActivePlot;
        var f = Fix;
        bool multi = plot?.Config.Multi ?? PlotConfig.Multi;
        if (!multi || plot == null)
        {
            var req = new RangeRequest { Prompt = "Y Axis Range" };
            if (plot != null)
            {
                req.Min = Crt.Sprintf("%g", plot.YSetMin);
                req.Max = Crt.Sprintf("%g", plot.YSetMax);
                if (plot.YScales[0].FixedScale) req.Increment = Crt.Sprintf("%g", plot.YScales[0].Graduation);
                req.Persist = plot.YScales[0].Persist;
                req.ApplyAll = Settings.ApplyAll;
            }
            else
            {
                req.Min = Crt.Sprintf("%g", f.YSetMin[0]);
                req.Max = Crt.Sprintf("%g", f.YSetMax[0]);
                if (f.FixedScaleY[0]) req.Increment = Crt.Sprintf("%g", f.YSetGrad[0]);
                req.Persist = f.FixYPersist[0];
                req.DisableApplyAll = true;
            }
            if (!Shell.Dialogs.Range(req)) return;
            double a = Crt.Atof(req.Min), b = Crt.Atof(req.Max);
            if (a < b) { f.YSetMin[0] = a; f.YSetMax[0] = b; } else { f.YSetMin[0] = b; f.YSetMax[0] = a; }
            f.FixYPersist[0] = req.Persist;
            f.YSetGrad[0] = -1;
            f.FixedScaleY[0] = false;
            double grad = -1;
            bool fixedScale = false;
            if (req.Increment.Length > 0 && !req.Increment.StartsWith("a", StringComparison.OrdinalIgnoreCase))
            {
                grad = Math.Abs(Crt.Atof(req.Increment));
                fixedScale = true;
                f.YSetGrad[0] = grad;
                f.FixedScaleY[0] = true;
            }
            string cmd = f.FixedScaleY[0] ? Crt.Sprintf("fixy %g,%g,%g", f.YSetMin[0], f.YSetMax[0], f.YSetGrad[0]) : Crt.Sprintf("fixy %g,%g", f.YSetMin[0], f.YSetMax[0]);
            if (f.FixYPersist[0]) cmd += ",p";
            Record(cmd);
            foreach (var p in TargetPlots(req.ApplyAll))
            {
                p.SaveScales();
                p.YSetMin = f.YSetMin[0];
                p.YSetMax = f.YSetMax[0];
                p.YScales[0].FixedScale = fixedScale;
                p.YScales[0].Graduation = grad;
                p.YScales[0].Persist = f.FixYPersist[0];
                Invalidate(p);
            }
            return;
        }
        plot.SaveScales();
        bool minAuto = false, maxAuto = false;
        for (int i = 0; i < plot.NScales; i++)
        {
            var ys = plot.YScales[i];
            var req = new RangeRequest
            {
                Prompt = ys.Name.Length < 24 ? ys.Name : Crt.Sprintf("Scale #%d", i + 1),
                Min = Crt.Sprintf("%g", ys.RMin),
                Max = Crt.Sprintf("%g", ys.RMax),
                Increment = f.FixedScaleY[i] ? Crt.Sprintf("%g", f.YSetGrad[i]) : "Auto",
                Persist = ys.Persist,
                DisableApplyAll = true,
            };
            if (!Shell.Dialogs.Range(req)) continue;
            string mn = req.Min.Trim(), mx = req.Max.Trim();
            minAuto = mn.Length == 0 || mn.StartsWith("a", StringComparison.OrdinalIgnoreCase);
            maxAuto = mx.Length == 0 || mx.StartsWith("a", StringComparison.OrdinalIgnoreCase);
            if (minAuto || maxAuto)
            {
                int cur = plot.SelectedAxis;
                plot.SelectedAxis = i;
                plot.ResetMinMax(0x0202, false, true);
                plot.SelectedAxis = cur;
            }
            double tmin = minAuto ? plot.YSetMin : Crt.Atof(mn), tmax = maxAuto ? plot.YSetMax : Crt.Atof(mx);
            if (tmin > tmax) (tmin, tmax) = (tmax, tmin);
            ys.RMin = f.YSetMin[i] = tmin;
            ys.RMax = f.YSetMax[i] = tmax;
            f.FixYPersist[i] = req.Persist;
            f.YSetGrad[i] = -1;
            f.FixedScaleY[i] = false;
            bool fixedScale = false;
            double grad = -1;
            if (req.Increment.Length > 0 && !req.Increment.StartsWith("a", StringComparison.OrdinalIgnoreCase) && (grad = Math.Abs(Crt.Atof(req.Increment))) > 0)
            {
                fixedScale = true;
                if (f.FixYPersist[i]) { f.YSetGrad[i] = grad; f.FixedScaleY[i] = true; }
            }
            foreach (var c in plot.Curves) if (c.ScaleIndex == i) { c.VMin = tmin; c.VMax = tmax; }
            ys.FixedScale = fixedScale;
            ys.Graduation = grad;
            ys.Persist = f.FixYPersist[i];
        }
        Invalidate(plot);
        var sb = new System.Text.StringBuilder("fixy multi ");
        for (int i = 0; i < plot.NCurves && i < plot.YScales.Count; i++)
        {
            sb.Append(minAuto ? "AUTO," : Crt.Sprintf("%g,", f.YSetMin[i]));
            sb.Append(maxAuto ? "AUTO," : Crt.Sprintf("%g,", f.YSetMax[i]));
            sb.Append(plot.YScales[i].Graduation < 0 ? "a," : Crt.Sprintf("%g,", plot.YScales[i].Graduation));
        }
        if (f.FixYPersist[0]) sb.Append('p');
        Record(sb.ToString());
    }

    /// <summary>Rescales X and Y (RESCALE, Home).</summary>
    public void RescaleXY()
    {
        bool dropPersist = ActivePlot == null;
        SetRefreshPause(true);
        foreach (var plot in TargetPlots(Settings.ApplyAll))
        {
            if (plot.XScale.IsZoomed == 0) dropPersist = true;
            plot.SaveScales();
            plot.ResetMinMax(3, false);
            Invalidate(plot);
            if (Shell.IsMaximized(plot)) break;
        }
        if (dropPersist)
        {
            Fix.FixXPersist = Fix.FixedScaleX = false;
            for (int i = 0; i < MaxCurves; i++) { Fix.FixYPersist[i] = Fix.FixedScaleY[i] = false; Fix.YSetGrad[i] = -1; }
        }
        Record("rescale");
        SetRefreshPause(false);
    }

    /// <summary>Rescales X (FIXX AUTO).</summary>
    public void RescaleX()
    {
        bool dropPersist = ActivePlot == null;
        foreach (var plot in TargetPlots(Settings.ApplyAll))
        {
            if (plot.XScale.IsZoomed == 0) dropPersist = true;
            plot.SaveScales();
            plot.ResetMinMax(1, false);
            Invalidate(plot);
            if (Shell.IsMaximized(plot)) break;
        }
        if (dropPersist)
        {
            Fix.FixXPersist = Fix.FixedScaleX = false;
            Fix.XSetGrad = -1;
        }
        Record("fixx AUTO");
    }

    /// <summary>Rescales Y (FIXY AUTO).</summary>
    public void RescaleY()
    {
        bool dropPersist = ActivePlot == null;
        foreach (var plot in TargetPlots(Settings.ApplyAll))
        {
            if (plot.XScale.IsZoomed == 0) dropPersist = true;
            plot.SaveScales();
            plot.ResetMinMax(0x0202, false, SelectedYOnly(plot));
            Invalidate(plot);
            if (Shell.IsMaximized(plot)) break;
        }
        if (dropPersist)
            for (int i = 0; i < MaxCurves; i++) { Fix.FixYPersist[i] = Fix.FixedScaleY[i] = false; Fix.YSetGrad[i] = -1; }
        Record("fixy AUTO");
    }

    /// <summary>Reverts to the previous scales (Backspace).</summary>
    public void UndoZoom()
    {
        var plot = ActivePlot;
        if (plot != null && plot.XScale.IsZoomed != 0)
        {
            plot.ResetMinMax(3, true);
            Invalidate(plot);
        }
    }

    /// <summary>3D Z angle (simplified dialog).</summary>
    public void ZAngleDialog()
    {
        var plot = ActivePlot;
        if (plot != null && !plot.Config.Is3D) plot = null;
        var r = Shell.Dialogs.Input(new InputRequest("3D Z axis angle (degrees):", PlotConfig.ZAngle.ToString(System.Globalization.CultureInfo.InvariantCulture), "Z Axis Angle",
            new[] { "15", "30", "45", "60", "75" }));
        if (r == null) return;
        PlotConfig.ZAngle = Math.Clamp(Crt.Atoi(r.Text), 0, 90);
        if (plot == null) return;
        foreach (var p in TargetPlots(Settings.ApplyAll)) { p.Config.ZAngle = PlotConfig.ZAngle; Invalidate(p); }
    }
}
