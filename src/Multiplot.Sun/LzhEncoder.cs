// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

namespace Multiplot.Sun;

/// <summary>
/// LZH encoder — the ar002 encoder (FR-LZH-005):
/// binary-tree match finder with PERCOLATE, 8 KiB symbol buffer, <c>send_block</c> when the buffer
/// is nearly full and a final <c>putbits(7, 0)</c> flush. Streams decode with <see cref="LzhDecoder"/>
/// and with other ar002 decoders.
/// </summary>
public sealed class LzhEncoder
{
    private const int DICBIT = 13;
    private const int DICSIZ = 1 << DICBIT;
    private const int UCHAR_MAX = 255;
    private const int MAXMATCH = 256;
    private const int THRESHOLD = 3;
    private const int NC = UCHAR_MAX + MAXMATCH + 2 - THRESHOLD;
    private const int CBIT = 9;
    private const int CODE_BIT = 16;
    private const int NP = DICBIT + 1;
    private const int NT = CODE_BIT + 3;
    private const int PBIT = 4;
    private const int TBIT = 5;
    private const int NPT = NT;
    private const int CHAR_BIT = 8;
    private const ushort PERC_FLAG = 0x8000;
    private const short NIL = 0;
    private const int MAX_HASH_VAL = 3 * DICSIZ + (DICSIZ / 512 + 1) * UCHAR_MAX;

    private readonly ushort[] _left = new ushort[2 * NC - 1];
    private readonly ushort[] _right = new ushort[2 * NC - 1];
    private readonly byte[] _buf = new byte[8192];
    private readonly byte[] _cLen = new byte[NC];
    private readonly byte[] _ptLen = new byte[NPT];
    private const uint Bufsiz = 8192;
    private readonly ushort[] _cFreq = new ushort[2 * NC - 1];
    private readonly ushort[] _cCode = new ushort[NC];
    private readonly ushort[] _pFreq = new ushort[2 * NP - 1];
    private readonly ushort[] _ptCode = new ushort[NPT];
    private readonly ushort[] _tFreq = new ushort[2 * NT - 1];

    private uint _subbitbuf;
    private int _bitcount;
    private long _compsize;

    // make_tree state
    private int _n, _heapsize;
    private readonly short[] _heap = new short[NC + 1];
    private ushort[] _freq = Array.Empty<ushort>();
    private byte[] _len = Array.Empty<byte>();
    private ushort[] _sortArr = Array.Empty<ushort>();
    private int _sortPtr;
    private readonly ushort[] _lenCnt = new ushort[17];
    private int _depth;

    // slide dictionary
    private readonly byte[] _text = new byte[DICSIZ * 2 + MAXMATCH];
    private readonly byte[] _level = new byte[DICSIZ + UCHAR_MAX + 1];
    private readonly byte[] _childcount = new byte[DICSIZ + UCHAR_MAX + 1];
    private readonly short[] _position = new short[DICSIZ + UCHAR_MAX + 1];
    private readonly short[] _parent = new short[DICSIZ * 2];
    private readonly short[] _prev = new short[DICSIZ * 2];
    private readonly short[] _next = new short[MAX_HASH_VAL + 1];
    private short _pos, _matchpos, _avail;
    private int _remaindr, _matchlen;

    private uint _outputPos, _outputMask, _cpos;

    private byte[] _inbuf = Array.Empty<byte>();
    private long _ipos, _insize;
    private Stream _out = Stream.Null;

    /// <summary>Compresses <paramref name="count"/> bytes of <paramref name="data"/> to <paramref name="output"/>; returns the compressed byte count.</summary>
    public long Encode(byte[] data, int offset, int count, Stream output)
    {
        if (offset != 0)
        {
            var copy = new byte[count];
            Buffer.BlockCopy(data, offset, copy, 0, count);
            data = copy;
        }
        _inbuf = data;
        _insize = count;
        _ipos = 0;
        _out = output;
        _subbitbuf = 0;
        _bitcount = 0;
        _compsize = 0;
        DoEncode();
        return _compsize;
    }

    /// <summary>Convenience: compresses a whole buffer into a new array.</summary>
    public static byte[] EncodeAll(byte[] data)
    {
        using var ms = new MemoryStream();
        new LzhEncoder().Encode(data, 0, data.Length, ms);
        return ms.ToArray();
    }

