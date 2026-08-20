# 30 — The viewmodel packs, poses, draws, and is not on screen

*(measured on the running viewer, 20 August 2026)*

First person shows no weapon and no hands. Everything that can be checked short of the rasteriser
says it should.

## What was wrong, and is fixed

**The arms were never loaded, so they packed to nothing.** `MainForm.ModelGeometry` is a dictionary
lookup into `MapAssets.EntityModels` — it does not read a model, it finds one that was read when the
map loaded. That set is built by `DemoModelPaths`, which walks the class models and
`timeline.Props`.

**A viewmodel is not a prop.** It carries no origin, so the timeline deliberately keeps it out of
`Props` — which means the arms were in neither list, were never loaded, and packed to zero batches
for ever. The log said so on every frame and it read as a missing file:

```
[props]  asked for 1, produced 0; skipped 0 not-studio [none], 1 no-batches [1xc_demo_arms.mdl]
[render] viewmodel models/weapons/c_models/c_demo_arms.mdl seq 1 at tick 40000: 0 instances
```

The file was in the archive the whole time — 39,928 bytes of `.mdl`, 38,569 of `.vtx`, 126,720 of
`.vvd`, checked directly. `DemoTimeline.ViewmodelModels` now exposes the arms and `HeldWeaponModels`
resolves every weapon any player holds at any tick, both feeding the load set. Entity models asked
for went from 18 to 225, and both now pack:

```
[render] viewmodel c_demo_arms.mdl seq 1 at tick 40000: 1 instances
[render] viewmodel weapon c_sniperrifle.mdl: 1 instances
```

## What is still wrong

**Nothing appears.** The model is packed, posed, instanced, and present in the draw list — the
frame's own summary names it. It is simply not visible in the capture.

Three explanations were tested and killed, which is worth recording because each is the obvious one:

| Guess | Test | Result |
|---|---|---|
| Near-plane clipping — the plane is 7 units and the model sits at the eye | pushed it 24 units along the view forward | no change |
| Mirroring reverses the winding and back-face culling eats it | stopped mirroring unconditionally | no change |
| The posed geometry is off to the side | rotated the yaw by −90 | no change |

**The measurement to start from next time is the posed bounding box**, which the poser already
prints:

```
[props] posed c_demo_arms.mdl CORNER, no pose parameters:
        10086 of 10086 corners weighted, 65 bones, x -24.3..24.3 y 31.7..65.9 z -9..6.8
```

Every corner is weighted and there are 65 bones, so the model is real and skinned. But the geometry
sits **32 to 66 units along +Y** and is centred on nothing — a viewmodel placed at the eye and
rotated by the view angles therefore puts its hands well outside the frustum, and the −90 experiment
only moved the problem rather than testing the premise properly.

**The likely answer is that this is the wrong space entirely.** `CalcViewModelView` sets the
viewmodel entity's origin and angles, and the client then draws it through
`C_BaseViewModel::InternalDrawModel` with **its own projection** — a separate FOV and a much nearer
depth range than the world pass. This viewer has no such pass: it puts the model in the world list
with the world's camera, which is not what the engine does and cannot be made right by moving the
model around.

So the next piece of work is a viewmodel pass, not another offset.

## What generalises

**Four guesses is three too many.** Each was cheap to test and each cost a build, a capture and a
look; the bounding box was in the log the whole time and named the shape of the problem in one line.
This project's own rule — read the map before the renderer, measure every hop before blaming one —
applied and was not followed until the guesses ran out.

**A loader that is a dictionary lookup should not be documented as loading on demand.** The comment
at the call site said "Packed on demand like any other model, so a weapon seen for the first time is
loaded rather than skipped". It is not, and the sentence is why nobody looked there.
