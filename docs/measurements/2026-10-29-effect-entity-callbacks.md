# 2026-10-29 — the strip entities built without per-frame delegates, closures and boxed enumerators (B599)

**Status: written and committed before any timed run.** Sections above `## Result` are fixed from here; deviations go
under Result.

## Observation

The census (`measurements/2026-10-29-allocation-census.md`, `2a46490c`, f12 gummo first person) attributes to
`EffectEntities.BuildEffectEntities` (callees inlined): a boxed `List<DetailSpriteVertex>.Enumerator` 16.5 KiB a frame,
`Func<int, SceneProp>` 13.1 KiB, a boxed `SZGenericArrayEnumerator<SpritePass>` 6.4 KiB and `ParticleBatch[]` growth
3.0 KiB — about 39 KiB, the figure B599 files. Read on the code: `AbsolutePose` made a lambda over `this` per call (one
per placed strip prop and parent walk); `SpriteStripBatches.Add` walked its interface-typed corner and pass lists with
`foreach`; each builder walked `props` the same way; seven method groups became new delegates every frame; the
Christmas check was a capturing closure per frame; the ropes' ready list and the result array were made per frame; and
`EntitySprites.PassesFor` built a one-element pass list per non-Sprite-shader strip.

## The engine's shape

Not a mechanism change: the same strips, passes, corners and order. The engine's beams, trails and ropes write into a
mesh builder per draw and allocate nothing per frame (`view_beams.cpp`, `c_rope.cpp` `CRopeManager::DrawRenderCache`).

## The change (`perf/alloc-sites-2`)

- `EffectEntities`: every callback made once in the constructor (`_drawnParent`, `_absolutePose`, `_beamEnd`,
  `_trailOrigin`, `_ropeEnd`, `_ropeLight`, `_ropeCollide`, `_ropeEndDormant`, `_christmas`); the Christmas question reads
  two fields set just before it; the result list reused (`FrameParticles` copies it the same frame); `_frame.Drawn`
  indexed.
- `SpriteStripBatches.Add`, `EntitySpriteBatches`, `EntityBeams`, `EntityTrails`, `EntityRopes`: interface-typed lists
  indexed; the ropes' ready list reused.
- `EntitySprites.PassesFor`: the one-pass list per blend made once.

## Question

Does the change remove those bytes a frame on f12, with the same frames drawn, and what does it do to the frame?

## Prediction

- **Allocation, G:** `allocated … MB a frame` falls by 0.02-0.05 MB against item 2's build. **E:** falls by 0.01-0.04 MB
  (the 10-25 record had this the empty view's fourth site, 8%).
- **Frame:** inside the spread; no claim unless the ranges separate.
- **Identity:** paused-shot world pixels A/B 0 different (HUD digits excepted); mode-2 verifier on B 0 changed frames.

Falsified if G does not fall by at least 0.015 MB, or any identity check fails.

## Method

As `measurements/2026-10-29-econ-attribute-walk.md`, with A = item 2's commit (label `c`) and B = this change (label `d`,
`D:/measure-f12/as2-d-src`): `all.ps1 -A c -B d -Tag item3`.

## Control

As item 1's record; the B trace must lose the `EffectEntities` sites and keep untouched ones (`WorldDecals.Placed`,
`WorldRenderer.DrawModel`) within ~15% of the census.

## Environment

(to be filled at the run)

## Result

(pending)

## Verdict

(pending)
