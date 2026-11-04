using System.Diagnostics;
using Multiplot.Core;
using MultiToParquet;
using Parquet;
using Parquet.Schema;

const string Usage = """
multi2parquet - convert SUN_MULTITIME / SUN_MULTITIME64 (.multi) files to Apache Parquet

usage:
  multi2parquet info    <file.multi> [--year N]
  multi2parquet convert <file.multi> [output.parquet] [--compression zstd|snappy|gzip|none]
                        [--level fastest|optimal|smallest]
                        [--value-encoding plain|split|dictionary] [--row-group N] [--year N] [--force]
  multi2parquet verify  <file.multi> <file.parquet> [--year N]

Output layout (multiplot.timeseries.wide/1): INT64 column 'time_ticks' (.NET ticks, UTC) plus one
nullable DOUBLE column per parameter; header, parameter map, events and the comment block are stored
in the file's key/value metadata under 'multiplot.*'.
""";

if (args.Length < 2) { Console.Error.WriteLine(Usage); return 2; }
string cmd = args[0].ToLowerInvariant();
var pos = new List<string>();
string compression = "zstd";
string valueEnc = "plain";
string level = "smallest";
int rowGroup = 1 << 20;
int? year = null;
bool force = false;
for (int i = 1; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--compression": compression = args[++i].ToLowerInvariant(); break;
        case "--row-group": rowGroup = int.Parse(args[++i]); break;
        case "--year": year = int.Parse(args[++i]); break;
        case "--force": force = true; break;
        case "--level": level = args[++i].ToLowerInvariant(); break;
        case "--value-encoding": valueEnc = args[++i].ToLowerInvariant(); break;
        default: pos.Add(args[i]); break;
    }
}

try
{
    switch (cmd)
    {
        case "info": return Info(pos[0], year);
        case "convert": return await Convert(pos[0], pos.Count > 1 ? pos[1] : Path.ChangeExtension(pos[0], ".parquet"));
        case "verify": return await Verify(pos[0], pos[1], year);
        case "crosscheck": return CrossCheck(pos[0], year);
        case "synth": Synth(pos[0], pos.Count > 1 && pos[1] == "32"); return 0;
        default: Console.Error.WriteLine(Usage); return 2;
    }
}
catch (Exception ex) when (ex is Multiplot.Plugins.DataAccessException or InvalidDataException or IOException)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}

static List<ParameterSeries> ReadAll(MultiTimeArchive a) =>
    Enumerable.Range(0, a.File.Parameters.Count).Select(a.ReadParameter).ToList();

static int Info(string path, int? year)
{
    using var a = MultiTimeArchive.Open(path, year);
    var fi = a.File.Info;
    Console.WriteLine($"file        {a.Path} ({new FileInfo(a.Path).Length:N0} bytes)");
    Console.WriteLine($"format      {fi.FormatDescription} (0x{fi.FileSig:X8}), blockPoints {string.Join("/", a.File.BlockPoints)}{(fi.IsDynamic ? " LIVE" : "")}");
    Console.WriteLine($"refTime     {string.Join(",", fi.RefTime)}  effective reference {MultiTimeArchive.Iso(a.RefTicks)}");
    Console.WriteLine($"title       '{fi.Title}'  comment '{fi.Comment}'");
    Console.WriteLine($"serial/stand/pos/type/num  '{fi.SerialNum}' '{fi.TestStand}' '{fi.TestPosition}' '{fi.TestType}' '{fi.TestNumber}'  cutoff {fi.Cutoff}");
    Console.WriteLine($"comment     {a.File.CommentBlock?.Length ?? 0} chars (block {a.File.CommentSize:N0} bytes at 0x{a.File.CommentOffset:X})");
    foreach (var e in a.File.Events) Console.WriteLine($"  event     {e.Name} = {e.Gmt}");
    Console.WriteLine($"parameters  {fi.Items}");
    long total = 0;
    var all = ReadAll(a);
    foreach (var s in all)
    {
        total += s.Ticks.Length;
        string range = s.Ticks.Length > 0 ? $"{MultiTimeArchive.Iso(s.Ticks[0])} .. {MultiTimeArchive.Iso(s.Ticks[^1])}" : "(no data)";
        var flags = new List<string>();
        if (s.Dropouts > 0) flags.Add($"{s.Dropouts} dropouts");
        if (s.DuplicateTimes > 0) flags.Add($"{s.DuplicateTimes} dup times");
        if (s.OutOfOrder > 0) flags.Add($"{s.OutOfOrder} out-of-order");
        if (s.InexactIntegers > 0) flags.Add($"{s.InexactIntegers} ints > 2^53");
        if (s.BadPackedDays > 0) flags.Add($"{s.BadPackedDays} packed times with day outside 1..366");
        if (s.SkipReason != null) flags.Add("SKIP: " + s.SkipReason);
        Console.WriteLine($"  [{s.Info.MapPosition,3}] {s.Info.Name,-16} {s.TypeId} {s.Info.Units,-10} rate {s.Info.Rate,6} blocks {s.Blocks,5} ({s.CompressedBlocks} lzh) idx {string.Join("/", s.IndexTypes.Select(MultiTimeArchive.IndexTypeName))}");
        Console.WriteLine($"        points {s.Ticks.Length,10:N0}  {range}  {string.Join(", ", flags)}");
    }
    Console.WriteLine($"total samples {total:N0}");
    foreach (var w in a.Warnings) Console.WriteLine($"warning: {w}");
    return 0;
}

