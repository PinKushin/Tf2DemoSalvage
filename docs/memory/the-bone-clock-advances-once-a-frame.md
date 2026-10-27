---
name: the bone clock advances once a frame
description: EntityModelSet's bone clock is the engine's g_iModelBoneCounter, bumped once a frame at the world pass; a per-pass bump made every post-pass attachment query rebuild a drawn skeleton
type: project
---

`_boneFrames.Advance()` must run once a frame, at the world pass, as `InvalidateBoneCaches` runs once from
`OnRenderStart` (cdll_client_int.cpp:2163). The viewmodel and undrawn passes are draws INSIDE that frame.

**Why:** until 2026-10-27 `Instances` advanced it on every pass. Every effect's attachment query, which runs after those
passes, became a first touch on a stale clock: it rebuilt the skeleton the world pass had just built, and rolled the
entity's previous mask down to the attachment bit, so next frame an item drawn before its wearer built the wearer narrow
and the wearer's own call built it again. 0.43-0.53 ms a frame on f12
(`docs/measurements/2026-10-27-bone-clock-and-threaded-setup.md`). Finding 83 had marked the row "same" because the
counter is one counter; how often it is bumped was never compared. Finding 82's "0 rebuilt" was an instrument that asked
`BuiltThisFrame` after the clock had moved and could only read 0.

**How to apply:** a question "was this built this frame" asked after the later passes uses `BuiltSince(_worldBoneFrame)`.
When reading an engine counter's parity, compare WHERE and HOW OFTEN it is bumped, not only that it is one counter. Held
by `AttachmentQueryTests.AttachmentPosition_AfterTheLaterPassesOnADrawnEntity_BuildsNothing` and
`Instances_AFrameAfterAnAttachmentQuery_BuildsTheWearerOnceWhenItsItemIsDrawnFirst`.
