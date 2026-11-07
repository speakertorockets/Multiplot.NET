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
to its `[events]` section appear at the next refresh, and a marker whose GMT has not been reached
yet appears once the data gets there.

[Redline markers](markers.md#redline-markers) stay at their value on the value axis. While *Auto Scale
Y* is on, the value axis refits to the data at each refresh, so a redline moves up or down with the
scale and disappears while its value is outside the visible range; with *Auto Scale Y* off the value
axis keeps its scale and the redlines stay put.

## AutoRun

`AUTORUN EVENT,script.wps` runs a script the first time the named event appears in a file's
`[events]` section during a refresh. You can manage the list with *Options → Auto Run*.
`AUTORUN CLEAR` empties it.

## Live streams

The PC GOAL live-stream library is not part of Multiplot.NET. Streams need a data-source plugin that
implements the stream protocol; see [Plugins](plugins.md).
