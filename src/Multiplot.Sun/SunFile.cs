// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

using System.Buffers.Binary;
using Multiplot.Core;
using Multiplot.Plugins;

namespace Multiplot.Sun;

/// <summary>
/// Managed reader for the SUN file family (chapter 03): COMPRESSED, UNCOMPRESSED, CIRCULAR,
/// COMMONTIME, MULTITIME, MULTITIME64 and the _SRT variants. Opens, reads and
/// refreshes files.
/// </summary>
public sealed class SunFile : IDataFile, ITimeIndexedFile
{
    internal const int HeaderSize = 264;
    private const int PHEADSIZE = 36;
    private const int PHEAD64SIZE = 40;

    private readonly SunLibraryOptions _opt;
    private readonly object _lock = new();
    private FileStream? _fs; // kept open for dynamic files

    // header
    private uint _sig;             // base format (SRT normalised)
    private readonly uint[] _refTime = new uint[6];
    private uint _items;
    private readonly uint[] _blockPoints = new uint[2];
    private float _cutoff;

    private ParmEntry[] _map = Array.Empty<ParmEntry>();
    private long _commentOffset;
    private uint _commentSize;
    private string? _comment;
    private List<FileEvent> _events = new();

    // file indexes (common-time formats)
    private float[]? _timeIndex;
    private int _timeIndexPoints;
    private TimeTag[]? _gmtIndex;
    private int _gmtIndexPoints;
    private float _rate;

    private MultiTimeLookup[]? _lookups;

    /// <inheritdoc />
    public DataFileInfo Info { get; } = new();

    /// <summary>The SUN provider options in effect.</summary>
    public SunLibraryOptions Options => _opt;

    /// <summary>Last error code of the most recent operation.</summary>
    public SunError LastError { get; private set; }

    internal sealed class ParmEntry
    {
        public readonly ParameterInfo Info = new();
        public string TypeIds = "f4";
        public bool Extended;
        public uint BlockPtr0, BlockPtr1;
        public long BlockPtr64;
        public uint BlockSize0, BlockSize1;
    }

    private struct BlockHeader
    {
        public uint Points;
        public long HeaderAddr;
        public long DataAddr;
        public uint TimeLzhSize, DataLzhSize;
        public byte IndexType, NomenSize, Compressed, BigEndian;
        public double FirstTime, LastTime;
        public TimeTag BaseGmt;
    }

    private sealed class MultiTimeLookup
    {
        public readonly List<BlockHeader> Heads = new();
        public long Pending;
        public bool Initialized;
        public string IndexNomen = "";
    }

    private SunFile(SunLibraryOptions opt) => _opt = opt;

    /// <summary>True when bytes 0..3 of the file carry a SUN signature.</summary>
    public static uint ProbeSignature(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        Span<byte> b = stackalloc byte[4];
        if (fs.Read(b) != 4) return 0;
        uint sig = BinaryPrimitives.ReadUInt32LittleEndian(b);
        return SunSignatures.IsSun(sig) ? sig : 0;
    }

