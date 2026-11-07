# 03 — SUN Data File Format, LZH Codec and Time Arithmetic

This chapter is the **normative specification** of the SUN family of binary test-data files.
Multiplot.NET implements it in managed C# (owner decision: no native DLL). Everything needed to
implement a bit-compatible reader and writer is in this file.

> **Byte order rule.** Unless explicitly stated, every multi-byte numeric field in a SUN file
> header, parameter map, and MULTITIME block header (`pidHeader`) is stored **big-endian**
> (little-endian hosts swap them after reading). The only exception is the
> **payload** of MULTITIME data blocks (time-stamp and data arrays), whose byte order is given by
> the per-block `bigEndian` flag (§3.6).

---

## 3.1 Format family

| Format | On-disk signature bytes (offset 0..3) | `uint32` read little-endian | Data blocks | Map entries | Real-time capable |
|---|---|---|---|---|---|
| SUN_COMPRESSED | `FF FF FF FF` | `0xFFFFFFFF` | 1–2 LZH blocks per parameter | fixed 80 B | no |
| SUN_UNCOMPRESSED | `FF FF FF F0` | `0xF0FFFFFF` | 1 raw block per parameter | fixed 80 B | yes |
| SUN_CIRCULAR | `FF FF FF F1` | `0xF1FFFFFF` | 1 raw circular block | fixed 80 B | yes |
| SUN_MULTITIME | `FF FF FF F2` | `0xF2FFFFFF` | linked list of blocks (32-bit links) | variable | yes, if header `blockPoints[0] != blockPoints[1]` |
| SUN_COMMONTIME | `FF FF FF F3` | `0xF3FFFFFF` | like COMPRESSED | variable | no |
| SUN_MULTITIME64 | `FF FF FF F4` | `0xF4FFFFFF` | linked list (64-bit links) | variable, 64-bit block pointer | as MULTITIME |
| SUN_MULTITIME_SRT | `FF FF FF F5` | `0xF5FFFFFF` | as MULTITIME | variable | always |
| SUN_MULTITIME64_SRT | `FF FF FF F6` | `0xF6FFFFFF` | as MULTITIME64 | variable | always |

The value `SUN_FORMAT` (on-disk `FF FF FF F0`) is the format mask.

**FR-SUN-001** The reader shall accept a file as a SUN file iff `(sig & SUN_FORMAT) == SUN_FORMAT`,
where both values are formed from bytes 0..3 in the same byte order (i.e. bytes 0–2 are `FF` and
the high nibble of byte 3 is `F`). Otherwise it shall fail with `ERR_FILESIG`.
*Acceptance:* every fixture in TST-SUN-001 opens; a file whose first byte is `00` is rejected with ERR_FILESIG.

**FR-SUN-002** `_SRT` signatures shall be normalised to their base format for all structural
decoding, but the original signature shall be retained in the file-information object
(`FileInfo.FileSig`) and shall mark the file as dynamic (real-time).
*Acceptance:* a `FF FF FF F5` file decodes identically to the same file with `F2`, and reports `IsDynamic = true`.

**FR-SUN-003** Dynamic (real-time capable) status shall be determined at open as follows:
UNCOMPRESSED, CIRCULAR, MULTITIME_SRT, MULTITIME64_SRT → dynamic; MULTITIME/MULTITIME64 →
dynamic iff header `blockPoints[0] != blockPoints[1]`; COMPRESSED and COMMONTIME → static.
A MULTITIME parameter whose map `blockPtr` was 0 at open but becomes non-zero on a later read
shall switch the file to dynamic. Dynamic files keep the file handle open (shared read,
`FileShare.ReadWrite | FileShare.Delete`) for refresh; static files may close it after each read.

---

## 3.2 File header (264 bytes, offset 0)

| Offset | Size | Field | Type | Notes |
|---|---|---|---|---|
| 0 | 4 | `fileSig` | bytes | See §3.1 (compare raw) |
| 4 | 24 | `refTime[6]` | u32 BE ×6 | year, Julian day, hour, minute, second, millisecond of time zero |
| 28 | 4 | `items` | u32 BE | number of parameters (map entries) |
| 32 | 4 | `blockPoints[0]` | u32 BE | COMPRESSED/COMMONTIME/UNCOMPRESSED: points in block 1. CIRCULAR: total slots. MULTITIME: reserved (0 when written) |
| 36 | 4 | `blockPoints[1]` | u32 BE | COMPRESSED/COMMONTIME: points in block 2 (0 = single block). CIRCULAR: wrap position. MULTITIME: **update counter** (incremented by writer on every appended block) |
| 40 | 4 | `cutoff` | f32 BE | engine cutoff time (seconds, relative) |
| 44 | 80 | `title` | char | NUL- or length-terminated ASCII |
| 124 | 80 | `comment` | char | |
| 204 | 12 | `serialNum` | char | |
| 216 | 12 | `testStand` | char | |
| 228 | 12 | `testPosition` | char | |
| 240 | 12 | `testType` | char | |
| 252 | 12 | `testNumber` | char | |