    private int FreadCrc(int dest, int n)
    {
        int b = 0;
        for (int i = 0; i < n; i++)
        {
            if (_ipos < _insize)
            {
                _text[dest + i] = _inbuf[_ipos++];
                b++;
            }
        }
        return b;
    }

    private void EncPutc(uint ch)
    {
        _out.WriteByte((byte)ch);
    }

    private void InitSlide()
    {
        short i;
        for (i = DICSIZ; i <= DICSIZ + UCHAR_MAX; i++)
        {
            _level[i] = 1;
            _position[i] = NIL;
        }
        for (i = DICSIZ; i < DICSIZ * 2; i++) _parent[i] = NIL;
        _avail = 1;
        for (i = 1; i < DICSIZ - 1; i++) _next[i] = (short)(i + 1);
        _next[DICSIZ - 1] = NIL;
        for (int k = DICSIZ * 2; k <= MAX_HASH_VAL; k++) _next[k] = NIL;
    }

    private static int Hash(int p, int c) => p + (c << (DICBIT - 9)) + DICSIZ * 2;

    private short Child(short q, byte c)
    {
        short r = _next[Hash(q, c)];
        _parent[NIL] = q;
        while (_parent[r] != q) r = _next[r];
        return r;
    }

    private void MakeChild(short q, byte c, short r)
    {
        short h = (short)Hash(q, c);
        short t = _next[h];
        _next[h] = r;
        _next[r] = t;
        _prev[t] = r;
        _prev[r] = h;
        _parent[r] = q;
        _childcount[q]++;
    }

    private void Split(short old)
    {
        short nw = _avail;
        _avail = _next[nw];
        _childcount[nw] = 0;
        short t = _prev[old];
        _prev[nw] = t;
        _next[t] = nw;
        t = _next[old];
        _next[nw] = t;
        _prev[t] = nw;
        _parent[nw] = _parent[old];
        _level[nw] = unchecked((byte)_matchlen);
        _position[nw] = _pos;
        MakeChild(nw, _text[_matchpos + _matchlen], old);
        MakeChild(nw, _text[_pos + _matchlen], _pos);
    }

    private void InsertNode()
    {
        short q, r, j, t;
        if (_matchlen >= 4)
        {
            _matchlen--;
            r = (short)((_matchpos + 1) | DICSIZ);
            while ((q = _parent[r]) == NIL) r = _next[r];
            while (_level[q] >= _matchlen)
            {
                r = q;
                q = _parent[q];
            }
            t = q;
            while (_position[t] < 0)
            {
                _position[t] = _pos;
                t = _parent[t];
            }
            if (t < DICSIZ) _position[t] = unchecked((short)(_pos | PERC_FLAG));
        }
        else
        {
            q = (short)(_text[_pos] + DICSIZ);
            byte c = _text[_pos + 1];
            if ((r = Child(q, c)) == NIL)
            {
                MakeChild(q, c, _pos);
                _matchlen = 1;
                return;
            }
            _matchlen = 2;
        }
        for (; ; )
        {
            if (r >= DICSIZ)
            {
                j = MAXMATCH;
                _matchpos = r;
            }
            else
            {
                j = _level[r];
                _matchpos = unchecked((short)(_position[r] & ~PERC_FLAG));
            }
            if (_matchpos >= _pos) _matchpos -= DICSIZ;
            int t1 = _pos + _matchlen, t2 = _matchpos + _matchlen;
            while (_matchlen < j)
            {
                if (_text[t1] != _text[t2])
                {
                    Split(r);
                    return;
                }
                _matchlen++;
                t1++;
                t2++;
            }
            if (_matchlen >= MAXMATCH) break;
            _position[r] = _pos;
            q = r;
            if ((r = Child(q, _text[t1])) == NIL)
            {
                MakeChild(q, _text[t1], _pos);
                return;
            }
            _matchlen++;
        }
        t = _prev[r];
        _prev[_pos] = t;
        _next[t] = _pos;
        t = _next[r];
        _next[_pos] = t;
        _prev[t] = _pos;
        _parent[_pos] = q;
        _parent[r] = NIL;
        _next[r] = _pos;
    }

