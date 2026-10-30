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

(to be filled at the run)

## Result

(pending)

## Verdict

(pending)