**FR-SUN-010** The reader shall decode the header exactly as above. Fixed-length text fields are
decoded as "up to the first NUL, at most N bytes", Latin-1 (Windows-1252) encoding.

**FR-SUN-011** If `refTime[1]` (Julian day) is 0 the reader shall treat it as 1. The file
reference UTC shall be `TimeTag{ JulianDay = refTime[1], MilliSeconds = h*3600000 + m*60000 +
s*1000 + ms, MicroSeconds = 0 }`. If the comment block contains an event named exactly `FILE_REF`
(case-sensitive), that event's time shall replace the file reference UTC (evaluated once, at open).

**FR-SUN-012** The reported point count shall be `blockPoints[0] + blockPoints[1]` for
non-MULTITIME formats and `blockPoints[1]` (the update counter) for MULTITIME formats.

---

## 3.3 Parameter map (starts at offset 264)

### 3.3.1 Fixed entries — COMPRESSED, UNCOMPRESSED, CIRCULAR (80 bytes each)

| Rel. offset | Size | Field | Notes |
|---|---|---|---|
| 0 | 12 | `name1` | primary name |
| 12 | 12 | `name2` | secondary (alternate) name |
| 24 | 32 | `description` | |
| 56 | 2 | `typeFlag` | bytes `00 FF` ⇒ extended type present |
| 58 | 2 | `typeIDS` | 2-char type id (see §3.5), only meaningful if `typeFlag` is `00 FF` |
| 60 | 8 | `units` | |
| 68 | 4 | `seqNum` | u32 BE |
| 72 | 4 | `blockPtr[0]` | u32 BE file offset of data block 1 |
| 76 | 4 | `blockPtr[1]` | u32 BE file offset of data block 2 (0 if single-blocked) |

**FR-SUN-020** The description shall be decoded as up to **36** bytes starting at relative offset 24
(i.e. it may overlay `typeFlag`/`typeIDS` when no extended type is present). When `typeFlag` is
`00 FF`, the description ends at byte 32 (the `00`). The first character of `typeIDS` shall be
lower-cased. If no extended type is present the type is `f4`.
*Acceptance:* a map entry with a 36-character description and no extended type shows all 36 characters.

### 3.3.2 Variable entries — MULTITIME, MULTITIME64, COMMONTIME

Each entry, in order:

| Field | Encoding |
|---|---|
| `name1` | u8 length L (0..255) followed by L bytes |
| `name2` | u8 length + bytes |
| `description` | u8 length + bytes |
| `units` | u8 length + bytes |
| `typeIDS` | 2 bytes (first char lower-cased on read) |
| `seqNum` | u32 BE |
| block pointer(s) | MULTITIME, COMMONTIME: `blockPtr[0]` u32 BE, `blockPtr[1]` u32 BE. MULTITIME64: one **i64 BE** occupying the same 8 bytes |

**FR-SUN-021** The reader shall decode variable entries as above. `extendedType` is true iff
`typeIDS != "f4"`. For MULTITIME64 the 8 pointer bytes shall be read as one big-endian signed
64-bit offset of the first block header.

**FR-SUN-022 (rate packing)** For MULTITIME/MULTITIME64/COMMONTIME entries, if bit 31 of `seqNum`
is set, the parameter's data rate (samples/s) is `(seqNum >> 16) & 0x7FFF` and the sequence
number is `seqNum & 0xFFFF`; otherwise the rate is *unknown* (−1). Writers shall encode with
`PACK_RATE(rate, seq) = 0x80000000 | (rate << 16) | min(seq, 0xFFFF)`.
For fixed-entry formats the rate is unknown until the TIME index is read (then `1/Δt` at t = 0, §3.4.4).

### 3.3.3 Parameter lookup

