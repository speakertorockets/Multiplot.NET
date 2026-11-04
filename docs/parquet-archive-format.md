# Multiplot Parquet archive format (`multiplot.timeseries.wide/1`)

This is the Apache Parquet layout that Multiplot.NET uses to archive SUN_MULTITIME / SUN_MULTITIME64 data.
`tools/MultiToParquet` (`multi2parquet.exe`) writes it. Any Parquet reader can open it, for example pyarrow,
pandas, DuckDB, Spark or Parquet.Net.

## Columns

| Column | Parquet type | Nullable | Content |
|---|---|---|---|
| `time_ticks` | INT64 (no logical type) | no | .NET ticks: 100 ns intervals since 0001-01-01T00:00:00Z (`DateTime.Ticks`, UTC). Rows are sorted ascending by this column. |
| one column per parameter | DOUBLE | yes | The parameter's value at `time_ticks`. |

* The column name is the parameter's primary name (`name1` in the SUN map). If two parameters have the
  same name, or a parameter is named `time_ticks`, the later one gets the suffix `_2`, `_3`, and so on.
  The `multiplot.parameters` metadata (below) maps each column back to its map position.
* **null** means the parameter has no sample at that row's time. Each parameter keeps its own time
  stamps and rate. A row is the union of all parameters that sampled at exactly that tick. When a
  parameter repeats a time stamp, the repeat goes into the next row, which has the same `time_ticks`.
* **NaN** is a SUN *dropout*: a sample exists but is invalid (bit pattern `0x80000001` / `FP_DROP_OUT`).
  This keeps it apart from "no sample".
* All numeric SUN types are stored as DOUBLE. Values of `f4`, `i2`, `u2`, `i4`, `u4` and `b1` are exact.
  `i8` and `u8` are exact only up to 2^53, and the converter warns about any value above that.
  The original type id is kept in the metadata.
* These types are not exported, and the converter lists them in `multiplot.skipped`:
  `v1` (variable-size blobs), `t8` used as a data value (not as the index), and image/`p4` data.
* Encodings: `time_ticks` uses DELTA_BINARY_PACKED and the value columns use PLAIN. Pages are compressed
  with Zstandard at the smallest-size level by default (see `--compression` and `--level`).

## File key/value metadata

Every key starts with `multiplot.`. Values are strings.

| Key | Content |
|---|---|
| `schema` | `multiplot.timeseries.wide/1` |
| `time.column`, `time.unit` | `time_ticks` and a description of the unit |
| `source.file`, `source.format`, `source.signature` | Original file name, format (`SUN_MULTITIME64` …) and signature (`0xF4FFFFFF`) |
| `header.refTime` | The six header reference fields: `year,julianDay,hour,minute,second,millisecond` |
| `header.refTicks` | The header reference time as ticks |
| `reference.ticks`, `reference.iso` | The effective relative-time zero: the header reference, or the `FILE_REF` event when one is present |
| `header.items`, `header.blockPoints`, `header.cutoff` | Header counters and the engine cutoff |
| `header.title`, `header.comment`, `header.serialNum`, `header.testStand`, `header.testPosition`, `header.testType`, `header.testNumber` | Header text fields. Empty fields are stored as `""`. |
| `comment` | The comment block, verbatim, up to the first NUL. Its `[events]`, `[rates]`, `[extendednomen]` and application sections such as `[coef]` keep their SUN meaning. |
| `parameters` | JSON array with one object per map entry, in map order: `mapPosition`, `column` (null if not exported), `name`, `altName`, `extName`, `description`, `extDescription`, `units`, `typeId`, `rate` (−1 unknown, 0 variable, > 0 Hz), `sequenceNumber`, `indexNomen` |
| `events` | JSON array of `{name, julianDay, milliSeconds, microSeconds, ticks}` parsed from `[events]` (`ticks` is null for an invalid day) |
| `skipped` | One line per parameter that was not exported, with the reason |
| `converter` | Name and version of the tool that wrote the file |

## Time conversion

SUN time stamps carry no year. The converter takes the year from header field `refTime[0]`, or from the
`--year` option when the header year is not usable. It then places each stamp in that year or a neighbouring
one with the same ±180-day rule that `TimeMath.Time` uses:

| SUN index type | Conversion to ticks |
|---|---|
| `f4` relative seconds | effective reference + seconds (the float32 value, as stored) |
| `d8` relative seconds | effective reference + seconds |
| `u4` milliseconds | effective reference (whole milliseconds) + ms |
| `t8` TimeTag | year + (Julian day − 1) days + ms + µs |
| packed time (`t8` + PACK) | as `t8`, with the 5-bit day relative to the block's first time stamp; microseconds are 0 |

The converter writes only the samples that are stored in the file. It never adds the points that the
plotting read path creates for display: the EOF hold value, start/stop clamping, or the full-rate rebuild.

## Reading in Multiplot.NET

The bundled `Multiplot.Parquet` plugin reads these files, and also generic Parquet files with a TIMESTAMP,
INT64 ticks or relative-seconds time column (see the user guide, *Plugins*). It behaves like the MULTITIME
reader, with these differences:

* GMT tags are built directly from the absolute time. A leap-year day 366 is therefore correct, while the SUN
  reader can be one day off for `d8` time indexes (OQ-017). All differences are recorded as DEV-PLG-002.
* The EOF hold point is added for every parameter. The SUN reader skips it for `f4`-indexed parameters.
* Min/max cover the points returned. The SUN reader also counts the original value of a clamped trailing point.
* *Rebuild data rate* (options 1001/1002) is not applied.
* A full read of one parameter reads the time column and that column of every row group. On test_large this
  takes about 3 s per parameter. Windowed reads only touch the row groups that overlap the window.

## Not representable / caveats

* Column names keep the SUN names as they are, dots included (`ftms1b.pccc1`). In Spark or DuckDB SQL,
  quote such names (`"ftms1b.pccc1"`, or backticks in Spark).
* With many parameters that do not share time stamps, the wide layout has about one row per sample, and
  most of the cells are null. Nulls take almost no space: test_large.multi has 372 M samples in 136 M rows
  and still comes out smaller than the SUN file.

* A data set that spans more than about 180 days, or a file without a valid header year, needs `--year`.
  Even with it, results across a year end follow the ±180-day rule.
* Packed time cannot cross a year end. The day field is relative and only 5 bits wide, so a block written
  across Dec 31 decodes to days after 366. The converter exports these values as decoded and warns. The
  application reader produces the same values.
