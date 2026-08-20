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

---

## The pass, built

*(20 August 2026)*

`CViewRender::DrawViewModels` keeps the world view's origin and angles and replaces three things:

| | world | viewmodel | source |
|---|---|---|---|
| field of view | 75 here, `fov_desired` in the game | **54** | `viewmodel_fov`, `view.cpp:111` |
| near plane | **7** (`VIEW_NEARZ`, `view.h:26`) | **1** | `view.cpp:643` |
| depth range | 0…1 | **0…0.1** | `DepthRange( 0.0f, 0.1f )` |

The depth range is what keeps a gun out of a wall: everything in the pass writes into the nearest
tenth of the buffer, so it is in front of all world geometry without being moved an inch. Valve's own
comment calls it a hack.

**TF2 reads a different convar during demo playback**, which is this project's only case —
`ClientModeTFNormal::GetViewModelFOV` returns `viewmodel_fov_demo` when `engine->IsPlayingDemo()`.
Its default is the same 54, so the number does not change, but the two could diverge and the demo one
is the one that applies here. Recorded in `ViewmodelPass` and asserted.

`Device3D.DrawViewmodels` runs after the world and its translucent pass, sets the viewport's depth
range, swaps the camera constant, draws, and puts both back.

**Restoring the camera is not tidiness.** The world's camera constant is written when the VIEW
changes, not every frame, so a pass that leaves its own projection behind is never corrected — the
entire map draws at 54 degrees from then on. That was visible in the first capture as a zoom nobody
asked for, and it is why `Device3D` now remembers the last world camera.

## Still not visible, and the reason is no longer the pass

With the correct FOV, near plane and depth range, nothing appears. That is consistent with the
measurement this document opened with and narrows the problem to one thing:

```
posed c_demo_arms.mdl CORNER, no pose parameters:
    10086 of 10086 corners weighted, 65 bones, x -24.3..24.3 y 31.7..65.9 z -9..6.8
```

The geometry sits 32 to 66 units along **+Y**. Source's +Y is left, and a narrower field of view puts
an off-axis model further outside the frustum rather than nearer the middle — so the pass could only
ever have been necessary, not sufficient.

**The lead worth following is `no pose parameters`.** A `c_*_arms` model is posed by the weapon it
holds, and this project already knows that pose parameters live in the included model rather than the
base one (`docs/memory/pose-parameters-live-in-the-included-model.md`) — the same trap that once ran
every player backwards. An arms model posed without them is not the pose the engine would produce,
and the bounding box says exactly that: it is somewhere a viewmodel never is.

---

## The arms are authored in body space, and the sequence rotates them

*(20 August 2026, after the pass was built)*

With the pass correct — 54 degrees, near plane 1, depth range 0…0.1 — still nothing. The reason is
not the projection, and the poser's own log says what it is. Three arms models, same family, in one
frame:

```
c_engineer_arms  sequence 0   x -7.3..9.1    y -23.6..23.6   z 31.1..62.1
c_spy_arms       sequence 0   x -7.2..11.1   y -20.8..20.8   z 39..70.5
c_demo_arms      sequence 1   x -24.3..24.3  y 31.7..65.9    z -9..6.8
```

The first two are worn by players and stand correctly: a pair of arms about 30 units tall, 31 to 70
units up. **The third has the same extents with the axes permuted** — what the others carry in Z it
carries in Y.

Forcing it to sequence 0 confirms the sequence is the whole difference:

```
c_demo_arms      sequence 0   x -9..6.8      y -24.3..24.3   z 31.6..65.9
```

Identical in shape to the engineer's. So sequence 1 rotates the skeleton, and neither sequence puts
the model anywhere near a camera: **both leave the arms 32 to 66 units from the model origin**,
because `c_*_arms` are authored in PLAYER BODY SPACE — they are the same arms the player model wears.

Placed at the eye, they are therefore a body's height above it in one sequence and a body's length
along Y in the other. Off screen either way, which is exactly what four captures show.

**So "put the model at the eye and rotate by the view angles" cannot be the whole placement**, even
though `CalcViewModelView` really does only that:

```cpp
SetLocalOrigin( vmorigin );   // the eye
SetLocalAngles( vmangles );   // the view angles
```

