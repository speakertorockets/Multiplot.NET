# Getting started

## Installing

Multiplot.NET is a folder that you copy anywhere. It needs the .NET 10 Desktop Runtime (x64).

```
Multiplot.exe                 the application
Multiplot.ini                 default settings (copied to your user folder on first start)
ColorPrinter.wps            colour table used by SET COLOR PRINTERTABLE
docs\user-guide\            this user guide (Help → User Guide)
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
* **No toolbars**: every command is in the menus or in the right-click menus of the plot windows,
  which leaves the most room for plots.
* **Document area**: holds the plot windows. Each plot window has its own caption bar with
  minimise, maximise and close buttons. You can drag a window by its caption and resize it from its
  edges. *Window → Tile / Cascade* arranges the windows.
* **Status bar**: shows menu prompts, cursor coordinates, delta and track read-outs, and the CPU and
  memory load (*SYSTEMLOAD ON*).

Hover over a menu item to see its prompt in the status bar. *Help → User Guide* opens this guide in
your web browser.

## Opening data

You can open data in any of these ways:

* *File → Open* (Ctrl+O) closes the files that are open and opens one or more data files.
* *File → Add* opens more files and keeps the ones that are already open.
* *File → Recent Files* reopens a file you used before.
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

1. Open a file. When you open a file from the menu, from *Recent Files* or by dragging it onto the
   window, and no plot window is open yet, the parameter-selection dialog opens by itself. (It
   doesn't open while a script runs.)
2. Otherwise choose *Parameter → Selection* (or press Enter). The parameter-selection dialog lists
   the parameters of every open file. Select one or more of them, then choose **Plot**.
3. A new plot window appears. Each command that creates a plot opens a **new** window with the
   current settings.

The selected names make up the **Y parameter list**, for example `PC1,TEMP`. You can also type the
list directly. A list can contain expressions such as `[PC1]*2` and functions such as `avg(PC1 0.1)`.
See [Parameter lists and expressions](expressions.md).

When several files are open, a name is loaded from **every** file that has it. To take it from one
file only, pick that file in the dialog's **File** box: the names are then written as file-qualified
names, for example `static_run.sun:PC1`, and only that file is searched. This also works for *Add
Parameter*, so you can add a recorded parameter next to the live parameter of the same name. With
**All Files** selected the names stay unqualified. If a file's name can't be told apart from the
other open files, the dialog says so; type a qualifier yourself (any part of the path) or choose All
Files.

To limit the time range, use *TimeSlice → Set Time Slice* or `TIME start,stop[,reference]`. Times
are in seconds relative to the reference. A reference can be a GMT (`ddd/hh:mm:ss.mmm`) or the name
of an event in the file's `[events]` section.

## Next steps

* Zoom, pan and read values with the mouse: see [Plot windows](plot-windows.md).
* Record what you do as a script: use *Script → Record*, then replay it with *Script → Run*. See
  [Scripts](scripts.md).
* Save the plot as a picture or as CSV: see [Import and export](import-export.md).
* Show when events happened, or draw limit lines on the value axis: see
  [Event and redline markers](markers.md).