**FR-SUN-030** Parameter lookup by name shall:
1. strip leading and trailing spaces and single quotes (`'`) from the requested name (embedded spaces kept);
2. compare case-insensitively, ignoring trailing spaces of map names;
3. if the name is `@n` (digit after `@`), select map position *n* (0-based) if `n < items`;
4. otherwise search primary names, then secondary names (result flagged *alternate*), then
   extended names from `[extendednomen]` (flagged *extended*); first match wins.

The returned nomenclature depends on which list matched: primary → (name1, description);
alternate → (name2, description); extended → (extended name, extended description). Units always
come from the map.
*Acceptance:* `readParm(" 'pc1' ")` finds a parameter mapped as `PC1   `.

**FR-SUN-031** The special name `@index` is reserved for the file time index (§3.4.4) and shall not
be user-visible.

---

## 3.4 COMPRESSED, COMMONTIME, UNCOMPRESSED and CIRCULAR data

### 3.4.1 Common rules

* Map position 0 is always the TIME parameter: type `f4`, relative seconds from the reference time.
* Map position 1 *may* be a UTC index of type `t8` (TimeTag), used for high-resolution
  UTC-referenced access (§3.4.4).
* All parameters share the same time base (one value per TIME point).
* Data values are stored big-endian with the element size of their type (§3.5).

### 3.4.2 COMPRESSED / COMMONTIME blocks ("double blocking")

**FR-SUN-040** Each parameter has one or two LZH-compressed blocks (§3.7). Block 1 holds points
`[0, blockPoints[0])`, block 2 holds `[blockPoints[0], blockPoints[0]+blockPoints[1])`.
The compressed byte length of each block is **not stored**; it shall be computed (as `calc_blksz`):

* If `parmInfo[0].blockPtr[1] != 0` (file is double-blocked): for parameter *i*,
  `size0 = blockPtr[1] − blockPtr[0]`, `size1 = next.blockPtr[0] − blockPtr[1]`;
  for the last parameter `size1 = fileLength − blockPtr[1]`.
* Else: `size0 = size1 = next.blockPtr[0] − blockPtr[0]`, last parameter to EOF.

Consequently writers **shall** lay blocks out contiguously in map order with nothing between them,
and the comment block must lie between the map and the first block.

**FR-SUN-041** Reading a slice `[start_point, stop_point]` shall determine, per block, the byte
window `[strb, stpb)` to keep (`getBytePos`), decompress each needed block from its start
discarding bytes before `strb` and stopping at `stpb`, and concatenate block 1's part followed by
block 2's part.

**FR-SUN-042 (TIME delta encoding)** In COMPRESSED and COMMONTIME files the TIME parameter
(position 0) is stored as float32 deltas, **independently per block**: within a block, element 0 is
absolute and element *k* holds `t[k] − Σ` where Σ is the running double-precision sum of the
*already-rounded* float values preceding it in that block. Decoding (`delt2lin`): `sum = x[0]`;
for *p* ≥ 1: if *p* equals `blockPoints[0]` restart `sum = x[p]` (absolute) and leave it, else
`sum += x[p]; x[p] = (float)sum`. Encoding is the exact inverse (`lin2delt`).
Note: decoding of a partial slice of TIME is never done — the full TIME index is always read.
*Acceptance:* TIME decoded from a fixture equals the golden exported TIME column bit-for-bit.

**FR-SUN-043 (double-block split rule — writer)** When writing COMPRESSED/COMMONTIME, find the
first index *i* with `time[i] ≥ 0`; if *i* > 0 use *i − 1*. If `i ≥ 500` and `nPoints − i ≥ 500`
(`DBL_BLOCK`) the file is double-blocked with `blockPoints = {i, nPoints − i}`; otherwise
`blockPoints = {nPoints, 0}`.

### 3.4.3 UNCOMPRESSED and CIRCULAR

**FR-SUN-045** UNCOMPRESSED: each parameter's data (including TIME, which is *not* delta-encoded)
is a raw big-endian array starting at `blockPtr[0]`; point *k* is at `blockPtr[0] + k*size`.
**FR-SUN-046** CIRCULAR: the block holds `blockPoints[0]` slots; `blockPoints[1]` is the wrap
position (0 = not yet wrapped). Logical point *k* maps to slot `(blockPoints[1] + k) mod
blockPoints[0]`. Before every read the reader shall re-read header offsets 32..39; if changed it
shall discard cached TIME/UTC indexes and update its counts. If `blockPoints[0] == blockPoints[1]`
the writer is mid-update and the index read shall fail with `ERR_REFRESHTIME` (caller retries later).

### 3.4.4 Time index and slicing

