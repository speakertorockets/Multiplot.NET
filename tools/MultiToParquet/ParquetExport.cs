// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

using System.Text.Json;
using Multiplot.Core;
using Multiplot.Plugins;
using Parquet;
using Parquet.Schema;

namespace MultiToParquet;

/// <summary>
/// Names of the Multiplot Parquet time-series convention ("wide" layout):
/// one non-null INT64 column of .NET ticks (100 ns since 0001-01-01T00:00:00Z) and one nullable DOUBLE column
/// per parameter; a null cell means "no sample for this parameter at this row's time".
/// </summary>
public static class Convention
{
    public const string SchemaId = "multiplot.timeseries.wide/1";
    public const string TimeColumn = "time_ticks";
    public const string Prefix = "multiplot.";
}

public sealed record ExportResult(long Rows, int RowGroups, int Columns, long Samples, long Bytes, List<string> Skipped);

public static class ParquetExport
{
    public static async Task<ExportResult> WriteAsync(MultiTimeArchive a, IReadOnlyList<ParameterSeries> all, string output,
        CompressionMethod compression, int rowGroupSize, EncodingHint valueEncoding = EncodingHint.Default,
        System.IO.Compression.CompressionLevel level = System.IO.Compression.CompressionLevel.SmallestSize)
    {
        var skipped = new List<string>();
        var series = new List<ParameterSeries>();
        foreach (var s in all)
        {
            if (s.SkipReason != null) skipped.Add($"{s.Info.Name}: {s.SkipReason}");
            else series.Add(s);
        }

        // column names: primary parameter name, made unique and distinct from the time column
        var used = new HashSet<string>(StringComparer.Ordinal) { Convention.TimeColumn };
        var columns = new string[series.Count];
        for (int i = 0; i < series.Count; i++)
        {
            string baseName = series[i].Info.Name.Trim();
            if (baseName.Length == 0) baseName = $"param{series[i].Info.MapPosition}";
            string name = baseName;
            for (int k = 2; !used.Add(name); k++) name = $"{baseName}_{k}";
            columns[i] = name;
        }

        var timeField = new DataField<long>(Convention.TimeColumn);
        var fields = new List<Field> { timeField };
        var valueFields = columns.Select(c => new DataField<double?>(c)).ToArray();
        fields.AddRange(valueFields);
        var schema = new ParquetSchema(fields);

        var opts = new ParquetOptions { CompressionMethod = compression, CompressionLevel = level, DictionaryEncodingSampleSize = 65536 };
        // ascending time stamps delta-encode to a few bits each; split-byte streams help the codec on doubles
        opts.ColumnEncodingHints[timeField.Path.ToString()] = EncodingHint.DeltaBinaryPacked;
        foreach (var f in valueFields) opts.ColumnEncodingHints[f.Path.ToString()] = valueEncoding;
        long rows = 0, samples = 0;
        int groups = 0;
        await using (var fs = File.Create(output))
        {
            await using var writer = await ParquetWriter.CreateAsync(schema, fs, opts);
            writer.CustomMetadata = BuildMetadata(a, series, columns, skipped);

            // k-way merge of the (time-sorted) parameter series into rows
            var pos = new int[series.Count];
            var tBuf = new long[rowGroupSize];
            var vBuf = series.Select(_ => new double?[rowGroupSize]).ToArray();
            while (true)
            {
                int r = 0;
                while (r < rowGroupSize)
                {
                    long next = long.MaxValue;
                    for (int i = 0; i < series.Count; i++)
                        if (pos[i] < series[i].Ticks.Length && series[i].Ticks[pos[i]] < next) next = series[i].Ticks[pos[i]];
                    if (next == long.MaxValue) break;
                    tBuf[r] = next;
                    for (int i = 0; i < series.Count; i++)
                    {
                        // one sample per parameter per row; a repeated time stamp starts another row
                        if (pos[i] < series[i].Ticks.Length && series[i].Ticks[pos[i]] == next)
                        {
                            vBuf[i][r] = series[i].Values[pos[i]++];
                            samples++;
                        }
                        else vBuf[i][r] = null;
                    }
                    r++;
                }
                if (r == 0) break;
                using (var rg = writer.CreateRowGroup())
                {
                    await rg.WriteAsync(timeField, new ReadOnlyMemory<long>(tBuf, 0, r));
                    for (int i = 0; i < series.Count; i++)
                        await rg.WriteAsync(valueFields[i], new ReadOnlyMemory<double?>(vBuf[i], 0, r));
                }
                groups++;
                rows += r;
                if (r < rowGroupSize) break;
            }
        }
        return new ExportResult(rows, groups, columns.Length, samples, new FileInfo(output).Length, skipped);
    }

