using System.Buffers.Binary;
using Multiplot.Core;
using Multiplot.Plugins;

namespace Multiplot.Sun;

/// <summary>Create modes of <see cref="SunWriter"/>.</summary>
public enum SunCreateMode
{
    /// <summary>Fail if the file exists.</summary>
    Create = 0,
    /// <summary>Overwrite an existing file.</summary>
    Overwrite = 1,
    /// <summary>Revise: append blocks to an existing MULTITIME file.</summary>
    Update = 2,
    /// <summary>Modify header fields only.</summary>
    HeaderOnly = 3,
}

/// <summary>Options of <see cref="SunWriter.AddParm"/>.</summary>
[Flags]
public enum SunWriteOptions : byte
{
    /// <summary>None.</summary>
    None = 0,
    /// <summary>LZH compress time and data (time stamps delta encoded).</summary>
    LzhCompress = 0x01,
    /// <summary>Pack TimeTags into p4, ignoring microseconds.</summary>
    PackTimeTag = 0x04,
    /// <summary>Application signals updates with <see cref="SunWriter.ShowFileUpdated"/>.</summary>
    ManualUpdate = 0x08,
    /// <summary>Store payload little endian.</summary>
    OrientLittleEnd = 0x10,
    /// <summary>Store payload big endian.</summary>
    OrientBigEnd = 0x20,
    /// <summary>Store payload in native order.</summary>
    OrientNative = 0x40,
}

/// <summary>
/// SUN file writer: create, revise, add parameters, update notification, close and the header setters
/// (FR-SUN-100, FR-SUN-101). Supports COMPRESSED, COMMONTIME, MULTITIME(_SRT), MULTITIME64(_SRT).
/// </summary>
public sealed class SunWriter : IDisposable
{
    private const int PHEADSIZE = 36;
    private const int PHEAD64SIZE = 40;
    private const int DBL_BLOCK = 500;

    private uint _fileSig;
    private FileStream _fs = null!;
    private SunCreateMode _writeMode;
    private string _name = "";
    private uint _nPoints, _nItems, _currentMap, _mapEntrySize;
    private readonly uint[] _blockPoints = new uint[2];
    private uint _commentSize;
    private long _nextMapEntryPos;
    private long _commentOffset;
    private long[] _currentBlockAddr = Array.Empty<long>();
    private long _nextParmHeader;
    private List<string> _currentParmSet = new();
    private TimeTag _revRefGmt;
    private double[] _appendAfter = Array.Empty<double>();
    private bool _closed;

    /// <summary>Status of the last operation.</summary>
    public SunError Status { get; private set; }

    /// <summary>Signature being written.</summary>
    public uint FileSig => _fileSig;

    private bool IsMulti => SunSignatures.IsMultiTime(_fileSig);
    private bool Is64 => SunSignatures.Is64(_fileSig);

    private SunWriter() { }

    /// <summary>Throws <see cref="DataAccessException"/> on failure.</summary>
    public static SunWriter Create(string fileName, SunCreateMode mode, uint commentBlockSize, uint nItems, uint nPoints, uint fileSig)
    {
        if (mode is SunCreateMode.Update or SunCreateMode.HeaderOnly)
            return Revise(fileName, mode);
        if (fileSig != SunSignatures.MultiTime && fileSig != SunSignatures.MultiTime64 && fileSig != SunSignatures.MultiTimeSrt
            && fileSig != SunSignatures.MultiTime64Srt && fileSig != SunSignatures.CommonTime && fileSig != SunSignatures.Compressed)
            throw new DataAccessException(SunError.WrtInvalidFormat);
        if (mode == SunCreateMode.Create && File.Exists(fileName))
            throw new DataAccessException(SunError.WrtFileExists);

        var w = new SunWriter
        {
            _writeMode = SunCreateMode.Create,
            _fileSig = fileSig,
            _nPoints = SunSignatures.IsMultiTime(fileSig) ? 0 : nPoints,
            _nItems = nItems,
            _commentSize = commentBlockSize,
            _currentMap = 0,
            _nextMapEntryPos = 264,
            _name = fileName,
        };
        try
        {
            w._fs = new FileStream(fileName, FileMode.Create, FileAccess.ReadWrite, FileShare.Read | FileShare.Delete);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new DataAccessException(SunError.WrtOpenErr);
        }
        w._fs.Write(new byte[264]);
        w._mapEntrySize = fileSig == SunSignatures.Compressed ? 80u : 1038u;
        w._commentOffset = (w._nItems * (long)w._mapEntrySize) + 264;
        byte fill = (byte)(fileSig == SunSignatures.Compressed ? 0 : 32);
        WriteFill(w._fs, fill, (long)nItems * w._mapEntrySize);
        WriteFill(w._fs, 0, commentBlockSize);
        if (SunSignatures.IsMultiTime(fileSig))
            w._currentBlockAddr = new long[nItems];
        w._nextParmHeader = (w._nItems * (long)w._mapEntrySize) + 264 + w._commentSize;
        return w;
    }