async Task<int> Convert(string input, string output)
{
    if (File.Exists(output) && !force) { Console.Error.WriteLine($"error: {output} exists (use --force)"); return 1; }
    var method = compression switch
    {
        "zstd" => CompressionMethod.Zstd,
        "snappy" => CompressionMethod.Snappy,
        "gzip" => CompressionMethod.Gzip,
        "none" => CompressionMethod.None,
        _ => throw new InvalidDataException($"unknown compression '{compression}'"),
    };
    var sw = Stopwatch.StartNew();
    using var a = MultiTimeArchive.Open(input, year);
    var all = ReadAll(a);
    var enc = valueEnc switch
    {
        "split" => EncodingHint.ByteSplitStream,
        "plain" => EncodingHint.Default,
        "dictionary" => EncodingHint.Dictionary,
        _ => throw new InvalidDataException($"unknown value encoding '{valueEnc}'"),
    };
    var tRead = sw.Elapsed;
    var lvl = level switch
    {
        "fastest" => System.IO.Compression.CompressionLevel.Fastest,
        "optimal" => System.IO.Compression.CompressionLevel.Optimal,
        "smallest" => System.IO.Compression.CompressionLevel.SmallestSize,
        _ => throw new InvalidDataException($"unknown level '{level}'"),
    };
    var r = await ParquetExport.WriteAsync(a, all, output, method, rowGroup, enc, lvl);
    foreach (var w in a.Warnings) Console.WriteLine($"warning: {w}");
    foreach (var s in r.Skipped) Console.WriteLine($"skipped: {s}");
    foreach (var s in all.Where(s => s.OutOfOrder > 0)) Console.WriteLine($"warning: {s.Info.Name}: {s.OutOfOrder} out-of-order time stamps were sorted");
    foreach (var s in all.Where(s => s.BadPackedDays > 0)) Console.WriteLine($"warning: {s.Info.Name}: {s.BadPackedDays} packed time stamps decode to a day outside 1..366 (written across a year end); exported as decoded");
    foreach (var s in all.Where(s => s.InexactIntegers > 0)) Console.WriteLine($"warning: {s.Info.Name}: {s.InexactIntegers} 64-bit integers exceed 2^53 and are rounded in DOUBLE");
    long inLen = new FileInfo(input).Length;
    Console.WriteLine($"{Path.GetFileName(input)} -> {output}");
    Console.WriteLine($"  {r.Columns} value columns, {r.Rows:N0} rows in {r.RowGroups} row groups, {r.Samples:N0} samples, {compression} ({level})");
    Console.WriteLine($"  {inLen:N0} -> {r.Bytes:N0} bytes ({(inLen > 0 ? 100.0 * r.Bytes / inLen : 0):F1}%), {sw.Elapsed.TotalSeconds:F1} s (read {tRead.TotalSeconds:F1} s)");
    return 0;
}

