# 06 — Parameter Expressions and Functions

Anything that can be typed as a parameter name (Y list, X axis, favourites, WPD displays,
scripts) may instead be an **expression** or a **function call**. Owner decision: the expression
syntax must remain compatible with existing scripts, favourites and display files.

---

## 6.1 Parameter list and item classification

**FR-EXP-001 (list splitting)** A parameter list is split on **every** comma (`strtok(",")`) —
commas inside brackets or parentheses are *not* protected. (Function arguments are therefore
space-separated, never comma-separated.) Each item is then classified, in this order:
1. **function** if, lower-cased and with any `"…"` suffix removed, it starts with a known function
   name followed by `(` (§6.5) — unless the text after the function's closing `)` is not `]` and is
   itself an expression (then it is an expression);
2. **expression** if it contains `[`, `]`, `(` or `)` outside single quotes and before any `"`, or
   starts with `+`/`-` followed by a digit (or `.digit`) (a signed constant);
3. otherwise a **plain parameter name**, optionally `file:name` and/or with a `"description"` suffix.

**FR-EXP-002 (plain names)** `name` is looked up with FR-SUN-030 rules (`@n` positions, primary,
alternate, extended names, quotes stripped). `prefix:name` (last `:`) restricts the lookup to files
whose lower-cased full path **or** group name contains `prefix` (substring). A `"text"` suffix is a
user description override (FR-EXP-060).

**FR-EXP-003 (one curve per file)** For each group, each list item is evaluated once per file of the
group; every successful evaluation produces one curve (max 1000 curves total). For expressions, the
**first** parameter of the expression must be found in the current file, otherwise that file is
skipped (no curve); other parameters may come from any file of the group, current file first.

---

## 6.2 Expression syntax

```
expression := operand { operator operand }
operand    := { "(" } ( "[" item "]" | constant ) { ")" }
item       := parameterName | file ":" parameterName | functionCall | expression
constant   := [+|-] digits [ "." digits ] [ (e|E) [+|-] digits ]   |   0x h{1,4}
operator   := ^  /  *  %  +  -  &  &&  |  ||  ~  <  <=  <<  <>  <|  >  >=  >>  >|  ==  =  ?  #
suffix     := [ "\"" description "\"" ]
```

**FR-EXP-010** Parameters and functions inside expressions **must** be in square brackets; brackets
may nest only when the outer bracket holds a function or expression. A parameter name inside
brackets may contain any characters except `]` (operators inside brackets are not operators).

**FR-EXP-011 (syntax errors)** Unbalanced/invalid enclosures shall be reported with the exact
messages (only when dialogs are allowed), and the expression yields no curve; in that case the whole
load is aborted (`!abort`):
`Open bracket missing close`, `Close bracket missing open`, `Open parenthesis missing close`,
`Invalid combination of ( and [`, `Invalid combination of ( and ]`, `Close parenthesis missing open`,
`Invalid combination of ) and [`, `Invalid combination of ) and ]`,
`Invalid syntax for function or grouping operation` (each followed by a newline and the expression);
stack-building errors: `Invalid file override\nFile specification must be within brackets\n`
(when the text contains `:[`) or `Invalid constant in expression\nIf parameter or function, enclose
with brackets\n`; `Unbalanced grouping of constants/parameters\n`; `Stack overflow--expression too
large\n`; `Unidentified trailing characters\n`; otherwise `Failed to parse expression
components\nParameters AND Functions used in expressions\nMUST have enclosing brackets\n`.
`Too many parameters in expression` when ≥ 128 bracketed items (`MAXEXPIDS`).

**FR-EXP-012 (constants)** Decimal constants: optional leading sign, digits, at most one `.`,
optional exponent `e`/`E` with optional sign and **mandatory** digits. Hex constants `0x` + 1–4 hex
digits (max `0xffff`, value as unsigned). Anything else outside brackets is an invalid constant.

**FR-EXP-013 (operator scanning)** The next operator is the first character from the operator set
`^/*%+-&|~<>=?#` at position ≥ 1 of the remaining text that is not immediately preceded by `e`/`E`
(so `1e-5` is one constant and a leading sign belongs to the constant). Two-character operators are
`&&`, `||`, `<=`, `<<`, `<>`, `<|`, `>=`, `>>`, `>|`, `==` (the second char is consumed when it is one
of `<>&|=`; note `<|` and `>|` are recognised by the evaluator via the second character).

