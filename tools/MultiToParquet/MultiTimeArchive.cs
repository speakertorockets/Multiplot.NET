// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

using System.Buffers.Binary;
using System.Text;
using Multiplot.Core;
using Multiplot.Plugins;
using Multiplot.Sun;

namespace MultiToParquet;

/// <summary>One parameter's complete series at full stored precision.</summary>
public sealed class ParameterSeries
{
    public required ParameterInfo Info { get; init; }
    /// <summary>Two-character map type id after lower-casing ("f4", "d8", ...).</summary>
    public required string TypeId { get; init; }
    /// <summary>Absolute time stamps as .NET ticks (100 ns since 0001-01-01 UTC).</summary>
    public long[] Ticks { get; set; } = Array.Empty<long>();
    /// <summary>Values converted to double; dropouts are NaN.</summary>
    public double[] Values { get; set; } = Array.Empty<double>();
    public int Blocks { get; set; }
    public int CompressedBlocks { get; set; }
    /// <summary>Index types seen in this parameter's blocks (DT codes).</summary>
    public SortedSet<int> IndexTypes { get; } = new();
    /// <summary>Time/index axis nomenclature from the first block ("name,units,description"), or empty.</summary>
    public string IndexNomen { get; set; } = "";
    public int Dropouts { get; set; }
    public int OutOfOrder { get; set; }
    public int DuplicateTimes { get; set; }
    /// <summary>Why the parameter is not exportable as double values (null = exportable).</summary>
    public string? SkipReason { get; set; }
    /// <summary>Integer values whose magnitude exceeds 2^53 (not exact as double).</summary>
    public int InexactIntegers { get; set; }
    /// <summary>Packed-time samples whose decoded Julian day is outside 1..366 (packed day cannot cross a year end).</summary>
    public int BadPackedDays { get; set; }
}

/// <summary>
/// Reads a SUN_MULTITIME / SUN_MULTITIME64 file block by block. Header, map, comment and events come from
/// the shared <see cref="SunFile"/> reader; the data blocks are walked here so that the time stamps keep their
/// full stored precision (the application read path returns float32 relative seconds) and so that no
/// display-oriented points are synthesized (EOF hold value, start/stop clamping, full-rate rebuild).
/// </summary>
public sealed class MultiTimeArchive : IDisposable
{
    private const int HeaderSize = 264;

    public SunFile File { get; }
    public string Path { get; }
    public bool Is64 { get; }
    /// <summary>Calendar year of the header reference time (refTime[0]).</summary>
    public int RefYear { get; }
    /// <summary>Calendar year used for the header reference time.</summary>
    public int HeaderYear { get; }
    /// <summary>Effective reference time (header, or FILE_REF event) as ticks.</summary>
    public long RefTicks { get; }
    public List<string> Warnings { get; } = new();
    private readonly long[] _blockPtr;

    private MultiTimeArchive(SunFile file, string path, int? yearOverride)
    {
        File = file;
        Path = path;
        uint sig = file.BaseSignature;
        if (!SunSignatures.IsMultiTime(sig))
            throw new InvalidDataException($"{SunSignatures.Describe(file.Info.FileSig)} is not a MULTITIME format");
        Is64 = sig == SunSignatures.MultiTime64;

        int headerYear = (int)file.Info.RefTime[0];
        if (yearOverride.HasValue) headerYear = yearOverride.Value;
        if (headerYear < 1 || headerYear > 9998)
            throw new InvalidDataException($"header reference year {file.Info.RefTime[0]} is not usable; pass --year");
        // FILE_REF may move the reference across a year boundary relative to the header day
        HeaderYear = headerYear;
        var headerJd = (short)Math.Max(1u, file.Info.RefTime[1]);
        RefYear = headerYear + YearWrap(file.Info.RefUtc.JulianDay - headerJd);
        RefTicks = ToTicks(RefYear, file.Info.RefUtc);
        _blockPtr = ReadMapBlockPointers();
    }

