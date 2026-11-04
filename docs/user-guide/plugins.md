# Plugins

Plugins are .NET assemblies (`*.dll`) placed in the `plugins` folder next to `Multiplot.exe` and, if
*Load plugins from user folder* is on, in `%USERPROFILE%\My Multiplot\plugins`. When the program
starts, it loads every public, non-abstract class that implements `IMultiplotPlugin` and has a
parameterless constructor. Each plugin assembly gets its own load context, so plugins can be
unloaded and reloaded from the *Manage Plug-ins* dialog.

A plugin that needs its own libraries goes in a sub-folder with the same name as the plugin. Its main DLL is
`plugins\<Name>\<Name>.dll`, with its dependencies next to it.

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

## Apache Parquet files

The bundled plugin `plugins\Multiplot.Parquet\Multiplot.Parquet.dll` opens `.parquet` files the same way as
SUN files: with *File → Open*, in groups, and in scripts (`open data.parquet`). Parquet files are archives, so
they never update in real time.

* Files written by `multi2parquet` (`tools/MultiToParquet`) keep everything from the SUN source: header
  fields, parameter names, alternate names, units, descriptions, rates, events (`EOF`, `FILE_REF`, ...)
  and the comment block.
* Other Parquet files work as well when they have a time column. The time column can be INT64
  `time_ticks`, any TIMESTAMP column (taken as UTC), or a DOUBLE/FLOAT column named `time` holding seconds.
  Every other numeric column becomes a parameter, named after the column. For a tick or TIMESTAMP column,
  relative time 0 is the first sample. For a seconds column, 0 is the column's own zero, and the clock time
  comes from the `multiplot.reference.iso` (or `multiplot.reference.ticks`) metadata. Without that metadata
  the clock time starts at 2000-01-01 00:00.
* An empty (null) cell means "no sample". NaN is shown as a dropout.

To convert existing files, run `multi2parquet convert file.multi`. See `docs/parquet-archive-format.md` for the format.

## Writing a data-source plugin

Reference `Multiplot.Plugins.Abstractions` with `Private="false"`, so the host's copy of the contracts
assembly is used. If the plugin uses NuGet packages, set `<EnableDynamicLoading>true</EnableDynamicLoading>` and
deploy its whole output folder as `plugins\<Name>\`. `src/Multiplot.Parquet` is an example. Then implement these interfaces:

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
