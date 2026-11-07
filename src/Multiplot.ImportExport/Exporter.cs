// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

using System.Text;
using Multiplot.Core;
using Multiplot.Core.Model;
using Multiplot.Plugins;
using Multiplot.Sun;

namespace Multiplot.ImportExport;

/// <summary>Application options used by the exporters.</summary>
public sealed class ExportOptions
{
    /// <summary>INCLUDEUTC.</summary>
    public bool ShowUtc;
    /// <summary>SHAREDTIME.</summary>
    public bool SharedTabTime;
    /// <summary>Time precision (−7..12).</summary>
    public int TimePrecision = -1;
    /// <summary>Data precision.</summary>
    public int DataPrecision = -1;
    /// <summary>Export folder.</summary>
    public string ExportDir = "";
    /// <summary>Temp folder.</summary>
    public string TempDir = "";
}

/// <summary>
/// Data export of a plot (<c>ToSUN</c>, FR-XPT-010/011/020) and the
/// statistics file (FR-XPT-041).
/// </summary>
public static class Exporter
{
    private const double TimeTolerance = TimeMath.TimeTolerance;

    /// <summary>Resolves the output name of <see cref="ToAscii"/>.</summary>
    public static string AsciiFileName(PlotDocument plot, char delimiter, string? fileName, ExportOptions o)
    {
        if (string.IsNullOrEmpty(fileName))
            return Crt.Sprintf("%s\\Plot%04d", o.TempDir, plot.PlotNo) + (delimiter == ',' ? ".csv" : ".tab");
        if (!fileName.Contains('\\'))
        {
            if (Directory.Exists(o.ExportDir)) return o.ExportDir + "\\" + fileName;
            if (Directory.Exists(o.TempDir)) return o.TempDir + "\\" + fileName;
            return fileName;
        }
        return fileName.Length > 255 ? fileName[..255] : fileName;
    }