    public static MultiTimeArchive Open(string path, int? yearOverride = null)
    {
        var f = SunFile.Open(path, new SunLibraryOptions { AllowExtrapolation = false, MakeFullRate = false });
        try { return new MultiTimeArchive(f, System.IO.Path.GetFullPath(path), yearOverride); }
        catch { f.Dispose(); throw; }
    }

    public void Dispose() => File.Dispose();

    /// <summary>Same ±180 day rule as <see cref="TimeMath.Time"/>: a day difference of 180 or more means the other year.</summary>
    private static int YearWrap(int dayDiff) => dayDiff >= 180 ? -1 : dayDiff <= -180 ? 1 : 0;

    /// <summary>Ticks of a year-less TimeTag in the given year (day 1 = Jan 1).</summary>
    public static long ToTicks(int year, TimeTag t) =>
        new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks
        + (t.JulianDay - 1L) * TimeSpan.TicksPerDay
        + t.MilliSeconds * TimeSpan.TicksPerMillisecond
        + t.MicroSeconds * 10L;

    /// <summary>Ticks of a TimeTag, resolving its year against the reference.</summary>
    public long TagTicks(TimeTag t) => ToTicks(RefYear + YearWrap(t.JulianDay - File.Info.RefUtc.JulianDay), t);

    private long SecondsToTicks(double seconds) => RefTicks + (long)Math.Round(seconds * TimeSpan.TicksPerSecond);

    // ------------------------------------------------------------------ map

    private long[] ReadMapBlockPointers()
    {
        int n = File.Parameters.Count;
        var ptr = new long[n];
        using var fs = OpenStream();
        fs.Position = HeaderSize;
        Span<byte> t = stackalloc byte[14];
        for (int i = 0; i < n; i++)
        {
            for (int k = 0; k < 4; k++)
            {
                int len = fs.ReadByte();
                if (len < 0) throw new InvalidDataException("file map truncated");
                fs.Seek(len, SeekOrigin.Current);
            }
            if (fs.Read(t) != 14) throw new InvalidDataException("file map truncated");
            ptr[i] = Is64 ? BinaryPrimitives.ReadInt64BigEndian(t[6..]) : BinaryPrimitives.ReadUInt32BigEndian(t[6..]);
        }
        return ptr;
    }

    private FileStream OpenStream() =>
        new(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan);

    // ------------------------------------------------------------------ blocks

    private struct Head
    {
        public uint Points, TimeLzh, DataLzh;
        public byte IndexType, NomenSize, Compressed, BigEndian;
        public TimeTag FirstTag;
        public long DataAddr;
    }

