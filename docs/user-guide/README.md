# Multiplot.NET User Guide

Multiplot.NET is a .NET 10 / WPF application. It plots test data stored in SUN files, in Apache Parquet
files (through the bundled Parquet plugin) and in any other format a data-source plugin adds. It can
be driven by scripts. Its settings live in `Multiplot.ini`; it also uses `.wps` scripts, `.wpd` display files, `.markers` files and favourites
files.

| Chapter | Contents |
|---|---|
| [Getting started](getting-started.md) | Installing, the main window, opening files, making a first plot |
| [Plot windows](plot-windows.md) | Plot styles, mouse and keyboard, context menus, markers, tabular displays |
| [Parameter lists and expressions](expressions.md) | Parameter lists, expressions, operators and functions |
| [Scripts](scripts.md) | The `.wps` language, symbols, recording, and a command reference |
| [Import and export](import-export.md) | ASCII import, CSV/SUN export, converting `.multi` files to Parquet, pictures, statistics, details files |
| [Real-time operation](realtime.md) | Auto refresh, compaction, discard, AutoRun |
| [Settings and files](settings.md) | User folder, `Multiplot.ini`, registry, command line |
| [Plugins](plugins.md) | Managing plugins, the bundled Parquet plugin, writing a data-source plugin |
| [Limitations and notes](limitations.md) | Unsupported features and behaviour notes |

The Parquet archive layout is described in `docs/parquet-archive-format.md`. The detailed
specification behind this guide is in `docs/requirements`. Each chapter there gives the exact rules (for example, the parsing rules for every script command).