    private void DeleteNode()
    {
        short q, r, s, t, u;
        if (_parent[_pos] == NIL) return;
        r = _prev[_pos];
        s = _next[_pos];
        _next[r] = s;
        _prev[s] = r;
        r = _parent[_pos];
        _parent[_pos] = NIL;
        if (r >= DICSIZ || --_childcount[r] > 1) return;
        t = unchecked((short)(_position[r] & ~PERC_FLAG));
        if (t >= _pos) t -= DICSIZ;
        s = t;
        q = _parent[r];
        while (((u = _position[q]) & unchecked((short)PERC_FLAG)) != 0)
        {
            u &= unchecked((short)~PERC_FLAG);
            if (u >= _pos) u -= DICSIZ;
            if (u > s) s = u;
            _position[q] = (short)(s | DICSIZ);
            q = _parent[q];
        }
        if (q < DICSIZ)
        {
            if (u >= _pos) u -= DICSIZ;
            if (u > s) s = u;
            _position[q] = unchecked((short)(s | DICSIZ | PERC_FLAG));
        }
        s = Child(r, _text[t + _level[r]]);
        t = _prev[s];
        u = _next[s];
        _next[t] = u;
        _prev[u] = t;
        t = _prev[r];
        _next[t] = s;
        _prev[s] = t;
        t = _next[r];
        _prev[t] = s;
        _next[s] = t;
        _parent[s] = _parent[r];
        _parent[r] = NIL;
        _next[r] = _avail;
        _avail = r;
    }

    private void GetNextMatch()
    {
        _remaindr--;
        if (++_pos == DICSIZ * 2)
        {
            Buffer.BlockCopy(_text, DICSIZ, _text, 0, DICSIZ + MAXMATCH);
            int n = FreadCrc(DICSIZ + MAXMATCH, DICSIZ);
            _remaindr += n;
            _pos = DICSIZ;
        }
        DeleteNode();
        InsertNode();
    }

    private void DoEncode()
    {
        int lastmatchlen;
        short lastmatchpos;

        Array.Clear(_text);
        InitSlide();
        HufEncodeStart();
        _remaindr = FreadCrc(DICSIZ, DICSIZ + MAXMATCH);
        _matchlen = 0;
        _pos = DICSIZ;
        InsertNode();
        if (_matchlen > _remaindr) _matchlen = _remaindr;
        while (_remaindr > 0)
        {
            lastmatchlen = _matchlen;
            lastmatchpos = _matchpos;
            GetNextMatch();
            if (_matchlen > _remaindr) _matchlen = _remaindr;
            if (_matchlen > lastmatchlen || lastmatchlen < THRESHOLD)
                Output(_text[_pos - 1], 0);
            else
            {
                Output((uint)(lastmatchlen + (UCHAR_MAX + 1 - THRESHOLD)),
                       (uint)((_pos - lastmatchpos - 2) & (DICSIZ - 1)));
                while (--lastmatchlen > 0) GetNextMatch();
                if (_matchlen > _remaindr) _matchlen = _remaindr;
            }
        }
        HufEncodeEnd();
    }

    private void CountTFreq()
    {
        int i, k, n, count;
        for (i = 0; i < NT; i++) _tFreq[i] = 0;
        n = NC;
        while (n > 0 && _cLen[n - 1] == 0) n--;
        i = 0;
        while (i < n)
        {
            k = _cLen[i++];
            if (k == 0)
            {
                count = 1;
                while (i < n && _cLen[i] == 0) { i++; count++; }
                if (count <= 2) _tFreq[0] += (ushort)count;
                else if (count <= 18) _tFreq[1]++;
                else if (count == 19) { _tFreq[0]++; _tFreq[1]++; }
                else _tFreq[2]++;
            }
            else _tFreq[k + 2]++;
        }
    }

    private void WritePtLen(int n, int nbit, int iSpecial)
    {
        int i, k;
        while (n > 0 && _ptLen[n - 1] == 0) n--;
        PutBits(nbit, (uint)n);
        i = 0;
        while (i < n)
        {
            k = _ptLen[i++];
            if (k <= 6) PutBits(3, (uint)k);
            else PutBits(k - 3, (1U << (k - 3)) - 2);
            if (i == iSpecial)
            {
                while (i < 6 && _ptLen[i] == 0) i++;
                PutBits(2, (uint)((i - 3) & 3));
            }
        }
    }

