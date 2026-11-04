using System.Text;
using Multiplot.Core;
using Multiplot.Core.Model;
using Multiplot.Core.Plotting;
using Multiplot.Plugins;

namespace Multiplot.Application;

/// <summary>Plot regions for context menus.</summary>
public enum PlotRegion
{
    /// <summary>Title/legend area (X Heading).</summary>
    Heading = 1,
    /// <summary>Y scale area (Y Heading).</summary>
    YScale = 2,
    /// <summary>Footer / X area (Footing).</summary>
    Footer = 3,
    /// <summary>2D plot area (Plot).</summary>
    Plot2D = 4,
    /// <summary>3D plot area (Plot3D).</summary>
    Plot3D = 5,
    /// <summary>Tabular display (Tabular).</summary>
    Tabular = 6,
}

/// <summary>Mouse interactions in plot windows (FR-PLT-030..035).</summary>
public sealed partial class Session
{
    private const double ZoomStop = 3.4e-37;

    /// <summary>Range of the last slice-average drag (X units).</summary>
    public (double Start, double Stop) SliceRange;
    /// <summary>Time of the last data pick click.</summary>
    public double PickTime;

    private static bool InGraph(PlotDocument p, int x, int y, int slack = 0) =>
        x >= p.GraphLeft - slack && x <= p.GraphRight + slack && y >= p.GraphTop - slack && y <= p.GraphBottom + slack;

    private static double XAt(PlotDocument p, int x) => (x - p.GraphLeft) / p.XScale.Factor + p.XScale.RMin;

    private static double YAt(PlotDocument p, Scale s, int y) => (p.GraphBottom - y) / s.Factor + s.RMin;

    /// <summary>Region of a click for the context menu.</summary>
    public static PlotRegion RegionAt(PlotDocument p, int x, int y)
    {
        if (p.Config.Style == PlotStyle.Tabular) return PlotRegion.Tabular;
        if ((x > p.GraphRight || x < p.GraphLeft) && y > p.GraphTop) return PlotRegion.YScale;
        if (y < p.GraphTop) return PlotRegion.Heading;
        if (y > p.GraphBottom) return PlotRegion.Footer;
        return p.Config.Is3D ? PlotRegion.Plot3D : PlotRegion.Plot2D;
    }

    private static TimeTag ClockBase(PlotDocument p, bool headerDayFix)
    {
        var d = p.Curves[0].Data;
        if (d.RefGmt.HasValue) return d.RefGmt.Value;
        if (headerDayFix)
        {
            var g = d.Source?.Info.RefGmt ?? new TimeTag();
            if (g.JulianDay == 0) g.JulianDay++;
            return g;
        }
        return PlotDocument.HeaderGmt(d);
    }

    /// <summary>Mouse-move coordinate text (null = idle message).</summary>
    public string? CoordinatesText(PlotDocument p, int x, int y)
    {
        if (!Settings.ShowCoord || Refreshing || p.NCurves == 0) return null;
        if (p.Config.Style == PlotStyle.Tabular)
        {
            double tx = p.XScale.Factor != 0 ? x / p.XScale.Factor : 0;
            double ty = p.YScales.Count > 0 && p.YScales[0].Factor != 0 ? y / p.YScales[0].Factor : 0;
            return Crt.Sprintf(" X=%d Y=%d", (int)tx, 4096 - (int)ty);
        }
        if (!InGraph(p, x, y))
        {
            if (p.Config.Multi && x < p.GraphLeft && y < p.GraphBottom && y > p.GraphTop) return "Click on a scale to select the active Y axis";
            return null;
        }
        string xBuf;
        double xv = XAt(p, x);
        if (p.Config.ClockTime)
        {
            var d = p.Curves[0].Data;
            if (d.RefTime.HasValue) xv += d.RefTime.Value;
            xBuf = ValueFormat.ClockString(TimeMath.Gmt(xv, ClockBase(p, false), true), "GMT=%02u:%02u:%02u.%03u");
        }
        else
        {
            if (p.Config.LogX) xv = Math.Exp(xv);
            xBuf = Crt.Sprintf(" X=%.3f", xv);
        }
        var sb = new StringBuilder(xBuf);
        if (p.Config.Multi)
        {
            for (int i = 0; i < p.NCurves; i++)
            {
                var c = p.Curves[i];
                var s = p.YScales[Math.Min(c.ScaleIndex, p.YScales.Count - 1)];
                double yv = YAt(p, s, y);
                if (p.Config.LogY) yv = Math.Exp(yv);
                string v = ValueFormat.FormatValue((float)yv, c.TabFormat, (p.Config.YNomen & Nomen.EFORMAT) != 0);
                sb.Append(c.Name.Length > 20 ? Crt.Sprintf(" CURVE%d=%s", i, v) : Crt.Sprintf(" %s=%s", c.Name, v));
            }
        }
        else
        {
            double yv = YAt(p, p.YScales[0], y);
            if (p.Config.LogY) yv = Math.Exp(yv);
            sb.Append(Crt.Sprintf(" Y=%g", yv));
        }
        return sb.ToString();
    }

