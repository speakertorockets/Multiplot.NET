# Limitations and notes

## Not supported

| Feature | Notes |
|---|---|
| Printing and print preview | Save a picture and print it from another program. `PRINT*` and `LANDSCAPE` commands are accepted, and their values are kept in the INI |
| EMF / metafile output | `SAVEPIC EMF` and `COPYMETA` save a PNG instead |
| TIFF pictures | Use PNG |
| Mailslot remote control (`/comm/`, `EXTCMDPOLLING`) | Accepted and ignored |
| Query tool, `MCI`, `PLAYSOUND` | Commands accepted; `MCI_RETURN` is empty |
| HTML Help | Use this guide |
| PC GOAL streams, REFPROP, polynomial fitting, external function DLLs | Streams and REFPROP need .NET plugins. `polyfit()` produces no curve. `fn()` is served by `IExpressionFunctionProvider` plugins |
| Native C plugin DLLs | Write a .NET plugin instead ([Plugins](plugins.md)) |

## Parquet files

* Parquet files are archives. They are never dynamic, so auto refresh does not change them.
* The *rebuild data rate* option does not apply to Parquet data.
* Only numeric top-level columns become parameters. Strings, binary, list and nested columns are ignored.
* `multi2parquet` does not export `v1` (variable blob) parameters or parameters whose data type is a time
  tag (`t8` as data). It writes them to the `multiplot.skipped` metadata instead.
* 64-bit integer values above 2^53 are rounded, because the archive stores values as DOUBLE.
* Packed time stamps (`p4`) that were written across a year end decode to days after 366. The SUN
  reader decodes them the same way. `multi2parquet` exports them as decoded and prints a warning.
* Compared with the same data read from the `.multi` file, a Parquet read differs in four ways:
  * GMT is correct on day 366 of a leap year.
  * The EOF hold point is also added to `f4`-indexed parameters.
  * Min/max are computed from the points returned.
  * GMT of a window end point can differ by 1 µs.

## Behaviour notes

* WPD display files: the colour index and decimal (`DECn`) formats of tabular items are applied.
* `IMPORT ... /R ref` honours the reference time.
* An empty boolean value in the INI counts as true.
* Symbol substitution stops after 1000 replacements, so a symbol that refers to itself cannot
  hang the program.
* Hidden mode (`/hide`) answers every dialog with its default and logs messages instead of
  showing them.
* The Toolbars dialog shows and hides bands and resets them to their defaults. You cannot rearrange
  individual buttons in a band.
* The Time Alignment tool sets a time shift for each curve.
* Fonts that are not installed (such as the default "Multiplot" face) are replaced with Arial.
* SUN files: GMT time tags of `d8`-indexed MULTITIME data on day 366 of a leap year are shown one day
  late. `TimeMath.Gmt` wraps day 366 to day 1; see OQ-017.