### 6.2.1 Precedence and associativity (must be bit-exact)

**FR-EXP-020** Each operator gets rank `R = 14 − index(op in "^/*%+-&|~<>=?#")`, then `*` is raised
to equal `/` and `-` is raised to equal `+`, plus `14 × parenDepth` where parenDepth is the number of
`(` opened before the operator's left operand minus `)` that appear between that operand and the
operator. Resulting base ranks:

| Rank | Operators |
|---|---|
| 14 | `^` |
| 13 | `/` `*` |
| 11 | `%` |
| 10 | `+` `-` |
| 8 | `&` `&&` |
| 7 | `|` `||` |
| 6 | `~` |
| 5 | `<` `<=` `<<` `<>` `<|` |
| 4 | `>` `>=` `>>` `>|` |
| 3 | `=` `==` |
| 2 | `?` |
| 1 | `#` |

Evaluation repeatedly picks the operator with the **highest** rank, ties resolved to the
**leftmost**, applies it to its two neighbouring values, and collapses the stack. (Hence `^` is
left-associative; `%` binds weaker than `*`; `<` binds tighter than `>`; `~` binds weaker than `&`/`|`.)
These rules shall be preserved exactly (DEV-EXP-001 lists the surprises for user documentation).
*Acceptance:* TST-EXP-001 — a table of ≥ 200 constant-only expressions matches
the golden results.

### 6.2.2 Operator semantics (values are double precision)

| Op | Result |
|---|---|
| `a ^ b` | `pow(a,b)`; if a < 0 and b not integral → **error −2** (prompt "‑X ^ non-integer Y!") |
| `a * b`, `a + b`, `a - b` | arithmetic |
| `a / b` | b == 0 → **error −1** (prompt "Divide by zero!"), result 0 |
| `a % b` | `fmod(a,b)` |
| `a ~ ` | **point-to-point delta** (postfix; the right operand slot is a dummy 0): result = a(current point) − a(previous point); the first output point is forced to 0. Any number of `~` per expression (DEV-EXP-003). |
| `a & b` / `a | b` | bitwise AND / OR of `(uint32)a`, `(uint32)b` |
| `a && b` / `a || b` | logical (operands truncated to uint32 first) → 1 or 0 |
| `a << b` / `a >> b` | shifts on uint32 |
| `a < b`, `a <= b`, `a > b`, `a >= b` | → 1 or 0 |
| `a <> b` | exact inequality → 1/0 |
| `a == b` | `|a − b| < 0.0001` → 1/0 |
| `a <| b` / `a >| b` | min / max |
| `c = v` | **filter**: if `(int)c != 0` → v, else the point is **removed** from the result |
| `c ? v` | if c ≠ 0 → v else INVALID (−1.7e308) |
| `x # w` | if x ≠ INVALID → x else w (so `c ? v # w` is if/else) |

**FR-EXP-021 (errors during evaluation)** Divide-by-zero and negative-power errors: if dialogs are
allowed, ask `<group>\nDivide by zero![ (may be removed by filter)]\n<expr>\nIgnore and
continue?` (resp. `\n-X ^ non-integer Y!\n`); *Yes* suppresses the prompt for the rest of the current
load operation and uses result 0; *No* (or dialogs not allowed) abandons this expression (no curve).
**FR-EXP-022 (dropouts)** If any parameter value used at a point is a dropout, or the output time is
outside a parameter's first..last time, the result at that point is a dropout (no operators evaluated).
**FR-EXP-023 (filter)** If the expression contains `=` that is not part of `==`, points whose result
is the filter flag are omitted; if all points are removed → no curve and (unless suppressed, dialogs
allowed and not auto-refreshing) the prompt `<group>\nFilter removed all points for\n<expr>\nIgnore
additional occurances?`.

---

## 6.3 Timeline and alignment

