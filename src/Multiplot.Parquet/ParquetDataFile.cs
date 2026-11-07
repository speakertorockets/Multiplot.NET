// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Multiplot.Plugins;
using Parquet;
using Parquet.Schema;

namespace Multiplot.Parquet;

/// <summary>
/// An open Parquet file. Each non-time numeric top-level column is a parameter; a null cell means
/// "no sample at this row". Time stamps are absolute (.NET ticks internally) and converted to the
/// application's relative-seconds / year-less <see cref="TimeTag"/> model on every read.
/// </summary>
internal sealed class ParquetDataFile : IDataFile
{
    private enum TimeKind { Ticks, Timestamp, Seconds }

    private sealed class Column
    {
        public required DataField Field { get; init; }
        public required ParameterInfo Info { get; init; }
        public string IndexNomen { get; init; } = "";
        public bool HasData { get; set; } = true;
    }

    private sealed class RowGroupIndex
    {
        public long Rows;
        public long MinTicks = long.MaxValue, MaxTicks = long.MinValue;
        public bool Known; // min/max known (statistics or scan)
    }

    private const long CacheLimit = 32L * 1024 * 1024; // cached time stamps (8 bytes each)
    private const string P = "multiplot.";

    private readonly object _lock = new();
    private readonly ParquetDataSource _owner;
    private readonly FileStream _fs;
    private readonly ParquetReader _reader;
    private readonly DataField _timeField;
    private readonly TimeKind _timeKind;
    private readonly long _secondsRefTicks; // reference of a relative-seconds time column
    private readonly List<Column> _columns = new();
    private readonly List<ParameterInfo> _parameters = new();
    private readonly List<FileEvent> _events = new();
    private readonly RowGroupIndex[] _groups;
    private readonly Dictionary<int, long[]> _timeCache = new();
    private long _cached;
    private bool _unsorted;
    private readonly long _refTicks;
    private readonly int _refYear;

    public DataFileInfo Info { get; } = new();
    public IReadOnlyList<ParameterInfo> Parameters => _parameters;
    public IReadOnlyList<FileEvent> Events => _events;
    public string? CommentBlock { get; }

