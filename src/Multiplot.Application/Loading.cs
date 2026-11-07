// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

using Multiplot.Core;
using static Multiplot.Core.ExprSyntax;
using Multiplot.Core.Model;
using Multiplot.Expressions;
using Multiplot.Plugins;
using Multiplot.Scripting;

namespace Multiplot.Application;

/// <summary>File/group opening and parameter loading (chapter 07).</summary>
public sealed partial class Session
{
    // ------------------------------------------------------------------ files and groups

    /// <summary>0 ok, −1 max groups (aborts scripts), 1 re-entry.</summary>
    public int LoadFileGroup(string name)
    {
        if (DataLoading || FileLoading)
        {
            Message("Can't execute, data/file load in progress");
            return 1;
        }
        string groupName = name.Replace("\"", "");
        if (groupName.Contains(','))
        {
            ParseFileList(groupName);
            return 0;
        }
        if (_groups.Count >= MaxGroups)
        {
            Message("Max number of file groups reached!");
            return -1;
        }
        FileLoading = true;
        SetStatus("Initializing...");
        int currentGrp = _groups.Count;
        var grp = new FileGroup
        {
            NFiles = 0,
            StartPos = _files.Count,
            TRef = Settings.TRef,
            TStart = Settings.TStart,
            TStop = Settings.TStop,
            EngineNo = "unknown",
            EngineCO = "unknown",
        };
        bool dbDir = false;
        try
        {
            string ext = groupName[(groupName.LastIndexOf('.') + 1)..];
            if (!ext.Equals("flt", StringComparison.OrdinalIgnoreCase))
            {
                string? fspec = File.Exists(groupName) ? groupName : null;
                string shortName = groupName;
                if (fspec == null)
                {
                    int bs = groupName.LastIndexOf('\\');
                    if (bs >= 0) shortName = groupName[(bs + 1)..];
                    fspec = WhereIs(shortName, Settings.DbFolder);
                }
                if (fspec != null)
                {
                    byte[] head = ReadHead(fspec, 4);
                    if (head.Length == 4)
                    {
                        uint sig = BitConverter.ToUInt32(head, 0);
                        var t = (byte[])head.Clone();
                        for (int i = 0; i < 4; i++)
                            if (t[i] == 10 || t[i] == 13) { for (int j = i; j < 4; j++) t[j] = (byte)' '; break; }
                        bool ascii = t.All(b => b > 31 && b < 127);
                        if (!SunSignatures.IsSun(sig) && ascii) dbDir = true;
                    }
                    if (dbDir) dbDir = LoadGroupFile(fspec, shortName, grp);
                }
            }
            else
                dbDir = LoadFilterFile(groupName, grp);

            if (!dbDir)
            {
                if (LoadDB(groupName))
                {
                    var f = _files[^1];
                    grp.FileName = f.Info.Path;
                    grp.GroupName = f.Info.Path.Contains('\\') ? f.Info.Path[(f.Info.Path.LastIndexOf('\\') + 1)..] : f.Info.Path;
                    grp.NFiles++;
                    _groups.Add(grp);
                }
            }

            if (currentGrp != _groups.Count)
            {
                for (int i = grp.StartPos; i < grp.StartPos + grp.NFiles; i++)
                {
                    if (_files[i].Info.Cutoff != 0f) { grp.EngineCO = Crt.Sprintf("%.3f", (double)_files[i].Info.Cutoff); break; }
                }
                for (int i = grp.StartPos; i < grp.StartPos + grp.NFiles; i++)
                {
                    string sn = _files[i].Info.SerialNum;
                    if (sn.Length > 0 && sn[0] != ' ') { grp.EngineNo = sn.Length > 12 ? sn[..12] : sn; break; }
                }
                int tmp = grp.GroupName.IndexOf(".~", StringComparison.Ordinal);
                if (tmp != -1) grp.GroupName = grp.GroupName[..tmp];
            }
        }
        finally
        {
            SetStatus("");
            FileLoading = false;
        }

        if (Settings.EmbeddedScript && currentGrp != _groups.Count)
        {
            for (int i = grp.StartPos; i < grp.StartPos + grp.NFiles; i++)
            {
                string? cb = _files[i].CommentBlock;
                if (cb != null) ParseFileScript(cb);
            }
        }
        Shell.RefreshCommands();
        return 0;
    }

