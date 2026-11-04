# Getting started

## Installing

Multiplot.NET is a folder that you copy anywhere. It needs the .NET 10 Desktop Runtime (x64).

```
Multiplot.exe                 the application
Multiplot.ini                 default settings (copied to your user folder on first start)
ColorPrinter.wps            colour table used by SET COLOR PRINTERTABLE
plugins\                    data-source plugins
  Multiplot.Parquet\         bundled Apache Parquet plugin (Multiplot.Parquet.dll and its libraries)
```

The first time Multiplot.NET starts, it creates the user folder `%USERPROFILE%\My Multiplot`. It copies
`Multiplot.ini` (and `Multiplot.favorites`, if there is one) into that folder. Settings are then read
from and saved to the copy in the user folder. See [Settings and files](settings.md).

To build from source, run `dotnet build Multiplot.NET\Multiplot.NET.slnx -c Release`. The program is
written to `Multiplot.NET\src\Multiplot.App\bin\Release\net10.0-windows\Multiplot.exe`.

## The main window

* **Menu bar**: File, Edit, Script, Parameter, TimeSlice, View, Axis, Style, Nomenclature, Markers,
  Options, User, Data, Window and Help. Some commands are not supported (see
  [Limitations](limitations.md)).
* **Toolbars**: 19 toolbar bands. You can show or hide them with *Options → Toolbars*, and the
  layout is saved in the registry.
* **Document area**: holds the plot windows. Each plot window has its own caption bar with
  minimise, maximise and close buttons. You can drag a window by its caption and resize it from its
  edges. *Window → Tile / Cascade* arranges the windows.
* **Status bar**: shows menu prompts, cursor coordinates, delta and track read-outs, and the CPU and
  memory load (*SYSTEMLOAD ON*).

Hover over a toolbar button or menu item to see its prompt in the status bar.

## Opening data

You can open data in any of these ways:

* *File → Open* (Ctrl+O) opens one or more data files.
* Drag files onto the window.
* Pass them on the command line: `Multiplot.exe data.sun` or `Multiplot.exe archive.parquet`.
* Use the script command `OPEN file`.

You can open these kinds of file:

* **Data files**: SUN files (all SUN formats, including compressed and dynamic files), Apache
  Parquet files (`.parquet`, read by the bundled plugin; see [Plugins](plugins.md)), plus any other
  format that a loaded plugin supports. *File → Open* has a *Parquet Files* filter.
* **Group files**: lists of data files that are loaded as one group.
* **Filter files** (`.flt`).
* **CSV/ASCII files**: these go through the [ASCII import](import-export.md).

The files you open stay open until *File → Close All* or `CLEAR FILES`. Every plot reads from the
set of open files. The files are organised in **groups**. When a group holds several files, each
list item produces one curve per file.

## Making a plot

1. Open a file.
2. Choose *Parameter → Select* (or press the plot button). The parameter-selection dialog lists
   the parameters of every open file. Select one or more of them, then choose **Plot**.
3. A new plot window appears. Each command that creates a plot opens a **new** window with the
   current settings.

The selected names make up the **Y parameter list**, for example `PC1,TEMP`. You can also type the
list directly. A list can contain expressions such as `[PC1]*2` and functions such as `avg(PC1 0.1)`.
See [Parameter lists and expressions](expressions.md).

To limit the time range, use *TimeSlice → Set Time Slice* or `TIME start,stop[,reference]`. Times
are in seconds relative to the reference. A reference can be a GMT (`ddd/hh:mm:ss.mmm`) or the name
of an event in the file's `[events]` section.

## Next steps

* Zoom, pan and read values with the mouse: see [Plot windows](plot-windows.md).
* Record what you do as a script: use *Script → Record*, then replay it with *Script → Run*. See
  [Scripts](scripts.md).
* Save the plot as a picture or as CSV: see [Import and export](import-export.md).