// Writes a synthetic MULTITIME(64) file that exercises every index type, compression, byte order,
// appended blocks, dropouts, integer types, a v1 blob, FILE_REF/EOF events and a [rates] table.
static void Synth(string path, bool bits32)
{
    using var w = Multiplot.Sun.SunWriter.Create(path, Multiplot.Sun.SunCreateMode.Overwrite, 4096, 9, 0,
        bits32 ? SunSignatures.MultiTime : SunSignatures.MultiTime64);
    w.SetRefTime(2024, 366, 23, 59, 0, 250); // leap-year last day: data crosses into 2025
    w.SetTitle("Synthetic"); w.SetSerialNum("SN-1"); w.SetTestStand("Stand 7"); w.SetTestType("Unit"); w.SetTestNumb("42");
    const int n = 1000;
    var f4t = new float[n]; var d8t = new double[n]; var u4t = new uint[n]; var t8 = new Multiplot.Plugins.TimeTag[n];
    var f4 = new float[n]; var d8 = new double[n]; var i2 = new short[n]; var u4 = new uint[n];
    for (int i = 0; i < n; i++)
    {
        f4t[i] = i * 0.1f; d8t[i] = i * 0.125 + 1e-6 * i; u4t[i] = (uint)(i * 50);
        long us = (23 * 3600 + 59 * 60) * 1_000_000L + 250_000 + i * 120_000L + 7; // day 366 23:59:00.250007 + i*120 ms
        long dayUs = 86_400_000_000L;
        t8[i] = us < dayUs
            ? new Multiplot.Plugins.TimeTag(366, (uint)(us / 1000), (ushort)(us % 1000))
            : new Multiplot.Plugins.TimeTag(1, (uint)((us - dayUs) / 1000), (ushort)((us - dayUs) % 1000));
        f4[i] = MathF.Sin(i / 50f); d8[i] = Math.Cos(i / 30.0) * 1e3; i2[i] = (short)(i - 500); u4[i] = (uint)(i * 7);
    }
    f4[10] = Multiplot.Plugins.DataTypes.FpDropOut;
    var none = Multiplot.Sun.SunWriteOptions.None; var lzh = Multiplot.Sun.SunWriteOptions.LzhCompress;
    w.AddParm("F4IDX", "f4idx", "V", "float index, raw", 1, "f4", f4, "f4", f4t, n, none, "Time,Seconds,Relative Time");
    w.AddParm("D8IDX", "d8idx", "Pa", "double index, lzh", 2, "d8", d8, "d8", d8t, n, lzh, null);
    w.AddParm("U4IDX", "u4idx", "cnt", "ms index", 3, "i2", i2, "u4", u4t, n, none, null);
    w.AddParm("T8BE", "t8be", "K", "timetag index, big endian", 4, "u4", u4, "t8", t8, n, Multiplot.Sun.SunWriteOptions.OrientBigEnd, null);
    w.AddParm("T8LZH", "t8lzh", "K", "timetag index, lzh", 5, "d8", d8, "t8", t8, n, lzh, null);
    w.AddParm("PACKED", "packed", "K", "packed time", 6, "f4", f4, "t8", t8, n, lzh | Multiplot.Sun.SunWriteOptions.PackTimeTag, null);
    w.AddParm("BLOB", "blob", "", "variable blob", 7, "v1", new byte[] { 1, 2, 3, 4 }, "d8", new double[] { 1.0 }, 4, none, null);
    w.AddParm("EMPTY", "empty", "", "declared, no data", 8, "f4", Array.Empty<float>(), "f4", Array.Empty<float>(), 0, none, null);
    w.AddParm("TIME", "time", "s", "name collision with time column?", 9, "f4", f4, "f4", f4t, n, none, null);
    // appended second block for D8IDX
    var d8t2 = d8t.Select(x => x + 200.0).ToArray();
    w.AddParm("D8IDX", "d8idx", "Pa", "double index, lzh", 2, "d8", d8, "d8", d8t2, n, lzh, null);
    w.SetCommentText("[events]\nFILE_REF=366/86340500.0\nEOF=001/600000.0\n[rates]\n0,F4IDX,10\n[coef]\nMSID=F4IDX\n");
    w.Close();
}