    private ParquetDataFile(ParquetDataSource owner, string path, FileStream fs, ParquetReader reader)
    {
        _owner = owner;
        _fs = fs;
        _reader = reader;
        var md = reader.CustomMetadata ?? new Dictionary<string, string>();
        string? Md(string key) => md.TryGetValue(P + key, out var v) ? v : null;

        var fields = reader.Schema.GetDataFields().Where(f => f.Path.Length == 1 && !f.IsArray).ToList();
        (_timeField, _timeKind) = FindTimeColumn(fields, Md("time.column"));
        if (_timeKind == TimeKind.Seconds)
        {
            _secondsRefTicks = long.TryParse(Md("reference.ticks"), out long rt) ? rt
                : DateTime.TryParse(Md("reference.iso"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var ri) ? ri.Ticks
                : new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
        }

        _groups = new RowGroupIndex[reader.RowGroupCount];
        long totalRows = 0;
        for (int g = 0; g < _groups.Length; g++)
        {
            using var rg = reader.OpenRowGroupReader(g);
            var gi = _groups[g] = new RowGroupIndex { Rows = rg.RowCount };
            totalRows += gi.Rows;
            var st = Safe(() => rg.GetStatistics(_timeField));
            // statistics are trusted only when their value type matches the time kind: for TIMESTAMP columns some
            // writers' statistics come back as the raw int64 unit count (us/ns), which is not .NET ticks
            bool usable = _timeKind switch
            {
                TimeKind.Ticks => st?.MinValue is long && st.MaxValue is long,
                TimeKind.Timestamp => st?.MinValue is DateTime or DateTimeOffset && st.MaxValue is DateTime or DateTimeOffset,
                _ => false,
            };
            if (usable)
            {
                gi.MinTicks = ToTicks(st.MinValue);
                gi.MaxTicks = ToTicks(st.MaxValue);
                gi.Known = true;
            }
        }
        for (int g = 0; g < _groups.Length; g++)
            if (!_groups[g].Known) ScanTime(g);
        for (int g = 1; g < _groups.Length; g++)
            if (_groups[g].Rows > 0 && _groups[g - 1].Rows > 0 && _groups[g].MinTicks < _groups[g - 1].MaxTicks) _unsorted = true;

        // reference: converter metadata, else the first sample
        long first = _groups.Where(g => g.Rows > 0).Select(g => g.MinTicks).DefaultIfEmpty(_secondsRefTicks).Min();
        _refTicks = long.TryParse(Md("reference.ticks"), out long r) ? r : _timeKind == TimeKind.Seconds ? _secondsRefTicks : first;
        if (_refTicks <= 0 || _refTicks >= DateTime.MaxValue.Ticks) _refTicks = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
        var refDt = new DateTime(_refTicks, DateTimeKind.Utc);
        _refYear = refDt.Year;

        // parameters
        var meta = ParseParameterMetadata(Md("parameters"));
        int pos = 0;
        foreach (var f in fields)
        {
            if (f == _timeField || !IsNumeric(f.ClrType)) continue;
            meta.TryGetValue(f.Name, out var m);
            var pi = new ParameterInfo { MapPosition = pos++ };
            pi.Names[0] = Str(m, "name") ?? f.Name;
            pi.Names[1] = Str(m, "altName") ?? pi.Names[0];
            pi.Names[2] = Str(m, "extName");
            pi.Descriptions[0] = Blank(Str(m, "description"));
            pi.Descriptions[1] = Str(m, "extDescription");
            pi.Units = Blank(Str(m, "units"));
            pi.TypeId = Str(m, "typeId") ?? TypeIdOf(f.ClrType);
            pi.ExtendedType = pi.TypeId != "f4";
            pi.Rate = m.ValueKind == JsonValueKind.Object && m.TryGetProperty("rate", out var rate) && rate.TryGetSingle(out float rf) ? rf : -1f;
            pi.SequenceNumber = m.ValueKind == JsonValueKind.Object && m.TryGetProperty("sequenceNumber", out var sq) && sq.TryGetUInt32(out uint su) ? su : (uint)pos;
            var col = new Column { Field = f, Info = pi, IndexNomen = Str(m, "indexNomen") ?? "" };
            // all-null column (declared parameter without data)
            long nulls = 0;
            bool known = true;
            for (int g = 0; g < _groups.Length && known; g++)
            {
                using var rg = reader.OpenRowGroupReader(g);
                var st = f.IsNullable ? Safe(() => rg.GetStatistics(f)) : null;
                if (!f.IsNullable) { known = false; break; }
                if (st?.NullCount is long nc) nulls += nc; else known = false;
            }
            if (known && nulls >= totalRows) col.HasData = false;
            _columns.Add(col);
            _parameters.Add(pi);
        }

        Info.Path = path;
        Info.FileSig = ParquetDataSource.ParquetSignature;
        Info.FormatDescription = Md("schema") is { } schema ? $"Apache Parquet ({schema})" : "Apache Parquet";
        var refTime = Md("header.refTime")?.Split(',').Select(s => uint.TryParse(s, out uint u) ? u : 0u).ToArray();
        Info.RefTime = refTime is { Length: 6 } ? refTime
            : new[] { (uint)refDt.Year, (uint)refDt.DayOfYear, (uint)refDt.Hour, (uint)refDt.Minute, (uint)refDt.Second, (uint)refDt.Millisecond };
        Info.RefUtc = TagOf(_refTicks);
        Info.Items = (uint)_parameters.Count;
        Info.Points = (uint)Math.Min(totalRows, uint.MaxValue);
        Info.IsDynamic = false;
        Info.Cutoff = float.TryParse(Md("header.cutoff"), NumberStyles.Float, CultureInfo.InvariantCulture, out float co) ? co : 0f;
        Info.Title = Blank(Md("header.title"));
        Info.Comment = Blank(Md("header.comment"));
        Info.SerialNum = Blank(Md("header.serialNum"));
        Info.TestStand = Blank(Md("header.testStand"));
        Info.TestPosition = Blank(Md("header.testPosition"));
        Info.TestType = Blank(Md("header.testType"));
        Info.TestNumber = Blank(Md("header.testNumber"));
        CommentBlock = Md("comment");
        ParseEvents(Md("events"));
    }

    public static ParquetDataFile Open(string path, ParquetDataSource owner)
    {
        string full = Path.GetFullPath(path);
        var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 1 << 16, FileOptions.RandomAccess);
        try
        {
            var reader = Run(() => ParquetReader.CreateAsync(fs, null, true));
            try { return new ParquetDataFile(owner, full, fs, reader); }
            catch { Run(() => reader.DisposeAsync().AsTask()); throw; }
        }
        catch
        {
            fs.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            Run(() => _reader.DisposeAsync().AsTask());
            _fs.Dispose();
        }
    }