    /// <summary>ASCII export text (caller handles the end-to-end confirmation).</summary>
    public static string FormatAscii(PlotDocument plot, char delimiter, ExportOptions o)
    {
        var sb = new StringBuilder();
        void W(string s) => sb.Append(s);
        var cfg = plot.Config;
        var pid = plot.Curves;
        int n = pid.Count;
        if (n == 0) return "";
        var offset = new int[n];
        long maxpnts = 0;
        bool multiTime;
        bool utcCol = o.ShowUtc || cfg.ClockTime;

        if (cfg.PidVsPid || cfg.EndToEnd)
        {
            multiTime = true;
            for (int i = 0; i < n; i++) maxpnts = i == 0 ? pid[i].Points : Math.Max(pid[i].Points, maxpnts);
        }
        else
        {
            var start = new int[n];
            var stop = new int[n];
            for (int i = 0; i < n; i++)
            {
                offset[i] = start[i] = TimeMath.Location(plot.XScale.RMin, pid[i].Time, pid[i].Points, -1);
                stop[i] = TimeMath.Location(plot.XScale.RMax, pid[i].Time, pid[i].Points, 1);
                maxpnts = Math.Max(pid[i].Points, maxpnts);
            }
            bool pntVariance = false, timeVariance = false;
            uint c1 = unchecked((uint)(stop[0] - start[0]));
            for (int i = 1; i < n; i++) if (unchecked((uint)(stop[i] - start[i])) != c1) pntVariance = true;
            for (int i = 1; i < n; i++) if (TimeAt(pid[i], start[i]) != TimeAt(pid[0], start[0])) timeVariance = true;
            multiTime = pntVariance || timeVariance;
        }
        if (!o.SharedTabTime) multiTime = true;

        var files = plot.Host.Files;
        var groups = plot.Host.Groups;

        if ((cfg.YNomen & Nomen.DBNAME) != 0)
        {
            for (int i = 0; i < n; i++)
            {
                int j;
                for (j = files.Count - 1; j > 0; j--) if (ReferenceEquals(pid[i].Data.Source, files[j])) break;
                int g;
                for (g = groups.Count - 1; g > 0; g--) if (j >= groups[g].StartPos) break;
                W(groups.Count > 0 ? groups[Math.Max(g, 0)].GroupName : "");
                if (!multiTime) { W("\n"); break; }
                if (cfg.PidVsPid) W(delimiter.ToString());
                if (utcCol) W(delimiter.ToString());
                W(delimiter.ToString());
                W((i == n - 1 ? '\n' : delimiter).ToString());
            }
        }

        if ((cfg.YNomen & Nomen.FILENAME) != 0)
        {
            for (int i = 0; i < n; i++)
            {
                W(pid[i].Data.Source?.Info.Path ?? "");
                if (utcCol && (i == 0 || multiTime)) W(delimiter.ToString());
                if (i == 0 || multiTime) W(delimiter.ToString());
                if (cfg.PidVsPid) W(delimiter.ToString());
                W((i == n - 1 ? '\n' : delimiter).ToString());
            }
        }

        if ((cfg.YNomen & Nomen.NAME) != 0)
        {
            for (int i = 0; i < n; i++)
            {
                if (i == 0 || multiTime)
                {
                    if (utcCol) W(pid[i].Data.TimeName + delimiter);
                    W(pid[i].Data.TimeName + delimiter);
                }
                if (cfg.PidVsPid && (i == 0 || multiTime)) W(plot.XAxisNomen[0] + delimiter);
                string name = pid[i].Name;
                W(name.Length > 0 && name[0] == '$' ? name[1..] : name);
                W((i == n - 1 ? '\n' : delimiter).ToString());
            }
        }

        if ((cfg.YNomen & Nomen.UNITS) != 0)
        {
            for (int i = 0; i < n; i++)
            {
                if (i == 0 || multiTime)
                {
                    string b = pid[i].Data.TimeUnits.TrimStart();
                    if (utcCol) W(b + delimiter);
                    W(b + delimiter);
                }
                if (cfg.PidVsPid && (i == 0 || multiTime)) W(plot.XAxisNomen[1] + delimiter);
                W(pid[i].Data.Units.TrimStart());
                W((i == n - 1 ? '\n' : delimiter).ToString());
            }
        }

        if ((cfg.YNomen & Nomen.DESC) != 0)
        {
            for (int i = 0; i < n; i++)
            {
                if (i == 0 || multiTime)
                {
                    if (utcCol) W(pid[i].Data.TimeDescription + delimiter);
                    W(pid[i].Data.TimeDescription + delimiter);
                }
                if (cfg.PidVsPid && (i == 0 || multiTime)) W(plot.XAxisNomen[2] + delimiter);
                W(ValueFormat.StripGroupSpec(pid[i].Data.Description));
                W((i == n - 1 ? '\n' : delimiter).ToString());
            }
        }

        bool tabular = cfg.Style == PlotStyle.Tabular;
        double xmax = tabular ? plot.XSetMax : plot.XScale.RMax;
        double xmin = tabular ? plot.XSetMin : plot.XScale.RMin;
        bool inbounds = true;
        for (long p = 0; p < maxpnts; p++)
        {
            if (!cfg.PidVsPid && !cfg.EndToEnd)
            {
                bool lobounds = false;
                inbounds = false;
                for (int j = 0; j < n; j++)
                {
                    if (pid[j].Points > offset[j] + p)
                    {
                        double t = pid[j].Time[p + offset[j]];
                        double tPoint = cfg.LogX ? (t > 0 ? Math.Log(t) : -99999.99) : t;
                        if (tPoint - TimeTolerance <= xmax) inbounds = true;
                        if (tPoint < xmin) lobounds = true;
                    }
                }
                if (!(inbounds || lobounds)) break;
            }
            if (!inbounds) continue;
            for (int j = 0; j < n; j++)
            {
                var c = pid[j];
                var d = c.Data;
                long k = p + offset[j];
                if (k >= c.Points)
                {
                    if (j == 0 || multiTime) W(" " + delimiter);
                    if (utcCol) W(" " + delimiter);
                    if (cfg.PidVsPid && (j == 0 || multiTime)) W(" " + delimiter);
                }
                else if (!cfg.PidVsPid && !cfg.EndToEnd && c.Time[k] - TimeTolerance > xmax)
                {
                    if (j == 0 || multiTime) W(" " + delimiter);
                    if (utcCol) W(" " + delimiter);
                    if (cfg.PidVsPid && (j == 0 || multiTime)) W(" " + delimiter);
                }
                else
                {
                    double refTime = d.RefTime ?? 0.0;
                    if (j == 0 || multiTime)
                    {
                        if (cfg.PidVsPid)
                        {
                            int iPos = (int)(d.UtcOffset + k);
                            if (o.ShowUtc)
                            {
                                if (d.HasValidUtc) W(ValueFormat.ClockString(d.Utc![iPos], "%03d/%02u:%02u:%02u.%03u.%03u") + delimiter);
                                else W("n/a" + delimiter);
                            }
                            if (d.HasValidUtc)
                            {
                                double tv = TimeMath.Time(d.Utc![iPos], d.RefUtc!.Value);
                                W(ValueFormat.FormatValue(tv - refTime, o.TimePrecision) + delimiter);
                            }
                            else if (d.UtcOffsetValid && c.OriginalTime != null && IsCommonTime(d))
                                W(ValueFormat.FormatValue(c.OriginalTime.Time[k], o.TimePrecision) + delimiter);
                            else
                                W("n/a" + delimiter);
                        }
                        else
                        {
                            bool dbl = false;
                            double timeValue = 0;
                            if (utcCol)
                            {
                                TimeTag tt;
                                if (!d.HasValidUtc)
                                {
                                    timeValue = c.Time[k] + refTime;
                                    timeValue -= d.TimeShift;
                                    if (d.RefUtc.HasValue) tt = TimeMath.Utc(timeValue, d.RefUtc.Value, false);
                                    else tt = TimeMath.Utc(timeValue, PlotDocument.HeaderUtc(d), false);
                                    W(ValueFormat.ClockString(tt, plot.UtcFormat) + delimiter);
                                }
                                else
                                {
                                    dbl = true;
                                    var g = d.UtcAt((int)k);
                                    timeValue = TimeMath.Time(g, d.RefUtc!.Value);
                                    W(ValueFormat.ClockString(g, "%03d/%02u:%02u:%02u.%03u.%03u") + delimiter);
                                }
                            }
                            W((dbl ? ValueFormat.FormatValue(timeValue - refTime, o.TimePrecision) : ValueFormat.FormatValue(c.Time[k], o.TimePrecision)) + delimiter);
                        }
                    }
                    if (cfg.PidVsPid && (j == 0 || multiTime)) W(ValueFormat.FormatValue(c.Time[k], o.TimePrecision) + delimiter);
                    W(ValueFormat.FormatValue(c.Values[k], tabular ? c.TabFormat : o.DataPrecision, (cfg.YNomen & Nomen.EFORMAT) != 0));
                }
                W((j == n - 1 ? '\n' : delimiter).ToString());
            }
        }
        return sb.ToString();
    }

