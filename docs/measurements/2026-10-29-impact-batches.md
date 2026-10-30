# 2026-10-29 — the impact batches built without garbage (B599 item 2)

**Status: written and committed before any timed run.** Sections above `## Result` are fixed from here; deviations go
under Result.

## Observation

The census (`measurements/2026-10-29-allocation-census.md`, trace of `2a46490c`, f12 gummo first person) attributes
`DetailSpriteVertex[]` 54.1 KiB a frame and `<>z__ReadOnlyArray<DetailSpriteVertex>` 4.3 KiB to
`ImpactParticles.WithImpactEffects` (its callees inlined), and `ParticleBatch[]` 13.9 + 6.9 KiB to the same method. Read
from our own Release assembly (`ilspycmd`, `Tf2DemoSalvage.Scene.ImpactDraw`): each of `ImpactDraw`'s three quad
builders calls `into.AddRange([c0, c1, c2, c0, c2, c3])`, which binds `List<T>.AddRange(IEnumerable<T>)` and so builds a
six-element array and a read-only wrapper per quad. `WithImpactEffects` also copies the frame's batches into a new list
(`[.. batches]`), makes a two-element runner array and a capturing alpha lambda, and boxes a value-collection
enumerator in `Values.Any(…)`; `ImpactDraw.Build` walks three `IList`-typed lists with `foreach`, boxing an enumerator
per effect and per emitter.

## The engine's shape

Not a mechanism change: the quads, their order and their batches are the same. `CSimpleEmitter::RenderParticles` and its
kin write corners straight into a mesh builder (`particle_util.h`, `RenderParticle_ColorSizeAngle`), allocating nothing
per quad, which is what the span form does.

## The change (`perf/alloc-sites-2`)

- `ImpactDraw`: the three `AddRange([…])` calls take a `ReadOnlySpan` (the six corners on the stack, appended by
  `CollectionExtensions.AddRange`); the emitter, particle and quad lists are walked by index.
- `ImpactParticles.WithImpactEffects`: the batch list is one reused field (the device copies what it keeps in
  `SetParticles` before the next frame, as it already does for the reused corner lists); the two runners are built by
  name; the alpha lookup is one delegate made in the constructor over a materials field; `Any` is a loop.

## Question

Does the change remove those bytes a frame on f12, with the same frames drawn, and what does it do to the frame?

## Prediction

- **Allocation, G (gummo first person):** `allocated … MB a frame` falls by 0.04-0.09 MB against item 1's build (the
  census's ~80 KiB at these sites, which vary with the bullets in flight). **E (empty view):** falls by 0.02-0.09 MB (the
  10-25 record named this the empty view's largest site; the census of E is the before figure).
- **Frame:** inside the spread; no claim unless the ranges separate.
- **Identity:** paused-shot world pixels A/B 0 different (the HUD frame-time digits excepted); mode-2 verifier on B 0
  changed frames.

Falsified if G does not fall by at least 0.03 MB, or any identity check fails.

## Method

As `measurements/2026-10-29-econ-attribute-walk.md`, with A = item 1's commit (label `b`) and B = this change (label `c`,
`D:/measure-f12/as2-c-src`): `all.ps1 -A b -B c -Tag item2`.

## Control

As item 1's record; the B trace must lose the `ImpactParticles`/`ImpactDraw` sites and keep untouched ones
(`EffectEntities.BuildEffectEntities`, `WorldDecals.Placed`) within ~15% of the census.

## Environment

RTX 3060, i5-10600K, 2026-10-30 02:09-03:50, the queue under `pmux` (`chain.ps1`). A = `D:/measure-f12/as2-b-src`
(`bece7a0d`), B = `D:/measure-f12/as2-c-src` (`83c807e0`), Release from `git archive` (117 warnings on the first build
of each archive, 0 incremental; 0 in-tree). Every timed run: load before under 25% on all three samples (most 3-10%),
exit 0, ended at tick 31347. Raw output in `D:/measure-f12/alloc2/runs/item2/`, not committed.

## Result

**Deviations.** None from the plan. The B runs were launched with label `c` (the `$side` fix, `e648792f`).

**Allocation (the decision):**

| view | A (4 runs) | B (4 runs) | B − A |
|---|---|---|---|
| first person, MB a frame | 0.84-0.89 | 0.73-0.77 | **−0.11 MB** |
| first person, main thread bytes | 646,123-647,474 (646,806) | 524,274-527,425 (525,678) | **−121,128 (−18.7%)**, ranges apart |
| first person, gc pause | 0.19-0.20 ms | 0.17-0.19 ms | −0.01 to −0.02 ms |
| empty, MB a frame | 0.47-0.52 | 0.39-0.41 | **−0.08 MB** |
| empty, main thread bytes | 380,225-390,816 (382,982) | 303,486-305,001 (304,165) | **−78,817 (−20.6%)**, ranges apart |

Attribution (B trace, first person): 555.9 KiB a frame against item 1's B trace 641.1; every `ImpactParticles`/
`ImpactDraw` site of the census is gone (left: `StepImpactEffects`' and the runner's enumerators, under 1 KiB), and the
untouched sites hold (`EffectEntities` enumerator 16.5 vs 16.2, `WorldDecals.Placed` 18.2 vs 18.8). The fall is larger
than the census's ~81 KiB for these sites; *not established why* — the census window had different bullets in flight
(the impact batches scale with them) and the sampled ticks charge a whole tick to the type that crosses it.

**Frame, steady window (seconds 6-20), frame-weighted, ms (% of A's mean steady frame):**

| view | part | A | B | B − A | |
|---|---|---|---|---|---|
| first person | frame | 5.955-6.021 (5.999) | 5.909-5.972 (5.940) | −0.058 (1.0%) | **not claimed**: overlap (08-B 5.972 above 01-A 5.955) |
| first person | `camera particles` | 1.488-1.524 (1.501) | 1.421-1.471 (1.445) | **−0.057 (0.9%)** | ranges apart |
| first person | `camera pose` (untouched) | 2.813-2.843 (2.825) | 2.807-2.839 (2.824) | 0.000 | overlap |
| empty | frame | 2.521-3.162 (2.691) | 2.486-2.587 (2.526) | — | overlap (15-A 3.162, as item 1's 14-A: an outlier at that place in the order both times) |
| empty | `camera particles` | 1.155-1.609 (1.273) | 1.124-1.172 (1.142) | — | overlap through 15-A; without it 1.155-1.164 against 1.124-1.172, still overlapping |

**Identity.** World pixels 0 in every pair (empty A/B, A/A2; first person A/B, A/A2). Whole frame: empty 0 in both pairs;
first person 42,997 pixels in the top bar (box (0,0)-(1279,48)) in A/B **and** A/A2 — the two-state bar item 1's record
found, not the change. Mode-2 verifier on B: `queue verify: 820000 frames checked, 0 changed`.

## Verdict

**Allocation confirmed, above its band:** −0.11 MB a frame in first person (main thread −121 KB, −18.7%) against a
predicted −0.04 to −0.09, so the prediction is **falsified as worded on the high side**; the empty view −0.08 MB (main
−79 KB, −20.6%), inside its band. Identity held (world pixels, verifier, the known top-bar state). The frame is not
claimed in either view; the part that holds the change, `camera particles`, fell 0.057 ms in first person (0.9%) with
the ranges apart.