The missing step is between those and the vertices, and it is the animation. A viewmodel plays
sequences that come from the WEAPON, merged into the arms model — the soldier's arms report "98
merged sequences from 2 models" — and it is those that carry the hands from body space to in front
of the camera. `m_nSequence` indexes that merged list.

**Which makes this the same trap as the pose parameters, one level up.** This project already knows
that a paramindex is local to its group and that reading it against the base model ran every player
backwards (`docs/memory/pose-parameters-live-in-the-included-model.md`). A sequence index has the
same shape: if our merge order does not match the engine's, `m_nSequence` selects a real animation
that is the wrong one — which is precisely what a rotated skeleton looks like.

**The next measurement, stated so it is not guessed at again:** dump the merged sequence list for
`c_demo_arms.mdl` with each sequence's source model and name, and find which entry the engine would
call 1. If the list is `[arms sequences…, weapon sequences…]` and the engine's is the other way
round, the fix is the merge order and nothing else.

Four captures were spent moving a model that was never going to be visible where it was pointed.
The bounding box distinguished the two sequences in one line, and comparing it against the two arms
models already on screen is what named the space.

---

## Two corrections from the owner, and what the measurement says now

**The viewmodel field of view is a setting in TF2, and shipping it as a constant was a miss.** The
SDK was read, the convar was quoted, its clamp was quoted — and then 54 was written into the code as
though it were a fact rather than a default. `docs/findings/13-settings-parity.md` states the rule:
if TF2 lets you change it, this should too. It is `viewmodel_fov` in the settings file now, named as
the game names it, clamped to the game's own 54…70 rather than refused outside it — a ConVar with
bounds clamps, so a config asking for 90 gets 70 in both.

Worth noting the shape of the error: reading the source produced a correct number and an incorrect
conclusion, because the number was the only part being looked for.

**The owner also asked whether this is the same problem as the canteen at a player's feet.** It is
the same *class* and not the same *code path*, which is worth stating precisely because the answer
decides whether one fix serves both.

`docs/RISKS.md` B82, still open: a halo, an MvM canteen and a spellbook sit at the wearer's feet
because they are parented to a named ATTACHMENT rather than bone-merged —
`hwn_spellbook_complete.mdl` has one bone called `mvm`, no player skeleton has a bone by that name,
so nothing matches and the item falls back to the wearer's transform. Neither
`mstudioattachment_t` nor `m_iParentAttachment` is read here.

The viewmodel arms fail the same way in outline — the thing that should place them is not
implemented, so they fall back to the entity's own transform — but the missing mechanism is the
animation rather than an attachment. Fixing B82 will not put a weapon in anyone's hands, and fixing
this will not lift a canteen off the floor. Both are worth doing; neither is the other.

### Where the sequence lead actually stands

```
c_demo_arms.mdl     3452 frames over 74 merged sequences from 2 models
c_sniperrifle.mdl   1 frame  over  1 merged sequence  from 1 model
```

**The weapon model carries one sequence and includes nothing**, so the viewmodel animations are not
in the weapon — they are in whatever the arms include. That kills the reading in the previous section
that had `m_nSequence` indexing a weapon-first merged list; the merge here is arms-first and the
weapon contributes nothing to it.

And the two poses are not merely different. They are the same numbers with the axes cycled:

```
sequence 0   x -9..6.8      y -24.3..24.3   z 31.6..65.9
sequence 1   x -24.3..24.3  y 31.7..65.9    z -9..6.8
```

`(x, y, z)` becomes `(y, z, x)` exactly. An animation that merely posed the arms differently would
not reproduce three extents to the decimal in a rotated order — that is a basis change, which is
what a viewmodel animation's root bone does when it takes the arms out of body space. So sequence 1
is plausibly the RIGHT animation applying the rotation half of the transform while the translation
to the camera is lost or never applied.

**The next measurement is therefore the root bone, not the sequence list:** dump the root bone's
matrix for `c_demo_arms.mdl` at sequence 1 frame 0 and compare its translation against what the
extents imply. A rotation that arrives without its translation is a specific, checkable defect, and
it is one this project has met before in another form.