**FR-EXP-030 (base timeline)** If `CALCEVERYTIME` is OFF: the base is the first bracketed parameter
unless another parameter's **file** data rate (> 0) is lower than the base file's rate, in which
case the lowest-rate one becomes the base. The result has the base's time stamps.
If `CALCEVERYTIME` is ON and there are ≥ 2 parameters: the timeline is the sorted union of all
parameters' time stamps with duplicates closer than 1 µs removed (float32 arithmetic, or double
GMT-derived when 64-bit time is on).
For constant-only expressions (no brackets), the timeline is the file's TIME parameter (position 0)
and the expression is evaluated only for the first file of the group.

**FR-EXP-031 (alignment)** A parameter is index-aligned with the base when it has the same point
count, same file, the file is a common-time format (COMPRESSED, COMMONTIME, UNCOMPRESSED,
CIRCULAR), it is not a function result, and 64-bit time is off. Otherwise its value at each base
time `t` is found by time search: `ALIGNCLOSEST ON` → closest point; OFF → last point at or before `t`
(sample-and-hold / "stale" value). With 64-bit time, the search is done on GMTs.

**FR-EXP-032 (result nomenclature)** Result name = expression text (if > 256 chars, truncated to
252 + `...`); for constant-only expressions with a `file:` prefix, the text after the last `:`.
Units/description: if the **last** operator of the expression is `=` the units and description of
the last bracketed parameter are kept; otherwise both are blank. Then FR-EXP-060 overrides apply.

---

## 6.4 X-axis (parameter vs parameter)

**FR-EXP-040** When the X axis is not `(default)`, for every group the X item (name, function or
expression; `file:` prefix allowed, searched in all files if not found in the group) is loaded once,
and every Y curve's time values are replaced by the X value at the closest time
(`syncXaxis`). If no X item can be found for a group, that group produces no curves
(`X Axis: <name>` error).

---

## 6.5 Functions

General form: `name(arg1 arg2 …)["description"]` — arguments are **space-separated**; an argument
may be a parameter name, `'quoted name with spaces'`, a nested function, or an expression
(`[a]+[b]`, parenthesised groups allowed). Function names are case-insensitive. Up to 8 parameter
arguments. If the first argument has a `prefix:` it restricts the file as in FR-EXP-002.
Secondary parameter arguments are read from the current file, else from any file in the group.
Every function returns a curve named by the function text; if a function produces 0 points it fails.

