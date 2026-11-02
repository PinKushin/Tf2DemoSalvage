# Measurement 2026-11-02: what the iklock-compose reductions save per root rebuild

## Record (written and committed with the change, before any run)

Nothing below has been run in the viewer. Every number under `## Predictions` is a **[pred]**, fixed before the lead's run. The
implementer did not run `--measure` (the desktop is shared); the lead does, with the command below.

## Question

`perf/iklock-compose` makes three parity-safe reductions to `SkeletonPose.Build`'s accumulate and its lock brackets
(`2026-10-31-iklock-bracket-read.md` §4, `2026-10-31-accumulate-parts.md`). How much does each save per root rebuild, in the
`roots split` line's `accumulate`, `compose`, `capture` and `solve`, and is the roots total still within the run-to-run spread?

The commits, in order, each its own record in git:

1. **Indexed loops** (`perf: index the pose build's interface loops`): every `foreach` over an `IReadOnlyList` in
   `SkeletonPose.Accumulate`, `Adjust`, `SpinBarrel`, `ReachWithIk` and `IkContext.Solve` becomes an indexed `for`. That is the
   setup-bones read's M6: 21.4 counted enumerators a rebuild in `Accumulate` alone (`2026-10-31-b624-cost.md`), plus uncounted
   ones in `Adjust` (controllers), `ReachWithIk` (chains and links) and `IkContext.Solve` (rules). The pose is identical by
   construction (the same elements in the same order). Held by `PoseBuildDifferentialTests` against a frozen copy of the
   path at `55484e1f`, and by an allocation test of the layered path: 464 bytes per build before, 0 after; restoring one
   `foreach` (the controllers) as a sabotage brought back exactly 32, one array enumerator.
