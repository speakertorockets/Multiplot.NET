# 04 — Data Access Layer and .NET Plugin API

Owner decision: the SUN formats are implemented natively in managed code (chapter 03); other
formats and live sources are provided by **.NET plugins** through the API of this chapter. Native
plugin DLLs are **not** loaded. The sample plugin is `samples/MinimalPlugin`.

---

## 4.1 Core data model

**FR-ACC-001** The data-access layer shall expose these immutable/value types (C#):

| Type | Content |
|---|---|
| `TimeTag` | `uint Milliseconds`, `ushort Microseconds`, `short JulianDay` |
| `DataFileInfo` | path, signature (`uint`, numeric value as in the "uint32 LE" column of §3.1, or plugin signature), format description, `refTime[6]`, reference `TimeTag`, item count, point count, `IsDynamic`, cutoff, title, comment, serial number, test stand, test position, test type, test number, optional certification status text (streams) |
| `ParameterInfo` | map position, names[3] (primary, alternate, extended), units, descriptions[2], type id, rate (−1 unknown, 0 variable, > 0 Hz), sequence number, rate-rebuildable flag |
| `FileEvent` | name, `TimeTag` |
| `ParameterData` | source file, name/units/description (mutable copy), data type, `float[] Time` (relative seconds), typed data buffer (at least `float[]`; `double[]`/raw for other types), optional `TimeTag[]` UTC array, reference used (`RefTime` double? / `RefUtc` TimeTag?), min/max index & value, sequence number, rate, time shift applied (`vTshift`), and application-owned attribute slots (redline flag, pack position, tab position, display attributes) |

**FR-ACC-002** `ParameterData` shall support the operations the application needs from the data
library: duplicate, append (another block, no time overlap/reversal, optional "retain scale"),
reset nomenclature, find min/max (dropout-aware), find time offset (FR-SUN-051 semantics),
find UTC offset, UTC↔relative conversions (FR-TIM), average, quantize, compact. Memory is managed by
the GC; there is no `FreeParameter`/`CreateStorage` in the API (DEV-ACC-001).

**FR-ACC-003 (data-access façade)** A single `DataAccessService` shall: hold the provider list
(built-in SUN provider first, then plugins in load order); detect a file's provider (`IsSupported`:
SUN signature check first; if the file cannot be opened and the name has no `]`, report
`ERR_FILEOPEN`; otherwise ask each plugin in order; none → `ERR_UNKNOWN_FMT`); open, refresh, close
files; read parameters; return the comment block; report the last status per provider and
description/version strings (`DataAcc` version + SUN library version + each plugin's version,
newline-separated, shown in Help→About and Manage Plugins).

**FR-ACC-004 (read request)** `Read(file, name, start, stop, refTime?, refUtc?, returnType,
returnUtc)` returns `ParameterData` or a typed error (`SunError` or plugin status code). The
application always requests `float` data (plus UTC arrays when 64-bit time is enabled).

**FR-ACC-005 (refresh result)** `Refresh(file, clearEventFlag)` returns a bit set: 1 = new data,
2 = reference changed, 4 = events added, 8 = dynamic event (stream); negative = error
(FR-RT-010). Static files return 0.

**FR-ACC-006 (library options & commands)** The façade shall broadcast generic option ids
(1001–1012, FR-SUN-077) to all providers when the corresponding application setting changes:
rebuild rate (`DATARATE REBUILD`), script running (start/end of every script), dialogs allowed
(`SCRIPTPROMPT`), extrapolation (`EXTRAPOLATE`). `LIBCOMMAND [0xSIG] text` sends text to every
plugin (or only the one whose signature equals SIG) and, when SIG equals the SUN format mask, to the
SUN provider (which interprets the text as an option id).

**FR-ACC-007 (threading)** All provider calls for one file are serialized; different files may be
read in parallel. Refresh runs on a background task (FR-RT) while the UI keeps reading the last
consistent snapshot.

---

## 4.2 .NET plugin API (`Multiplot.Plugins.Abstractions`)

**FR-PLG-001** A versioned contracts assembly `Multiplot.Plugins.Abstractions` (strong-named, semantic
version, no WPF dependency) shall define:

```csharp
public interface IMultiplotPlugin            // discovered by reflection, public parameterless ctor
{
    string Name { get; }                   // short display name
    string Version { get; }                // shown in Manage Plugins / About
    string Description { get; }
    bool Initialize(IPluginHost host);     // false = do not load
    void Shutdown();                       // stop threads, release files
}

public interface IDataSourceProvider : IMultiplotPlugin
{
    uint Signature { get; }                // 4-byte format id; must not collide with SUN ids or other plugins
    string FormatDescription { get; }      // shown in file details
    int LastStatus { get; }                // status of the last operation
    bool IsSupported(string path);         // may inspect content
    IDataFile Open(string path);           // throws DataAccessException
    int ExecuteCommand(string command);    // LIBCOMMAND text ("@commands" for live sources)
    int SetOption(int optionId);           // generic options 1001..1012
}

public interface IDataFile : IDisposable   // releases the file on Dispose
{
    DataFileInfo Info { get; }
    IReadOnlyList<ParameterInfo> Parameters { get; }
    IReadOnlyList<FileEvent> Events { get; }
    string? CommentBlock { get; }          // raw comment block text
    RefreshResult Refresh(bool clearEventFlag);
    ParameterData? Read(ParameterRequest request);
}

public interface IMenuContributor          // optional; plugin menu commands 0..19
{
    IReadOnlyList<PluginCommand> Commands { get; }   // max 20, shown in the Data menu under the plugin name
}

public interface IExpressionFunctionProvider : IMultiplotPlugin   // fn() / refprop() functions
{
    IReadOnlyList<string> FunctionNames { get; }     // names usable in fn(name …) and as refprop(fn …)
    ParameterData? Invoke(string function, string options, IReadOnlyList<ParameterData> args,
                          out string? errorMessage);
}

public interface IPluginHost
{
    string UserFolder { get; }  string TempFolder { get; }
    bool ScriptRunning { get; } bool DialogsAllowed { get; }
    void RecordScriptLine(string line);    // script recording
    void Log(string message);              // script log / diagnostics
    IUserInteraction Ui { get; }           // message boxes/dialogs routed through the host
}
```
Exact member names may be refined during design; the capabilities above are mandatory.

