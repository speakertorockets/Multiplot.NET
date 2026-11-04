# Real-time operation

Dynamic SUN files, and plugin files that report themselves as dynamic, grow while a test is running.
Multiplot.NET can follow them. Parquet files are always static. Convert a `.multi` file to Parquet
with `multi2parquet` only after the test has finished and the file is closed.

| Command / menu | Effect |
|---|---|
| *View → Auto Refresh*, `REFRESH seconds` | Re-read the open files every n seconds and update the plots |
| `REFRESH` (no number), toolbar Refresh | Refresh once now |
| `REFRESH OFF` | Stop auto refresh |
| `TIMESLIDE ON` | Slide the X window along as new data arrives |
| `FREEZE ON` | Stop updating the active plot. The data still keeps being collected |
| `AUTOCOMPACT points[,interval]` | When a curve has more than *points* points, keep only the minimum and maximum in each interval |
| `AUTODISCARD seconds` | Throw away data older than the given window |
| `IGNORESTATIC ON` | Do not re-read files that are not dynamic |

During a refresh, new points are appended to each curve. The scales follow the data unless you
have fixed or zoomed them; zooming while auto refresh is on fixes the scales.

## AutoRun

`AUTORUN EVENT,script.wps` runs a script the first time the named event appears in a file's
`[events]` section during a refresh. You can manage the list with *Options → Auto Run*.
`AUTORUN CLEAR` empties it.

## Live streams

The PC GOAL live-stream library is not part of Multiplot.NET. Streams need a data-source plugin that
implements the stream protocol; see [Plugins](plugins.md).