    /// <summary>Opens a SUN file; throws <see cref="DataAccessException"/>.</summary>
    public static SunFile Open(string path, SunLibraryOptions options)
    {
        if (string.IsNullOrEmpty(path)) throw new DataAccessException(SunError.FileOpen);
        var f = new SunFile(options);
        FileStream fs;
        try
        {
            fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.RandomAccess);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new DataAccessException(SunError.FileOpen);
        }
        bool keep = false;
        try
        {
            f.Info.Path = Path.IsPathRooted(path) ? path : Path.GetFullPath(path);
            f.ReadHeaderAndMap(fs);
            keep = f.Info.IsDynamic;
            if (keep) f._fs = fs;
            f.LoadComment(fs);
            f.ParseEvents();
            foreach (var e in f._events)
            {
                if (e.Name == "FILE_REF")
                {
                    f.Info.RefGmt = e.Gmt;
                    break;
                }
            }
            f.ParseExtendedNomen();
            if (SunSignatures.IsMultiTime(f._sig)) f.ParseRateTable();
            return f;
        }
        finally
        {
            if (!keep) fs.Dispose();
        }
    }

    private static string StrnDup(ReadOnlySpan<byte> b, int max)
    {
        int l = 0;
        int lim = Math.Min(max, b.Length);
        while (l < lim && b[l] != 0) l++;
        if (l == 0) return " ";
        return AnsiText.DecodeAnsi(b[..l]);
    }

    private static int ReadFully(Stream s, Span<byte> buf)
    {
        int total = 0;
        while (total < buf.Length)
        {
            int n = s.Read(buf[total..]);
            if (n <= 0) break;
            total += n;
        }
        return total;
    }

    private void ReadHeaderAndMap(FileStream fs)
    {
        Span<byte> h = stackalloc byte[HeaderSize];
        h.Clear();
        fs.Position = 0;
        ReadFully(fs, h);
        uint rawSig = BinaryPrimitives.ReadUInt32LittleEndian(h);
        if (!SunSignatures.IsSun(rawSig)) throw new DataAccessException(SunError.FileSig);
        for (int j = 0; j < 6; j++) _refTime[j] = BinaryPrimitives.ReadUInt32BigEndian(h[(4 + j * 4)..]);
        if (_refTime[1] == 0) _refTime[1] = 1;
        _items = BinaryPrimitives.ReadUInt32BigEndian(h[28..]);
        _blockPoints[0] = BinaryPrimitives.ReadUInt32BigEndian(h[32..]);
        _blockPoints[1] = BinaryPrimitives.ReadUInt32BigEndian(h[36..]);
        _cutoff = BinaryPrimitives.ReadSingleBigEndian(h[40..]);

        Info.FileSig = rawSig;
        _sig = rawSig switch
        {
            SunSignatures.MultiTimeSrt => SunSignatures.MultiTime,
            SunSignatures.MultiTime64Srt => SunSignatures.MultiTime64,
            _ => rawSig,
        };
        Info.FormatDescription = SunSignatures.Describe(rawSig);
        Array.Copy(_refTime, Info.RefTime, 6);
        Info.RefGmt = HeaderRefGmt();
        Info.Items = _items;
        Info.Points = SunSignatures.IsMultiTime(rawSig) ? _blockPoints[1] : _blockPoints[0] + _blockPoints[1];
        Info.Cutoff = _cutoff;
        Info.Title = StrnDup(h[44..], 80);
        Info.Comment = StrnDup(h[124..], 80);
        Info.SerialNum = StrnDup(h[204..], 12);
        Info.TestStand = StrnDup(h[216..], 12);
        Info.TestPosition = StrnDup(h[228..], 12);
        Info.TestType = StrnDup(h[240..], 12);
        Info.TestNumber = StrnDup(h[252..], 12);

        fs.Position = HeaderSize;
        RetrieveFileMap(fs);
        _commentOffset = fs.Position;
        long fileLen = fs.Length;
        if (_items == 0)
        {
            _map = new[] { new ParmEntry() };
            _map[0].BlockPtr0 = (uint)fileLen;
        }

        long size;
        if (_sig == SunSignatures.MultiTime64)
            size = _map[0].BlockPtr64 == 0 ? (uint)fileLen - (uint)_commentOffset : (uint)(_map[0].BlockPtr64 - _commentOffset);
        else
            size = _map[0].BlockPtr0 == 0 ? (uint)((uint)fileLen - (uint)_commentOffset) : (uint)(_map[0].BlockPtr0 - (uint)_commentOffset);
        _commentSize = (uint)Math.Min(size, 67108864L);

        if (!SunSignatures.IsMultiTime(_sig) && _items != 0) CalcBlockSizes(fileLen);

        _rate = SunSignatures.IsMultiTime(_sig) ? 0f : -1f;
        Info.IsDynamic = rawSig switch
        {
            SunSignatures.Uncompressed or SunSignatures.Circular or SunSignatures.MultiTimeSrt or SunSignatures.MultiTime64Srt => true,
            SunSignatures.MultiTime or SunSignatures.MultiTime64 => _blockPoints[0] != _blockPoints[1],
            _ => false,
        };
    }

    private TimeTag HeaderRefGmt() => new(
        unchecked((short)_refTime[1]),
        unchecked((_refTime[2] * 3600000U) + (_refTime[3] * 60000U) + (_refTime[4] * 1000U) + _refTime[5]),
        0);

    private void RetrieveFileMap(Stream fs)
    {
        long cap = Math.Max(1, (fs.Length - HeaderSize) / 18 + 1);
        int n = (int)Math.Min(Math.Min(_items, 10_000_000u), cap);
        _items = (uint)n;
        var map = new ParmEntry[Math.Max(n, 1)];
        Span<byte> e = stackalloc byte[80];
        byte[] sBuf = new byte[256];
        for (int i = 0; i < n; i++)
        {
            var pe = new ParmEntry();
            pe.Info.MapPosition = i;
            switch (_sig)
            {
                case SunSignatures.Compressed:
                case SunSignatures.Uncompressed:
                case SunSignatures.Circular:
                    {
                        e.Clear();
                        ReadFully(fs, e);
                        uint seq = BinaryPrimitives.ReadUInt32BigEndian(e[68..]);
                        pe.BlockPtr0 = BinaryPrimitives.ReadUInt32BigEndian(e[72..]);
                        pe.BlockPtr1 = BinaryPrimitives.ReadUInt32BigEndian(e[76..]);
                        bool ext = e[56] == 0x00 && e[57] == 0xFF;
                        pe.Info.Names[0] = StrnDup(e[0..], 12);
                        pe.Info.Names[1] = StrnDup(e[12..], 12);
                        pe.Info.Units = StrnDup(e[60..], 8);
                        pe.Info.Descriptions[0] = StrnDup(e[24..], 36);
                        pe.Info.SequenceNumber = seq;
                        pe.Info.Rate = -1f;
                        pe.Extended = ext;
                        pe.TypeIds = ext ? new string(new[] { AsciiLower((char)e[58]), (char)e[59] }) : "f4";
                        break;
                    }
                default:
                    {
                        pe.Info.Names[0] = ReadVarString(fs, sBuf);
                        pe.Info.Names[1] = ReadVarString(fs, sBuf);
                        pe.Info.Descriptions[0] = ReadVarString(fs, sBuf);
                        pe.Info.Units = ReadVarString(fs, sBuf);
                        Span<byte> t = stackalloc byte[14];
                        t.Clear();
                        ReadFully(fs, t);
                        pe.TypeIds = new string(new[] { AsciiLower((char)t[0]), (char)t[1] });
                        pe.Extended = pe.TypeIds != "f4";
                        uint seq = BinaryPrimitives.ReadUInt32BigEndian(t[2..]);
                        pe.Info.SequenceNumber = seq;
                        if (_sig == SunSignatures.MultiTime64)
                            pe.BlockPtr64 = BinaryPrimitives.ReadInt64BigEndian(t[6..]);
                        else
                        {
                            pe.BlockPtr0 = BinaryPrimitives.ReadUInt32BigEndian(t[6..]);
                            pe.BlockPtr1 = BinaryPrimitives.ReadUInt32BigEndian(t[10..]);
                        }
                        pe.Info.Rate = (seq & 0x80000000u) == 0x80000000u ? (float)((seq >> 16) & 0x7fff) : -1f;
                        break;
                    }
            }
            pe.Info.TypeId = pe.TypeIds;
            pe.Info.ExtendedType = pe.Extended;
            map[i] = pe;
        }
        if (n == 0) map[0] = new ParmEntry();
        _map = map;
    }

    private static char AsciiLower(char c) => c >= 'A' && c <= 'Z' ? (char)(c + 32) : c;

    private static string ReadVarString(Stream fs, byte[] sBuf)
    {
        int len = fs.ReadByte();
        if (len < 0) len = 0;
        if (len > 0) ReadFully(fs, sBuf.AsSpan(0, len));
        return StrnDup(sBuf, len);
    }

    private void CalcBlockSizes(long fileLen)
    {
        int np = (int)Math.Min(_items, (uint)_map.Length);
        if (_map[0].BlockPtr1 != 0)
        {
            for (int i = 0; i < np - 1; i++)
            {
                _map[i].BlockSize0 = _map[i].BlockPtr1 - _map[i].BlockPtr0;
                _map[i].BlockSize1 = _map[i + 1].BlockPtr0 - _map[i].BlockPtr1;
            }
            _map[np - 1].BlockSize0 = _map[np - 1].BlockPtr1 - _map[np - 1].BlockPtr0;
            _map[np - 1].BlockSize1 = (uint)fileLen - _map[np - 1].BlockPtr1;
        }
        else
        {
            for (int i = 0; i < np - 1; i++)
                _map[i].BlockSize0 = _map[i].BlockSize1 = _map[i + 1].BlockPtr0 - _map[i].BlockPtr0;
            _map[np - 1].BlockSize0 = _map[np - 1].BlockSize1 = (uint)fileLen - _map[np - 1].BlockPtr0;
        }
    }

    // ------------------------------------------------------------------ comment block

    private void LoadComment(Stream fs)
    {
        _comment = null;
        if (_commentSize == 0) return;
        var buf = new byte[_commentSize];
        fs.Position = _commentOffset;
        if (ReadFully(fs, buf) != buf.Length) return; // fread(buf, size, 1) fails → no comment block
        int l = Array.IndexOf(buf, (byte)0);
        if (l < 0) l = buf.Length;
        _comment = AnsiText.DecodeAnsi(buf.AsSpan(0, l));
    }

    /// <inheritdoc />
    public string? CommentBlock => _comment;

    /// <summary>Comment block size in bytes as computed at open.</summary>
    public uint CommentSize => _commentSize;

    /// <summary>Comment block file offset.</summary>
    public long CommentOffset => _commentOffset;

    /// <summary>Returns the new count when it changed, else 0.</summary>
    private int ParseEvents()
    {
        int lastCount = _events.Count;
        var list = new List<FileEvent>();
        if (_comment != null)
        {
            int p = _comment.IndexOf("[events]", StringComparison.Ordinal);
            if (p >= 0)
            {
                var tok = new CStrtok(_comment, p + 8);
                var raw = new List<string>();
                string? s;
                while ((s = tok.Next("\n\r")) != null)
                {
                    s = s.TrimStart(' ');
                    if (s.StartsWith('[')) break;
                    int sc = s.IndexOf(';');
                    if (sc >= 0) s = s[..sc];
                    if (s.Contains('='))
                    {
                        raw.Add(s);
                        if (raw.Count > 4095) break;
                    }
                }
                foreach (string ev in raw)
                {
                    int eq = ev.IndexOf('=');
                    string name = ev[..eq].TrimEnd(' ');
                    TimeTag g = TimeParse.EventValue(ev[(eq + 1)..]);
                    if (!(g.JulianDay >= 1 && g.JulianDay <= 366)) name += "(invalid)";
                    list.Add(new FileEvent(name, g));
                }
            }
        }
        _events = list;
        return list.Count == lastCount ? 0 : list.Count;
    }

    private void ParseRateTable()
    {
        if (_comment == null) return;
        int p = _comment.IndexOf("[rates]", StringComparison.Ordinal);
        if (p < 0) return;
        var tok = new CStrtok(_comment, p);
        tok.Next("\n");
        string? lPtr = tok.Next(",");
        while (lPtr != null && lPtr.Length > 0 && lPtr[0] != '[')
        {
            string? nPtr = tok.Next(",");
            string? rPtr = tok.Next("\n");
            if (nPtr != null && rPtr != null)
            {
                int mapLoc = Crt.Atol(lPtr);
                if (mapLoc < 0) mapLoc = FindParameter(nPtr, out _);
                if (mapLoc >= 0 && mapLoc < (int)_items)
                {
                    _map[mapLoc].Info.Rate = (float)Math.Abs(Crt.Atof(rPtr));
                    _map[mapLoc].Info.SequenceNumber |= 0x80000000u;
                }
                lPtr = tok.Next(",");
            }
            else
                lPtr = null;
        }
    }

    private void ParseExtendedNomen()
    {
        if (_comment == null) return;
        int p = _comment.IndexOf("[extendednomen]", StringComparison.Ordinal);
        if (p < 0) return;
        var tok = new CStrtok(_comment, p);
        tok.Next("\n");
        string? s = tok.Next(",");
        while (s != null)
        {
            if (s.Length == 0 || s[0] == '[') break;
            string? nPtr = tok.Next(",");
            string? dPtr = tok.Next("\n");
            if (nPtr != null && dPtr != null)
            {
                uint pNo = unchecked((uint)Crt.Atol(s));
                if (pNo < _items && _map[pNo].Info.Names[2] == null)
                {
                    _map[pNo].Info.Names[2] = nPtr;
                    _map[pNo].Info.Descriptions[1] = dPtr;
                }
                s = tok.Next(",");
            }
            else
                s = null;
        }
    }

    // ------------------------------------------------------------------ IDataFile

    /// <inheritdoc />
    public IReadOnlyList<ParameterInfo> Parameters
    {
        get
        {
            int n = (int)Math.Min(_items, (uint)_map.Length);
            var l = new ParameterInfo[n];
            for (int i = 0; i < n; i++) l[i] = _map[i].Info;
            return l;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<FileEvent> Events => _events;

    /// <summary>Base (SRT normalised) format signature.</summary>
    public uint BaseSignature => _sig;

    /// <summary>Header block points (diagnostics).</summary>
    public IReadOnlyList<uint> BlockPoints => _blockPoints;

    /// <summary>File data rate (computed from the TIME index for common-time formats).</summary>
    public float FileRate
    {
        get
        {
            if (_rate < 0f && !SunSignatures.IsMultiTime(_sig) && _timeIndex == null)
                IndexEndTime();
            return _rate;
        }
    }

    /// <inheritdoc />
    public bool HasCommonTimeIndex => !SunSignatures.IsMultiTime(_sig);

    /// <inheritdoc />
    public double? IndexEndTime()
    {
        if (SunSignatures.IsMultiTime(_sig)) return null;
        lock (_lock)
        {
            if (_timeIndex == null)
            {
                Stream fs;
                bool owned;
                try { fs = AcquireStream(out owned); } catch (DataAccessException) { return null; }
                try { LoadIndex(fs, false); }
                catch (DataAccessException) { return null; }
                finally { if (owned) fs.Dispose(); }
            }
            return _timeIndexPoints > 0 ? _timeIndex![_timeIndexPoints - 1] : null;
        }
    }

    /// <inheritdoc />
    public bool TryIndexTimeAtGmt(TimeTag gmt, out double time)
    {
        time = 0;
        if (SunSignatures.IsMultiTime(_sig)) return false;
        lock (_lock)
        {
            Stream fs;
            bool owned;
            try { fs = AcquireStream(out owned); } catch (DataAccessException) { return false; }
            try
            {
                if (_gmtIndex == null) { try { LoadIndex(fs, true); } catch (DataAccessException) { } }
                if (_timeIndex == null) { try { LoadIndex(fs, false); } catch (DataAccessException) { } }
            }
            finally { if (owned) fs.Dispose(); }
            if (_gmtIndex == null || _timeIndex == null || _gmtIndexPoints != _timeIndexPoints) return false;
            int u = TimeMath.GmtLocation(0.0, _gmtIndex, 0, _gmtIndexPoints, gmt, -1);
            time = _timeIndex[u];
            return true;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_lock)
        {
            _fs?.Dispose();
            _fs = null;
        }
    }

    private Stream AcquireStream(out bool owned)
    {
        if (_fs != null) { owned = false; return _fs; }
        owned = true;
        try
        {
            return new FileStream(Info.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.RandomAccess);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new DataAccessException(SunError.FileOpen);
        }
    }

    /// <summary>Finds a parameter.</summary>
    public int FindParameter(string parmName, out int nameList)
    {
        nameList = 0;
        int location = -1;
        int s = 0;
        while (s < parmName.Length && (parmName[s] == ' ' || parmName[s] == '\'')) s++;
        char[] pName = parmName[s..].ToCharArray();
        int len = pName.Length;
        for (int j = len; j > 0; j--)
        {
            char c = j < len ? pName[j] : '\0';
            if (c == ' ' || c == '\'') { pName[j] = '\0'; c = '\0'; }
            if (c != '\0') break;
        }
        int z = Array.IndexOf(pName, '\0');
        string name = new string(pName, 0, z < 0 ? len : z);
        name = LowerAscii(name);

        int items = (int)Math.Min(_items, (uint)_map.Length);
        if (name.Length > 0 && name[0] == '@')
        {
            if (name.Length > 1 && char.IsAsciiDigit(name[1]))
                location = Crt.Atoi(name, 1);
            if (!(location < items)) location = -1;
            nameList = 0;
        }
        if (location < 0)
        {
            for (int i = 0; i < items; i++)
                if (MapName(_map[i].Info.Names[0]) == name) { location = i; nameList = 0; break; }
        }
        if (location < 0)
        {
            for (int i = 0; i < items; i++)
                if (MapName(_map[i].Info.Names[1]) == name) { location = i; nameList = 1; break; }
        }
        if (location < 0)
        {
            for (int i = 0; i < items; i++)
                if (_map[i].Info.Names[2] != null && MapName(_map[i].Info.Names[2]) == name) { location = i; nameList = -1; break; }
        }
        return location;
    }

    private static string MapName(string? s) => LowerAscii((s ?? "").TrimEnd(' '));

    private static string LowerAscii(string s)
    {
        Span<char> b = s.Length <= 512 ? stackalloc char[s.Length] : new char[s.Length];
        for (int i = 0; i < s.Length; i++) b[i] = AsciiLower(s[i]);
        return new string(b);
    }

    /// <inheritdoc />
    public ParameterData? Read(ParameterRequest request)
    {
        ushort rt = (ushort)request.ReturnType;
        if (request.ReturnGmt) rt |= DataTypes.ReturnGmtFlag;
        return ReadSunParm(request.Name, request.Start, request.Stop, request.RefTime, request.RefGmt, rt);
    }

    /// <summary>Throws <see cref="DataAccessException"/> on failure.</summary>
    public ParameterData ReadSunParm(string parmName, double startTime, double stopTime, double? refTIME, TimeTag? refGMT, ushort returnType)
    {
        lock (_lock)
        {
            LastError = SunError.Success;
            try
            {
                var p = ReadSunParmCore(parmName, startTime, stopTime, refTIME, refGMT, returnType);
                return p;
            }
            catch (DataAccessException ex)
            {
                LastError = ex.SunCode;
                throw;
            }
        }
    }

    private ParameterData ReadSunParmCore(string parmName, double startTime, double stopTime, double? refTIME, TimeTag? refGMT, ushort returnType)
    {
        if (!(stopTime - startTime >= 0.0)) throw new DataAccessException(SunError.TimeSlice);

        if (SunSignatures.IsMultiTime(_sig))
            return ReadMultiTimeParm(parmName, startTime, stopTime, refTIME, refGMT, returnType);

        returnType &= 0x00ff;
        if (Info.Points == 0) throw new DataAccessException(SunError.NullFile);

        Stream fs = AcquireStream(out bool owned);
        try
        {
            if (_sig == SunSignatures.Circular) RecheckCircular(fs);

            int loc = FindParameter(parmName, out int altname);
            if (loc < 0) throw new DataAccessException(SunError.PidNotFound);
            ParmEntry parmInfo = _map[loc];
            if (loc == 0 && refGMT.HasValue && _timeIndex == null)
            {
                try { LoadIndex(fs, false); }
                catch (DataAccessException) { throw new DataAccessException(SunError.BadRef); }
            }

            SunError warning = SunError.Success;
            if (refGMT.HasValue)
            {
                if (_gmtIndex == null)
                {
                    SunError gerr = SunError.Success;
                    try { LoadIndex(fs, true); }
                    catch (DataAccessException ex) { gerr = ex.SunCode; }
                    if (_gmtIndex == null)
                    {
                        if (gerr != SunError.RefreshTime)
                        {
                            if (_timeIndex == null)
                            {
                                try { LoadIndex(fs, false); } catch (DataAccessException) { }
                            }
                            if (_timeIndex != null)
                            {
                                refTIME = TimeMath.Time(refGMT.Value, Info.RefGmt);
                                refGMT = null;
                            }
                            else
                                throw new DataAccessException(SunError.TimeError);
                        }
                        else
                            throw new DataAccessException(SunError.RefreshTime);
                    }
                    if (_sig == SunSignatures.Circular && _timeIndex == null)
                    {
                        try { LoadIndex(fs, false); } catch (DataAccessException) { }
                    }
                }
            }
            else if (_timeIndex == null)
            {
                try { LoadIndex(fs, false); }
                catch (DataAccessException ex)
                {
                    throw new DataAccessException(ex.SunCode == SunError.RefreshTime ? SunError.RefreshTime : SunError.TimeError);
                }
            }

            // locate slice
            int startPoint, stopPoint;
            if (refGMT.HasValue)
            {
                var g = _gmtIndex!;
                int np = _gmtIndexPoints;
                var rg = refGMT.Value;
                startPoint = TimeMath.GmtLocation(startTime, g, 0, np, rg, -1);
                stopPoint = TimeMath.GmtLocation(stopTime, g, 0, np, rg, 1);
                if (startPoint == stopPoint)
                {
                    if (np == 1)
                    {
                        if (((startTime - TimeMath.Time(g[np - 1], rg)) > TimeMath.TimeTolerance)
                            || ((TimeMath.Time(g[0], rg) - stopTime) > TimeMath.TimeTolerance))
                            throw new DataAccessException(SunError.TimeSlice);
                    }
                    if (stopPoint == 0)
                    {
                        if ((TimeMath.Time(g[0], rg) - stopTime) > TimeMath.TimeTolerance)
                            throw new DataAccessException(SunError.TimeSlice);
                    }
                    else if (startPoint == np - 1)
                    {
                        if ((startTime - TimeMath.Time(g[np - 1], rg)) > TimeMath.TimeTolerance)
                            throw new DataAccessException(SunError.TimeSlice);
                    }
                }
            }
            else
            {
                double dtime = refTIME ?? 0.0;
                var ti = _timeIndex!;
                int np = _timeIndexPoints;
                startPoint = TimeMath.Location(startTime + dtime, ti, np, -1);
                stopPoint = TimeMath.Location(stopTime + dtime, ti, np, 1);
                if (startPoint > stopPoint) throw new DataAccessException(SunError.TimeSlice);
                if (startPoint == stopPoint)
                {
                    if (np == 1)
                    {
                        if ((((startTime + dtime) - ti[np - 1]) > TimeMath.TimeTolerance) || ((ti[0] - (stopTime + dtime)) > TimeMath.TimeTolerance))
                            throw new DataAccessException(SunError.TimeSlice);
                    }
                    if (stopPoint == 0)
                    {
                        if ((ti[0] - (stopTime + dtime)) > TimeMath.TimeTolerance)
                            throw new DataAccessException(SunError.TimeSlice);
                    }
                    else if (startPoint == np - 1)
                    {
                        if (((startTime + dtime) - ti[np - 1]) > TimeMath.TimeTolerance)
                            throw new DataAccessException(SunError.TimeSlice);
                    }
                }
            }
            if (startPoint > stopPoint) throw new DataAccessException(SunError.TimeSlice);

            int dataTypeSize = parmInfo.Extended ? DataTypes.SizeFromId(parmInfo.TypeIds) : 4;
            int tpoints = stopPoint - startPoint + 1;

            var p = new ParameterData { Source = this, CommonTimeIndex = true };
            string typeIds;
            Array data;
            if (loc != 0)
            {
                byte[] buf = ReadFixedData(fs, parmInfo, loc, startPoint, stopPoint, dataTypeSize);
                typeIds = parmInfo.Extended ? parmInfo.TypeIds : "f4";
                data = DecodeTyped(buf, tpoints, typeIds, true);
            }
            else
            {
                var f = new float[tpoints];
                Array.Copy(_timeIndex!, startPoint, f, 0, tpoints);
                data = f;
                typeIds = "f4";
            }

            p.IndexStart = startPoint;
            p.Points = tpoints;
            p.TimeShift = 0f;

            var time = new float[tpoints];
            if (refGMT.HasValue)
            {
                p.RefTime = null;
                p.RefGmt = refGMT;
                p.Gmt = _gmtIndex;
                p.GmtOffset = startPoint;
                p.GmtPoints = _gmtIndexPoints - startPoint;
                var g = _gmtIndex!;
                var rg = refGMT.Value;
                for (int i = 0; i < tpoints; i++)
                    time[i] = (float)TimeMath.Time(g[i + startPoint], rg);
                p.TimeName = "TIME"; p.TimeUnits = "Seconds"; p.TimeDescription = "Relative Time in Seconds";
            }
            else
            {
                p.RefGmt = null;
                p.RefTime = refTIME;
                float off = refTIME.HasValue ? (float)refTIME.Value : 0f;
                var ti = _timeIndex!;
                for (int i = 0; i < tpoints; i++)
                    time[i] = ti[i + startPoint] - off;
                p.TimeName = _map[0].Info.Name;
                p.TimeUnits = _map[0].Info.Units;
                p.TimeDescription = _map[0].Info.Description;
            }
            p.Time = time;

            DataType typeId = DataTypes.FromId(typeIds);
            p.Type = typeId;
            p.Data = data;
            if (returnType != 0 && returnType != (ushort)typeId)
                ConvertType(p, (DataType)returnType, dataTypeSize);

            SetNomen(p, parmInfo, altname);
            p.MapPosition = loc;
            p.Rate = _rate;
            bool filter = _opt.FilterInvalid && returnType != 0;
            p.SequenceNumber = parmInfo.Info.SequenceNumber;
            ParameterOps.ScanMinMax(p, filter);
            p.Warning = (int)warning;
            return p;
        }
        finally
        {
            if (owned) fs.Dispose();
        }
    }

    private void RecheckCircular(Stream fs)
    {
        Span<byte> b = stackalloc byte[8];
        fs.Position = 32;
        ReadFully(fs, b);
        uint s0 = BinaryPrimitives.ReadUInt32BigEndian(b);
        uint s1 = BinaryPrimitives.ReadUInt32BigEndian(b[4..]);
        if (s0 != _blockPoints[0] || s1 != _blockPoints[1])
        {
            _timeIndex = null;
            _gmtIndex = null;
            _blockPoints[0] = s0;
            _blockPoints[1] = s1;
            Info.Points = s0 + s1;
        }
    }

    private static void SetNomen(ParameterData p, ParmEntry pi, int altname)
    {
        switch (altname)
        {
            case 0:
                p.Name = pi.Info.Names[0] ?? " ";
                p.Description = pi.Info.Descriptions[0] ?? " ";
                break;
            case 1:
                p.Name = pi.Info.Names[1] ?? " ";
                p.Description = pi.Info.Descriptions[0] ?? " ";
                break;
            default:
                p.Name = pi.Info.Names[2] ?? " ";
                p.Description = pi.Info.Descriptions[1] ?? " ";
                break;
        }
        p.Units = pi.Info.Units;
    }

    /// <summary>Loads the TIME (position 0) or GMT (position 1, t8) index — the "@index" read.</summary>
    private void LoadIndex(Stream fs, bool gmt)
    {
        if (_sig == SunSignatures.Circular && _blockPoints[0] == _blockPoints[1])
            throw new DataAccessException(SunError.RefreshTime);
        int loc;
        if (!gmt) loc = 0;
        else
        {
            if (_items < 2 || !(_map[1].Extended && _map[1].TypeIds == "t8"))
                throw new DataAccessException(SunError.TimeRef);
            loc = 1;
        }
        if (Info.Points == 0) throw new DataAccessException(SunError.NullFile);
        ParmEntry pi = _map[loc];
        long stop = (long)_blockPoints[0] - 1;
        if (_sig == SunSignatures.Compressed || _sig == SunSignatures.CommonTime) stop += _blockPoints[1];
        if (stop < 0) throw new DataAccessException(SunError.TimeSlice);
        int tpoints = (int)(stop + 1);
        int size = pi.Extended ? DataTypes.SizeFromId(pi.TypeIds) : 4;
        byte[] buf = ReadFixedData(fs, pi, loc, 0, (int)stop, size);
        if (!gmt)
        {
            var t = (float[])DecodeTyped(buf, tpoints, "f4", true);
            if (_sig == SunSignatures.Compressed || _sig == SunSignatures.CommonTime)
                Delt2Lin(t, tpoints, _blockPoints[0]);
            _timeIndex = t;
            _timeIndexPoints = tpoints;
            int j = TimeMath.Location(0.0, t, tpoints, 0);
            if (j > (long)tpoints - 2) j = 0;
            if (j < (long)tpoints - 1)
            {
                float zdelta = t[j + 1] - t[j];
                if (zdelta > 0.0) _rate = 1.0f / zdelta;
            }
        }
        else
        {
            _gmtIndex = (TimeTag[])DecodeTyped(buf, tpoints, "t8", true);
            _gmtIndexPoints = tpoints;
        }
    }

    /// <summary>Delta to linear time.</summary>
    internal static void Delt2Lin(float[] x, int np, uint rp)
    {
        if (np == 0) return;
        double runsum = x[0];
        for (int p = 1; p < np; p++)
        {
            if (p == rp)
                runsum = (float)x[rp];
            else
            {
                runsum += (double)x[p];
                x[p] = (float)runsum;
            }
        }
    }

    /// <summary>Reads big-endian raw bytes of points [start, stop] for a fixed-time format parameter.</summary>
    private byte[] ReadFixedData(Stream fs, ParmEntry pi, int loc, int startPoint, int stopPoint, int size)
    {
        int tpoints = stopPoint - startPoint + 1;
        long bsize = (long)tpoints * size;
        if (bsize > int.MaxValue) throw new DataAccessException(SunError.MemAlloc14);
        var buf = new byte[bsize];
        if (_sig == SunSignatures.Compressed || _sig == SunSignatures.CommonTime)
        {
            GetBytePos((uint)startPoint, (uint)stopPoint, size, out uint[] strb, out uint[] stpb, out bool[] dblk);
            var dec = new LzhDecoder();
            for (int j = 0; j < 2; j++)
            {
                if (!dblk[j]) continue;
                int offset = (j == 1 && dblk[0]) ? (int)(stpb[0] - strb[0]) : 0;
                fs.Position = j == 0 ? pi.BlockPtr0 : pi.BlockPtr1;
                uint blk = j == 0 ? _map[loc].BlockSize0 : _map[loc].BlockSize1;
                var err = dec.Decode(fs, blk, buf, offset, strb[j], stpb[j]);
                if (err != SunError.Success) throw new DataAccessException(err);
            }
        }
        else if (_sig == SunSignatures.Uncompressed)
        {
            fs.Position = pi.BlockPtr0 + (long)startPoint * size;
            ReadFully(fs, buf);
        }
        else if (_sig == SunSignatures.Circular)
        {
            if (_blockPoints[1] == 0)
            {
                fs.Position = pi.BlockPtr0 + (long)startPoint * size;
                ReadFully(fs, buf);
            }
            else
            {
                long readItems = 0;
                long rightSide = (long)_blockPoints[0] - _blockPoints[1];
                if (startPoint < rightSide)
                {
                    long readStart = (_blockPoints[1] + (long)startPoint) * size;
                    readItems = stopPoint >= rightSide ? (rightSide - startPoint) : tpoints;
                    fs.Position = pi.BlockPtr0 + readStart;
                    ReadFully(fs, buf.AsSpan(0, (int)(readItems * size)));
                }
                if (stopPoint >= rightSide)
                {
                    long off = (long)size * ((startPoint > rightSide) ? (startPoint - rightSide) : 0);
                    fs.Position = pi.BlockPtr0 + off;
                    ReadFully(fs, buf.AsSpan((int)(size * readItems), (int)((tpoints - readItems) * size)));
                }
            }
        }
        return buf;
    }

    /// <summary>Byte position.</summary>
    private void GetBytePos(uint startPos, uint stopPos, int dts, out uint[] strb, out uint[] stpb, out bool[] dblk)
    {
        uint size = (uint)dts;
        strb = new uint[2];
        stpb = new uint[2];
        dblk = new bool[2];
        uint bp0 = _blockPoints[0], bp1 = _blockPoints[1];
        if (startPos < bp0)
        {
            strb[0] = startPos * size;
            stpb[0] = stopPos < bp0 ? (stopPos * size) + size : bp0 * size;
            dblk[0] = true;
        }
        if (stopPos >= bp0)
        {
            strb[1] = startPos >= bp0 ? (startPos - bp0) * size : 0;
            stpb[1] = (stopPos - bp0) < bp1 ? ((stopPos - bp0) * size) + size : bp1 * size;
            dblk[1] = true;
        }
    }

    /// <summary>Converts raw element bytes to a typed array, swapping byte order when <paramref name="bigEndian"/>.</summary>
    internal static Array DecodeTyped(byte[] buf, int count, string typeIds, bool bigEndian)
    {
        DataType t = DataTypes.FromId(typeIds);
        int size = DataTypes.SizeFromId(typeIds);
        ReadOnlySpan<byte> b = buf;
        switch (t)
        {
            case DataType.Float:
                {
                    var a = new float[count];
                    for (int i = 0; i < count; i++)
                        a[i] = bigEndian ? BinaryPrimitives.ReadSingleBigEndian(b[(i * 4)..]) : BinaryPrimitives.ReadSingleLittleEndian(b[(i * 4)..]);
                    return a;
                }
            case DataType.Double:
                {
                    var a = new double[count];
                    for (int i = 0; i < count; i++)
                        a[i] = bigEndian ? BinaryPrimitives.ReadDoubleBigEndian(b[(i * 8)..]) : BinaryPrimitives.ReadDoubleLittleEndian(b[(i * 8)..]);
                    return a;
                }
            case DataType.ShortInt:
                {
                    var a = new short[count];
                    for (int i = 0; i < count; i++) a[i] = bigEndian ? BinaryPrimitives.ReadInt16BigEndian(b[(i * 2)..]) : BinaryPrimitives.ReadInt16LittleEndian(b[(i * 2)..]);
                    return a;
                }
            case DataType.UShortInt:
                {
                    var a = new ushort[count];
                    for (int i = 0; i < count; i++) a[i] = bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(b[(i * 2)..]) : BinaryPrimitives.ReadUInt16LittleEndian(b[(i * 2)..]);
                    return a;
                }
            case DataType.LongInt:
                {
                    var a = new int[count];
                    for (int i = 0; i < count; i++) a[i] = bigEndian ? BinaryPrimitives.ReadInt32BigEndian(b[(i * 4)..]) : BinaryPrimitives.ReadInt32LittleEndian(b[(i * 4)..]);
                    return a;
                }
            case DataType.ULongInt:
                {
                    var a = new uint[count];
                    for (int i = 0; i < count; i++) a[i] = bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(b[(i * 4)..]) : BinaryPrimitives.ReadUInt32LittleEndian(b[(i * 4)..]);
                    return a;
                }
            case DataType.LongLong:
                {
                    var a = new long[count];
                    for (int i = 0; i < count; i++) a[i] = bigEndian ? BinaryPrimitives.ReadInt64BigEndian(b[(i * 8)..]) : BinaryPrimitives.ReadInt64LittleEndian(b[(i * 8)..]);
                    return a;
                }
            case DataType.ULongLong:
                {
                    var a = new ulong[count];
                    for (int i = 0; i < count; i++) a[i] = bigEndian ? BinaryPrimitives.ReadUInt64BigEndian(b[(i * 8)..]) : BinaryPrimitives.ReadUInt64LittleEndian(b[(i * 8)..]);
                    return a;
                }
            case DataType.TimeTag:
                {
                    var a = new TimeTag[count];
                    for (int i = 0; i < count; i++)
                    {
                        var e = b[(i * 8)..];
                        a[i] = bigEndian
                            ? new TimeTag(BinaryPrimitives.ReadInt16BigEndian(e[6..]), BinaryPrimitives.ReadUInt32BigEndian(e), BinaryPrimitives.ReadUInt16BigEndian(e[4..]))
                            : new TimeTag(BinaryPrimitives.ReadInt16LittleEndian(e[6..]), BinaryPrimitives.ReadUInt32LittleEndian(e), BinaryPrimitives.ReadUInt16LittleEndian(e[4..]));
                    }
                    return a;
                }
            case DataType.Byte:
                {
                    var a = new byte[count];
                    Array.Copy(buf, a, count);
                    return a;
                }
            default:
                {
                    // unknown / image types: keep bytes, flipped per element
                    var a = new byte[(long)count * size];
                    Array.Copy(buf, a, a.Length);
                    if (bigEndian && size > 1)
                        for (int i = 0; i < count; i++) Array.Reverse(a, i * size, size);
                    return a;
                }
        }
    }

    /// <summary>Type conversion to DT_FLOAT/DT_DOUBLE (TimeTag days made relative to the first sample).</summary>
    private static void ConvertType(ParameterData p, DataType returnType, int dataTypeSize)
    {
        if (returnType != DataType.Float && returnType != DataType.Double)
            throw new DataAccessException(SunError.Undefined);
        int n = p.Points;
        if (p.Type == DataType.TimeTag)
        {
            var tt = (TimeTag[])p.Data;
            short saveDay = 0;
            for (int i = 0; i < n; i++)
            {
                if (i != 0) tt[i].JulianDay -= saveDay;
                else
                {
                    saveDay = tt[0].JulianDay;
                    tt[0].JulianDay = 0;
                }
            }
        }
        if (returnType == DataType.Float)
        {
            var f = new float[Math.Max(n, p.Data.Length)];
            for (int i = 0; i < n; i++) f[i] = ValueConvert.ToFloat(p.Data, p.Type, i);
            p.Data = f;
        }
        else
        {
            var d = new double[Math.Max(n, p.Data.Length)];
            for (int i = 0; i < n; i++) d[i] = ValueConvert.ToDouble(p.Data, p.Type, i);
            p.Data = d;
        }
        p.Type = returnType;
        _ = dataTypeSize;
    }

    // ------------------------------------------------------------------ MULTITIME

    private int HeadSize => _sig == SunSignatures.MultiTime64 ? PHEAD64SIZE : PHEADSIZE;

    private long MapBlockPtr(ParmEntry pi) => _sig == SunSignatures.MultiTime64 ? pi.BlockPtr64 : pi.BlockPtr0;

    /// <summary>Re-reads a parameter's block pointer from the file map.</summary>
    private void RereadMapBlockPtr(Stream fs, int iParm, ParmEntry pi)
    {
        fs.Position = HeaderSize;
        Span<byte> t = stackalloc byte[14];
        for (int i = 0; i <= iParm; i++)
        {
            for (int k = 0; k < 4; k++)
            {
                int len = fs.ReadByte();
                if (len > 0) fs.Seek(len, SeekOrigin.Current);
            }
            t.Clear();
            ReadFully(fs, t);
            if (i == iParm)
            {
                if (SunSignatures.Is64(_sig)) pi.BlockPtr64 = BinaryPrimitives.ReadInt64BigEndian(t[6..]);
                else
                {
                    pi.BlockPtr0 = BinaryPrimitives.ReadUInt32BigEndian(t[6..]);
                    pi.BlockPtr1 = BinaryPrimitives.ReadUInt32BigEndian(t[10..]);
                }
            }
        }
    }

    private bool ReadBlockHeader(Stream fs, long addr, out BlockHeader h, out long nextLink, ref string indexNomen)
    {
        h = default;
        nextLink = 0;
        bool is64 = _sig == SunSignatures.MultiTime64;
        int hs = HeadSize;
        Span<byte> b = stackalloc byte[PHEAD64SIZE];
        try
        {
            if (addr < 0 || addr >= fs.Length) return false;
            fs.Position = addr;
            if (ReadFully(fs, b[..hs]) != hs) return false;
        }
        catch (IOException) { return false; }
        h.Points = BinaryPrimitives.ReadUInt32BigEndian(b);
        h.HeaderAddr = addr;
        int o = 4;
        if (is64) { nextLink = BinaryPrimitives.ReadInt64BigEndian(b[o..]); o += 8; }
        else { nextLink = BinaryPrimitives.ReadUInt32BigEndian(b[o..]); o += 4; }
        h.TimeLzhSize = BinaryPrimitives.ReadUInt32BigEndian(b[o..]); o += 4;
        h.DataLzhSize = BinaryPrimitives.ReadUInt32BigEndian(b[o..]); o += 4;
        h.IndexType = b[o++];
        h.NomenSize = b[o++];
        h.Compressed = b[o++];
        h.BigEndian = b[o++];
        var first = b.Slice(o, 8);
        var last = b.Slice(o + 8, 8);
        if (h.NomenSize != 0)
        {
            var nb = new byte[h.NomenSize];
            ReadFully(fs, nb);
            // the bytes are copied into a 256-byte buffer without terminating: text ends at first NUL
            int l = Array.IndexOf(nb, (byte)0);
            string s = AnsiText.DecodeAnsi(nb.AsSpan(0, l < 0 ? nb.Length : l));
            indexNomen = s.Length >= indexNomen.Length ? s : s + indexNomen[s.Length..];
        }
        h.DataAddr = fs.Position;
        switch ((DataType)h.IndexType)
        {
            case DataType.Double:
                h.FirstTime = BinaryPrimitives.ReadDoubleBigEndian(first);
                h.LastTime = BinaryPrimitives.ReadDoubleBigEndian(last);
                break;
            case DataType.ULongInt:
                h.FirstTime = BinaryPrimitives.ReadUInt32BigEndian(first) / 1000.0;
                h.LastTime = BinaryPrimitives.ReadUInt32BigEndian(last) / 1000.0;
                break;
            case DataType.TimeTag:
            case DataType.PackedTime:
                {
                    var tf = new TimeTag(BinaryPrimitives.ReadInt16BigEndian(first[6..]), BinaryPrimitives.ReadUInt32BigEndian(first), BinaryPrimitives.ReadUInt16BigEndian(first[4..]));
                    var tl = new TimeTag(BinaryPrimitives.ReadInt16BigEndian(last[6..]), BinaryPrimitives.ReadUInt32BigEndian(last), BinaryPrimitives.ReadUInt16BigEndian(last[4..]));
                    h.BaseGmt = tf;
                    h.FirstTime = TimeMath.Time(tf, Info.RefGmt);
                    h.LastTime = TimeMath.Time(tl, Info.RefGmt);
                    break;
                }
            case DataType.Float:
                h.FirstTime = BinaryPrimitives.ReadSingleBigEndian(first);
                h.LastTime = BinaryPrimitives.ReadSingleBigEndian(last);
                break;
            default:
                throw new DataAccessException(SunError.TimeError);
        }
        return true;
    }

    private ParameterData ReadMultiTimeParm(string parmName, double startTime, double stopTime, double? pRefTIME, TimeTag? pRefGMT, ushort returnType)
    {
        Stream fs = AcquireStream(out bool owned);
        try
        {
            int mapPos = FindParameter(parmName, out int altName);
            if (mapPos < 0) throw new DataAccessException(SunError.PidNotFound);
            ParmEntry parmInfo = _map[mapPos];
            if (MapBlockPtr(parmInfo) == 0)
            {
                RereadMapBlockPtr(fs, mapPos, parmInfo);
                if (MapBlockPtr(parmInfo) == 0) throw new NoDataException();
                Info.IsDynamic = true;
            }
            bool isV = parmInfo.TypeIds.Length > 0 && parmInfo.TypeIds[0] == 'v';
            bool image = (returnType & 0x00ff) == 0;

            MultiTimeLookup lk;
            if (_opt.RetainedLookup)
            {
                _lookups ??= new MultiTimeLookup[_items];
                lk = _lookups[mapPos] ??= new MultiTimeLookup();
            }
            else
                lk = new MultiTimeLookup();

            if (!lk.Initialized)
            {
                lk.Initialized = true;
                lk.Heads.Clear();
                lk.Pending = MapBlockPtr(parmInfo);
            }
            else if (lk.Heads.Count > 0)
            {
                var lastH = lk.Heads[^1];
                lk.Heads.RemoveAt(lk.Heads.Count - 1);
                lk.Pending = lk.Heads.Count == 0 ? MapBlockPtr(parmInfo) : lastH.DataAddr - HeadSize;
            }
            else
                lk.Pending = MapBlockPtr(parmInfo);

            string nomen = lk.IndexNomen;
            while (lk.Pending != 0)
            {
                if (!ReadBlockHeader(fs, lk.Pending, out var h, out long next, ref nomen))
                    throw new DataAccessException(SunError.LinkHdrRd);
                lk.Heads.Add(h);
                lk.Pending = next;
                if (lk.Heads.Count > 50_000_000) break;
            }
            lk.IndexNomen = nomen;

            var heads = lk.Heads;
            int count = heads.Count;
            double rs, re;
            if (pRefGMT.HasValue)
            {
                double dTmp = TimeMath.Time(pRefGMT.Value, Info.RefGmt);
                rs = startTime + dTmp;
                re = stopTime + dTmp;
            }
            else
            {
                rs = startTime + (pRefTIME ?? 0.0);
                re = stopTime + (pRefTIME ?? 0.0);
            }

            int beginAddr = -1, endAddr = -1;
            long nPoints = 0;
            bool extrap = _opt.AllowExtrapolation;
            for (int u = 0; u < count; u++)
            {
                if (beginAddr == -1)
                {
                    if (((rs <= heads[u].LastTime) && (re >= heads[u].FirstTime))
                        || (extrap && (u < count - 1) && (rs <= heads[u + 1].FirstTime) && (re >= heads[u].FirstTime)))
                    {
                        beginAddr = u;
                        endAddr = u;
                        nPoints = heads[u].Points + (isV ? 0 : 1);
                        if (image && isV) break;
                    }
                }
                else
                {
                    if (re >= heads[u].FirstTime)
                    {
                        endAddr = u;
                        nPoints += heads[u].Points;
                    }
                    else
                    {
                        if (extrap && u > 0 && re > heads[u - 1].LastTime)
                        {
                            endAddr = u;
                            nPoints += heads[u].Points;
                        }
                        break;
                    }
                }
            }

            ParameterData? pSunParm = null;
            if (beginAddr != -1)
            {
                for (int i = beginAddr; i <= endAddr; i++)
                {
                    if (i == beginAddr)
                    {
                        pSunParm = ReadMultiTimeBlock(fs, mapPos, altName, startTime, stopTime, pRefTIME, pRefGMT, heads[i], returnType, (int)Math.Min(nPoints, int.MaxValue));
                    }
                    else
                    {
                        ParameterData tmp;
                        try { tmp = ReadMultiTimeBlock(fs, mapPos, altName, startTime, stopTime, pRefTIME, pRefGMT, heads[i], returnType, 0); }
                        catch (DataAccessException) { break; }
                        ParameterOps.Append(pSunParm!, tmp, false);
                    }
                }
                if (pSunParm != null && pSunParm.Points > 0)
                {
                    bool retGmt = (returnType & DataTypes.ReturnGmtFlag) != 0;
                    if (pSunParm.Time[0] < startTime)
                    {
                        pSunParm.Time[0] = (float)startTime;
                        if (retGmt && pSunParm.Gmt != null && pSunParm.GmtPoints > 0)
                            pSunParm.Gmt[pSunParm.GmtOffset] = TimeMath.Gmt(startTime, pRefGMT ?? Info.RefGmt, true);
                    }
                    int last = pSunParm.Points - 1;
                    if (pSunParm.Time[last] > stopTime)
                    {
                        // getValue(points-2) then setValue(points-1, stopTime)
                        if (pSunParm.Points >= 2)
                            Array.Copy(pSunParm.Data, last - 1, pSunParm.Data, last, 1);
                        else
                            ClearElement(pSunParm.Data, last);
                        pSunParm.Time[last] = (float)stopTime;
                        if (retGmt && pSunParm.Gmt != null && pSunParm.GmtPoints > 0)
                            pSunParm.Gmt[pSunParm.GmtOffset + last] = TimeMath.Gmt(stopTime, pRefGMT ?? Info.RefGmt, true);
                    }
                }
            }
            else if (!isV)
            {
                int u = count - 1;
                if (u >= 0 && Info.IsDynamic && rs > heads[u].LastTime)
                {
                    pSunParm = ReadMultiTimeBlock(fs, mapPos, altName, heads[u].LastTime, heads[u].LastTime, null, pRefGMT, heads[u], returnType, 32);
                }
                else
                    throw new DataAccessException(SunError.TimeSlice);
            }
            if (pSunParm == null) throw new DataAccessException(SunError.TimeSlice);

            // tag an end point with the stale value at the EOF event time
            if (!isV && pSunParm.RefGmt.HasValue && pSunParm.Points > 0 && pSunParm.Time[pSunParm.Points - 1] < stopTime)
            {
                foreach (var ev in _events)
                {
                    if (ev.Name != "EOF") continue;
                    double dTmp = TimeMath.Time(ev.Gmt, pSunParm.RefGmt.Value);
                    if (pRefTIME.HasValue) dTmp -= pRefTIME.Value;
                    dTmp = dTmp > stopTime ? stopTime : dTmp;
                    int n = pSunParm.Points;
                    if (dTmp > (double)pSunParm.Time[n - 1])
                    {
                        EnsureCapacity(pSunParm, n + 1);
                        pSunParm.Time[n] = (float)dTmp;
                        Array.Copy(pSunParm.Data, n - 1, pSunParm.Data, n, 1);
                        if (pSunParm.HasValidGmt)
                        {
                            EnsureGmtCapacity(pSunParm, n + 1);
                            pSunParm.Gmt![pSunParm.GmtOffset + n] = TimeMath.Gmt(dTmp, pSunParm.RefGmt.Value, true);
                            pSunParm.GmtPoints++;
                        }
                        pSunParm.Points++;
                    }
                    break;
                }
            }

            if (lk.IndexNomen.Length > 0)
            {
                var parts = lk.IndexNomen.Split(',', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3)
                {
                    pSunParm.TimeName = parts[0];
                    pSunParm.TimeUnits = parts[1];
                    pSunParm.TimeDescription = parts[2];
                }
            }

            if (!isV)
            {
                pSunParm.Rate = parmInfo.Info.Rate;
                if (_opt.MakeFullRate && (parmInfo.Info.SequenceNumber & 0x80000000u) == 0x80000000u)
                    ParameterOps.MakeFullRate(pSunParm, _opt.FilterInvalid && !image);
                else if (parmInfo.Info.Rate == -1f)
                {
                    int n = pSunParm.Points;
                    int u = TimeMath.Location(0.0, pSunParm.Time, n, 0);
                    if (u > (long)n - 2) u = 0;
                    if (u < (long)n - 1)
                    {
                        double dTmp = pSunParm.Time[u + 1] - pSunParm.Time[u];
                        if (dTmp > 0.0) pSunParm.Rate = (float)(1.0 / dTmp);
                    }
                }
            }
            return pSunParm;
        }
        finally
        {
            if (owned) fs.Dispose();
        }
    }

    /// <summary>
    /// Walks a MULTITIME parameter's block list and returns the last block header address and its
    /// last time (relative to the file reference) — used by <see cref="SunWriter.Revise"/>.
    /// </summary>
    public bool TryGetLastBlock(int mapPos, out long headerAddr, out double lastTime)
    {
        headerAddr = 0;
        lastTime = 0;
        if (!SunSignatures.IsMultiTime(_sig) || mapPos < 0 || mapPos >= (int)_items) return false;
        lock (_lock)
        {
            Stream fs = AcquireStream(out bool owned);
            try
            {
                long addr = MapBlockPtr(_map[mapPos]);
                string nomen = "";
                BlockHeader lastH = default;
                bool any = false;
                while (addr != 0)
                {
                    if (!ReadBlockHeader(fs, addr, out var h, out long next, ref nomen)) return false;
                    lastH = h;
                    any = true;
                    addr = next;
                }
                if (!any) return false;
                headerAddr = lastH.HeaderAddr;
                lastTime = lastH.LastTime;
                return true;
            }
            finally
            {
                if (owned) fs.Dispose();
            }
        }
    }

    private static void ClearElement(Array a, int i) => Array.Clear(a, i, 1);

    private static void EnsureCapacity(ParameterData p, int n)
    {
        if (p.Time.Length < n)
        {
            var t = new float[n + 16];
            Array.Copy(p.Time, t, p.Points);
            p.Time = t;
        }
        if (p.Data.Length < n)
        {
            var d = ValueConvert.Allocate(p.Type, n + 16);
            Array.Copy(p.Data, d, p.Points);
            p.Data = d;
        }
    }

    private static void EnsureGmtCapacity(ParameterData p, int n)
    {
        if (p.Gmt!.Length - p.GmtOffset < n)
        {
            var g = new TimeTag[n + 16];
            Array.Copy(p.Gmt, p.GmtOffset, g, 0, p.GmtPoints);
            p.Gmt = g;
            p.GmtOffset = 0;
        }
    }

    private ParameterData ReadMultiTimeBlock(Stream fs, int mapPos, int altName, double startTime, double stopTime,
        double? pRefTIME, TimeTag? pRefGMT, BlockHeader h, ushort returnTypeIn, int preAllocSz)
    {
        bool retGmtRequested = (returnTypeIn & DataTypes.ReturnGmtFlag) != 0;
        bool retGmt = retGmtRequested;
        ushort returnType = (ushort)(returnTypeIn & 0x00ff);
        ParmEntry parmInfo = _map[mapPos];
        TimeTag baseRef = Info.RefGmt;
        TimeTag refGMT = pRefGMT ?? baseRef;
        bool isV = parmInfo.TypeIds.Length > 0 && parmInfo.TypeIds[0] == 'v';

        int dataTypeSize;
        if (parmInfo.Extended)
            dataTypeSize = isV ? 4 + (returnType == 0 ? (int)h.TimeLzhSize : 0) : DataTypes.SizeFromId(parmInfo.TypeIds);
        else
            dataTypeSize = 4;

        int iSz = (DataType)h.IndexType switch
        {
            DataType.ULongInt or DataType.PackedTime or DataType.Float => 4,
            DataType.TimeTag or DataType.Double => 8,
            _ => throw new DataAccessException(SunError.TimeError),
        };

        int hp = (int)h.Points;
        long rawTimeLen = (long)hp * iSz;
        if (rawTimeLen > int.MaxValue) throw new DataAccessException(SunError.MemAlloc14);
        var tbuf = new byte[rawTimeLen];
        fs.Position = h.DataAddr;
        bool readOK;
        if (h.Compressed != 0 && !isV)
            readOK = new LzhDecoder().Decode(fs, h.TimeLzhSize, tbuf, 0, 0, rawTimeLen) == SunError.Success;
        else
            readOK = ReadFully(fs, tbuf) == tbuf.Length;
        if (!readOK) throw new DataAccessException(SunError.TimeError);

        int points = hp;
        var time = new float[hp];
        TimeTag[]? tags = null;
        bool hasTimeTags = false;
        ReadOnlySpan<byte> tb = tbuf;
        bool be = h.BigEndian != 0;
        switch ((DataType)h.IndexType)
        {
            case DataType.PackedTime:
                {
                    hasTimeTags = true;
                    tags = new TimeTag[hp];
                    uint packedTime = 0;
                    short saveDay = 0, wrapDays = 0;
                    for (int i = 0; i < hp; i++)
                    {
                        uint v = be ? BinaryPrimitives.ReadUInt32BigEndian(tb[(i * 4)..]) : BinaryPrimitives.ReadUInt32LittleEndian(tb[(i * 4)..]);
                        if (i != 0 && h.Compressed != 0) packedTime = unchecked(packedTime + v);
                        else packedTime = v;
                        short day = (short)(packedTime >> 27);
                        if (i != 0)
                        {
                            if (day < saveDay)
                            {
                                wrapDays += 32;
                                saveDay = day;
                            }
                        }
                        else
                            saveDay = day;
                        tags[i].JulianDay = unchecked((short)(day + h.BaseGmt.JulianDay + wrapDays));
                        tags[i].MilliSeconds = packedTime & 0x07FFFFFF;
                        tags[i].MicroSeconds = 0;
                        time[i] = (float)TimeMath.Time(tags[i], refGMT);
                        if (pRefTIME.HasValue) time[i] -= (float)pRefTIME.Value;
                        if (time[i] > stopTime) { points = i + 1; break; }
                    }
                    break;
                }
            case DataType.TimeTag:
                {
                    hasTimeTags = true;
                    tags = new TimeTag[hp];
                    for (int i = 0; i < hp; i++)
                    {
                        var e = tb[(i * 8)..];
                        TimeTag t = be
                            ? new TimeTag(BinaryPrimitives.ReadInt16BigEndian(e[6..]), BinaryPrimitives.ReadUInt32BigEndian(e), BinaryPrimitives.ReadUInt16BigEndian(e[4..]))
                            : new TimeTag(BinaryPrimitives.ReadInt16LittleEndian(e[6..]), BinaryPrimitives.ReadUInt32LittleEndian(e), BinaryPrimitives.ReadUInt16LittleEndian(e[4..]));
                        if (i != 0 && h.Compressed != 0)
                        {
                            t.JulianDay = unchecked((short)(t.JulianDay + tags[i - 1].JulianDay));
                            t.MilliSeconds = unchecked(t.MilliSeconds + tags[i - 1].MilliSeconds);
                            t.MicroSeconds = unchecked((ushort)(t.MicroSeconds + tags[i - 1].MicroSeconds));
                        }
                        tags[i] = t;
                        time[i] = (float)TimeMath.Time(t, refGMT);
                        if (pRefTIME.HasValue) time[i] -= (float)pRefTIME.Value;
                        if (time[i] > stopTime) { points = i + 1; break; }
                    }
                    break;
                }
            case DataType.ULongInt:
                {
                    hasTimeTags = true;
                    tags = new TimeTag[hp];
                    for (int i = 0; i < hp; i++)
                    {
                        uint v = be ? BinaryPrimitives.ReadUInt32BigEndian(tb[(i * 4)..]) : BinaryPrimitives.ReadUInt32LittleEndian(tb[(i * 4)..]);
                        var t = new TimeTag(baseRef.JulianDay, unchecked(baseRef.MilliSeconds + v), 0);
                        if (t.MilliSeconds > 86399999U)
                        {
                            t.JulianDay = unchecked((short)(t.JulianDay + (short)(t.MilliSeconds / 86400000U)));
                            t.MilliSeconds %= 86400000U;
                        }
                        tags[i] = t;
                        time[i] = (float)TimeMath.Time(t, refGMT);
                        if (pRefTIME.HasValue) time[i] -= (float)pRefTIME.Value;
                        if (time[i] > stopTime) { points = i + 1; break; }
                    }
                    break;
                }
            case DataType.Float:
                {
                    if (retGmt) retGmt = false; // ERR_GMTNOTAVAIL (not reported)
                    double? refDelta = pRefGMT.HasValue ? TimeMath.Time(pRefGMT.Value, baseRef) : null;
                    double dSum = 0;
                    for (int i = 0; i < hp; i++)
                    {
                        float v = be ? BinaryPrimitives.ReadSingleBigEndian(tb[(i * 4)..]) : BinaryPrimitives.ReadSingleLittleEndian(tb[(i * 4)..]);
                        if (h.Compressed != 0)
                        {
                            if (i != 0) dSum += (double)v;
                            else dSum = (double)v;
                            v = (float)dSum;
                        }
                        if (pRefTIME.HasValue) v -= (float)pRefTIME.Value;
                        if (refDelta.HasValue) v -= (float)refDelta.Value;
                        time[i] = v;
                        if (time[i] > stopTime) { points = i + 1; break; }
                    }
                    break;
                }
            case DataType.Double:
                {
                    hasTimeTags = true;
                    tags = new TimeTag[hp];
                    double? refDelta = pRefGMT.HasValue ? TimeMath.Time(pRefGMT.Value, baseRef) : null;
                    var dbl = new double[hp];
                    for (int i = 0; i < hp; i++)
                    {
                        dbl[i] = be ? BinaryPrimitives.ReadDoubleBigEndian(tb[(i * 8)..]) : BinaryPrimitives.ReadDoubleLittleEndian(tb[(i * 8)..]);
                        if (h.Compressed != 0 && i != 0) dbl[i] += dbl[i - 1];
                        tags[i] = TimeMath.Gmt(dbl[i], baseRef, true);
                        time[i] = (float)dbl[i];
                        if (pRefTIME.HasValue) time[i] -= (float)pRefTIME.Value;
                        if (refDelta.HasValue) time[i] -= (float)refDelta.Value;
                        if (time[i] > stopTime) { points = i + 1; break; }
                    }
                    break;
                }
        }

        bool extrap = _opt.AllowExtrapolation;
        int startPoint = TimeMath.Location((float)startTime, time, points, extrap ? 1 : -1);
        int endPoint = TimeMath.Location((float)stopTime, time, points, extrap ? -1 : 1);
        if (startPoint > endPoint) startPoint = endPoint;
        int np = endPoint - startPoint + 1;
        int bufAlloc = preAllocSz > np ? preAllocSz : np;

        var p = new ParameterData { Source = this, Points = np, CommonTimeIndex = false };
        var outTime = new float[bufAlloc];
        Array.Copy(time, startPoint, outTime, 0, np);
        p.Time = outTime;
        if (retGmt && tags != null)
        {
            var g = new TimeTag[bufAlloc];
            Array.Copy(tags, startPoint, g, 0, np);
            p.Gmt = g;
            p.GmtOffset = 0;
            p.GmtPoints = np;
        }

        // data block
        string typeIds = parmInfo.TypeIds;
        Array data;
        if (isV && returnType != 0)
        {
            var f = new float[bufAlloc];
            f[0] = DataTypes.FpVariable;
            data = f;
            p.Type = DataType.Variable;
        }
        else if (isV)
        {
            int objSize = dataTypeSize - 4;
            var bytes = new byte[4 + Math.Max(objSize, 0)];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)objSize);
            if (h.Compressed != 0)
            {
                fs.Position = h.DataAddr + rawTimeLen;
                var err = new LzhDecoder().Decode(fs, h.DataLzhSize, bytes, 4, (long)startPoint * objSize, (long)startPoint * objSize + (long)np * objSize);
                if (err != SunError.Success) throw new DataAccessException(SunError.TimeError);
            }
            else
            {
                fs.Position = h.DataAddr + rawTimeLen + (long)startPoint * objSize;
                ReadFully(fs, bytes.AsSpan(4));
            }
            data = bytes;
            p.Type = DataType.Variable;
        }
        else
        {
            long dlen = (long)np * dataTypeSize;
            if (dlen > int.MaxValue) throw new DataAccessException(SunError.MemAlloc4);
            var raw = new byte[dlen];
            if (h.Compressed != 0)
            {
                fs.Position = h.DataAddr + h.TimeLzhSize;
                var err = new LzhDecoder().Decode(fs, h.DataLzhSize, raw, 0, (long)startPoint * dataTypeSize, (long)startPoint * dataTypeSize + dlen);
                if (err != SunError.Success) throw new DataAccessException(err);
            }
            else
            {
                fs.Position = h.DataAddr + rawTimeLen + (long)startPoint * dataTypeSize;
                if (ReadFully(fs, raw) != raw.Length) throw new DataAccessException(SunError.TimeError);
            }
            string tid = parmInfo.Extended ? typeIds : "f4";
            var typed = DecodeTyped(raw, np, tid, be);
            if (bufAlloc > np)
            {
                var grown = ValueConvert.Allocate(DataTypes.FromId(tid), bufAlloc);
                if (typed is byte[] && grown is byte[] && DataTypes.FromId(tid) == DataType.Image)
                    grown = new byte[(long)bufAlloc * DataTypes.SizeFromId(tid)];
                Array.Copy(typed, grown, typed.Length);
                typed = grown;
            }
            data = typed;
            p.Type = DataTypes.FromId(tid);
        }
        p.Data = data;
        p.TimeShift = 0f;
        if (pRefTIME.HasValue) p.RefTime = pRefTIME;
        if (pRefGMT.HasValue || hasTimeTags) p.RefGmt = refGMT;

        p.TimeName = "Time";
        p.TimeUnits = "Seconds";
        p.TimeDescription = "Relative Time in Seconds";

        if (returnType != 0 && returnType != (ushort)p.Type && !(isV))
            ConvertType(p, (DataType)returnType, dataTypeSize);
        else if (returnType != 0 && isV && returnType != (ushort)DataType.Variable)
        {
            // conversion of DT_VARIABLE requested — newType has no conversion for this type
            var f = (float[])p.Data;
            if (returnType == (ushort)DataType.Double)
            {
                var d = new double[f.Length];
                for (int i = 0; i < np; i++) d[i] = ValueConvert.ToDouble(f, DataType.Variable, i);
                p.Data = d;
                p.Type = DataType.Double;
            }
            else
            {
                for (int i = 0; i < np; i++) f[i] = ValueConvert.ToFloat(f, DataType.Variable, i);
                p.Type = DataType.Float;
            }
        }

        SetNomen(p, parmInfo, altName);
        p.MapPosition = mapPos;
        p.SequenceNumber = parmInfo.Info.SequenceNumber;
        if (!isV)
            ParameterOps.ScanMinMax(p, _opt.FilterInvalid && returnType != 0);
        return p;
    }

    // ------------------------------------------------------------------ refresh

    /// <inheritdoc />
    public RefreshResult Refresh(bool clearEventFlag)
    {
        lock (_lock)
        {
            return new RefreshResult(RefreshCore());
        }
    }

    private int RefreshCore()
    {
        if (!Info.IsDynamic) return -2;
        Stream fs;
        bool owned;
        try { fs = AcquireStream(out owned); }
        catch (DataAccessException) { return 0; }
        int refreshed = 0;
        bool needsUpdate = false, gmtChange = false;
        uint[] np = new uint[9];
        try
        {
            if (fs is FileStream ffs) ffs.Flush();
            fs.Seek(0, SeekOrigin.End);
            fs.Seek(0, SeekOrigin.Begin);
            Span<byte> b = stackalloc byte[40];
            int got = ReadFully(fs, b);
            uint fileSig = got >= 4 ? BinaryPrimitives.ReadUInt32LittleEndian(b) : 0;
            if (fileSig == Info.FileSig)
            {
                if (got == 40)
                {
                    for (int i = 0; i < 9; i++) np[i] = BinaryPrimitives.ReadUInt32BigEndian(b[(4 + i * 4)..]);
                    if (SunSignatures.IsMultiTime(_sig))
                    {
                        if (np[7] == np[8]) Info.IsDynamic = false;
                    }
                    if (np[7] != _blockPoints[0] || np[8] != _blockPoints[1]) needsUpdate = true;
                    for (int i = 5; i >= 0; i--)
                        if (np[i] != _refTime[i]) { gmtChange = true; break; }
                    if (np[6] != _items) gmtChange = true;
                    if (gmtChange) needsUpdate = true;
                }
                else
                    refreshed = -5;
            }
            else
                refreshed = -4;

            if (gmtChange)
            {
                _lookups = null;
                _items = np[6];
                Info.Items = _items;
                fs.Position = HeaderSize;
                RetrieveFileMap(fs);
            }

            if (needsUpdate)
            {
                _blockPoints[0] = np[7];
                _blockPoints[1] = np[8];
                Info.Points = SunSignatures.IsMultiTime(_sig) ? _blockPoints[1] : _blockPoints[0];
                if (gmtChange)
                {
                    for (int i = 0; i < 6; i++) _refTime[i] = np[i];
                    Array.Copy(_refTime, Info.RefTime, 6);
                    Info.RefGmt = HeaderRefGmt();
                    refreshed = 2;
                }
                else
                    refreshed = 1;

                if (!SunSignatures.IsMultiTime(_sig))
                {
                    if (_timeIndex != null)
                    {
                        _timeIndex = null;
                        try { LoadIndex(fs, false); }
                        catch (DataAccessException) { refreshed = -1; }
                    }
                    if (_gmtIndex != null)
                    {
                        _gmtIndex = null;
                        try { LoadIndex(fs, true); }
                        catch (DataAccessException) { refreshed = -1; }
                    }
                }

                LoadComment(fs);
                if (ParseEvents() != 0) refreshed |= 0x04;
            }
            else
                refreshed = 0; // open/signature/header errors (-3/-4/-5) are reported as "current" 
        }
        catch (IOException)
        {
            refreshed = -5;
        }
        finally
        {
            if (owned) fs.Dispose();
        }
        return refreshed;
    }
}