    private static void WriteFill(Stream s, byte b, long count)
    {
        var buf = new byte[65536];
        if (b != 0) Array.Fill(buf, b);
        while (count > 0)
        {
            int n = (int)Math.Min(count, buf.Length);
            s.Write(buf, 0, n);
            count -= n;
        }
    }

    /// <summary>Revises a file (UPDATE: append MULTITIME blocks; HEADERONLY: header fields).</summary>
    public static SunWriter Revise(string fileName, SunCreateMode mode)
    {
        if ((File.GetAttributes(fileName) & FileAttributes.ReadOnly) != 0)
            throw new DataAccessException(SunError.WrtReadOnly);
        var opt = new SunLibraryOptions { RetainedLookup = true };
        var w = new SunWriter { _name = fileName };
        using (var f = SunFile.Open(fileName, opt))
        {
            w._revRefGmt = f.Info.RefGmt;
            w._fileSig = f.Info.FileSig;
            w._nPoints = 0;
            if (SunSignatures.IsMultiTime(w._fileSig))
            {
                w._blockPoints[0] = 0;
                w._blockPoints[1] = f.BlockPoints[1];
            }
            else
            {
                w._blockPoints[0] = f.BlockPoints[0];
                w._blockPoints[1] = f.BlockPoints[1];
            }
            w._nItems = f.Info.Items;
            w._commentOffset = f.CommentOffset;
            w._commentSize = f.CommentSize;
            w._currentMap = f.Info.Items;
            w._mapEntrySize = 1038;
            w._nextMapEntryPos = 264;
            foreach (var p in f.Parameters) w._currentParmSet.Add(p.Name);

            if (SunSignatures.IsMultiTime(w._fileSig) && mode == SunCreateMode.Update)
            {
                w._writeMode = SunCreateMode.Update;
                w._currentBlockAddr = new long[w._nItems];
                w._appendAfter = new double[w._nItems];
                for (int i = 0; i < w._nItems; i++)
                {
                    if (f.TryGetLastBlock(i, out long hdrAddr, out double lastTime))
                    {
                        w._currentBlockAddr[i] = hdrAddr;
                        w._appendAfter[i] = lastTime;
                    }
                }
            }
            else
                w._writeMode = SunCreateMode.HeaderOnly;
        }
        try
        {
            w._fs = new FileStream(fileName, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new DataAccessException(SunError.WrtOpenErr);
        }
        if (SunSignatures.IsMultiTime(w._fileSig) && mode == SunCreateMode.Update)
            w._nextParmHeader = w._fs.Length;
        return w;
    }

    // ------------------------------------------------------------------ header setters

    private void WriteAt(long pos, ReadOnlySpan<byte> bytes)
    {
        long save = _fs.Position;
        _fs.Position = pos;
        _fs.Write(bytes);
        _fs.Position = save;
    }

    private void WriteU32BE(long pos, uint v)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, v);
        WriteAt(pos, b);
    }

    /// <summary>Sets the reference time.</summary>
    public void SetRefTime(uint yr, uint day, uint hr, uint min, uint sec, uint mil)
    {
        Span<byte> b = stackalloc byte[24];
        BinaryPrimitives.WriteUInt32BigEndian(b, yr);
        BinaryPrimitives.WriteUInt32BigEndian(b[4..], day);
        BinaryPrimitives.WriteUInt32BigEndian(b[8..], hr);
        BinaryPrimitives.WriteUInt32BigEndian(b[12..], min);
        BinaryPrimitives.WriteUInt32BigEndian(b[16..], sec);
        BinaryPrimitives.WriteUInt32BigEndian(b[20..], mil);
        WriteAt(4, b);
    }

    /// <summary>Sets the cutoff time.</summary>
    public void SetCutoff(float cutoff)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteSingleBigEndian(b, cutoff);
        WriteAt(40, b);
    }

    private void SetText(long pos, int len, string? text)
    {
        var buf = new byte[len];
        if (text != null)
        {
            var src = AnsiText.Encode(text);
            int l = 0;
            while (l < src.Length && l < len && src[l] != 0) l++;
            Array.Copy(src, buf, l);
        }
        WriteAt(pos, buf);
    }

    /// <summary>Sets the test title.</summary>
    public void SetTitle(string? text) => SetText(44, 80, text);
    /// <summary>Sets the header comment.</summary>
    public void SetComment(string? text) => SetText(124, 80, text);
    /// <summary>Sets the serial number.</summary>
    public void SetSerialNum(string? text) => SetText(204, 12, text);
    /// <summary>Sets the test stand.</summary>
    public void SetTestStand(string? text) => SetText(216, 12, text);
    /// <summary>Sets the test position.</summary>
    public void SetTestPos(string? text) => SetText(228, 12, text);
    /// <summary>Sets the test type.</summary>
    public void SetTestType(string? text) => SetText(240, 12, text);
    /// <summary>Sets the test number.</summary>
    public void SetTestNumb(string? text) => SetText(252, 12, text);

    /// <summary>Writes up to the declared comment size, NUL padded.</summary>
    public void SetCommentText(string commentText)
    {
        var src = AnsiText.Encode(commentText);
        int wSize = (int)Math.Min(_commentSize, (uint)src.Length);
        var buf = new byte[_commentSize];
        Array.Copy(src, buf, wSize);
        WriteAt(_commentOffset, buf);
    }

    // ------------------------------------------------------------------ add parameter

    private static byte[] ToNativeBytes(Array a, int count, int elemSize)
    {
        var b = new byte[(long)count * elemSize];
        if (a is TimeTag[] tt)
        {
            for (int i = 0; i < count; i++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(i * 8), tt[i].MilliSeconds);
                BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(i * 8 + 4), tt[i].MicroSeconds);
                BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(i * 8 + 6), tt[i].JulianDay);
            }
        }
        else
            Buffer.BlockCopy(a, 0, b, 0, b.Length);
        return b;
    }

    private static void FlipElements(byte[] b, int count, int size, int offset = 0)
    {
        if (size <= 1) return;
        for (int i = 0; i < count; i++) Array.Reverse(b, offset + i * size, size);
    }

    private static void FlipTimeTags(byte[] b, int count, int offset = 0)
    {
        for (int i = 0; i < count; i++)
        {
            int o = offset + i * 8;
            Array.Reverse(b, o, 4);
            Array.Reverse(b, o + 4, 2);
            Array.Reverse(b, o + 6, 2);
        }
    }

    /// <summary>
    /// Adds a parameter. For COMPRESSED/COMMONTIME <paramref name="indexId"/>/<paramref name="indexArray"/>
    /// must be null and <paramref name="dataArray"/> holds the file point count; parameter 0 must be TIME f4.
    /// For MULTITIME a repeated <paramref name="n1"/> appends a block. For "v1" data
    /// <paramref name="dataArray"/> is a byte[] object and <paramref name="pCount"/> its size.
    /// </summary>
    public void AddParm(string n1, string n2, string units, string descr, int seqNum, string dataId, Array dataArray,
        string? indexId, Array? indexArray, uint pCount, SunWriteOptions options, string? indexNomen)
    {
        Status = SunError.Success;
        if (_writeMode == SunCreateMode.Update && !IsMulti) Fail(SunError.WrtNotValid);
        DataType iType = DataType.Image;
        if (IsMulti)
        {
            iType = DataTypes.FromId(indexId);
            if (iType != DataType.Float && iType != DataType.Double && iType != DataType.TimeTag && iType != DataType.ULongInt)
                Fail(SunError.WrtInvalidIdx);
            if (pCount != 0 && (indexArray == null || dataArray == null)) Fail(SunError.WrtMissingIdx);
        }
        else if (indexId != null || indexArray != null || pCount != 0 || options != 0 || indexNomen != null || dataId[0] == 'v')
            Fail(SunError.WrtInvalidArgs);

        if (_writeMode == SunCreateMode.Update)
        {
            if (pCount == 0) Fail(SunError.WrtZeroPoints);
            int ndx = _currentParmSet.FindIndex(s => string.Equals(s, n1, StringComparison.OrdinalIgnoreCase));
            if (ndx < 0) Fail(SunError.WrtRevNoMatch);
            int lPoint = dataId[0] == 'v' ? 0 : (int)pCount - 1;
            double dTime = iType switch
            {
                DataType.Float => ((float[])indexArray!)[lPoint],
                DataType.Double => ((double[])indexArray!)[lPoint],
                DataType.TimeTag => TimeMath.Time(((TimeTag[])indexArray!)[lPoint], _revRefGmt),
                _ => ((uint[])indexArray!)[lPoint] / 1000.0,
            };
            if (dTime <= _appendAfter[ndx]) Fail(SunError.WrtInvalidIdx);
            _appendAfter[ndx] = dTime;
        }

        int appendingParm = -1;
        if (IsMulti)
        {
            for (int i = 0; i < _currentMap && i < _currentParmSet.Count; i++)
                if (_currentParmSet[i] == n1) { appendingParm = i; break; }
        }
        if (appendingParm != -1 && pCount == 0) Fail(SunError.WrtInvalidArgs);
        if (_currentMap >= _nItems && appendingParm == -1) Fail(SunError.WrtFileFull);

        long blockPos0 = 0, blockPos1 = 0;
        long previousBlockAddr = 0;
        bool haveData = pCount != 0 || _nPoints != 0;

        if (haveData)
        {
            int iSz, dSz;
            bool isV = dataId[0] == 'v';
            if (isV)
            {
                iSz = indexId![1] - '0';
                dSz = (int)pCount;
                pCount = 1;
            }
            else
            {
                iSz = indexId != null ? indexId[1] - '0' : 0;
                dSz = dataId[1] - '0';
            }
            int count = (int)(pCount != 0 ? pCount : _nPoints);
            byte[] indexBuf = iSz != 0 && indexArray != null ? ToNativeBytes(indexArray, (int)pCount, iSz) : Array.Empty<byte>();
            byte[] dataBuf = isV ? ((byte[])dataArray).AsSpan(0, dSz).ToArray() : ToNativeBytes(dataArray, count, dSz);

            _fs.Position = _nextParmHeader;
            if (IsMulti)
            {
                if (appendingParm != -1)
                {
                    previousBlockAddr = _currentBlockAddr[appendingParm];
                    _currentBlockAddr[appendingParm] = _nextParmHeader;
                }
                else
                    _currentBlockAddr[_currentMap] = _nextParmHeader;
            }
            blockPos0 = _nextParmHeader;
            blockPos1 = 0;

            if (_fileSig == SunSignatures.Compressed || _fileSig == SunSignatures.CommonTime)
            {
                if (_currentMap == 0 && !dataId.StartsWith("f4", StringComparison.Ordinal))
                    Fail(SunError.WrtInvalidIdx);
                if (_currentMap == 0)
                {
                    var f = (float[])dataArray;
                    var fcopy = new float[_nPoints];
                    Array.Copy(f, fcopy, _nPoints);
                    int i;
                    for (i = 0; i < _nPoints; i++)
                    {
                        if (fcopy[i] >= 0.0)
                        {
                            if (i != 0) i--;
                            break;
                        }
                    }
                    if (i >= DBL_BLOCK && (_nPoints - i) >= DBL_BLOCK)
                    {
                        _blockPoints[0] = (uint)i;
                        Lin2Delt(fcopy, 0, (int)_blockPoints[0]);
                        _blockPoints[1] = _nPoints - (uint)i;
                        Lin2Delt(fcopy, (int)_blockPoints[0], (int)_blockPoints[1]);
                    }
                    else
                    {
                        _blockPoints[0] = _nPoints;
                        _blockPoints[1] = 0;
                        Lin2Delt(fcopy, 0, (int)_blockPoints[0]);
                    }
                    dataBuf = ToNativeBytes(fcopy, (int)_nPoints, 4);
                }
                if (dataId.StartsWith("t8", StringComparison.Ordinal)) FlipTimeTags(dataBuf, (int)_nPoints);
                else FlipElements(dataBuf, (int)_nPoints, dSz);

                var enc = new LzhEncoder();
                enc.Encode(dataBuf, 0, (int)(_blockPoints[0] * dSz), _fs);
                if (_blockPoints[1] != 0)
                {
                    blockPos1 = _fs.Position;
                    enc.Encode(dataBuf, (int)(_blockPoints[0] * dSz), (int)(_blockPoints[1] * dSz), _fs);
                }
                _nextParmHeader = _fs.Position;
            }
            else
            {
                bool storeBigEndian = (options & SunWriteOptions.OrientBigEnd) != 0;
                int headSize = Is64 ? PHEAD64SIZE : PHEADSIZE;
                long headPos = _nextParmHeader;
                _fs.Write(new byte[headSize]);
                byte nomenSize = 0;
                if (appendingParm == -1 && indexNomen != null)
                {
                    var nb = AnsiText.Encode(indexNomen);
                    int l = Math.Min(nb.Length, 255);
                    nomenSize = (byte)l;
                    _fs.Write(nb, 0, l);
                }

                byte indexType = 0;
                var first = new byte[8];
                var last = new byte[8];
                int n = (int)pCount;
                bool compress = (options & SunWriteOptions.LzhCompress) != 0;
                bool flip = storeBigEndian; // little endian host
                int timeElemSize = iSz;
                byte[] timeBytes;
                switch (iType)
                {
                    case DataType.TimeTag when (options & SunWriteOptions.PackTimeTag) != 0:
                        {
                            var tags = (TimeTag[])indexArray!;
                            indexType = (byte)DataType.PackedTime;
                            WriteTagBE(first, tags[0]);
                            WriteTagBE(last, tags[n - 1]);
                            TimeTag refGmt = tags[0];
                            var packed = new uint[n];
                            uint preTime = 0;
                            for (int j = 0; j < n; j++)
                            {
                                uint rday = unchecked((uint)(tags[j].JulianDay - refGmt.JulianDay));
                                uint mil = tags[j].MilliSeconds;
                                packed[j] = (mil & 0x07FFFFFF) | (rday << 27);
                                if (compress)
                                {
                                    uint tmp = packed[j];
                                    if (j != 0) packed[j] = unchecked(packed[j] - preTime);
                                    preTime = tmp;
                                }
                            }
                            timeElemSize = 4;
                            timeBytes = new byte[n * 4];
                            for (int j = 0; j < n; j++)
                                if (flip) BinaryPrimitives.WriteUInt32BigEndian(timeBytes.AsSpan(j * 4), packed[j]);
                                else BinaryPrimitives.WriteUInt32LittleEndian(timeBytes.AsSpan(j * 4), packed[j]);
                            break;
                        }
                    case DataType.TimeTag:
                        {
                            var src = (TimeTag[])indexArray!;
                            var tags = new TimeTag[n];
                            Array.Copy(src, tags, n);
                            indexType = (byte)DataType.TimeTag;
                            WriteTagBE(first, tags[0]);
                            WriteTagBE(last, tags[n - 1]);
                            if (compress)
                            {
                                TimeTag pre = default;
                                for (int j = 0; j < n; j++)
                                {
                                    TimeTag tmp = tags[j];
                                    if (j != 0)
                                    {
                                        tags[j].JulianDay = unchecked((short)(tags[j].JulianDay - pre.JulianDay));
                                        tags[j].MilliSeconds = unchecked(tags[j].MilliSeconds - pre.MilliSeconds);
                                        tags[j].MicroSeconds = unchecked((ushort)(tags[j].MicroSeconds - pre.MicroSeconds));
                                    }
                                    pre = tmp;
                                }
                            }
                            timeBytes = ToNativeBytes(tags, n, 8);
                            if (flip) FlipTimeTags(timeBytes, n);
                            break;
                        }
                    case DataType.ULongInt:
                        {
                            var u = (uint[])indexArray!;
                            indexType = (byte)DataType.ULongInt;
                            BinaryPrimitives.WriteUInt32BigEndian(first, u[0]);
                            BinaryPrimitives.WriteUInt32BigEndian(last, u[n - 1]);
                            timeBytes = ToNativeBytes(u, n, 4);
                            if (flip) FlipElements(timeBytes, n, 4);
                            break;
                        }
                    case DataType.Float:
                        {
                            var src = (float[])indexArray!;
                            var f = new float[n];
                            Array.Copy(src, f, n);
                            indexType = (byte)DataType.Float;
                            BinaryPrimitives.WriteSingleBigEndian(first, f[0]);
                            BinaryPrimitives.WriteSingleBigEndian(last, f[n - 1]);
                            if (compress)
                            {
                                double dSum = 0;
                                for (int j = 0; j < n; j++)
                                {
                                    if (j != 0)
                                    {
                                        f[j] = (float)((double)f[j] - dSum);
                                        dSum += (double)f[j];
                                    }
                                    else
                                        dSum = f[0];
                                }
                            }
                            timeBytes = ToNativeBytes(f, n, 4);
                            if (flip) FlipElements(timeBytes, n, 4);
                            break;
                        }
                    default:
                        {
                            var src = (double[])indexArray!;
                            var d = new double[n];
                            Array.Copy(src, d, n);
                            indexType = (byte)DataType.Double;
                            BinaryPrimitives.WriteDoubleBigEndian(first, d[0]);
                            BinaryPrimitives.WriteDoubleBigEndian(last, d[n - 1]);
                            if (compress)
                            {
                                double dSum = 0;
                                for (int j = 0; j < n; j++)
                                {
                                    if (j != 0)
                                    {
                                        d[j] = d[j] - dSum;
                                        dSum += d[j];
                                    }
                                    else
                                        dSum = d[0];
                                }
                            }
                            timeBytes = ToNativeBytes(d, n, 8);
                            if (flip) FlipElements(timeBytes, n, 8);
                            break;
                        }
                }
                if (flip && dSz > 1 && !isV)
                {
                    if (dataId.StartsWith("t8", StringComparison.Ordinal)) FlipTimeTags(dataBuf, n);
                    else FlipElements(dataBuf, n, dSz);
                }

                uint timeLzh, dataLzh;
                byte compressed;
                if (compress)
                {
                    var enc = new LzhEncoder();
                    if (isV)
                    {
                        _fs.Write(timeBytes, 0, timeElemSize * n);
                        timeLzh = (uint)dSz;
                    }
                    else
                        timeLzh = (uint)enc.Encode(timeBytes, 0, timeElemSize * n, _fs);
                    dataLzh = (uint)enc.Encode(dataBuf, 0, dSz * n, _fs);
                    compressed = 1;
                }
                else
                {
                    _fs.Write(timeBytes, 0, timeElemSize * n);
                    _fs.Write(dataBuf, 0, dSz * n);
                    compressed = 0;
                    timeLzh = dataLzh = isV ? (uint)dSz : 0;
                }
                _nextParmHeader = _fs.Position;

                var hb = new byte[headSize];
                int o = 0;
                BinaryPrimitives.WriteUInt32BigEndian(hb, pCount); o += 4;
                o += Is64 ? 8 : 4; // linked = 0
                BinaryPrimitives.WriteUInt32BigEndian(hb.AsSpan(o), timeLzh); o += 4;
                BinaryPrimitives.WriteUInt32BigEndian(hb.AsSpan(o), dataLzh); o += 4;
                hb[o++] = indexType;
                hb[o++] = nomenSize;
                hb[o++] = compressed;
                hb[o++] = (byte)(storeBigEndian ? 1 : 0);
                first.CopyTo(hb, o); o += 8;
                last.CopyTo(hb, o);
                _fs.Position = headPos;
                _fs.Write(hb);
            }
        }

        if (appendingParm == -1)
        {
            if (_fileSig == SunSignatures.Compressed)
            {
                long b = 264 + (_currentMap * 80L);
                var e = new byte[80];
                CopyText(e, 0, n1, 12);
                CopyText(e, 12, n2, 12);
                CopyText(e, 24, descr, 36);
                if (!dataId.StartsWith("f4", StringComparison.Ordinal))
                {
                    e[56] = 0x00; e[57] = 0xff; e[58] = (byte)dataId[0]; e[59] = (byte)dataId[1];
                }
                CopyText(e, 60, units, 8);
                BinaryPrimitives.WriteInt32BigEndian(e.AsSpan(68), seqNum);
                BinaryPrimitives.WriteUInt32BigEndian(e.AsSpan(72), (uint)blockPos0);
                BinaryPrimitives.WriteUInt32BigEndian(e.AsSpan(76), (uint)blockPos1);
                // fields are written in a fixed order (description may overlay the type flag) into a zero-filled entry
                _fs.Position = b;
                _fs.Write(e);
                _nextMapEntryPos = b + 80;
            }
            else
            {
                _fs.Position = _nextMapEntryPos;
                WriteVar(n1);
                WriteVar(n2);
                WriteVar(descr);
                WriteVar(units);
                _fs.WriteByte((byte)dataId[0]);
                _fs.WriteByte((byte)dataId[1]);
                Span<byte> t = stackalloc byte[12];
                BinaryPrimitives.WriteInt32BigEndian(t, seqNum);
                if (Is64)
                {
                    BinaryPrimitives.WriteInt64BigEndian(t[4..], blockPos0);
                }
                else
                {
                    BinaryPrimitives.WriteUInt32BigEndian(t[4..], (uint)blockPos0);
                    BinaryPrimitives.WriteUInt32BigEndian(t[8..], (uint)blockPos1);
                }
                _fs.Write(t);
                _nextMapEntryPos = _fs.Position;
            }
            if (IsMulti) _currentParmSet.Add(n1);
            _currentMap++;
            if (_currentMap == _nItems)
            {
                WriteU32BE(28, _nItems);
                Span<byte> sig = stackalloc byte[4];
                BinaryPrimitives.WriteUInt32LittleEndian(sig, _fileSig);
                WriteAt(0, sig);
                if ((options & SunWriteOptions.ManualUpdate) == 0) _fs.Flush(true);
            }
        }
        else
        {
            long linkPos = previousBlockAddr == 0 ? MapBlockPtrPosition(appendingParm) : previousBlockAddr + 4;
            Span<byte> lb = stackalloc byte[8];
            if (Is64)
            {
                BinaryPrimitives.WriteInt64BigEndian(lb, _currentBlockAddr[appendingParm]);
                WriteAt(linkPos, lb);
            }
            else
            {
                BinaryPrimitives.WriteUInt32BigEndian(lb, (uint)_currentBlockAddr[appendingParm]);
                WriteAt(linkPos, lb[..4]);
            }
            if ((options & SunWriteOptions.ManualUpdate) == 0)
            {
                WriteU32BE(36, ++_blockPoints[1]);
                _fs.Flush(true);
            }
        }
    }

    private static void ReadExact(Stream s, byte[] b)
    {
        int t = 0;
        while (t < b.Length)
        {
            int n = s.Read(b, t, b.Length - t);
            if (n <= 0) break;
            t += n;
        }
    }

    private static void CopyText(byte[] dst, int offset, string s, int max)
    {
        var b = AnsiText.Encode(s);
        Array.Copy(b, 0, dst, offset, Math.Min(b.Length, max));
    }

    private static void WriteTagBE(byte[] dst, TimeTag t)
    {
        BinaryPrimitives.WriteUInt32BigEndian(dst, t.MilliSeconds);
        BinaryPrimitives.WriteUInt16BigEndian(dst.AsSpan(4), t.MicroSeconds);
        BinaryPrimitives.WriteInt16BigEndian(dst.AsSpan(6), t.JulianDay);
    }

    private void WriteVar(string s)
    {
        var b = AnsiText.Encode(s);
        int l = Math.Min(b.Length, 255);
        _fs.WriteByte((byte)l);
        if (l > 0) _fs.Write(b, 0, l);
    }

    /// <summary>File position of a variable map entry's block pointer.</summary>
    private long MapBlockPtrPosition(int iParm)
    {
        long save = _fs.Position;
        _fs.Position = 264;
        long pos = 0;
        for (int i = 0; i <= iParm; i++)
        {
            for (int k = 0; k < 4; k++)
            {
                int len = _fs.ReadByte();
                if (len > 0) _fs.Seek(len, SeekOrigin.Current);
            }
            _fs.Seek(6, SeekOrigin.Current);
            pos = _fs.Position;
            _fs.Seek(8, SeekOrigin.Current);
        }
        _fs.Position = save;
        return pos;
    }

    /// <summary>Linear to delta time.</summary>
    internal static void Lin2Delt(float[] x, int offset, int np)
    {
        if (np == 0) return;
        double runsum = x[offset];
        for (int p = 1; p < np; p++)
        {
            x[offset + p] = (float)((double)x[offset + p] - runsum);
            runsum += (double)x[offset + p];
        }
    }

    /// <summary>Increments the MULTITIME update counter.</summary>
    public uint ShowFileUpdated()
    {
        if (!IsMulti) { Status = SunError.WrtNotValid; return 0; }
        WriteU32BE(36, ++_blockPoints[1]);
        _fs.Flush(true);
        return _blockPoints[1];
    }

    private void Fail(SunError e)
    {
        Status = e;
        throw new DataAccessException(e);
    }

    /// <summary>Re-tags _SRT files as standard MULTITIME, then closes.</summary>
    public void CloseSrt()
    {
        if (_fileSig == SunSignatures.MultiTimeSrt || _fileSig == SunSignatures.MultiTime64Srt)
        {
            _fileSig = _fileSig == SunSignatures.MultiTimeSrt ? SunSignatures.MultiTime : SunSignatures.MultiTime64;
            Span<byte> sig = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(sig, _fileSig);
            WriteAt(0, sig);
        }
        Close();
    }

    /// <summary>Finishes the header, relocates the comment block and packs data.</summary>
    public void Close()
    {
        if (_closed) return;
        _closed = true;
        uint actParmCnt = _currentMap;
        if (actParmCnt != _nItems) Status = SunError.WrtNotEnoParms;

        WriteU32BE(28, _currentMap);
        uint b0 = (_fileSig == SunSignatures.MultiTime || _fileSig == SunSignatures.MultiTime64) ? _blockPoints[1] : _blockPoints[0];
        WriteU32BE(32, b0);
        WriteU32BE(36, _blockPoints[1]);

        bool create = _writeMode == SunCreateMode.Create;
        if ((create && (_fileSig == SunSignatures.MultiTime || _fileSig == SunSignatures.MultiTime64 || _fileSig == SunSignatures.CommonTime))
            || (_fileSig == SunSignatures.Compressed && Status == SunError.WrtNotEnoParms))
        {
            if (_commentSize != 0)
            {
                var buf = new byte[_commentSize];
                _fs.Position = 264 + (_nItems * (long)_mapEntrySize);
                int got = 0;
                while (got < buf.Length) { int n = _fs.Read(buf, got, buf.Length - got); if (n <= 0) break; got += n; }
                if (got == buf.Length)
                {
                    _fs.Position = _nextMapEntryPos;
                    _fs.Write(buf);
                }
            }
        }

        long truncateBy = 0;
        if (create && (_fileSig == SunSignatures.MultiTime || _fileSig == SunSignatures.MultiTime64 || _fileSig == SunSignatures.CommonTime))
        {
            if (_fileSig != SunSignatures.MultiTime64 && !(_fileSig == SunSignatures.MultiTime && _blockPoints[1] != 0))
            {
                long sourcePos = 264 + (_nItems * (long)_mapEntrySize) + _commentSize;
                long destPos = _nextMapEntryPos + _commentSize;
                long packSpace = sourcePos - destPos;
                if (packSpace > 8192)
                {
                    truncateBy = packSpace;
                    var chunk = new byte[8192];
                    while (true)
                    {
                        _fs.Position = sourcePos;
                        int n = 0;
                        while (n < chunk.Length) { int r = _fs.Read(chunk, n, chunk.Length - n); if (r <= 0) break; n += r; }
                        _fs.Position = destPos;
                        _fs.Write(chunk, 0, n);
                        sourcePos += n;
                        destPos += n;
                        if (n < chunk.Length) break;
                    }
                    long pos = 264;
                    for (int i = 0; i < actParmCnt; i++)
                    {
                        _fs.Position = pos;
                        for (int k = 0; k < 4; k++)
                        {
                            int len = _fs.ReadByte();
                            if (len > 0) _fs.Seek(len, SeekOrigin.Current);
                        }
                        _fs.Seek(6, SeekOrigin.Current);
                        long ptrPos = _fs.Position;
                        var pb = new byte[8];
                        ReadExact(_fs, pb);
                        uint p0 = BinaryPrimitives.ReadUInt32BigEndian(pb);
                        uint p1 = BinaryPrimitives.ReadUInt32BigEndian(pb.AsSpan(4));
                        p0 -= (uint)packSpace;
                        p1 -= p1 != 0 ? (uint)packSpace : 0;
                        BinaryPrimitives.WriteUInt32BigEndian(pb, p0);
                        BinaryPrimitives.WriteUInt32BigEndian(pb.AsSpan(4), p1);
                        _fs.Position = ptrPos;
                        _fs.Write(pb);
                        pos = ptrPos + 8;
                    }
                }
            }
        }

        if (create)
        {
            Span<byte> sig = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(sig, _fileSig);
            WriteAt(0, sig);
        }
        if (truncateBy > 0) _fs.SetLength(_fs.Length - truncateBy);
        _fs.Flush(true);
        _fs.Dispose();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_closed)
        {
            try { Close(); } catch (IOException) { }
        }
    }
}
