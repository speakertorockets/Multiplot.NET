# Parameter lists and expressions

## Parameter lists

A parameter list (what `PLOT`, `YAXIS` and the parameter dialog produce) is split at **every**
comma. Commas are not protected inside brackets, so function arguments are separated by spaces.
Each item can be:

* **A name**: `PC1`. Names are matched against the primary, alternate and extended names, without
  regard to case. `@n` selects the parameter at position n.
* **A file-qualified name**: `run12:PC1`. Only files whose path or group name contains `run12` are
  searched. The parameter-selection dialog writes these for you when one file is selected in its
  **File** box. A colon inside a `"description"` is not taken as a qualifier.
* **An expression**: anything that contains `[ ] ( )`, for example `[PC1]-[PC2]`.
* **A function**: `avg(PC1 0.5)`.
* Any of the above followed by `"description"`: the text in quotes replaces the curve description.

Each item is evaluated once for each file in the group. The limit is 1000 curves.

## Expressions

Parameters and functions in an expression **must** be inside square brackets. Constants can be
decimal (`1.5e-3`) or hexadecimal (`0x1F`, up to `0xffff`).

```
([PC1] + [PC2]) / 2
[VALVE] & 0x0004
([PC1] > 100) = [TEMP]          filter: TEMP only where PC1 > 100
([MODE] == 2) ? [A] # [B]       if MODE is 2 then A else B
[COUNTER]~                      point-to-point change
```

| Operator | Meaning |
|---|---|
| `^ * / % + -` | Power, multiply, divide (division by zero asks before going on), fmod, add, subtract |
| `& \| << >>` | Bitwise operations on unsigned 32-bit integers |
| `&& \|\|` | Logical and, or (result is 1 or 0) |
| `< <= > >= <> ==` | Comparisons. `==` is true when the values differ by less than 0.0001 |
| `<\| >\|` | Minimum, maximum |
| `~` | Postfix delta from the previous point |
| `c = v` | Filter: keeps the points where c is not zero |
| `c ? v`, `x # w` | Value v where c is true; replace invalid values with w |

Operator precedence and evaluation order follow the rules in the specification (chapter 06),
including some unusual precedences. If any input is a
[dropout](plot-windows.md#dropouts) at a point, the result at that point is a dropout too.

**Timeline.** By default the result uses the time points of the first parameter. The other
parameters take their value at the last point at or before each time. Two commands change this:
`CALCEVERYTIME ON` uses every time point of every parameter, and `ALIGNCLOSEST ON` takes the
closest point instead.

## Functions

| Function | Result |
|---|---|
| `avg(p [dt] [window] [b\|e\|a])` | Block or sliding average. The time stamp is the first, last or averaged point |
| `abs acos asin atan ceil cos cosh exp floor log log10 sin sinh sqrt tan tanh int (p)` | Applied to each point. Values outside the valid range become dropouts |
| `atan2(y x)` | Four-quadrant arctangent |
| `rand(p)` | Random values (0 to 32767) at the time points of p |
| `spline(p [n])` | Natural cubic spline resampled to n times as many points |
| `delta(p)`, `deltat(p)` | Change in value, and change in time, from the previous point |
| `fit(p)` | Linear least-squares fit. The slope and intercept are shown in the description |
| `polyfit(p [n])` | Recognised, but produces no curve: polynomial fitting is not included |
| `stale(p1 p2 [dt])` | Interleaves p2 at t−dt with p1 at t |
| `zshift(p [t1] [t2])` | Subtracts the value at t1, or the mean over [t1, t2] |
| `redline(p MAX\|MIN\|HI\|LO)` | Draws the curve as an upper or lower redline |
| `compact(p [dt])` | Keeps only the minimum and maximum in each dt interval |
| `xypair(y x)` | Plots y against x |
| `reltime(p)` | The relative time of each point |
| `clip(p [min] [max])` | Limits the values to [min, max] |
| `quantize(p [dt] [sampled])` | Resamples to multiples of dt |
| `fn(name options p1 … p8)` | A function supplied by a plugin (`IExpressionFunctionProvider`) |

`refprop(...)` and the old thermodynamic aliases are still recognised. They work only when a plugin
provides them; otherwise the message *RefProp() function not available* is shown.

## Parameter against parameter

`XAXIS name` plots each Y item against the parameter `name`, using the value at the closest time.
`XAXIS` with no argument returns to plotting against time.