// Compares the converter's block reader with the application's SUN read path (SunFile.Read).
static int CrossCheck(string input, int? year)
{
    using var a = MultiTimeArchive.Open(input, year);
    int errors = 0;
    for (int m = 0; m < a.File.Parameters.Count; m++)
    {
        var s = a.ReadParameter(m);
        if (s.SkipReason != null || s.Ticks.Length == 0) { Console.WriteLine($"  {s.Info.Name}: skipped ({s.SkipReason ?? "no data"})"); continue; }
        var p = a.File.Read(new Multiplot.Plugins.ParameterRequest($"@{m}", -1e7, 1e7, null, null, Multiplot.Plugins.DataType.Double, true))!;
        int n = Math.Min(p.Points, s.Ticks.Length);
        var vals = (double[])p.Data;
        int vBad = 0, tBad = 0;
        long maxDt = 0;
        for (int i = 0; i < n; i++)
        {
            double x = vals[i], y = s.Values[i];
            if (!(double.IsNaN(y) ? Multiplot.Plugins.DataTypes.IsDropOut(x) || double.IsNaN(x) : x == y)) vBad++;
            long appTicks = p.HasValidGmt ? a.TagTicks(p.GmtAt(i)) : a.RefTicks + (long)Math.Round(p.Time[i] * 1e7);
            long dt = Math.Abs(appTicks - s.Ticks[i]);
            maxDt = Math.Max(maxDt, dt);
            if (p.HasValidGmt && dt > 10) tBad++;
        }
        bool ok = vBad == 0 && tBad == 0 && (p.Points == s.Ticks.Length || p.Points == s.Ticks.Length + 1);
        if (!ok) errors++;
        Console.WriteLine($"  {s.Info.Name}: app {p.Points:N0} pts{(p.HasValidGmt ? " (GMT)" : " (float time)")}, converter {s.Ticks.Length:N0}; value diffs {vBad}, time diffs {tBad}, max |dt| {maxDt / 10.0:F1} us {(ok ? "OK" : "MISMATCH")}");
    }
    return errors == 0 ? 0 : 1;
}

static async Task<int> Verify(string input, string parquet, int? year)
{
    using var a = MultiTimeArchive.Open(input, year);
    var all = ReadAll(a).Where(s => s.SkipReason == null).ToList();
    await using var reader = await ParquetReader.CreateAsync(parquet);
    var md = reader.CustomMetadata;
    var fields = reader.Schema.GetDataFields();
    var timeField = fields.First(f => f.Name == Convention.TimeColumn);
    var cols = System.Text.Json.JsonDocument.Parse(md["multiplot.parameters"]).RootElement.EnumerateArray()
        .Where(e => e.GetProperty("column").ValueKind == System.Text.Json.JsonValueKind.String)
        .ToDictionary(e => e.GetProperty("mapPosition").GetInt32(), e => e.GetProperty("column").GetString()!);

    // streaming comparison: each non-null cell must be the next source sample of that parameter, bit for bit
    var at = new long[all.Count];
    var bad = new bool[all.Count];
    long prev = long.MinValue;
    int errors = 0;
    for (int g = 0; g < reader.RowGroupCount; g++)
    {
        using var rg = reader.OpenRowGroupReader(g);
        int n = (int)rg.RowCount;
        var t = new long[n];
        await rg.ReadAsync(timeField, t.AsMemory());
        for (int i = 0; i < n; i++)
        {
            if (t[i] < prev) { errors++; Console.WriteLine($"row group {g} row {i}: time not ascending"); break; }
            prev = t[i];
        }
        var v = new double?[n];
        for (int k = 0; k < all.Count; k++)
        {
            var s = all[k];
            var f = fields.First(x => x.Name == cols[s.Info.MapPosition]);
            await rg.ReadAsync(f, v.AsMemory());
            for (int i = 0; i < n && !bad[k]; i++)
            {
                if (!v[i].HasValue) continue;
                long j = at[k]++;
                if (j >= s.Ticks.Length || t[i] != s.Ticks[j]
                    || BitConverter.DoubleToInt64Bits(v[i]!.Value) != BitConverter.DoubleToInt64Bits(s.Values[j]))
                {
                    bad[k] = true;
                    Console.WriteLine($"MISMATCH {s.Info.Name}: sample {j:N0} (row group {g} row {i})");
                }
            }
        }
    }
    for (int k = 0; k < all.Count; k++)
    {
        if (!bad[k] && at[k] != all[k].Ticks.Length)
        {
            bad[k] = true;
            Console.WriteLine($"MISMATCH {all[k].Info.Name}: parquet {at[k]:N0} vs source {all[k].Ticks.Length:N0} samples");
        }
        if (bad[k]) errors++;
    }
    foreach (string k in new[] { "multiplot.header.title", "multiplot.header.testStand", "multiplot.reference.iso" })
        Console.WriteLine($"  {k} = {(md.TryGetValue(k, out var x) ? x : "(missing)")}");
    Console.WriteLine(errors == 0 ? $"OK: {all.Count} parameters, {all.Sum(s => (long)s.Ticks.Length):N0} samples match bit-for-bit" : $"FAILED: {errors} problem(s)");
    return errors == 0 ? 0 : 1;
}
