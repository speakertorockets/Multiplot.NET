# 11 — Real-Time Operation

In scope (owner decision): live (growing) files, automatic refresh, AutoRun scripts on events, PC GOAL
`.flt` filter streams, compaction/discard, dropout display.

---

## 11.1 Refresh control

**FR-RT-001 (auto refresh)** Auto refresh runs a timer with interval *R* ms. It is started
automatically when a dynamic file or stream is opened and auto refresh is off (interval = previous
value or INI `AutoRefresh`, default 2000 ms). Controls:
* *Refresh interval…* dialog: `Refresh interval delta (in seconds):` with choices `OFF, .1, .2, .5, 1,
  2, 5, 10`; 0/OFF stops; recorded `refresh %g` / `refresh OFF`.
* *Toggle refresh* (toolbar) stops/restarts using the previous interval (only when real-time data is loaded).
* *Refresh now* performs one refresh if none is in progress.
* Script `REFRESH` (FR-SCR-284).

**FR-RT-002 (pause)** Any operation that reads or rearranges plot data (loading, plotting, style
changes, scale changes, exports, marker changes, window redraws, time changes) shall pause refresh
for its duration: set the paused flag and wait until an in-progress refresh
completes; un-pause afterwards. A paused tick does nothing.

**FR-RT-003 (threading)** Timer ticks start the refresh on a background task when auto refresh is on
and not paused; one-shot refreshes run synchronously. Re-entry is prevented (one refresh at a time).
Plot data swaps happen only when the plot is not painting; all mutations of
plot-bound data shall be marshalled to the UI thread or use immutable snapshots (NFR-RT-001).

## 11.2 File refresh (SUN dynamic files)

**FR-RT-010** `Refresh(file)` for dynamic SUN files shall: re-open/seek to force OS cache refresh,
compare the signature (mismatch → −4), read header words 1..9 (failure −5), and:
* MULTITIME: if `blockPoints[0] == blockPoints[1]` the writer has finished → file becomes static.
* changed `blockPoints` → update needed;
* changed `refTime[0..5]` or item count → **reference change** (re-read the whole map, discard cached
  MULTITIME block lists) → result 2, else 1;
* non-MULTITIME: reload cached TIME / GMT indexes (failure → −1);
* re-read events; if the count changed → OR 4.
Not dynamic → −2; cannot open → −3; unchanged → 0.

## 11.3 Plot refresh algorithm

**FR-RT-020** On each refresh: refresh every open file (stream sources sharing a stream id do not
clear stream events until the last of them); note *reference changed*, *event added*, and *dynamic
event* (a stream event; triggers reload when any group uses reference `TIMEREF`). If any file updated
or the time reference was changed by `TIME`, update the status-bar current time (first stream's
current GMT, else first dynamic file's GMT index / `EOF` event) and, for every plot (stop early if
paused), for every curve (last to first) whose source file updated:
1. Determine the request start:
   * tabular style, not forced, *TabHistory* off → from the curve's last time, replace data;
   * not parameter-vs-parameter, not forced, group reference ≠ `EOF`, no reference change → from the
     curve's last time, **append**;
   * otherwise → the group's full slice (FR-LOAD-021), replace.
2. Stop: `EOF` → +3e38, else the group's stop time.
3. If the plot is frozen, or (no time change and stop − start < 0.250 s): if not frozen and stop ≠
   `EOF`, mark the plot as **frozen-complete** (slice fulfilled; shown on the plot) — skip.
4. Re-evaluate the curve item (symbols re-substituted; function, expression or plain read; X item for
   parameter-vs-parameter, which always replaces).
5. Replace: keep display attributes, tab position, and name (units/description too unless the
   reference changed); keep previous min/max if the Y scale is persistent, else adopt new; for the
   first curve also reset X (if not persistent) and Y (single-scale, not persistent) scales.
   Append: `AppendParameter` (retain scale if Y persistent or time-slide on) and incrementally extend
   the drawn trace; with *TimeSlide* the X window slides to keep the latest data visible.
6. If nothing was returned: after a time change set the curve to one dropout point at the start time;
   otherwise stop processing this plot.