**FR-PLG-002 (isolation)** Each plugin assembly is loaded in its own collectible
`AssemblyLoadContext` so its dependencies do not conflict with the host; an exception thrown by a
plugin call is caught, logged, shown as a status error, and does not terminate the application.
**FR-PLG-003 (signatures)** A plugin whose `Signature` equals a SUN signature or a signature already
registered is rejected with a warning at load.

## 4.3 Plugin discovery and management

**FR-PLG-010 (locations)** Plugins are loaded from (a) the `plugins` folder beside the executable and,
if *Use user plugin folder* is enabled, (b) `<UserFolder>\plugins`. Every `*.dll` that contains a
public non-abstract type implementing `IMultiplotPlugin` is a candidate. A plugin that has its own dependencies is
deployed in a sub-folder named after it, and the candidate is `plugins\<Name>\<Name>.dll`. Its `.deps.json` and
dependency assemblies resolve inside that plugin's load context. The blocking name is the file name `<Name>.dll`.

**FR-PLG-011 (blocking)** File names listed as value names under
`HKCU\SOFTWARE\Multiplot\Plugins\Blocked` (case-insensitive) are not loaded. `DISABLE PLUGIN name` adds
a value (REG_BINARY 1) and reloads plugins; `ENABLE PLUGIN name` deletes it and reloads. The user-folder
switch is stored as the default DWORD value of `HKCU\SOFTWARE\Multiplot\Plugins\UserFolder`.
`/noplugins` on the command line loads no plugins (until Manage Plugins → OK).

**FR-PLG-012 (Manage Plugins dialog)** Lists: the built-in data-access core and SUN library
(Enabled = `Always`), each loaded plugin (`Yes`), each expression-function provider (`Loaded`), and
each blocked plugin (`No`), with columns *Filename*, *Version* (header click toggles to *Description*),
*ID* (`%08x` signature, `n/a` for function providers), *Enabled*. Double-click toggles Yes/No for
data plugins. Colours: No = red (204,0,0), Yes = green (0,120,0), Loaded = blue (0,120,200).
OK rewrites the Blocked key, stores the user-folder flag and reloads all plugins (shutdown → load →
rebuild Data menu and user-button menu labels).

**FR-PLG-013 (menu)** Plugin commands are inserted into the **Data** menu, before its last item,
followed by a separator; command ids are allocated in blocks of 20 per plugin in load order.

## 4.4 Plugins to be provided with the product

**FR-PLG-030** A **sample plugin** project (`samples/MinimalPlugin`, reading `demo_data.min`) shall be
delivered with the SDK as documentation and as an integration test of the API.
**FR-PLG-031** A **live-stream plugin** for PC GOAL streams (signature
`PCGOAL_REALTIME = 0xFFF0FFFF`, handling `.flt` filter files, certification status, stream events
`STREAMnnREF`, stream "EOF" UTC) is required for the in-scope real-time stream feature; its protocol
specification is **not available** — see OQ-001.
**FR-PLG-032** An ElvAcc live plugin (signatures `0x4C41424C` / `0x4C42414C`, the ASCII tags "LBAL"/"LABL") is optional — see OQ-001.
**FR-PLG-033** An **Apache Parquet plugin** (`src/Multiplot.Parquet`, signature `0x31524150`, the magic
"PAR1") ships with the application in `plugins\Multiplot.Parquet\`. It reads static (non-real-time) archives:
the `multiplot.timeseries.wide/1` convention written by `tools/MultiToParquet`
(see `docs/parquet-archive-format.md`), and generic Parquet files whose time column is INT64 `time_ticks`, a
TIMESTAMP, or relative seconds. Every other numeric top-level column is a parameter. A null cell is "no sample"
and NaN is a dropout. Reads follow the MULTITIME rules: lead-in and trailing points with extrapolation, clamping
to the window, and the EOF hold point. UTC tags are built directly from the absolute times.

**FR-PLG-040** Expression functions `fn(...)` and `refprop(...)` (and its deprecated aliases) are
served by `IExpressionFunctionProvider` plugins; if no provider implements the requested name the
error text is `Externally defined fn() not available (DLL not loaded)` /
`RefProp() function not available (DLL not loaded)`. `polyfit` is built in
(OQ-006).
