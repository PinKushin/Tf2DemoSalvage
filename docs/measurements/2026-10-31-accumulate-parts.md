# Measurement 2026-10-31: what is in accumulate besides the layer samples

## Record (written and committed with the instrument, before the first run)

Nothing below `## Result` has been run. Every number in `## Prediction` is a **[pred]**, fixed before the run; the
reasoning behind each is **[interp]** unless marked otherwise. This branch is an instrument only: no optimization, no
output change.

## Observation

After the frame-pair and dense-pose changes, the `roots split` accumulate remainder (accumulate minus layer sampling) is
still about **0.067 ms per rebuild** (`2026-10-31-dense-pose.md`, Verdict (final): 0.063 to 0.068 across its sets), with
layer sampling about 0.10 and roots about 0.49. The dense-pose change removed the rest seed under a dense base and did not
move the remainder measurably, so the seed is not where it is. That verdict names the two candidates left: the
per-layer arithmetic and the boxed enumerators (`2026-10-31-setup-bones-read.md` M6). Nobody has timed either.

## Question

Of `SkeletonPose.Accumulate`'s time outside the layer samples, per rebuild, how much is each of:

- **seed**: the base pose leaving the sample workspace, which is the dense copy or the rest seed plus sparse overlay;
- **capture** and **solve**: the two halves of the IK-lock brackets (`IkLocks.Capture` and `IkLocks.Solve`), the base's
  and each layer's;
- **compose**: each layer's per-bone pass, meaning the enumerator, the bone and weight guards, and the slerp or delta add;
- **other**: what none of them covers?

And how many interface enumerators do `Accumulate`'s `foreach` loops take per rebuild?

## Instrument

New sub-parts of `accumulate` in the existing `roots split (...)` segment, printed by `MomentCostLog` straight after
`over N`:

    accumulate A of which layer sampling L over N (seed S, capture K, solve V, compose C, other O; enumerators E), adjust ...

- **One chain of stamps inside `Accumulate`** (`managed/Tf2DemoSalvage.Animation/Animating/SkeletonPose.cs`). Each
  sub-part is the gap between two consecutive `Stopwatch.GetTimestamp` reads, so no tick is in two of them:
  - **Seed** runs from the entry stamp to the stamp after the copy or overlay.
  - With base locks the seed is two spans, broken by the capture, and the solve runs from the seed's end stamp.
  - **Per layer:**
    - the lock capture runs from the layer's existing `sampledAt` stamp, and its end stamp becomes the sample's start;
    - the sample keeps its existing span, and its end stamp is compose's start;
    - compose ends at a new stamp, and that stamp is the solve's start.
  - A layer without locks pays one new read, compose's end.
  - Capture and solve are separate fields because the stamps already separate them; summed they are the whole bracket.
- **Carried, not recomputed** (B243). The five values are fields of the pose, reset with `_layerSampling` at the top of
  `Build`. They are carried out in `LastBuild`, into `BoneSetupMeter.Record` (inside `if (built)`, roots only), and out
  through the same `SetupMark`/`PoseCounters.Since` route as every other part. `RootSetupParts.Since`/`Add` carry them.
- **Other** is `RootSetupParts.AccumulateOther = Accumulate − Sampling − Seed − Capture − Solve − Compose`. It is the
  reader's residual, never recorded. It holds:
  - `Build`'s static `Interlocked.Add(_animationTicks)`/`Increment(_animationCalls)` and the field resets, which run after
    `accumulateAt`;
  - the `Accumulate` guard and its early return, so a root with no layers and no locks is all `other`;
  - the outer `foreach`'s `MoveNext` and each layer's weight and `holds` tests;
  - the cost of the stamps that border those gaps (see Timer cost).