    /// <summary>Reads every block of parameter <paramref name="mapPos"/>.</summary>
    public ParameterSeries ReadParameter(int mapPos)
    {
        var info = File.Parameters[mapPos];
        string typeId = info.ExtendedType ? info.TypeId : "f4";
        var s = new ParameterSeries { Info = info, TypeId = typeId };
        DataType dt = DataTypes.FromId(typeId);
        s.SkipReason = dt switch
        {
            DataType.Variable => "variable-size blob (v1) has no numeric value",
            DataType.TimeTag => "time-tag data (t8) is not a scalar value",
            DataType.Image => $"type '{typeId}' has no numeric conversion",
            _ => null,
        };

        var chunks = new List<(long[] T, double[] V)>();
        using var fs = OpenStream();
        long addr = _blockPtr[mapPos];
        var seen = new HashSet<long>();
        string nomen = "";
        int hs = Is64 ? 40 : 36;
        Span<byte> hb = stackalloc byte[40];
        while (addr != 0)
        {
            if (!Is64 && s.Blocks == 0 && addr >= 0xFFFF0000L)
            {
                // 32-bit file packed at close: a null map pointer had the pack distance subtracted (wraps to 0xFFFFxxxx)
                Warnings.Add($"{info.Name}: map pointer 0x{addr:X} is a wrapped null pointer (32-bit packing defect); no data");
                break;
            }
            if (addr < 0 || addr + hs > fs.Length || !seen.Add(addr))
            {
                Warnings.Add($"{info.Name}: bad or cyclic block link 0x{addr:X}; chain truncated");
                break;
            }
            fs.Position = addr;
            fs.ReadExactly(hb[..hs]);
            var h = new Head { Points = BinaryPrimitives.ReadUInt32BigEndian(hb) };
            int o = 4;
            long next;
            if (Is64) { next = BinaryPrimitives.ReadInt64BigEndian(hb[o..]); o += 8; }
            else { next = BinaryPrimitives.ReadUInt32BigEndian(hb[o..]); o += 4; }
            h.TimeLzh = BinaryPrimitives.ReadUInt32BigEndian(hb[o..]); o += 4;
            h.DataLzh = BinaryPrimitives.ReadUInt32BigEndian(hb[o..]); o += 4;
            h.IndexType = hb[o++]; h.NomenSize = hb[o++]; h.Compressed = hb[o++]; h.BigEndian = hb[o++];
            var first = hb.Slice(o, 8);
            h.FirstTag = new TimeTag(BinaryPrimitives.ReadInt16BigEndian(first[6..]), BinaryPrimitives.ReadUInt32BigEndian(first), BinaryPrimitives.ReadUInt16BigEndian(first[4..]));
            if (h.NomenSize != 0)
            {
                var nb = new byte[h.NomenSize];
                fs.ReadExactly(nb);
                int l = Array.IndexOf(nb, (byte)0);
                if (nomen.Length == 0) nomen = AnsiText.DecodeAnsi(nb.AsSpan(0, l < 0 ? nb.Length : l));
            }
            h.DataAddr = fs.Position;
            s.Blocks++;
            if (h.Compressed != 0) s.CompressedBlocks++;
            s.IndexTypes.Add(h.IndexType);
            if (s.SkipReason == null) DecodeBlock(fs, h, typeId, chunks, s);
            addr = next;
        }
        s.IndexNomen = nomen;
        if (chunks.Count == 1) (s.Ticks, s.Values) = chunks[0];
        else if (chunks.Count > 1)
        {
            long total = chunks.Sum(c => (long)c.T.Length);
            if (total > Array.MaxLength) throw new InvalidDataException($"{info.Name}: {total:N0} points exceed one array");
            s.Ticks = new long[total];
            s.Values = new double[total];
            int at = 0;
            foreach (var (t, v) in chunks)
            {
                t.CopyTo(s.Ticks, at);
                v.CopyTo(s.Values, at);
                at += t.Length;
            }
        }
        for (int i = 1; i < s.Ticks.Length; i++)
        {
            if (s.Ticks[i] < s.Ticks[i - 1]) s.OutOfOrder++;
            else if (s.Ticks[i] == s.Ticks[i - 1]) s.DuplicateTimes++;
        }
        if (s.OutOfOrder > 0)
        {
            // keep the original order of equal time stamps (stable sort)
            var idx = Enumerable.Range(0, s.Ticks.Length).ToArray();
            var keys = s.Ticks;
            Array.Sort(idx, (a, b) => keys[a] != keys[b] ? keys[a].CompareTo(keys[b]) : a.CompareTo(b));
            s.Ticks = idx.Select(i => keys[i]).ToArray();
            var v = s.Values;
            s.Values = idx.Select(i => v[i]).ToArray();
        }
        return s;
    }