| ID | Function | Arguments | Semantics |
|---|---|---|---|
| FR-FN-001 | `avg(p [dt] [avgT] [b|e|a])` | dt = output interval (default INI `AverageRate`, used if < 1e-6), avgT = averaging window (default dt; if ≠ dt → sliding average; if ≤ 0 → sampling), time-stamp mode letter anywhere after the name: `b` first point, `e` last point, `a` averaged (default INI `TimeStampMode`) | block/sliding average via library `average()` (FR-FN-030) |
| FR-FN-002 | `abs acos asin atan ceil cos cosh exp floor log log10 sin sinh sqrt tan tanh int` `(p)` | | pointwise on non-dropout points; `acos/asin` |x| > 1 → dropout; `cosh, sinh, exp, tan, tanh` |x| > 85 → dropout; `log, log10, sqrt` x ≤ 0 → dropout (note: `sqrt(0)` is a dropout); `int` truncates toward zero |
| FR-FN-003 | `atan2(y x)` | | `atan2(y, x(t))` with x aligned by last-point-at-or-before; x dropout → dropout |
| FR-FN-004 | `rand(p)` | | replaces each non-dropout value with C `rand()` (0..32767) |
| FR-FN-005 | `spline(p [n])` | n = factor (|n|, default 2, min 2) | natural cubic spline (Burden & Faires Alg. 3.4) resampled to `points × n` equally spaced points over [t0, tN); confirm prompt if points < 2 or > 50,000 (`Perform SPLINE() with %d points?\n*Long process`) |
| FR-FN-006 | `deltat(p)` | | value = t[i] − t[i−1] (GMT-based when available); first point dropout |
| FR-FN-007 | `delta(p)` | | value = v[i] − previous valid v; first point dropout; dropouts stay dropouts and do not reset the previous value |
| FR-FN-008 | `fit(p)` | | linear least squares y = a + b·t; description becomes `Slope=%g Intercept=%g` (+ user description) |
| FR-FN-009 | `polyfit(p [n])` | n = degree (default 2) | polynomial least-squares fit, description from the fitter — see OQ-006 |
| FR-FN-010 | `stale(p1 p2 [dt])` | dt default = half of 1/(file rate), else 0.02 | requires equal point counts; output interleaves (t−dt, p2[i]), (t, p1[i]) → 2N points |
| FR-FN-011 | `zshift(p [t1] [t2])` | t1 default 0, t2 default t1 | subtract the value at closest point to t1 (if |t1−t2| < 0.001) or the mean over [t1,t2] (re-read from file) |
| FR-FN-012 | `redline(p MAX\|MIN\|HI\|LO)` | | data unchanged; curve flagged as upper redline (HI)/MAX)), lower (LO)/MIN)), else generic — drawn as redline (FR-PLT) |
| FR-FN-013 | `compact(p [dt])` | dt default 1.0 s | per interval keep min and max points in time order (FR-RT compaction algorithm) |
| FR-FN-014 | `xypair(y x)` | | Y vs X curve (time replaced by x value at closest time) |
| FR-FN-015 | `reltime(p)` | | value = relative time of each point |
| FR-FN-016 | `clip(p [min] [max])` | defaults ±3.4e38 | clamp |
| FR-FN-017 | `quantize(p [dt] [sampled])` | dt default `AverageRate`; sampled 0/1 | resample to multiples of dt (library `quantize`): sampled → closest point; else average if ≥ 3 points per interval and rate allows, else linear interpolation |
| FR-FN-018 | `refprop(fn fluid code p1 p2)` | | thermodynamic property via REFPROP — see OQ-005 |
| FR-FN-019 | `density enthalpy entropy rho (p1 p2 fluid code)`, `pressure temperature (p fluid)` | numeric fluid codes 2 N₂, 3 O₂, 8 He, 10 H₂, 11 para-H₂; codes 1 TP, 2 PS | deprecated aliases mapped to `refprop` |
| FR-FN-020 | `fn(name options p1 … p8)` | | user extension function — replaced by the .NET plugin API (FR-PLG-040) |

**FR-FN-030 (averaging algorithm)** `average(pid, dt, avgT, mode)` and `quantize` shall behave exactly as
implemented in `src/Multiplot.Core/ParameterOps.cs`, including: in averaged-time mode the
times are temporarily shifted by −avgT/2; break points at multiples of `min(dt,1)` aligned to
`floor(t0/·)`; dropouts skipped; last partial interval written; sliding mode accumulates points with
time ≥ tStart over the previous window.
*Acceptance:* TST-FN-001 — golden outputs for `avg`, `quantize`, `spline`, `compact`, `stale`,
`zshift`, `delta`, `deltat` on fixture data match golden exports within 1 ulp (float).

---

## 6.6 Descriptions, units and scale groups

**FR-EXP-060** A trailing `"text"` on any item overrides nomenclature:
* if text starts with `\` → it is appended to the existing description (scale-group id only);
* else `/u` or `/U` starts the units, which run to the next `\` or end; the remainder (with the units
  part removed) is the description;
* `\GROUP` at the end of a description assigns a **scale group id** used by `SCALEGROUP ON`
  (curves with the same id share a Y scale) — the part after `\` is hidden from legends (`STRIPGROUPSPEC`).
For functions the description override is used as is (fit/polyfit append it to the computed description).

**FR-EXP-061 (symbols in items)** `$SYMBOL` inside list items is substituted at load time
(FR-SCR-051); when an item contained a symbol, the curve's name is reset to the original **symbolic**
text (description part removed) and an empty description is replaced by the evaluated name, so that
legends show the symbol and real-time reloads re-evaluate it.

---

## 6.7 Implementation requirements

**NFR-EXP-001** The expression engine shall be a pure, UI-free library (`Multiplot.Expressions`)
with: tokenizer, stack evaluator, and a binding layer that resolves names via the
data-access interfaces. Prompts are raised through an injected `IUserInteraction` callback so that
scripts/hidden mode and tests can answer them.
**NFR-EXP-002** Evaluation of 10 million points with a 5-operator expression shall complete in
< 2 s on the reference machine (NFR-PERF), using vectorised loops where semantics allow (index-aligned
case), without changing results.
