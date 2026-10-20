# 84. The particle caller fixes, played rather than sought (2026-10-20, B557)

The owner, 2026-10-20: *"if something needs me to see it, that is a golden image comparison test, or at least you giving
me a SSS and comparison shot to look at"*. Finding 82's explosion pair for `fix/particle-caller-parity` was
byte-identical. That is not because nothing changed: a `--shot` reaches its tick by SEEK, and a seek replays from each
effect's own tick, so the creation-frame, sleep and kill paths never ran in it. This is the comparison done by playing.

## The instrument, and its control

Two script-only variables, now in `--help`. **`TF2VIEW_FIXED_FRAME="0.0125"`** makes playback advance a fixed step
every frame, so two runs show the same moments. **`TF2VIEW_PLAY_SHOTS="<folder> tick ..."`** works with
`--autoplay --measure` and captures each listed tick, with its sidecar, on the first frame PLAYED at or past it. The
`pair-diff` probe compares two folders pixel by pixel. With `--sbs` it writes main | branch | |difference| × 8.

**The control: main against main.** I made two first-person sweeps of 14 ticks (30050 to 31350, every 100) and two
free-camera sweeps of 14 ticks (18050 to 19350). Most ticks are byte-identical. The same few differ every time, and
always inside one box: (37,441)-(408,538) at 18150, 18250 and 30050, with up to about 9,000 pixels at 30150/30350/31050.
That is the noise floor, and it never reaches the regions below. Builds were main `e05064db` with the instrument
commit cherry-picked (`scratch/particle-diff-main`), and the branch at `55deabf5`.

## First person, main against the branch (both runs clean)

| tick | pixels differ | max channel Δ |
|---:|---:|---:|
| 31250 | 181,016 | 153 |
| 31050 | 164,887 | 210 |
| 31350 | 125,700 | 176 |
| 30850 | 43,899 | 155 |
| 30950 | 42,561 | 158 |
| 31150 | 30,453 | 155 |
| 30150 | 28,131 | 132 (noise box included) |
| 30750 | 23,289 | 76 |
| 30350 | 18,384 | 52 |
| 30550 | 15,958 | 79 |
| 30650 | 13,129 | 106 |
| 30450 | 10,454 | 80 |
| 30250 | 1,855 | 68 |
| 30050 | 0 | 0 |

## Free camera at the tick-18348 explosion, main against the branch

The main run overlapped another agent's build, so it is void by the hygiene rule. A second main run, also under load,
agreed with it pixel for pixel outside the noise box.

| tick | pixels differ | max Δ |
|---:|---:|---:|
| 18350 | 366,621 | 200 |
| 18450 | 145,916 | 196 |
| 18550 | 90,457 | 86 |
| 18750 | 65,937 | 191 |
| 18850 | 23,321 | 158 |
| 19350 | 11,472 | 62 |
| 18950 | 7,334 | 66 |
| 18650 | 1,713 | 2 |
| 19050 | 348 | 23 |
| 18050, 19150, 19250 | 0 | 0 |
| 18150, 18250 | about 9,000 | the noise box only |

## What differs, and which row

**Every difference is an effect made while playing, one frame younger on the branch.** The tick-18348 rocket explosion
two ticks in has a smaller, less spread fireball, and its light tints the whole view less. Explosions in front of the
recorder at 31050 and 31250 have their flash and streaks a frame behind. The rocket-jump smoke and heal-beam particles
at 30850 and 30950 sit a frame apart along their paths. By eye the two sides look alike apart from the fireball's
size at 18350.

**Attribution: row 6 (the creation frame).** I built the branch with row 6's two commits reverted
(`scratch/particle-diff-no-row6`). It matches main pixel for pixel at all 14 first-person ticks outside the noise box,
so rows 1, 3, 9 and 17 change nothing visible in that window. The free-camera attribution is inferred from that run, not
run separately.

**Not added: a regression test.** The difference is a frame's age, which `ParticleCallerConformanceTests` already pins
(a burst first offered half way through a tick is age 0 on that frame).

**Images** (`images/84/`, each with its `.png.json`): `free-18350-main-branch-diff.png`, `fp-31250-main-branch-diff.png`,
`fp-31050-main-branch-diff.png`, and the control `fp-30850-control-main-norow6.png` (identical).