    public RefreshResult Refresh(bool clearEventFlag) => new(0);

    // ------------------------------------------------------------------ schema helpers

    private static (DataField, TimeKind) FindTimeColumn(List<DataField> fields, string? declared)
    {
        DataField? f = declared != null ? fields.FirstOrDefault(x => x.Name == declared) : null;
        f ??= fields.FirstOrDefault(x => x.Name == "time_ticks" && x.ClrType == typeof(long));
        f ??= fields.FirstOrDefault(x => x.ClrType == typeof(DateTime) || x.ClrType == typeof(DateTimeOffset));
        f ??= fields.FirstOrDefault(x => (x.ClrType == typeof(double) || x.ClrType == typeof(float))
            && x.Name.ToLowerInvariant() is "time" or "time_s" or "seconds" or "rel_time");
        if (f == null)
            throw new DataAccessException((int)SunError.UnknownFormat,
                "Parquet file has no time column (time_ticks INT64, a TIMESTAMP column, or 'time' seconds)");
        TimeKind k = f.ClrType == typeof(long) ? TimeKind.Ticks
            : f.ClrType == typeof(DateTime) || f.ClrType == typeof(DateTimeOffset) ? TimeKind.Timestamp
            : f.ClrType == typeof(double) || f.ClrType == typeof(float) ? TimeKind.Seconds
            : throw new DataAccessException((int)SunError.UnknownFormat, $"time column '{f.Name}' has unsupported type {f.ClrType.Name}");
        return (f, k);
    }

    private static readonly Type[] NumericTypes =
    {
        typeof(double), typeof(float), typeof(decimal), typeof(long), typeof(ulong), typeof(int), typeof(uint),
        typeof(short), typeof(ushort), typeof(sbyte), typeof(byte), typeof(bool),
    };

    private static bool IsNumeric(Type t) => NumericTypes.Contains(t);

    private static string TypeIdOf(Type t) =>
        t == typeof(float) ? "f4" : t == typeof(short) || t == typeof(sbyte) ? "i2" : t == typeof(int) ? "i4" : t == typeof(long) ? "i8"
        : t == typeof(ushort) ? "u2" : t == typeof(uint) ? "u4" : t == typeof(ulong) ? "u8" : t == typeof(byte) || t == typeof(bool) ? "b1" : "d8";

