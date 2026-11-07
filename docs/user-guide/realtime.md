# Real-time operation

Dynamic SUN files, and plugin files that report themselves as dynamic, grow while a test is running.
Multiplot.NET can follow them. Parquet files are always static. Convert a `.multi` file to Parquet
with `multi2parquet` only after the test has finished and the file is closed.

| Command / menu | Effect |
|---|---|
| *View → Auto Refresh*, `REFRESH seconds` | Re-read the open files every n seconds and update the plots |
| `REFRESH` (no number), *Data → Real-Time → Refresh* | Refresh once now |
| `REFRESH OFF` | Stop auto refresh |
| `AUTOSCALE X OFF` | Keep the visible time span and slide it along as new data arrives (`AUTOSCALE X ON`: show all data) |
| `AUTOSCALE Y OFF` | Keep the value scale while new data arrives (`AUTOSCALE Y ON`: fit the visible data) |
| `FREEZE ON` | Stop updating the active plot. The data still keeps being collected |
| `AUTOCOMPACT points[,interval]` | When a curve has more than *points* points, keep only the minimum and maximum in each interval |
| `AUTODISCARD seconds` | Throw away data older than the given window |
| `IGNORESTATIC ON` | Do not re-read files that are not dynamic |

During a refresh, new points are appended to each curve. The *Auto Scale X* and *Auto Scale Y*
switches decide what the scales do (see [Scales and autoscaling](plot-windows.md#scales-and-autoscaling)).
Zooming, panning or Fix X/Y switches the autoscaling of that axis off, so a refresh never undoes
a range you set. Home fits both axes once and switches both off: from then on the time axis
slides with the data and the value axis keeps its scale.

[Event markers](markers.md) follow their timestamps: as the X window slides, a marker moves with the
data and disappears when its timestamp leaves the visible range. Events that a growing file adds
to its `[events]` section appear at the next refresh, and a marker whose UTC has not been reached
yet appears once the data gets there.

[Redline markers](markers.md#redline-markers) stay at their value on the value axis. While *Auto Scale
Y* is on, the value axis refits to the data at each refresh, so a redline moves up or down with the
scale and disappears while its value is outside the visible range; with *Auto Scale Y* off the value
axis keeps its scale and the redlines stay put.

## Aligning recorded data with live data

To compare a recorded (static) file with a file that is still growing, plot parameters from both
in one plot. Then choose *TimeSlice → Align Static to Live* (or press Ctrl+L, or use the plot area's
context menu). While the mode is on:

| Action | Effect |
|---|---|
| Left drag | Move all static curves along the time axis. The live curves stay put |
| ← / → | Move by one pixel |
| Shift+← / → | Move by one X grid division |
| Ctrl+← / → | Move by a tenth of a pixel |
| Click, then click | Snap: click a feature on a static curve (a dashed line marks it), then the same feature on a live curve. The static curves move so the two line up |
| Right click | Cancel the drag in progress or a pending snap click |
| Wheel, middle drag | Zoom and pan as usual, so you can fine-tune at a closer zoom |
| Enter (or Ctrl+L again) | Accept |
| Esc | Cancel a pending snap click, otherwise cancel and put the curves back |

The status bar shows the current shift. Refreshing is held while the mode is on and catches up
when you accept or cancel.

Accepting moves the time reference of the static file groups: each group's reference becomes a UTC
(a seconds offset if its files have no common reference UTC), so the alignment lasts. The static
file's events move with its curves. It survives refreshes, replots and
new plots, and it is recorded as a `TIME group:start,stop,reference` command when you record a
script. To undo it, set the group's reference again (*TimeSlice → Per Database*). A group whose
reference is EOF cannot be aligned this way.

Align Static to Live needs a time plot. It is not available in tabular, End-to-End,
parameter-vs-parameter or log-X plots, or with 64-bit time switched on.

## AutoRun

`AUTORUN EVENT,script.wps` runs a script the first time the named event appears in a file's
`[events]` section during a refresh. You can manage the list with *Options → Auto Run*.
`AUTORUN CLEAR` empties it.

## Live streams

The PC GOAL live-stream library is not part of Multiplot.NET. Streams need a data-source plugin that
implements the stream protocol; see [Plugins](plugins.md).
