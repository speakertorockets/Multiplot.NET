// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

using System.Windows.Media;
using Multiplot.Core;
using Multiplot.Core.Model;
using Multiplot.Core.Plotting;
using Multiplot.Plugins;

namespace Multiplot.Rendering;

/// <summary>
/// Plot window renderer: graphs, tabular displays, markers, symbols and pixel reduction (chapter 08).
/// Layout rules, colours and scale values follow chapter 08 (DEV-UI-001).
/// </summary>
public sealed class PlotRenderer
{
    /// <summary>
    /// Plot window size (smaller of width and 1.5 × height, in device-independent pixels) at which a font
    /// is drawn at about its chosen point size; text grows and shrinks in proportion with the window.
    /// </summary>
    public const double FontReferenceSize = 768.0;

    /// <summary>
    /// Text cell height (pixels) of a font of <paramref name="pointSizeTenths"/> (tenths of a point) in a plot
    /// whose smaller dimension is <paramref name="minHW"/>: proportional to the window, so that a font of
    /// 10 pt has a cell height of 1/50 of <paramref name="minHW"/>, and never below 1.2 pixels per point.
    /// </summary>
    public static int FontCellHeight(int pointSizeTenths, double minHW)
    {
        double pt = Math.Clamp(pointSizeTenths > 0 ? pointSizeTenths / 10.0 : 10.0, 4.0, 200.0);
        return (int)Math.Max(1.2 * pt, minHW * pt / 500.0);
    }

    /// <summary>Most legend rows per column for a label font of <paramref name="pointSizeTenths"/>.</summary>
    private static int MaxLegendRows(int pointSizeTenths) =>
        Math.Clamp((int)(1600.0 / Math.Max(40, pointSizeTenths)), 4, 30);
    private const int WpdDisplayRes = 4096;

    private readonly PlotDocument _d;
    private PlotConfig _c = null!;
    private GdiCanvas _g = null!;
    private int _compactBufSz;
    private float[] _packedTime = Array.Empty<float>(), _packedData = Array.Empty<float>();
    private int[] _packedIndx = Array.Empty<int>();
    private int _tickW, _tickH;
    private GdiFont? _legendFont;

    /// <summary>The user name shown in the footer.</summary>

    public PlotRenderer(PlotDocument doc) => _d = doc;

    private static int IntFloat(double x)
    {
        if (double.IsNaN(x)) return 0;
        return (int)(Math.Abs(x) < 16383f ? x : 16383.0 * (x >= 0 ? 1.0 : -1.0));
    }

    private static double Log(double x) => x > 0.0 ? Math.Log(x) : -99999.99;

    private static bool IsDrop(float f) => BitConverter.SingleToUInt32Bits(f) == DataTypes.DropOutBits;

    private static string DspFmt(bool e) => e ? "%.*e" : "%.*f";

    private bool _dark;

    /// <summary>Displayed colour of colour-table entry <paramref name="idx"/> (mono colour in black-and-white mode; adapted in the dark appearance).</summary>
    private int Col(int idx)
    {
        int i = _c.Color ? idx : ColorIndex.Mono;
        return PlotColors.Map(_c.DrawColor[i], i, _dark);
    }

    private GdiPen TracePen(Curve c) => new(_c.Is3D ? GdiPen.Solid : c.LineStyle, c.LineSize, Col(c.ColorIndex));

    private GdiPen SymbolPen(Curve c) => new(GdiPen.Solid, c.LineSize, Col(c.ColorIndex));

