# 92. The steady frame is the processor's, and a face rewrote slots no flex reaches (2026-10-22)

**The question.** On f12 (`demostf-cp_process_f12-2026-08-08-2207`, tick 30000, first person, 1280x720) the viewer's
steady frame is about 14-15 ms and TF2's on the same scene about 5 ms
(`measurements/2026-10-21-tf2-vs-viewer-f12.md`). Is the frame waiting on the GPU or the processor, where does it go, and
which part is work the engine does not do? Every number here is from `measurements/2026-10-22-f12-steady-frame.md` or
`measurements/2026-10-22-flex-spread.md`, which hold the raw output, the load and the deviations.

## (1) CPU-bound, measured on the GPU's own counter

- **The first instrument failed its control.** The plan was to shrink the 3D view to 320x180 (1/16 of the pixels) and see
  whether the frame moved. Under 30-94% background load the 1280x720 runs spread 14.2-20.8 ms and the 320x180 runs
  13.1-17.2, so the 15% pair control failed twice and the size test resolves nothing. *Measured*, voided.
- **The instrument that answered:** Windows' `GPU Engine … engtype_3D` utilisation for the viewer's own PID. 6.3-10.2%
  at 1280x720 and 6.0-9.0% at 320x180 (the control: the smaller view reads lower). The GPU is idle nine tenths of the
  frame. *Measured.*
- Post hoc, not predicted: `draw` is 0.31-0.34 of `camera` at 1280x720 and 0.23-0.27 at 320x180 in every run, so about
  0.6 ms of `draw` scales with pixels. The rest is the processor.

## (2) Where the steady frame goes

Steady seconds, ms a frame, in the three runs under 75% load: `camera` 8.4-9.5 (about 60%), `draw` 2.8-2.9, `advance`
1.2-1.3, `hud` 1.1-1.2, `sound` 0.3. The render thread's CPU sample over the same window ranks by inclusive share:
`MomentScene.Pose` 33.6%, `Device3D.DrawFrame` 18.6%, `BurstEffects.StepOver` 8.0%, `SkeletonPose.Build` 6.9%,
`EntityModelSet.FlexFor` 6.0%, `ModelLighting.For` 5.8%, `Device3D.UpdateExposure` 4.0%, `DecalEffects.StepModelDecals` 2.3%.

## (3) The leaf column lies about the collector, which reopens finding 89's reading

`PollGCWorker` is **42.3% of the render thread's leaf samples**, but the collector's own pause is about 1 ms of a 14-16 ms
frame (the `gc:` line, 6-7%). So at least 35 points of that 42% are not the collector. By its callers it is a long tail of
loop safe points (`Instances` 3.7%, `ModelLighting.For` 2.7%, `SceneProp.GetHashCode` 1.8%, …): the sampler's own suspension
lands at a safe point and is charged there. *Measured.* **Consequence:** rank by inclusive share only, and finding 89's
conclusion that `BulkMoveWithWriteBarrier`'s time "is the collector" rested on this same leaf reading. Its three fixes
stand on their own allocation tests; whether the copies cost time of their own is open again (B574).

## (4) The face's spread into buffer order (fixed)

`FlexFor` builds a talking face each changing frame: `StudioFlexRules.Accumulate` adds each flex's deltas into per-`.vvd`
arrays, and `FaceFlex.InBufferOrder` spread them into the model's buffer order — **clearing and rewriting every slot of the
model**, 26,922 for the soldier, through two interface-indexed lists.
- **Valve.** The accumulation walks only the vertices each flex names (`R_StudioFlexVerts`, studiorender `0x18001eb90`,
  read in disassembly for finding 75; `Accumulate` is that loop). The spread is ours: our skinning is on the GPU and the
  deltas go up as a stream in buffer order. *Read in our code; the engine's side is finding 75's reading, not re-read.*
- **What a laugh reaches:** 8,105 of 26,922 soldier slots (B513's own output test). The other two thirds were written as
  zero every changing frame, into a stream that was already zero there.
- **Fix:** `FaceFlex.FlexedCorners` lists, once per model, the buffer slots whose `.vvd` vertex any flex moves;
  `FaceFlex.IntoFlexedSlots` writes only those, without clearing. The stream is kept per entity together with the list that
  wrote it, and is reused only for the same list, so a class change gets a fresh zero stream. **No output changes.**
- **Tests:** `FlexedCorners_AModelWhoseFlexMovesOneVertex_ListsOnlyTheBufferSlotsCarryingIt` and
  `IntoFlexedSlots_AStreamWithAnUnflexedSlot_WritesTheFullWalksValuesAndLeavesTheRestUntouched` (a sentinel in the unreached
  slot survives; the reached slots equal the full walk's). Both were red with the filter sabotaged to admit every slot.
  **Output level:** the face demo tests print 8,105 of 26,922 (1.791) for the laugh, 398 (0.254) at rest, medic 3,785 and
  scout 2,548, exactly B513's full-walk figures. **The weak spot, kept deliberately:** those tests assert only lower bounds,
  and writing half the slots passed all 87 of them; the exact counts are an observation compared by hand, not an assertion.

| f12 tick 30000, first person, interleaved (measurements/2026-10-22-flex-spread.md) | before | after |
|---|---:|---:|
| `flex` column, ms per rebuild | 0.832, 0.863 | 0.499, 0.495 |
| `place`, ms per rebuild | 1.24, 1.32 | 0.95, 0.93 |
| steady frame, ms | 13.68, 14.16 | 13.66, 13.42 (inside the noise) |

## (5) Next, not started

- **The exposure count on the processor** (`ExposureHistogram.TryRead`, 3.9% as a leaf). The readback does not block
  (`DONotWait`); the cost is walking a sixteenth of the region's pixels on the CPU every frame. Valve counts with GPU
  occlusion queries, one bin per frame (`viewpostprocess.cpp:551-613`), so its processor pays nothing per pixel. Filed as B575.
- `BurstEffects.StepOver` (8.0%) was passed over here: finding 86 already removed the offers the engine does not make, and
  what is left is spread over eleven offerers, none above 0.5 ms.