**FR-SUN-050** The reader shall lazily load and cache, per open file, a TIME index (position 0,
float) and, when UTC-referenced access is requested, a UTC index (position 1, must be type `t8`,
else `ERR_TIMEREF`). If a UTC index is unavailable the reader shall fall back to the TIME index by
converting the requested reference UTC into a relative offset (`TIME(refUTC, fileRefUTC)`).

**FR-SUN-051** Point location in a time array shall use binary search with tolerance
`TIME_TOLERANCE = 1e-6` s (`TIME_location`), with modes: −1 (first point ≥ t, within tolerance),
+1 (last point ≤ t, within tolerance), 0 (closest; ties and `t ≥ upper` choose the upper point).
The search operates on **float32** time values compared in double precision. Identical semantics
are required for UTC search (`UTC_location`).
*Acceptance:* unit tests cover edge cases (empty, 1 point, exact hits, between points, beyond ends).

**FR-SUN-052** A read with `stop < start` shall fail with `ERR_TIMESLICE`. A read whose window
contains no points shall fail with `ERR_TIMESLICE`, except that when the search collapses to one
point the read succeeds if that point lies within `TIME_TOLERANCE` of the requested window.

**FR-SUN-053** The returned parameter shall carry: name/units/description (FR-SUN-030), type,
points, a float32 relative-time array (TIME minus `refTIME` when a relative reference offset is
given, or `TIME(UTC[i], refUTC)` when UTC-referenced), the reference used, index of min and max
value, min/max values (double), sequence number, data rate.

**FR-SUN-054** The file data rate for fixed formats shall be computed when the TIME index is read:
locate t = 0 (closest); if not found before the last two points use point 0; rate = `1/(t[j+1] −
t[j])` if positive.

**FR-SUN-055 (invalid-value filtering)** During the min/max scan, for float and double data, any
value that is not a normal number and not zero (i.e. NaN, ±Inf, **subnormals**) and is not already
the dropout sentinel shall be replaced by the dropout sentinel, unless the caller requested the raw
image (`DT_IMAGE`) or filtering is disabled by library option. Dropouts are excluded from min/max.
For integer types min/max are plain comparisons.

**FR-SUN-056 (type conversion)** A caller may request conversion to `f4` or `d8` on read; other
target types are rejected. When converting `t8` TimeTags, the first sample's day is taken as day 0
and later samples are made relative to it before conversion. File-index parameters are never
converted.

---

## 3.5 Data types

| Id string | DT id | Size | Meaning |
|---|---|---|---|
| `"  "` | 0 `DT_IMAGE` | – | raw image (no conversion) |
| `f4` | 1 | 4 | IEEE float32 |
| `d8` | 2 | 8 | IEEE float64 |
| `i2` | 3 | 2 | int16 |
| `i4` | 4 | 4 | int32 |
| `u2` | 5 | 2 | uint16 |
| `u4` | 6 | 4 | uint32 |
| `t8` | 7 | 8 | TimeTag (§3.8) |
| `b1` | 8 | 1 | byte |
| `p4` | 9 | 4 | packed time: bits 31..27 relative day, bits 26..0 milliseconds of day |
| `v1` | 10 | var | variable-size block object (one per block) |
| `u8` | 11 | 8 | uint64 |
| `i8` | 12 | 8 | int64 |
| – | 15 `DT_CLOCKSTR` | – | pseudo-type for parsing clock strings, never stored |

The flag `DT_RETURNUTC = 0x0100` may be OR-ed into a requested type (MULTITIME only) to also
return a TimeTag array.

**Sentinels.** `DROP_OUT = 0x80000001` (as a 4-byte pattern; as float32 it is the negative subnormal
−1.40130e−45, `FP_DROP_OUT`). For doubles the sentinel is `(double)FP_DROP_OUT`. `FP_VARIABLE =
+1.40130e−45` (bit pattern 0x00000001) marks a variable-block placeholder.

**FR-SUN-060** The data model shall support all types above; on-disk TimeTags are stored field by
field (`MilliSeconds` u32, `MicroSeconds` u16, `JulianDay` i16 — in that order, 8 bytes), each field
byte-swapped individually.

**FR-SUN-061** Dropout detection shall compare the **bit pattern** for float32 (`0x80000001`), never
the float value (it is subnormal and compares unequal to many things / may be flushed).

---

## 3.6 MULTITIME / MULTITIME64

Each parameter owns a singly linked list of blocks. `blockPtr` in the map points at the first
block header; a 0 pointer means "parameter declared, no data yet".

