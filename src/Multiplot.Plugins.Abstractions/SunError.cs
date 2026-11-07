// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

namespace Multiplot.Plugins;

/// <summary>
/// Data access and write error codes. Values must not change.
/// </summary>
public enum SunError
{
    /// <summary>No errors.</summary>
    Success = 0,
    FileOpen,
    UnknownFormat,
    MemAlloc1,
    MemAlloc2,
    FileSig,
    MemAlloc3,
    MemAlloc4,
    MemAlloc5,
    MemAlloc6,
    MemAlloc7,
    MemAlloc8,
    PidNotFound,
    TimeError,
    ConvPtr,
    MemAlloc9,
    MemAlloc14,
    NullFile,
    TimeRef,
    MemAlloc10,
    BadRef,
    ComRead,
    MemAlloc11,
    ComReadFail,
    MemAlloc12,
    TimeSlice,
    RefreshTime,
    MemAlloc15,
    LzwDict,
    LzwReadFail,
    FiltOpen,
    StreamFind,
    ParmFind,
    MemAlloc13,
    ParmFilt,
    UtcMemAlloc,
    FmtNotSupp,
    UtcNotAvail,
    WrtStrAlloc,
    WrtFileExists,
    WrtOpenErr,
    WrtFwrComBlk,
    WrtFskComBlk,
    WrtCloseErr,
    WrtNotEnoParms,
    WrtInvalidIdx,
    WrtParmFilRst,
    WrtHeaderWrt,
    WrtHeaderFsk,
    WrtFileFull,
    WrtInvalidFormat,
    WrtMissingIdx,
    WrtBufferAlloc,
    WrtInvalidArgs,
    WrtSeekFailed,
    WrtNotValid,
    WrtZeroPoints,
    FileType,
    NoArchive,
    FmtNoSup,
    WrtReadOnly,
    WrtRevNoMatch,
    WrtNotRevising,
    LinkHdrRd,
    /// <summary>Invalid error code (must be last).</summary>
    Undefined,
}

/// <summary>Error message table and lookup.</summary>
public static class SunErrorText
{
    private static readonly string[] Strings =
    {
        "OK",
        "file open error",
        "file format ID unknown",
        "alloc failed for file control struct",
        "alloc failed for file map struct",
        "invalid file signature",
        "alloc failed for for data type conversion",
        "alloc failed for time data buffer",
        "alloc failed for file header struct",
        "alloc failed for file map struct",
        "alloc failed for block size array",
        "alloc failed for file map storage",
        "name not found in file map",
        "time array error",
        "failed to get pointer to type conversion buffer",
        "couldn't allocate memory for pid struct",
        "couldn't allocate memory for data buffer",
        "null sun file structure used",
        "parameter retrieval by UTC when UTC not available",
        "couldn't allocate memory for access reference UTC",
        "attempt to access time using UTC reference, use TIME",
        "failed to open Sun file to retrieve comment block",
        "couldn't allocate memory to retrieve comment block",
        "fread failed reading comment block data",
        "couldn't allocate memory for time stamps UTC->TIME",
        "data not available for selected time slice",
        "time load/re-load attempt during file update",
        "alloc failed for Nomen pointer structure",
        "corrupt dictionary table (currupted file)",
        "general file read error",
        "failed to open filter file",
        "failed to find stream",
        "failed to find parm in stream",
        "memory allocation failed",
        "failed to find parm in file map (filter)",
        "failed to allocate memory for UTC array",
        "SUN_MULTITIME64 not supported on this platform",
        "UTC array requested but not available",
        "structure allocation failed",
        "output file exists",
        "failed to open output file",
        "failure writing comment block",
        "failure seeking to comment block",
        "failure closing output file",
        "not enough parameters written",
        "invalid file index parameter",
        "failure seeking to EOF/end of data",
        "failure writing header item",
        "failure seeking to header item",
        "too many parameter writes attempted",
        "invalid file type requested",
        "MULTI_TIME write missing timestamp",
        "failure allocating data buffer",
        "invalid arguments to function",
        "file seek() failed",
        "function not valid for file format or mode",
        "attempt to write with zero point count",
        "unrecognized file type",
        "no archive source selected",
        "file format not supported on platform",
        "file is read only",
        "no matching parameter name for revision",
        "file not in revise mode",
        "error reading MULTITIME parameter link header",
        "undefined error code",
    };

    /// <summary>Message for a (possibly negative) error code.</summary>
    public static string Get(int code)
    {
        int a = Math.Abs(code);
        return Strings[a < (int)SunError.Undefined ? a : (int)SunError.Undefined];
    }

    /// <summary>Message for an error code.</summary>
    public static string Get(SunError code) => Get((int)code);

    /// <summary>Non-fatal codes reported as warnings on a successful read.</summary>
    public static bool IsNonFatal(SunError code) =>
        code is SunError.MemAlloc10 or SunError.MemAlloc12 or SunError.UtcNotAvail or SunError.UtcMemAlloc;
}

/// <summary>Error thrown by data access providers; carries a SUN (or plugin) status code.</summary>
public class DataAccessException : Exception
{
    /// <summary>Status code (a <see cref="SunError"/> value for the SUN provider).</summary>
    public int Code { get; }

    /// <summary>Creates an exception with a SUN error code.</summary>
    public DataAccessException(SunError code) : base(SunErrorText.Get(code)) => Code = (int)code;

    /// <summary>Creates an exception with a plugin specific code and message.</summary>
    public DataAccessException(int code, string message) : base(message) => Code = code;

    /// <summary>The code as <see cref="SunError"/> (meaningful for the SUN provider).</summary>
    public SunError SunCode => (SunError)Code;
}

/// <summary>
/// A parameter is declared in the file map but has no data blocks (MULTITIME map pointer 0).
/// The status stays <see cref="SunError.TimeSlice"/> so numeric codes are unchanged (FR-SUN-090).
/// </summary>
public sealed class NoDataException : DataAccessException
{
    /// <summary>User-visible message.</summary>
    public const string Text = "parameter has no data in file";

    /// <summary>Creates the exception.</summary>
    public NoDataException() : base((int)SunError.TimeSlice, Text) { }
}
