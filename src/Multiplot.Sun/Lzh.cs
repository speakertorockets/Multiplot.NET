using Multiplot.Plugins;

namespace Multiplot.Sun;

/// <summary>
/// LZH decoder — the ar002 "-lh5-" static Huffman LZSS decoder
/// (FR-LZH-001..004, 006). Raw bit stream: no header, no CRC, no end
/// marker; the compressed length is supplied by the caller. Input beyond the supplied size reads as
/// zero bits. One instance per decode.
/// </summary>
public sealed class LzhDecoder
{
    private const int DICBIT = 13;
    private const int DICSIZ = 1 << DICBIT;
    private const int UCHAR_MAX = 255;
    private const int MAXMATCH = 256;
    private const int THRESHOLD = 3;
    private const int NC = UCHAR_MAX + MAXMATCH + 2 - THRESHOLD; // 510
    private const int CBIT = 9;
    private const int CODE_BIT = 16;
    private const int NP = DICBIT + 1;    // 14
    private const int NT = CODE_BIT + 3;  // 19
    private const int PBIT = 4;
    private const int TBIT = 5;
    private const int NPT = NT;
    private const int BITBUFSIZ = 32;
    private const int CHAR_BIT = 8;

    private uint _bitbuf;
    private uint _subbitbuf;
    private int _bitcount;
    private readonly ushort[] _left = new ushort[2 * NC - 1];
    private readonly ushort[] _right = new ushort[2 * NC - 1];
    private readonly byte[] _cLen = new byte[NC];
    private readonly byte[] _ptLen = new byte[NPT];
    private uint _blocksize;
    private readonly ushort[] _cTable = new ushort[4096];
    private readonly ushort[] _ptTable = new ushort[256];
    private readonly byte[] _window = new byte[DICSIZ];

    private Stream _in = Stream.Null;
    private long _compsize;
    private SunError _error;

    /// <summary>
    /// Decodes from the current position of <paramref name="input"/>, storing output bytes
    /// <c>[strb, stpb)</c> into <paramref name="dest"/> at <paramref name="destOffset"/>.
    /// Reads at most <paramref name="compressedSize"/> bytes from the stream.
    /// </summary>
    /// <returns><see cref="SunError.Success"/>, <see cref="SunError.LzwDict"/> or <see cref="SunError.LzwReadFail"/>.</returns>
    public SunError Decode(Stream input, long compressedSize, byte[] dest, int destOffset, long strb, long stpb)
    {
        _in = input;
        _compsize = compressedSize;
        _error = SunError.Success;
        _bitbuf = _subbitbuf = 0;
        _bitcount = 0;
        _blocksize = 0;
        if (stpb <= strb || stpb <= 0) return SunError.Success;

        InitGetBits();
        if (_error != 0) return _error;

        long pos = 0;          // absolute output position
        int bpos = destOffset; // destination index
        int copyLeft = 0;      // static j in ar002
        uint copySrc = 0;      // static i in ar002
        while (true)
        {
            if (copyLeft > 0)
            {
                byte b = _window[copySrc];
                copySrc = (copySrc + 1) & (DICSIZ - 1);
                copyLeft--;
                _window[pos & (DICSIZ - 1)] = b;
                if (pos >= strb)
                {
                    if (bpos >= dest.Length) return SunError.LzwDict;
                    dest[bpos++] = b;
                }
                pos++;
                if (pos == stpb) return SunError.Success;
                continue;
            }

            uint c = DecodeC();
            if (_error != 0) return _error;
            if (c <= UCHAR_MAX)
            {
                _window[pos & (DICSIZ - 1)] = (byte)c;
                if (pos >= strb)
                {
                    if (bpos >= dest.Length) return SunError.LzwDict;
                    dest[bpos++] = (byte)c;
                }
                pos++;
                if (pos == stpb) return SunError.Success;
            }
            else
            {
                copyLeft = (int)(c - (UCHAR_MAX + 1 - THRESHOLD));
                uint p = DecodeP();
                if (_error != 0) return _error;
                copySrc = (uint)((pos & (DICSIZ - 1)) - p - 1) & (DICSIZ - 1);
            }
        }
    }

    /// <summary>Convenience: decode a whole block of known uncompressed length.</summary>
    public static byte[] DecodeAll(byte[] compressed, int outputLength)
    {
        var dest = new byte[outputLength];
        using var ms = new MemoryStream(compressed, false);
        var err = new LzhDecoder().Decode(ms, compressed.Length, dest, 0, 0, outputLength);
        if (err != SunError.Success) throw new DataAccessException(err);
        return dest;
    }