### 3.6.1 Block header (`pidHeader`) — on-disk layout, big-endian fields

| Offset | MULTITIME | MULTITIME64 | Field |
|---|---|---|---|
| 0 | u32 | u32 | `points` in this block |
| 4 | u32 | **i64** | `linked`: file offset of next block header, 0 = last |
| 8 / 12 | u32 | u32 | `TimeLzhSize`: compressed byte size of time array (for `v1`: uncompressed object size) |
| 12 / 16 | u32 | u32 | `DataLzhSize`: compressed byte size of data array |
| 16 / 20 | u8 | u8 | `indexType`: DT id of time stamps (1 f4, 2 d8, 6 u4, 7 t8, 9 p4) |
| 17 / 21 | u8 | u8 | `nomenSize`: bytes of optional index nomenclature that follows the header (0 = "TIME") |
| 18 / 22 | u8 | u8 | `compressed`: 1 = LZH (+ delta time), 0 = raw |
| 19 / 23 | u8 | u8 | `bigEndian`: byte order of the **payload** (1 = big, 0 = little) |
| 20 / 24 | 8 | 8 | `firstTime` (interpretation by `indexType`, below) |
| 28 / 32 | 8 | 8 | `lastTime` |
| 36 / 40 | nomenSize | nomenSize | optional index nomenclature (≤ 256 bytes: CSV "name,units,descr") |

Header size: 36 bytes (MULTITIME), 40 bytes (MULTITIME64), plus `nomenSize`.
`firstTime`/`lastTime` encoding (big-endian): `d8` → double; `f4` → float in the first 4 bytes;
`u4` → uint32 milliseconds relative to the file reference in the first 4 bytes; `t8`/`p4` → a
TimeTag (field-wise BE) giving the block's **base UTC** (`blkBaseUTC`).

Immediately after header + nomen: the time array (`points` × size, or `TimeLzhSize` compressed
bytes), then the data array (`points` × size, or `DataLzhSize` compressed bytes).
Time-array element size: `f4`,`u4`,`p4` = 4; `d8`,`t8` = 8.

**FR-SUN-070** The reader shall walk the list from the map pointer, decode each header (converting
first/last times to seconds relative to the file reference UTC: `u4` /1000, `t8`/`p4` via `TIME()`),
and cache the header list per parameter. On later reads of a dynamic file it shall re-check the last
cached header's `linked` field to pick up appended blocks (the last header is re-read).
A header read failure yields `ERR_LINK_HDR_RD`.

**FR-SUN-071 (block selection)** For a requested window `[s, e]` (shifted into file-reference time
by `refTIME`, or by `TIME(refUTC, fileRef)`), the reader shall include the first block with
`s ≤ lastTime && e ≥ firstTime`, and every following block while `e ≥ firstTime`. With
**extrapolation enabled** (library option, script `EXTRAPOLATE`) it shall additionally include the
block preceding the window (lead-in value) and one block beyond (trailing value), then clamp the
first returned time to `s` and set the last point to (`e`, value of the second-to-last point).
If no block matches and the file is dynamic and `s` is after the last block, it shall return the
single last point of the last block (current value); otherwise `ERR_TIMESLICE`.

**FR-SUN-072 (time decoding per indexType)**, after byte-order correction by `bigEndian`:
* `f4`: if compressed, running double sum of deltas (element 0 absolute). Relative seconds.
* `d8`: if compressed, `x[i] += x[i−1]` in double. Relative seconds; UTC derived with `UTC()`.
* `u4`: milliseconds relative to the file reference UTC; never delta-encoded.
* `t8`: if compressed, each field is delta-added to the previous decoded TimeTag *field-wise*
  with natural integer wrap-around (u32 ms, u16 µs, i16 day).
* `p4`: if compressed, uint32 running sum (wrap-around) of deltas; then day = value >> 27
  (5 bits, relative), ms = value & 0x07FFFFFF, µs = 0; when the relative day decreases versus the
  previous sample add 32 to a wrap counter; absolute day = `blkBaseUTC.JulianDay + relDay + wrap`.

Relative float times are then reduced by `refTIME` (if given) and by the delta between a
requested reference UTC and the file reference UTC (for `f4`/`d8`). Decoding of a block may stop
at the first time > `stopTime`.

**FR-SUN-073** Data arrays shall be decompressed (or read raw) for the selected point window only
and byte-swapped iff `bigEndian` differs from the host order.
Note: `v1` object payloads are never swapped — see DEV-SUN-001.