    private void DecodeBlock(FileStream fs, Head h, string typeId, List<(long[] T, double[] V)> chunks, ParameterSeries s)
    {
        int n = (int)h.Points;
        if (n == 0) return;
        int iSz = (DataType)h.IndexType switch
        {
            DataType.ULongInt or DataType.PackedTime or DataType.Float => 4,
            DataType.TimeTag or DataType.Double => 8,
            _ => throw new InvalidDataException($"{s.Info.Name}: unknown index type {h.IndexType}"),
        };
        int dSz = DataTypes.SizeFromId(typeId);
        long tLen = (long)n * iSz, dLen = (long)n * dSz;
        if (tLen > int.MaxValue || dLen > int.MaxValue || h.DataAddr + (h.Compressed != 0 ? h.TimeLzh + h.DataLzh : tLen + dLen) > fs.Length)
            throw new InvalidDataException($"{s.Info.Name}: block at 0x{h.DataAddr:X} exceeds the file");

        var tb = new byte[tLen];
        var db = new byte[dLen];
        fs.Position = h.DataAddr;
        if (h.Compressed != 0)
        {
            if (new LzhDecoder().Decode(fs, h.TimeLzh, tb, 0, 0, tLen) != SunError.Success)
                throw new InvalidDataException($"{s.Info.Name}: corrupt compressed time stream");
            fs.Position = h.DataAddr + h.TimeLzh;
            if (new LzhDecoder().Decode(fs, h.DataLzh, db, 0, 0, dLen) != SunError.Success)
                throw new InvalidDataException($"{s.Info.Name}: corrupt compressed data stream");
        }
        else
        {
            fs.ReadExactly(tb);
            fs.ReadExactly(db);
        }

        var bt = new long[n];
        var bv = new double[n];
        bool be = h.BigEndian != 0, delta = h.Compressed != 0;
        ReadOnlySpan<byte> t = tb;
        switch ((DataType)h.IndexType)
        {
            case DataType.Float:
                {
                    double sum = 0;
                    for (int i = 0; i < n; i++)
                    {
                        float v = be ? BinaryPrimitives.ReadSingleBigEndian(t[(i * 4)..]) : BinaryPrimitives.ReadSingleLittleEndian(t[(i * 4)..]);
                        if (delta) { sum = i == 0 ? v : sum + v; v = (float)sum; }
                        bt[i] = SecondsToTicks(v);
                    }
                    break;
                }
            case DataType.Double:
                {
                    double prev = 0;
                    for (int i = 0; i < n; i++)
                    {
                        double v = be ? BinaryPrimitives.ReadDoubleBigEndian(t[(i * 8)..]) : BinaryPrimitives.ReadDoubleLittleEndian(t[(i * 8)..]);
                        if (delta && i != 0) v += prev;
                        prev = v;
                        bt[i] = SecondsToTicks(v);
                    }
                    break;
                }
            case DataType.ULongInt:
                {
                    // milliseconds since the reference (the SUN reader drops the reference microseconds here)
                    long baseTicks = RefTicks - File.Info.RefUtc.MicroSeconds * 10L;
                    for (int i = 0; i < n; i++)
                    {
                        uint v = be ? BinaryPrimitives.ReadUInt32BigEndian(t[(i * 4)..]) : BinaryPrimitives.ReadUInt32LittleEndian(t[(i * 4)..]);
                        bt[i] = baseTicks + v * TimeSpan.TicksPerMillisecond;
                    }
                    break;
                }
            case DataType.TimeTag:
                {
                    TimeTag prev = default;
                    for (int i = 0; i < n; i++)
                    {
                        var e = t[(i * 8)..];
                        TimeTag g = be
                            ? new TimeTag(BinaryPrimitives.ReadInt16BigEndian(e[6..]), BinaryPrimitives.ReadUInt32BigEndian(e), BinaryPrimitives.ReadUInt16BigEndian(e[4..]))
                            : new TimeTag(BinaryPrimitives.ReadInt16LittleEndian(e[6..]), BinaryPrimitives.ReadUInt32LittleEndian(e), BinaryPrimitives.ReadUInt16LittleEndian(e[4..]));
                        if (delta && i != 0)
                        {
                            g.JulianDay = unchecked((short)(g.JulianDay + prev.JulianDay));
                            g.MilliSeconds = unchecked(g.MilliSeconds + prev.MilliSeconds);
                            g.MicroSeconds = unchecked((ushort)(g.MicroSeconds + prev.MicroSeconds));
                        }
                        prev = g;
                        bt[i] = TagTicks(g);
                    }
                    break;
                }
            case DataType.PackedTime:
                {
                    uint packed = 0;
                    int saveDay = 0, wrap = 0;
                    for (int i = 0; i < n; i++)
                    {
                        uint v = be ? BinaryPrimitives.ReadUInt32BigEndian(t[(i * 4)..]) : BinaryPrimitives.ReadUInt32LittleEndian(t[(i * 4)..]);
                        packed = delta && i != 0 ? unchecked(packed + v) : v;
                        int day = (int)(packed >> 27);
                        if (i == 0) saveDay = day;
                        else if (day < saveDay) { wrap += 32; saveDay = day; }
                        var g = new TimeTag(unchecked((short)(day + h.FirstTag.JulianDay + wrap)), packed & 0x07FFFFFF, 0);
                        if (g.JulianDay is < 1 or > 366) s.BadPackedDays++;
                        bt[i] = TagTicks(g);
                    }
                    break;
                }
        }

        ReadOnlySpan<byte> d = db;
        DataType dt = DataTypes.FromId(typeId);
        for (int i = 0; i < n; i++)
        {
            var e = d[(i * dSz)..];
            double v;
            switch (dt)
            {
                case DataType.Float:
                    {
                        uint bits = be ? BinaryPrimitives.ReadUInt32BigEndian(e) : BinaryPrimitives.ReadUInt32LittleEndian(e);
                        if (bits == DataTypes.DropOutBits) { v = double.NaN; s.Dropouts++; }
                        else v = BitConverter.UInt32BitsToSingle(bits);
                        break;
                    }
                case DataType.Double:
                    v = be ? BinaryPrimitives.ReadDoubleBigEndian(e) : BinaryPrimitives.ReadDoubleLittleEndian(e);
                    if (DataTypes.IsDropOut(v)) { v = double.NaN; s.Dropouts++; }
                    break;
                case DataType.ShortInt: v = be ? BinaryPrimitives.ReadInt16BigEndian(e) : BinaryPrimitives.ReadInt16LittleEndian(e); break;
                case DataType.UShortInt: v = be ? BinaryPrimitives.ReadUInt16BigEndian(e) : BinaryPrimitives.ReadUInt16LittleEndian(e); break;
                case DataType.LongInt:
                    {
                        int x = be ? BinaryPrimitives.ReadInt32BigEndian(e) : BinaryPrimitives.ReadInt32LittleEndian(e);
                        if (unchecked((uint)x) == DataTypes.DropOutBits) { v = double.NaN; s.Dropouts++; } else v = x;
                        break;
                    }
                case DataType.ULongInt:
                    {
                        uint x = be ? BinaryPrimitives.ReadUInt32BigEndian(e) : BinaryPrimitives.ReadUInt32LittleEndian(e);
                        if (x == DataTypes.DropOutBits) { v = double.NaN; s.Dropouts++; } else v = x;
                        break;
                    }
                case DataType.LongLong:
                    {
                        long x = be ? BinaryPrimitives.ReadInt64BigEndian(e) : BinaryPrimitives.ReadInt64LittleEndian(e);
                        if (Math.Abs((double)x) > 9007199254740992.0) s.InexactIntegers++;
                        v = x;
                        break;
                    }
                case DataType.ULongLong:
                    {
                        ulong x = be ? BinaryPrimitives.ReadUInt64BigEndian(e) : BinaryPrimitives.ReadUInt64LittleEndian(e);
                        if (x > 9007199254740992UL) s.InexactIntegers++;
                        v = x;
                        break;
                    }
                case DataType.Byte: v = e[0]; break;
                default: v = double.NaN; break;
            }
            bv[i] = v;
        }
        chunks.Add((bt, bv));
    }

    /// <summary>Human-readable form of a ticks value.</summary>
    public static string Iso(long ticks) => new DateTime(ticks, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'");

    /// <summary>Decoded type name for reports.</summary>
    public static string IndexTypeName(int t) => (DataType)t switch
    {
        DataType.Float => "f4 rel. seconds",
        DataType.Double => "d8 rel. seconds",
        DataType.ULongInt => "u4 rel. ms",
        DataType.TimeTag => "t8 TimeTag",
        DataType.PackedTime => "packed time",
        _ => $"unknown({t})",
    };
}