    /// <summary>Multi-Y: a click left of the graph selects the axis; true when handled.</summary>
    public bool SelectAxisAt(PlotDocument p, int x, int y)
    {
        if (!p.Config.Multi || !(x < p.GraphLeft - 10 && y <= p.GraphBottom && y >= p.GraphTop)) return false;
        p.SelectedAxis = (p.GraphLeft - x) / Math.Max(1, p.YaSpacing);
        Invalidate(p);
        return true;
    }

    /// <summary>Zoom box (left drag) in pixel coordinates.</summary>
    public void ZoomBoxPixels(PlotDocument p, int l, int t, int r, int b, bool shift)
    {
        if (l > r) (l, r) = (r, l);
        if (t > b) (t, b) = (b, t);
        if (r - l <= 2 || b - t <= 2) return;
        bool selectedY = p.Config.Multi && !shift;
        p.XScale.AddZoomLevel(p.XSetMin, p.XSetMax, p.XScale.Persist);
        double zmin, zmax;
        if (!selectedY)
        {
            p.XScale.FixedScale = false;
            for (int i = 0; i < p.NCurves && i < p.YScales.Count; i++) p.YScales[i].FixedScale = false;
            zmin = l > p.GraphLeft ? XAt(p, l) : p.XScale.RMin;
            zmax = r < p.GraphRight ? XAt(p, r) : p.XScale.RMax;
            if (zmax - zmin > ZoomStop)
            {
                p.XSetMin = p.Config.LogX ? Math.Exp(zmin) : zmin;
                p.XSetMax = p.Config.LogX ? Math.Exp(zmax) : zmax;
            }
        }
        if (!p.Config.Multi)
        {
            for (int i = 0; i < p.NCurves && i < p.YScales.Count; i++) p.YScales[i].AddZoomLevel(p.YSetMin, p.YSetMax, p.YScales[0].Persist);
            var s = p.YScales[0];
            zmax = YAt(p, s, t);
            zmin = YAt(p, s, b);
            if (zmax - zmin > ZoomStop)
            {
                s.RMin = p.YSetMin = p.Config.LogY ? Math.Exp(zmin) : zmin;
                s.RMax = p.YSetMax = p.Config.LogY ? Math.Exp(zmax) : zmax;
            }
        }
        else
        {
            for (int i = 0; i < p.NCurves; i++)
            {
                var s = p.YScales[Math.Min(p.Curves[i].ScaleIndex, p.YScales.Count - 1)];
                p.YScales[i].AddZoomLevel(s.RMin, s.RMax, s.Persist);
            }
            for (int i = selectedY ? p.SelectedAxis : 0; i < p.NScales; i++)
            {
                var s = p.YScales[i];
                zmax = YAt(p, s, t);
                zmin = YAt(p, s, b);
                p.YSetMin = p.Config.LogY ? Math.Exp(zmax) : zmax; // intentionally assigns max to ysetmin here
                p.YSetMax = p.Config.LogY ? Math.Exp(zmin) : zmin;
                s.FixedScale = false;
                if (zmax - zmin > ZoomStop)
                    foreach (var c in p.Curves) if (c.ScaleIndex == i) { c.VMin = p.YSetMin; c.VMax = p.YSetMax; }
                if (selectedY) break;
            }
        }
        if (AutoRefresh != 0)
        {
            p.XScale.Persist = true;
            for (int i = 0; i < p.NCurves && i < p.YScales.Count; i++) p.YScales[i].Persist = true;
        }
        Invalidate(p);
        if (Recording)
        {
            Record(Crt.Sprintf("fixx %g,%g", p.XSetMin, p.XSetMax));
            if (!p.Config.Multi) Record(Crt.Sprintf("fixy %g,%g", p.YSetMin, p.YSetMax));
            else
            {
                var sb = new StringBuilder("fixy multi ");
                for (int i = 0; i < p.NScales && i < p.NCurves; i++)
                {
                    var m = new Scale(p.Curves[i].VMin, p.Curves[i].VMax, 10.0, false, p.Config.LogY, false, false);
                    sb.Append(Crt.Sprintf("%g,%g,a,", m.RMin, m.RMax));
                }
                Record(sb.ToString());
            }
        }
    }

