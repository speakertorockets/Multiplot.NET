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
* **Test title**: with *Nomenclature → Test Info (Test Title)* (`TESTINFO ON`, INI `TestInfo`) the
  bottom-left corner shows the test title of the plot's data file on one line (the `Title` property:
  the SUN header title, the Parquet `header.title` metadata). When a plot shows data from several
  files, the title of the first curve's file is shown. Notes such as *Data is Averaged* follow it on
  the same line.
* **Window title**: the plot window's caption. *Nomenclature → Set Plot Window Title...* (also *Window
  → Set Plot Window Title...* in the right-click menus, script `CAPTION USER text`) sets your own text;
  *Options → Window Caption* chooses whether the caption shows that title, the plot title, the
  sub-title, the plot number, the parameter set or the script name.

The plot no longer shows a version number, script name, user name, page number or time stamp, and
plot group names are gone (*Set Group Name*, *Window → Tile By Group*, *Window → Auto Tab → By
Group*). See [Limitations](limitations.md) for the retired script commands.

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
| Page Up / Page Down | Zoom Y in or out |
| Insert / Delete | Zoom X in or out |
| Home | Fit both axes once to all values, then switch autoscaling off (see below) |
| Backspace | Revert scales (undo zoom) |
| Enter | Parameter selection |
| Space / Ctrl+Space | Pause or resume a running script |
| Ctrl+Esc | Stop a running script |
| Esc | Leave presentation mode |
| Alt+F1 … Alt+F5 | User buttons 1–5 |

The other shortcuts are shown next to the menu items.

## Scales and autoscaling