    private static Dictionary<string, string> BuildMetadata(MultiTimeArchive a, List<ParameterSeries> series, string[] columns, List<string> skipped)
    {
        var info = a.File.Info;
        string P(string k) => Convention.Prefix + k;
        string Txt(string s) => s == " " ? "" : s; // the SUN reader turns empty fixed-width fields into " "
        var md = new Dictionary<string, string>
        {
            [P("schema")] = Convention.SchemaId,
            [P("time.column")] = Convention.TimeColumn,
            [P("time.unit")] = "ticks: 100 ns intervals since 0001-01-01T00:00:00Z (System.DateTime.Ticks, UTC)",
            [P("source.file")] = System.IO.Path.GetFileName(a.Path),
            [P("source.format")] = info.FormatDescription,
            [P("source.signature")] = $"0x{info.FileSig:X8}",
            [P("header.refTime")] = string.Join(",", info.RefTime),
            [P("header.refTicks")] = MultiTimeArchive.ToTicks(a.HeaderYear, HeaderRef(info)).ToString(),
            [P("reference.ticks")] = a.RefTicks.ToString(),
            [P("reference.iso")] = MultiTimeArchive.Iso(a.RefTicks),
            [P("header.items")] = info.Items.ToString(),
            [P("header.blockPoints")] = string.Join(",", a.File.BlockPoints),
            [P("header.cutoff")] = info.Cutoff.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            [P("header.title")] = Txt(info.Title),
            [P("header.comment")] = Txt(info.Comment),
            [P("header.serialNum")] = Txt(info.SerialNum),
            [P("header.testStand")] = Txt(info.TestStand),
            [P("header.testPosition")] = Txt(info.TestPosition),
            [P("header.testType")] = Txt(info.TestType),
            [P("header.testNumber")] = Txt(info.TestNumber),
        };
        if (a.File.CommentBlock is { } c) md[P("comment")] = c;

        var colByMap = new Dictionary<int, string>();
        for (int i = 0; i < series.Count; i++) colByMap[series[i].Info.MapPosition] = columns[i];
        var parms = a.File.Parameters.Select(p => new Dictionary<string, object?>
        {
            ["mapPosition"] = p.MapPosition,
            ["column"] = colByMap.TryGetValue(p.MapPosition, out var col) ? col : null,
            ["name"] = p.Names[0]?.Trim(),
            ["altName"] = p.Names[1]?.Trim(),
            ["extName"] = p.Names[2],
            ["description"] = Txt(p.Descriptions[0] ?? " "),
            ["extDescription"] = p.Descriptions[1],
            ["units"] = Txt(p.Units),
            ["typeId"] = p.ExtendedType ? p.TypeId : "f4",
            ["rate"] = p.Rate,
            ["sequenceNumber"] = p.SequenceNumber,
            ["indexNomen"] = series.FirstOrDefault(s => s.Info.MapPosition == p.MapPosition)?.IndexNomen is { Length: > 0 } n ? n : null,
        }).ToList();
        md[P("parameters")] = JsonSerializer.Serialize(parms);

        var events = a.File.Events.Select(e => new Dictionary<string, object?>
        {
            ["name"] = e.Name,
            ["julianDay"] = e.Utc.JulianDay,
            ["milliSeconds"] = e.Utc.MilliSeconds,
            ["microSeconds"] = e.Utc.MicroSeconds,
            ["ticks"] = e.Utc.JulianDay is >= 1 and <= 366 ? a.TagTicks(e.Utc) : null,
        }).ToList();
        md[P("events")] = JsonSerializer.Serialize(events);
        if (skipped.Count > 0) md[P("skipped")] = string.Join("\n", skipped);
        md[P("converter")] = "multi2parquet 1.0.0 (Multiplot.NET)";
        return md;
    }

    private static TimeTag HeaderRef(DataFileInfo info) => new(
        unchecked((short)Math.Max(1u, info.RefTime[1])),
        unchecked(info.RefTime[2] * 3600000U + info.RefTime[3] * 60000U + info.RefTime[4] * 1000U + info.RefTime[5]), 0);
}