    /// <summary>Click identification: the curve whose colour matches <paramref name="colorRef"/>.</summary>
    public static string? IdentifyCurve(PlotDocument p, int colorRef)
    {
        foreach (var c in p.Curves)
        {
            if (p.Config.DrawColor[c.ColorIndex] != colorRef) continue;
            string n = c.Data.Source?.Info.Path ?? "";
            return n[(n.LastIndexOf('\\') + 1)..] + ":" + c.Name;
        }
        return null;
    }

    /// <summary>Plots the tabular items inside the box (layout units).</summary>
    public void TabularZoom(PlotDocument p, int l, int t, int r, int b, int clientW, int clientH)
    {
        if (l > r) (l, r) = (r, l);
        if (t > b) (t, b) = (b, t);
        string saveY = YAxis;
        YAxis = "";
        float xs = 4096f / Math.Max(1, clientW), ys = 4096f / Math.Max(1, clientH);
        foreach (var c in p.Curves)
        {
            float xp = c.TabX, yp = 4096 - c.TabY;
            if (!(l * xs < xp && r * xs > xp && t * ys < yp && b * ys > yp)) continue;
            if (YAxis.Contains(c.Name)) continue;
            if (YAxis.Length > 0) YAxis += ",";
            if (ExprSyntax.IsExpression(c.Name)) YAxis += c.Name;
            else
            {
                string fn = c.Data.Source?.Info.Path ?? "";
                int br = fn.LastIndexOf(']');
                if (br >= 0) fn = fn[(br + 1)..];
                YAxis += fn[(fn.LastIndexOf('\\') + 1)..] + ":" + c.Name;
            }
        }
        if (YAxis.Length > 0)
        {
            int save = PlotConfig.Style;
            PlotConfig.Style = PlotStyle.Line2D;
            Plot();
            PlotConfig.Style = save;
        }
        YAxis = saveY;
    }

    /// <summary>Middle-button pan step (one graduation per 20 px movement).</summary>
    public void MousePan(PlotDocument p, int xdelt, int ydelt, bool shift)
    {
        bool panned = false;
        var xScl = new Scale(0.001, p.XScale.Graduation, 10.0, false, false, false, false);
        if (Math.Abs(xdelt) > 20)
        {
            p.XScale.FixedScale = true;
            panned = true;
            if (xdelt < 0) { p.XSetMin = p.XScale.RMin + xScl.Graduation; p.XSetMax = p.XScale.RMax + xScl.Graduation; }
            if (xdelt > 0) { p.XSetMin = p.XScale.RMin - xScl.Graduation; p.XSetMax = p.XScale.RMax - xScl.Graduation; }
        }
        if (Math.Abs(ydelt) > 20)
        {
            panned = true;
            bool all = p.Config.Multi && shift;
            if (!p.Config.Multi)
            {
                var s = p.YScales[Math.Min(p.SelectedAxis, p.YScales.Count - 1)];
                var ys = new Scale(0.001, s.Graduation, 10.0, false, false, false, false);
                s.FixedScale = true;
                if (ydelt > 0) { p.YSetMin = s.RMin + ys.Graduation; p.YSetMax = s.RMax + ys.Graduation; }
                if (ydelt < 0) { p.YSetMin = s.RMin - ys.Graduation; p.YSetMax = s.RMax - ys.Graduation; }
            }
            else
            {
                for (int i = all ? 0 : p.SelectedAxis; i < p.NScales; i++)
                {
                    var s = p.YScales[i];
                    var ys = new Scale(0.001, s.Graduation, 10.0, false, false, false, false);
                    s.FixedScale = true;
                    if (ydelt > 0) { p.YSetMin = s.RMin + ys.Graduation; p.YSetMax = s.RMax + ys.Graduation; }
                    if (ydelt < 0) { p.YSetMin = s.RMin - ys.Graduation; p.YSetMax = s.RMax - ys.Graduation; }
                    SetScaleCurves(p, i);
                    if (!all) break;
                }
            }
        }
        if (panned) Invalidate(p);
    }