7. Data reduction (not for parameter-vs-parameter): if *AutoDiscard* N > 0 discard data older than
   (last time − N s) — only when more than 1024 points would be removed; else if *AutoCompact* P > 0
   and points since the last packed position ≥ P, compact with interval I (FR-RT-031).
A plot is repainted fully if data was replaced, it was marked frozen, or an event was added; otherwise
only the appended segments are drawn.
*Acceptance:* TST-RT-001 — a test writer appends MULTITIME blocks to a fixture file at 10 Hz while the
application auto-refreshes at 0.5 s; after 60 s the plotted data equals a static re-load of the final file;
UI remains responsive (frame time < 100 ms).

**FR-RT-021 (freeze)** `FREEZE ON|OFF` / plot popup *Freeze* stops/starts refreshing of the active
plot (user freeze = −1 vs. automatic slice-complete freeze = 1); frozen plots show an indicator.
Changing `TIME` unfreezes non-user-frozen plots and forces reload.

**FR-RT-022 (ignore static)** With *IgnoreStatic*, X autoscale on real-time plots ignores static
(non-dynamic) curves' data beyond the dynamic data range.

## 11.4 Data reduction

**FR-RT-030 (discard)** Discard: start = first index with time ≥ last − N (mode −1); if start
> 1024, shift time/data (and own GMT array for MULTITIME) down, adjust common-time offsets, recompute
min/max offsets (and values unless Y persistent).

**FR-RT-031 (compact)** Compaction of a curve over an interval: from the last packed position, partition into
consecutive time intervals of length *interval* (starting at each segment's first time); for each
segment keep the min and max points in time order (1 point if equal/single); unless *all*, leave the
newest 10 % of unpacked points untouched; record the new packed position. Dialog *Auto Compact*:
point threshold (`0 (disable)`, 10000, 25000, 50000, 100000) and interval (.5, 1.0, 2.0, 5.0, 10.0);
*Auto Discard*: `Maximum number of seconds maintained:` (`0 (ALL)`, 300, 900, 1800, 3600).

## 11.5 AutoRun

**FR-RT-040 (AutoRun)** The AutoRun list holds entries `EVENT,script.wps` (event upper-cased).
After each refresh tick, for each entry in order and each **dynamic** file: if the event resolves in
that file (strict GMT lookup, FR-LOAD-020), then
* if a script is running: count deferrals; when deferral × interval > 6000 ms ask `A running script is
  blocking a triggered AutoRun\nForce cancel?` (Yes → abort the running script); keep waiting;
* else remove the entry (one-shot), wait for the refresh to finish, and run the script.
At most one AutoRun fires per tick. The *AutoRun* dialog edits the list (recorded as `AUTORUN clear`
followed by `AUTORUN <entry>` lines).

## 11.6 Streams (PC GOAL)

**FR-RT-050** Stream sources (signature `0xFFF0FFFF`) are provided by a plugin (FR-PLG-031). The host
shall support for them: stream id per source (`header.blockPoints[1]`), stream events
including `STREAMnnREF` used when the reference is `NONE`, `EOF` = stream current GMT, `TIMEREF`
dynamic event, certification status text shown on plots when *StreamCert* is on, history access
toggle (`ENABLE/DISABLE PCGHISTORY` → plugin command), cache size (`PcgCacheSize` → `cachesize n`),
*FirstOccurrence* loading rule, and `@commands` via `LIBCOMMAND`. The exact protocol is unknown — OQ-001.

## 11.7 Dropouts

**FR-RT-060** Dropout values are never drawn as data. With *DropoutIgnore* ON (default) the dropout
is skipped and the line continues from the last valid point to the next valid point; with OFF the
trace is broken (gap) at the dropout. With *DropoutMarker* ON, a small marker is drawn just below the
top edge of the plot area at each dropout's X position (not for parameter-vs-parameter plots).
displays.

**NFR-RT-001** Refresh of 50 curves from live files at 10 Hz with 1 kHz data shall keep the UI
responsive (input latency < 100 ms) and keep memory bounded when AutoDiscard/AutoCompact are set.
