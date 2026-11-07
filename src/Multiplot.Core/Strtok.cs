// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

namespace Multiplot.Core;

/// <summary>
/// Faithful emulation of C <c>strtok</c> over a NUL-terminated character buffer, used where
/// comment-block parsing depends on its exact token boundaries.
/// </summary>
public sealed class Strtok
{
    private readonly char[] _buf;
    private int _pos;

    public Strtok(string text, int start = 0)
    {
        _buf = new char[text.Length - start + 1];
        text.CopyTo(start, _buf, 0, text.Length - start);
        _buf[^1] = '\0';
        _pos = 0;
    }

    /// <summary>Next token or null (strtok(NULL, delims) semantics; first call starts at the buffer start).</summary>
    public string? Next(string delims)
    {
        int i = _pos;
        if (i >= _buf.Length) return null;
        while (_buf[i] != '\0' && delims.IndexOf(_buf[i]) >= 0) i++;
        if (_buf[i] == '\0') { _pos = i; return null; }
        int start = i;
        while (_buf[i] != '\0' && delims.IndexOf(_buf[i]) < 0) i++;
        string tok = new string(_buf, start, i - start);
        if (_buf[i] != '\0') { _buf[i] = '\0'; _pos = i + 1; }
        else _pos = i;
        return tok;
    }
}