2. **No diagnostic re-walk after a successful lock** (`perf: carry a lock's travel from the solved matrix`): `IkLocks.Solve`
   dropped the `Reset` + 4-bone `Build` after each success (8 of the 15 bone builds in a successful two-lock solve, M1), and
   carries `travelled` from the slammed end matrix, Valve's `boneToWorld[bone]`. **This is also a latent parity fix**: Valve's
   `SolveSequenceLocks` shares one `CBoneBitList` across every `SolveLock` of a sequence (`bone_setup.cpp:4301-4309`), so a
   later lock whose chain hangs below an earlier lock's end bone reads that bone's SLAMMED matrix; the mid-solve `Reset` made
   ours read a re-concatenation instead (after a success only). Inert on TF2's players by census (`iklock-census`: 814 lock
   lists, all rfoot then lfoot at weights 1/0, no chain locked twice, no walk through an earlier lock's links), and the
   nine-class differential is bit-identical. `Moved`/`FurthestMove` keep their meaning ("of the successful solves,
   effector travel") but now measure to the solved matrix rather than to its local-space round trip: a last-bits change of
   route (B243), compared with a tolerance in the differential.
3. **The array read and the unzeroed walk buffer** (`perf: read a lock's dense pose by index, unzeroed walk buffer`):
   `BoneChain.Build` reads a `StudioBonePose[]` pose (what every lock bracket passes) by index instead of twice through
   the interface per bone (M2), and carries `[SkipLocalsInit]`, so the 512-byte walk buffer and the 12-float local are
   not zeroed before being written whole (M3). Bit-identical by construction; a sabotaged array read reddened both
   differentials, which also shows production takes the array path. **Rank 4 (resolve each lock's chain once per
   bracket) was left**: predicted at most 0.0005 ms, and it changes the lock-list read counts that `AccumulatePartsTests`
   uses as its timing controls.

## Before

The numbers the task started from, `2026-10-31-accumulate-parts.md`'s Result (before B624): accumulate remainder **0.065** ms
per root rebuild = compose 0.035, solve 0.016, capture 0.008, seed 0.003, other 0.004; **18.4** enumerators per rebuild.

The nearest measured "before" WITH B624 is `2026-10-31-b624-cost.md`'s **after** (merge `d67f496d`), ms per root rebuild:

| part | mean |
|---|---|
| roots total | 0.530 |
| layer sampling | 0.123, over 15.9 samples |
| compose | 0.042 |
| capture + solve | 0.028 |
| seed / other | 0.004 / 0.004 |
| ik | 0.033 |
| enumerators | 21.4 |
| roots built / eligible | 13.4 / 4.9 |

**B622, B633 and B634 have landed since**, so that table is not a clean before.

## Protocol: two sets

- **before** — the pushed tree at `6e8f2f47`, measured by the lead with its own copy of `all.sh` in the main tree, folder
  `before/`.
- **after** — the same tree with this branch (`perf/iklock-compose` and `perf/iklock-compose-2`) merged, folder `after/`.

The predictions below are moves from `before` to `after`. The three commits are NOT measured one by one: their per-commit
predictions are kept so the after set's split can be read against them, but only the sum is a measured move, and only the
`solve` drop (2a) is expected to stand clear of one set's spread. **The `enumerators` column is gone from the after set**: it
was removed end to end (`perf/iklock-compose-2`), so prediction 1a is moot and `summ.ps1` reads `other` from both line shapes.

## Method

`2026-11-02-iklock-compose-cost/all.sh`: the setup-parts command (`2026-10-31-setup-parts.md` Method), three runs, each built in
the command that runs it, read with `2026-10-31-setup-parts/summ.ps1 -Dir`. It measures the checkout it sits in (three folders
up) and takes the set's folder as its argument.

```bash
# Usage: all.sh before|after
repo="$(cd "$(dirname "$0")/../../.." && pwd)"
out="$repo/docs/measurements/2026-11-02-iklock-compose-cost/${1:?before|after}"
mkdir -p "$out"
for n in 01 02 03; do
  MSBUILDDISABLENODEREUSE=1 dotnet build "$repo/managed/Tf2DemoSalvage.Viewer3D" -c Release > "$out/$n-build.txt" 2>&1 && \
  TF2VIEW_WINDOW_POS=-3000,100 TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=1280x720 \
    pwsh C:/Users/pinku/source/repos/PinKushin/run-exclusive.ps1 \
    "$repo/managed/Tf2DemoSalvage.Viewer3D/bin/Release/net10.0-windows/tf2demoview.exe" \
    D:/measure-f12/f12.dem --tick 30000 --spectate gummo --first-person --measure 20 +fps_max 0 +mat_queue_mode 2 \
    > "$out/$n.txt" 2> "$out/$n.err"
  echo "run $n exit=$?" >> "$out/status.txt"
done
```

## Predictions (all [pred], per root rebuild, after set against the `6e8f2f47` before set)

| # | commit | prediction | falsified if |
|---|---|---|---|
| 1a | indexed loops | (moot: the `enumerators` column was removed in `perf/iklock-compose-2`; the allocation test is the evidence that the loops box nothing) | — |
| 1b | indexed loops | accumulate − layer sampling falls by **0.0005-0.003** (about 21 heap enumerators and their interface dispatch, 20-60 ns each [interp], plus less gen0 pressure) | it falls by more than 0.010 (then something else moved), or rises by more than 0.005 |
| 1c | indexed loops | roots total moves by less than the run-to-run spread (about 0.02) | it moves by more than 0.03 either way |
| 2a | no re-walk | `solve` falls by **0.003-0.006** (the bracket read's rank 1, from 8 of 15 bone builds in a successful solve) | it falls by less than 0.001 or more than 0.012 |
| 2c | no re-walk | `capture` unchanged, ±0.001: the re-walk was only in the solve, so this is the control | it moves by more than 0.002 |
| 2b | no re-walk | `Applied`, `Moved` per build unchanged (the `bone-flags` probe's lock counters, if read) | either moves by more than 1% |
| 3 | array read + unzeroed buffer | capture + solve (both build chains) falls by a further **0.001-0.003** (ranks 2 and 3: 0.0005-0.002 and 0.0003-0.001); below one set's resolution | it falls by more than 0.006, or rises by more than 0.003 |
| all | all three | roots total falls by **0.005-0.012**, about 1-2% of 0.53, which is **within one set's spread**: three runs per commit will probably not separate commit 1 or 3 from noise, only commit 2 at best | the roots total falls by more than 0.03 (then something other than these changes moved) |

**Void conditions** (the setup-parts and accumulate-parts records'): `prepose 0 over 0`; roots built 8-21 and eligible 4-7;
**roots built, eligible and layer samples per rebuild unchanged from the before run (±1, ±1, ±0.5)**, since none of these
changes alters what is built; the parts plus `unsplit` sum to the roots total; `unsplit` ≥ 0 and ≤ 0.05.

## Result

Not run. The lead fills this in.