- **Enumerators** is a count, not ticks. It is incremented once before the outer `foreach` over `Layers` and once before
  each inner `foreach` over a sampled pose. Both are `IReadOnlyList<T>`, so each `foreach` takes its enumerator through
  the interface.
  - **[interp], not measured here:** each enumerator is a heap object when its list is non-empty. A `List<T>` boxes its
    struct enumerator and an array allocates an `SZGenericArrayEnumerator`. An empty array hands back a shared one.
  - So `E` is an upper bound on the allocations, and is exact when every list is a non-empty `List<T>` or array.
  - **Not counted:** `Adjust`'s and `SpinBarrel`'s `foreach` over the pose and `Controllers`, and `ReachWithIk`'s over
    the chains (M6's R4, R5 and R8). They are outside accumulate, and `adjust` is 0.002 ms in total.
- **Not split, on purpose:**
  - The weight and mask handling and the blend arithmetic are interleaved per bone inside compose. Separating them needs
    a stamp per bone, which `2026-10-31-setup-parts.md` already rejected on cost (about 80 bones × 2 reads × 25 ns per
    layer).
  - The weight **ramp** is not in accumulate at all. It is computed in `AutoLayersFor`
    (`managed/Tf2DemoSalvage.Scene/EntityModels.Autoplay.cs:149-178`) and lands in `blend`.

Tested in `tests/Tf2DemoSalvage.Animation.Tests/AccumulatePartsTests.cs`:

- **Known spins.** Each list a step reads spins for a known time and counts its own reads:
  - the sparse base's indexer, read only by the overlay, lands in seed;
  - the lock list's indexer, read once by the capture and three times by the solve (`IkLocks.cs`, [src]; the total of
    four is the test's control), lands in capture and solve;
  - the sampled layer's enumerator lands in compose;
  - the sample lands in sampling.
  Each part must read at least reads × spin, and `other` ≥ 0. A base-lock case covers the split seed chain.
- **Enumerator count**: exactly 3 for two weighted layers and one at zero weight; 0 with no layers and no locks.
- **Children add exactly zero** to the five, with the child's own `LastBuild` carrying a compose and two enumerators as
  the control. Also in `RootSetupPartsTests`'s existing child case.
- **`Since` and `Add`**: `RootSetupPartsTests.SinceAndAdd_EveryFieldDistinct_WorkFieldByField`, all fourteen fields,
  every input, difference and sum distinct. Nothing else reaches these hand-written lists with distinct values: the Scene
  charge compares `Since` with itself, and the child cases see only zeros.
- **Meter**: `BoneSetupMeterTests` sums eight threads, every field distinct, with the children dropped.
- **Scene charge**: `EntityModelSetSetupPartsTests` injects distinct values for the five new fields and checks both
  intervals.
- **Log**: `MomentCostLogTests` checks the printed segment, `other`, and `unsplit` unchanged at 28.

`sub-parts + other == accumulate − sampling` is asserted too, but it holds by definition and is not a control.

**Red first, and where it was not.** `AccumulatePartsTests` and the extended meter and child cases were seen red on the
stub: the record had the fields, nothing filled them. The `MomentCostLog` and Scene extensions were written beside the
code, not before it. They and the `Since`/`Add` test were shown able to fail by the sabotages below.

**Sabotaged by hand after green, each restored by the inverse edit** (Debug, the filtered assemblies):

| sabotage | reddened |
|---|---|
| compose stamped from `sampledAt` (before the sample) | `Build_WithASlowSeedLockComposeAndSample…`, `other` negative |
| the layer solve's end stamp removed (before the capture/solve split) | the same test, locks below 4 reads × 5 ms |
| the layer solve charged to `_capture` | the same test, solve below 3 reads × 5 ms |
| the base capture's end stamp not carried into the seed chain | `Build_WithBaseLocksAndNoLayers…`, `other` negative |
| the outer enumerator increment removed | the enumerator test (2 ≠ 3) and the child control (1 ≠ 2) |
| the meter adding `Seed` into `_compose` | `BoneSetupMeterTests` eight-thread, the child test, `EntityModelSetSetupPartsTests` |
| the log printing `Locks` as `seed` (before the split) | `Report_WithAccumulateSubParts…` |
| `Since` writing `Solve`'s difference into `Capture`'s place and back | `SinceAndAdd_EveryFieldDistinct…` only |

The second sabotage first **survived** with a constant floor of 2 × 5 ms, because the capture span read about 10 ms on
its own. The 5 ms over one lock read is most likely the JIT's first compile of the `IkLocks` path in a Debug test
**[interp]**. The floor is now reads × spin, carried from the list's own read count.

**Timer cost [interp].** Before the first stamp is counted:

- **25 ns a `GetTimestamp`, 8 ns an uncontended `Interlocked.Add`.** These are setup-parts' figures, not measured here.
- **No layers and no locks:** no new stamps.
- **A root with layers or locks:** 2 new stamps (seed). A locked base adds 3 more.
- **Per sampled layer:** 1 new stamp. A locking layer adds 2 more.
- **The meter:** 5 more `Interlocked.Add`s per built root.
- **Allocation:** none. The fields are longs on the pose.

At 12.9 root builds, 12.7 samples and perhaps 6 locking layers a rebuild, that is about 10-26 + 13 + 12 ≈ 35-50 stamps
(0.9-1.3 µs) plus 65 adds (0.5 µs). That is **about 0.001-0.002 ms per rebuild**. Where it lands:

- **The meter adds are outside `Build`**, in `BoneSetupMeter.Record`, so they land in `unsplit`, not in accumulate.
- **Each stamp's cost splits across the two spans it borders.** Seed, capture, solve, compose and sampling each carry part
  of a read, and `other` carries the part of the reads that border its own gaps: the entry stamp, the seed's end and each
  compose end before the next layer's weight test.
- **None of it is large enough to move a prediction.** All 50 stamps landing in `other` would still be 0.00125 ms.

## Method

Three runs of one command that builds in Release and runs that build, as in `2026-10-31-setup-parts.md` Method. The
driver is `2026-10-31-accumulate-parts/all.sh`, a copy of `2026-10-31-dense-pose/after-review/all.sh` with only `out`
changed. Its `repo=` is the t3 checkout, so **check out the tip commit of `measure/accumulate-parts` there first, as a
detached checkout** (`git checkout --detach <hash>`). The branch itself is checked out in an agent worktree, so git
refuses to check it out a second time.

```bash
repo=/c/Users/pinku/.t3/worktrees/Tf2DemoSalvage/t3-0b761547
out="$repo/docs/measurements/2026-10-31-accumulate-parts"
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

Each run's figures are the mean over its `moment cost` lines with the first left out, as before. The existing reader
`2026-10-31-setup-parts/summ.ps1` still binds: none of `seed`, `capture`, `solve`, `compose`, `other` or `enumerators` is
a word its patterns anchor on, and `adjust`, `loop`, `ik`, `finish` and `unsplit` still each appear once. **Checked**:
all sixteen of its patterns and the six below were matched with PowerShell `-match` against a line built around the
exact `roots split (...)` segment `MomentCostLogTests.Report_WithAccumulateSubParts…` expects. Every one returned its own
value (blend 2, base 3, accumulate 13, sampling 5 over 6, seed 0.5, capture 0.75, solve 0.125, compose 5.5, other 1.125,
enumerators 9, adjust 1, loop 11, ik 4, finish 8, unsplit 28). The new values are read with these patterns, which anchor
on the new group:

    seed    = '\(seed ([0-9.]+), capture'
    capture = ', capture ([0-9.]+), solve'
    solve   = ', solve ([0-9.]+), compose'
    compose = ', compose ([0-9.]+), other'
    other   = ', other (-?[0-9.]+); enumerators'
    enums   = '; enumerators ([0-9.]+)\)'

## Control

Checked on every run before any part is believed:

1. **`other` ≥ 0 in every line.** A negative residual means a sub-part counted a span twice.
2. **Seed + capture + solve + compose + other = accumulate − layer sampling** in each run mean, to print width (0.001 per
   term). This is arithmetic, so it only catches a reader error.
3. **`unsplit` is unchanged in kind**: ≥ 0 per line and ≤ 0.05 per run mean. The new parts are inside accumulate and must
   not move it.
4. **The four TIMED sub-parts (seed, capture, solve, compose) are not all near zero while the remainder is about 0.067.**
   That would mean the wiring is broken, not that the cost is elsewhere. (Instrument audit, 2026-10-31: the earlier wording
   counted `other` among "the five", but `other` is the residual and takes the whole remainder when every timer is dead, so
   it could never be near zero; `enumerators` is a count and stays 15-30 with every timer dead. Only the four timed
   sub-parts can serve as this control.)
5. ~~Enumerators ≥ samples per rebuild.~~ **Not a control.** `_layerSamples++` and `_enumerators++` sit on adjacent lines in
   the same iteration (`SkeletonPose.cs` ~1017, ~1020), so it holds by construction and cannot fail (audit, 2026-10-31).

**Further audit limits (2026-10-31, Opus `instrument-auditor`; no wrong number found, the carrying holds):**

- **`capture` and `solve` are means per rebuild with no count of lock brackets beside them**, and the base's bracket is merged
  with every layer's. A high `solve` cannot be told apart from more locking layers, so prediction 3's "perhaps 5-10 locking
  layers a rebuild" cannot be confirmed or falsified by this line alone. `compose` is covered (`over N` prints the sample
  count).
- **`other` is recomputed by the reader** (accumulate − sampling − seed − capture − solve − compose), never recorded. Control 1
  (`other` ≥ 0) catches a span counted twice and nothing else: a sub-part timed around the wrong code still prints a plausible
  `other`.
- Holds: the five fields are reset before `Accumulate` and read straight into `LastBuild`; the stamps form one chain so the
  sub-parts plus `other` equal accumulate minus sampling exactly; roots only; `enumerators` is an upper bound.

## Prediction (before the runs)

All **[pred]**. Run means, ms per rebuild.

**Void, not falsified,** if the scene differs from the earlier runs:

- built outside 30-50;
- root builds outside 8-21;
- eligible builds outside 4-7;
- layer samples outside 8-30;
- `prepose` not `0 over 0`.

1. **Remainder unchanged: other + seed + capture + solve + compose = 0.05-0.09.** The instrument adds about 0.002 (above).
   *Falsified* outside that range on a non-void run.
2. **Compose is the largest sub-part, 0.02-0.05.** About 12.7 layers a rebuild over about 80 bones each, where a dense
   sample names every bone. Per bone:
   - an interface read of `weights[bone]`;
   - on the slerp branch, `_bones[bone].Flags`, a copy of the 136-byte `StudioBone` through `IReadOnlyList`, which is
     the cost the `Build` loop's comment measured and avoided with `_boneArray`;
   - `Slerp`'s trig, or the delta add's quaternion multiply.

   At 20-50 ns a bone, about half of them blended. *Falsified* if compose is below 0.015 or above 0.06, or is not the
   largest sub-part.
3. **Capture + solve 0.005-0.04, with solve ≥ capture.** TF2's locking sequences are the aim matrices and attack stands
   (`IkChains` remarks, B311), which reach here as autolayers. Each locking layer pays a capture (one chain build from
   the root to the effector and a decompose) and a solve (three chain builds, a two-bone solve, three rebuilds and a
   check build), a few µs together, for perhaps 5-10 locking layers a rebuild. *Falsified* if the sum is outside that
   range, or if capture exceeds solve.
4. **Seed ≤ 0.005.** A player's base is dense after the dense-pose change, so the seed is one 32-byte-per-bone copy, about
   2.5 KB per layered root. *Falsified* above 0.008. That would mean bases arrive sparse or locked more often than
   assumed, and the rest seed is back.
5. **Other ≤ 0.01**: the statics, the guards and the timers. *Falsified* above 0.015, which would mean contention on
   `_animationTicks` (audit caveat 5 below) or untimed layer bookkeeping larger than assumed.
6. **Enumerators 15-30 per rebuild**: 12.7 samples plus one per root that reaches the layer loop, 5-13 of them.
   *Falsified* outside that range.

What follows from each outcome:

- **Compose largest (2 holds):** the next change is the per-bone read path in `Accumulate`:
  - index `_boneArray` by `ref` instead of `_bones[bone]`;
  - read the weights from a span;
  - index the sampled pose's span instead of `foreach`ing it, which also removes that enumerator.

  That is the M6 change inside the loop that costs.
- **Capture + solve largest:** the lock brackets go first, the larger half first. Read `IkLocks` against `CIKContext`
  before touching them.
- **Other above 0.015:** look at the statics before any arithmetic.

## Instrument audit caveats (inherited from `2026-10-31-setup-parts.md`, read before believing a result)

These apply to the new sub-parts:

1. **Caveat 5 now lands in `other`, named.** `accumulate` starts at `accumulateAt`, before `Build`'s static
   `Interlocked.Add(_animationTicks)`/`Increment(_animationCalls)` and the field resets. With the threaded pre-pass on,
   worker contention on those statics reads as `other`, not as any sub-part. Small with the pre-pass off, the only
   non-void case.
2. **Caveat 2 applies to `other` too.** It is `Accumulate − Sampling − Seed − Capture − Solve − Compose`, computed by the
   reader and never recorded. Control 1 (`other` ≥ 0) catches a double count only; it cannot catch a sub-part timed around the
   wrong code.
3. **The enumerator creation is inside compose** (the `foreach` over `sampled` starts after `composingAt`), so compose
   includes the allocation of the boxed enumerator, not only the arithmetic. The outer `Layers` enumerator is in `other`.
4. **First uses land in their sub-part.** The first `Held()` call allocates `_parents` and the `IkLocks` (and in a Debug
   test, JITs the path: see the second sabotage above), inside capture. On the f12 run these happen once per entity,
   in the first moments; the first `moment cost` line is left out, as before, but a later first-time player would add a
   one-off to capture. `slowest`/`peak` in the same line flag a window that has one.
5. **Roots only.** A child's accumulate is not recorded. The children column holds it, unsplit.
6. **The count is not an allocation measurement.** `enumerators` counts the `foreach` loops entered. It equals the
   allocations only under the **[interp]** in Instrument. A `GC.GetAllocatedBytesForCurrentThread` bracket would measure
   the bytes, but it was not added, because it would also count anything else allocated in the span.

## Environment

To be recorded with the result: the branch commit, the build, load before and after each run.

## Result

Not run. The runs are the lead's to schedule (the viewer takes the desktop lock).
