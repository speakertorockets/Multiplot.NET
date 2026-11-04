using Multiplot.Core;
using Multiplot.Core.Model;
using Multiplot.ImportExport;
using Multiplot.Plugins;
using Multiplot.Rendering;
using Multiplot.Scripting;

namespace Multiplot.Application;

/// <summary>The script command interpreter (chapter 05 §5.6).</summary>
public sealed partial class Session
{
    private ScriptRunner? _scripts;

    /// <summary>Script runner (DoBatch, stack, pause/resume).</summary>
    public ScriptRunner Scripts => _scripts ??= new ScriptRunner(this);

    /// <summary>ON/OFF argument: OFF → false, ON or empty → true, else null (syntax error).</summary>
    private static bool? OnOffArg(string parm) =>
        parm.Equals("OFF", StringComparison.OrdinalIgnoreCase) ? false
        : parm.Equals("ON", StringComparison.OrdinalIgnoreCase) || parm.Length == 0 ? true : null;

    private static bool Is(string a, string b) => a.Equals(b, StringComparison.OrdinalIgnoreCase);

    private static bool Left(string s, int n, string k) => s.Length >= n && s[..n].Equals(k, StringComparison.OrdinalIgnoreCase);

    /// <summary>Pre-processes a command line, including symbol substitution.</summary>
    public string PreProcess(string command, bool report = true)
    {
        ScriptLexer.PreProcess(command, out string cmdBuf, s => { SymbolSubstitute(ref s, report); return s; });
        return cmdBuf;
    }

