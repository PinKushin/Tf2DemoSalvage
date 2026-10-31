# Frame-pair one-pass decode: before and after (2026-10-31)

**Question.** Does decoding each pair of keyframes in one pass (`perf/frame-pair-one-pass`, `38ff5841` + `534705f9`; Valve
`bone_setup.cpp:413-446, 525-530`) lower the roots' `base` and `layer sampling` parts of the `roots split` line, with output
unchanged? Output identity is held by `FramePairDifferentialTests` (bitwise, 711,568 + 102,516 real-model samples), not by this
record.

**Before.** `2026-10-31-setup-parts/01.txt`..`03.txt` (same demo, tick, arguments, build configuration, window condition and
instrument): roots 0.565 / 0.567 / 0.692; base 0.208 / 0.208 / 0.245; layer sampling 0.131 / 0.130 / 0.159; `anim` 0.219 /
0.225 / 0.264; children 0.052 / 0.055 / 0.069.

**After.** Three runs of the setup-parts command (`2026-10-31-setup-parts.md` Method), each built in the command that runs it,
into `2026-10-31-frame-pair-one-pass/`, summarized with `2026-10-31-setup-parts/summ.ps1`.

## Predictions (made before any run; all predictions, from the implementing agent's reading and mine)

| # | Prediction | Falsified if |
|---|---|---|
| 1 | `base` + `layer sampling` drop by 0.04-0.10 ms per root rebuild (about 15-30% of the 0.34 before) | the drop is below 0.02 or above 0.17, in the three-after vs three-before means |
| 2 | `blend`, `loop`, `ik`, `adjust`, `finish` stay within the before-runs' own spread (about ±0.01 each) | any moves by more than 0.02 |
| 3 | `anim` drops by about the same amount as `base` | `anim` moves by less than half of `base`'s move |
| 4 | roots total drops by the same amount | roots moves by less than 0.7 of the `base` + `layer sampling` move |
| 5 | layer samples per rebuild unchanged (12.0-12.7) and roots built/eligible unchanged | either moves by more than 1 (the runs are void) |

Why not the read's 30-50%: the blend arithmetic `a*(1-s)+a*s` is not bit-equal to `a`, so equal keys are still blended; the
dense seeding and the grid blends stay; the second Euler conversion stays whenever the keys differ.

**Controls and void conditions** are the setup-parts record's: `prepose 0 over 0`, roots built 8-21, eligible 4-7, the parts plus
`unsplit` sum to the roots total, `unsplit` ≥ 0 and ≤ 0.05, layer sampling ≤ accumulate. The audit's caveats (setup-parts
record) apply unchanged.

## Result (merge `43d37479`, before the review-fix batch)

Three runs into `2026-10-31-frame-pair-one-pass/` (driver `all.sh`, reader `2026-10-31-setup-parts/summ.ps1 -Dir`), off
screen, no void condition (`prepose 0 over 0`; roots built 12.8-13.7; eligible 4.6-4.9; samples 11.7-12.6; all `.err` empty).
Means per run over the `moment cost` lines with the first left out (n = 16, 14, 11), ms per rebuild, beside the before runs:

| part | before 01 / 02 / 03 | after 01 / 02 / 03 | mean before → after |
|---|---|---|---|
| base | 0.208 / 0.208 / 0.245 | 0.164 / 0.194 / 0.233 | 0.220 → 0.197 |
| layer sampling | 0.131 / 0.130 / 0.159 | 0.099 / 0.124 / 0.141 | 0.140 → 0.121 |
| base + layer sampling | 0.339 / 0.338 / 0.404 | 0.263 / 0.318 / 0.374 | 0.360 → 0.318 |
| roots total | 0.565 / 0.567 / 0.692 | 0.482 / 0.589 / 0.694 | 0.608 → 0.588 |
| `anim` | 0.219 / 0.225 / 0.264 | 0.169 / 0.200 / 0.245 | 0.236 → 0.205 |
| blend | 0.071 / 0.079 / 0.099 | 0.073 / 0.091 / 0.112 | 0.083 → 0.092 |
| loop | 0.039 / 0.040 / 0.051 | 0.039 / 0.050 / 0.065 | 0.043 → 0.051 |

Both sets show the same shape across runs (the third run is the slowest in each): run-to-run spread, about 0.1 ms in roots, is
larger than the effect, so a three-vs-three mean carries that spread.

**Predictions against the result:**

| # | Prediction | Result |
|---|---|---|
| 1 | base + layer sampling drop 0.04-0.10 (falsified below 0.02) | holds at the bottom of the range: 0.042 (position by position: 0.076, 0.020, 0.030) |
| 2 | blend, loop, ik, adjust, finish within ±0.02 | holds (blend +0.009, loop +0.008, ik/adjust/finish ≤ +0.004) |
| 3 | `anim` drops by about `base`'s move | holds: `anim` −0.031 vs base −0.023 |
| 4 | roots total drops by ≥ 0.7 of the base + layer sampling move (0.029) | **falsified**: roots −0.020 (position by position −0.083, +0.022, +0.002), the blend and loop rise ate part of it |
| 5 | samples, built, eligible unchanged (±1) | holds |

## Verdict

Direction is consistent: base and layer sampling are lower in all three runs, 6 of 6 comparisons, and `anim` follows. The size
is small: base + layer sampling −0.042 ms per root rebuild (−12% of the sampling cost, −7% of the roots total), at the
bottom of the predicted range, and the roots total moved less than predicted (−0.020, inside the run spread). Three runs per
side cannot separate the roots-total move from noise; the sampling parts are the stronger evidence. Reported as a small win
(above the 0.01 ms bar), not as a large one. Remaining in setup: base + layer sampling 0.32, accumulate remainder 0.077,
blend 0.09. The review's fixes land in a later commit; if they change production code, the effect is re-checked.