    private static float TimeAt(Curve c, int i) => i >= 0 && i < c.Time.Length ? c.Time[i] : 0f;

    private static bool IsCommonTime(ParameterData d)
    {
        uint sig = d.Source?.Info.FileSig ?? 0;
        return sig is SunSignatures.Compressed or SunSignatures.Uncompressed or SunSignatures.Circular or SunSignatures.CommonTime;
    }

    /// <summary>Writes the ASCII export; returns the file name or null when it cannot be created.</summary>
    public static string? ToAscii(PlotDocument plot, char delimiter, string? fileName, ExportOptions o)
    {
        string outFile = AsciiFileName(plot, delimiter, fileName, o);
        try
        {
            string text = FormatAscii(plot, delimiter, o).Replace("\n", "\r\n");
            File.WriteAllBytes(outFile, AnsiText.Encode(text));
            return outFile;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Writes the plot curves as SUN_MULTITIME (FR-XPT-020).</summary>
    public static void ToSun(PlotDocument plot, string fileName)
    {
        var pid = plot.Curves;
        if (pid.Count == 0) return;
        var cmt = new StringBuilder("[history]\nData created " + plot.PlotDate + "\n*note that parameters may not be referenced to same times or file header reference\n");
        for (int i = 0; i < pid.Count; i++)
        {
            var d = pid[i].Data;
            TimeTag r = d.RefUtc ?? d.Source?.Info.RefUtc ?? new TimeTag();
            cmt.Append(Crt.Sprintf("pos(%d) %s", i, d.Name)).Append(" source=").Append(d.Source?.Info.Path ?? "").Append(" Tref=").Append(ValueFormat.ClockString(r)).Append('\n');
        }
        bool utcTags;
        if (pid.Count == 1) utcTags = pid[0].Data.HasValidUtc;
        else if (pid[0].Data.HasValidUtc)
        {
            var baseRef = pid[0].Data.RefUtc!.Value;
            utcTags = true;
            foreach (var c in pid)
            {
                if (!c.Data.RefUtc.HasValue || Math.Abs(TimeMath.Time(c.Data.RefUtc.Value, baseRef)) > TimeTolerance) { utcTags = false; break; }
            }
        }
        else utcTags = false;
        string comment = cmt.ToString();
        if (utcTags)
        {
            var r = pid[0].Data.RefUtc!.Value;
            comment = Crt.Sprintf("[events]\nFILE_REF=%03d/%d.%.03d\n", (int)r.JulianDay, r.MilliSeconds, (int)r.MicroSeconds) + comment;
        }
        using var w = SunWriter.Create(fileName, SunCreateMode.Overwrite, (uint)(AnsiText.Encode(comment).Length + 256), (uint)pid.Count, 0, SunSignatures.MultiTime);
        w.SetTitle(plot.XTitle);
        w.SetComment("Plot data from Multiplot display");
        var info = pid[0].Data.Source?.Info;
        if (info != null)
        {
            w.SetRefTime(info.RefTime[0], info.RefTime[1], info.RefTime[2], info.RefTime[3], info.RefTime[4], info.RefTime[5]);
            w.SetCutoff(info.Cutoff);
        }
        w.SetCommentText(comment);
        for (int i = 0; i < pid.Count; i++)
        {
            var d = pid[i].Data;
            int np = d.Points;
            string name1 = d.Name.Replace("$", "");
            string name2 = Crt.Sprintf("Curve%03d", i + 1);
            var values = new float[np];
            Array.Copy(d.Values, values, np);
            if (utcTags)
            {
                var tags = new TimeTag[np];
                Array.Copy(d.Utc!, d.UtcOffset, tags, 0, np);
                w.AddParm(name1, name2, d.Units, d.Description, unchecked((int)d.SequenceNumber), "f4", values, "t8", tags, (uint)np, SunWriteOptions.LzhCompress | SunWriteOptions.OrientNative, null);
            }
            else
            {
                var t = new float[np];
                Array.Copy(d.Time, t, np);
                w.AddParm(name1, name2, d.Units, d.Description, unchecked((int)d.SequenceNumber), "f4", values, "f4", t, (uint)np, SunWriteOptions.LzhCompress | SunWriteOptions.OrientNative, null);
            }
        }
        w.Close();
    }

    /// <summary>Writes data statistics (FR-XPT-041); returns false when the file cannot be written.</summary>
    public static bool WriteDataStats(PlotDocument plot, string fileName, int dataPrecision)
    {
        try
        {
            AnsiText.WriteAllText(fileName, plot.DataStatsText(dataPrecision).Replace("\r\n", "\n").Replace("\n", "\r\n"));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