    private static byte[] ReadHead(string path, int n)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var b = new byte[n];
            int r = fs.Read(b, 0, n);
            return r == n ? b : Array.Empty<byte>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Array.Empty<byte>();
        }
    }

    private bool LoadGroupFile(string fspec, string shortName, FileGroup grp)
    {
        List<string> lines;
        try { lines = AnsiText.ReadAllLines(fspec); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
        if (lines.Count == 0) return false;
        string Clip(string s) => s.Length > 254 ? s[..254] : s;
        grp.GroupName = Clip(lines[0]).TrimEnd('\r');
        grp.FileName = fspec;
        if (lines.Count < 2) return false;
        string count = Clip(lines[1]).TrimEnd('\r');
        int files = ExprSyntax.ValidateConst(count) != 0 ? 0 : Crt.Atoi(count);
        if (files <= 0 || files > MaxFiles) return false;
        bool tempGroup = shortName.Contains("~Multiplot_");
        for (int i = 0; i < files; i++)
        {
            string fn = 2 + i < lines.Count ? Clip(lines[2 + i]).TrimEnd('\r') : "";
            if (LoadDB(fn, tempGroup)) grp.NFiles++;
        }
        if (grp.NFiles > 0)
        {
            _groups.Add(grp);
            AddRecentFile(shortName);
        }
        return true;
    }

    private bool LoadFilterFile(string groupName, FileGroup grp)
    {
        string filtSpec = groupName, strChan = "";
        int i1 = filtSpec.IndexOf('[');
        if (i1 >= 0)
        {
            int i2 = filtSpec.IndexOf(']');
            if (i2 > i1)
            {
                strChan = filtSpec.Substring(i1, i2 - i1 + 1);
                filtSpec = filtSpec.Replace(strChan, "");
            }
        }
        string? fspec = File.Exists(filtSpec) ? filtSpec : WhereIs(filtSpec[(filtSpec.LastIndexOf('\\') + 1)..], Settings.FileSpec);
        if (fspec == null) return false;
        List<string> lines;
        try { lines = AnsiText.ReadAllLines(fspec); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
        grp.FileName = fspec[(fspec.LastIndexOf('\\') + 1)..];
        grp.GroupName = grp.FileName.Length >= 4 ? grp.FileName[..^4] : grp.FileName;
        StreamWriter? tmp = null;
        int parmCnt = 0;
        string sub = filtSpec;
        void Flush()
        {
            if (tmp == null) return;
            tmp.Dispose();
            tmp = null;
            if (parmCnt == 0) return;
            string subFilt = sub.Insert(sub.LastIndexOf('\\') + 1, strChan);
            if (LoadDB(subFilt, false))
            {
                grp.NFiles++;
                if (strChan.Length == 0)
                {
                    string n = _files[^1].Info.Path;
                    int a = n.IndexOf('['), b = n.IndexOf(']');
                    if (a >= 0 && b > a) strChan = n.Substring(a, b - a + 1);
                }
            }
        }
        foreach (string raw in lines.Append(null!))
        {
            bool eof = raw == null;
            string rec = (raw ?? "").Trim(' ', '\n', '\t', '\r');
            int c1 = rec.IndexOf("[FILTER]=", StringComparison.Ordinal);
            if (c1 != -1 || eof)
            {
                Flush();
                if (!eof)
                {
                    string name = rec[(c1 + 9)..].Trim();
                    foreach (char ch in "\\/:*?\"<>|") name = name.Replace(ch, '_');
                    sub = Settings.TempDir + "\\" + name + ".~.flt";
                    parmCnt = 0;
                    try { tmp = AnsiText.CreateWriter(sub); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { tmp = null; }
                }
            }
            else if (tmp != null && rec.Length > 0 && rec[0] != ';')
            {
                tmp.Write(rec + "\r\n");
                parmCnt++;
            }
        }
        if (grp.NFiles > 0)
        {
            _groups.Add(grp);
            AddRecentFile(groupName);
            return true;
        }
        return false;
    }

    /// <summary>Each comma-separated item becomes its own group (DEV-LOAD-001).</summary>
    public int ParseFileList(string list)
    {
        int count = 0;
        foreach (string item in list.Split(','))
        {
            string fn = item.Trim();
            if (fn.Length == 0) continue;
            count++;
            LoadFileGroup(fn);
        }
        return count;
    }

    /// <summary>Opens one data file; returns true on success.</summary>
    public bool LoadDB(string dbName, bool saveRecent = true)
    {
        string name = dbName.Trim();
        SetStatus("Initializing " + name);
        if (_files.Count >= MaxFiles)
        {
            Message("File Capacity Reached");
            return false;
        }
        var f = Data.Open(name);
        if (f == null)
        {
            int last = name.LastIndexOf(']');
            if (last < 0) last = name.LastIndexOf('\\');
            string shortName = last >= 0 ? name[(last + 1)..] : name;
            string? fspec = WhereIs(shortName, Settings.FileSpec);
            if (fspec != null)
            {
                string path = fspec;
                int br = name.IndexOf('[');
                if (br >= 0) path = path[..(path.LastIndexOf('\\') + 1)] + name[br..];
                f = Data.Open(path);
            }
        }
        if (f == null)
        {
            int status = Data.LastStatus;
            string msg = status == 0 ? Crt.Sprintf("Open failed for %s\n\nUnsupported format.", name) : Crt.Sprintf("Open failed for %s\n\nStatus: %s", name, SunErrorText.Get(status));
            ScriptLog(msg.Replace("\n\n", " "));
            if (Message(msg, ScriptRun != 0 ? PromptButtons.OkCancel : PromptButtons.Ok, PromptIcon.Warning) == PromptResult.Cancel)
                AbortScript(true);
            return false;
        }
        if (saveRecent) AddRecentFile(f.Info.Path);
        if (f.Info.FileSig == SunSignatures.PcGoalRealtime || f.Info.IsDynamic)
        {
            if (AutoRefresh == 0)
            {
                bool save = RtLoaded;
                RtLoaded = true;
                ToggleRefresh();
                RtLoaded = save;
            }
        }
        _files.Add(f);
        if (f.Info.IsDynamic) RtLoaded = true;
        return true;
    }

    /// <summary>Recent file list (registry).</summary>
    public List<string> RecentFiles { get; } = new();
    private int _menuPos = -1;

    /// <summary>Adds a file to the recent files menu.</summary>
    public void AddRecentFile(string name, bool saveKey = true)
    {
        if (name.Contains("~Multiplot_")) return;
        if (RecentFiles.Any(r => string.Equals(r, name, StringComparison.OrdinalIgnoreCase))) return;
        if (RecentFiles.Count < 10) RecentFiles.Add(name);
        else RecentFiles[(_menuPos + 10) % 10] = name;
        _menuPos++;
        if (saveKey) Registry.AddRecentFile(name, _menuPos);
        Shell.RefreshCommands();
    }

    /// <summary>Closes all files (CLEAR FILES).</summary>
    public void CloseAllFiles()
    {
        if (DataLoading)
        {
            Message("Can't close files while data is loading");
            return;
        }
        SetRefreshPause(true);
        CloseAllPlots();
        foreach (var f in _files)
        {
            string n = f.Info.Path;
            try { f.Dispose(); } catch (Exception) { }
            if (n.Contains(".~"))
            {
                try { File.Delete(n); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        _files.Clear();
        _groups.Clear();
        CurrentFile = -1;
        RtLoaded = false;
        ImportCnt = 0;
        Shell.SetClock("");
        Record("CLEAR FILES");
        SetRefreshPause(false);
        Shell.RefreshCommands();
    }

    /// <summary>Closes the last file (CLEAR LAST).</summary>
    public void CloseLast()
    {
        if (DataLoading)
        {
            Message("Can't close files while data is loading");
            return;
        }
        if (_groups.Count > 0)
        {
            var g = _groups[^1];
            if (Message(Crt.Sprintf("(%s) has %d files; continue?", g.GroupName, g.NFiles), PromptButtons.YesNo, PromptIcon.Question, PromptResult.Yes) == PromptResult.Yes)
            {
                SetRefreshPause(true);
                if (_plots.Count > 0)
                {
                    if (Message("Plot Windows will be closed", PromptButtons.OkCancel, PromptIcon.Warning) == PromptResult.Cancel) return;
                    CloseAllPlots();
                }
                _groups.RemoveAt(_groups.Count - 1);
                for (int i = g.StartPos + g.NFiles - 1; i >= g.StartPos; i--)
                {
                    try { _files[i].Dispose(); } catch (Exception) { }
                    _files.RemoveAt(i);
                }
                if (RtLoaded && !_files.Any(f => f.Info.IsDynamic)) RtLoaded = false;
                SetRefreshPause(false);
            }
            Shell.SetClock("");
            Record("clear last");
        }
        if (_files.Count == 0) CurrentFile = -1;
        Shell.RefreshCommands();
    }

    /// <summary>Removes one file (File Properties → Remove).</summary>
    public void RemoveFile(int fileNo)
    {
        if (fileNo < 0 || fileNo >= _files.Count) return;
        CloseAllPlots();
        int g = 0;
        for (int i = _groups.Count - 1; i >= 0; i--) if (fileNo >= _groups[i].StartPos) { g = i; break; }
        try { _files[fileNo].Dispose(); } catch (Exception) { }
        _files.RemoveAt(fileNo);
        _groups[g].NFiles--;
        for (int i = g + 1; i < _groups.Count; i++) _groups[i].StartPos--;
        if (_groups[g].NFiles == 0) _groups.RemoveAt(g);
        Shell.RefreshCommands();
    }

    /// <summary>Writes <c>&lt;temp&gt;\~id</c> group file for <c>OPEN … ASDATABASE</c>.</summary>
    public string CreateTempDB(string fileListCsv, string? groupId)
    {
        var list = fileListCsv.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        if (list.Count == 0) return "";
        string tmpName = Settings.TempDir + "\\~" + (groupId ?? "");
        try
        {
            using var w = AnsiText.CreateWriter(tmpName);
            if (groupId != null)
            {
                string gid = groupId.Trim();
                w.Write((gid.Length == 0 ? "File Group" : gid) + "\r\n");
            }
            else
            {
                int j = list[0].LastIndexOf('\\') + 1;
                int n = list[0].IndexOf('.', j);
                int len = n > j ? n - j : 12;
                w.Write(list[0].Substring(j, Math.Min(len, list[0].Length - j)) + "\r\n");
            }
            w.Write(list.Count + "\r\n");
            foreach (var f in list) w.Write(f + "\r\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return "";
        }
        return tmpName;
    }

    // ------------------------------------------------------------------ parameter loading

    /// <summary>Resets the parameter nomenclature.</summary>
    private static void ResetNomen(ParameterData p, string? name, string? units, string? descr)
    {
        if (name != null) p.Name = name;
        if (units != null) p.Units = units;
        if (descr != null) p.Description = descr;
    }

    /// <summary>
    /// Loads parameters (FR-LOAD-031): returns the curves (count &gt; 0), 0 none, −1 cancelled, −2 time slice error.
    /// </summary>
    public int LoadParameters(List<Curve> curves, string[] xAxisNomen, ref string errMsg)
    {
        curves.Clear();
        string xAxis = XAxis;
        SymbolSubstitute(ref xAxis);
        XAxis = xAxis;
        PlotConfig.PidVsPid = !string.Equals("(default)", XAxis, StringComparison.Ordinal);
        errMsg = "";
        bool canceled = false, timeErr = false, xNomSet = false;
        var engine = new ExpressionEngine(this);
        var parmSet = new List<string>();
        {
            var t = new Strtok(YAxis);
            string? p;
            while ((p = t.Next(",")) != null) parmSet.Add(p);
        }
        var blockRt = new bool[parmSet.Count];
        var data = new List<ParameterData>();
        var origTimes = new List<ParameterData?>();
        double[] slice = new double[2];

        for (int nGroup = 0; nGroup < _groups.Count && !canceled; nGroup++)
        {
            var g = _groups[nGroup];
            var gfiles = GroupFiles(nGroup);
            if (TimeReference.SetTime(gfiles, 0, g.TStart, g.TRef, out slice[0]) == 0)
            {
                if (TimeReference.SetTime(gfiles, 0, g.TStop, g.TRef, out slice[1]) != 0)
                {
                    if (ShowDialogs && Message(Crt.Sprintf("Invalid Stop/Reference time combination\nfor %s", g.GroupName), PromptButtons.OkCancel, PromptIcon.Warning) == PromptResult.Cancel)
                    {
                        canceled = true;
                        break;
                    }
                    continue;
                }
            }
            else
            {
                if (ShowDialogs && Message(Crt.Sprintf("Invalid Start/Reference time combination\nfor %s", g.GroupName), PromptButtons.OkCancel, PromptIcon.Warning) == PromptResult.Cancel)
                {
                    canceled = true;
                    break;
                }
                continue;
            }

            ParameterData? xpid = null;
            if (PlotConfig.PidVsPid)
            {
                xpid = LoadXAxis(engine, nGroup, slice, ref errMsg);
                if (xpid == null)
                {
                    if (LastStatus == (int)SunError.TimeSlice) timeErr = true;
                    errMsg = "X Axis: " + XAxis;
                    continue;
                }
            }

            for (int iParm = 0; iParm < parmSet.Count && !canceled; iParm++)
            {
                string pnBuf = parmSet[iParm];
                bool hasSymbolic = SymbolSubstitute(ref pnBuf);
                string pn = pnBuf;
                for (int j = 0; j < g.NFiles; j++)
                {
                    if (data.Count >= MaxCurves) break;
                    var file = gfiles[j];
                    if (blockRt[iParm] && file.Info.FileSig == SunSignatures.PcGoalRealtime) continue;
                    ParameterData? loaded = null;
                    if (ExprSyntax.IsFunction(pn) != Fn.None)
                    {
                        SetStatus(" Performing function " + (pn.Length > 80 ? pn[..80] : pn) + "...");
                        loaded = engine.DoFunction(gfiles, j, nGroup, pn, slice, g.TRef, ref errMsg);
                        if (loaded != null)
                        {
                            if (hasSymbolic) KeepSymbolicName(loaded, parmSet[iParm]);
                        }
                        else if (errMsg.StartsWith("!abort", StringComparison.Ordinal))
                        {
                            canceled = true;
                            break;
                        }
                    }
                    else if (ExprSyntax.IsExpression(pn))
                    {
                        SetStatus(" Calculating expression " + pn);
                        loaded = engine.Parse(gfiles, j, nGroup, pn, slice, g.TRef, ref errMsg);
                        if (loaded != null)
                        {
                            if (hasSymbolic) KeepSymbolicName(loaded, parmSet[iParm]);
                        }
                        else if (errMsg.StartsWith("!abort", StringComparison.Ordinal))
                        {
                            canceled = true;
                            break;
                        }
                        else if (errMsg.StartsWith("!break", StringComparison.Ordinal))
                        {
                            errMsg = errMsg.Replace("!break", "");
                            break;
                        }
                    }
                    else
                    {
                        string buf;
                        int colon = pn.LastIndexOf(':');
                        if (colon >= 0)
                        {
                            string s1 = file.Info.Path.ToLowerInvariant();
                            string s2 = pn[..colon].ToLowerInvariant();
                            string s3 = g.GroupName.ToLowerInvariant();
                            if (!s1.Contains(s2) && !s3.Contains(s2)) continue;
                            buf = pn[(colon + 1)..];
                        }
                        else buf = pn;
                        SetStatus("Retrieving...");
                        string? userDescription = null;
                        int q = buf.IndexOf('"');
                        if (q >= 0)
                        {
                            userDescription = buf[(q + 1)..];
                            buf = buf[..q];
                            int q2 = userDescription.IndexOf('"');
                            if (q2 >= 0) userDescription = userDescription[..q2];
                        }
                        double? refTime = null;
                        TimeTag? refGmt = null;
                        if (!g.TRef.Equals("NONE", StringComparison.OrdinalIgnoreCase))
                        {
                            refGmt = TimeReference.GetRefGmt(gfiles, j, g.TRef);
                            if (refGmt == null)
                            {
                                refTime = TimeReference.GetRefTime(gfiles, j, g.TRef);
                                if (refTime == null)
                                {
                                    errMsg = Crt.Sprintf("Reference Event, %s, not found\nfile: %s\ngroup: %s", g.TRef, file.Info.Path, g.GroupName);
                                    continue;
                                }
                            }
                        }
                        loaded = Read(file, buf, slice[0], slice[1], refTime, refGmt, PlotConfig.Use64BitTime);
                        if (loaded == null)
                        {
                            if (LastReadNoData)
                                errMsg += (errMsg.Length > 0 ? "\n" : "") + Crt.Sprintf("%s: %s\n  %s", buf, NoDataException.Text, file.Info.Path);
                            else if (LastStatus == (int)SunError.TimeRef) errMsg = SunErrorText.Get(LastStatus);
                            else if (LastStatus == (int)SunError.TimeSlice || LastStatus == (int)SunError.TimeError) timeErr = true;
                        }
                        else
                        {
                            if (hasSymbolic) ResetNomen(loaded, parmSet[iParm], null, null);
                            if (userDescription != null)
                                ResetNomen(loaded, null, null, userDescription.Length > 0 && userDescription[0] == '\\' ? loaded.Description + userDescription : userDescription);
                        }
                    }

                    if (loaded != null)
                    {
                        ParameterData? orig = null;
                        if (PlotConfig.PidVsPid)
                        {
                            orig = loaded.Clone();
                            if (engine.SyncXaxis(loaded, xpid) != 0) Message("Error synchronizing X axis data");
                        }
                        if (Settings.FirstOccurrence && loaded.Source?.Info.FileSig == SunSignatures.PcGoalRealtime) blockRt[iParm] = true;
                        data.Add(loaded);
                        origTimes.Add(orig);
                    }
                }
            }

            if (PlotConfig.PidVsPid && xpid != null)
            {
                if (!xNomSet)
                {
                    xAxisNomen[0] = xpid.Name;
                    xAxisNomen[1] = xpid.Units;
                    xAxisNomen[2] = xpid.Description;
                    xNomSet = true;
                }
            }
            else if (!xNomSet && data.Count > 0)
            {
                var last = data[^1];
                xAxisNomen[0] = last.TimeName;
                xAxisNomen[1] = last.TimeUnits;
                xAxisNomen[2] = last.TimeDescription;
                xNomSet = true;
            }
        }
        SetStatus("");
        if (canceled) return -1;
        if (data.Count == 0 && timeErr) return -2;
        for (int n = 0; n < data.Count; n++)
        {
            var c = new Curve(data[n])
            {
                LineSize = PlotConfig.LineSize,
                LineStyle = 0,
                UseSymbols = PlotConfig.Symbols,
                ScatterPlot = false,
                SymbolId = n % ColorIndex.MaxColors,
                ColorIndex = n % ColorIndex.MaxColors,
                TabFormat = Settings.DataPrecision,
                TabLegend = true,
                LogicTxt = Settings.LogicTxtIdx,
                SourceItem = data[n].Name,
                XItem = PlotConfig.PidVsPid ? XAxis : null,
                OriginalTime = origTimes[n],
            };
            curves.Add(c);
        }
        return curves.Count;
    }

    private static void KeepSymbolicName(ParameterData p, string symbolic)
    {
        string descr = p.Description.Trim();
        if (descr.Length == 0) descr = p.Name;
        string name = symbolic;
        int q = name.IndexOf('"');
        if (q != -1) name = name[..q];
        ResetNomen(p, name, null, descr);
    }

    private ParameterData? LoadXAxis(ExpressionEngine engine, int nGroup, double[] slice, ref string errMsg)
    {
        var g = _groups[nGroup];
        var gfiles = GroupFiles(nGroup);
        ParameterData? xpid = null;
        for (int j = 0; j < g.NFiles; j++)
        {
            if (ExprSyntax.IsFunction(XAxis) != Fn.None)
            {
                SetStatus(" Performing function " + XAxis);
                if ((xpid = engine.DoFunction(gfiles, j, nGroup, XAxis, slice, g.TRef, ref errMsg)) != null) break;
            }
            else if (ExprSyntax.IsExpression(XAxis))
            {
                SetStatus(" Calculating " + XAxis);
                if ((xpid = engine.Parse(gfiles, j, nGroup, XAxis, slice, g.TRef, ref errMsg)) != null) break;
            }
            else
            {
                SetStatus("Retrieving...");
                string buf = XAxis;
                int q = buf.IndexOf('"');
                if (q >= 0)
                {
                    buf = buf[..q];
                    XAxis = buf;
                }
                double? refTime = null;
                TimeTag? refGmt = null;
                xpid = null;
                if (!g.TRef.Equals("NONE", StringComparison.OrdinalIgnoreCase))
                {
                    refGmt = TimeReference.GetRefGmt(gfiles, j, g.TRef);
                    if (refGmt == null)
                    {
                        refTime = TimeReference.GetRefTime(gfiles, j, g.TRef);
                        if (refTime == null)
                        {
                            Message(Crt.Sprintf("Reference Event, %s, not found for %s", g.TRef, g.GroupName));
                            break;
                        }
                    }
                }
                xpid = Read(gfiles[j], buf, slice[0], slice[1], refTime, refGmt, false);
                if (xpid != null) break;
            }
        }
        if (xpid == null && XAxis.Contains(':'))
        {
            string xName = XAxis;
            if (ExprSyntax.IsFunction(xName) != Fn.None || ExprSyntax.IsExpression(xName))
            {
                for (int iGroup = 0; iGroup < _groups.Count && xpid == null; iGroup++)
                {
                    var ig = _groups[iGroup];
                    var ifiles = GroupFiles(iGroup);
                    var xSlice = new double[2];
                    if (TimeReference.SetTime(ifiles, 0, ig.TStart, ig.TRef, out xSlice[0]) == 0
                        && TimeReference.SetTime(ifiles, 0, ig.TStop, ig.TRef, out xSlice[1]) != 0)
                    {
                        Message("Invalid stop/reference time combination for group");
                        continue;
                    }
                    for (int j = 0; j < ig.NFiles; j++)
                    {
                        if (ExprSyntax.IsFunction(xName) != Fn.None)
                        {
                            if ((xpid = engine.DoFunction(ifiles, j, iGroup, xName, xSlice, ig.TRef, ref errMsg)) != null) break;
                        }
                        else if ((xpid = engine.Parse(ifiles, j, iGroup, xName, slice, ig.TRef, ref errMsg)) != null) break;
                    }
                }
            }
            else
            {
                int c = xName.LastIndexOf(':');
                string spec = xName[..c].ToLowerInvariant();
                string parm = xName[(c + 1)..];
                for (int iFile = 0; iFile < _files.Count && xpid == null; iFile++)
                {
                    int iGroup;
                    for (iGroup = _groups.Count - 1; iGroup > 0; iGroup--) if (iFile >= _groups[iGroup].StartPos) break;
                    var ig = _groups[iGroup];
                    if (!_files[iFile].Info.Path.ToLowerInvariant().Contains(spec) && !ig.GroupName.ToLowerInvariant().Contains(spec)) continue;
                    var xSlice = new double[2];
                    var nfiles = GroupFiles(nGroup);
                    var ng = _groups[nGroup];
                    if (TimeReference.SetTime(nfiles, 0, ng.TStart, ng.TRef, out xSlice[0]) == 0
                        && TimeReference.SetTime(nfiles, 0, ng.TStop, ng.TRef, out xSlice[1]) != 0)
                    {
                        Message("Invalid stop/reference time combination");
                        continue;
                    }
                    var ifiles = GroupFiles(iGroup);
                    if (!ig.TRef.Equals("NONE", StringComparison.OrdinalIgnoreCase))
                    {
                        var refGmt = TimeReference.GetRefGmt(ifiles, iFile - ig.StartPos, ig.TRef);
                        double? refTime = refGmt == null ? TimeReference.GetRefTime(ifiles, iFile - ig.StartPos, ig.TRef) : null;
                        if (refGmt != null || refTime != null) xpid = Read(_files[iFile], parm, xSlice[0], xSlice[1], refTime, refGmt, false);
                        else Message(Crt.Sprintf("Reference Event, %s, not found in parse()", ig.TRef));
                    }
                    else xpid = Read(_files[iFile], parm, xSlice[0], xSlice[1], null, null, false);
                }
            }
        }
        return xpid;
    }

    // ------------------------------------------------------------------ plotting

    /// <summary>PLOT command.</summary>
    public void Plot()
    {
        if (DataLoading || FileLoading)
        {
            Message("Can't execute, data/file load in progress");
            return;
        }
        if (_files.Count == 0 && ShowDialogs) OpenFileDialog(false);
        if (_files.Count == 0) return;
        if (YAxis.Length == 0 && ShowDialogs)
        {
            SelectParameter(ParmSelectMode.Normal);
            return;
        }
        LoadAndDisplay();
    }

    /// <summary>Loads parameters and displays them.</summary>
    public void LoadAndDisplay()
    {
        if (DataLoading || FileLoading)
        {
            Message("Can't execute, file/data load in progress");
            return;
        }
        DataLoading = true;
        Record("plot");
        SetRefreshPause(true);
        string saveY = YAxis, saveX = XAxis;
        YAxis = ScriptKeywords.TrimRightC(YAxis);
        XAxis = ScriptKeywords.TrimRightC(XAxis);
        var curves = new List<Curve>();
        string[] xAxisNomen = { "TIME", "Seconds", "Relative Time in Seconds" };
        string errMsg = "";
        bool fromWpd = false;
        List<Marker>? savedMarkers = null;
        List<RedlineMarker>? savedRedlines = null;
        int saveYNomen = PlotConfig.YNomen;
        int[] saveYLen = (int[])PlotConfig.YNomLen.Clone(), saveYOrd = (int[])PlotConfig.YNomOrder.Clone();
        string? bitmap = null;
        string tSlice = "";
        int ncurves;
        try
        {
            if (YAxis.EndsWith(".wpd", StringComparison.OrdinalIgnoreCase))
            {
                savedMarkers = Marker.CopySet(MasterMarkers);
                MasterMarkers.Clear();
                savedRedlines = RedlineMarker.CopySet(MasterRedlines);
                MasterRedlines.Clear();
                ncurves = LoadParametersFromWpd(curves, xAxisNomen, out bitmap);
                fromWpd = true;
            }
            else
                ncurves = LoadParameters(curves, xAxisNomen, ref errMsg);

            if (ncurves > 0)
            {
                if (!XAxis.Equals("(default)", StringComparison.OrdinalIgnoreCase) && PlotConfig.EndToEnd) PlotConfig.EndToEnd = false;
                int saveXNomen = PlotConfig.XNomen;
                int saveYNomen2 = PlotConfig.YNomen;
                string saveUdef = Settings.XUdef;
                if (Settings.AllowAutoLabel)
                {
                    if (PlotConfig.EndToEnd)
                    {
                        Settings.XUdef = "Accumulated Time Seconds (" + _groups[0].TStart + " to " + _groups[0].TStop + ")";
                        PlotConfig.XNomen = (PlotConfig.XNomen & Nomen.EFORMAT) | Nomen.UDEF;
                    }
                    else if (PlotConfig.PidVsPid)
                    {
                        Settings.XUdef = " (" + _groups[0].TStart + " to " + _groups[0].TStop + " seconds";
                        Settings.XUdef += !_groups[0].TRef.Equals("NONE", StringComparison.OrdinalIgnoreCase) ? " from " + _groups[0].TRef + ")" : ")";
                        PlotConfig.XNomen = Nomen.UDEF | PlotConfig.XNomen;
                    }
                }
                PlotNo++;
                tSlice = _groups[0].TStart + "," + _groups[0].TStop + "," + _groups[0].TRef;
                var markers = fromWpd ? MasterMarkers : (Settings.UseMarkers ? MasterMarkers : null);
                var plot = CreatePlot(curves, markers, xAxisNomen, Settings.XUdef, Settings.YUdef, Settings.XTitle, Settings.YTitle, _groups[0].TRef, tSlice, p =>
                {
                    p.PrevXNomen = saveXNomen;
                    p.PrevXUdef = saveUdef;
                    if (fromWpd || Settings.UseMarkers) p.Redlines = RedlineMarker.CopySet(MasterRedlines);
                    if (fromWpd)
                    {
                        p.Config.Style = PlotStyle.Tabular;
                        p.BackgroundFile = BackgroundFn;
                    }
                });
                _ = plot;
                PlotConfig.YNomen = saveYNomen2;
                PlotConfig.XNomen = saveXNomen;
                Settings.XUdef = saveUdef;
            }
            else if (ncurves == 0)
            {
                if (errMsg.Length > 0)
                    Message("Parameter(s) not available for time requested\nor not found in file group\n\n" + errMsg, PromptButtons.Ok, PromptIcon.Information);
                else
                    Message(Crt.Sprintf("Parameter(s) not available\nX=%s\nY=%s\nStatus: %s", XAxis, YAxis, SunErrorText.Get(LastStatus)), PromptButtons.Ok, PromptIcon.Information);
            }
            else if (ncurves == -2)
                Message("Data not available for requested time slice." + (Settings.Extrapolate ? "" : "\nExtrapolation is currently OFF."));
            else if (ncurves == -10)
                Message("Failed to open Multiplot display file (.wpd)");

            if (Settings.UsePlaceholder && IsScriptActive && ncurves <= 0) CreatePlaceholder(saveY, tSlice);
        }
        finally
        {
            YAxis = saveY;
            XAxis = saveX;
            if (fromWpd)
            {
                PlotConfig.YNomen = saveYNomen;
                PlotConfig.YNomLen = saveYLen;
                PlotConfig.YNomOrder = saveYOrd;
                if (savedMarkers != null)
                {
                    MasterMarkers.Clear();
                    MasterMarkers.AddRange(savedMarkers);
                }
                if (savedRedlines != null)
                {
                    MasterRedlines.Clear();
                    MasterRedlines.AddRange(savedRedlines);
                }
            }
            DataLoading = false;
            SetRefreshPause(false);
        }
    }

    private void CreatePlaceholder(string saveY, string tSlice)
    {
        string saved = YAxis;
        YAxis = "-1.40130E-45";
        var curves = new List<Curve>();
        string[] xn = new string[3];
        string err = "";
        int n = LoadParameters(curves, xn, ref err);
        YAxis = saved;
        if (n > 0)
        {
            string msg = "* Placeholder for {" + saveY + "} *";
            ResetNomen(curves[0].Data, msg, msg, msg);
            xn[0] = msg; xn[1] = saveY; xn[2] = msg;
            PlotNo++;
            CreatePlot(curves, null, xn, msg, msg, msg, msg, _groups.Count > 0 ? _groups[0].TRef : "NONE", tSlice, p =>
            {
                p.Config.Style = PlotStyle.Tabular;
                p.Config.YNomen = Nomen.NAME;
                p.TimeFreeze = 1;
            });
        }
        else
        {
            switch (Message("Failed to generate data for Placeholder plot.\nVerify validity of file parameter 1.", PromptButtons.AbortRetryIgnore, PromptIcon.Warning, PromptResult.Ignore))
            {
                case PromptResult.Ignore: Settings.ScriptPrompt = false; break;
                case PromptResult.Abort: AbortScript(true); break;
            }
        }
    }

    /// <summary>Creates a plot window.</summary>
    public PlotDocument CreatePlot(List<Curve> curves, IEnumerable<Marker>? markers, string[] xAxisNomen, string xUdef, string yUdef, string xTitle, string yTitle,
        string tRef, string tSlice, Action<PlotDocument>? beforeInit = null)
    {
        var plot = new PlotDocument(this, curves, PlotConfig, markers, xAxisNomen, xUdef, yUdef, xTitle, yTitle, tRef, tSlice);
        beforeInit?.Invoke(plot);
        plot.Initialize(PlotNo, Settings.MultiGroupBy, Settings.GmtFormat, UserCaption, ScriptRun != 0 ? ScriptFile : null,
            Settings.ContinueTitle, Settings.ContinueSubTitle, Settings.IgnoreStatic, Fix, DateTime.Now);
        if (plot.Config.Style == PlotStyle.Tabular && plot.BackgroundFile.Length > 0) LoadBackground(plot, plot.BackgroundFile);
        CancelAlign();
        _plots.Insert(0, plot);
        Shell.PlotAdded(plot, Settings.PlotMax);
        Shell.RefreshCommands();
        return plot;
    }

    private void LoadBackground(PlotDocument plot, string name)
    {
        string? path = File.Exists(name) ? name : WhereIs(name, Settings.WpdFolder) ?? WhereIs(name, Settings.FileSpec);
        plot.BackgroundFile = path ?? name;
        plot.BackgroundImage = null;
    }

    /// <summary>Closes every plot window.</summary>
    public void CloseAllPlots()
    {
        foreach (var p in _plots.ToList()) RemovePlot(p);
    }

    /// <summary>Loads parameters from a WPD display file (FR-LOAD-040; COLOR_INDEX and DECn honoured, DEV-TAB-001).</summary>
    public int LoadParametersFromWpd(List<Curve> curves, string[] xAxisNomen, out string? bitmap)
    {
        bitmap = null;
        string saveName = YAxis;
        string? path = File.Exists(YAxis) ? YAxis : WhereIs(YAxis, Settings.WpdFolder);
        if (path == null) return -10;
        List<string> lines;
        try { lines = AnsiText.ReadAllLines(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return -10; }
        var display = new List<string>();
        YAxis = "";
        foreach (string raw in lines)
        {
            string rec = (raw.Length > 1023 ? raw[..1023] : raw).Trim();
            if (rec.Length > 0 && rec[0] == ';') continue;
            bool L(int n, string k) => rec.Length >= n && rec[..n].Equals(k, StringComparison.OrdinalIgnoreCase);
            if (L(7, "bitmap ")) bitmap = rec[7..];
            else if (L(12, "coloredtext ")) PlotConfig.LegendColor = !rec.Substring(12, Math.Min(3, rec.Length - 12)).Equals("ON", StringComparison.OrdinalIgnoreCase) ? false : true;
            else if (L(7, "ynomen "))
            {
                string err = "";
                CommandParsers.ParseNomen(rec[7..], true, PlotConfig.YNomLen, PlotConfig.YNomOrder, out int set, ref err);
                PlotConfig.YNomen = set;
            }
            else if (L(8, "display "))
            {
                if (rec.Contains('@') && rec.Contains(','))
                {
                    string item = rec[8..];
                    display.Add(item);
                    if (YAxis.Length > 0) YAxis += ",";
                    YAxis += item[..item.IndexOf('@')];
                }
            }
            else if (RedlineMarker.TryParseCommand(rec, out var rl))
            {
                if (rl != null) RedlineMarker.AddTo(MasterRedlines, rl);
                else Message("Invalid redline command in WPD file\nCommand: " + rec);
            }
            else if (L(7, "marker "))
            {
                var m = Marker.Parse(rec[7..]);
                if (m != null) Marker.AddTo(MasterMarkers, m);
                else Message("Invalid marker command in WPD file\nCommand: " + rec);
            }
        }
        string err2 = "";
        int n = LoadParameters(curves, xAxisNomen, ref err2);
        if (n > 0)
        {
            if (bitmap != null) BackgroundFn = bitmap;
            foreach (var c in curves)
            {
                for (int j = 0; j < display.Count; j++)
                {
                    bool matched = false;
                    string szName = display[j][..display[j].IndexOf('@')];
                    int colon;
                    if (szName.Equals(c.Name, StringComparison.OrdinalIgnoreCase)) matched = true;
                    else if ((colon = szName.IndexOf(':')) != -1 && szName.IndexOf('[') == -1)
                    {
                        string fSpec = szName[..colon].ToLowerInvariant();
                        string fName = (c.Data.Source?.Info.Path ?? "").ToLowerInvariant();
                        if (fName.Contains(fSpec)) matched = true;
                        else
                        {
                            int f, gi;
                            for (f = _files.Count - 1; f > 0; f--) if (ReferenceEquals(_files[f], c.Data.Source)) break;
                            for (gi = _groups.Count - 1; gi > 0; gi--) if (f >= _groups[gi].StartPos) break;
                            if (_groups[Math.Max(gi, 0)].GroupName.ToLowerInvariant().Contains(fSpec)) matched = true;
                        }
                    }
                    if (!matched) continue;
                    string pos = display[j][(display[j].IndexOf('@') + 1)..];
                    int x = Crt.Atoi(pos);
                    int y = Crt.Atoi(pos[(pos.IndexOf(',') + 1)..]);
                    c.TabPosition = unchecked(((uint)(ushort)y << 16) | (ushort)x);
                    string up = pos.ToUpperInvariant();
                    int off, v;
                    if ((off = up.IndexOf("COLOR_INDEX=", StringComparison.Ordinal)) != -1 && (v = Crt.Atoi(up[(off + 12)..])) >= 1 && v <= ColorIndex.MaxColors)
                        c.ColorIndex = v - 1;
                    if ((off = up.IndexOf("TAB_LEGEND=", StringComparison.Ordinal)) != -1 && (v = Crt.Atoi(up[(off + 11)..])) >= 0 && v <= 1)
                        c.TabLegend = v != 0;
                    if ((off = up.IndexOf("TAB_FORMAT=", StringComparison.Ordinal)) != -1)
                    {
                        string f = up[(off + 11)..];
                        if (f.Contains("HEX2")) c.TabFormat = TabFormat.Hex2;
                        else if (f.Contains("HEX4")) c.TabFormat = TabFormat.Hex4;
                        else if (f.Contains("HEX8")) c.TabFormat = TabFormat.Hex8;
                        else if (f.Contains("OCT3")) c.TabFormat = TabFormat.Oct3;
                        else if (f.Contains("OCT6")) c.TabFormat = TabFormat.Oct6;
                        else if (f.Contains("OCT12")) c.TabFormat = TabFormat.Oct12;
                        else if (f.Contains("GENERAL")) c.TabFormat = TabFormat.General;
                        else if (f.Contains("DEC"))
                        {
                            v = Crt.Atoi(f[(f.IndexOf("DEC", StringComparison.Ordinal) + 3)..]);
                            c.TabFormat = v >= 0 && v <= 7 ? v : TabFormat.General;
                        }
                    }
                    display.RemoveAt(j);
                    break;
                }
            }
        }
        YAxis = saveName;
        return n;
    }

    /// <summary>Adds a curve (ADDPARAMETER / Add to Plot).</summary>
    public void AddParameterToPlot(int clickX = 0, int clickY = 0, int width = 1, int height = 1)
    {
        var plot = ActivePlot;
        if (_files.Count == 0 || plot == null)
        {
            Message("Plot not available for Add Parameter", PromptButtons.Ok, PromptIcon.Information);
            return;
        }
        string saveY = YAxis;
        bool load = true;
        int x = 0, y = 0;
        if (IsInteractive)
        {
            if (SelectParameter(ParmSelectMode.AddParameter))
            {
                x = (int)((clickX + 1f) / width * 4096);
                y = (int)((clickY + 1f) / height * 4096);
                if (SnapToGridValue && LayoutGridSpacingValue > 0)
                {
                    x = (x + LayoutGridSpacingValue / 2) / LayoutGridSpacingValue * LayoutGridSpacingValue;
                    y = (y + LayoutGridSpacingValue / 2) / LayoutGridSpacingValue * LayoutGridSpacingValue;
                }
                y = 4096 - y;
            }
            else load = false;
        }
        if (load)
        {
            int i, j;
            if ((i = YAxis.IndexOf('@')) > 0 && (j = YAxis.IndexOf(':', i)) != -1)
            {
                x = Crt.Atoi(YAxis[(i + 1)..]);
                y = Crt.Atoi(YAxis[(j + 1)..]);
                YAxis = YAxis[..i];
            }
            var curves = new List<Curve>();
            string[] xn = new string[3];
            string err = "";
            int n = LoadParameters(curves, xn, ref err);
            if (n > 0)
            {
                SetRefreshPause(true);
                if (plot.Config.Style == PlotStyle.Tabular && n == 1)
                {
                    curves[0].TabPosition = unchecked(((uint)(ushort)y << 16) | (ushort)x);
                    curves[0].TabLegend = false;
                }
                plot.AddCurves(curves, PlotConfig);
                plot.ResetMinMax(plot.Config.EndToEnd ? 0x0003 : 0x0202, false);
                Invalidate(plot);
                SetRefreshPause(false);
                if (plot.Config.Style == PlotStyle.Tabular && IsInteractive)
                {
                    Shell.Dialogs.TraceAttributes(this, plot, plot.NCurves - 1, out _);
                    Invalidate(plot);
                }
            }
            else
                Message("Parameter(s) not available for selected time\nor not found in file group\n", PromptButtons.Ok, PromptIcon.Information);
        }
        YAxis = saveY;
    }

    /// <summary>Returns true on OK.</summary>
    public bool SelectParameter(ParmSelectMode mode)
    {
        if (DataLoading || FileLoading)
        {
            Message("Can't execute, file/data load in progress");
            return false;
        }
        if (_files.Count == 0) OpenFileDialog(false);
        if (_files.Count == 0) return false;
        string oldX = XAxis;
        if (Shell.Dialogs.ParameterSelect(this, mode, out bool doPlot))
        {
            if (mode == ParmSelectMode.Normal && oldX != XAxis) Record("xaxis " + XAxis);
            if (mode == ParmSelectMode.Normal) Record("yaxis " + YAxis);
            SaveParm(YAxis);
            SaveParm(XAxis);
            if (mode == ParmSelectMode.Normal && doPlot) LoadAndDisplay();
            return true;
        }
        if (ScriptRun != 0) AbortScript(true);
        return false;
    }

    /// <summary>
    /// After an interactive file open: when files are open but no plot window exists yet, opens the
    /// Parameter Selection dialog to guide the user to a first plot. Never while a script runs.
    /// </summary>
    public void PromptFirstPlot()
    {
        if (ScriptRun != 0 || RunHidden || _files.Count == 0 || _plots.Count > 0 || DataLoading || FileLoading) return;
        SelectParameter(ParmSelectMode.Normal);
    }

    /// <summary>File → Open (close existing files first) / File → Add dialog; true when a file was loaded.</summary>
    public bool OpenFileDialog(bool closeFirst, bool asGroup = false)
    {
        string? dir = Settings.FileSpec.Split(';')[0];
        var files = Shell.Dialogs.OpenFile(asGroup ? "Open Files as Group" : (closeFirst ? "Open" : "Add"), Settings.FileFilters.Replace("||", ""), dir, true);
        if (files == null || files.Length == 0) return false;
        if (closeFirst) CloseAllFiles();
        if (asGroup || files.Length > 1 && asGroup)
        {
            string tmp = CreateTempDB(string.Join(",", files), null);
            bool ok = false;
            if (tmp.Length > 0)
            {
                ok = LoadFileGroup(tmp) == 0;
                try { File.Delete(tmp); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            return ok;
        }
        bool loaded = false;
        foreach (var f in files)
        {
            if (LoadFileGroup(f) != 0) break;
            loaded = true;
            Record("OPEN " + f.Replace(";", "\\;"));
        }
        return loaded;
    }
}
