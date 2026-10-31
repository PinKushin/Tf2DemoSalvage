# Measurement 2026-10-31: where a root's bone setup goes

## Record (written and committed with the instrument, before the first run)

Nothing below `## Result` has been run. Every number in `## Prediction` is a **[pred]**, fixed before the run, and most of them
rest on **[interp]** reasoning (named where it is used), because the point of the run is to replace that reasoning with an
observation.

## Observation

`docs/measurements/2026-10-31-setup-bones-read.md` §0 (a read, nothing run): on f12 tick 30000, gummo first person, the
`setup` column's roots cost 0.666 ms a rebuild over 10.4 root builds (`2026-10-30-flex-for-cost/01-A.txt:110`), and only the
base-sequence sample has a timer (`anim 0.2`, which covers all 33.9 builds, children included). At least 0.47 ms of the roots'
time had no split. That document's ranking of parity-safe reductions (§4) assumes how the 0.47 ms divides, and its follow-up 1
asks for the split before acting on the ranking.

Across the seven earlier runs on the same scene (01-A, 04-A to 09-B), a single `moment cost` line's roots ranged 0.22-1.47 ms
over 4.6-21.6 root builds, with eligible roots (players, in practice) 0.20-0.91 ms over 1.6-7 builds. **[meas]**, read from
those files' setup segments.

## Question

Of the roots' `SetupBones` time per rebuild, how much is each of: the blend inputs (`BuildBlend`), the base-sequence sample, the
layer accumulation and the layer samples inside it, the steps between the layers and the per-bone loop (`CalcBoneAdj`, the
barrel, the override index), the per-bone loop, `ReachWithIk`, the duck-jump shift and bone scales, and what no part covers?

## Instrument

`RootSetupParts` (`managed/Tf2DemoSalvage.Animation/Animating/RootSetupParts.cs`), printed by `MomentCostLog` as a `roots
split (...)` segment straight after `setup (...)`:

    roots split (blend B, base S, accumulate A of which layer sampling L over N, adjust J, loop P, ik K, finish F; unsplit U)

- **One chain of timestamps through `SkeletonPose.Build`**: base, accumulate, adjust, loop, ik and finish are the gaps
  between consecutive `Stopwatch.GetTimestamp` reads, so they are disjoint and sum to the build exactly. The base's span is
  the one `AnimationTicks` has always added; it reuses that pair rather than taking a second reading.
- **Layer sampling** is timed around each layer's `_animation` call inside `Accumulate`, and is a sub-part of accumulate,
  never added to the sum a second time. `N` is the count of layer samples per rebuild.
- **Blend** is timed by `AnimatingEntity` around `BeforeBuild`, on **every** root call (the cache hits too), because the
  roots' ticks count every call.
- The parts are carried out of the build (`IBonePose.LastBuild`) into `BoneSetupMeter.Record` beside the root's own ticks, and
  out of the meter through the same `SetupMark` as `RootTicks` (`EntityModels.Instances.cs` `ChargeSetup`), so the parts and
  the total always cover the same window (B243).
- **Unsplit** is the residual: roots minus the disjoint parts. It holds the guards, the mask bookkeeping, `_written.Clear`,
  a ragdoll hook, and the timers' own cost.
- **Roots only.** A child's build parts are not recorded; the children column is unchanged.