    private static Dictionary<string, JsonElement> ParseParameterMetadata(string? json)
    {
        var d = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(json)) return d;
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var e in doc.RootElement.EnumerateArray())
                if (e.TryGetProperty("column", out var c) && c.ValueKind == JsonValueKind.String)
                    d[c.GetString()!] = e.Clone();
        }
        catch (JsonException) { }
        return d;
    }

    private void ParseEvents(string? json)
    {
        if (string.IsNullOrEmpty(json)) return;
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                string name = e.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                short day = e.TryGetProperty("julianDay", out var d) && d.TryGetInt16(out short dv) ? dv : (short)0;
                uint ms = e.TryGetProperty("milliSeconds", out var m) && m.TryGetUInt32(out uint mv) ? mv : 0;
                ushort us = e.TryGetProperty("microSeconds", out var u) && u.TryGetUInt16(out ushort uv) ? uv : (ushort)0;
                if (name.Length == 0) continue;
                // the converter keeps the SUN reader's "(invalid)" suffix in the name
                _events.Add(new FileEvent(name, new TimeTag(day, ms, us)));
            }
        }
        catch (JsonException) { }
    }

    private static string? Str(JsonElement m, string key) =>
        m.ValueKind == JsonValueKind.Object && m.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>The SUN reader reports empty text fields as a single space.</summary>
    private static string Blank(string? s) => string.IsNullOrEmpty(s) ? " " : s;

    private static T? Safe<T>(Func<T?> f) where T : class
    {
        try { return f(); }
        catch (Exception) { return null; }
    }

    private static T Run<T>(Func<Task<T>> f) => Task.Run(f).GetAwaiter().GetResult();
    private static void Run(Func<Task> f) => Task.Run(f).GetAwaiter().GetResult();

    // ------------------------------------------------------------------ time conversion

    private long ToTicks(object v) => v switch
    {
        long l => l,
        // UTC-adjusted timestamps arrive as Kind=Utc; naive ones keep their wall-clock value (Kind=Local or
        // Unspecified) and are taken as UTC without conversion
        DateTime dt => dt.Ticks,
        DateTimeOffset o => o.UtcTicks,
        double d => SecondsToTicks(d),
        float f => SecondsToTicks(f),
        _ => throw new InvalidDataException($"unsupported time value {v.GetType().Name}"),
    };

    private long SecondsToTicks(double s) => AddSeconds(_secondsRefTicks, s);

    /// <summary>
    /// <paramref name="baseTicks"/> + <paramref name="seconds"/>, clamped to the DateTime range. The offset is rounded
    /// on its own: adding it to ~6e17 ticks in double would lose about 13 us of precision.
    /// </summary>
    private static long AddSeconds(long baseTicks, double seconds)
    {
        double d = seconds * TimeSpan.TicksPerSecond;
        if (double.IsNaN(d)) return baseTicks;
        if (d <= -baseTicks) return 0;
        if (d >= DateTime.MaxValue.Ticks - baseTicks) return DateTime.MaxValue.Ticks;
        return baseTicks + (long)Math.Round(d);
    }

    /// <summary>Year-less time tag of an absolute time (built directly, not through TimeMath.Utc).</summary>
    internal static TimeTag TagOf(long ticks)
    {
        var dt = new DateTime(ticks, DateTimeKind.Utc);
        long dayTicks = ticks % TimeSpan.TicksPerDay;
        return new TimeTag((short)dt.DayOfYear, (uint)(dayTicks / TimeSpan.TicksPerMillisecond), (ushort)(dayTicks % TimeSpan.TicksPerMillisecond / 10));
    }

    /// <summary>Absolute time of a year-less tag, placed within ±180 days of the file reference (as TimeMath.Time).</summary>
    internal long TicksOf(TimeTag t)
    {
        int diff = t.JulianDay - Info.RefUtc.JulianDay;
        int year = _refYear + (diff >= 180 ? -1 : diff <= -180 ? 1 : 0);
        return new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks + (t.JulianDay - 1L) * TimeSpan.TicksPerDay
            + t.MilliSeconds * TimeSpan.TicksPerMillisecond + t.MicroSeconds * 10L;
    }

    // ------------------------------------------------------------------ column reading

    /// <summary>Time stamps of a row group; long.MinValue marks a row without a time stamp.</summary>
    private long[] GroupTimes(int g)
    {
        if (_timeCache.TryGetValue(g, out var c)) return c;
        int n = checked((int)_groups[g].Rows);
        var t = new long[n];
        using (var rg = _reader.OpenRowGroupReader(g))
        {
            var f = _timeField;
            if (f.ClrType == typeof(long)) Read<long>(rg, f, n, (i, v) => t[i] = v, i => t[i] = long.MinValue);
            else if (f.ClrType == typeof(DateTime)) Read<DateTime>(rg, f, n, (i, v) => t[i] = ToTicks(v), i => t[i] = long.MinValue);
            else if (f.ClrType == typeof(DateTimeOffset)) Read<DateTimeOffset>(rg, f, n, (i, v) => t[i] = v.UtcTicks, i => t[i] = long.MinValue);
            else if (f.ClrType == typeof(double)) Read<double>(rg, f, n, (i, v) => t[i] = SecondsToTicks(v), i => t[i] = long.MinValue);
            else Read<float>(rg, f, n, (i, v) => t[i] = SecondsToTicks(v), i => t[i] = long.MinValue);
        }
        for (int i = 1; i < n; i++)
            if (t[i] != long.MinValue && t[i] < t[i - 1]) { _unsorted = true; break; }
        if (_cached + n > CacheLimit) { _timeCache.Clear(); _cached = 0; }
        _timeCache[g] = t;
        _cached += n;
        return t;
    }

    private void ScanTime(int g)
    {
        var t = GroupTimes(g);
        var gi = _groups[g];
        foreach (long v in t)
        {
            if (v == long.MinValue) continue;
            if (v < gi.MinTicks) gi.MinTicks = v;
            if (v > gi.MaxTicks) gi.MaxTicks = v;
        }
        gi.Known = true;
    }

    /// <summary>Reads a column; <paramref name="set"/> gets each value, <paramref name="none"/> each null row.</summary>
    private static void Read<T>(ParquetRowGroupReader rg, DataField f, int n, Action<int, T> set, Action<int> none) where T : struct
    {
        if (f.IsNullable)
        {
            var buf = new T?[n];
            Run(() => rg.ReadAsync<T>(f, buf.AsMemory()).AsTask());
            for (int i = 0; i < n; i++) { if (buf[i] is T v) set(i, v); else none(i); }
        }
        else
        {
            var buf = new T[n];
            Run(() => rg.ReadAsync<T>(f, buf.AsMemory()).AsTask());
            for (int i = 0; i < n; i++) set(i, buf[i]);
        }
    }

    private static void ReadNumber<T>(ParquetRowGroupReader rg, DataField f, int n, double[] v, bool[] has) where T : struct, INumberBase<T>
        => Read<T>(rg, f, n, (i, x) => { v[i] = double.CreateTruncating(x); has[i] = true; }, i => has[i] = false);

    private static void ReadValues(ParquetRowGroupReader rg, DataField f, int n, double[] v, bool[] has)
    {
        Type t = f.ClrType;
        if (t == typeof(double)) ReadNumber<double>(rg, f, n, v, has);
        else if (t == typeof(float)) ReadNumber<float>(rg, f, n, v, has);
        else if (t == typeof(decimal)) ReadNumber<decimal>(rg, f, n, v, has);
        else if (t == typeof(long)) ReadNumber<long>(rg, f, n, v, has);
        else if (t == typeof(ulong)) ReadNumber<ulong>(rg, f, n, v, has);
        else if (t == typeof(int)) ReadNumber<int>(rg, f, n, v, has);
        else if (t == typeof(uint)) ReadNumber<uint>(rg, f, n, v, has);
        else if (t == typeof(short)) ReadNumber<short>(rg, f, n, v, has);
        else if (t == typeof(ushort)) ReadNumber<ushort>(rg, f, n, v, has);
        else if (t == typeof(sbyte)) ReadNumber<sbyte>(rg, f, n, v, has);
        else if (t == typeof(byte)) ReadNumber<byte>(rg, f, n, v, has);
        else Read<bool>(rg, f, n, (i, x) => { v[i] = x ? 1 : 0; has[i] = true; }, i => has[i] = false);
    }

    // ------------------------------------------------------------------ lookup / read

    public int FindParameter(string name, out int nameList)
    {
        nameList = 0;
        string n = name.Trim().Trim('\'').ToLowerInvariant();
        if (n.Length > 1 && n[0] == '@' && int.TryParse(n.AsSpan(1), out int at))
            return at >= 0 && at < _parameters.Count ? at : -1;
        for (int list = 0; list < 3; list++)
        {
            for (int i = 0; i < _parameters.Count; i++)
            {
                string? s = _parameters[i].Names[list];
                if (s != null && s.Trim().ToLowerInvariant() == n)
                {
                    nameList = list == 2 ? -1 : list;
                    return i;
                }
            }
        }
        return -1;
    }

    public ParameterData? Read(ParameterRequest request)
    {
        lock (_lock)
        {
            int loc = FindParameter(request.Name, out int nameList);
            if (loc < 0) throw new DataAccessException(SunError.PidNotFound);
            double start = request.Start, stop = request.Stop;
            if (!(stop - start >= 0.0)) throw new DataAccessException(SunError.TimeSlice);
            var col = _columns[loc];
            if (!col.HasData) throw new NoDataException();

            long refTicks = request.RefUtc.HasValue ? TicksOf(request.RefUtc.Value) : _refTicks;
            double off = request.RefTime ?? 0.0;
            long ws = AddSeconds(refTicks, start + off);
            long we = AddSeconds(refTicks, stop + off);
            bool extrap = _owner.Options.AllowExtrapolation;

            // row groups overlapping the window, plus one on each side for the lead-in / trailing point
            var sel = new List<int>();
            for (int g = 0; g < _groups.Length; g++)
            {
                var gi = _groups[g];
                if (gi.Rows == 0) continue;
                if (_unsorted || (gi.MaxTicks >= ws && gi.MinTicks <= we)) sel.Add(g);
            }
            if (extrap && sel.Count > 0 && !_unsorted)
            {
                if (sel[0] > 0) sel.Insert(0, sel[0] - 1);
                if (sel[^1] < _groups.Length - 1) sel.Add(sel[^1] + 1);
            }
            if (sel.Count == 0)
            {
                // window beyond the data: same error as MULTITIME
                throw new DataAccessException(SunError.TimeSlice);
            }

            var ticks = new List<long>();
            var vals = new List<double>();
            foreach (int g in sel)
            {
                var t = GroupTimes(g);
                int n = t.Length;
                var v = new double[n];
                var has = new bool[n];
                using (var rg = _reader.OpenRowGroupReader(g)) ReadValues(rg, col.Field, n, v, has);
                for (int i = 0; i < n; i++)
                {
                    if (!has[i] || t[i] == long.MinValue) continue;
                    ticks.Add(t[i]);
                    vals.Add(v[i]);
                }
            }
            if (ticks.Count == 0)
            {
                if (sel.Count == _groups.Count(x => x.Rows > 0)) { col.HasData = false; throw new NoDataException(); }
                throw new DataAccessException(SunError.TimeSlice);
            }
            long[] tk = ticks.ToArray();
            double[] vv = vals.ToArray();
            if (_unsorted) SortStable(tk, vv);

            if (we < tk[0] || ws > tk[^1]) throw new DataAccessException(SunError.TimeSlice);
            int si, ei;
            if (extrap)
            {
                si = LastAtOrBefore(tk, ws); if (si < 0) si = 0;
                ei = FirstAtOrAfter(tk, we); if (ei < 0) ei = tk.Length - 1;
            }
            else
            {
                si = FirstAtOrAfter(tk, ws);
                ei = LastAtOrBefore(tk, we);
                if (si < 0 || ei < 0 || si > ei) throw new DataAccessException(SunError.TimeSlice);
            }
            return Build(request, col, loc, nameList, tk, vv, si, ei, refTicks, off, start, stop);
        }
    }

    private ParameterData Build(ParameterRequest request, Column col, int loc, int nameList, long[] tk, double[] vv,
        int si, int ei, long refTicks, double off, double start, double stop)
    {
        int np = ei - si + 1;
        int cap = np + 1; // room for the EOF hold point
        bool dbl = request.ReturnType == DataType.Double;
        var time = new float[cap];
        var utcTicks = new long[cap];
        var fv = dbl ? null : new float[cap];
        var dv = dbl ? new double[cap] : null;
        for (int i = 0; i < np; i++)
        {
            long t = tk[si + i];
            utcTicks[i] = t;
            time[i] = (float)((t - refTicks) / (double)TimeSpan.TicksPerSecond - off);
            double x = vv[si + i];
            if (dbl) dv![i] = double.IsNaN(x) ? DataTypes.DpDropOut : x;
            else fv![i] = double.IsNaN(x) ? DataTypes.FpDropOut : (float)x;
        }

        // same end-point treatment as the MULTITIME reader
        if (time[0] < start)
        {
            time[0] = (float)start;
            utcTicks[0] = AddSeconds(refTicks, start + off);
        }
        int last = np - 1;
        if (time[last] > stop)
        {
            if (np >= 2) { if (dbl) dv![last] = dv[last - 1]; else fv![last] = fv[last - 1]; }
            else { if (dbl) dv![last] = 0; else fv![last] = 0; }
            time[last] = (float)stop;
            utcTicks[last] = AddSeconds(refTicks, stop + off);
        }
        // hold the last value until the EOF event
        if (time[last] < stop)
        {
            foreach (var ev in _events)
            {
                if (ev.Name != "EOF") continue;
                long evTicks = TicksOf(ev.Utc);
                double d = Math.Min((evTicks - refTicks) / (double)TimeSpan.TicksPerSecond - off, stop);
                if (d > time[last])
                {
                    np++;
                    time[np - 1] = (float)d;
                    utcTicks[np - 1] = AddSeconds(refTicks, d + off);
                    if (dbl) dv![np - 1] = dv[np - 2]; else fv![np - 1] = fv[np - 2];
                }
                break;
            }
        }

        var info = col.Info;
        var p = new ParameterData
        {
            Source = this,
            Name = (nameList == 1 ? info.Names[1] : nameList == -1 ? info.Names[2] : info.Names[0]) ?? info.Name,
            Units = info.Units,
            Description = nameList == -1 && info.Descriptions[1] != null ? info.Descriptions[1]! : info.Description,
            Type = dbl ? DataType.Double : DataType.Float,
            Points = np,
            Time = time,
            Data = dbl ? dv! : fv!,
            RefTime = request.RefTime,
            RefUtc = request.RefUtc ?? Info.RefUtc,
            SequenceNumber = info.SequenceNumber,
            MapPosition = loc,
            CommonTimeIndex = false,
        };
        if (request.ReturnUtc)
        {
            var g = new TimeTag[cap];
            for (int i = 0; i < np; i++) g[i] = TagOf(utcTicks[i]);
            p.Utc = g;
            p.UtcOffset = 0;
            p.UtcPoints = np;
        }
        var nomen = col.IndexNomen.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (nomen.Length >= 3) { p.TimeName = nomen[0]; p.TimeUnits = nomen[1]; p.TimeDescription = nomen[2]; }

        // min / max ignoring dropouts
        int iMin = -1, iMax = -1;
        for (int i = 0; i < np; i++)
        {
            if (p.IsDropOut(i)) continue;
            double x = p.GetDouble(i);
            if (iMin < 0 || x < p.GetDouble(iMin)) iMin = i;
            if (iMax < 0 || x > p.GetDouble(iMax)) iMax = i;
        }
        p.MinIndex = Math.Max(iMin, 0);
        p.MaxIndex = Math.Max(iMax, 0);
        p.MinValue = iMin >= 0 ? p.GetDouble(iMin) : 0;
        p.MaxValue = iMax >= 0 ? p.GetDouble(iMax) : 0;

        p.Rate = info.Rate;
        if (info.Rate == -1f)
        {
            int u = TimeMath.Location(0.0, time, np, 0);
            if (u > np - 2) u = 0;
            if (u < np - 1)
            {
                double dt = time[u + 1] - time[u];
                if (dt > 0.0) p.Rate = (float)(1.0 / dt);
            }
        }
        return p;
    }

    private static int FirstAtOrAfter(long[] t, long x)
    {
        int i = Array.BinarySearch(t, x);
        if (i >= 0) { while (i > 0 && t[i - 1] == x) i--; return i; }
        i = ~i;
        return i < t.Length ? i : -1;
    }

    private static int LastAtOrBefore(long[] t, long x)
    {
        int i = Array.BinarySearch(t, x);
        if (i >= 0) { while (i < t.Length - 1 && t[i + 1] == x) i++; return i; }
        return ~i - 1;
    }

    private static void SortStable(long[] t, double[] v)
    {
        var idx = Enumerable.Range(0, t.Length).ToArray();
        Array.Sort(idx, (a, b) => t[a] != t[b] ? t[a].CompareTo(t[b]) : a.CompareTo(b));
        var t2 = idx.Select(i => t[i]).ToArray();
        var v2 = idx.Select(i => v[i]).ToArray();
        t2.CopyTo(t, 0);
        v2.CopyTo(v, 0);
    }
}
