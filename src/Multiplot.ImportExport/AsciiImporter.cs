using System.Text;
using Multiplot.Core;
using Multiplot.Plugins;
using Multiplot.Sun;

namespace Multiplot.ImportExport;

/// <summary>
/// Headless ASCII import: header detection, file evaluation, record parsing, file generation and the
/// time helpers (FR-IMP-001..008). The interactive dialog binds to the same properties.
/// </summary>
public sealed class AsciiImporter
{
    private const int LbLen = 2097152;

    /// <summary>Source file.</summary>
    public string SourceFile = "";
    /// <summary>Destination SUN file.</summary>
    public string DestFile = "";
    /// <summary>Index (time) column, 1-based.</summary>
    public int FileIndex = 1;
    /// <summary>Lines to skip.</summary>
    public int SkipLines;
    /// <summary>Title record (0 = file name).</summary>
    public int TitleRecord;
    /// <summary>Generate TimeTags.</summary>
    public bool GenTimeTag;
    /// <summary>Standard format (COMMONTIME instead of MULTITIME64).</summary>
    public bool StandardFormat;
    /// <summary>Index column is Unix epoch seconds.</summary>
    public bool UnixTime;
    /// <summary>Field delimiter (' ' = any whitespace).</summary>
    public char Delimiter = ' ';
    /// <summary>Reference time text "yyyy ddd hh:mm:ss.mmm.uuu".</summary>
    public string RefTime = "(current time)";
    /// <summary>Default reference computed by <see cref="Initialize"/>.</summary>
    public string DefRef = "";
    /// <summary>Status message.</summary>
    public string StatusMsg = "";
    /// <summary>Abort requested.</summary>
    public bool Abort;
    /// <summary>Output generated.</summary>
    public bool FileReady;
    /// <summary>Lines shown in the dialog preview.</summary>
    public List<string> Preview { get; } = new();
    /// <summary>Application version for the header comment.</summary>
    public string AppVersion = "Multiplot.NET v1.0.0";
    /// <summary>Message box callback (OK/Cancel questions return false to abort); null = auto OK.</summary>
    public Func<string, bool, bool>? Message;
    /// <summary>Progress 0..100.</summary>
    public Action<int>? Progress;
    /// <summary>Clock (tests).</summary>
    public DateTime? Now;

    private bool _gmtIsSet;
    private int _addIndex;
    private TimeTag _refGmt;
    private TimeTag _dataStart;
    private int _nParms;
    private int _nPoints;
    private int _numHl;
    private string[]? _names, _units, _descr, _alt;
    private int _numNames, _numUnits, _numDescr, _numAlt;
    private string _title = "";
    private long _lastDay;
    private double _addDays;

    private byte[] _data = Array.Empty<byte>();
    private int _pos;
    private bool _eof;

    // ------------------------------------------------------------------ file reading (fgets emulation)

    private void Rewind() { _pos = 0; _eof = false; }

    private string? Fgets(int max = LbLen - 1)
    {
        if (_pos >= _data.Length) { _eof = true; return null; }
        int start = _pos;
        while (_pos < _data.Length && _pos - start < max)
        {
            if (_data[_pos++] == (byte)'\n') break;
        }
        if (_pos >= _data.Length && _data[_pos - 1] != (byte)'\n') _eof = true;
        return AnsiText.DecodeAnsi(_data.AsSpan(start, _pos - start));
    }