    /// <summary>Renders the plot into a drawing context of size W×H.</summary>
    public void Draw(DrawingContext dc, int W, int H, bool imageCopy)
    {
        var d = _d;
        _c = d.Config;
        _g = new GdiCanvas(dc);
        var g = _g;
        d.RenderWidth = W;
        d.RenderHeight = H;
        _compactBufSz = Math.Max(2048, Math.Max(W, (int)System.Windows.SystemParameters.PrimaryScreenWidth) * 5);
        if (_packedTime.Length < _compactBufSz)
        {
            _packedTime = new float[_compactBufSz];
            _packedData = new float[_compactBufSz];
            _packedIndx = new int[_compactBufSz];
        }
        int nCurves = d.NCurves;

        _dark = d.Host.DarkAppearance;
        g.FillRect(0, 0, W, H, PlotColors.Display(_c.DrawColor, ColorIndex.Background, _dark));
        if (nCurves == 0) { g.Finish(); return; }

        int maxRows = MaxLegendRows(_c.LabelPointSize);
        if (_c.LegendColumns < 1) _c.LegendColumns = 1;
        if (_c.Legend && (nCurves / _c.LegendColumns) > maxRows)
            _c.LegendColumns = (nCurves / maxRows) + ((nCurves % maxRows) != 0 ? 1 : 0);
        if ((_c.Multi && (nCurves == 1 || d.NScales == 1)) || d.NScales > 10) _c.Multi = false;
        if (_c.ClockTime && _c.EndToEnd) _c.ClockTime = false;

        int tickH = (int)(H * .01f);
        int tickW = (int)(W * .0066f);
        _tickH = tickH;
        _tickW = tickW;
        int rightO = (int)(W * .03f);
        int bottomO = tickH;
        int lineSz = _c.LineSize;

        if (_c.Style == PlotStyle.Tabular)
        {
            if (d.XScale.IsZoomed == 0) d.SaveScales();
            d.XScale.RMin = 0.0;
            d.XScale.RMax = WpdDisplayRes;
            d.XScale.Graduation = 1024.0;
            d.XScale.FixedScale = true;
            d.XScale.ScreenRange = W;
            d.XScale.EFormat = false;
            d.XScale.Logarithmic = false;
            d.YScales[0] = d.XScale.Clone();
            d.YScales[0].ScreenRange = H;
            d.XScale.GetScale();
            d.YScales[0].GetScale();
            d.GraphLeft = d.GraphTop = 0;
            d.GraphBottom = H;
            d.GraphRight = W;
            DrawTabular(W, H, lineSz);
            g.Finish();
            return;
        }

        if (_c.Style == PlotStyle.Logic)
        {
            _c.Multi = false;
            _c.LogY = false;
        }

        // font sizes (FR-UI-029): the chosen point sizes, scaled with the window
        float minHW = Math.Min(H * 1.5f, W);
        int baseFontH = FontCellHeight(_c.LabelPointSize, minHW);

        int xtitO = 0, ytitO = 0, ftitO = 0, rtitO = 0;
        GdiFont? titleFont = null, vTitleFont = null;
        (int cx, int cy) te;
        if (d.UseTitle || d.UseSubTitle)
        {
            int th = FontCellHeight(_c.TitlePointSize, minHW);
            titleFont = new GdiFont(_c.TitleFont.FaceName, th, _c.TitleFont.Weight, _c.TitleFont.Italic != 0, false);
            vTitleFont = new GdiFont(_c.TitleFont.FaceName, th, _c.TitleFont.Weight, _c.TitleFont.Italic != 0, true);
            g.TextColor = Col(ColorIndex.Title);
            if (d.UseTitle && d.XTitle.Length > 0)
            {
                g.Font = titleFont;
                int b = d.XTitle.IndexOf("\\t", StringComparison.Ordinal);
                string ts = b == -1 ? d.XTitle : d.XTitle[..b];
                if (ts.Length > 0)
                {
                    te = g.TextExtent(ts);
                    xtitO = te.cy + tickH;
                    int p = ts.IndexOf("\\n", StringComparison.Ordinal);
                    if (p == -1) g.TextOut((W / 2) - (te.cx / 2), 0, ts);
                    else
                    {
                        te = g.TextExtent(ts[..p]);
                        g.TextOut((W / 2) - (te.cx / 2), 0, ts[..p]);
                        te = g.TextExtent(ts[(p + 2)..]);
                        g.TextOut((W / 2) - (te.cx / 2), te.cy, ts[(p + 2)..]);
                        xtitO *= 2;
                    }
                }
                if (b != -1)
                {
                    ts = d.XTitle[(b + 2)..];
                    if (ts.Length > 0)
                    {
                        te = g.TextExtent(ts);
                        ftitO = te.cy + 1;
                        int p = ts.IndexOf("\\n", StringComparison.Ordinal);
                        if (p == -1) g.TextOut((W / 2) - (te.cx / 2), H - te.cy, ts);
                        else
                        {
                            te = g.TextExtent(ts[..p]);
                            g.TextOut((W / 2) - (te.cx / 2), H - (2 * te.cy), ts[..p]);
                            te = g.TextExtent(ts[(p + 2)..]);
                            g.TextOut((W / 2) - (te.cx / 2), H - te.cy, ts[(p + 2)..]);
                            ftitO *= 2;
                        }
                    }
                }
            }
            if (d.UseSubTitle && d.YTitle.Length > 0)
            {
                g.Font = titleFont;
                te = g.TextExtent(d.YTitle);
                int p = d.YTitle.IndexOf("\\t", StringComparison.Ordinal);
                if (p != -1)
                {
                    if (p > 0) ytitO = te.cy;
                    int n = d.YTitle.IndexOf("\\n", StringComparison.Ordinal);
                    if (n < p && n != -1) ytitO *= 2;
                    rtitO = te.cy;
                    if (d.YTitle.IndexOf("\\n", p, StringComparison.Ordinal) != -1) rtitO *= 2;
                }
                else
                {
                    ytitO = te.cy;
                    if (d.YTitle.Contains("\\n", StringComparison.Ordinal)) ytitO *= 2;
                }
            }
        }

        var lf = _c.LabelFont;
        int scaleidFontH = 0;
        if (_c.GroupId)
        {
            scaleidFontH = (int)(baseFontH * .75f);
            if (scaleidFontH < 8) scaleidFontH = 8;
        }
        var legendFont = new GdiFont(lf.FaceName, baseFontH, lf.Weight, lf.Italic != 0, false);
        _legendFont = legendFont;
        var smallFont = new GdiFont(lf.FaceName, (baseFontH / 2) > 6 ? (baseFontH / 2) : 6, lf.Weight, lf.Italic != 0, false);
        g.Font = legendFont;

        string strBuf;
        int yaSpacing;
        bool eFormatY = (_c.YNomen & Nomen.EFORMAT) != 0;
        if (_c.Multi)
        {
            yaSpacing = 0;
            te = (0, 0);
            for (int p = 0; p < d.NScales; p++)
            {
                bool bSet = false;
                var s = d.YScales[p];
                foreach (var cv in d.Curves)
                {
                    if (cv.ScaleIndex != p) continue;
                    if (bSet)
                    {
                        s.RMin = s.RMin < cv.VMin ? s.RMin : cv.VMin;
                        s.RMax = s.RMax > cv.VMax ? s.RMax : cv.VMax;
                    }
                    else
                    {
                        s.RMin = cv.VMin;
                        s.RMax = cv.VMax;
                        bSet = true;
                    }
                }
                if (!bSet) s.RMin = s.RMax = 0.0;
                s.ScreenRange = 10;
                s.EFormat = eFormatY;
                s.Logarithmic = _c.LogY;
                s.GetScale();
                strBuf = _c.LogY
                    ? Crt.Sprintf(DspFmt(s.EFormat), s.DecimalPlaces, Math.Pow(10.0, Math.Max(Math.Abs(s.MaxExp), Math.Abs(s.MinExp))))
                    : Crt.Sprintf(DspFmt(s.EFormat), s.DecimalPlaces, Math.Max(Math.Abs(s.RMax), Math.Abs(s.RMin)));
                te = g.TextExtent(strBuf);
                if (te.cx > yaSpacing) yaSpacing = te.cx;
            }
        }
        else if (_c.Style == PlotStyle.Logic)
        {
            var host = d.Host;
            int nSel = d.Curves[0].LogicTxt % 4;
            int maxL = Math.Max(host.LogicTxtTrue[nSel].Length, host.LogicTxtFalse[nSel].Length);
            for (int i = 1; i < nCurves; i++)
            {
                nSel = d.Curves[i].LogicTxt % 4;
                maxL = Math.Max(maxL, Math.Max(host.LogicTxtTrue[nSel].Length, host.LogicTxtFalse[nSel].Length));
            }
            strBuf = new string('X', Math.Min(32, (maxL / 2) + 1));
            te = g.TextExtent(strBuf);
            yaSpacing = (2 * tickW) + Math.Max(0, te.cx - (3 * tickW));
        }
        else
        {
            var s = d.YScales[0];
            s.RMin = d.YSetMin;
            s.RMax = d.YSetMax;
            s.ScreenRange = 10;
            s.EFormat = eFormatY;
            s.Logarithmic = _c.LogY;
            s.GetScale();
            strBuf = _c.LogY
                ? Crt.Sprintf(DspFmt(s.EFormat), s.DecimalPlaces, Math.Pow(10.0, Math.Max(Math.Abs(s.MaxExp), Math.Abs(s.MinExp))))
                : Crt.Sprintf(DspFmt(s.EFormat), s.DecimalPlaces, Math.Max(Math.Abs(s.RMax), Math.Abs(s.RMin)));
            te = g.TextExtent(strBuf);
            yaSpacing = te.cx;
        }
        int topO = te.cy;
        if (topO == 0) topO = g.TextExtent("X").cy;
        yaSpacing += 3 * tickW;
        d.YaSpacing = yaSpacing;

        int leftO = _c.Multi ? d.NScales * yaSpacing : yaSpacing;
        leftO += ytitO;
        int reservedYAxis = (W / 100) * d.Host.MinGridOffset;
        if (leftO < reservedYAxis) leftO = reservedYAxis;

        int symSize = (int)(Math.Min(H * 1.5f, (float)W) * .005f);
        if (_c.SymbolSize == 1) symSize = (int)(symSize * 1.25f);
        else if (_c.SymbolSize == -1) symSize = (int)(symSize * .75f);
        if (symSize < 1) symSize = 1;
        d.SymSize = symSize;
        int scaleIDsPerRow = (_c.Multi && d.NScales > 1) ? Math.Max(1, yaSpacing / (5 * symSize)) : 1;

        int gTop, gLeft, gRight, gBottom;
        if (_c.Legend)
        {
            _c.LegendColumns = _c.LegendColumns > nCurves ? nCurves : _c.LegendColumns;
            int rows = (nCurves / _c.LegendColumns) + (nCurves % _c.LegendColumns != 0 ? 1 : 0);
            if (((rows * _c.LegendColumns) - nCurves) >= rows)
                _c.LegendColumns = nCurves / rows + (nCurves % rows != 0 ? 1 : 0);
            int legRes1 = tickH + topO * (nCurves / _c.LegendColumns + (nCurves % _c.LegendColumns != 0 ? 1 : 0));
            int legRes2 = (topO * d.Host.ReservedLegend) + tickH;
            gTop = tickH + Math.Max(legRes1, legRes2);
        }
        else
            gTop = 2 * tickH;
        gTop += xtitO;
        gLeft = leftO;
        gRight = W - 1 - rightO - rtitO;
        if (_c.ClockTime || (_c.XNomen & Nomen.EFORMAT) != 0)
        {
            te = g.TextExtent("_000/00:00:00.000_");
            if ((W - gRight) < (te.cx / 2)) gRight = W - (te.cx / 2);
            if (gLeft < (te.cx / 2)) gLeft = te.cx / 2;
        }
        gBottom = H - 1 - bottomO - (2 * (topO + tickH)) - ftitO;
        string testTitle = _c.TestInfo ? TestTitle(d) : "";
        if (testTitle.Length > 0) gBottom -= topO;
        if (_c.Legend && _c.Multi && d.NScales > 1)
        {
            int nSymbs = 0;
            for (int ii = 0; ii < d.NScales; ii++) nSymbs = ii != 0 ? Math.Max(nSymbs, d.YScales[ii].UseCount) : d.YScales[ii].UseCount;
            int symSpace = (3 * tickH) + topO + (nSymbs / scaleIDsPerRow) * (3 * symSize) + symSize;
            if (H < (gBottom + symSpace + scaleidFontH)) gBottom -= (gBottom + symSpace + scaleidFontH + 2) - H;
        }
        if (gBottom <= gTop) gBottom = gTop + 1;
        if (gRight <= gLeft) gRight = gLeft + 1;

        int tdxo = 0, tdyo = 0;
        if (_c.Is3D)
        {
            float zoffset = (90 - _c.ZAngle) / 900.0f;
            zoffset *= nCurves > 3 ? 3 : nCurves;
            if (zoffset > .35f) zoffset = .35f;
            tdxo = (int)(zoffset * (gRight - gLeft));
            tdxo /= nCurves;
            gRight -= tdxo * nCurves;
            zoffset = _c.ZAngle / 900.0f;
            zoffset *= nCurves > 3 ? 3 : nCurves;
            if (zoffset > .35f) zoffset = .35f;
            tdyo = (int)(zoffset * (gBottom - gTop));
            tdyo /= nCurves;
            gTop += tdyo * nCurves;
        }
        d.Tdxo = tdxo;
        d.Tdyo = tdyo;
        SetGraph(gLeft, gTop, gRight, gBottom);

        // Y axis title
        if (d.UseSubTitle && d.YTitle.Length > 0 && vTitleFont != null)
        {
            g.TextColor = Col(ColorIndex.Title);
            g.Font = titleFont;
            string tit = d.YTitle;
            int tSplit = tit.IndexOf("\\t", StringComparison.Ordinal);
            string left = tSplit != -1 ? tit[..tSplit] : tit;
            int mid = ((gBottom - gTop) / 2) + gTop;
            int p = left.IndexOf("\\n", StringComparison.Ordinal);
            if (p == -1)
            {
                te = g.TextExtent(left);
                g.Font = vTitleFont;
                g.TextOut(0, mid + (te.cx / 2), left);
            }
            else
            {
                te = g.TextExtent(left[..p]);
                g.Font = vTitleFont;
                g.TextOut(0, mid + (te.cx / 2), left[..p]);
                g.Font = titleFont;
                te = g.TextExtent(left[(p + 2)..]);
                g.Font = vTitleFont;
                g.TextOut(te.cy, mid + (te.cx / 2), left[(p + 2)..]);
            }
            if (tSplit != -1)
            {
                string right = tit[(tSplit + 2)..];
                g.Font = titleFont;
                p = right.IndexOf("\\n", StringComparison.Ordinal);
                if (p == -1)
                {
                    te = g.TextExtent(right);
                    g.Font = vTitleFont;
                    g.TextOut(W - te.cy - 2, mid + (te.cx / 2), right);
                }
                else
                {
                    te = g.TextExtent(right[(p + 2)..]);
                    g.Font = vTitleFont;
                    g.TextOut(W - te.cy - 2, mid + (te.cx / 2), right[(p + 2)..]);
                    g.Font = titleFont;
                    te = g.TextExtent(right[..p]);
                    g.Font = vTitleFont;
                    g.TextOut(W - (te.cy * 2) - 2, mid + (te.cx / 2), right[..p]);
                }
            }
            g.Font = legendFont;
        }

        // scaling
        var xs = d.XScale;
        xs.RMin = d.XSetMin;
        xs.RMax = d.XSetMax;
        xs.ScreenRange = gRight - gLeft;
        xs.EFormat = (_c.XNomen & Nomen.EFORMAT) != 0;
        xs.Logarithmic = _c.LogX;
        xs.GetScale();
        if (!xs.FixedScale && _c.ClockTime)
        {
            var refG = PlotDocument.HeaderGmt(d.Curves[0].Data);
            var cg = TimeMath.Gmt(xs.RMin, refG, false);
            xs.AdjustToClockScale(cg.MilliSeconds, d.GmtFormat);
        }

        if (_c.Style == PlotStyle.Logic)
        {
            var s = d.YScales[0];
            s.FixedScale = true;
            s.Persist = true;
            s.Graduation = 1.0;
            s.ScreenRange = gBottom - gTop;
            s.RMin = 0.0;
            s.RMax = (nCurves * 2) + 1;
            s.EFormat = false;
            s.Logarithmic = false;
            s.GetScale();
        }
        else if (_c.Multi)
        {
            for (int sc = 0; sc < d.NScales; sc++)
            {
                var s = d.YScales[sc];
                if (_c.LogY)
                {
                    foreach (var cv in d.Curves)
                    {
                        if (cv.ScaleIndex != sc) continue;
                        s.RMin = s.RMin < cv.VMin ? s.RMin : cv.VMin;
                        s.RMax = s.RMax > cv.VMax ? s.RMax : cv.VMax;
                    }
                }
                s.ScreenRange = gBottom - gTop;
                s.EFormat = eFormatY;
                s.Logarithmic = _c.LogY;
                s.GetScale();
            }
        }
        else
        {
            var s = d.YScales[0];
            if (_c.LogY)
            {
                s.RMin = d.YSetMin;
                s.RMax = d.YSetMax;
            }
            s.ScreenRange = gBottom - gTop;
            s.EFormat = eFormatY;
            s.Logarithmic = _c.LogY;
            s.GetScale();
        }
        d.SelectedAxis = _c.Multi ? (d.SelectedAxis > d.NScales - 1 ? 0 : d.SelectedAxis) : 0;
        var ysel = d.YScales[d.SelectedAxis];

        // grid lines
        if (_c.GridLines)
        {
            g.Pen = new GdiPen(_c.GridStyle, 0, Col(ColorIndex.Grid));
            int li = xs.Divisions / (_c.ClockTime ? 5 : 7);
            if (li == 0) li = 1;
            for (int i = 1; i <= xs.Divisions - 1; i++)
            {
                if (xs.Divisions <= 100 || (i % li) == 0)
                {
                    int xv = gLeft + (int)(xs.Factor * (xs.Graduation * i));
                    g.MoveTo(xv, gBottom);
                    if (_c.Is3D)
                    {
                        g.LineTo(xv + nCurves * tdxo, gBottom - nCurves * tdyo);
                        g.LineTo(xv + nCurves * tdxo, gTop - nCurves * tdyo);
                    }
                    else
                        g.LineTo(xv, gTop - 1);
                }
            }
            if (d.SelectedAxis != 0 && _c.Color)
            {
                for (int i = 1; i < nCurves; i++)
                {
                    if (d.Curves[i].ScaleIndex == d.SelectedAxis)
                    {
                        g.Pen = new GdiPen(_c.GridStyle, 0, Col(d.Curves[i].ColorIndex));
                        break;
                    }
                }
            }
            li = ysel.Divisions / 10;
            if (li == 0) li = 1;
            for (int i = 1; i <= ysel.Divisions - 1; i++)
            {
                if (ysel.Divisions <= 100 || ((ysel.Divisions - i) % li) == 0)
                {
                    int yv = gTop + (int)(ysel.Factor * ysel.Graduation * i);
                    g.MoveTo(gLeft, yv);
                    if (_c.Is3D)
                    {
                        g.LineTo(gLeft + nCurves * tdxo, yv - nCurves * tdyo);
                        g.LineTo(gRight + nCurves * tdxo, yv - nCurves * tdyo);
                    }
                    else
                        g.LineTo(gRight, yv);
                }
            }
        }

        var framePen = new GdiPen(GdiPen.Solid, 0, Col(ColorIndex.Grid));
        g.Pen = framePen;
        if (!_c.Is3D && _c.BoldFrame)
        {
            g.MoveTo(gLeft + 1, gTop);
            g.LineTo(gLeft + 1, gBottom - 1);
            g.LineTo(gRight - 1, gBottom - 1);
            g.LineTo(gRight - 1, gTop + 1);
            g.LineTo(gLeft + 1, gTop + 1);
        }

        // X axis ticks and labels
        var tmpScl = new Scale(0.001, xs.Graduation, 10.0, false, false, false, false);
        int labelInterval;
        if (_c.ClockTime && xs.Divisions < 10 && xs.Divisions > 5) labelInterval = 2;
        else
        {
            labelInterval = xs.Divisions / (_c.ClockTime ? 5 : 7);
            if (labelInterval == 0) labelInterval = 1;
        }
        g.TextColor = Col(ColorIndex.Label);
        TimeTag clockRef = PlotDocument.HeaderGmt(d.Curves[0].Data);
        for (int i = 0; i <= xs.Divisions; i++)
        {
            int xv = IntFloat((xs.Factor * (xs.Graduation * i)) + gLeft + (i == xs.Divisions ? .5 : 0.0));
            if ((i % labelInterval) == 0)
            {
                if (_c.ClockTime && !_c.PidVsPid)
                {
                    double dTime = xs.RMin + (xs.Graduation * i);
                    if (d.Curves[0].Data.RefTime.HasValue) dTime += d.Curves[0].Data.RefTime.Value;
                    var cg = TimeMath.Gmt(dTime, clockRef, false);
                    uint ms = cg.MilliSeconds;
                    strBuf = d.GmtFormat.StartsWith("%03u", StringComparison.Ordinal)
                        ? Crt.Sprintf(d.GmtFormat, (uint)cg.JulianDay, (ms / 3600000U) % 24, (ms % 3600000U) / 60000U, (ms % 60000U) / 1000U, ms % 1000U)
                        : Crt.Sprintf(d.GmtFormat, (ms / 3600000U) % 24, (ms % 3600000U) / 60000U, (ms % 60000U) / 1000U, ms % 1000U);
                }
                else
                    strBuf = Crt.Sprintf(DspFmt(xs.EFormat), xs.DecimalPlaces, _c.LogX ? Math.Pow(10.0, xs.MinExp + i) : xs.RMin + (xs.Graduation * i));
                te = g.TextExtent(strBuf);
                g.TextOut(xv - (te.cx / 2), gBottom + tickH, strBuf);
                g.MoveTo(xv, gBottom + tickH);
            }
            else
                g.MoveTo(xv, gBottom + (tickH / 2 != 0 ? tickH / 2 : 1));
            if (_c.Is3D)
            {
                g.LineTo(xv, gBottom);
                if (i == 0 || i == xs.Divisions)
                {
                    g.LineTo(xv + nCurves * tdxo, gBottom - nCurves * tdyo);
                    g.LineTo(xv + nCurves * tdxo, gTop - nCurves * tdyo);
                }
            }
            else
                g.LineTo(xv, (i == 0 || i == xs.Divisions) ? gTop - 1 : gBottom);

            if (_c.MinorTics && i < xs.Divisions && labelInterval < 3)
            {
                if (_c.LogX)
                {
                    float labelDelta = (float)((xs.Factor * xs.Graduation) / Log(10.0));
                    for (int t = 0; t < 8; t++)
                    {
                        g.MoveTo(xv + (int)(labelDelta * Log(9.0 - t)), gBottom + (tickH / 2 != 0 ? tickH / 2 : 1));
                        g.LineTo(xv + (int)(labelDelta * Log(9.0 - t)), gBottom);
                    }
                }
                else
                {
                    float labelDelta = (float)(xs.Factor * xs.Graduation) / tmpScl.Divisions;
                    for (int t = 1; t < tmpScl.Divisions; t++)
                    {
                        g.MoveTo(xv + (int)(labelDelta * t), gBottom + (tickH / 2 != 0 ? tickH / 2 : 1));
                        g.LineTo(xv + (int)(labelDelta * t), gBottom);
                    }
                }
            }
        }

        // Y axes
        if (_c.Style == PlotStyle.Logic) g.Font = smallFont;
        for (int p = 0; p < (_c.Multi ? d.NScales : 1); p++)
        {
            var s = d.YScales[p];
            int ax = gLeft - (p * yaSpacing);
            if (p != 0)
            {
                g.MoveTo(ax, gBottom);
                g.LineTo(ax, gTop);
            }
            if (_c.Color)
            {
                if (_c.Multi)
                {
                    foreach (var cv in d.Curves)
                        if (cv.ScaleIndex == p) { g.TextColor = Col(cv.ColorIndex); break; }
                }
                else
                    g.TextColor = Col(ColorIndex.Label);
            }
            else
                g.TextColor = Col(ColorIndex.Mono);

            labelInterval = s.Divisions / 10;
            if (labelInterval == 0) labelInterval = 1;
            int tLog = _c.LogY ? (int)(s.Factor * s.Graduation) : 0;
            for (int i = 0; i <= s.Divisions; i++)
            {
                int yv = (int)((s.Factor * (s.Graduation * i)) + gTop + (i == s.Divisions ? .5 : 0.0));
                if (_c.Style != PlotStyle.Logic && _c.MinorTics && i < s.Divisions && labelInterval < 3)
                {
                    if (_c.LogY)
                    {
                        float labelDelta = (float)((s.Factor * s.Graduation) / Log(10.0));
                        for (int t = 0; t < 8; t++)
                        {
                            g.MoveTo(ax - (tickW / 2 != 0 ? tickW / 2 : 1), tLog + yv - (int)(labelDelta * Log(9.0 - t)));
                            g.LineTo(ax, tLog + yv - (int)(labelDelta * Log(9.0 - t)));
                        }
                    }
                    else
                    {
                        if (i == 0)
                        {
                            tmpScl.RMin = 0.001;
                            tmpScl.RMax = s.Graduation;
                            tmpScl.GetScale();
                        }
                        float labelDelta = (float)(s.Factor * s.Graduation) / tmpScl.Divisions;
                        for (int t = 1; t < tmpScl.Divisions; t++)
                        {
                            g.MoveTo(ax - (tickW / 2 != 0 ? tickW / 2 : 1), yv + (int)(labelDelta * t));
                            g.LineTo(ax, yv + (int)(labelDelta * t));
                        }
                    }
                }

                if (((s.Divisions - i) % labelInterval) == 0 || _c.Style == PlotStyle.Logic)
                {
                    if (_c.Style == PlotStyle.Logic)
                    {
                        if (i > 0 && i < s.Divisions)
                        {
                            int ci = Math.Min((i - 1) / 2, nCurves - 1);
                            int idx = d.Curves[ci].LogicTxt % 4;
                            strBuf = (i % 2) != 0 ? d.Host.LogicTxtTrue[idx] : d.Host.LogicTxtFalse[idx];
                            te = g.TextExtent(strBuf);
                            g.TextOut(ax - (int)(1.5f * tickW) - te.cx, yv - ((i % 2) != 0 ? 0 : te.cy), strBuf);
                        }
                    }
                    else
                    {
                        strBuf = Crt.Sprintf(DspFmt(s.EFormat), s.DecimalPlaces, _c.LogY ? Math.Pow(10.0, s.MaxExp - i) : s.RMax - (s.Graduation * i));
                        te = g.TextExtent(strBuf);
                        g.TextOut(ax - (int)(1.5f * tickW) - te.cx, yv - te.cy / 2, strBuf);
                    }
                    if (i == 0 && !d.Host.PresentationMode && !imageCopy && _c.Multi && d.NScales > 1 && p == d.SelectedAxis)
                    {
                        var xsz = g.TextExtent("*");
                        g.TextOut(ax - (int)(1.5f * tickW) - te.cx - xsz.cx, yv - (int)(te.cy / 1.5), "*");
                    }
                    g.MoveTo(ax - tickW, yv);
                }
                else
                    g.MoveTo(ax - (tickW / 2 != 0 ? tickW / 2 : 1), yv);

                if (_c.Style == PlotStyle.Logic) g.MoveTo(gLeft, yv);
                else g.LineTo(ax, yv);

                if (p == 0)
                {
                    if (_c.Is3D)
                    {
                        g.LineTo(gLeft, yv);
                        if (i == 0 || i == d.YScales[0].Divisions)
                        {
                            g.LineTo(gLeft + nCurves * tdxo, yv - nCurves * tdyo);
                            g.LineTo(gRight + nCurves * tdxo, yv - nCurves * tdyo);
                        }
                    }
                    else
                        g.LineTo((i == 0 || i == s.Divisions) ? gRight : gLeft, yv);
                }
            }
        }
        g.Font = legendFont;
        g.TextColor = Col(ColorIndex.Label);

        // X axis label
        string xLegend = d.XLegend();
        te = g.TextExtent(xLegend);
        g.TextOut(((gLeft + gRight) / 2) - (te.cx / 2), gBottom + (2 * tickH) + te.cy, xLegend);

        // footers
        string certStatus = "";
        if (d.Host.StreamCert)
        {
            foreach (var cv in d.Curves)
            {
                if (cv.Data.Source?.Info.CertStatus is { } cs) { certStatus = cs; break; }
            }
        }
        if (d.Curves.Any(cv => cv.Data.TimeShift != 0f)) certStatus += "    >> Contains time shifted parameters";
        if (!_c.Is3D)
        {
            if (_c.DataRate == DataRateMode.Averaged) certStatus += Crt.Sprintf("    *Data is Averaged to %d points", (int)_c.AvgFreq);
            else if (_c.DataRate == DataRateMode.Sampled) certStatus += Crt.Sprintf("    *Data is Sampled to %d points", (int)_c.SamFreq);
        }

        // scale group IDs below the value axes
        if (_c.Multi && _c.GroupId)
        {
            {
                g.Font = new GdiFont(lf.FaceName, scaleidFontH, lf.Weight, lf.Italic != 0, false);
                int downShift = 0;
                if (_c.Legend && d.NScales > 1)
                {
                    for (int ii = 0; ii < d.NScales; ii++)
                        downShift = Math.Max(downShift, d.Curves.Count(cv => cv.ScaleIndex == ii));
                    downShift = ((downShift % scaleIDsPerRow != 0 ? 1 : 0) + (downShift / scaleIDsPerRow)) * (3 * symSize);
                }
                for (int p = 0; p < d.NScales; p++)
                {
                    te = g.TextExtent(d.YScales[p].Name);
                    g.PushClip(0, 0, gLeft - (p * yaSpacing) - (tickW / 2), H);
                    int startPos = gLeft - (p * yaSpacing) - (tickW / 2) - te.cx;
                    if (startPos < (gLeft - ((p + 1) * yaSpacing) - (tickW / 2)))
                        startPos = (gLeft - ((p + 1) * yaSpacing) - (tickW / 2)) + symSize;
                    g.TextOut(startPos, gBottom + (2 * tickH) + topO + downShift, d.YScales[p].Name);
                    g.PopClip();
                }
            }
        }

        // bottom-left corner: the test title of the plot's first file (Test Info), then the status notes
        int footX = tickW;
        if (testTitle.Length > 0)
        {
            g.Font = legendFont;
            te = g.TextExtent(testTitle);
            g.TextOut(footX, H - te.cy, testTitle);
            footX += te.cx + (4 * tickW);
        }
        if (certStatus.Length > 0)
        {
            g.Font = smallFont;
            te = g.TextExtent(certStatus);
            g.TextOut(Math.Max(footX, gLeft), H - te.cy, certStatus);
        }
        g.Font = legendFont;

        // legend
        if (_c.Legend)
        {
            te = g.TextExtent(d.Curves[0].Name);
            if (_c.Is3D) leftO += nCurves * tdxo;
            if (_c.Multi && _c.LegendShift) leftO -= (d.NScales - 1) * yaSpacing;
            int labelSpace = ((_c.Is3D ? nCurves * tdxo : 0) + gRight - leftO) / _c.LegendColumns;
            int rows = (nCurves / _c.LegendColumns) + (nCurves % _c.LegendColumns != 0 ? 1 : 0);
            int idOffset = 5 * tickW;
            for (int p = 0; p < nCurves; p++)
            {
                var cv = d.Curves[p];
                bool redline = cv.Redline != 0;
                int lx = ((p / rows) * labelSpace) + leftO;
                int ly = topO * (p % rows) + xtitO;
                g.PushClip(lx, ly, lx + labelSpace - tickW, ly + topO);
                g.TextColor = _c.LegendColor ? Col(cv.ColorIndex) : Col(ColorIndex.Label);
                DisplayNomen(lx + idOffset, ly, _c.YNomen, p, d.YUdef, _c.YNomOrder, _c.YNomLen, null);
                g.PopClip();

                if (!cv.UseSymbols || (_c.Style != PlotStyle.Scatter2D && !cv.ScatterPlot) || (cv.ScatterPlot && !cv.UseSymbols) || _c.Is3D)
                {
                    g.Pen = TracePen(cv);
                    int yOffset = (!_c.Is3D && redline) ? (cv.Redline == 1 ? te.cy : 2) : (te.cy / 2);
                    g.MoveTo(lx, ly + yOffset);
                    g.LineTo(lx + (4 * tickW), ly + yOffset);
                    if (_c.Style == PlotStyle.Logic)
                    {
                        int yv = (int)(d.YScales[0].Factor * (d.YScales[0].Graduation * ((p + 1) * 2 - .5)) + gTop);
                        g.MoveTo(gLeft - (3 * tickW), yv);
                        g.LineTo(gLeft - (1 * tickW), yv);
                    }
                }
                if (!_c.Is3D && (redline || cv.UseSymbols))
                {
                    g.Pen = SymbolPen(cv);
                    if (redline)
                    {
                        if (cv.Redline == 2) RedlineMin(symSize, lx + (2 * tickW), ly + 2);
                        else if (cv.Redline == 1) RedlineMax(symSize, lx + (2 * tickW), ly + te.cy);
                    }
                    else
                    {
                        DrawCurveSymbol(cv, symSize, lx + (2 * tickW), ly + (te.cy / 2));
                        if (_c.Style == PlotStyle.Logic)
                        {
                            int yv = (int)(d.YScales[0].Factor * (d.YScales[0].Graduation * ((p + 1) * 2 - .5)) + gTop);
                            DrawCurveSymbol(cv, (int)((1.0 - (nCurves * .06)) * symSize), gLeft - (2 * tickW), yv);
                        }
                    }
                }
            }
            if (_c.Is3D) leftO -= nCurves * tdxo;
            if (_c.Multi && _c.LegendShift) leftO += (d.NScales - 1) * yaSpacing;

            for (int p = 0; p < nCurves && _c.Multi && d.NScales > 1; p++)
            {
                var cv = d.Curves[p];
                int iPos = cv.ScaleIndex;
                int iShift = 0;
                for (int ii = 0; ii < p; ii++) if (d.Curves[ii].ScaleIndex == iPos) iShift++;
                int downShift = (iShift / scaleIDsPerRow) * (3 * symSize);
                int baseY = gBottom + (2 * tickH) + topO + downShift;
                if (_c.Is3D || (_c.Style == PlotStyle.Scatter2D && !cv.UseSymbols)
                    || (_c.Style != PlotStyle.Scatter2D && ((cv.ScatterPlot && !cv.UseSymbols) || !cv.ScatterPlot)))
                {
                    g.Pen = TracePen(cv);
                    g.MoveTo(gLeft - (iPos * yaSpacing) - (3 * symSize) - (5 * ((iShift % scaleIDsPerRow) * symSize)) - tickW, baseY);
                    g.LineTo(gLeft - (iPos * yaSpacing) + symSize - (5 * ((iShift % scaleIDsPerRow) * symSize)) - tickW, baseY);
                }
                if (cv.UseSymbols && !_c.Is3D)
                {
                    g.Pen = SymbolPen(cv);
                    if (cv.Redline == 0)
                        DrawCurveSymbol(cv, (int)(.90 * symSize), gLeft - (iPos * yaSpacing) - symSize - (5 * ((iShift % scaleIDsPerRow) * symSize)) - tickW, baseY);
                    else if (cv.Redline == 1)
                        RedlineMax(symSize, gLeft - (iPos * yaSpacing) - symSize - (3 * ((iShift % scaleIDsPerRow) * symSize)) - tickW, baseY + (2 * symSize));
                    else if (cv.Redline == 2)
                        RedlineMin(symSize, gLeft - (iPos * yaSpacing) - symSize - (3 * ((iShift % scaleIDsPerRow) * symSize)) - tickW, baseY - (2 * symSize));
                }
            }
        }

        if (_c.Is3D)
        {
            gLeft += tdxo * nCurves;
            gBottom -= tdyo * nCurves;
            gRight += tdxo * nCurves;
            gTop -= tdyo * nCurves;
            SetGraph(gLeft, gTop, gRight, gBottom);
        }

        if (_c.Is3D) g.PushClip(gLeft - (tdxo * nCurves), gTop, gRight + 1, gBottom + 1);
        else g.PushClip(gLeft + 1, gTop + 1, gRight, gBottom);
        if (!_c.MarkersTopmost) { DrawMarkerSet(lineSz); DrawRedlines(lineSz); }
        if (_c.Is3D)
        {
            g.PopClip();
            g.PushClip(gLeft - (tdxo * nCurves), gTop, gRight + 1, gBottom + (tdyo * nCurves) + 1);
        }

        // curves
        double hiClip = _c.LogX ? Math.Exp(xs.RMax) : xs.RMax;
        double loClip = _c.LogX ? Math.Exp(xs.RMin) : xs.RMin;
        for (int p = nCurves - 1; p >= 0; p--)
        {
            var cv = d.Curves[p];
            var pd = cv.Data;
            if (pd.Points == 0) continue;
            bool hrTime = !_c.PidVsPid && _c.Use64BitTime && pd.HasValidGmt;
            int[]? iData = null;
            int sf = _c.Multi ? cv.ScaleIndex : 0;
            if (sf >= d.YScales.Count) sf = 0;
            var ys = d.YScales[sf];
            int pStartAt = 0;
            float endToEndRef = 0;
            var time = pd.Time;
            var vals = pd.Values;
            if (!_c.PidVsPid)
            {
                if (_c.EndToEnd)
                {
                    while (pStartAt < pd.Points && (time[pStartAt] - time[0] + cv.Offset) < loClip) pStartAt++;
                    endToEndRef = time[0];
                }
                else
                    while (pStartAt < pd.Points && time[pStartAt] < loClip) pStartAt++;
                if (pStartAt > 0) pStartAt--;
            }

            float[] xData, yData;
            int pCount;
            if ((_c.PidVsPid && _c.DataRate == DataRateMode.Full) || !OptimizedDraw || _c.Style == PlotStyle.Scatter2D || (cv.ScatterPlot && !_c.Is3D))
            {
                pCount = pd.Points;
                xData = time;
                yData = vals;
            }
            else if (pd.Points > 1 && (_c.Is3D || _c.DataRate != DataRateMode.Full))
            {
                hrTime = false;
                xData = _packedTime;
                yData = _packedData;
                pCount = _c.Is3D
                    ? QuickReduce(DataRateMode.Averaged, _c.ZaxFreq, pd)
                    : QuickReduce(_c.DataRate, _c.DataRate == DataRateMode.Sampled ? _c.SamFreq : _c.AvgFreq, pd);
                pStartAt = 0;
            }
            else
            {
                pCount = Pixelize(pStartAt, cv); // may grow the packed buffers, so read them afterwards
                xData = _packedTime;
                yData = _packedData;
                if (pCount != 0)
                {
                    pStartAt = 0;
                    if (hrTime) iData = _packedIndx;
                }
                else
                {
                    pCount = pd.Points;
                    xData = time;
                    yData = vals;
                }
            }

            int z1, z2;
            if (!_c.PidVsPid)
            {
                if (_c.EndToEnd) { z2 = 0; z1 = pCount; }
                else
                {
                    z1 = TimeMath.Location((float)hiClip, xData, pCount, 0);
                    z2 = TimeMath.Location((float)loClip, xData, pCount, 0);
                }
                if (z2 > z1) z2 = z1;
            }
            else { z2 = 0; z1 = pCount; }

            int symbolInterval = (int)((uint)(z1 - z2) / (_c.SymFreq != 0 ? _c.SymFreq : 1));
            if (symbolInterval == 0) symbolInterval = 1;
            int symOffset = !_c.EndToEnd
                ? (int)((symbolInterval / (float)nCurves) * p) + (symbolInterval / 2)
                : (symbolInterval > 2 ? symbolInterval / 2 : 0);

            System.Windows.Media.Brush? fill3d = null;
            if (_c.Is3D)
            {
                g.Pen = framePen;
                if (p < nCurves - 1)
                {
                    gLeft -= tdxo; gRight -= tdxo; gTop += tdyo; gBottom += tdyo;
                    SetGraph(gLeft, gTop, gRight, gBottom);
                }
                if (_c.GridLines)
                {
                    g.MoveTo(gLeft, gTop);
                    g.LineTo(gLeft, gBottom);
                    g.LineTo(gRight, gBottom);
                }
                fill3d = _c.Color ? g.BrushOf(Col(cv.ColorIndex)) : HatchBrush(Col(ColorIndex.Mono), (p + 2) % 6);
                g.Pen = TracePen(cv);
            }
            else
                g.Pen = (_c.Style == PlotStyle.Scatter2D || cv.ScatterPlot) ? SymbolPen(cv) : TracePen(cv);

            int lastPoint = 0;
            bool firstPoint = true;
            int xvFirst = -1, yvFirst = -1, xvLast = -1, yvLast = -1, sx = 0, sy = 0;
            bool redlineCurve = cv.Redline != 0;
            int colorRef = Col(cv.ColorIndex);
            int div3d = Math.Max(1, _c.LineSize3D);
            Span<(int, int)> zbox = stackalloc (int, int)[4];
            for (int tp = pStartAt; tp < pCount; tp++)
            {
                if (_c.PidVsPid && (IsDrop(xData[tp]) || IsDrop(yData[tp])))
                {
                    firstPoint = true;
                    continue;
                }
                if (lastPoint >= 2) break;
                if (!_c.PidVsPid && !_c.EndToEnd)
                {
                    if (xData[tp] > hiClip)
                    {
                        if (_c.Is3D)
                        {
                            if (lastPoint != 0) continue;
                            lastPoint++;
                        }
                        else
                            lastPoint++;
                    }
                }
                if (tp == pCount - 1) lastPoint++;

                double xVal;
                if (hrTime)
                {
                    int gi = iData != null ? iData[tp] : tp + pd.GmtOffset;
                    xVal = TimeMath.Time(pd.Gmt![gi], pd.RefGmt!.Value);
                    if (pd.RefTime.HasValue) xVal -= pd.RefTime.Value;
                }
                else
                    xVal = xData[tp];

                int xv;
                if (_c.LogX)
                    xv = _c.EndToEnd
                        ? IntFloat((((Log(cv.Offset - endToEndRef) + xVal) - xs.RMin) * xs.Factor) + gLeft)
                        : IntFloat(((Log(xVal) - xs.RMin) * xs.Factor) + gLeft);
                else
                    xv = _c.EndToEnd
                        ? IntFloat((((cv.Offset - endToEndRef) + xVal - xs.RMin) * xs.Factor) + gLeft)
                        : IntFloat(((xVal - xs.RMin) * xs.Factor) + gLeft);

                int yv;
                if (IsDrop(yData[tp]))
                {
                    if (_c.DropoutMarker) DropoutMarker(symSize, IsDrop(xData[tp]) ? xvLast : xv, gTop + 3);
                    if (_c.DropoutIgnore) g.MoveTo(xvLast, yvLast);
                    else firstPoint = true;
                    continue;
                }
                if (_c.Style == PlotStyle.Logic)
                    yv = IntFloat(gBottom - (((((int)yData[tp] != 0 ? 1 : 0) + (((nCurves - p) - 1) * 2) + 1) - ys.RMin) * ys.Factor));
                else if (_c.LogY)
                    yv = IntFloat(gBottom - ((Log(yData[tp]) - ys.RMin) * ys.Factor));
                else
                    yv = IntFloat(gBottom - ((yData[tp] - ys.RMin) * ys.Factor));

                if (redlineCurve && xvFirst == -1) { xvFirst = xv; yvFirst = yv; }

                if (_c.Is3D)
                {
                    if (xv > gRight) xv = gRight;
                    if (xv < gLeft) xv = gLeft;
                    if (yv > gBottom) yv = gBottom;
                    if (yv < gTop) yv = gTop;
                    if (firstPoint) firstPoint = false;
                    else
                    {
                        zbox[0] = (xv - (tdxo / div3d), yv + (tdyo / div3d));
                        zbox[1] = (sx - (tdxo / div3d), sy + (tdyo / div3d));
                        zbox[2] = (sx, sy);
                        zbox[3] = (xv, yv);
                        g.Polygon(zbox, fill3d);
                        if (_c.Style == PlotStyle.Area3D)
                        {
                            zbox[2] = (sx - (tdxo / div3d), gBottom + (tdyo / div3d));
                            zbox[3] = (xv - (tdxo / div3d), gBottom + (tdyo / div3d));
                            g.Polygon(zbox, fill3d);
                        }
                    }
                    sx = xv;
                    sy = yv;
                }
                else if (!redlineCurve && (_c.Style == PlotStyle.Scatter2D || cv.ScatterPlot))
                {
                    if (cv.UseSymbols) DrawCurveSymbol(cv, symSize, xv, yv);
                    else if (cv.LineSize == 1) g.SetPixel(xv, yv, colorRef);
                    else
                    {
                        g.MoveTo(xv, yv);
                        g.LineTo(xv + 1, yv);
                    }
                }
                else
                {
                    if (firstPoint)
                    {
                        g.MoveTo(xv, yv);
                        g.SetPixel(xv, yv, colorRef);
                        g.MoveTo(xv, yv);
                        firstPoint = false;
                    }
                    else
                    {
                        if (!(xv == xvLast && yv == yvLast))
                        {
                            if (_c.StairStep) g.LineTo(xv, yvLast);
                            g.LineTo(xv, yv);
                        }
                        if (lastPoint != 0 && _c.StairStep && xData[tp] > hiClip) g.LineTo(gRight, yvLast);
                    }
                    if (cv.UseSymbols && ((tp + symOffset) % symbolInterval) == 0 && !redlineCurve)
                    {
                        var tpen = g.Pen;
                        g.Pen = SymbolPen(cv);
                        DrawCurveSymbol(cv, symSize, xv, yv);
                        g.Pen = tpen;
                        g.MoveTo(xv, yv);
                    }
                    xvLast = xv;
                    yvLast = yv;
                }
            }

            if (_c.Style == PlotStyle.Area3D && !firstPoint)
            {
                zbox[0] = (sx - (tdxo / div3d), sy + (tdyo / div3d));
                zbox[1] = (sx, sy);
                zbox[2] = (sx, gBottom);
                zbox[3] = (sx - (tdxo / div3d), gBottom + (tdyo / div3d));
                g.Polygon(zbox, fill3d);
            }

            if (!_c.Is3D && redlineCurve && xvFirst != -1)
            {
                g.Pen = SymbolPen(cv);
                if (xvFirst == xvLast)
                {
                    if (cv.Redline == 2) RedlineMin(symSize, xvFirst, yvFirst);
                    else if (cv.Redline == 1) RedlineMax(symSize, xvFirst, yvFirst);
                }
                else
                {
                    for (int i = xvFirst + symSize; i < xvLast; i += 5 * symSize)
                    {
                        double xVal = ((double)(i - gLeft) / xs.Factor) + xs.RMin;
                        if (_c.LogX) xVal = Math.Exp(xVal);
                        int xPos = TimeMath.Location(xVal, xData, pCount, 2);
                        if (pCount > xPos + 1)
                        {
                            double Y = !_c.StairStep
                                ? (((yData[xPos + 1] - yData[xPos]) * (xVal - xData[xPos])) / (xData[xPos + 1] - xData[xPos])) + yData[xPos]
                                : yData[xPos];
                            int yv = _c.LogY ? IntFloat(gBottom - ((Log(Y) - ys.RMin) * ys.Factor)) : IntFloat(gBottom - ((Y - ys.RMin) * ys.Factor));
                            if (cv.Redline == 2) RedlineMin(symSize, i, yv);
                            else if (cv.Redline == 1) RedlineMax(symSize, i, yv);
                        }
                    }
                }
            }
        }

        if (_c.MarkersTopmost) { DrawMarkerSet(lineSz); DrawRedlines(lineSz); }
        g.PopAllClips();

        if (_c.Is3D)
        {
            gLeft -= tdxo; gRight -= tdxo; gTop += tdyo; gBottom += tdyo;
            SetGraph(gLeft, gTop, gRight, gBottom);
            g.Pen = framePen;
            g.MoveTo(gLeft, gTop);
            g.LineTo(gLeft, gBottom);
            g.LineTo(gRight, gBottom);
        }
        g.Finish();
    }