    private void ReadPtLen(int nn, int nbit, int iSpecial)
    {
        int i, c, n;
        uint mask;

        n = (int)GetBits(nbit);
        if (_error != 0) return;

        if (n == 0)
        {
            c = (int)GetBits(nbit);
            if (_error != 0) return;
            for (i = 0; i < nn; i++) _ptLen[i] = 0;
            for (i = 0; i < 256; i++) _ptTable[i] = (ushort)c;
        }
        else
        {
            i = 0;
            while (i < n)
            {
                c = (int)(_bitbuf >> (BITBUFSIZ - 3));
                if (c == 7)
                {
                    mask = 1U << (BITBUFSIZ - 1 - 3);
                    while ((mask & _bitbuf) != 0)
                    {
                        mask >>= 1;
                        c++;
                    }
                }
                FillBuf((c < 7) ? 3 : c - 3);
                if (_error != 0) return;

                if (i >= NPT) { _error = SunError.LzwDict; return; }
                _ptLen[i++] = (byte)c;
                if (i == iSpecial)
                {
                    c = (int)GetBits(2);
                    if (_error != 0) return;
                    while (--c >= 0)
                    {
                        if (i >= NPT) { _error = SunError.LzwDict; return; }
                        _ptLen[i++] = 0;
                    }
                }
            }
            while (i < nn) _ptLen[i++] = 0;

            MakeTable(nn, _ptLen, 8, _ptTable);
        }
    }

    private void ReadCLen()
    {
        int i, c, n;
        uint mask;

        n = (int)GetBits(CBIT);
        if (_error != 0) return;

        if (n == 0)
        {
            c = (int)GetBits(CBIT);
            if (_error != 0) return;
            for (i = 0; i < NC; i++) _cLen[i] = 0;
            for (i = 0; i < 4096; i++) _cTable[i] = (ushort)c;
        }
        else
        {
            i = 0;
            while (i < n)
            {
                c = _ptTable[_bitbuf >> (BITBUFSIZ - 8)];
                if (c >= NT)
                {
                    mask = 1U << (BITBUFSIZ - 1 - 8);
                    int guard = 0;
                    do
                    {
                        if (c >= _left.Length || ++guard > 64) { _error = SunError.LzwDict; return; }
                        c = (_bitbuf & mask) != 0 ? _right[c] : _left[c];
                        mask >>= 1;
                    } while (c >= NT);
                }

                FillBuf(_ptLen[c]);
                if (_error != 0) return;

                if (c <= 2)
                {
                    if (c == 0) c = 1;
                    else if (c == 1) c = (int)GetBits(4) + 3;
                    else c = (int)GetBits(CBIT) + 20;
                    if (_error != 0) return;
                    while (--c >= 0)
                    {
                        if (i >= NC) { _error = SunError.LzwDict; return; }
                        _cLen[i++] = 0;
                    }
                }
                else
                {
                    if (i >= NC) { _error = SunError.LzwDict; return; }
                    _cLen[i++] = (byte)(c - 2);
                }
            }
            while (i < NC) _cLen[i++] = 0;
            MakeTable(NC, _cLen, 12, _cTable);
        }
    }

    private uint DecodeC()
    {
        uint j, mask;
        if (_blocksize == 0)
        {
            _blocksize = GetBits(16);
            if (_error != 0) return 0;
            ReadPtLen(NT, TBIT, 3);
            if (_error != 0) return 0;
            ReadCLen();
            if (_error != 0) return 0;
            ReadPtLen(NP, PBIT, -1);
            if (_error != 0) return 0;
        }
        _blocksize--;
        j = _cTable[_bitbuf >> (BITBUFSIZ - 12)];
        if (j >= NC)
        {
            mask = 1U << (BITBUFSIZ - 1 - 12);
            int guard = 0;
            do
            {
                if (j >= _left.Length || ++guard > 64) { _error = SunError.LzwDict; return 0; }
                j = (_bitbuf & mask) != 0 ? _right[j] : _left[j];
                mask >>= 1;
            } while (j >= NC);
        }
        FillBuf(_cLen[j]);
        return j;
    }

    private uint DecodeP()
    {
        uint j, mask;
        j = _ptTable[_bitbuf >> (BITBUFSIZ - 8)];
        if (j >= NP)
        {
            mask = 1U << (BITBUFSIZ - 1 - 8);
            int guard = 0;
            do
            {
                if (j >= _left.Length || ++guard > 64) { _error = SunError.LzwDict; return 0; }
                j = (_bitbuf & mask) != 0 ? _right[j] : _left[j];
                mask >>= 1;
            } while (j >= NP);
        }
        FillBuf(_ptLen[j]);
        if (_error != 0) return 0;
        if (j != 0)
            j = (1U << (int)(j - 1)) + GetBits((int)(j - 1));
        return j;
    }

