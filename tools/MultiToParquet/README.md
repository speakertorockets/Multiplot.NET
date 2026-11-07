# multi2parquet

Converts SUN_MULTITIME and SUN_MULTITIME64 `.multi` files, including the `_SRT` variants, to compressed
Apache Parquet. The output layout is described in [docs/parquet-archive-format.md](../../docs/parquet-archive-format.md).

```
dotnet build tools/MultiToParquet -c Release
multi2parquet convert Test1.multi                     # writes Test1.parquet next to the input
multi2parquet convert in.multi out.parquet --level fastest --force
multi2parquet info in.multi                           # header, events, per-parameter blocks/points/time range
multi2parquet verify in.multi out.parquet             # re-reads the Parquet file and compares every sample bit for bit
multi2parquet crosscheck in.multi                     # compares the converter's reader with the application's SUN reader
multi2parquet synth test.multi [32]                   # writes a synthetic file covering every index type
```

| Option | Default | Notes |
|---|---|---|
| `--compression zstd\|snappy\|gzip\|none` | zstd | |
| `--level fastest\|optimal\|smallest` | smallest | test_large.multi (125 MB): `smallest` takes 7.3 min and gives 73.5 MB (59 %); `fastest` takes 53 s and gives 114 MB (91 %) |
| `--value-encoding plain\|split\|dictionary` | plain | `split` (BYTE_STREAM_SPLIT) was larger on the sample data |
| `--row-group N` | 1,048,576 rows | |
| `--year N` | header `refTime[0]` | Overrides the year when the header year is missing or wrong |

On the `synth` file, `crosscheck` returns a nonzero exit code and reports a MISMATCH for `D8IDX`.
That is a bug in the application, not in the converter. `TimeMath.Utc` wraps Julian day 366 with `% 365`,
so the application's own reader shifts leap-day `d8`-index time stamps by exactly one day.

The tool reuses `Multiplot.Sun` for the header, map, comment block, events, rates and the LZH decoder.
It walks the data blocks itself so that time stamps keep their full stored precision. The application
read path returns float32 relative seconds, and it adds display points (EOF hold, clamping) that are not
samples. The tool holds the whole file in memory, at about 16 bytes per sample (about 6 GB for
test_large.multi).