**FR-SUN-074 (variable blocks, `v1`)** A `v1` parameter stores one object per block: a single time
stamp (uncompressed) and an object of `TimeLzhSize` bytes. On a normal read the reader returns one
point per block with value `FP_VARIABLE`; on an image read it returns the object bytes prefixed with
their u32 size, one block only.

**FR-SUN-076 (rebuild change-only data to full rate)** When the library option *rebuild rate* is on
(`DATARATE REBUILD ON`, option 1001) and the parameter's map `seqNum` bit 31 is set (rate packed in the
map or supplied by `[rates]`), a float MULTITIME parameter with rate > 0, ≥ 2 points and
`(tLast − t0)·rate < 536,870,912` shall be expanded ("fluffed") to full rate: step Δt = 1/rate from
t0; whenever `stepTime − t[next] > −Δt` the next real sample is taken (time and value re-synchronised
to it), otherwise the previous value is repeated at the stepped time; UTC array regenerated with
`UTC(..., setMicro=1)` where present; sequence number masked to 16 bits.

**FR-SUN-077 (library options)** The SUN library shall honour the generic option ids (also accepted
as a number via `LIBCOMMAND 0xf0ffffff <id>` — the parsed hex value is compared with the little-endian `SUN_FORMAT` constant 0xF0FFFFFF): 1001/1002 rebuild rate on/off (default off),
1003/1004 script running, 1005/1006 dialogs allowed, 1007/1008 extrapolation on/off (**default on**),
1009/1010 invalid-value filtering on/off (default on), 1011/1012 retained MULTITIME lookup tables
on/off (default off; the application enables it for live files).

**FR-SUN-078 (empty strings)** Text decoding returns a single space `" "` for an
empty field; nomenclature shown/exported shall match (e.g. empty units export as `" "`).

**FR-SUN-075 (appending — writer)** To append a block, the writer writes header+arrays at EOF,
then patches the previous block's `linked` field (or the map pointer if the parameter had no data),
then, unless `MANUAL_UPDATE`, increments header `blockPoints[1]` at offset 36 and flushes. The
parameter count (offset 28) and signature (offset 0) are written **last** when the parameter set is
complete, so a reader never sees a half-written map.

---

## 3.7 LZH codec (ar002 "-lh5-" variant)

The compression is Haruhiko Okumura's **ar002** static-Huffman LZSS. It is a *raw* bit-stream: **no archive header, no CRC, no length prefix,
no end marker**. The compressed length is supplied externally (map arithmetic or pidHeader).

**FR-LZH-001** The codec shall implement ar002 exactly, with
these constants: `DICBIT = 13` (window 8192), `MAXMATCH = 256`, `THRESHOLD = 3`,
`NC = 255 + 256 + 2 − 3 = 510`, `CBIT = 9`, `CODE_BIT = 16`, `NP = DICBIT + 1 = 14`,
`NT = CODE_BIT + 3 = 19`, `PBIT = 4`, `TBIT = 5`, `NPT = 19`; decode tables `c_table[4096]`
(12-bit lookup) and `pt_table[256]` (8-bit lookup) with overflow trees `left/right[2*NC−1]`.
An off-the-shelf LHA library shall **not** be used (stream framing and EOF semantics differ).

**FR-LZH-002 (bit input)** The decoder's bit buffer is 32 bits wide
(`uInt bitbuf`, `BITBUFSIZ = 32`); `getbits(n)` returns the top *n* bits, `fillbuf(n)` shifts in
bytes MSB-first. When the supplied compressed size is exhausted, further input bytes shall read as
**zero** (no error). A `getbits(0)` returns 0.

**FR-LZH-003 (block structure)** The stream is a sequence of Huffman blocks. Each begins with a
16-bit symbol count, then the T-tree lengths (`read_pt_len(NT, TBIT, 3)`, with the special 2-bit
zero-run after index 2), the C-tree lengths (`read_c_len`, run-length codes 0 → 1 zero, 1 → 4-bit+3
zeros, 2 → 9-bit+20 zeros, else len = c − 2), then the P-tree lengths (`read_pt_len(NP, PBIT, −1)`).
A count of 0 in any length table means "single symbol" whose value follows. Symbols < 256 are
literals; symbol *c* ≥ 256 is a match of length `c − 253` with distance from `decode_p` (+1).
`make_table` shall fail with `ERR_LZWDICT` ("corrupt dictionary table") when the code space does not
sum to 2^16.