Tested in `tests/Tf2DemoSalvage.Animation.Tests/RootSetupPartsTests.cs` (known spins in the base, a layer and the blend each
read back at least the value spun, in their own part; a child's build adds nothing to the roots), `BoneSetupMeterTests`
(eight threads, exact sums, children dropped) and `MomentCostLogTests` (the printed segment and the residual). All were seen
red on stubs first. `RootSetupPartsTests` and `BoneSetupMeterTests` were also sabotaged by hand after they went green, and
reddened: moving the base end-stamp past the layers reddened the layer test, and letting a child's parts into the meter
reddened the child, eight-thread and nesting tests. `MomentCostLogTests` was red only on the stub; it was not sabotaged.

**The Scene wiring has no test.** Deleting the `SetupRootParts` line in `ChargeSetup`, or dropping `RootParts` from
`MarkSetup`, would leave every suite green. Control 6 below is the only check on that path.

**Not split, on purpose:**

- **Inside the per-bone loop** (`QuatInterp`, `Jiggle`): a timer per bone would cost about as much as what it measures (80
  bones × 2 reads × ~25 ns ≈ 4 µs a build **[interp]**).
- **Inside `BuildBlend`** (layer composition against `IkFor`): if blend comes back large, that is the next cut.
- **First uses** (the read's follow-up 1 asks to time the miss paths apart): not separated. The read's §0 bounds one 2.9 ms
  peak at 0.029 ms on a 100-rebuild mean **[arith]**; `slowest`/`peak` in the same line still flag a window that has one.

**Timer cost [interp].** Per root build: 5 timestamps in `Build` beyond the pair `AnimationTicks` already took, 2 around the
blend, 2 per layer sample, and 9 uncontended `Interlocked.Add`s in the meter (1 on a cache hit). At about 25 ns a
`GetTimestamp` (QPC on an invariant TSC; not measured here) and about 8 ns an uncontended interlocked add, that is about 0.3 µs
plus 50 ns per layer sample. Every child build pays the same 7 timestamps in `Build` and the blend pair (about 0.2 µs), but no
meter adds for parts.

## Method

Three runs of one command that builds the branch's viewer in Release and then runs it, so the binary measured is the one just
built (a run without a build in the same command is refused, and could measure a stale exe). The build is outside the
machine-wide lock; the run is inside it. `<repo>` is the absolute path of the checkout at `measure/setup-parts`:

```bash
MSBUILDDISABLENODEREUSE=1 dotnet build <repo>/managed/Tf2DemoSalvage.Viewer3D -c Release && \
TF2VIEW_WINDOW_POS=-3000,100 TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=1280x720 \
  pwsh C:/Users/pinku/source/repos/PinKushin/run-exclusive.ps1 \
  <repo>/managed/Tf2DemoSalvage.Viewer3D/bin/Release/net10.0-windows/tf2demoview.exe \
  D:/measure-f12/f12.dem --tick 30000 --spectate gummo --first-person --measure 20 +fps_max 0 +mat_queue_mode 2
```

Equally valid, and what the flex record did: `git archive` the commit into a new directory under `D:/measure-f12/`, build it
there, copy the output no-clobber, and run that copy.

**No script parses the new segment yet.** `2026-10-30-flex-for-cost/summ.ps1` reads `place`, `flex` and `flex parts` only, and
`2026-10-27-threaded-bone-setup/read.ps1`'s anchored `setup (...blend X)` pattern still matches, because the new segment follows
its closing parenthesis. A reader written for this run should anchor on `roots split \(`, since `blend` now appears twice in
the line (`prepose ... blend` and the roots' `blend`).

The demo, tick and arguments are `2026-10-30-flex-for-cost-after.md`'s, plus `TF2VIEW_WINDOW_POS=-3000,100` (off screen, as the
tf2-measure skill requires for a per-pass reading). Each run's figures are the mean over its `moment cost` lines with the first
left out, as before.

## Control

The instrument's own consistency, checked on every run before any part is believed:

1. **`unsplit` ≥ 0** in every line. A negative residual means a part was counted twice.
2. **`unsplit` ≤ 0.05 ms** in every run mean. More means an untimed span in `AnimatingEntity` (guards, `_written.Clear`, the
   ragdoll hook) or a part not chained, and the split is incomplete.
3. **Layer sampling ≤ accumulate** in every line (sampling nests inside accumulate).
4. **Roots' base ≤ `anim`** in every run mean, allowing `anim`'s print width (`0.#`, so a printed 0.2 is [0.15, 0.25)).
   `anim` covers every build; the roots' base is a subset.
5. **`anim` − roots' base ≤ `children` + 0.05** (the rest of `anim` is children's base samples, which sit inside the children
   column; 0.05 is `anim`'s print width).
6. **The parts are not all near zero.** Every part reading about 0 while roots reads about 0.6, so that `unsplit` ≈ roots,
   means the Scene wiring (`MarkSetup`/`ChargeSetup`) is broken. It does not mean the work is somewhere else.

## Prediction (before the runs)

All **[pred]**. Run means, ms a rebuild.

**Void, not falsified,** if the scene differs from the earlier runs: built outside 30-50, root builds outside 8-21, eligible
builds outside 4-7, or `prepose` not `0 over 0`. **That last matters:** with the threaded pre-pass on, blend is charged to
`prepose ... blend` before the meter's mark, and the root `blend` part reads about zero.

1. **Roots total 0.5-0.9**, the earlier runs' spread. The instrument adds at most 0.01 to it (timer cost above × about 15 root
   builds **[interp]**). *Falsified* by a run mean above 0.95 or below 0.45 on a non-void run.
2. **Decode dominates: base + layer sampling ≥ 50% of roots.** The read's R2/R3 put three-way blends and full-skeleton
   expands in every sample **[interp]**. *Falsified* if base + sampling < 50% of roots in any run.
3. **Base 0.08-0.20.** Below `anim` (0.1-0.4 in the earlier lines), and players carry most of it. *Falsified* outside that
   range.
4. **Layer sampling 0.08-0.30, over 8-30 samples a rebuild.** Every player builds its aim-matrix autolayers and any gestures
   (the read's "2-3 layers per player" was **[interp]**; `N` replaces it). *Falsified* outside either range.
5. **Accumulate minus sampling ≤ 0.05** (the seed, the overlay and the per-bone slerp or add over about 80 bones per player
   build).
6. **Blend 0.03-0.15.** Five-odd players, each with `LayersFor`, `AutoLayersFor` twice and `IkFor` per accumulated sequence.
   *Falsified* outside that range.
7. **Loop 0.03-0.10.** About 5 × 80 player bones plus small roots, one `FromQuaternion` and one concatenate each, plus jiggle
   bones. *Falsified* above 0.12 or below 0.02.
8. **IK 0.005-0.05.** Four chains per player, solved only when a rule asks. *Falsified* above 0.06.
9. **Adjust ≤ 0.02, finish ≤ 0.01.** The override index fill and an early-out each for the bone controllers, barrel and three
   scales. *Falsified* above either.
10. **The largest single part is base or layer sampling**, not loop and not blend. *Falsified* if loop or blend is the
    largest part in any run.

What follows from each outcome: if 2 holds, the read's ranking item 1 (single-pass frame-pair decode) is first; if blend or
loop is largest instead, the ranking is wrong and items 3-6 move up.

## Environment

To be recorded with the result: the branch commit, the build, load before and after each run.

## Result

Not run yet.

## Verdict

Not run yet.