    /// <summary>Optimised (pixelised) drawing; set from the application option.</summary>
    public bool OptimizedDraw { get; set; } = true;

    private void SetGraph(int l, int t, int r, int b)
    {
        _d.GraphLeft = l; _d.GraphTop = t; _d.GraphRight = r; _d.GraphBottom = b;
    }

    private static System.Windows.Media.Brush HatchBrush(int colorRef, int style)
    {
        var col = GdiCanvas.ToColor(colorRef);
        var pen = new System.Windows.Media.Pen(new SolidColorBrush(col), 1);
        var geo = new GeometryGroup();
        switch (style)
        {
            case 0: geo.Children.Add(new System.Windows.Media.LineGeometry(new System.Windows.Point(0, 4), new System.Windows.Point(8, 4))); break;
            case 1: geo.Children.Add(new System.Windows.Media.LineGeometry(new System.Windows.Point(4, 0), new System.Windows.Point(4, 8))); break;
            case 2: geo.Children.Add(new System.Windows.Media.LineGeometry(new System.Windows.Point(0, 8), new System.Windows.Point(8, 0))); break;
            case 3: geo.Children.Add(new System.Windows.Media.LineGeometry(new System.Windows.Point(0, 0), new System.Windows.Point(8, 8))); break;
            case 4:
                geo.Children.Add(new System.Windows.Media.LineGeometry(new System.Windows.Point(0, 4), new System.Windows.Point(8, 4)));
                geo.Children.Add(new System.Windows.Media.LineGeometry(new System.Windows.Point(4, 0), new System.Windows.Point(4, 8)));
                break;
            default:
                geo.Children.Add(new System.Windows.Media.LineGeometry(new System.Windows.Point(0, 0), new System.Windows.Point(8, 8)));
                geo.Children.Add(new System.Windows.Media.LineGeometry(new System.Windows.Point(0, 8), new System.Windows.Point(8, 0)));
                break;
        }
        var brush = new DrawingBrush(new GeometryDrawing(null, pen, geo))
        {
            TileMode = TileMode.Tile,
            Viewport = new System.Windows.Rect(0, 0, 8, 8),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new System.Windows.Rect(0, 0, 8, 8),
            ViewboxUnits = BrushMappingMode.Absolute,
        };
        brush.Freeze();
        return brush;
    }

