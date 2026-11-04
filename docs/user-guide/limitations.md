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
