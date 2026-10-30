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

RTX 3060, i5-10600K, 2026-10-30 03:49-05:20, under `pmux`. A = `D:/measure-f12/as2-c-src` (`83c807e0`), B =
`D:/measure-f12/as2-d-src` (`b88b8d97`), Release from `git archive` (117 warnings first build, 0 incremental; 0
in-tree). Every timed run: load before under 25% (1-10% on most samples), exit 0, ended at tick 31347. Raw output in
`D:/measure-f12/alloc2/runs/item3/`, not committed.

## Result

**Deviations.** One extra first-person shot of B (B2), added after the first pair differed in the top bar; it started
three seconds after timed run 03-fp-B passed the same gate, so the two were serialised by `run-exclusive.ps1` and the
shot ran between 03 and 04 (04 started 6.4 minutes after 03 rather than 3.5). 03-fp-B reads like 02-fp-B (5.901 ms
both).

**Allocation (the decision):**

| view | A (4 runs) | B (4 runs) | B − A |
|---|---|---|---|
| first person, main thread bytes | 521,899-526,841 (525,276) | 481,978-483,026 (482,528) | **−42,748 (−8.1%)**, ranges apart |
| first person, MB a frame (all threads) | 0.70-0.77 | 0.66-0.71 | −0.05 MB, ranges overlap (the render thread's share varies) |
| empty, main thread bytes | 303,908-304,012 (303,959) | 262,069-263,946 (263,036) | **−40,923 (−13.5%)**, ranges apart |
| empty, MB a frame | 0.40-0.41 | 0.36-0.38 | −0.04 MB |

Attribution (B trace, first person): 513.2 KiB a frame against item 2's B trace 555.9; every `EffectEntities` site of
the census is gone (the boxed corner and pass enumerators, the parent-lookup delegates, the result growth), and
`WorldDecals.Placed` holds at 18.2.

**Frame, steady window, frame-weighted, ms (% of A's mean steady frame):**

| view | part | A | B | B − A | |
|---|---|---|---|---|---|
| first person | frame | 5.914-6.027 (5.972) | 5.901-5.936 (5.911) | −0.062 (1.0%) | **not claimed**: overlap |
| first person | `camera particles` | 1.426-1.441 (1.434) | 1.402-1.414 (1.409) | **−0.025 (0.4%)** | ranges apart |
| empty | frame | 2.489-2.538 (2.505) | 2.457-2.541 (2.499) | — | overlap |
| empty | `camera particles` | 1.126-1.161 (1.136) | 1.093-1.155 (1.123) | −0.013 | overlap |

**Identity.** World pixels 0 in every pair. Whole frame: empty 0/0; first person A/B 42,997 pixels in the top bar, A/A2
0 — and A/B2 0 while B/B2 is 42,997, so B itself shows both states of the bar from run to run, as items 1 and 2 found.
Mode-2 verifier on B: `queue verify: 815000 frames checked, 0 changed`.

## Verdict

**Confirmed**: main-thread allocation −43 KB a frame in first person (−8.1%) and −41 KB in the empty view (−13.5%),
ranges apart, inside the predicted 0.015-0.05 MB; the all-thread MB column overlaps in first person, so the claim rests
on the main thread's figure, which is where these sites run. Identity held. The frame is not claimed; `camera
particles`, which holds the strip build, fell 0.025 ms in first person with the ranges apart.