    /// <summary>Mouse wheel handling.</summary>
    public void MouseWheel(PlotDocument p, int x, int y, int delta, bool shift)
    {
        if (p.Config.Style == PlotStyle.Tabular || p.Config.LogY || p.Config.LogX || Refreshing) return;
        if (!InGraph(p, x, y)) return;
        p.SaveScales();
        double xr = (double)(x - p.GraphLeft) / Math.Max(1, p.GraphRight - p.GraphLeft);
        double yr = (double)(y - p.GraphTop) / Math.Max(1, p.GraphBottom - p.GraphTop);
        bool allY = p.Config.Multi && shift;
        bool zoomX = !p.Config.Multi || allY;
        double sgn = delta < 0 ? 1 : -1;
        if (zoomX)
        {
            p.XScale.FixedScale = false;
            p.XSetMin += sgn * p.XScale.Graduation * xr;
            p.XSetMax -= sgn * p.XScale.Graduation * (1.0 - xr);
        }
        if (!p.Config.Multi)
        {
            p.YScales[0].FixedScale = false;
            p.YSetMin += sgn * p.YScales[0].Graduation * (1.0 - yr);
            p.YSetMax -= sgn * p.YScales[0].Graduation * yr;
        }
        else
        {
            for (int i = allY ? 0 : p.SelectedAxis; i < p.NScales; i++)
            {
                var s = p.YScales[i];
                var c0 = p.Curves.FirstOrDefault(c => c.ScaleIndex == i);
                s.FixedScale = false;
                p.YSetMin = (c0 != null ? c0.VMin : s.RMin) + sgn * s.Graduation * (1.0 - yr);
                p.YSetMax = (c0 != null ? c0.VMax : s.RMax) - sgn * s.Graduation * yr;
                SetScaleCurves(p, i);
                if (!allY) break;
            }
        }
        Invalidate(p);
    }

    /// <summary>Time label of a pixel column (slice/track bars).</summary>
    public string TimeLabelAt(PlotDocument p, int x)
    {
        double xv = XAt(p, x);
        if (!p.Config.ClockTime) return Crt.Sprintf("%g", xv);
        var d = p.Curves[0].Data;
        if (d.RefTime.HasValue) xv += d.RefTime.Value;
        return ValueFormat.ClockString(TimeMath.Gmt(xv, ClockBase(p, true), true));
    }

    /// <summary>Track text ("Time: … name=value …").</summary>
    public string TrackText(PlotDocument p, int x)
    {
        double xv = XAt(p, x);
        var sb = new StringBuilder("Time: " + TimeLabelAt(p, x));
        foreach (var c in p.Curves)
        {
            string name = c.Name.Length > 20 ? c.Name[..20] : c.Name;
            sb.Append(' ').Append(name).Append(c.Name.Length > 16 ? "..." : "").Append('=');
            if (c.Points > 0 && xv >= c.Time[0] && xv <= c.Time[c.Points - 1])
            {
                int n = TimeMath.Location(xv, c.Time, c.Points, 2);
                sb.Append(ValueFormat.FormatValue(c.Values[n], TabFormat.General, (p.Config.YNomen & Nomen.EFORMAT) != 0));
            }
            else sb.Append("N/A");
        }
        return sb.ToString();
    }

    /// <summary>Delta text between two pixel points.</summary>
    public string DeltaText(PlotDocument p, int x0, int y0, int x1, int y1)
    {
        double ax = XAt(p, x0), bx = XAt(p, x1);
        double changeX = bx - ax;
        var sb = new StringBuilder(Crt.Sprintf(" (delta) X=%g", Math.Abs(changeX)));
        double ya = p.GraphBottom - y0, yb = Math.Abs((double)(p.GraphBottom - y1));
        if (p.Config.Multi)
        {
            for (int i = 0; i < p.NCurves; i++)
            {
                var c = p.Curves[i];
                var s = p.YScales[Math.Min(c.ScaleIndex, p.YScales.Count - 1)];
                sb.Append(c.Name.Length > 20 ? Crt.Sprintf(" CURVE%d=", i) : Crt.Sprintf(" %s=", c.Name));
                double off = yb / s.Factor + s.RMin, anc = ya / s.Factor + s.RMin;
                if (p.Config.LogY) { off = Math.Exp(off); anc = Math.Exp(anc); }
                sb.Append(Crt.Sprintf("%g", off - anc));
            }
        }
        else
        {
            var s = p.YScales[0];
            double changeY = (yb / s.Factor + s.RMin) - (ya / s.Factor + s.RMin);
            sb.Append(Crt.Sprintf(" Y=%g", Math.Abs(changeY)));
            sb.Append(changeX != 0 ? Crt.Sprintf(" Slope=%g", changeY / changeX) : " Slope=n/a");
        }
        return sb.ToString();
    }

    /// <summary>X data value of a pixel column.</summary>
    public static double XValueAt(PlotDocument p, int x) => XAt(p, x);
}