    private void WriteCLen()
    {
        int i, k, n, count;
        n = NC;
        while (n > 0 && _cLen[n - 1] == 0) n--;
        PutBits(CBIT, (uint)n);
        i = 0;
        while (i < n)
        {
            k = _cLen[i++];
            if (k == 0)
            {
                count = 1;
                while (i < n && _cLen[i] == 0) { i++; count++; }
                if (count <= 2)
                {
                    for (k = 0; k < count; k++)
                        PutBits(_ptLen[0], _ptCode[0]);
                }
                else if (count <= 18)
                {
                    PutBits(_ptLen[1], _ptCode[1]);
                    PutBits(4, (uint)(count - 3));
                }
                else if (count == 19)
                {
                    PutBits(_ptLen[0], _ptCode[0]);
                    PutBits(_ptLen[1], _ptCode[1]);
                    PutBits(4, 15);
                }
                else
                {
                    PutBits(_ptLen[2], _ptCode[2]);
                    PutBits(CBIT, (uint)(count - 20));
                }
            }
            else PutBits(_ptLen[k + 2], _ptCode[k + 2]);
        }
    }

    private void EncodeC(int c) => PutBits(_cLen[c], _cCode[c]);

    private void EncodeP(uint p)
    {
        uint c = 0, q = p;
        while (q != 0) { q >>= 1; c++; }
        PutBits(_ptLen[c], _ptCode[c]);
        if (c > 1) PutBits((int)(c - 1), p & (0xFFFFU >> (int)(17 - c)));
    }

    private void SendBlock()
    {
        uint i, k, flags = 0, root, pos, size;
        root = (uint)MakeTree(NC, _cFreq, _cLen, _cCode);
        size = _cFreq[root];
        PutBits(16, size);
        if (root >= NC)
        {
            CountTFreq();
            root = (uint)MakeTree(NT, _tFreq, _ptLen, _ptCode);
            if (root >= NT)
                WritePtLen(NT, TBIT, 3);
            else
            {
                PutBits(TBIT, 0);
                PutBits(TBIT, root);
            }
            WriteCLen();
        }
        else
        {
            PutBits(TBIT, 0);
            PutBits(TBIT, 0);
            PutBits(CBIT, 0);
            PutBits(CBIT, root);
        }
        root = (uint)MakeTree(NP, _pFreq, _ptLen, _ptCode);
        if (root >= NP)
            WritePtLen(NP, PBIT, -1);
        else
        {
            PutBits(PBIT, 0);
            PutBits(PBIT, root);
        }
        pos = 0;
        for (i = 0; i < size; i++)
        {
            if (i % CHAR_BIT == 0) flags = _buf[pos++]; else flags <<= 1;
            if ((flags & (1U << (CHAR_BIT - 1))) != 0)
            {
                EncodeC(_buf[pos++] + (1 << CHAR_BIT));
                k = (uint)_buf[pos++] << CHAR_BIT;
                k += _buf[pos++];
                EncodeP(k);
            }
            else EncodeC(_buf[pos++]);
        }
        for (i = 0; i < NC; i++) _cFreq[i] = 0;
        for (i = 0; i < NP; i++) _pFreq[i] = 0;
    }

    private void Output(uint c, uint p)
    {
        if ((_outputMask >>= 1) == 0)
        {
            _outputMask = 1U << (CHAR_BIT - 1);
            if (_outputPos >= Bufsiz - 3 * CHAR_BIT)
            {
                SendBlock();
                _outputPos = 0;
            }
            _cpos = _outputPos++;
            _buf[_cpos] = 0;
        }
        _buf[_outputPos++] = (byte)c;
        _cFreq[c]++;
        if (c >= (1U << CHAR_BIT))
        {
            _buf[_cpos] |= (byte)_outputMask;
            _buf[_outputPos++] = (byte)(p >> CHAR_BIT);
            _buf[_outputPos++] = (byte)p;
            c = 0;
            while (p != 0) { p >>= 1; c++; }
            _pFreq[c]++;
        }
    }

    private void HufEncodeStart()
    {
        _buf[0] = 0;
        for (int i = 0; i < NC; i++) _cFreq[i] = 0;
        for (int i = 0; i < NP; i++) _pFreq[i] = 0;
        _outputPos = _outputMask = 0;
        _cpos = 0;
        InitPutBits();
    }