    /// <summary>Draws the items and returns the end position.</summary>
    private int DisplayNomen(int x, int y, int nomItems, int pidNo, string udef, int[] order, int[] len, int? tPoint)
    {
        var g = _g;
        int curx = x;
        int cwidth = g.TextExtent("X").cx;
        foreach (var it in _d.NomenItems(nomItems, pidNo, udef, order, len, tPoint))
        {
            g.TextOut(curx, y, it.Text);
            var ext = g.TextExtent(it.Text + "  ");
            if (len[it.Item] != 0 && ext.cx <= len[it.Item] * cwidth) curx += len[it.Item] * cwidth;
            else curx += ext.cx;
        }
        return curx;
    }

    // ------------------------------------------------------------------ tabular

    private void DrawTabular(int W, int H, int lineSz)
    {
        var d = _d;
        var g = _g;
        if (d.BackgroundImage is ImageSource bg) g.DrawImage(bg, 0, 0, W, H);
        float minHW = Math.Min(H * 1.5f, W);
        int fh = FontCellHeight(_c.LabelPointSize, minHW);
        var lf = _c.LabelFont;
        g.Font = _legendFont = new GdiFont(lf.FaceName, fh, lf.Weight, lf.Italic != 0, false);

        int timeParm = (d.Host.AutoRefreshOn || d.TabDirection <= 0) ? d.MaxTimeParm() : d.MinTimeParm();
        var tc = d.Curves[timeParm];
        if (tc.Points == 0) return;
        int tPoint;
        if (d.Host.AutoRefreshOn)
        {
            tPoint = tc.Points - 1;
            d.TabTime = tc.Time[tPoint];
        }
        else
        {
            tPoint = TimeMath.Location(d.TabTime, tc.Time, tc.Points, d.TabDirection);
            d.TabTime = tc.Time[tPoint];
        }

        if (_c.DefTabTime)
        {
            string tBuf;
            if (_c.ClockTime)
            {
                var refG = PlotDocument.HeaderGmt(tc.Data);
                double dTime = d.TabTime + (tc.Data.RefTime ?? 0.0);
                tBuf = ValueFormat.ClockString(TimeMath.Gmt(dTime, refG, true), "TIME %03d/%02u:%02u:%02u.%03u");
            }
            else
                tBuf = Crt.Sprintf("TIME : %.3f", d.TabTime);
            g.TextColor = Col(ColorIndex.Label);
            var te = g.TextExtent(tBuf);
            g.TextOut(W - (te.cx + 5), H - (te.cy + 5), tBuf);
        }

        for (int p = 0; p < d.NCurves; p++)
        {
            var cv = d.Curves[p];
            if (cv.Points == 0) continue;
            g.TextColor = Col(_c.LegendColor ? cv.ColorIndex : ColorIndex.Label);
            int tp = TimeMath.Location(d.TabTime, cv.Time, cv.Points, 1);
            int xPos, yPos;
            if (cv.TabPosition == 0)
            {
                xPos = 5;
                yPos = 10 + (p * fh);
            }
            else
            {
                xPos = (int)(d.XScale.Factor * cv.TabX);
                yPos = (int)(d.YScales[0].Factor * (WpdDisplayRes - cv.TabY));
                var te = g.TextExtent("0");
                yPos -= te.cy;
                if (yPos < -(te.cy / 2)) yPos = -(te.cy / 2);
                if (xPos > (W - te.cx)) xPos = W - te.cx;
            }
            DisplayNomen(xPos, yPos, cv.TabLegend ? (_c.YNomen | Nomen.CURVAL) : Nomen.CURVAL, p, d.YUdef, _c.YNomOrder, _c.YNomLen, tp);
        }

        int spacing = d.Host.LayoutGridSpacing;
        if (spacing > 0)
        {
            int col = d.Host.SnapToGrid ? 0x0000FF : 0xFFFF00;
            for (int ix = spacing; ix < WpdDisplayRes; ix += spacing)
                for (int iy = spacing; iy < WpdDisplayRes; iy += spacing)
                    g.SetPixel((int)(d.XScale.Factor * ix), (int)(d.YScales[0].Factor * (WpdDisplayRes - iy)), col);
        }
    }

