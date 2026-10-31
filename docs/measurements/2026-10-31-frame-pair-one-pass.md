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

## Environment, Result, Verdict

Not run yet.
