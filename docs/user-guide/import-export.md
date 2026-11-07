# Import and export

## ASCII import

*File → Import* (or `IMPORT`, or opening a `.csv` file) converts delimited text into a SUN file and
opens it. The import dialog shows a preview and guesses three things:

* the delimiter: comma, tab, semicolon or space;
* the title record, which gives the parameter names;
* the index (time) column and its format: seconds, clock `hh:mm:ss.mmm`, `ddd/hh:mm:ss`, epoch, or
  sample number.

Script options:

| Option | Meaning |
|---|---|
| `/In` | Index (time) column, starting at 1 |
| `/Sn` | Number of lines to skip |
| `/Tn` | Line that holds the titles |
| `/G` | Create GMT time tags |
| `/F` | Write the standard COMMONTIME format instead of MULTITIME64 |
| `/R ref` | Reference time |

`IMPORT CLIPBOARD` imports text that is on the clipboard.

## Export

* `EXPORT [file]` (*File → Export*) writes the active plot as comma-separated text. The first lines
  are the header: file, names, units and descriptions. Each curve has a time column and a value
  column, unless the curves share a time base. The default file name is `plotNNNN.csv` in the
  export folder.
* `EXPORT CLIPBOARD` copies the same text to the clipboard.
* `SAVESUN [file]` writes the plotted data to a SUN MULTITIME file.

## Converting `.multi` files to Parquet

`multi2parquet` (`tools/MultiToParquet`) converts SUN_MULTITIME and SUN_MULTITIME64 files, including the
`_SRT` variants, into compressed Apache Parquet archives. Multiplot.NET opens the result directly, and so
can other tools such as pandas, pyarrow, DuckDB and Spark. The archive has one INT64 column of .NET ticks
(UTC) and one nullable DOUBLE column per parameter. The header, parameter map, events and comment block go
into the file metadata. See `docs/parquet-archive-format.md`.

```
multi2parquet convert test.multi                  # writes test.parquet
multi2parquet convert test.multi out.parquet --level fastest
multi2parquet info test.multi                     # header, events, blocks and points per parameter
multi2parquet verify test.multi test.parquet      # compares every sample bit for bit
```

Use `--year N` when the file's header year is missing or wrong. Convert a file only after it has been
closed. A file with signature `00 00 00 00` is still being written, and the converter cannot open it.

## Pictures

`SAVEPIC BMP|JPEG|GIF|PNG [file|ALL|CLIPBOARD]` (*File → Save Picture*) draws the plot at the size
of its window, or at 1024×768 when running hidden, and saves it. `ALL` saves every plot with a
default name. *Edit → Copy* puts a bitmap on the clipboard.

## Statistics

`DATASTATS [file]` writes the statistics of every curve over the visible X range: minimum, maximum,
average, range, sum, number of points, sigma, coefficient of variation and TI. With no file name it
opens the **Slice Average** dialog, which can also be opened with the right button (Ctrl+drag) for
a chosen range. In the dialog you can change the precision, add a row with the column averages, and
save the table as CSV or text.

## Details files

| Command | Content |
|---|---|
| `SAVEPARMLIST n,file` | For file n (0 = all): the path, the item count, then `name1,name2,units,description,seqNum` for each parameter |
| `SAVEPARMDETAILS n,file` | Details of curve n of the active plot (0 = the list of curves) |
| `SAVEFILEDETAILS n,file` | Header and comment block of file n (0 = the list of files) |

The *Parameter → Parameter Properties* and *File → Properties* dialogs show the same text and can
copy or save it. *Parameter Properties* is also in the plot right-click menus (*Properties*).
