// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

using Multiplot.Scripting;

namespace Multiplot.Application;

/// <summary>A paused or calling script.</summary>
public sealed record ScriptFrame(string FileName, int Offset, int LineNo, bool Resume);

/// <summary>
/// Script execution (<c>OnScriptRun/Pause/Resume/Next/Abort</c>, FR-SCR-020..028).
/// Runs on the UI context and yields after every command so plots appear progressively (NFR-SCR-001).
/// </summary>
public sealed class ScriptRunner
{
    private readonly Session _s;
    private readonly List<ScriptFrame> _stack = new();

    /// <summary>Creates a runner.</summary>
    public ScriptRunner(Session s) => _s = s;

    /// <summary>Number of stacked (paused/calling) scripts.</summary>
    public int StackDepth => _stack.Count;

    /// <summary>Stacked frames (top last).</summary>
    public IReadOnlyList<ScriptFrame> Stack => _stack;

    /// <summary>Removes all stacked scripts.</summary>
    public void ClearStack() => _stack.Clear();

    /// <summary>Raised when a script starts or ends (menu state).</summary>
    public event Action? StateChanged;

    /// <summary>Runs a chosen script file (refuses while recording or running).</summary>
    public async Task RunFromUserAsync(string fileName)
    {
        if (_s.Recording)
        {
            _s.Message("Can't execute script during record.\nCancel recording first.");
            return;
        }
        if (_s.IsScriptActive)
        {
            _s.Message("Can't execute script.\nCancel or pause current script first.");
            return;
        }
        string? dir = Path.GetDirectoryName(fileName);
        if (!string.IsNullOrEmpty(dir))
        {
            try { Directory.SetCurrentDirectory(dir); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        await RunAsync(fileName);
    }

    /// <summary>Runs a script file.</summary>
    public async Task RunAsync(string batchName, int startAt = 0, int lineNo = 0)
    {
        if (_s.IsScriptActive)
        {
            _s.Message("Cancel current script first");
            return;
        }
        string spec = batchName.Replace("\"", "");
        if (spec.Length == 0)
        {
            _s.Message("Script filename is NULL.");
            return;
        }
        string? path = File.Exists(spec) ? spec : Session.WhereIs(spec[(spec.LastIndexOf('\\') + 1)..], _s.Settings.ScriptDir);
        byte[]? data = null;
        if (path != null)
        {
            try { data = File.ReadAllBytes(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { data = null; }
        }
        if (data == null)
        {
            _s.Message("Failure opening script " + batchName);
            return;
        }
        _s.Data.SetOption(1003);
        var reader = new ScriptReader(data, startAt);
        _s.ScriptLineNo = lineNo;
        _s.ScriptRun = RunState.Running;
        _s.ScriptFile = path!;
        StateChanged?.Invoke();
        await _s.Shell.YieldAsync();

        while (_s.ScriptRun == RunState.Running)
        {
            string cmd = reader.ReadCommand(out int lines);
            _s.ScriptLineNo += lines;
            try { await _s.ExecuteAsync(cmd); }
            catch (Exception ex)
            {
                _s.DiagLog("Script command failed: " + cmd.Trim() + " " + ex);
                _s.Message(ScriptLexer.Clean(cmd) + " -> " + ex.Message);
            }
            await _s.Shell.YieldAsync();
            if (_s.Shell.IsSpacePressed())
            {
                _s.ScriptRun = RunState.Paused;
                while (_s.Shell.IsSpacePressed()) await _s.Shell.YieldAsync();
            }
            if (_s.ScriptRun is RunState.Paused or RunState.Calling)
            {
                _stack.Add(new ScriptFrame(path!, reader.Position, _s.ScriptLineNo, _s.ScriptRun != RunState.Paused));
                if (_s.ScriptRun == RunState.Calling) _stack.Add(new ScriptFrame(_s.ScriptCall, 0, 0, true));
                else _s.SetStatus("");
            }
            if (reader.Eof) break;
        }

        int exitState = _s.ScriptRun;
        _s.ScriptRun = RunState.Idle;
        _s.Data.SetOption(1004);
        StateChanged?.Invoke();
        switch (exitState)
        {
            case RunState.Running:
                if (_stack.Count > 0 && _stack[^1].Resume) await ResumeAsync();
                break;
            case RunState.Calling:
                await ResumeAsync();
                break;
            case RunState.Aborting:
                _stack.Clear();
                break;
            case RunState.ExitRequested:
                _s.Shell.RequestExit();
                break;
        }
    }

    /// <summary>Resumes the paused script.</summary>
    public async Task ResumeAsync()
    {
        if (_stack.Count == 0) return;
        var f = _stack[^1];
        _stack.RemoveAt(_stack.Count - 1);
        await RunAsync(f.FileName, f.Offset, f.LineNo);
    }

    /// <summary>Pauses the script (Space / menu).</summary>
    public async Task PauseToggleAsync()
    {
        switch (_s.ScriptRun)
        {
            case RunState.Idle:
                if (_stack.Count > 0) await ResumeAsync();
                break;
            case RunState.Running:
                _s.ScriptRun = RunState.Paused;
                break;
            case RunState.TimedPause:
                _s.ScriptRun = RunState.Running;
                break;
        }
    }

    /// <summary>Runs the next script command.</summary>
    public async Task NextAsync()
    {
        if (_stack.Count == 0) return;
        _s.PauseNext = _s.Settings.PauseMode;
        await ResumeAsync();
    }

    /// <summary>
    /// Runs a script to completion inside the current command (nested script run used by
    /// SET COLOR PRINTERTABLE and user buttons).
    /// </summary>
    public async Task RunNestedAsync(string file)
    {
        int prev = _s.ScriptRun;
        string prevFile = _s.ScriptFile;
        int prevLine = _s.ScriptLineNo;
        _s.ScriptRun = prev == RunState.Idle ? RunState.Idle : RunState.Paused;
        await RunAsync(file);
        _s.ScriptRun = prev;
        _s.ScriptFile = prevFile;
        _s.ScriptLineNo = prevLine;
    }

    /// <summary>
    /// Executes a single command as a pseudo script (user buttons): prompting on,
    /// script name <c>*internal</c>; a CALL is queued and run as a script.
    /// </summary>
    public async Task ExecuteInternalAsync(string command)
    {
        string cmdBuf = ScriptLexer.Clean(command);
        if (ScriptKeywords.GetCmdKey(cmdBuf) == Cmd.Call)
        {
            string fn = ScriptKeywords.GetParm(cmdBuf);
            if (fn.Length == 0) return;
            _stack.Add(new ScriptFrame(fn, 0, 0, true));
            await ResumeAsync();
            return;
        }
        int state = _s.ScriptRun;
        string file = _s.ScriptFile;
        bool prompt = _s.Settings.ScriptPrompt;
        _s.ScriptRun = RunState.Running;
        _s.ScriptFile = "*internal";
        _s.Settings.ScriptPrompt = true;
        try { await _s.ExecuteAsync(command); }
        finally
        {
            if (_s.ScriptRun == RunState.ExitRequested) _s.Shell.RequestExit();
            _s.ScriptRun = state;
            _s.ScriptFile = file;
            _s.Settings.ScriptPrompt = prompt;
        }
    }
}