**FR-LZH-004 (windowed decode)** `lzh_decode(buf, strb, stpb, compressedSize)` decodes from the start
of the stream, discards output bytes with index < `strb`, stores bytes `[strb, stpb)` into `buf`,
and stops as soon as `stpb` bytes have been produced. The copy-in-progress state (`j`) and the copy
source index (`static i` inside `decode()`) persist between `decode()` calls and **must** be
instance fields (one codec instance per decode; no static state; thread-safe).

**FR-LZH-005 (encoder)** The encoder (binary-tree match finder with `PERCOLATE`, 8 KiB output
buffer, `send_block` when `output_pos ≥ bufsiz − 24`, final `putbits(7, 0)` flush) shall produce
streams that any ar002 decoder reads back correctly. Byte-identical output with other ar002 encoders
is **desirable but not required**; round-trip with the decoder is required. The encoder returns the compressed byte count.

**FR-LZH-006** The decoder shall never read beyond `compressedSize` bytes from the underlying
stream and shall never write outside the destination buffer even for corrupt input (fail with
`ERR_LZWDICT` or `ERR_LZWREADFAIL` instead).

*Acceptance (LZH):* TST-LZH-001 decodes every block of the fixture set and compares with golden
ASCII exports; TST-LZH-002 round-trips 10,000 random buffers (0 B…1 MiB, including
highly-repetitive and incompressible data); TST-LZH-003 fuzzes the decoder with truncated/garbled
streams and requires no exceptions other than the defined error codes.

---

## 3.8 Time arithmetic (TimeTag ↔ relative seconds)

`TimeTag = { u32 MilliSeconds (since midnight), u16 MicroSeconds, i16 JulianDay }`.

**FR-TIM-001 `TIME(t, ref)`** — relative seconds of TimeTag *t* from reference *ref*:
`dayDiff = t.Day − ref.Day; if |dayDiff| ≥ 180 then dayDiff += (dayDiff > 0 ? −365 : +365);`
`result = dayDiff*86400 + ((t.ms + t.us/1000) − (ref.ms + ref.us/1000)) / 1000` (double).
No leap-year handling (by design: year roll-over heuristic).

**FR-TIM-002 `UTC(offset, ref, setMicro)`** — TimeTag at `offset` seconds from *ref*:
clamp |offset| to 15,552,000 s (180 days); whole days → `jdayOffset` (signed); remainder
`+ 0.0000005`; `ms = (int)(rem/0.001)`; `us = (int)(fmod(rem, 0.001)*1e6)`; if `!setMicro` and
`us > 500` round ms up. Negative offsets borrow from the day (and from ms when subtracting µs).
Positive results normalise µs ≥ 1000 and ms ≥ 86,400,000. Finally add `jdayOffset`; if day < 1 add
365; if day > 365 take `day % 365`.
*Acceptance:* property tests confirm `TIME(UTC(x, r, true), r) ≈ x` within 1 µs for |x| < 180 days
and that the day wrap rules hold on a table of edge cases (TST-TIM-001).

**FR-TIM-003** Relative times inside parameters are float32. Time comparisons such as slice
clamping (FR-SUN-071) and array searches shall be made in float32 so that point selection is
exactly reproducible. Higher-precision internal storage may be added
(see DEV-SUN-002) but shall not change which points are selected.

---

## 3.9 Comment block

The bytes between the end of the map and the first data block (or EOF when there are no data /
`items == 0`) form a free-text, INI-like **comment block** (max 64 MiB honoured).

**FR-SUN-080** The reader shall expose the comment block as text and parse these sections
(section names are case-sensitive, found with substring search for the literal header):

| Section | Line format | Semantics |
|---|---|---|
| `[events]` | `NAME=ddd/msec[.usec]` or `NAME=[ddd/]hh:mm:ss.mmm` | Named UTC events. `;` starts a comment. Max 4096 events. Day outside 1..366 → name suffixed `(invalid)`. Stops at next line starting with `[`. |
| `[rates]` | `pos,name,Hz` | MULTITIME only. `pos < 0` ⇒ look up by name. Sets parameter rate = |Hz| and marks rate as rebuildable (sets seq bit 31). Stops at first invalid record or `[`. |
| `[extendednomen]` | `pos,name,description` | Adds a third (extended) name and description to map position `pos` (first definition wins). |
| `[script]` | Script lines | Embedded script (FR-SCR-001, FR-SCR-109). Terminated by any line starting with `[`. |
| others (`[history]`, `[libraries]`, …) | free text | Preserved, displayed in file details, not interpreted. |