    private bool Load()
    {
        try
        {
            using var fs = new FileStream(SourceFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            _data = new byte[fs.Length];
            fs.ReadExactly(_data);
            Rewind();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    // ------------------------------------------------------------------ dialog initialisation

    /// <summary>Preview, delimiter guess, reference detection. False when the file cannot be opened.</summary>
    public bool Initialize()
    {
        if (!Load()) return false;
        Preview.Clear();
        bool truncated = false;
        int nTestRows = 0;
        for (int i = 0; i < 512; i++)
        {
            string? buf = Fgets();
            if (buf != null)
            {
                if (buf.Length == 0 || buf[0] == '\r' || buf[0] == '\n') break;
                nTestRows++;
                if (buf.Length > 2048) buf = buf[..2048] + " . . .";
                else
                {
                    int c = buf.IndexOf('\r');
                    if (c >= 0) buf = buf[..c];
                }
                Preview.Add(buf.TrimEnd('\n'));
            }
            else { truncated = true; break; }
        }
        Preview.Add(truncated ? "- Remainder of file not shown -" : "- End of File -");

        Rewind();
        for (int i = 0; i < (nTestRows >= 5 ? nTestRows - 5 : 0); i++) Fgets();
        int comma = 0, tab = 0, space = 0;
        for (int i = 0; i < 5; i++)
        {
            string? buf = Fgets();
            if (buf == null) break;
            if (buf.Contains(',')) comma++;
            else if (buf.Contains('\t')) tab++;
            else space++;
        }
        if (comma != 0 && comma > tab && comma > space) Delimiter = ',';
        else if (tab != 0 && tab > comma && tab > space) Delimiter = '\t';

        Rewind();
        DateTime now = Now ?? DateTime.Now;
        _refGmt.JulianDay = (short)now.DayOfYear;
        CountHeaderLines();
        if (_gmtIsSet)
        {
            GenTimeTag = true;
            DefRef = Crt.Sprintf("%04u %03u %02u:%02u:%02u.%03u.%03u", now.Year, (int)_refGmt.JulianDay,
                (_refGmt.MilliSeconds / 3600000U) % 24, (_refGmt.MilliSeconds % 3600000U) / 60000U, (_refGmt.MilliSeconds % 60000U) / 1000U,
                _refGmt.MilliSeconds % 1000U, (uint)_refGmt.MicroSeconds);
        }
        else
        {
            DefRef = Crt.Sprintf("%04u %03u %02u:%02u:%02u.%03u.000", now.Year, now.DayOfYear, now.Hour, now.Minute, now.Second, now.Millisecond);
            _refGmt.MilliSeconds = (uint)(now.Hour * 3600000 + now.Minute * 60000 + now.Second * 1000 + now.Millisecond);
            _refGmt.MicroSeconds = 0;
        }
        RefTime = DefRef;
        return true;
    }

    // ------------------------------------------------------------------ record parsing

    /// <summary>Returns the fields and the number of empty fields.</summary>
    public List<string> ParseRecord(string buf, out int nullFields)
    {
        var items = new List<string>();
        nullFields = 0;
        char d = Delimiter;
        int end = buf.Length;
        while (end > 0 && (buf[end - 1] == '\r' || buf[end - 1] == '\n' || buf[end - 1] == '\t')) end--;
        // a NUL inside the record terminates it (C string)
        int nul = buf.IndexOf('\0');
        if (nul >= 0 && nul < end) end = nul;
        int p = 0;
        while (p < end && (buf[p] == '"' || buf[p] == ' ')) p++;
        while (end > p && buf[end - 1] == ' ') end--;
        bool addField = end > p && buf[end - 1] == d && d != ' ';
        while (p < end)
        {
            int start = p;
            int fieldEnd = -1;
            while (p < end && buf[p] != d)
            {
                if (buf[p] == '"' && fieldEnd < 0) fieldEnd = p;
                ++p;
            }
            string item = buf[start..(fieldEnd >= 0 ? fieldEnd : p)];
            if (p < end) p++;
            if (item.Length == 0) nullFields++;
            items.Add(item);
            while (p < end && (buf[p] == '"' || buf[p] == ' ')) p++;
        }
        if (addField)
        {
            items.Add("");
            nullFields++;
        }
        return items;
    }

    private static bool LooksNumeric(string f) =>
        f.Length > 0 && (Crt.Atof(f) != 0.0 || "0.+-#".IndexOf(f[0]) >= 0 || f == "NaN");

    /// <summary>Returns true on error.</summary>
    private bool CountHeaderLines()
    {
        _numHl = 0;
        bool err = false;
        Rewind();
        for (int i = 0; i < SkipLines; i++) Fgets(LbLen);
        int count = 0;
        bool dataRec = false;
        do
        {
            string? line = Fgets(LbLen);
            if (_eof || Abort) { err = true; break; }
            var f = ParseRecord(line ?? "", out _);
            count = f.Count;
            if (count != 0)
            {
                int k = f.Count(LooksNumeric);
                if (k != count) _numHl++;
                else
                {
                    dataRec = true;
                    if (!_gmtIsSet && FileIndex < count)
                        DetectReference(f[FileIndex - 1]);
                }
            }
            else _numHl++;
        } while (!dataRec);
        if (!err) _nParms = count;
        else { _nParms = 0; _numHl = 0; }
        Rewind();
        return err;
    }

    private void DetectReference(string field)
    {
        int colon = field.IndexOf(':');
        if (colon >= 0 || field.Contains('/'))
        {
            if (colon >= 0 && field.Length - colon >= 3)
            {
                // backs up two characters to the start of the hour; a single digit hour reads the
                // preceding (zeroed) byte, which yields hour 0 and a zero length string
                int ptr = colon - 2;
                string s = ptr >= 0 ? field[ptr..] : "";
                int sLen = s.Length;
                uint hr = (uint)Crt.Atol(s);
                uint mn = (uint)Crt.Atol(ptr + 3 >= 0 && ptr + 3 <= field.Length ? field[(ptr + 3)..] : "");
                uint sc = sLen > 6 && s[5] == ':' ? (uint)Crt.Atol(s[6..]) : 0;
                uint mil, mic;
                if (sLen > 9 && s[8] == '.')
                {
                    if (sLen > 13)
                    {
                        if (s[12] == '.')
                        {
                            mil = (uint)Crt.Atol(s[9..]);
                            mic = (uint)(Crt.Atol(s[13..]) % 1000);
                        }
                        else
                        {
                            double frac = Crt.Atof(s[8..]);
                            mil = (uint)(frac * 1000.0);
                            mic = ((uint)(frac * 1000000.0)) % 1000;
                        }
                    }
                    else
                    {
                        mil = sLen == 12 ? (uint)Crt.Atol(s[9..]) % 1000 : (uint)(Crt.Atof(s[8..]) * 1000.0);
                        mic = 0;
                    }
                }
                else mil = mic = 0;
                if (ptr >= 2) _refGmt.JulianDay = (short)((ushort)Crt.Atol(field) % 367);
                _refGmt.MilliSeconds = unchecked(hr * 3600000 + mn * 60000 + sc * 1000 + mil);
                _refGmt.MicroSeconds = (ushort)mic;
            }
            else
                TimeTagFromText(field, ref _refGmt);
            _gmtIsSet = true;
        }
        else
        {
            double v = Crt.Atof(field);
            if (v > 485438400.0)
            {
                _refGmt = Epoch2TimeTag(v);
                UnixTime = true;
                _gmtIsSet = true;
            }
        }
    }

    /// <summary>Returns 0 on success.</summary>
    private int EvaluateFile()
    {
        int err = 0;
        StatusMsg = "Scanning...";
        if (CountHeaderLines())
        {
            StatusMsg = "Unable to determine parameter values starting record (invalid content).";
            return 2;
        }
        string line = "";
        for (int j = 0; j <= _numHl + SkipLines; j++) line = Fgets(LbLen) ?? "";
        _nParms = ParseRecord(line, out _).Count;
        if (_nParms == 0)
        {
            StatusMsg = "Failed to determine parameter count.";
            return 3;
        }
        if (FileIndex > _nParms || FileIndex < 1)
        {
            FileIndex = 1;
            Message?.Invoke("File index column is invalid\nusing default", false);
        }
        if (_nParms == 1) _addIndex = 1;

        if (TitleRecord > 0)
        {
            Rewind();
            string t = "";
            for (int j = 0; j < TitleRecord; j++) t = Fgets() ?? "";
            int k = 0;
            while (k < t.Length && (t[k] == ' ' || t[k] == '"')) k++;
            t = t[k..];
            if (t.Length > 79) t = t[..79];
            int nulPos = t.IndexOf('\0');
            if (nulPos >= 0) t = t[..nulPos];
            while (t.Length > 0 && (t[^1] <= ' ' || t[^1] == '"')) t = t[..^1];
            _title = t;
        }
        else
        {
            string n = SourceFile[(SourceFile.LastIndexOf('\\') + 1)..];
            _title = n.Length > 79 ? n[..79] : n;
        }

        Rewind();
        int skip = 0, nullFields;
        _names = null;
        if (_numHl > 0)
        {
            skip++;
            if (SkipLines != 0)
                for (int sk = 0; sk < SkipLines; sk++, skip++) Fgets(LbLen);
            var f = ParseRecord(Fgets(LbLen) ?? "", out nullFields);
            _names = f.ToArray();
            _numNames = f.Count;
            if (nullFields != 0 || _numNames != _nParms)
            {
                if (_numHl > 1)
                {
                    for (int j = 1; j < _numHl; j++)
                    {
                        skip++;
                        f = ParseRecord(Fgets(LbLen) ?? "", out nullFields);
                        _names = f.ToArray();
                        _numNames = f.Count;
                        if (nullFields == 0 && _numNames == _nParms) break;
                    }
                }
                if (_numNames != _nParms) _numNames = 0;
            }
        }
        else _numNames = 0;

        string[] ReadNomen(ref int num)
        {
            string[]? arr = null;
            if (_numHl + SkipLines - skip > 0)
            {
                skip++;
                var f = ParseRecord(Fgets(LbLen) ?? "", out _);
                arr = f.ToArray();
                num = f.Count;
            }
            else num = 0;
            if (num != _nParms)
            {
                num = 0;
                arr = Enumerable.Repeat(" ", _nParms).ToArray();
            }
            return arr!;
        }
        _units = ReadNomen(ref _numUnits);
        _descr = ReadNomen(ref _numDescr);
        string[]? alt = null;
        if (_numHl + SkipLines - skip > 0)
        {
            skip++;
            var f = ParseRecord(Fgets(LbLen) ?? "", out _);
            alt = f.ToArray();
            _numAlt = f.Count;
        }
        else _numAlt = 0;
        if (_numAlt != _nParms)
        {
            _numAlt = 0;
            alt = new string[_nParms];
            for (int j = 0; j < _nParms; j++) alt[j] = _names != null && j < _names.Length ? _names[j] : "";
        }
        _alt = alt;
        if (_names == null || _names.Length < _nParms)
        {
            var n = new string[_nParms];
            for (int j = 0; j < _nParms; j++) n[j] = _names != null && j < _names.Length ? _names[j] : "";
            _names = n;
        }

        // count points
        Rewind();
        for (int j = 0; j < _numHl + SkipLines; j++) Fgets(LbLen);
        _nPoints = 0;
        while (!_eof)
        {
            if (Abort) { err = 10; break; }
            if (_nPoints % 1000 == 0 && _data.Length > 0) Progress?.Invoke((int)(_pos / (float)_data.Length * 100f));
            string? l = Fgets(LbLen);
            if (l == null) break;
            if (CLen(l) <= 2) break;
            if (_nPoints == 0)
            {
                var tok = new Strtok(l);
                string? timeStr = tok.Next(" ,;\t");
                if (timeStr != null && (timeStr.Contains('/') || timeStr.Contains(':'))) GenTimeTag = true;
            }
            _nPoints++;
        }
        if (_nPoints < 1) err = 1;

        if (err == 0 && FileIndex > 1)
        {
            void Swap(string[] a) { if (FileIndex - 1 < a.Length) (a[0], a[FileIndex - 1]) = (a[FileIndex - 1], a[0]); }
            Swap(_names); Swap(_alt); Swap(_units); Swap(_descr);
        }
        if (Abort) StatusMsg = "Import aborted.";
        else if (err != 0) StatusMsg = "Scan failed";
        return err;
    }

    private static int CLen(string s) { int n = s.IndexOf('\0'); return n >= 0 ? n : s.Length; }

    // ------------------------------------------------------------------ generation

    /// <summary>
    /// Generates the SUN file (also run by scripted imports). <paramref name="overrideRef"/> is the scripted /R
    /// reference which is honoured (DEV-IMP-001 fix). Returns 0 on success.
    /// </summary>
    public int GenerateFile(string? overrideRef = null)
    {
        if (_data.Length == 0 && !Load()) return 1;
        if (overrideRef != null) RefTime = overrideRef;
        if (EvaluateFile() != 0) return 2;

        int nItems = StandardFormat ? _nParms + (GenTimeTag ? 1 : 0) + _addIndex : _nParms - (_addIndex != 0 ? 0 : 1);
        SunWriter w;
        try
        {
            w = SunWriter.Create(DestFile, SunCreateMode.Overwrite, 256, (uint)nItems, (uint)_nPoints, StandardFormat ? SunSignatures.CommonTime : SunSignatures.MultiTime64);
        }
        catch (DataAccessException)
        {
            return 3;
        }
        using (w)
        {
            if (!ClockToTimeTag(RefTime, out _refGmt, true))
            {
                RefTime = DefRef;
                ClockToTimeTag(RefTime, out _refGmt, true);
            }
            uint[] reference =
            {
                (uint)Crt.Atoi(RefTime), (uint)(ushort)_refGmt.JulianDay, (_refGmt.MilliSeconds / 3600000U) % 24,
                (_refGmt.MilliSeconds % 3600000U) / 60000U, (_refGmt.MilliSeconds % 60000U) / 1000U, _refGmt.MilliSeconds % 1000U,
            };
            w.SetRefTime(reference[0], reference[1], reference[2], reference[3], reference[4], reference[5]);
            _dataStart = _refGmt;
            w.SetCutoff(0f);
            w.SetTitle(_title);
            w.SetComment("Imported by " + AppVersion);
            ParseFileData(w);
            if (GenTimeTag)
                w.SetCommentText(Crt.Sprintf("[events]\nFILE_REF=%03u/%u.%03u", (uint)(ushort)_refGmt.JulianDay, _refGmt.MilliSeconds, (uint)_refGmt.MicroSeconds));
            w.Close();
        }
        if (!Abort) FileReady = true;
        return 0;
    }

    private void ParseFileData(SunWriter w)
    {
        int np = _nPoints, nCols = _nParms + _addIndex;
        var dbuf = new float[(long)nCols * np];
        TimeTag[]? tags = _addIndex == 0 && GenTimeTag ? new TimeTag[np] : null;

        Rewind();
        for (int j = 0; j < _numHl + SkipLines; j++) Fgets(LbLen);
        StatusMsg = "Reading source data.";
        double baseTime = 0, relTime = 0;
        ResetClockToSeconds();
        char d = Delimiter;
        for (int point = 0; point < np; point++)
        {
            if (Abort) break;
            string line = Fgets(LbLen - 1) ?? "";
            if (point % (np / 100 + 1) == 0) Progress?.Invoke((int)((point + 1.0) / np * 50.0));
            int parmNo = 0;
            if (_addIndex != 0) { dbuf[point] = point; parmNo++; }

            // strtok_s emulation with null-field look-ahead
            int len = CLen(line);
            int next = 0;
            int tokStart = 0;
            string? NextTok()
            {
                int i = next;
                while (i < len && line[i] == d) i++;
                if (i >= len) { next = len; return null; }
                int s = i;
                while (i < len && line[i] != d) i++;
                next = i < len ? i + 1 : i;
                tokStart = s;
                return line[s..i];
            }
            string? tok = NextTok();
            int ptrPos = tokStart;
            bool atDelim = false;
            bool timetagIsSet = false;
            while (parmNo < nCols && (tok != null || atDelim))
            {
                string field = atDelim ? "" : tok!;
                char first = atDelim ? d : (field.Length > 0 ? field[0] : '\0');
                if (parmNo == FileIndex - 1)
                {
                    if (UnixTime)
                    {
                        relTime = Crt.Atof(field);
                        if (point == 0) baseTime = relTime;
                        var tt = Epoch2TimeTag(relTime);
                        if (tags != null) { tags[point] = tt; timetagIsSet = true; }
                        else timetagIsSet = false;
                        relTime = TimeMath.Time(tt, _refGmt);
                        dbuf[point + (long)parmNo * np] = (float)relTime;
                    }
                    else if (field.Contains('/'))
                    {
                        if (field.Contains(':'))
                        {
                            relTime = ClockToSeconds(field);
                            if (point == 0) baseTime = relTime;
                            ClockToTimeTag(field, out var tt, false);
                            if (tags != null) { tags[point] = tt; timetagIsSet = true; }
                            else timetagIsSet = false;
                            relTime = TimeMath.Time(tt, _refGmt);
                        }
                        else
                        {
                            var tt = new TimeTag();
                            bool valid = TimeTagFromText(field, ref tt);
                            if (tags != null) { tags[point] = tt; timetagIsSet = valid; }
                            relTime = TimeMath.Time(tt, _refGmt);
                        }
                        dbuf[point + (long)parmNo * np] = (float)relTime;
                    }
                    else if (field.Contains(':'))
                    {
                        relTime = ClockToSeconds(field);
                        if (point == 0) baseTime = relTime;
                        dbuf[point + (long)parmNo * np] = (float)(relTime - baseTime);
                        timetagIsSet = false;
                    }
                    else
                    {
                        relTime = Crt.Atof(field);
                        dbuf[point + (long)parmNo * np] = (float)relTime;
                        timetagIsSet = false;
                    }
                }
                else
                {
                    float v;
                    if (first == '\0' || first == d || first == '\r' || first == '\n') v = DataTypes.FpDropOut;
                    else if (first == 'N' || field.StartsWith("-N", StringComparison.Ordinal) || field.StartsWith("1.#Q", StringComparison.Ordinal)) v = DataTypes.FpDropOut;
                    else v = (float)Crt.Atof(field);
                    dbuf[point + (long)parmNo * np] = v;
                }

                if (parmNo == FileIndex - 1 && tags != null)
                {
                    if (!timetagIsSet)
                    {
                        relTime -= baseTime;
                        tags[point] = TimeMath.Gmt(relTime, _refGmt, true);
                    }
                    if (point == 0) _dataStart = tags[0];
                }

                // advance
                if (d != ' ')
                {
                    int p2 = atDelim ? ptrPos + 1 : ptrPos + field.Length + 1;
                    if (p2 < len && line[p2] == d)
                    {
                        atDelim = true;
                        ptrPos = p2;
                    }
                    else
                    {
                        atDelim = false;
                        tok = NextTok();
                        ptrPos = tokStart;
                    }
                }
                else
                {
                    tok = NextTok();
                    ptrPos = tokStart;
                }
                parmNo++;
            }
        }

        if (!Abort && FileIndex > 1)
        {
            for (int point = 0; point < np; point++)
                (dbuf[point], dbuf[point + (long)(FileIndex - 1) * np]) = (dbuf[point + (long)(FileIndex - 1) * np], dbuf[point]);
        }

        StatusMsg = "Generating file.";
        float[] Column(int c) { var a = new float[np]; Array.Copy(dbuf, (long)c * np, a, 0, np); return a; }
        if (StandardFormat && _addIndex != 0)
            w.AddParm("Index", "Index", "Count", "Data Point Index", 0, "f4", Column(0), null, null, 0, 0, null);
        for (int parmNo = (_addIndex != 0 || !StandardFormat) ? 1 : 0; parmNo < _nParms + _addIndex && !Abort; parmNo++)
        {
            Progress?.Invoke(50 + (int)((float)parmNo / _nParms * 50f));
            int k = parmNo - _addIndex;
            string name = _numNames != 0 && k < _names!.Length ? (_names[k].Length > 0 ? _names[k] : "<NULL>") : Crt.Sprintf("Column%u", parmNo + 1);
            string aname = _numAlt != 0 && k < _alt!.Length && _alt[k].Length > 0 ? _alt[k] : name;
            string units = _numUnits != 0 && k < _units!.Length ? _units[k] : " ";
            string descr = _numDescr != 0 && k < _descr!.Length ? _descr[k] : " ";
            name = Clip(name); aname = Clip(aname); units = Clip(units); descr = Clip(descr);
            if (StandardFormat)
            {
                w.AddParm(name, aname, units, descr, parmNo, "f4", Column(parmNo), null, null, 0, 0, null);
                if (parmNo == 0 && tags != null)
                    w.AddParm("GMT", "TimeTag", "Seconds", "Time in Seconds", 0, "t8", tags, null, null, 0, 0, null);
            }
            else if (tags != null)
                w.AddParm(name, aname, units, descr, parmNo, "f4", Column(parmNo), "t8", tags, (uint)np, SunWriteOptions.LzhCompress, null);
            else
                w.AddParm(name, aname, units, descr, parmNo, "f4", Column(parmNo), "f4", Column(0), (uint)np, SunWriteOptions.LzhCompress, null);
        }
        StatusMsg = Abort ? "Aborted." : "File complete.";
    }

    private static string Clip(string s) => s.Length > 255 ? s[..255] : s;

    // ------------------------------------------------------------------ time helpers

    /// <summary>Epoch seconds to TimeTag (UTC).</summary>
    public static TimeTag Epoch2TimeTag(double seconds)
    {
        long etime = (long)seconds;
        uint usec = (uint)(1000000.0 * ((seconds % 1.0) + .0000005));
        var ts = DateTimeOffset.FromUnixTimeSeconds(etime).UtcDateTime;
        return new TimeTag((short)ts.DayOfYear, (uint)((ts.Hour * 3600 + ts.Minute * 60 + ts.Second) * 1000 + usec / 1000), (ushort)(usec % 1000));
    }

    /// <summary>Clock text to TimeTag: "yyyy ddd hr:mn:sc.mil.mic" (yearFmt) or "ddd/hr:mn:sc.mil".</summary>
    public static bool ClockToTimeTag(string clockTime, out TimeTag tt, bool yearFmt)
    {
        tt = new TimeTag(1, 0, 0);
        if (yearFmt && clockTime.Length < 21) return false;
        string buf = clockTime.Length > 31 ? clockTime[..31] : clockTime;
        var t = new Strtok(buf);
        string? p;
        if (yearFmt)
        {
            t.Next(" :.");
            p = t.Next(" :./");
        }
        else p = t.Next(" :./");
        if (p == null) return false;
        ushort jd = (ushort)Crt.Atoi(p);
        uint hr = 0, mn = 0, sc, mil;
        ushort mic;
        if ((p = t.Next(" :.")) != null) hr = (uint)Crt.Atoi(p);
        if ((p = t.Next(" :.")) != null) mn = (uint)Crt.Atoi(p);
        if ((p = t.Next(" :.")) == null) return false;
        sc = (uint)Crt.Atoi(p);
        if ((p = t.Next(" :.")) != null)
        {
            if (p.Length > 6) p = p[..6];
            if (p.Length > 3)
            {
                mic = (ushort)Crt.Atoi(p[3..]);
                mil = (uint)Crt.Atoi(p[..3]);
            }
            else
            {
                mil = (uint)Crt.Atoi(p);
                mic = (p = t.Next(".")) != null ? (ushort)Crt.Atoi(p) : (ushort)0;
            }
        }
        else { mil = 0; mic = 0; }
        tt = new TimeTag((short)jd, unchecked(hr * 3600000 + mn * 60000 + sc * 1000 + mil), mic);
        return true;
    }

    /// <summary>TimeTag text to TimeTag ("ddd/msec[.usec]").</summary>
    public static bool TimeTagFromText(string s, ref TimeTag tt)
    {
        int slash = s.IndexOf('/');
        if (s.Length == 0 || slash < 0) return false;
        ushort jd = (ushort)Crt.Atoi(s);
        uint mtod = (uint)Crt.Atoi(s[(slash + 1)..]);
        int dot = s.IndexOf('.', slash);
        ushort mic = dot >= 0 ? (ushort)Crt.Atoi(s[(dot + 1)..]) : (ushort)0;
        tt = new TimeTag((short)jd, mtod, mic);
        return true;
    }

    private void ResetClockToSeconds() { _lastDay = 0; _addDays = 0; }

    /// <summary>Clock text to seconds with day roll-over state.</summary>
    public double ClockToSeconds(string gmtStr)
    {
        int g = 0;
        while (g < gmtStr.Length && gmtStr[g] == ' ') g++;
        string s = gmtStr[g..];
        int pf = s.IndexOf(':');
        if (pf < 0) return 0.0;
        int iOff = pf >= 2 ? 2 : 1;
        double seconds = Crt.Atof(s[(pf - iOff)..]) * 3600.0;
        seconds += Crt.Atof(s[(pf + 1)..]) * 60.0;
        int p = pf + 1;
        int c2 = s.IndexOf(':', p);
        if (c2 >= 0)
        {
            seconds += Crt.Atof(s[(c2 + 1)..]);
            int d1 = s.IndexOf('.', c2 + 1);
            if (d1 >= 0)
            {
                int d2 = s.IndexOf('.', d1 + 1);
                if (d2 >= 0) seconds += Crt.Atof(s[d2..]) / 1000.0;
            }
        }
        int sp = s.IndexOf(' ');
        int sl = sp >= 0 ? sp : s.IndexOf('/');
        if (sl >= 0 && sl < 4)
        {
            long jday = Crt.Atol(s);
            if (_lastDay != 0 && jday != _lastDay)
            {
                if (jday > _lastDay) _addDays += 86400.0 * (jday - _lastDay);
                else _addDays += 86400.0 * (jday + (_lastDay < 365 ? 365 : _lastDay) - _lastDay);
            }
            seconds += _addDays;
            _lastDay = jday;
        }
        return seconds;
    }

    /// <summary>Script recording text (FR-IMP-008).</summary>
    public string RecordText()
    {
        var sb = new StringBuilder();
        sb.Append(DestFile.Contains(".~") ? $"import \"{SourceFile}\"" : $"import \"{SourceFile}\" \"{DestFile}\"");
        if (FileIndex != 0) sb.Append(Crt.Sprintf(" /I%d", FileIndex));
        if (SkipLines != 0) sb.Append(Crt.Sprintf(" /S%d", SkipLines));
        if (TitleRecord != 0) sb.Append(Crt.Sprintf(" /T%d", TitleRecord));
        if (GenTimeTag) sb.Append(" /G");
        if (StandardFormat) sb.Append(" /F");
        if (RefTime.Length > 0) sb.Append(" /R").Append(RefTime);
        return sb.ToString();
    }
}