    /// <summary>
    /// Test title shown by Test Info: the <c>Title</c> of the file of the plot's first curve (for curves
    /// without a source file, such as expressions, the first file of the curve's group). Empty when blank.
    /// </summary>
    private static string TestTitle(PlotDocument d)
    {
        if (d.Curves.Count == 0) return "";
        var cv = d.Curves[0];
        var file = cv.Data.Source;
        if (file == null)
        {
            var groups = d.Host.Groups;
            int gi = cv.GroupNo >= 0 && cv.GroupNo < groups.Count ? cv.GroupNo : 0;
            if (groups.Count > 0 && groups[gi].StartPos >= 0 && groups[gi].StartPos < d.Host.Files.Count) file = d.Host.Files[groups[gi].StartPos];
            else if (d.Host.Files.Count > 0) file = d.Host.Files[0];
        }
        string t = (file?.Info.Title ?? "").Trim();
        int nl = t.IndexOfAny(new[] { '\r', '\n' });
        return nl >= 0 ? t[..nl].TrimEnd() : t;
    }

    // ------------------------------------------------------------------ markers

    /// <summary>
    /// Event markers (FR-MRK-002): a full-height vertical line at each resolved timestamp with the text
    /// beside it at the top or bottom of the plot area. Markers outside the visible X range are not drawn,
    /// so in real time they scroll with the data and leave the window with their timestamp. Not drawn on
    /// tabular displays, parameter-versus-parameter plots (no time axis) or end-to-end plots (the X axis
    /// is accumulated duration, not time).
    /// </summary>
    private void DrawMarkerSet(int lineSz)
    {
        var d = _d;
        var g = _g;
        if (_c.Style == PlotStyle.Tabular || _c.PidVsPid || _c.EndToEnd) return;
        if (d.Markers.Count == 0 && !_c.ShowFileEvents) return;
        var groups = d.Curves.Select(c => c.GroupNo).Distinct().ToList();
        if (groups.Count == 0) groups.Add(0);
        var list = EventMarkers.Resolve(d.Markers, _c.ShowFileEvents, d.Host.Files, d.Host.Groups, groups, d.TReference);
        if (list.Count == 0) return;
        var xs = d.XScale;
        int gl = d.GraphLeft, gr = d.GraphRight, gt = d.GraphTop, gb = d.GraphBottom;
        int color = Col(ColorIndex.Marker);
        g.Pen = new GdiPen(GdiPen.Solid, lineSz, color);
        g.TextColor = color;
        foreach (var m in list)
        {
            double xv = _c.LogX ? Log(m.Time) : m.Time;
            double xp = ((xv - xs.RMin) * xs.Factor) + gl;
            if (double.IsNaN(xp) || xp < gl || xp > gr) continue;
            int x = IntFloat(xp);
            g.MoveTo(x, gt + 1);
            g.LineTo(x, gb - 1);
            var te = g.TextExtent(m.Text);
            int tx = x + _tickW;
            if (tx + te.cx >= gr && x - _tickW - te.cx > gl) tx = x - _tickW - te.cx;
            int ty = m.Bottom ? gb - te.cy - 2 : gt + 2;
            g.TextOut(tx, ty, m.Text);
        }
    }

