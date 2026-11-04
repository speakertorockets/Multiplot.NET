# Plugins

Plugins are .NET assemblies (`*.dll`) placed in the `plugins` folder next to `Multiplot.exe` and, if
*Load plugins from user folder* is on, in `%USERPROFILE%\My Multiplot\plugins`. When the program
starts, it loads every public, non-abstract class that implements `IMultiplotPlugin` and has a
parameterless constructor. Each plugin assembly gets its own load context, so plugins can be
unloaded and reloaded from the *Manage Plug-ins* dialog.

Native plugin DLLs cannot be loaded. Plugins are written against the .NET interfaces described
below.

## Managing plugins

*Options → Manage Plug-ins* lists the built-in SUN library and every plugin, with its version,
signature and description. Double-click a plugin to enable or disable it. Disabled file names are
stored in the registry under `Plugins\Blocked`. Scripts can do the same:

```
DISABLE PLUGIN MinimalPlugin.dll
ENABLE PLUGIN MinimalPlugin.dll
```

To start without any plugins, use `Multiplot.exe /noplugins`.

## Writing a data-source plugin

Reference `Multiplot.Plugins.Abstractions` with `Private="false"`, so the host's copy of the contracts
assembly is used. Then implement these interfaces:

| Interface | Purpose |
|---|---|
| `IDataSourceProvider` | `Signature` must be a unique 4-byte id and must not clash with the SUN ids or with other plugins. Also implement `IsSupported(path)`, `Open(path)`, `ExecuteCommand(text)` (for `LIBCOMMAND`) and `SetOption(1001..1012)` |
| `IDataFile` | `Info` (header), `Parameters` (map), `Events`, `CommentBlock`, `Read(ParameterRequest)`, `FindParameter`, `Refresh` |
| `IMenuContributor` (optional) | Up to 20 commands to add to the **Data** menu |
| `IExpressionFunctionProvider` (optional) | Functions used as `fn(name …)` or `refprop(…)` in expressions |

Plugins talk to the program through `IPluginHost`. It gives them the user and temp folders, a
script-recording method, a log, and `Ui` for message boxes; in hidden mode these take their default
answer.

`Read` returns a `ParameterData` holding these values:

* `Time`: relative seconds as `float[]`;
* `Data`: a typed array;
* optional `Gmt` time tags;
* minimum and maximum, with their indexes;
* `Rate`.

Report errors by throwing `DataAccessException`.

### Sample

`Multiplot.NET\samples\MinimalPlugin` is a minimal data-source template. It accepts any existing `.min` file and creates three 32-point parameters: `UPDATA`,
`DOWNDATA` and `UPDOWN`. To try it:

```
dotnet build Multiplot.NET\samples\MinimalPlugin
copy Multiplot.NET\samples\MinimalPlugin\bin\Debug\net10.0\MinimalPlugin.dll <Multiplot folder>\plugins\
```

Then open `demo_data.min` and plot `UPDATA,DOWNDATA`. The test suite runs these same steps in
`SamplePluginTests`.
