# Settings and files

## Folders

| Location | Contents |
|---|---|
| Program folder (`%MULTIPLOTPATH%`) | `Multiplot.exe`, the default `Multiplot.ini`, `ColorPrinter.wps`, and `plugins\` |
| `%USERPROFILE%\My Multiplot` | Your `Multiplot.ini` and `Multiplot.favorites`. Its `plugins\` folder is used when *Load plugins from user folder* is on |
| Export folder (`EXPORTPATH`) | Default location for exports, pictures and statistics |
| `%LOCALAPPDATA%\Multiplot\logs` | Diagnostic log, and the messages from hidden mode |

## Multiplot.ini

Multiplot.NET reads and writes its settings in an INI file with fixed sections, keys and order.
These commands work with the INI:

* *Options → Save Settings* or `SAVESETTINGS [file]` writes the current settings.
* `LOADSETTINGS [file]` reads them back.
* Settings are saved automatically on exit unless auto-save is off or `/nosave` was given.

An empty boolean value such as `Symbols=` counts as **true**.

## Registry

Multiplot.NET keeps the following under `HKEY_CURRENT_USER\Software\Multiplot`:

* recent files and recently used parameter lists
* the favourites file
* window position
* toolbar layout and band order
* user buttons
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
`HKEY_CURRENT_USER\Software\WinPlot` (recent files, recent parameter lists, toolbars, user buttons
and plugin settings). WinPlot's own files and registry key are not changed. Blocked plugins and the
window position take effect the next time Multiplot.NET starts.

## Favourites

*Parameter → Favorites* lists saved parameter sets from the favourites file. Each line looks like
`name=Label yaxis=list xaxis=parm`. Pick one to plot it; you can use its label as the plot title.
`FAVORITES file` chooses a different file.
