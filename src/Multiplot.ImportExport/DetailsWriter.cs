using System.Text;
using Multiplot.Core;
using Multiplot.Core.Model;
using Multiplot.Plugins;

namespace Multiplot.ImportExport;

/// <summary>
/// Text reports of <c>SAVEPARMLIST</c>, <c>SAVEPARMDETAILS</c>, <c>SAVEFILEDETAILS</c> and the details dialogs
/// (FR-SCR-157..159, FR-XPT-021).
/// </summary>
public static class DetailsWriter
{
    private static string Nl(StringBuilder sb) => sb.ToString().Replace("\n", "\r\n");

    private static string StrValid(string? s) => string.IsNullOrEmpty(s) ? " " : s;

    /// <summary>Parameter list text; <paramref name="fileNo"/> &lt; 0 = all files.</summary>
    public static string ParmList(IReadOnlyList<IDataFile> files, int fileNo)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < files.Count; i++)
        {
            if (i != fileNo && fileNo >= 0) continue;
            var f = files[i];
            sb.Append(Crt.Sprintf("%s\n%d\n", f.Info.Path, (int)f.Info.Items));
            foreach (var p in f.Parameters.Take((int)f.Info.Items))
                sb.Append(Crt.Sprintf("%s,%s,%s,%s,%u\n", p.Names[0] ?? "(null)", p.Names[1] ?? "(null)", p.Units, p.Descriptions[0] ?? "(null)", p.SequenceNumber));
        }
        return Nl(sb);
    }

    /// <summary>Writes <see cref="ParmList"/> 0 / −1 open failure / −2 invalid file number.</summary>
    public static int SaveParmList(IReadOnlyList<IDataFile> files, int fileNo, string fileName)
    {
        if (fileNo > files.Count) return -2;
        return Write(fileName, ParmList(files, fileNo));
    }

    /// <summary>Parameter details text; <paramref name="parmNo"/> &lt; 0 = curve list. Null = invalid curve.</summary>
    public static string? ParmDetails(PlotDocument plot, int parmNo)
    {
        var sb = new StringBuilder();
        if (parmNo < 0)
        {
            sb.Append(Crt.Sprintf("Plot #%d: %d parameter(s)\n", plot.PlotNo, plot.NCurves));
            for (int i = 0; i < plot.NCurves; i++)
            {
                string name = plot.Curves[i].Data.Source?.Info.Path ?? "";
                string p = name.Contains('\\') ? name[(name.LastIndexOf('\\') + 1)..] : name;
                sb.Append(Crt.Sprintf("Curve %d:  <%s>%s\n", i + 1, p, plot.Curves[i].Name));
            }
        }
        else if (parmNo < plot.NCurves)
        {
            var d = plot.Curves[parmNo].Data;
            var src = d.Source;
            sb.Append(Crt.Sprintf("Parameter name: %s\n", d.Name));
            sb.Append(Crt.Sprintf("Parent file: %s\n", src?.Info.Path ?? ""));
            sb.Append(Crt.Sprintf("Parameter units: %s\n", d.Units));
            string descr = d.Description;
            int iScale = descr.IndexOf('\\');
            sb.Append(Crt.Sprintf("Parameter description: %s\n", iScale != -1 ? descr[..iScale] : descr));
            sb.Append(Crt.Sprintf("User defined scale ID: %s\n", iScale != -1 ? descr[(iScale + 1)..] : "(undefined)"));
            if (d.RefGmt.HasValue)
                sb.Append(Crt.Sprintf("Reference GMT: %03d/%u.%d\n", (int)d.RefGmt.Value.JulianDay, d.RefGmt.Value.MilliSeconds, (int)d.RefGmt.Value.MicroSeconds));
            if (d.RefTime.HasValue)
                sb.Append(Crt.Sprintf("Reference TIME: %f (relative offset)\n", d.RefTime.Value));
            if (!d.RefGmt.HasValue && !d.RefTime.HasValue && src != null)
            {
                var r = src.Info.RefTime;
                sb.Append(Crt.Sprintf("Reference time: File reference (%02ld %03ld %02ld:%02ld:%02ld.%03d)\n", r[0], r[1], r[2], r[3], r[4], r[5]));
            }
            sb.Append(Crt.Sprintf("Retrieved data points: %ld\n", d.Points));
            float rate = src is ITimeIndexedFile ti ? ti.FileRate : d.Rate;
            sb.Append(Crt.Sprintf("Rate (1/DeltaT): %g\n", (double)rate));
            string typeId = src != null && d.MapPosition >= 0 && d.MapPosition < src.Parameters.Count ? src.Parameters[d.MapPosition].TypeId : DataTypes.ToId(d.Type);
            sb.Append(Crt.Sprintf("Data type ID: %s\n", typeId));
            sb.Append(Crt.Sprintf("Sequence Number: %ld\n", unchecked((int)d.SequenceNumber)));
            sb.Append(Crt.Sprintf("Min Value/point offset: %f/%ld\n", d.MinValue, d.MinIndex));
            sb.Append(Crt.Sprintf("Max Value/point offset: %f/%ld\n", d.MaxValue, d.MaxIndex));
            sb.Append(Crt.Sprintf("Time shift applied: %.3f\n", (double)d.TimeShift));
        }
        else return null;
        return Nl(sb);
    }

    /// <summary>Writes <see cref="ParmDetails"/>; status 0 / −1 open failure / −2 no plot / −3 invalid curve.</summary>
    public static int SaveParmDetails(PlotDocument? plot, int parmNo, string fileName)
    {
        if (plot == null) return -2;
        string? t = ParmDetails(plot, parmNo);
        int s = Write(fileName, t ?? "");
        return s != 0 ? s : t == null ? -3 : 0;
    }

    /// <summary>File details text; <paramref name="fileNo"/> &lt; 0 = file list. Null = invalid file.</summary>
    public static string? FileDetails(IReadOnlyList<IDataFile> files, IReadOnlyList<FileGroup> groups, int fileNo, Func<uint, string> description)
    {
        var sb = new StringBuilder();
        if (fileNo < 0)
        {
            sb.Append(Crt.Sprintf("Total: %d files in %d groups\n", files.Count, groups.Count));
            for (int i = 0; i < groups.Count; i++)
            {
                for (int j = 0; j < groups[i].NFiles; j++)
                {
                    string n = files[groups[i].StartPos + j].Info.Path;
                    string p = n.Contains('\\') ? n[(n.LastIndexOf('\\') + 1)..] : n;
                    sb.Append(Crt.Sprintf("%s (group:%d file:%d) : %s\n", groups[i].GroupName, i + 1, j + 1, p));
                }
            }
        }
        else if (fileNo < files.Count)
        {
            var f = files[fileNo];
            var info = f.Info;
            var r = info.RefTime;
            sb.Append(Crt.Sprintf("File: %s\n", info.Path));
            sb.Append(Crt.Sprintf("File signature: %08X  ", info.FileSig));
            sb.Append(Crt.Sprintf("File type: %s\n", description(info.FileSig)));
            sb.Append(Crt.Sprintf("Header Reference time: %02ld %03ld %02ld:%02ld:%02ld.%03d\n", r[0], r[1], r[2], r[3], r[4], r[5]));
            sb.Append(Crt.Sprintf("Parameters: %lu\n", info.Items));
            if (SunSignatures.IsMultiTime(info.FileSig)) sb.Append(Crt.Sprintf("File data updates: %lu\n", info.Points));
            else sb.Append(Crt.Sprintf("Data points: %lu\n", info.Points));
            float rate = f is ITimeIndexedFile ti ? ti.FileRate : -1f;
            if (rate > 0f) sb.Append(Crt.Sprintf("Rate (1/DeltaT): %g\n", (double)rate));
            else sb.Append(Crt.Sprintf("Rate (1/DeltaT): %s\n", rate < 0f ? "unknown" : "multi"));
            sb.Append(Crt.Sprintf("Cutoff time: %.3f\n", (double)info.Cutoff));
            sb.Append(Crt.Sprintf("Test title: %s\n", StrValid(info.Title)));
            sb.Append(Crt.Sprintf("Header comment: %s\n", StrValid(info.Comment)));
            sb.Append(Crt.Sprintf("Serial number: %s\n", StrValid(info.SerialNum)));
            sb.Append(Crt.Sprintf("Test stand: %s\n", StrValid(info.TestStand)));
            sb.Append(Crt.Sprintf("Test position: %s\n", StrValid(info.TestPosition)));
            sb.Append(Crt.Sprintf("Test type: %s\n", StrValid(info.TestType)));
            sb.Append(Crt.Sprintf("Test number: %s\n", StrValid(info.TestNumber)));
            string? comment = f.CommentBlock;
            if (comment != null)
            {
                sb.Append("Comment block:\n");
                var t = new Strtok(comment);
                string? p;
                while ((p = t.Next("\r\n")) != null) sb.Append(p).Append('\n');
            }
            else sb.Append("Comment block: [empty]\n");
            if (info.FileSig == SunSignatures.PcGoalRealtime)
            {
                sb.Append("[data stream events]\n");
                foreach (var e in f.Events)
                    sb.Append(Crt.Sprintf("%s=%d/%d.%d\n", e.Name, (int)e.Gmt.JulianDay, (int)e.Gmt.MilliSeconds, (int)e.Gmt.MicroSeconds));
            }
        }
        else return null;
        return Nl(sb);
    }

    /// <summary>Writes <see cref="FileDetails"/>; status 0 / −1 open failure / −3 invalid file.</summary>
    public static int SaveFileDetails(IReadOnlyList<IDataFile> files, IReadOnlyList<FileGroup> groups, int fileNo, string fileName, Func<uint, string> description)
    {
        string? t = FileDetails(files, groups, fileNo, description);
        int s = Write(fileName, t ?? "");
        return s != 0 ? s : t == null ? -3 : 0;
    }

    private static int Write(string fileName, string text)
    {
        try
        {
            AnsiText.WriteAllText(fileName, text);
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return -1;
        }
    }
}