**FR-SUN-081** On refresh of a dynamic file, events shall be re-read; the refresh result shall flag
"events added" when the count changed (see FR-RT).

---

## 3.10 Error codes

The managed library shall expose fixed numeric error codes and messages (used in user-visible
messages and script logs). Values are sequential from 0 in this order:

`0 ERR_SUCCESS "OK"`, `ERR_FILEOPEN "file open error"`, `ERR_UNKNOWN_FMT`, `ERR_MEMALLOC1`,
`ERR_MEMALLOC2`, `ERR_FILESIG "invalid file signature"`, `ERR_MEMALLOC3`, `ERR_MEMALLOC4`,
`ERR_MEMALLOC5`, `ERR_MEMALLOC6`, `ERR_MEMALLOC7`, `ERR_MEMALLOC8`, `ERR_PIDNOTFOUND "name not found
in file map"`, `ERR_TIMEERROR "time array error"`, `ERR_CONVPTR`, `ERR_MEMALLOC9`, `ERR_MEMALLOC14`,
`ERR_NULLFILE`, `ERR_TIMEREF`, `ERR_MEMALLOC10`, `ERR_BADREF`, `ERR_COMREAD`, `ERR_MEMALLOC11`,
`ERR_COMREADFAIL`, `ERR_MEMALLOC12`, `ERR_TIMESLICE "data not available for selected time slice"`,
`ERR_REFRESHTIME`, `ERR_MEMALLOC15` (=27), `ERR_LZWDICT` (=28), `ERR_LZWREADFAIL`,
`ERR_FILTOPEN`, `ERR_STREAMFIND`, `ERR_PARMFIND`, `ERR_MEMALLOC13`, `ERR_PARMFILT`,
`ERR_UTCMEMALLOC`, `ERR_FMTNOTSUPP`, `ERR_UTCNOTAVAIL`, then the writer codes `WRTSTRALLOC`,
`WRTFILEXISTS`, `WRTOPENERR`, `WRTFWRCOMBLK`, `WRTFSKCOMBLK`, `WRTCLOSERR`, `WRTNOTENOPARMS`,
`WRTINVALIDIDX`, `WRTPARMFILRST`, `WRTHEADERWRT`, `WRTHEADERFSK`, `WRTFILEFULL`,
`WRTINVALIDFORMAT`, `WRTMISSINGIDX`, `WRTBUFFERALLOC`, `WRTINVALIDARGS`, `WRTSEEKFAILED`,
`WRTNOTVALID`, `WRTZEROPOINTS`, then `ERR_FILETYPE`, `ERR_NOARCHIVE`, `ERR_FMT_NO_SUP`,
`WRT_READONLY`, `WRT_REVNOMATCH`, `WRT_NOT_REVISING`, `ERR_LINK_HDR_RD`, `ERR_UNDEFINED` (last).

**FR-SUN-090** The enumeration and message table are defined in
`src/Multiplot.Plugins.Abstractions/SunError.cs`; numeric values must never change. They are an `enum
SunError` plus a `DataAccessException(SunError)`; "non-fatal" codes (`ERR_MEMALLOC10/12`,
`ERR_UTCNOTAVAIL`) are reported as warnings on a successful result instead of exceptions.
Codes `≥ ERR_LZWDICT` during a read shall discard the partially read parameter.

---

## 3.11 Writer API (needed by Export-to-SUN and Import)

**FR-SUN-100** The managed library shall provide a writer equivalent to `createSunFile` /
`addSunParm` / `closeSunFile` supporting at least: SUN_COMPRESSED (fixed map), SUN_COMMONTIME,
SUN_MULTITIME and SUN_MULTITIME64 output; options `LZH_COMPRESS`, `PACK_TIMETAG`,
`ORIENT_LITTLE_END | ORIENT_BIG_END | ORIENT_NATIVE`, `MANUAL_UPDATE` (+ `showFileUpdated`);
create modes CREATE / OVERWRITE / UPDATE (revise: append blocks to an existing MULTITIME file) /
HEADERONLY; a comment block of declared size written between map and data; parameter 0 must be
TIME `f4` for COMPRESSED/COMMONTIME (`WRTINVALIDIDX` otherwise).
*Acceptance:* every file written by the writer re-opens with identical values (TST-SUN-010).

**FR-SUN-101** The writer shall write the parameter count and signature last (FR-SUN-075) so that
concurrent readers never see an inconsistent map.