    private void HufEncodeEnd()
    {
        SendBlock();
        PutBits(CHAR_BIT - 1, 0);
    }

    private void PutBits(int n, uint x)
    {
        if (n < _bitcount)
        {
            _subbitbuf |= x << (_bitcount -= n);
        }
        else
        {
            EncPutc(_subbitbuf | (x >> (n -= _bitcount)));
            _compsize++;
            if (n < CHAR_BIT)
            {
                _subbitbuf = x << (_bitcount = CHAR_BIT - n);
            }
            else
            {
                EncPutc(x >> (n - CHAR_BIT));
                _compsize++;
                _subbitbuf = x << (_bitcount = 2 * CHAR_BIT - n);
            }
        }
    }

    private void InitPutBits()
    {
        _bitcount = CHAR_BIT;
        _subbitbuf = 0;
    }

    private void CountLen(int i)
    {
        if (i < _n) _lenCnt[(_depth < 16) ? _depth : 16]++;
        else
        {
            _depth++;
            CountLen(_left[i]);
            CountLen(_right[i]);
            _depth--;
        }
    }

    private void MakeLen(int root)
    {
        int i, k;
        uint cum;
        for (i = 0; i <= 16; i++) _lenCnt[i] = 0;
        CountLen(root);
        cum = 0;
        for (i = 16; i > 0; i--)
            cum += (uint)(_lenCnt[i] << (16 - i));
        while (cum != (1U << 16))
        {
            _lenCnt[16]--;
            for (i = 15; i > 0; i--)
            {
                if (_lenCnt[i] != 0)
                {
                    _lenCnt[i]--;
                    _lenCnt[i + 1] += 2;
                    break;
                }
            }
            cum--;
        }
        for (i = 16; i > 0; i--)
        {
            k = _lenCnt[i];
            while (--k >= 0) _len[_sortArr[_sortPtr++]] = (byte)i;
        }
    }

    private void DownHeap(int i)
    {
        int j, k;
        k = _heap[i];
        while ((j = 2 * i) <= _heapsize)
        {
            if (j < _heapsize && _freq[_heap[j]] > _freq[_heap[j + 1]])
                j++;
            if (_freq[k] <= _freq[_heap[j]]) break;
            _heap[i] = _heap[j];
            i = j;
        }
        _heap[i] = (short)k;
    }

    private void MakeCode(int n, byte[] len, ushort[] code)
    {
        var start = new ushort[18];
        start[1] = 0;
        for (int i = 1; i <= 16; i++)
            start[i + 1] = unchecked((ushort)((start[i] + _lenCnt[i]) << 1));
        for (int i = 0; i < n; i++) code[i] = start[len[i]]++;
    }

    private int MakeTree(int nparm, ushort[] freqparm, byte[] lenparm, ushort[] codeparm)
    {
        int i, j, k, avail;
        _n = nparm;
        _freq = freqparm;
        _len = lenparm;
        avail = _n;
        _heapsize = 0;
        _heap[1] = 0;
        for (i = 0; i < _n; i++)
        {
            _len[i] = 0;
            if (_freq[i] != 0) _heap[++_heapsize] = (short)i;
        }
        if (_heapsize < 2)
        {
            codeparm[_heap[1]] = 0;
            return _heap[1];
        }
        for (i = _heapsize / 2; i >= 1; i--)
            DownHeap(i);
        _sortArr = codeparm;
        _sortPtr = 0;
        do
        {
            i = _heap[1];
            if (i < _n) _sortArr[_sortPtr++] = (ushort)i;
            _heap[1] = _heap[_heapsize--];
            DownHeap(1);
            j = _heap[1];
            if (j < _n) _sortArr[_sortPtr++] = (ushort)j;
            k = avail++;
            _freq[k] = unchecked((ushort)(_freq[i] + _freq[j]));
            _heap[1] = (short)k;
            DownHeap(1);
            _left[k] = (ushort)i;
            _right[k] = (ushort)j;
        } while (_heapsize > 1);
        _sortArr = codeparm;
        _sortPtr = 0;
        MakeLen(k);
        MakeCode(nparm, lenparm, codeparm);
        return k;
    }
}
