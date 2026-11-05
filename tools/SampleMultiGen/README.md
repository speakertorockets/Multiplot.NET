# samplemultigen

Writes a synthetic SUN_MULTITIME64 `.multi` test file through the native SunAcc write library
(`native/SunAcc42.dll`, the 64-bit Release build of SunAcc v4.2.7.2 copied from `Winplot/AccessLibs/SunAcc/Release64`).
Because the file comes from the original C library, it also tests the managed `Multiplot.Sun` reader against the reference writer.

```
dotnet build tools/SampleMultiGen -c Release
samplemultigen                                   # writes datafiles/sample.multi: today, 12:00:00, 4 h at 100 Hz
samplemultigen out.multi --date 2026-11-05 --start 12:00:00 --hours 4 --rate 100 --block 60 --seed 12345
samplemultigen out.multi --32                    # SUN_MULTITIME (32-bit addresses) instead of SUN_MULTITIME64
samplemultigen out.multi --no-compress           # store blocks uncompressed
```

Every parameter is `f4` data with a `t8` TimeTag index and LZH compression. Blocks hold `--block` seconds of data and are
interleaved across parameters the way a real-time recorder appends them (240 linked blocks per parameter by default).
The comment block holds hourly `[events]` and a `[rates]` table. The random channels use a fixed seed, so a given set of
arguments always produces the same file.

| Parameter | Units | Signal |
|---|---|---|
| SINE | V | 0.2 Hz sine, 10 V amplitude |
| STAIR | level | One step per minute, 0 → 10 → 0 (20 minute cycle) |
| IMPULSE | g | Single-sample spikes, 5–50 g, random sign, 1 per 5 s on average |
| NOISE | mV | Gaussian white noise, mean 0, sigma 1 |
| RAMP | s | Continuously increasing (elapsed seconds) |
| SAWTOOTH | V | 10 s period, −5 to +5 V |

Check a generated file with the Parquet tool:

```
multi2parquet info datafiles/sample.multi
multi2parquet crosscheck datafiles/sample.multi
```

## Real weather data: `midc`

```
samplemultigen midc                                          # downloads 2025-07-01..07-08, writes datafiles/weather.multi
samplemultigen midc out.multi --begin 20250101 --end 20250108
samplemultigen midc out.multi --csv bms.csv                  # convert a previously downloaded API file
```

Downloads 1-minute data from the MIDC raw data API (`https://midcdmz.nlr.gov/apps/data_api.pl?site=BMS&begin=...&end=...`)
for the Solar Radiation Research Laboratory Baseline Measurement System in Golden, Colorado. MIDC is run by the
National Laboratory of the Rockies (NLR), which was NREL until December 2025. The old `midcdmz.nrel.gov` host no longer resolves.
Each day of 1-minute data gives 1,440 samples per channel, so 7 days or more gives at least 10,000. The default 8 days gives 11,520.
Times are MST (UTC-7), as published. Missing values (≤ −999) and night-time cloud cover (−1) are stored as `FP_DROP_OUT`.
Each day is one block. There is one event per midnight, and a `[source]` section in the comment block records the URL and DOI.
Cite the dataset as DOI [10.7799/1052221](https://doi.org/10.7799/1052221).
The station pressure sensor failed on 2025-12-14, so check pressure data for date ranges after that.

| Parameter | Units | MIDC column |
|---|---|---|
| TEMP, DEWPT, RH | °C, °C, % | Tower Dry Bulb Temp, Tower Dew Point Temp, Tower RH |
| PRESS, SLP | mbar | Station Pressure, Sea-Level Pressure (Est) |
| WSPD, WDIR, WGUST | m/s, deg, m/s | Avg Wind Speed / Direction, Peak Wind Speed @ 19 ft |
| WSPD33, WDIR33 | m/s, deg | Avg Wind Speed / Direction @ 33 ft |
| PRECIP, PRECIPACC | mm | Precipitation, Precipitation (Accumulated) |
| GHI, DNI, DHI | W/m² | Global CMP22, Direct NIP, Diffuse CM22-1 |
| GHICLEAR | W/m² | Global SAMPA/Bird (calc): clear-sky model, overlay with GHI to see clouds |
| CLOUD | % | Total Cloud Cover |
| EFIELD | kV/m | Atmospheric Electric Field: thunderstorm activity |
| ZENITH, AZIMUTH | deg | Solar position |

## Notes

Add channels in `Signals.cs`, or in the `Channels` table in `Midc.cs`. The parameter count passed to `createSunFile` follows the array length.
Call header functions (`h_set*`, `setCommentText`) before the first `addSunParm`: in 64-bit mode `h_setRefTime`
restores the file position from an uninitialized 32-bit variable.