    /// <summary>Executes one command line; returns the command key.</summary>
    public async Task<Cmd> ExecuteAsync(string command)
    {
        string cmdBuf = PreProcess(command);
        Cmd key = ScriptKeywords.GetCmdKey(cmdBuf);
        string cmdParm = ScriptKeywords.GetParm(cmdBuf);
        bool hadError = false;
        string errTxt = "invalid syntax";
        void Err(string? t = null) { hadError = true; if (t != null) errTxt = t; }
        bool? oo = OnOffArg(cmdParm);
        int i;

        switch (key)
        {
            case Cmd.Comment:
                break;

            case Cmd.Pause:
                if (IsScriptActive && ScriptFile.Length > 0 && ScriptFile[0] != '*')
                {
                    if (cmdParm.Length == 0)
                    {
                        ScriptRun = RunState.Paused;
                        PauseNext = 0;
                    }
                    else await TimedPauseAsync((float)Crt.Atof(cmdParm));
                }
                break;

            case Cmd.Prompt:
                Message(cmdParm.Length == 0 ? "Continue        " : cmdParm.Replace('|', '\n'), PromptButtons.Ok, PromptIcon.Information);
                break;

            case Cmd.Input:
                {
                    var req = CommandParsers.ParseInput(cmdParm);
                    string? text = RunHidden ? req.Default : Shell.Ui.Input(req.Prompt, req.Caption, req.Default);
                    if (RunHidden) ScriptLog("[auto] INPUT " + req.Prompt + " -> default '" + req.Default + "' (hidden mode)");
                    if (text != null) await ExecuteAsync("SET " + req.Symbol + "=" + text);
                }
                break;

            case Cmd.PauseMode:
                if (Is(cmdParm, "NEXTCOMMAND")) SetPauseMode(PauseMode.NextCommand);
                else if (Is(cmdParm, "NEXTPLOT")) SetPauseMode(PauseMode.NextPlot);
                else Err();
                break;

            case Cmd.Set:
                await SetCommandAsync(cmdParm, Err);
                break;

            case Cmd.Run:
                RunProgram(cmdParm);
                break;

            case Cmd.AutoRun:
                if (Is(cmdParm, "CLEAR")) AutoRunList.Clear();
                else if ((i = cmdParm.IndexOf(',')) > 0) AutoRunList.Add(cmdParm[..i].ToUpperInvariant() + cmdParm[i..]);
                else Err();
                break;

            case Cmd.Call:
                if (cmdParm.Length > 0)
                {
                    ScriptRun = RunState.Calling;
                    ScriptCall = cmdParm;
                }
                else Err("missing filename");
                break;

            case Cmd.PrinterName:
                Settings.UserPrinter = cmdParm;
                ScriptLog("PRINTERNAME stored (printing is not supported)");
                break;

            case Cmd.XNomen:
            case Cmd.YNomen:
                {
                    bool y = key == Cmd.YNomen;
                    UDefBuf = y ? Settings.YUdef : Settings.XUdef;
                    string up = cmdParm.ToUpperInvariant();
                    string e = errTxt;
                    if (CommandParsers.ParseNomen(up, y, y ? PlotConfig.YNomLen : PlotConfig.XNomLen, y ? PlotConfig.YNomOrder : PlotConfig.XNomOrder, out SetNomen, ref e) != 0) Err(e);
                    if (up.Contains("TITLE")) Settings.ContinueTitle = true;
                    if (up.Contains("EFORMAT")) SetNomen |= Nomen.EFORMAT;
                    ApplyNomen(y);
                }
                break;

            case Cmd.XUdef:
            case Cmd.YUdef:
                UDefBuf = cmdParm;
                SetNomen = -1;
                ApplyNomen(key == Cmd.YUdef);
                break;

            case Cmd.XTitle:
            case Cmd.YTitle:
                {
                    bool sub = key == Cmd.YTitle;
                    if (Is(cmdParm, "OFF")) { if (sub) Settings.ContinueSubTitle = false; else Settings.ContinueTitle = false; }
                    else if (Is(cmdParm, "ON")) { if (sub) Settings.ContinueSubTitle = true; else Settings.ContinueTitle = true; }
                    else if (Is(cmdParm, "CLEAR"))
                    {
                        if (sub) Settings.YTitle = ""; else Settings.XTitle = "";
                        ApplyTitle(sub, false);
                    }
                    else
                    {
                        string title = cmdParm;
                        int n = cmdParm.ToLowerInvariant().IndexOf("next=", StringComparison.Ordinal);
                        if (n >= 0)
                        {
                            title = title.Remove(n, 5);
                            if (sub) { Settings.YTitle = title; Settings.ContinueSubTitle = true; }
                            else { Settings.XTitle = title; Settings.ContinueTitle = true; }
                        }
                        else
                        {
                            if (sub) Settings.YTitle = title; else Settings.XTitle = title;
                            bool save = sub ? Settings.ContinueSubTitle : Settings.ContinueTitle;
                            if (sub) Settings.ContinueSubTitle = true; else Settings.ContinueTitle = true;
                            ApplyTitle(sub, false);
                            if (!Settings.ApplyAll) { if (sub) Settings.ContinueSubTitle = save; else Settings.ContinueTitle = save; }
                        }
                    }
                }
                break;

            case Cmd.MultiY:
                if (Is(cmdParm, "OFF")) SetMultiY(false);
                else if (Is(cmdParm, "ON") || cmdParm.Length == 0) SetMultiY(true);
                else if (Left(cmdParm, 8, "GROUPBY_"))
                {
                    int n = Crt.Atoi(cmdParm[8..]);
                    if (n > 0) SetGroupBy(n);
                    else if (Is(cmdParm, "GROUPBY_NAME")) SetGroupBy(GroupBy.Name);
                    else if (Is(cmdParm, "GROUPBY_UNITS")) SetGroupBy(GroupBy.Units);
                    else if (Is(cmdParm, "GROUPBY_DESCRIPTION")) SetGroupBy(GroupBy.Description);
                    else if (Is(cmdParm, "GROUPBY_UDEF")) SetGroupBy(GroupBy.UserDefined);
                    else SetGroupBy(GroupBy.Independent);
                    SetMultiY(true);
                }
                else Err();
                break;

            case Cmd.LogX:
            case Cmd.LogY:
                if (oo == null) Err(); else SetLog(key == Cmd.LogY, oo);
                break;

            case Cmd.GridLines:
                if (Is(cmdParm, "OFF")) SetGridlines(false, 0, Settings.ApplyAll);
                else if (Is(cmdParm, "ON") || cmdParm.Length == 0) SetGridlines(true, PlotConfig.GridStyle, Settings.ApplyAll);
                else if (Is(cmdParm, "solid")) SetGridlines(true, 0, Settings.ApplyAll);
                else if (Is(cmdParm, "dotted")) SetGridlines(true, 2, Settings.ApplyAll);
                else if (Is(cmdParm, "dashed")) SetGridlines(true, 1, Settings.ApplyAll);
                else Err();
                break;

            case Cmd.EndToEnd:
                if (oo == null) Err(); else SetEndToEnd(oo);
                break;

            case Cmd.Rescale:
                RescaleXY();
                break;

            case Cmd.FixXAutoY:
                if (oo == null) Err(); else Settings.FixXAutoY = oo.Value;
                break;

            case Cmd.FixX:
            case Cmd.FixY:
                if (cmdParm.Length > 0)
                {
                    var r = CommandParsers.ParseFix(cmdParm, key == Cmd.FixY);
                    if (r.Error) Err();
                    else ApplyFix(key == Cmd.FixY, r);
                }
                break;

            case Cmd.Zoom:
                {
                    string z = cmdParm.ToLowerInvariant();
                    if (z.Contains("out"))
                    {
                        if (z.Contains('x')) ZoomX(false);
                        else if (z.Contains('y')) ZoomY(false);
                        else { ZoomX(false); ZoomY(false); }
                    }
                    else if (z.Contains("in"))
                    {
                        if (z.Contains('x')) ZoomX(true);
                        else if (z.Contains('y')) ZoomY(true);
                        else { ZoomX(true); ZoomY(true); }
                    }
                    else if (z.Length == 0) { ZoomX(true); ZoomY(true); }
                    else Err();
                }
                break;

            case Cmd.Pan:
                {
                    string z = cmdParm.ToLowerInvariant();
                    ShiftMode mode = z.Contains("full") ? ShiftMode.MinMax : z.Contains("page") ? ShiftMode.Page : ShiftMode.Increment;
                    if (z.Contains("left")) ShiftX(false, mode);
                    else if (z.Contains("right")) ShiftX(true, mode);
                    else if (z.Contains("up")) ShiftY(true);
                    else if (z.Contains("down")) ShiftY(false);
                    else Err();
                }
                break;

            case Cmd.Style:
                {
                    int? s = cmdParm.ToLowerInvariant() switch
                    {
                        "2dline" => PlotStyle.Line2D,
                        "2dscatter" => PlotStyle.Scatter2D,
                        "3dline" => PlotStyle.Line3D,
                        "3darea" => PlotStyle.Area3D,
                        "logic" => PlotStyle.Logic,
                        "tabular" => PlotStyle.Tabular,
                        _ => null,
                    };
                    if (s == null) Err(); else SetStyle(s.Value);
                }
                break;

            case Cmd.FontSize:
                {
                    int fs = Crt.Atoi(cmdParm);
                    if (fs <= 4 && fs >= 1) SetFontSize(fs); else Err("out of range");
                }
                break;

            case Cmd.GridStyle:
                if (Is(cmdParm, "solid")) SetGridlines(true, 0, Settings.ApplyAll);
                else if (Is(cmdParm, "dashed")) SetGridlines(true, 1, Settings.ApplyAll);
                else if (Is(cmdParm, "dotted")) SetGridlines(true, 2, Settings.ApplyAll);
                else Err();
                break;

            case Cmd.MinorTics: if (oo == null) Err(); else SetMinorTics(oo); break;
            case Cmd.BoldFrame: if (oo == null) Err(); else SetBoldFrame(oo); break;
            case Cmd.ExtCmdPolling:
                if (oo == null) Err(); else { ExtCmdPolling = oo.Value; ScriptLog("EXTCMDPOLLING ignored (mailslot not supported)"); }
                break;
            case Cmd.ScaleGroup: if (oo == null) Err(); else SetScaleGroup(oo); break;
            case Cmd.StairStep: if (oo == null) Err(); else SetStairStep(oo); break;
            case Cmd.ClockTime: if (oo == null) Err(); else SetClockTime(oo); break;

            case Cmd.GmtFormat:
                SetGmtFormat(cmdParm.Length == 0 ? "%03u/%02u:%02u:%02u.%03u" : Core.Plotting.Scale.MakeCFormat(cmdParm));
                break;

            case Cmd.Legend:
                if (Is(cmdParm, "OFF")) SetLegend(false);
                else if (Is(cmdParm, "SINGLE")) SetLegendColumns(1);
                else if (Is(cmdParm, "COLOR")) SetLegendColor(true);
                else if (Is(cmdParm, "NOCOLOR")) SetLegendColor(false);
                else if (Is(cmdParm, "DOUBLE")) SetLegendColumns(2);
                else if (Left(cmdParm, 7, "RESERVE"))
                {
                    int n = Math.Abs(Crt.Atoi(cmdParm[7..]));
                    if (n <= 32) { Settings.ReservedLegend = n; RefreshAllPlots(); }
                }
                else if (Left(cmdParm, 7, "COLUMNS"))
                {
                    int n = Math.Abs(Crt.Atoi(cmdParm[7..]));
                    if (n <= 64) SetLegendColumns(n);
                }
                else if (Is(cmdParm, "ON") || cmdParm.Length == 0) SetLegend(true);
                else Err();
                break;

            case Cmd.GridOffset:
                {
                    int n = Math.Min(90, Math.Abs(Crt.Atoi(cmdParm)));
                    if (n != Settings.MinGridOffset) { Settings.MinGridOffset = n; RefreshAllPlots(); }
                }
                break;

            case Cmd.EmfWide:
                if (oo == null) Err(); else { Settings.EmfWide = oo.Value; ScriptLog("EMFWIDE stored (EMF output is not supported)"); }
                break;

            case Cmd.TimeStamp: if (oo == null) Err(); else SetTimeStamp(oo); break;
            case Cmd.TestInfo: if (oo == null) Err(); else SetTestInfo(oo); break;
            case Cmd.ScriptPrompt: if (oo == null) Err(); else SetScriptPrompt(oo); break;
            case Cmd.EmbeddedScript: if (oo == null) Err(); else Settings.EmbeddedScript = oo.Value; break;

            case Cmd.LineSize:
                if (Is(cmdParm, "3DTHIN")) Set3DSize(3, Settings.ApplyAll);
                else if (Is(cmdParm, "3DMEDIUM")) Set3DSize(2, Settings.ApplyAll);
                else if (Is(cmdParm, "3DWIDE")) Set3DSize(1, Settings.ApplyAll);
                else if (cmdParm.Length == 1 && char.IsAsciiDigit(cmdParm[0])) SetLineSize(Crt.Atoi(cmdParm), Settings.ApplyAll);
                else Err();
                break;

            case Cmd.Symbols:
                if (Is(cmdParm, "OFF") || cmdParm == "0") SetSymbols(false);
                else if ((i = Crt.Atoi(cmdParm)) > 0)
                {
                    PlotConfig.SymFreq = (uint)i;
                    PlotConfig.Symbols = true;
                    SetSymbolDensity((uint)i);
                    SetSymbols(true);
                }
                else if (Is(cmdParm, "ON")) SetSymbols(true);
                else Err();
                break;

            case Cmd.DataRate:
                {
                    string up = cmdParm.ToUpperInvariant();
                    int Density() { int k = up.IndexOfAny(new[] { ',', ' ' }); return k > 0 ? Crt.Atoi(up[(k + 1)..]) : 0; }
                    if (up.StartsWith("REBUILD", StringComparison.Ordinal))
                    {
                        Settings.RebuildRate = up.Contains("OFF");
                        ToggleRebuildRate();
                    }
                    else if (up.StartsWith("FULL", StringComparison.Ordinal)) SetDataRate(DataRateMode.Full);
                    else if (up.StartsWith("AVERAGE", StringComparison.Ordinal))
                    {
                        if (Density() > 0) PlotConfig.AvgFreq = (uint)Density();
                        SetDataRate(DataRateMode.Averaged);
                    }
                    else if (up.StartsWith("SAMPLE", StringComparison.Ordinal))
                    {
                        if (Density() > 0) PlotConfig.SamFreq = (uint)Density();
                        SetDataRate(DataRateMode.Sampled);
                    }
                    else if (up.StartsWith("3D", StringComparison.Ordinal))
                    {
                        if (up.IndexOfAny(new[] { ',', ' ' }) > 0) SetDensity(2, Density() > 0 ? (uint)Density() : PlotConfig.ZaxFreq);
                        else Err("invalid density");
                    }
                    else Err();
                }
                break;

            case Cmd.Refresh:
                {
                    double r = Math.Abs(Crt.Atof(cmdParm));
                    if (r > 0)
                    {
                        if (AutoRefresh != 0) { AutoRefresh = 0; StartRefreshTimer(); }
                        AutoRefresh = (int)(1000.0 * (float)r);
                        StartRefreshTimer();
                    }
                    else if ((Is(cmdParm, "off") || Left(cmdParm, 1, "0")) && AutoRefresh != 0)
                    {
                        AutoRefresh = 0;
                        StartRefreshTimer();
                    }
                    else RefreshNow();
                }
                break;

            case Cmd.SavePic:
                SavePicCommand(cmdParm, Err);
                break;

            case Cmd.SaveSun:
                {
                    var plot = ActivePlot;
                    if (plot != null) SaveSun(CommandParsers.ValidateFileName(cmdParm, Settings.ExportDir, plot.PlotNo, "sun"));
                    else Err("No existing plot data to save");
                }
                break;

            case Cmd.CopyMeta:
            case Cmd.CopyBmp:
            case Cmd.CopyJpeg:
                if (Is(cmdParm, "clipboard")) CopyPlotToClipboard();
                else
                {
                    if (ActivePlot == null) { Err("Nothing to copy"); break; }
                    bool all = cmdParm.Length > 0 && Is(cmdParm, "all");
                    string type = key == Cmd.CopyJpeg ? "JPEG" : key == Cmd.CopyBmp ? "BMP" : "PNG";
                    if (key == Cmd.CopyMeta) ScriptLog("COPYMETA: EMF is not supported, saving PNG instead (DEV-XPT-001)");
                    foreach (var p in TargetPlots(all))
                    {
                        string fn = CommandParsers.ValidateFileName(all ? "" : cmdParm, Settings.ExportDir, p.PlotNo, PlotImage.Extension(type));
                        SavePicture(p, type, fn);
                    }
                }
                break;

            case Cmd.Import:
                if (cmdParm.Length > 0)
                {
                    if (Is(cmdParm, "clipboard")) ImportClipboard();
                    else
                    {
                        var r = CommandParsers.ParseImport(cmdParm);
                        if (r == null) Err("invalid file specification");
                        else if (ImportAscii(r.Source, false, r.Destination, r.IndexColumn, r.SkipLines, r.TitleRecord, r.GenerateTimeTags, r.StandardFormat, r.Reference) != 0)
                            Message("Import Failed.");
                    }
                }
                else if (ShowDialogs) ImportDialog();
                break;

            case Cmd.Open:
            case Cmd.New:
                if (cmdParm.Length > 0)
                {
                    if (key == Cmd.New) CloseAllFiles();
                    int k = cmdParm.ToLowerInvariant().IndexOf("asdatabase", StringComparison.Ordinal);
                    if (k >= 0)
                    {
                        string id = cmdParm[(k + 10)..].Trim();
                        string tmp = CreateTempDB(cmdParm[..k], id.Length == 0 ? null : id);
                        if (tmp.Length > 0)
                        {
                            if (LoadFileGroup(tmp) == -1) AbortScript(true);
                            try { File.Delete(tmp); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                        }
                        else OpenFileDialog(key == Cmd.New, true);
                    }
                    else if (LoadFileGroup(cmdParm) == -1) AbortScript(true);
                }
                else OpenFileDialog(key == Cmd.New);
                break;

            case Cmd.Plot:
                if (cmdParm.Length > 0)
                {
                    YAxis = ScriptLexer.ParmSetClean(cmdParm);
                    SaveParm(YAxis);
                }
                Plot();
                break;

            case Cmd.XAxis:
                XAxis = cmdParm.Length > 0 ? ScriptLexer.ParmSetClean(cmdParm) : "(default)";
                break;

            case Cmd.YAxis:
                if (cmdParm.Length > 0)
                {
                    YAxis = ScriptLexer.ParmSetClean(cmdParm);
                    SaveParm(YAxis);
                }
                else YAxis = "";
                break;

            case Cmd.Clear:
                {
                    string c = cmdParm.ToLowerInvariant();
                    if (c.Length == 0 || c == "plots") CloseAllPlots();
                    else if (c == "files") CloseAllFiles();
                    else if (c == "last") CloseLast();
                    else if (c.Contains("markers"))
                    {
                        if (c.Contains("all")) ClearAllMarkers();
                        else
                        {
                            var nums = new List<int>();
                            int first = 7, last;
                            int m = Crt.Atoi(c.Length > 8 ? c[8..] : "");
                            while (m != 0)
                            {
                                nums.Add(m);
                                if ((last = c.IndexOf(',', first)) > first)
                                {
                                    m = Crt.Atoi(c[++last..]);
                                    first = last;
                                }
                                else m = 0;
                            }
                            ClearMarkers(nums);
                        }
                    }
                    else if (c.Contains('$'))
                    {
                        if (c.Contains("$$")) Symbols.Entries.Clear();
                        else Symbols.Remove(c.Replace("$", ""));
                    }
                    else Err();
                }
                break;

            case Cmd.Exit:
                ScriptRun = RunState.ExitRequested;
                break;

            case Cmd.Window:
                if (Is(cmdParm, "minimize")) Shell.MainWindowState(1);
                else if (Is(cmdParm, "maximize")) Shell.MainWindowState(2);
                else if (Is(cmdParm, "restore")) Shell.MainWindowState(0);
                else
                {
                    int first = 0, last;
                    int l = 0, t = 0, rr = 0, b = 0;
                    if ((last = cmdParm.IndexOf(',', first)) > 0) l = Crt.Atol(cmdParm[first..]);
                    if ((last = cmdParm.IndexOf(',', first)) > 0) t = Crt.Atol(cmdParm[(first = ++last)..]);
                    if ((last = cmdParm.IndexOf(',', first)) > 0) rr = Crt.Atol(cmdParm[(first = ++last)..]);
                    if ((last = cmdParm.IndexOf(',', first)) > 0)
                    {
                        b = Crt.Atol(cmdParm[(first = ++last)..]);
                        if (t != b) Shell.MoveMainWindow(l, t, rr, b);
                    }
                    else Err();
                }
                break;

            case Cmd.Print:
                ScriptLog("PRINT ignored (printing is not supported): " + cmdParm);
                SetStatus("PRINT ignored (printing is not supported)");
                break;

            case Cmd.Time:
                if (cmdParm.Length == 0) { if (ShowDialogs) Shell.Dialogs.TimeSlice(this, false); }
                else if (Is(cmdParm, "MULTI")) { if (ShowDialogs) Shell.Dialogs.TimeSlice(this, true); }
                else
                {
                    var trip = CommandParsers.ParseTime(cmdParm, Settings.TStop, Settings.TRef);
                    int groupNo = 0;
                    foreach (var t in trip)
                    {
                        Settings.TStart = t.Start;
                        Settings.TStop = t.Stop;
                        Settings.TRef = t.Ref;
                        if (t.GroupName != null)
                        {
                            var g = _groups.FirstOrDefault(x => x.GroupName.Equals(t.GroupName, StringComparison.OrdinalIgnoreCase));
                            if (g != null) { g.TRef = t.Ref; g.TStart = t.Start; g.TStop = t.Stop; }
                            break;
                        }
                        for (int k = groupNo; k < _groups.Count; k++) { _groups[k].TRef = t.Ref; _groups[k].TStart = t.Start; _groups[k].TStop = t.Stop; }
                        groupNo++;
                    }
                    TimeReferenceChanged();
                }
                break;

            case Cmd.Freeze:
                if (!Freeze(!Is(cmdParm, "OFF"))) Err("current data not realtime");
                break;

            case Cmd.PrinterMargin:
                {
                    float r = (float)Crt.Atof(cmdParm);
                    if (r >= 0f && r <= 10f) Settings.MarginSize = r; else Err("out of range");
                }
                break;

            case Cmd.ColorPrinter: if (oo == null) Err(); else Settings.ColorPrinter = oo.Value; break;
            case Cmd.SystemLoad: SetSystemLoad(!Is(cmdParm, "OFF")); break;
            case Cmd.StreamCert: SetStreamCert(!Is(cmdParm, "OFF")); break;
            case Cmd.TabHistory: SetTabHistory(!Is(cmdParm, "OFF")); break;
            case Cmd.DefTabTime: SetDefTabTime(!Is(cmdParm, "OFF")); break;
            case Cmd.Landscape: if (oo == null) Err(); else Settings.Landscape = oo.Value; break;
            case Cmd.AutoLabel: if (oo == null) Err(); else Settings.AllowAutoLabel = oo.Value; break;
            case Cmd.FilePath: Settings.FileSpec = cmdParm; break;
            case Cmd.DbPath: Settings.DbFolder = cmdParm; break;
            case Cmd.ScriptPath: Settings.ScriptDir = cmdParm; break;
            case Cmd.ExportPath:
                Settings.ExportDir = cmdParm.Length > 2 ? cmdParm.TrimEnd('\\') : cmdParm;
                break;
            case Cmd.WpdPath: Settings.WpdFolder = cmdParm; break;
            case Cmd.Favorites: if (cmdParm.Length > 0) Settings.FavFile = cmdParm; break;

            case Cmd.LoadSettings:
                if (cmdParm.Length > 0) Settings.IniFile = cmdParm;
                if (!LoadSettings(Settings.IniFile)) Message("Specified INI file not found");
                break;

            case Cmd.SaveSettings:
                if (cmdParm.Length > 0) Settings.IniFile = cmdParm;
                SaveSettings();
                break;

            case Cmd.SaveParmList:
            case Cmd.SaveParmDetails:
            case Cmd.SaveFileDetails:
                if (cmdParm.Length > 0)
                {
                    int fileNo = Crt.Atoi(cmdParm) >= 0 ? Crt.Atoi(cmdParm) : 0;
                    int pos = cmdParm.IndexOfAny(new[] { ',', ' ' });
                    if (pos != -1 && cmdParm.Length > pos + 1)
                    {
                        string fn = cmdParm[(pos + 1)..];
                        if (key == Cmd.SaveParmDetails) DetailsWriter.SaveParmDetails(ActivePlot, fileNo - 1, fn);
                        else if (key == Cmd.SaveParmList) DetailsWriter.SaveParmList(_files, fileNo - 1, fn);
                        else DetailsWriter.SaveFileDetails(_files, _groups, fileNo - 1, fn, Data.GetDescription);
                    }
                    else Err("incomplete command");
                }
                else Err("incomplete command");
                break;

            case Cmd.MarkerSet:
                {
                    string m = ScriptKeywords.TrimRightC(cmdParm);
                    if (m.Length > 5)
                    {
                        if (Left(m, 7, "topmost")) SetMarkersTopmost(!m.ToLowerInvariant().Contains(" off"));
                        else if (Left(m, 5, "save ")) MarkersToFile(m[5..]);
                        else if (Left(m, 5, "load ")) MarkersFromFile(m[5..]);
                        else Err("invalid command");
                    }
                    else Err("incomplete command");
                }
                break;

            case Cmd.ShowVersion: if (oo == null) Err(); else SetShowVersion(oo); break;
            case Cmd.ShowUserName: if (oo == null) Err(); else SetShowUserName(oo); break;
            case Cmd.TimeSlide: if (oo == null) Err(); else SetTimeSlide(oo); break;
            case Cmd.IgnoreStatic: if (oo == null) Err(); else SetIgnoreStatic(oo); break;
            case Cmd.DropoutIgnore: if (oo == null) Err(); else SetDropoutIgnore(oo); break;
            case Cmd.DropoutMarker: if (oo == null) Err(); else SetDropoutMarker(oo); break;
            case Cmd.ShowScript: if (oo == null) Err(); else SetShowScript(oo); break;
            case Cmd.FirstOccurrence: if (oo == null) Err(); else Settings.FirstOccurrence = oo.Value; break;
            case Cmd.Extrapolate:
                if (oo == null) Err();
                else { Settings.Extrapolate = !oo.Value; ToggleExtrapolate(); }
                break;
            case Cmd.ShowPageNo: if (oo == null) Err(); else SetShowPageNo(oo); break;
            case Cmd.LegendShift: if (oo == null) Err(); else SetLegendShift(oo); break;

            case Cmd.Tile:
                if (Is(cmdParm, "VERTICAL")) Arrange(WindowArrange.TileVertical);
                else if (Is(cmdParm, "HORIZONTAL") || cmdParm.Length == 0) Arrange(WindowArrange.TileHorizontal);
                else if (Is(cmdParm, "CASCADE")) Arrange(WindowArrange.Cascade);
                else if (Is(cmdParm, "STACKED")) Arrange(WindowArrange.Stack);
                else if (Is(cmdParm, "OFF")) { if (ActivePlot != null && !Shell.IsMaximized(ActivePlot)) Shell.Arrange(WindowArrange.MaximizeActive, null); }
                else Arrange(WindowArrange.TileGroup, cmdParm);
                break;

            case Cmd.Export:
                if (cmdParm.Length > 0 && Is(cmdParm, "clipboard")) ExportClipboard();
                else ToAscii(',', cmdParm.Length > 0 ? cmdParm : null);
                break;

            case Cmd.Tabulate:
                Tabulate();
                break;

            case Cmd.Tab:
                if (Is(cmdParm, "NEXT") || cmdParm.Length == 0) NextWindow();
                else if (Is(cmdParm, "PREVIOUS")) PreviousWindow();
                else Err();
                break;

            case Cmd.AutoTab:
                if ((i = Crt.Atoi(cmdParm)) >= 0)
                {
                    int k = cmdParm.IndexOfAny(new[] { ',', ' ' });
                    SetAutoTab(i, k > 0 ? cmdParm[(k + 1)..].Trim() : null);
                }
                else Err("invalid range");
                break;

            case Cmd.AutoCompact:
                if (cmdParm.Length > 0)
                {
                    Settings.CompactPoints = unchecked((uint)Crt.Atol(cmdParm));
                    int k = cmdParm.IndexOfAny(new[] { ',', ' ' });
                    Settings.CompactInterval = k != -1 ? (float)Crt.Atof(cmdParm[(k + 1)..]) : 1.0f;
                }
                else Err("missing parameters");
                break;

            case Cmd.AutoDiscard:
                if (cmdParm.Length > 0) Settings.DiscardSeconds = unchecked((uint)Crt.Atol(cmdParm));
                else Err("missing parameters");
                break;

            case Cmd.Caption:
                if (Is(cmdParm, "PARMSET")) SetCaptionSource(CaptionSource.ParmSet, Settings.ApplyAll);
                else if (Left(cmdParm, 4, "USER"))
                {
                    string text = cmdParm.Length > 5 ? cmdParm[5..].Trim() : "";
                    if (text.Length == 0) SetCaptionSource(CaptionSource.User, Settings.ApplyAll);
                    else SetUserCaption(text);
                }
                else if (Is(cmdParm, "PLOTNO")) SetCaptionSource(CaptionSource.PlotNo, Settings.ApplyAll);
                else if (Is(cmdParm, "SUBTITLE")) SetCaptionSource(CaptionSource.YTitle, Settings.ApplyAll);
                else if (Is(cmdParm, "TITLE")) SetCaptionSource(CaptionSource.XTitle, Settings.ApplyAll);
                else if (Is(cmdParm, "SCRIPT")) SetCaptionSource(CaptionSource.Script, Settings.ApplyAll);
                else if (Is(cmdParm, "OFF")) SetNoCaptionBar(true);
                else if (Is(cmdParm, "ON") || cmdParm.Length == 0) SetNoCaptionBar(false);
                else Err();
                break;

            case Cmd.SymbolSize:
                if (Is(cmdParm, "NORMAL")) SetSymbolSize(0);
                else if (Is(cmdParm, "SMALL")) SetSymbolSize(-1);
                else if (Is(cmdParm, "LARGE")) SetSymbolSize(1);
                else Err();
                break;

            case Cmd.ClearList:
                Array.Fill(PastParms, "");
                break;

            case Cmd.Toolbar:
                if (Is(cmdParm, "OFF")) SetToolbarVisible(false);
                else if (Is(cmdParm, "ON")) SetToolbarVisible(true);
                else if (cmdParm.Length == 0) SetToolbarVisible(null);
                else Err();
                break;

            case Cmd.StatusBar:
                if (Is(cmdParm, "OFF")) SetStatusBarVisible(false);
                else if (Is(cmdParm, "ON")) SetStatusBarVisible(true);
                else if (cmdParm.Length == 0) SetStatusBarVisible(null);
                else Err();
                break;

            case Cmd.AlignClosest: if (oo == null) Err(); else Settings.AlignClosest = oo.Value; break;
            case Cmd.CalcEveryTime: if (oo == null) Err(); else Settings.CalcEveryInstance = oo.Value; break;

            case Cmd.Marker:
                {
                    var m = new Marker();
                    int mNo = CommandParsers.ParseMarker(cmdParm, m, out bool global);
                    if (mNo >= 0) SetMarker(mNo, m, global); else Err();
                }
                break;

            case Cmd.Enable:
            case Cmd.Disable:
                {
                    bool en = key == Cmd.Enable;
                    if (Is(cmdParm, "MARKERS")) Settings.UseMarkers = en;
                    else if (Is(cmdParm, "TITLES")) Settings.ContinueTitle = en;
                    else if (Is(cmdParm, "SUBTITLES")) Settings.ContinueSubTitle = en;
                    else if (Is(cmdParm, "PCGHISTORY"))
                    {
                        Settings.PcgHistory = en;
                        Data.ExecuteCommand(en ? "history on" : "history off", SunSignatures.PcGoalRealtime);
                    }
                    else if (Left(cmdParm, 6, "PLUGIN")) SetPluginBlocked(cmdParm[6..].TrimStart(), !en);
                    else Err();
                }
                break;

            case Cmd.ScriptLog:
                if (Is(cmdParm, "OFF")) { if (LogOpen) ToggleScriptLog(); }
                else if (Is(cmdParm, "ON"))
                {
                    if (LogFileName.Length > 0)
                    {
                        if (!LogOpen) ToggleScriptLog();
                        else Err("script log already on");
                    }
                    else Err("script log name not set");
                }
                else if (cmdParm.Length > 0)
                {
                    if (LogOpen) ToggleScriptLog();
                    LogFileName = cmdParm;
                    ToggleScriptLog();
                }
                else Err();
                break;

            case Cmd.ApplyGlobal: if (oo == null) Err(); else SetApplyAll(oo); break;
            case Cmd.OptimizedDraw: if (oo == null) Err(); else { Settings.OptimizedDraw = oo.Value ? 1 : 0; RefreshAllPlots(); } break;

            case Cmd.RetainMtGmt:
            case Cmd.Bit64Time:
                if (key == Cmd.RetainMtGmt) Err("Command is deprecated\n\nUse 64BITTIME [ON | OFF] instead");
                if (oo == null) Err(); else Set64BitTime(oo);
                break;

            case Cmd.UseIniToolbar: if (oo == null) Err(); else Settings.IncludeToolbar = oo.Value; break;
            case Cmd.UsePlaceholder: if (oo == null) Err(); else Settings.UsePlaceholder = oo.Value; break;
            case Cmd.IncludeGmt: if (oo == null) Err(); else Settings.ShowGmt = oo.Value; break;

            case Cmd.PresentationMode:
                if (Is(cmdParm, "OFF")) { if (PresentationMode) TogglePresentationMode(); }
                else if (Is(cmdParm, "ON") || cmdParm.Length == 0) { if (!PresentationMode) TogglePresentationMode(); }
                else Err();
                break;

            case Cmd.PresentationFull:
                if (Is(cmdParm, "OFF")) { if (Settings.MaxPresentation) Settings.MaxPresentation = false; }
                else if ((Is(cmdParm, "ON") || cmdParm.Length == 0) && !Settings.MaxPresentation) Settings.MaxPresentation = true;
                else Err();
                break;

            case Cmd.LibCommand:
                if (cmdParm.Length > 0)
                {
                    uint sig = 0;
                    string text = cmdParm;
                    if (cmdParm.StartsWith("0x", StringComparison.Ordinal))
                    {
                        sig = Crt.Strtoul(cmdParm, 16);
                        text = cmdParm[(cmdParm.IndexOf(' ') + 1)..].TrimStart();
                    }
                    Data.ExecuteCommand(text, sig);
                    if (sig == SunSignatures.SunFormat) Data.ExecuteSunCommand(text);
                }
                else Err("missing command");
                break;

            case Cmd.PlaySound:
                ScriptLog("PLAYSOUND ignored (not supported): " + cmdParm);
                break;

            case Cmd.Mci:
                ScriptLog("MCI ignored (not supported): " + cmdParm);
                Symbols.Set("MCI_RETURN=");
                break;

            case Cmd.AddParameter:
                if (cmdParm.Length > 0)
                {
                    string save = YAxis;
                    YAxis = cmdParm;
                    AddParameterToPlot();
                    YAxis = save;
                }
                break;

            case Cmd.DataStats:
                DataStats(cmdParm);
                break;

            case Cmd.TabStepRate:
                {
                    float r = (float)Crt.Atof(cmdParm);
                    if (r >= 0f) Settings.TabStepRate = r; else Err("out of range");
                }
                break;

            case Cmd.ShowCoord: if (oo == null) Err(); else Settings.ShowCoord = oo.Value; break;
            case Cmd.ScaleRedlines: if (oo == null) Err(); else Settings.ScaleRedlines = oo.Value; break;
            case Cmd.SharedTime: if (oo == null) Err(); else Settings.SharedTabTime = oo.Value; break;

            case Cmd.Unknown:
                Err("unrecognized command");
                break;
        }

        if (hadError)
        {
            ScriptLog(cmdBuf + " -> " + errTxt.Replace("\n", " "));
            Message(cmdBuf + " -> " + errTxt);
        }

        if (PauseNext != 0)
        {
            if (PauseNext == PauseMode.NextPlot)
            {
                if (key == Cmd.Plot) { ScriptRun = RunState.Paused; PauseNext = 0; }
            }
            else if ((int)key > 0)
            {
                ScriptRun = RunState.Paused;
                PauseNext = 0;
            }
        }
        return key;
    }

    private async Task TimedPauseAsync(float sleepTime)
    {
        float accumulated = 0f;
        ScriptRun = RunState.TimedPause;
        while (accumulated < sleepTime)
        {
            if (sleepTime > 2) SetStatus(Crt.Sprintf("Paused %d...", (int)(sleepTime - accumulated)));
            await Shell.YieldAsync();
            if (ScriptRun != RunState.TimedPause) break;
            if (Shell.IsSpacePressed())
            {
                while (Shell.IsSpacePressed()) await Shell.YieldAsync();
                break;
            }
            await Task.Delay(sleepTime < .1 ? (int)(sleepTime * 1000) : 100);
            accumulated += 0.1f;
        }
        if (ScriptRun != RunState.Aborting && ScriptRun != RunState.ExitRequested) ScriptRun = RunState.Running;
        SetStatus("");
    }

    private void SavePicCommand(string cmdParm, Action<string?> err)
    {
        var plot = ActivePlot;
        if (plot == null) { err("Nothing to save"); return; }
        if (cmdParm.Length == 0) { CopyPlotToClipboard(); return; }
        int sp = cmdParm.IndexOf(' ');
        string szType = sp >= 0 ? cmdParm[..sp] : cmdParm;
        string fname = sp >= 0 ? cmdParm[(sp + 1)..] : "";
        string? type = PlotImage.NormalizeType(szType);
        if (type == null && szType.Equals("EMF", StringComparison.OrdinalIgnoreCase))
        {
            ScriptLog("SAVEPIC EMF: EMF is not supported, saving PNG instead (DEV-XPT-001)");
            type = "PNG";
        }
        if (type == null) { err("Format type invalid or not supported"); return; }
        if (fname.Equals("clipboard", StringComparison.OrdinalIgnoreCase)) CopyPlotToClipboard();
        else if (fname.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var p in TargetPlots(true))
                SavePicture(p, type, CommandParsers.ValidateFileName("", Settings.ExportDir, p.PlotNo, PlotImage.Extension(type)));
        }
        else SavePicture(plot, type, CommandParsers.ValidateFileName(fname, Settings.ExportDir, plot.PlotNo, PlotImage.Extension(type)));
    }

