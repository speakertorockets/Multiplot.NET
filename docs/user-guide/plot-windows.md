# Plot windows

## Layout

A plot window shows these parts:

* **Title and sub-title**: set with `XTITLE` / `YTITLE` or with *Nomenclature → Set Title*. A
  title can contain title keywords and `\n`.
* **Legend**: one entry per curve, built from the Y nomenclature items (name, units, description,
  file and so on). Use *Nomenclature → Y Labels* or `YNOMEN NAME UNITS DESC`.
* **Axes**: the X axis shows time (or clock time with `CLOCKTIME ON`), or a parameter when one is
  set with `XAXIS`. The plot can have one Y scale or several (`MULTIY ON`). With several scales,
  curves are grouped by name, units, description, user text or independently.
* **Footer**: shows the time stamp, test information, version, user name and page number. Turn
  each item on or off from the *View* menu or with the matching script command.

## Styles

Use the *Style* menu or `STYLE` to switch styles:

| Style | Description |
|---|---|
| 2D line | Curves against time; optional symbols, stair-step drawing, dropout markers |
| 2D scatter | Symbols only |
| 3D line / 3D area | Perspective view with depth (`LINESIZE 3DTHIN/3DMEDIUM/3DWIDE`); *Axis → Z Angle* sets the angle |
| Logic | Each curve on its own strip, drawn as true or false labels (`SET LOGIC TEXT`) |
| Tabular | The values as text at a chosen time. Items can be laid out freely; there is a layout grid, a background bitmap (`SET BACKGROUND`) and number formats (`SET PRECISION`) |

Each curve keeps its own line size, line style, colour, symbol, tabular format and logic label set.
Change them with *Style → Trace Attributes* or `SET TRACE n,...`. With **Apply to all plots**
(`APPLYGLOBAL ON`), style commands change every plot.

## Mouse

| Action | Effect |
|---|---|
| Left drag in the plot area | Zoom box. Right-click cancels it. With several Y scales, it zooms the selected axis; hold **Shift** to zoom all axes |
| Left click on a curve | Shows the name of the curve under the cursor |
| Right drag | Runs the right-button function: **Delta** (slope read-out), **Slice Average** (opens the statistics dialog for the range; a click with no drag opens **Data Pick**) or **Track** (values of every curve at the cursor). **Shift** = Delta, **Ctrl** = Slice, **Shift+Ctrl** = Track |
| Right click outside the plot area, or with **Alt** | Context menu for the clicked region (X heading, Y heading, footer, plot area, tabular) |
| Middle drag | Pan by one graduation for every 20 px of movement |
| Wheel | Zoom in or out about the cursor (not in tabular or log plots) |
| Mouse move | Shows the coordinates in the status bar (*Options → Show Coordinates*) |

Choose the right-button function with *Options → Right Button*.

## Keyboard

| Key | Effect |
|---|---|
| Ctrl+Tab / Shift+Tab | Next or previous plot |
| Arrow keys | Shift the X or Y axis |
| Page Up / Page Down | Zoom in or out |
| Backspace | Undo zoom |
| Enter | Parameter selection |
| Space / Ctrl+Space | Pause or resume a running script |
| Ctrl+Esc | Stop a running script |
| Esc | Leave presentation mode |
| Alt+F1 … Alt+F5 | User buttons 1–5 |

The other shortcuts are shown next to the menu items.

## Scales

* *Axis → Fix X / Fix Y* (`FIXX lo,hi[,inc]`, `FIXY ...`, `FIXY MULTI ...`) fix the scale ranges.
* *Axis → Rescale* (`RESCALE`) returns to automatic scaling.
* *Undo Zoom* steps back through the zoom history.
* `ZOOM IN|OUT [X|Y]` and `PAN LEFT|RIGHT [PAGE|FULL]|UP|DOWN` work like the toolbar buttons.
* `LOGX` / `LOGY` select logarithmic axes.
* `ENDTOEND ON` places the files of a group one after another on the time axis.

The automatic scaling chooses the graduation, minimum and maximum of each axis from the data range.

## Markers

A plot can hold up to 1000 markers. The marker types are: text, redline, line, box, circle, event
and image. You can create them in four ways:

* *Markers → Draw*, then choose the type.
* *Markers → Edit*: the dialog lets you edit a marker by number, set its type, coordinates, colour
  and label, and choose whether the coordinates are a percentage of the plot area.
* The script command `MARKER n,type,x1,y1,x2,y2,color,label[\g]`.
* *Markers → Load* or `MARKERSET LOAD file.markers`.

`\g` applies the marker to every plot. Event markers can take their label from a file event, and the
labels `MAX` and `MIN` show the extreme values. Use `MARKERSET TOPMOST` to draw markers on top of
the curves.

## Tabular displays

In tabular style each curve shows its value at the current time, formatted with the curve's
tabular format. The format can be GENERAL, 0 to 7 decimals, HEX2/4/8 or OCT3/6/12. Item
positions come from `.wpd` display files, and *Options → Layout Grid* sets the layout grid.
(Multiplot.NET does not yet let you drag items with the mouse.) `TABSTEPRATE`, `TABHISTORY`, `DEFTABTIME` and
`SHAREDTIME` control how the displayed time moves.

## Display files (`.wpd`)

*File → Save Display* stores the plot setup in a `.wpd` file: the parameter list, scales, styles,
traces, markers and tabular layout. Opening the `.wpd` file again is meant to re-create the plot
from the files that are open at that time; this has not been tested yet in Multiplot.NET.
