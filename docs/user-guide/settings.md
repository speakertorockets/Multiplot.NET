# Settings and files

## Folders

| Location | Contents |
|---|---|
| Program folder (`%MULTIPLOTPATH%`) | `Multiplot.exe`, the default `Multiplot.ini`, `ColorPrinter.wps`, and `plugins\` |
| `%USERPROFILE%\My Multiplot` | Your `Multiplot.ini` and `Multiplot.favorites`. Its `plugins\` folder is used when *Load plugins from user folder* is on |
| Export folder (`EXPORTPATH`) | Default location for exports, pictures and statistics |
| `%LOCALAPPDATA%\Multiplot\logs` | Diagnostic log, and the messages from hidden mode |

### File paths

*Options → File Paths* sets where Multiplot.NET looks for files and where it writes them:

| Path | Script command | Used for |
|---|---|---|
| Data file search path | `FILEPATH` | Data files given without a folder |
| DB_DIR search path | `DBPATH` | Database (`DB_DIR`) files |
| Script search path | `SCRIPTPATH` | Scripts (`.wps`) given without a folder; the first folder is also where the script and marker file dialogs start |
| WPD search path | `WPDPATH` | Display files (`.wpd`) and their background bitmaps |
| Export folder | `EXPORTPATH` | Exports, pictures and statistics |

The four search paths are lists of folders separated by `;` (for example `D:\Tests;D:\Archive`). Each
path has a **Browse...** button that opens the Windows folder selection dialog:

* For a search path, you can select one or more folders; they are **added** to the end of the list
  (folders already in the list are not added twice). To remove a folder or change the order, edit the
  text.
* For the export folder, the folder you select **replaces** the current one.

The dialog starts in the first existing folder of the path. The paths are saved in `Multiplot.ini` by
*Save Settings*.

## Multiplot.ini

Multiplot.NET reads and writes its settings in an INI file with fixed sections, keys and order.
These commands work with the INI:

* *Options → Save Settings* or `SAVESETTINGS [file]` writes the current settings.
* `LOADSETTINGS [file]` reads them back.
* Settings are saved automatically on exit unless auto-save is off or `/nosave` was given.

An empty boolean value such as `Symbols=` counts as **true**.

`Appearance = Light|Dark` in `[interface]` stores the light or dark appearance (*View → Appearance*).
`Colors` in `[plot style]` stores the colour table: 100 curve colours, then grid, label, background,
monochrome, title, event marker and redline colours. The shipped `Multiplot.ini` has the default
palette (see [Colors and appearance](plot-windows.md#colors-and-appearance)).

## Registry

Multiplot.NET keeps the following under `HKEY_CURRENT_USER\Software\Multiplot`:

* recent files and recently used parameter lists
* the favourites file
* window position
* user buttons (kept under the `Toolbars` key for compatibility)
* blocked plugins (`Plugins\Blocked`) and the user-folder plugin switch (`Plugins\UserFolder`)
* time-slice history

For testing, or to run two configurations side by side, set the environment variable
`MULTIPLOTNET_REGISTRY_ROOT` to another key, for example `Software\MultiplotTest`.

## Command line

```
Multiplot.exe [/noplugins] [/default] [/hide] [/nosave] [file ...]
```

| Switch | Effect |
|---|---|
| `/noplugins` | Do not load plugins |
| `/default` | Start from the INI in the program folder, but still save to the user INI |
| `/hide` | Run hidden. Dialogs take their defaults and messages are logged (see [Scripts](scripts.md)) |
| `/nosave` | Do not save settings or the window position on exit |
| `/comm/name` | Accepted and ignored |

The remaining arguments are processed in order, according to their extension:

* `.wps`: run the script;
* `.ini`: load the settings;
* `.csv`: import;
* anything else: open as a data file or group.

## Importing WinPlot settings

If you used WinPlot before, Multiplot.NET can take over its settings:

* The first time Multiplot.NET starts, it looks for `%USERPROFILE%\My WinPlot\WinPlot.ini` and, if
  the file exists, asks whether to import it.
* At any time, *Options → Import WinPlot Settings...* lets you pick a WinPlot INI file and import it.
  This replaces the current Multiplot.NET settings.

The import takes the INI settings, the WinPlot favourites file, and the registry settings under
`HKEY_CURRENT_USER\Software\WinPlot` (recent files, recent parameter lists, user buttons
and plugin settings). WinPlot's own files and registry key are not changed. Blocked plugins and the
window position take effect the next time Multiplot.NET starts.

## Favourites

*Parameter → Favorites* lists saved parameter sets from the favourites file. Each line looks like
`name=Label yaxis=list xaxis=parm`. Pick one to plot it; you can use its label as the plot title.
`FAVORITES file` chooses a different file.

## Operations telemetry

On operations workstations, Multiplot.NET can report what it is doing to operations staff while
operations are running. It uses [OpenTelemetry](https://opentelemetry.io/): every Multiplot window on
the workstation sends traces, metrics and log lines to one OpenTelemetry Collector on the same
machine. The collector stores the data and can forward it to the monitoring system.

Telemetry is **off by default**. Two things must both be true when Multiplot.NET starts:

1. The environment variable `MULTIPLOT_TELEMETRY` is set to `1`.
2. The OpenTelemetry Collector is installed and running on the workstation.

If either is missing, Multiplot.NET runs exactly as before and makes no network connection. Normal
desktop workstations used for technical reviews after operations do not need the collector.

### Installing the collector

The production tools install the collector with administrator rights. The collector, its
configuration and the scripts are in the `tools\OtelCollector` folder of the Multiplot.NET source:

```
powershell -ExecutionPolicy Bypass -File Install-MultiplotCollector.ps1 -EnableTelemetry
```

This installs the `MultiplotOtelCollector` Windows service, which listens on `127.0.0.1:4318`. With
`-EnableTelemetry` it also sets `MULTIPLOT_TELEMETRY=1` for all users. To remove everything, run
`Uninstall-MultiplotCollector.ps1`. `tools\OtelCollector\README.md` describes the configuration
(`C:\ProgramData\Multiplot\otel\multiplot-collector.yaml`) and how to forward data to a monitoring
backend.

Multiplot.NET checks the variable and the collector only when it starts. A Multiplot window that was
already open when telemetry was enabled must be restarted before it sends anything. If the collector
stops while Multiplot.NET is running, Multiplot.NET keeps working, but the telemetry from that period
is lost.

### Environment variables

| Variable | Effect |
|---|---|
| `MULTIPLOT_TELEMETRY` | `1` (or `true`, `on`, `yes`) turns telemetry on. Not set means off |
| `MULTIPLOT_OTLP_ENDPOINT` | Collector address, default `http://127.0.0.1:4318`. Only `http` addresses on the local machine are accepted |
| `MULTIPLOT_OTEL_METRIC_INTERVAL_MS` | How often metrics are sent, in milliseconds (default 15000, minimum 1000) |
| `OTEL_SDK_DISABLED` | `true` turns telemetry off even when `MULTIPLOT_TELEMETRY` is set |

At start-up, the diagnostic log (`%LOCALAPPDATA%\Multiplot\logs`) shows whether telemetry is on. If it
is not on, the log line says why, for example that no collector is listening.

### What is reported

* **Traces**: one entry for each script run and each command. Each command entry has the full command
  text. There are also entries for opening data files (with the file path), loading parameters (with
  the parameter list and the number of points), exports, imports and real-time refresh cycles. A
  command that fails is marked as an error, together with its message.
* **Metrics**: counts of commands, errors, file opens (with the file path) and loaded points; the
  parameters added to plots (with the parameter name, its file, and whether it started a new plot or was
  added with *Add Parameter*); load, draw and refresh times;
  .NET runtime figures (memory, garbage collection, threads).
* **Logs**: every line written to the diagnostic log.
* **Identity**: each Multiplot process has its own instance id. The process id, computer name, user
  name, the command line and whether it runs hidden (`/hide`) are also sent, so the reports from
  different Multiplot windows on one workstation can be told apart.

File names, parameter names, command text and script paths are sent as they are. Telemetry is meant
for protected operations workstations whose data is watched by privileged production staff.
