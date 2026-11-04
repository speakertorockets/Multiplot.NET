# Multiplot.NET

Multiplot.NET is a Windows desktop application for exploring and plotting test data. It is a modern
.NET 10/WPF implementation of Multiplot, with support for SUN data files, Apache Parquet files,
automation scripts, and data-source plugins.

## Features

- Plot one or more parameters from SUN and Apache Parquet data files.
- Import CSV/ASCII data and export plots and data in several formats.
- Select time slices, use expressions and functions, and inspect values interactively.
- Automate workflows with `.wps` scripts, including headless report generation.
- Extend file-format support with .NET data-source plugins.
- Convert SUN `.multi` files to compressed Apache Parquet with `multi2parquet`.
- Support real-time operation for data sources that provide refreshable files.

## Requirements

- Windows x64
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)

The application targets `net10.0-windows` and uses WPF. Native plugin DLLs are not supported; plugins
must be managed .NET assemblies.

## Getting started

### Download or build

Multiplot.NET is a folder-based application. After building or publishing it, the application and its
`plugins` directory can be copied to any suitable location.

Build the complete solution in Release configuration:

```powershell
dotnet build Multiplot.NET.slnx -c Release
```

The executable is produced at:

```text
src\Multiplot.App\bin\Release\net10.0-windows\Multiplot.exe
```

Run the application with a data file:

```powershell
src\Multiplot.App\bin\Release\net10.0-windows\Multiplot.exe data.sun
src\Multiplot.App\bin\Release\net10.0-windows\Multiplot.exe archive.parquet
```

You can also open files from **File → Open**, drag them onto the application window, or run a script:

```powershell
Multiplot.exe report.wps
```

On first start, user settings are initialized in `%USERPROFILE%\My Multiplot`.

### First plot

1. Open a SUN or Parquet file.
2. Select **Parameter → Select**.
3. Choose one or more parameters and select **Plot**.
4. Use the plot window to zoom, pan, inspect values, and change the time slice.

## Scripting

`.wps` files contain one command per line and can be run from the UI, the command line, or another
script. For example:

```text
open C:\data\run12.sun
time IGNITION-5,IGNITION+30,IGNITION
plot PC1,PC2,[PC1]-[PC2]"Delta P"
savepic PNG C:\reports\run12.png
export C:\reports\run12.csv
exit
```

For unattended operation, use hidden mode and disable settings persistence when appropriate:

```powershell
Multiplot.exe /hide /nosave report.wps
```

Other useful command-line switches include `/noplugins` and `/default`. See the [script reference](docs/user-guide/scripts.md)
and [settings and files](docs/user-guide/settings.md) documentation for details.

## Apache Parquet support

The application ships with the `Multiplot.Parquet` plugin. It opens `.parquet` files through the same
file-open and scripting workflows as SUN files. The bundled plugin is deployed under:

```text
plugins\Multiplot.Parquet\
```

To convert a SUN `.multi` file, build the conversion tool and run it from the repository root:

```powershell
dotnet build tools/MultiToParquet -c Release
dotnet run --project tools/MultiToParquet -- convert input.multi output.parquet
```

The tool also provides `info`, `verify`, `crosscheck`, and `synth` commands. See the
[MultiToParquet README](tools/MultiToParquet/README.md) for all options and the
[Parquet archive format](docs/parquet-archive-format.md) specification.

## Plugins

Plugins are .NET assemblies placed in the application's `plugins` directory. A plugin can provide a
data-source provider and optionally contribute data-menu commands or expression functions. The
[MinimalPlugin sample](samples/MinimalPlugin) demonstrates the plugin contract and deployment model.

To build the sample plugin:

```powershell
dotnet build samples/MinimalPlugin -c Debug
```

Copy the resulting `MinimalPlugin.dll` to the application's `plugins` directory, then open a `.min`
file and plot `UPDATA`, `DOWNDATA`, or `UPDOWN`.

## Development

Build the solution:

```powershell
dotnet build Multiplot.NET.slnx
```

Run the test suite:

```powershell
dotnet test Multiplot.NET.slnx
```

The repository is organized into application, domain, data access, scripting, expressions, rendering,
import/export, settings, plugin contracts, and plugin implementation projects. The `tests` directory
contains the xUnit test project.

## Documentation

- [User guide](docs/user-guide/README.md)
- [Getting started](docs/user-guide/getting-started.md)
- [Plot windows](docs/user-guide/plot-windows.md)
- [Expressions](docs/user-guide/expressions.md)
- [Scripts](docs/user-guide/scripts.md)
- [Import and export](docs/user-guide/import-export.md)
- [Plugins](docs/user-guide/plugins.md)
- [Limitations](docs/user-guide/limitations.md)
- [Requirements and design notes](docs/requirements/README.md)
- [Parquet archive format](docs/parquet-archive-format.md)

## License

No license file is currently included in this repository. Add or consult the repository's licensing
terms before redistributing the software.