    private void FillBuf(int n)
    {
        _bitbuf <<= n;
        while (n > _bitcount)
        {
            _bitbuf |= _subbitbuf << (n -= _bitcount);
            if (_compsize != 0)
            {
                _compsize--;
                int b;
                try { b = _in.ReadByte(); }
                catch (IOException) { _error = SunError.LzwReadFail; break; }
                _subbitbuf = (byte)b; // (uChar)getc(): EOF reads as 0xFF
            }
            else
                _subbitbuf = 0;
            _bitcount = CHAR_BIT;
        }
        if (_error == 0)
            _bitbuf |= _subbitbuf >> (_bitcount -= n);
    }

    private uint GetBits(int n)
    {
        if (n == 0) return 0;
        uint x = _bitbuf >> (BITBUFSIZ - n);
        FillBuf(n);
        return x;
    }

    private void InitGetBits()
    {
        _bitbuf = 0; _subbitbuf = 0; _bitcount = 0;
        FillBuf(BITBUFSIZ);
    }

    private void MakeTable(int nchar, byte[] bitlen, int tablebits, ushort[] table)
    {
        ushort[] count = new ushort[17], weight = new ushort[17], start = new ushort[18];
        int i, k, len, ch;
        uint jutbits, avail, nextcode, mask;

        for (i = 1; i <= 16; i++) count[i] = 0;
        for (i = 0; i < nchar; i++)
        {
            if (bitlen[i] > 16) { _error = SunError.LzwDict; return; }
            count[bitlen[i]]++;
        }

        start[1] = 0;
        for (i = 1; i <= 16; i++)
            start[i + 1] = unchecked((ushort)(start[i] + (count[i] << (16 - i))));
        if (start[17] != unchecked((ushort)(1U << 16)))
        {
            _error = SunError.LzwDict;
            return;
        }

        jutbits = (uint)(16 - tablebits);
        for (i = 1; i <= tablebits; i++)
        {
            start[i] >>= (int)jutbits;
            weight[i] = (ushort)(1U << (tablebits - i));
        }
        while (i <= 16)
        {
            weight[i] = (ushort)(1U << (16 - i));
            i++;
        }

        i = start[tablebits + 1] >> (int)jutbits;
        if (i != unchecked((ushort)(1U << 16)))
        {
            k = 1 << tablebits;
            while (i != k)
            {
                if (i >= table.Length) { _error = SunError.LzwDict; return; }
                table[i++] = 0;
            }
        }

        avail = (uint)nchar;
        mask = 1U << (15 - tablebits);
        for (ch = 0; ch < nchar; ch++)
        {
            if ((len = bitlen[ch]) == 0) continue;
            nextcode = (uint)(start[len] + weight[len]);
            if (len <= tablebits)
            {
                if (nextcode > table.Length) { _error = SunError.LzwDict; return; }
                for (i = start[len]; i < (int)nextcode; i++) table[i] = (ushort)ch;
            }
            else
            {
                k = start[len];
                int pIndex = k >> (int)jutbits;
                // p points into table first, then into left/right
                int which = 0; // 0 table, 1 left, 2 right
                if (pIndex >= table.Length) { _error = SunError.LzwDict; return; }
                i = len - tablebits;
                while (i != 0)
                {
                    ushort pv = which == 0 ? table[pIndex] : which == 1 ? _left[pIndex] : _right[pIndex];
                    if (pv == 0)
                    {
                        if (avail >= _left.Length) { _error = SunError.LzwDict; return; }
                        _right[avail] = _left[avail] = 0;
                        pv = (ushort)avail++;
                        if (which == 0) table[pIndex] = pv; else if (which == 1) _left[pIndex] = pv; else _right[pIndex] = pv;
                    }
                    if (pv >= _left.Length) { _error = SunError.LzwDict; return; }
                    if ((k & mask) != 0) { which = 2; pIndex = pv; }
                    else { which = 1; pIndex = pv; }
                    k <<= 1; i--;
                }
                if (which == 0) table[pIndex] = (ushort)ch; else if (which == 1) _left[pIndex] = (ushort)ch; else _right[pIndex] = (ushort)ch;
            }
            start[len] = (ushort)nextcode;
        }
    }
}
