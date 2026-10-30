# 2026-10-29 — attachments composed on the stack (B599 item 5)

**Status: written and committed before any timed run.** Sections above `## Result` are fixed from here; deviations go
under Result.

## Observation

The census (`measurements/2026-10-29-allocation-census.md`, `2a46490c`) puts `float[]` at `EntityModelSet.AttachmentsOf`
(15.2 + 6.1 + 3.2 + 1.4 KiB a frame, G), `MatrixConvention.ToModelMatrix` under it (6.9) and `ToBoneMatrix` (5.8) — about
39 KiB in G and 36 in E, from `PosedAttachmentMatrix` asked by `BurstEffects` (particle dispatches, muzzle flashes, heal
beams). Read on the code: each attachment resolved per entity per pass made the bone's copy (`Bone(i).ToArray()`), an
identity wearer (`PropTransform.Identity.ToMatrix()`), the concatenation, the model matrix and their product — five
arrays — before the one twelve-float result that is kept.

## The engine's shape (read from published source)

`C_BaseAnimating::SetupBones_AttachmentHelper` (`c_baseanimating.cpp:2055`, quoted in `EntityModels.Queries.cs`)
composes `ConcatTransforms( GetBone( iBone ), pattachment.local, world )` into a stack `matrix3x4_t world` and
`PutAttachment` copies it into the entity's preallocated `m_Attachments`. *Re-read 2026-10-30:* `matrix3x4_t world` is
declared once before the loop (`:2061`), the world-aligned branch builds an identity around the position (`:2070-2078`),
and `PutAttachment( i + 1, world )` (`:2082`) writes into `m_Attachments[number-1]` (`:2026-2045`), refusing a number
past `m_Attachments.Count()`. The kept per-attachment array here is that
copy; the five temporaries are not the engine's.

## The change (`perf/alloc-sites-2`)

`AttachmentPlacement.Matrix` and `MatrixConvention.Concatenate`/`ToModelMatrix`/`Multiply` gain span forms holding the
arithmetic, which the array forms now call (one implementation, same expressions in the same order);
`AttachmentsOf` composes on the stack against a static identity wearer and allocates only the kept
`BoneMatrixOf` result and the per-entity table.

## Question

Does the change remove those bytes a frame on f12, every attachment bit-identical, and what does it do to the frame?

## Prediction

- **Allocation:** G falls by 0.02-0.04 MB a frame against item 3's build; E by 0.02-0.04 MB.
- **Frame:** inside the spread; no claim unless the ranges separate.
- **Identity:** paused-shot world pixels A/B 0 (HUD excepted); verifier 0 changed.

Falsified if G does not fall by at least 0.015 MB, or any identity check fails.

## Method

As `measurements/2026-10-29-econ-attribute-walk.md`, A = item 3's commit (label `d`), B = this change (label `e`,
`D:/measure-f12/as2-e-src`): `all.ps1 -A d -B e -Tag item5`.

## Control

As item 1's record; the B trace must lose the `AttachmentsOf`/`MatrixConvention` sites and keep `WorldDecals.Placed`
within ~15% of the census.

## Environment

RTX 3060, i5-10600K, 2026-10-30 05:21-06:58, under `pmux`. A = `D:/measure-f12/as2-d-src` (`b88b8d97`), B =
`D:/measure-f12/as2-e-src` (`fb734d69`), Release from `git archive` (117 warnings first build, 0 incremental; 0
in-tree). Every timed run: load before under 25% (most 3-10%), exit 0, ended at tick 31347. Raw output in
`D:/measure-f12/alloc2/runs/item5/`, not committed.

## Result

**Deviations.** None from the plan.

**Allocation (the decision):**

| view | A (4 runs) | B (4 runs) | B − A |
|---|---|---|---|
| first person, main thread bytes | 480,748-481,935 (481,439) | 443,903-452,955 (448,810) | **−32,629 (−6.8%)**, ranges apart |
| first person, MB a frame (all threads) | 0.65-0.67 | 0.62-0.70 | overlap |
| empty, main thread bytes | 262,514-266,488 (264,614) | 231,483-233,428 (232,238) | **−32,376 (−12.2%)**, ranges apart |
| empty, MB a frame | 0.37 | 0.33-0.34 | −0.03 MB |

Attribution (B trace, first person): 480.5 KiB a frame against item 3's B trace 513.2; the `AttachmentsOf` sites fall
from the census's 38.6 KiB to 6.7 — the twelve-float array kept per attachment (5.9) and the per-entity table (0.8) —
and `MatrixConvention`'s are gone; `WorldDecals.Placed` holds at 18.6.

**Frame, steady window, frame-weighted, ms:**

| view | part | A | B | |
|---|---|---|---|---|
| first person | frame | 5.858-5.888 (5.872) | 5.835-6.066 (5.917) | **not claimed**: overlap (02-B 6.066) |
| first person | `camera particles` | 1.391-1.403 (1.396) | 1.367-1.418 (1.390) | overlap |
| empty | frame | 2.453-2.773 (2.609) | 2.449-2.622 (2.511) | overlap (14-A, 15-A, 16-B high, as late runs were in items 1-2) |

**Identity.** World pixels 0 in every pair; whole frame 0 except empty A/B, 46,816 pixels in the top bar — the
two-state bar items 1-3 showed within one build (A/A3, B/B2). Mode-2 verifier on B: `queue verify: 799000 frames
checked, 0 changed`. The attachment arithmetic is bit-identical by test (`Matrix_OnTheStack_IsTheArrayArithmeticBitForBit`).

## Verdict

**Confirmed** on allocation: main thread −33 KB a frame in first person (−6.8%) and −32 KB in the empty view (−12.2%),
ranges apart, inside the predicted 0.02-0.04 MB; the all-thread column overlaps in first person. Identity held. No
frame or part is claimed: every timing range overlaps. Kept as an allocation-only change at no measured cost, which
the brief allows, said plainly here.