    private async Task SetCommandAsync(string cmdParm, Action<string?> err)
    {
        int i;
        if (Left(cmdParm, 5, "TRACE") || Left(cmdParm, 5, "LOGIC"))
        {
            SetTraceOrLogic(cmdParm);
        }
        else if (Left(cmdParm, 5, "COLOR"))
        {
            string c = cmdParm[5..].ToUpperInvariant();
            if (c.Contains("PRINTERTABLE")) await LoadColorTableAsync();
            else if (c.Contains("RESTORE")) RestoreColorTable();
            else if (CommandParsers.ParseSetColor(c, out int no, out int rgb, out bool global)) SetColor(no, Settings.ApplyAll || global, rgb);
            else err(null);
        }
        else if (Left(cmdParm, 10, "PRECISION "))
        {
            var (t, d) = CommandParsers.ParsePrecision(cmdParm[10..]);
            if (t != null) Settings.TimePrecision = t.Value;
            if (d != null) Settings.DataPrecision = d.Value;
        }
        else if (Left(cmdParm, 11, "BACKGROUND "))
        {
            BackgroundFn = cmdParm[11..].Trim();
            SetTabBackground(false);
        }
        else if (Left(cmdParm, 10, "GROUPNAME "))
        {
            string g = ScriptKeywords.TrimRightC(cmdParm);
            string lower = g.ToLowerInvariant();
            if ((i = lower.IndexOf("next=", StringComparison.Ordinal)) >= 0) GroupName = g[(i + 5)..].TrimStart();
            else if ((i = lower.IndexOf("all=", StringComparison.Ordinal)) >= 0)
            {
                GroupName = g[(i + 4)..].TrimStart();
                ApplyGroupName(true, false);
            }
            else
            {
                GroupName = g[10..].TrimStart();
                ApplyGroupName(false, false);
            }
        }
        else if (Left(cmdParm, 4, "USER") && cmdParm.Length > 4 && char.IsAsciiDigit(cmdParm[4]))
        {
            int n = Crt.Atoi(cmdParm[4..]) - 1;
            if (n >= 0 && n < Core.Model.AppSettings.NUserButtons)
            {
                int sp = cmdParm.IndexOf(' ');
                string rest = sp >= 0 ? cmdParm[(sp + 1)..] : "";
                int comma = rest.IndexOf(',');
                string label = comma >= 0 ? rest[..comma].Trim() : rest.Trim();
                string cmd = comma >= 0 ? rest[(comma + 1)..] : "";
                int c2 = cmd.IndexOf(',');
                if (c2 >= 0) cmd = cmd[..c2];
                cmd = cmd.Trim();
                if (label.Length > 0 && cmd.Length > 0)
                {
                    Settings.UserButtons[n].Label = label;
                    Settings.UserButtons[n].Command = cmd;
                    Shell.RefreshCommands();
                }
                else err("missing parameter");
            }
            else err("invalid function number");
        }
        else if (cmdParm.Contains('=')) Symbols.Set(cmdParm);
        else err(null);
    }

