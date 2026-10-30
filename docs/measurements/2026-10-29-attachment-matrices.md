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
`PutAttachment` copies it into the entity's preallocated `m_Attachments`. The kept per-attachment array here is that
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

(to be filled at the run)

## Result

(pending)

## Verdict

(pending)