    /// <summary>
    /// Redline markers (FR-MRK-010): a full-width horizontal line at each redline value with the text above
    /// it (below when there is no room) at the left or right end. The value is measured on the selected value
    /// axis (the first axis unless another one is selected in multiple-Y mode). Redlines whose value is
    /// outside the visible value range are not drawn, so they move with the scale when it is panned or
    /// zoomed and disappear when the value leaves the view. Not drawn on tabular displays, logic plots and
    /// 3D plots (no single value plane).
    /// </summary>
    private void DrawRedlines(int lineSz)
    {
        var d = _d;
        var g = _g;
        if (d.Redlines.Count == 0 || d.YScales.Count == 0) return;
        if (_c.Style == PlotStyle.Tabular || _c.Style == PlotStyle.Logic || _c.Is3D) return;
        int axis = d.SelectedAxis >= 0 && d.SelectedAxis < d.YScales.Count ? d.SelectedAxis : 0;
        var ys = d.YScales[axis];
        int gl = d.GraphLeft, gr = d.GraphRight, gt = d.GraphTop, gb = d.GraphBottom;
        int color = Col(ColorIndex.Redline);
        g.Pen = new GdiPen(GdiPen.Solid, lineSz, color);
        g.TextColor = color;
        foreach (var r in d.Redlines)
        {
            if (_c.LogY && r.Value <= 0) continue;
            double yv = _c.LogY ? Math.Log(r.Value) : r.Value;
            double yp = gb - ((yv - ys.RMin) * ys.Factor);
            if (double.IsNaN(yp) || double.IsInfinity(yp) || yp < gt || yp > gb) continue;
            int y = IntFloat(yp);
            g.MoveTo(gl + 1, y);
            g.LineTo(gr, y);
            var te = g.TextExtent(r.Text);
            int tx = r.Right ? gr - _tickW - te.cx : gl + _tickW;
            if (tx < gl + 1) tx = gl + 1;
            int ty = y - te.cy - 1;
            if (ty < gt + 1) ty = y + lineSz + 1;
            g.TextOut(tx, ty, r.Text);
        }
    }