Each plot has two autoscale switches in the *Axis* menu (and in the plot's right-click menu). The
check mark shows whether each one is on for the active plot:

| Switch | On | Off |
|---|---|---|
| *Auto Scale X (Time Axis)* (`AUTOSCALE X ON\|OFF`) | The time axis spans all of the data. In real time it grows to include each new point. | The visible time span stays the same. In real time it slides along with the newest data, so the curves move to the left. If you are looking at older data (the newest point is not in view), the window stays where it is. |
| *Auto Scale Y (Value Axis)* (`AUTOSCALE Y ON\|OFF`) | The value axes fit the data in the visible time range, and refit whenever the time range changes or new data arrives. | The value axes keep their scale, also during real-time updates. |

* New plots start with both switches on. The defaults are saved in the INI as `AutoScaleX` and
  `AutoScaleY`.
* Turning a switch on rescales that axis at once.
* Setting an axis by hand switches its autoscaling off for that plot: zooming (keys, mouse wheel,
  zoom box), panning (arrow keys, middle-button drag), *Fix X* / *Fix Y* and *Revert Scales*. When
  you change the time range by hand while *Auto Scale Y* is on, the value axes refit to the new time
  range.
* *Axis → Auto Range X/Y Once* (Home, `RESCALE`) fits both axes once to all values on the plot and
  then switches both autoscale switches off. During real-time updates the time axis then slides
  with the data and the value axis keeps the fitted scale.
* Autoscaling applies to plots with a time axis. Parameter-versus-parameter plots and tabular
  displays keep their scales when new data arrives; use Home to fit them.
* `FIXX AUTO` and `FIXY AUTO` switch the axis back to autoscaling (the same as `AUTOSCALE X ON` /
  `AUTOSCALE Y ON`).

Other scale commands:

* *Axis → Fix X / Fix Y* (`FIXX lo,hi[,inc]`, `FIXY ...`, `FIXY MULTI ...`) set the scale ranges.
* *Revert Scales* (Backspace) steps back through the zoom history.
* `ZOOM IN|OUT [X|Y]` and `PAN LEFT|RIGHT [PAGE|FULL]|UP|DOWN` work like the Pan/Zoom keys (arrow keys, Page Up/Down, Insert/Delete).
* `LOGX` / `LOGY` select logarithmic axes.
* `ENDTOEND ON` places the files of a group one after another on the time axis.

The automatic scaling chooses the graduation, minimum and maximum of each axis from the data range.

## Fonts

A plot uses two fonts: the **label** font (legend, scale values, axis labels, event marker and redline
text, tabular displays) and the **title** font (title and subtitle). Both are chosen in the standard
Windows font dialog, which sets the font, the style (regular, bold, italic) and the size in points:

* *Nomenclature → Fonts → Labels...* and *Nomenclature → Fonts → Titles...*
* *Font → Legend/Scale...* and *Font → Title/Subtitle...* in the right-click menus of the title,
  subtitle, footer, plot and tabular areas.

The font applies to the active plot (all plots when *Options → Apply Changes Globally* is on) and to new
plots. *Save Settings* stores both fonts in `Multiplot.ini` (`TitleFont`, `LabelFont`).

Text grows and shrinks with the plot window. The chosen size is the size of the text in a plot window
about 770 pixels wide (and at least 512 high); a larger window shows proportionally larger text and a
smaller window smaller text, so the plot keeps the same layout at any size, in saved pictures and in
print. The default sizes are 10 pt for labels and 12 pt for titles. To make all text larger or smaller,
choose a larger or smaller size in the font dialogs.

The *Screen Scaling* (1–4), *Scale Up* / *Scale Down* and *Font Scaling → Enlarge* / *Reduce* menu items
no longer exist; the font sizes replace them. The `FONTSIZE` script command is still accepted but does
nothing. An INI file from an older version that has a `FontSize` key is converted once when it is
loaded: the label and title sizes are set so that the text looks as it did with that scaling.

## Colors and appearance

### Choosing colours

*Style → Colors* (and the *Colors* submenus of the plot right-click menus) has one item per colour:
*Line Colors* (asks for the curve colour number 1–100), *Label Color*, *Title Color*, *Grid Color*,
*Marker Color*, *Redline Color* and *Background Color*. Each opens the standard Windows colour dialog,
with the colour editor open and the first 16 curve colours as its custom colours. The colour applies
to the active plot (all plots with *Options → Apply Changes Globally*) and to new plots. Scripts use
`SET COLOR n|GRID|LABEL|TITLE|BACKGROUND|MARKER|REDLINE,r,g,b [GLOBAL]`. *Restore Colors* returns to
the startup colours and *Save Settings* stores them in `Multiplot.ini` (`Colors`).

### Default curve colours

Curves get the colours of a 25-colour palette, repeated for curve colours 26–100: red, blue, green,
orange, magenta, steel blue, maroon, gold, lime, plum, teal, cyan, salmon, pink, violet, azure, light
blue, olive, navy, dark red, dark green, dark blue, dark olive, dark teal and dark purple. The palette
contains no black and no white, and no two curve colours are the same. A `Colors` line in
`Multiplot.ini`, `SET COLOR` in a script or the colour dialog overrides it; an INI file that you saved
before keeps its colours.

### Light and dark appearance

*View → Appearance → Light* / *Dark* (script `APPEARANCE LIGHT|DARK`, INI `Appearance = Light|Dark`
in `[interface]`) switches the look of the whole application. Light is the default.

* **Dark** uses the dark Windows 11 style for the menus, the status bar, the dialogs and the window
  frames, and draws the plots on a dark background: a light background colour is drawn dark (white
  becomes very dark grey), and colours that would be hard to see on it — black or dark text, grid,
  markers and dark curve colours — are drawn lighter. The colour settings themselves are not changed,
  so switching back to *Light* shows the plots exactly as before.
* **Light** uses the light Windows 11 style and draws the colours as they are set.
* Pictures saved or copied from a plot (`SAVEPIC`, *Copy to Clipboard*, *Copy to File*) show the plot
  as it looks on the screen, so in the dark appearance they are dark too.
* The Windows colour and font dialogs and the message boxes keep the light Windows style.

## Markers

Event markers draw a vertical line with a text at a timestamp, for example to show when a valve
opened relative to the curves. The `[events]` of the data file are drawn automatically. Redline
markers draw a horizontal line with a text at a value on the value axis, for example a temperature
limit. A redline moves up or down when you pan or zoom the value axis, and it's hidden while its
value is outside the visible range. In multiple-Y mode the redlines use the selected value axis
(click an axis to select it). Event markers and redlines each have their own colour (*Style →
Colors → Marker Color* and *Redline Color*). See [Event and redline markers](markers.md) for the menus,
the `MARKER` and `REDLINE` commands and `.markers` files.

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
