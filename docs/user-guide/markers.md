# Event and redline markers

A plot can show two kinds of markers:

* **Event markers** mark a moment on the time axis with a vertical line (this page, from the top).
* **Redline markers** mark a value on the value axis with a horizontal line, for example a limit that
  a parameter must not cross (see [Redline markers](#redline-markers)).

Each kind has its own colour, which is separate from the grid colour (see
[Marker colours](#marker-colours)).

## Event markers

An event marker shows when something happened (an ignition, a valve opening, an operator action, a
fault) relative to the curves on the plot. A marker is only a **timestamp** and a **text**:

* Multiplot.NET draws a vertical line across the full height of the plot area at the timestamp,
  whatever the scales are.
* The text is written beside the line, at the **top** of the plot area (default) or at the
  **bottom**. If there is no room to the right of the line, the text goes on its left.
* Markers are drawn in the marker colour (*Style → Colors → Marker Color*, `SET COLOR MARKER`), under
  the curves. *Markers → Topmost* (`MARKERSET TOPMOST`) draws them on top of the curves.
* A marker is only drawn while its timestamp is inside the visible X range. When you zoom or pan, or
  when the plot slides along during real-time updates, the marker moves with its timestamp and
  leaves the window when the timestamp scrolls out of view.
* Markers are drawn on time plots only. Tabular displays, parameter-versus-parameter plots and
  end-to-end plots (`ENDTOEND ON`) don't show them.

A plot holds up to 1000 markers.

## Timestamps

A timestamp can be written in any of these forms. Each one can end with a `+seconds` or `-seconds`
offset, for example `IGNITION+2.5`.

| Form | Example | Meaning |
|---|---|---|
| `ddd/hh:mm:ss[.mmm]` | `100/12:00:05.250` | GMT: day of year and time of day |
| `hh:mm:ss[.mmm]` | `12:00:05` | GMT on the reference day of the file |
| `ddd/msec[.usec]` | `100/43205250.0` | GMT as day / milliseconds of day (the `[events]` notation) |
| number | `12.5` | Seconds on the plot's time axis (relative to the plot's time reference) |
| event name | `IGNITION` | An entry of the file's `[events]` section |
| `START`, `EOF`, `CUTOFF` | `EOF-10` | The start of the data, the end of the file, the cutoff time |

The timestamp is resolved against the plot's time reference every time the plot is drawn. A GMT or
event marker therefore stays on the right instant when you change the time reference, and a marker
whose GMT has not been reached yet in a real-time file appears as soon as the data gets there.

## Markers from the data file

Events stored in the data file are drawn as markers automatically. The event name is the marker
text and the event time is its timestamp.

* **SUN files** (`SUN_MULTITIME64` and the other SUN formats): the `[events]` section of the comment
  block, one `NAME=time` line per event. The time is `ddd/msec[.usec]` or `ddd/hh:mm:ss[.mmm]`:

  ```
  [events]
  FILE_REF=100/43200000.0
  IGNITION=100/12:00:10.000
  VALVE_OPEN=100/43230500.0
  ```

* **Parquet files**: the `multiplot.events` key-value metadata. It is a JSON array of
  `{"name", "julianDay", "milliSeconds", "microSeconds", "ticks"}` objects, which `multi2parquet`
  writes from the `[events]` section of the source `.multi` file (see
  [Import and export](import-export.md)):

  ```json
  [{"name":"IGNITION","julianDay":100,"milliSeconds":43210000,"microSeconds":0,"ticks":null}]
  ```

`FILE_REF` and `EOF` are time-reference entries and are not drawn. Entries that the reader flags as
`(invalid)` are not drawn either. Only the events of the files that the plot shows are drawn, and
an event that appears in several files of a group is drawn once. Real-time files that add events
while they grow get the new markers at the next refresh.

*Markers → Show File Events* (`MARKERSET FILEEVENTS ON|OFF`, INI `ShowFileEvents`) turns these
markers on or off. It is on by default. Turning it off doesn't affect the markers that you add
yourself.

## Adding markers yourself

* *Markers → Add Event Marker...* (Ctrl+D) asks for the timestamp, the text and the text position.
  The timestamp list offers the event names of the open files. On a time plot it starts with the
  centre of the visible X range in seconds.
* *Markers → Edit...* lists the markers of the active plot. Select a marker to change it with
  **Update**, or use **Add**, **Delete** and **Delete All**. **OK** applies the list.
* The script command `MARKER <timestamp>,<text>[,TOP|BOTTOM]`.
* A `.markers` file: *Markers → Load*, `MARKERSET LOAD file`, or drag the file onto the window.

If data files are open and the timestamp can't be found in them, the dialogs ask whether to keep
the marker anyway. That can be useful for a real-time file that hasn't reached the time yet.

Markers go to the active plot, or to all plots when *Options → Apply Changes Globally* is on. They
also go into the master marker set, which new plots copy when *Options → Continue Markers*
(`ENABLE MARKERS`) is on. A marker that is identical to one already on the plot is not added again.

### Script examples

```
open test.multi
plot PC1,TEMP

marker 100/12:00:05,Ignition                ; GMT, text at the top
marker IGNITION+2.5,Valve open,BOTTOM       ; 2.5 s after the IGNITION event, text at the bottom
marker 30,Operator: throttle up             ; 30 s on the time axis
marker EOF-10,Shutdown sequence, stage 1    ; the text may contain commas

markerset topmost on                        ; draw markers over the curves
markerset fileevents off                    ; hide the [events] markers of the file
markerset save C:\tests\run12.markers

clear marker 2                              ; remove the 2nd marker of the active plot
clear markers all                           ; remove all markers you added
markerset load C:\tests\run12.markers
```

In the `MARKER` command the timestamp is everything before the first comma. A trailing `,TOP` or
`,BOTTOM` (any case) sets the text position, and everything else is the text. The old form
`MARKER n,type,x1,y1,x2,y2,color,label` is no longer accepted and reports a script error.

## Marker files (`.markers`)

A `.markers` file is plain text with one marker per line, in the same form as the `MARKER`
arguments. Blank lines and lines that start with `;` or `#` are ignored.

```
; Multiplot.NET event markers: <timestamp>,<text>[,TOP|BOTTOM]
100/12:00:05,Ignition
IGNITION+2.5,Valve open,BOTTOM
EOF-10,Shutdown sequence
```

*Markers → Save* (`MARKERSET SAVE file`) writes the markers of the active plot. *Markers → Load*
(`MARKERSET LOAD file`) adds the markers of a file. Lines that are not valid markers, including
lines in the old `Marker = n valid type x1 y1 x2 y2 color label` format, are skipped and reported in
one message.

The same file also holds redline markers, as `redline <value>,<text>[,LEFT|RIGHT]` lines (see
[Redline markers](#redline-markers)). *Markers → Save* writes both kinds and *Markers → Load* reads
both kinds.

Display files (`.wpd`) store markers as `marker <timestamp>,<text>[,TOP|BOTTOM]` lines and redlines as
`redline <value>,<text>[,LEFT|RIGHT]` lines.

## Redline markers

A redline marker shows a limit or a reference value on the value (Y) axis. A redline is only a
**value** and a **text**:

* Multiplot.NET draws a horizontal line across the full width of the plot area at the value.
* The text is written just above the line, at the **left** end (default) or the **right** end of the
  line. If there is no room above the line (the value is at the top of the plot area), the text goes
  below it.
* Redlines are drawn in the redline colour, red by default (*Style → Colors → Redline Color*,
  `SET COLOR REDLINE`). Like event markers they are drawn under the curves unless *Markers → Topmost*
  is on.
* A redline is only drawn while its value is inside the visible value range. When you pan or zoom
  the value axis (arrow keys, mouse wheel, zoom box, *Fix Y*, autoscaling), the line moves up or down
  with the scale. When the value leaves the visible range, the line disappears, and it comes back
  when the value is in view again. Autoscaling fits the curves only; it doesn't stretch the value axis
  to include a redline.
* With *Axis → Multi Y → On* (`MULTIY ON`), the value is measured on the **selected** value axis.
  That is the first axis until you click another axis to the left of the plot area; the redlines then
  move to the position of the value on that axis. With a single value axis the redlines use it.
* On a logarithmic value axis (`LOGY ON`) redlines with a value of zero or less are not drawn.
* Redlines are drawn on 2D line and scatter plots, including parameter-versus-parameter and end-to-end
  plots. Tabular displays, logic plots and 3D plots don't show them.

A plot holds up to 1000 redlines.

Redlines are different from curve redlines made with `redline()` in a parameter list (see
[Parameter lists and expressions](expressions.md)). Those turn a curve into a redline curve; a redline
marker is a fixed value.

### Adding redlines

* *Markers → Add Redline Marker...* asks for the value, the text and the text position (Left or
  Right). The value starts at the centre of the visible range of the selected value axis.
* *Markers → Edit Redlines...* lists the redlines of the active plot. Select a redline to change it
  with **Update**, or use **Add**, **Delete** and **Delete All**. **OK** applies the list.
* The script command `REDLINE <value>,<text>[,LEFT|RIGHT]`.
* A `.markers` file with `redline` lines: *Markers → Load*, `MARKERSET LOAD file`, or drag the file onto
  the window.

The value is a plain number such as `450`, `-12.5` or `1.5e3` (a point is the decimal separator). The
text may contain commas. A trailing `,LEFT` or `,RIGHT` (any case) sets the text position.

Redlines go to the active plot, or to all plots when *Options → Apply Changes Globally* is on. They
also go into the master redline set, which new plots copy when *Options → Continue Markers*
(`ENABLE MARKERS`) is on. A redline that is identical to one already on the plot is not added again.

*Markers → Clear All* removes the event markers and the redlines. In scripts, `CLEAR MARKERS ALL`
removes only the event markers and `CLEAR REDLINES ALL` removes only the redlines.

### Script examples

```
open test.multi
plot PC1,TEMP

redline 450,Max chamber temperature         ; text at the left end of the line
redline 50,Min fuel pressure,RIGHT          ; text at the right end of the line
redline -1.5e3,Lower limit, stage 2         ; the text may contain commas

multiy on                                   ; several value axes: redlines use the selected axis
fixy -100,600,100                           ; the redlines move with the value scale

set color REDLINE,255,0,0                   ; red redlines
set color MARKER,0,128,0                    ; green event markers

clear redline 2                             ; remove the 2nd redline of the active plot
clear redlines all                          ; remove all redlines
```

A `.markers` file with both kinds of markers:

```
; Multiplot.NET event markers: <timestamp>,<text>[,TOP|BOTTOM]
; redline markers: redline <value>,<text>[,LEFT|RIGHT]
100/12:00:05,Ignition
IGNITION+2.5,Valve open,BOTTOM
redline 450,Max chamber temperature
redline 50,Min fuel pressure,RIGHT
```

## Marker colours

Event markers and redlines have their own colours, so you can change them without changing the grid:

| Colour | Menu | Script | Default |
|---|---|---|---|
| Event markers (line and text) | *Style → Colors → Marker Color*, *Markers → Marker Color...* | `SET COLOR MARKER,r,g,b [GLOBAL]` | Grey (the grid colour) |
| Redline markers (line and text) | *Style → Colors → Redline Color*, *Markers → Redline Color...* | `SET COLOR REDLINE,r,g,b [GLOBAL]` | Red (crimson, RGB 220,20,60) |

The two items are also in the *Colors* submenus of the plot right-click menus. The colours apply to
the active plot, or to all plots with *Options → Apply Changes Globally* (or `GLOBAL`), and to new
plots. *Style → Colors → Restore Colors* returns them to their startup values, and *Save Settings*
writes them to `Multiplot.ini` with the other colours. An INI file written by an older version has no
marker colour; the event markers then keep the grid colour of that file, and the redlines are red.
In black-and-white mode both are drawn in the monochrome colour.