    // ------------------------------------------------------------------ symbols

    private void DrawCurveSymbol(Curve c, int size, int x, int y)
    {
        var g = _g;
        if (x < -32000 || x > 32000 || y < -32000 || y > 32000) return;
        int s = c.SymbolId;
        switch (s)
        {
            case 0: g.MoveTo(x - size, y - size); g.LineTo(x + size + 1, y + size + 1); g.MoveTo(x + size, y - size); g.LineTo(x - size - 1, y + size + 1); break;
            case 1: g.MoveTo(x, y - size - 1); g.LineTo(x - size, y + size - 1); g.LineTo(x + size, y + size - 1); g.LineTo(x, y - size - 1); break;
            case 2: Rect(x - size, y - size, x + size, y + size); break;
            case 3: g.MoveTo(x, y + size); g.LineTo(x - size, y - size); g.LineTo(x + size, y - size); g.LineTo(x, y + size); break;
            case 4: g.MoveTo(x - size, y); g.LineTo(x + size, y - size); g.LineTo(x + size, y + size); g.LineTo(x - size, y); break;
            case 5: g.MoveTo(x + size, y); g.LineTo(x - size, y - size); g.LineTo(x - size, y + size); g.LineTo(x + size, y); break;
            case 6: g.MoveTo(x, y - size); g.LineTo(x - size, y); g.LineTo(x, y + size); g.LineTo(x + size, y); g.LineTo(x, y - size); break;
            case 7: Rect(x - (size * 2) + (size / 2), y - size, x + size + (size / 2), y + size); break;
            case 8: Rect(x - (size / 2), y - size, x + (size / 2), y + size); break;
            case 9: g.MoveTo(x - size, y - size); g.LineTo(x - size, y + size); g.LineTo(x + size, y - size); g.LineTo(x - size, y - size); break;
            case 10: g.MoveTo(x + size, y - size); g.LineTo(x + size, y + size); g.LineTo(x - size, y + size); g.LineTo(x + size, y - size); break;
            case 11: g.MoveTo(x + size, y - size); g.LineTo(x + size, y + size); g.LineTo(x - size, y - size); g.LineTo(x + size, y - size); break;
            case 12: g.MoveTo(x - size, y - size); g.LineTo(x - size, y + size); g.LineTo(x + size, y + size); g.LineTo(x - size, y - size); break;
            default:
                {
                    int save = g.TextColor;
                    g.TextColor = Col(c.ColorIndex);
                    string buf;
                    if (s >= 75) buf = new string((char)(s - 10), 2);
                    else if (s >= 65) buf = ((char)(s - 17)).ToString();
                    else if (s >= 39) buf = ((char)(s + 58)).ToString();
                    else buf = ((char)(s + 52)).ToString();
                    var te = g.TextExtent(buf);
                    g.TextOut(x - size, y - (te.cy / 2), buf);
                    g.TextColor = save;
                    break;
                }
        }
        g.MoveTo(x, y);
    }