    /// <summary>Parses SET TRACE / SET LOGIC.</summary>
    private void SetTraceOrLogic(string setCmd)
    {
        var selected = ActivePlot;
        if (Left(setCmd, 5, "TRACE") && selected != null)
        {
            if (!CommandParsers.ApplySetTrace(setCmd, selected.Curves, out int t)) return;
            Invalidate(selected);
            if (Settings.ApplyAll)
            {
                var src = selected.Curves[t];
                foreach (var p in _plots.Skip(1))
                {
                    if (t >= p.NCurves) continue;
                    var c = p.Curves[t];
                    c.LineSize = src.LineSize; c.LineStyle = src.LineStyle; c.ScatterPlot = src.ScatterPlot; c.ColorIndex = src.ColorIndex;
                    c.SymbolId = src.SymbolId; c.UseSymbols = src.UseSymbols; c.TabFormat = src.TabFormat;
                    Invalidate(p);
                }
            }
        }
        else if (Left(setCmd, 6, "LOGIC "))
        {
            string buf = setCmd[6..].Trim();
            bool ok = false;
            int v;
            if (Left(buf, 6, "INDEX "))
            {
                if ((v = Crt.Atoi(buf[6..])) >= 1 && v <= Core.Model.AppSettings.NLogicLabels)
                {
                    Settings.LogicTxtIdx = v - 1;
                    ok = true;
                }
            }
            else if (Left(buf, 5, "TEXT "))
            {
                buf = buf[5..].TrimStart();
                int idx = Crt.Atoi(buf);
                buf = buf.Length > 0 ? buf[1..] : "";
                buf = buf.TrimStart(',', ' ');
                int delim = buf.IndexOf(',');
                if (idx >= 1 && idx <= Core.Model.AppSettings.NLogicLabels && delim != -1)
                {
                    Settings.LogicTxtTrue[idx - 1] = buf[..delim].Trim();
                    Settings.LogicTxtFalse[idx - 1] = buf[(delim + 1)..].Trim();
                    ok = true;
                }
            }
            if (ok)
            {
                foreach (var p in TargetPlots(Settings.ApplyAll))
                {
                    foreach (var c in p.Curves) c.LogicTxt = Settings.LogicTxtIdx;
                    Invalidate(p);
                }
            }
        }
    }

    /// <summary>Runs the <c>[script]</c> section of a comment block.</summary>
    public int ParseFileScript(string block)
    {
        int start = block.IndexOf("[script]", StringComparison.Ordinal);
        if (block.Length == 0 || start == -1) return 0;
        start += 8;
        foreach (string raw in block[start..].Split('\n'))
        {
            string cmd = raw.TrimEnd('\r');
            if (cmd.Length == 0) continue;
            if (cmd[0] == '[') break;
            _ = ExecuteAsync(cmd);
        }
        return 1;
    }

    /// <summary>Aborts the running script; <paramref name="confirm"/> asks the user first.</summary>
    public void AbortScript(bool confirm)
    {
        if (ScriptRun > 0)
        {
            if (!confirm || Message("Cancel script execution?", PromptButtons.YesNo, PromptIcon.Warning, PromptResult.Yes) == PromptResult.Yes)
                ScriptRun = RunState.Aborting;
        }
        else if (Scripts.StackDepth > 0)
        {
            if (!confirm || Message("Cancel paused scripts?", PromptButtons.YesNo, PromptIcon.Warning, PromptResult.Yes) == PromptResult.Yes)
                Scripts.ClearStack();
        }
    }
}