    private void Rect(int tx, int ty, int bx, int by)
    {
        _g.MoveTo(tx, ty);
        _g.LineTo(bx, ty);
        _g.LineTo(bx, by);
        _g.LineTo(tx, by);
        _g.LineTo(tx, ty);
    }

    private void DropoutMarker(int size, int x, int y)
    {
        int h = (size / 2) >= 1 ? size / 2 : 1;
        _g.MoveTo(x, y + size);
        _g.LineTo(x - h, y);
        _g.LineTo(x + h, y);
        _g.LineTo(x, y + size);
    }

    private void RedlineMax(int size, int x, int y)
    {
        y -= (size / 2) > 1 ? size / 2 : 2;
        _g.MoveTo(x, y);
        _g.LineTo(x, y - (3 * size));
        _g.MoveTo(x - size, y - size);
        _g.LineTo(x, y);
        _g.LineTo(x + size + 1, y - size - 1);
    }

    private void RedlineMin(int size, int x, int y)
    {
        y += (size / 2) > 1 ? size / 2 : 2;
        _g.MoveTo(x, y);
        _g.LineTo(x, y + (3 * size));
        _g.MoveTo(x - size, y + size);
        _g.LineTo(x, y);
        _g.LineTo(x + size + 1, y + size + 1);
    }

    // ------------------------------------------------------------------ reduction

    private int QuickReduce(int reduceMode, uint reduceFreq, ParameterData p)
    {
        var xs = _d.XScale;
        int firstP = TimeMath.Location((float)(_c.LogX ? Math.Exp(xs.RMin) : xs.RMin), p.Time, p.Points, 0);
        if (firstP > 0) firstP--;
        int lastP = TimeMath.Location((float)(_c.LogX ? Math.Exp(xs.RMax) : xs.RMax), p.Time, p.Points, 0);
        if (lastP < p.Points - 1) lastP++;
        int nPoints = 0;
        uint freq = reduceFreq < (uint)_compactBufSz ? reduceFreq : (uint)_compactBufSz;
        if (freq == 0) freq = 1;
        int pInterval = (int)((uint)(lastP - firstP) / freq);
        if (pInterval <= 0) pInterval = 1;
        float dSum = 0f;
        int nSum = 0;
        var v = p.Values;
        for (int i = firstP; i <= lastP; i++)
        {
            if (reduceMode == DataRateMode.Averaged)
            {
                dSum += v[i];
                nSum++;
            }
            if (((i - firstP) % pInterval) == 0 || i == lastP)
            {
                _packedTime[nPoints] = p.Time[i];
                if (reduceMode == DataRateMode.Averaged)
                {
                    _packedData[nPoints] = dSum / nSum;
                    dSum = 0f;
                    nSum = 0;
                }
                else
                    _packedData[nPoints] = v[i];
                nPoints++;
                if (nPoints == _compactBufSz) break;
            }
        }
        return nPoints;
    }

    private int Pixelize(int startOffset, Curve c)
    {
        var p = c.Data;
        int pCount = p.Points - startOffset;
        var time = p.Time;
        var data = p.Values;
        int o = startOffset;
        double xFactor = _d.XScale.Factor;
        double xOrigin = _d.XScale.RMin + (_c.EndToEnd ? -c.Offset + time[o] : 0.0);
        int maxPackSz = _compactBufSz - 4;
        int packedPos = 0, startPos = 0;
        int lastChange = -9999;
        int baseIndex = startOffset + p.GmtOffset;
        bool logX = _c.LogX;
        for (int endPos = 1; endPos < pCount; endPos++)
        {
            double tx = logX ? Log(time[o + endPos]) : time[o + endPos];
            int xv = (int)((tx - xOrigin) * xFactor);
            if (xv != lastChange || endPos == pCount - 1)
            {
                lastChange = xv;
                int dropoutPos = endPos;
                int entryPos, minPos, maxPos, exitPos;
                switch (endPos - startPos)
                {
                    case 1:
                        if (endPos == pCount - 1)
                        {
                            entryPos = minPos = startPos;
                            exitPos = maxPos = endPos;
                        }
                        else
                            entryPos = exitPos = minPos = maxPos = startPos;
                        break;
                    case 2:
                        entryPos = minPos = startPos;
                        exitPos = maxPos = minPos + 1;
                        if (endPos == pCount - 1) exitPos = endPos;
                        break;
                    default:
                        if (IsDrop(data[o + startPos]))
                        {
                            Pack(ref packedPos, time[o + startPos], data[o + startPos], baseIndex + startPos);
                            startPos++;
                            while (IsDrop(data[o + startPos]) && startPos < endPos - 1) startPos++;
                        }
                        entryPos = minPos = maxPos = startPos;
                        exitPos = endPos - 1;
                        for (int n = startPos + 1; n < endPos; n++)
                        {
                            float dv = data[o + n];
                            if (IsDrop(dv))
                            {
                                if (dropoutPos == endPos) dropoutPos = n;
                            }
                            else
                            {
                                minPos = dv < data[o + minPos] ? n : minPos;
                                maxPos = dv > data[o + maxPos] ? n : maxPos;
                            }
                        }
                        break;
                }
                if (minPos > maxPos) (minPos, maxPos) = (maxPos, minPos);
                if (entryPos != minPos) Pack(ref packedPos, time[o + entryPos], data[o + entryPos], baseIndex + entryPos);
                if (dropoutPos < minPos)
                {
                    Pack(ref packedPos, time[o + dropoutPos], data[o + dropoutPos], baseIndex + dropoutPos);
                    dropoutPos = endPos;
                }
                Pack(ref packedPos, time[o + minPos], data[o + minPos], baseIndex + minPos);
                if (maxPos != minPos) Pack(ref packedPos, time[o + maxPos], data[o + maxPos], baseIndex + maxPos);
                if (dropoutPos < exitPos) Pack(ref packedPos, time[o + dropoutPos], data[o + dropoutPos], baseIndex + dropoutPos);
                if (exitPos != maxPos) Pack(ref packedPos, time[o + exitPos], data[o + exitPos], baseIndex + exitPos);
                startPos = endPos;
                if (endPos == pCount - 1) Pack(ref packedPos, time[o + endPos], data[o + endPos], baseIndex + endPos);
                if (packedPos > maxPackSz)
                {
                    if (_packedTime[packedPos - 1] >= _d.XScale.RMax) break;
                    // Buffer full before the right edge (dropouts, non-monotonic time): grow it rather than
                    // returning 0, which made the caller draw every raw sample.
                    GrowPackBuffers();
                    maxPackSz = _packedTime.Length - 4;
                }
            }
        }
        return packedPos;
    }

    private void GrowPackBuffers()
    {
        int n = _packedTime.Length * 2;
        Array.Resize(ref _packedTime, n);
        Array.Resize(ref _packedData, n);
        Array.Resize(ref _packedIndx, n);
    }

    private void Pack(ref int pos, float t, float v, int idx)
    {
        if (pos >= _packedTime.Length) return;
        _packedTime[pos] = t;
        _packedData[pos] = v;
        _packedIndx[pos] = idx;
        pos++;
    }
}
