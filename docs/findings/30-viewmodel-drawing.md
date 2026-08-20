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

### The root bone, measured — and two more hypotheses dead

The pose report now prints the root bone's matrix, because extents cannot separate a rotation from a
move. One frame, three arms models:

```
c_engineer_arms  seq 0   root [0 -0 1 | 1 0 -0 | -0 1 0]  at (0,0,0)   z 31.1..62.1
c_spy_arms       seq 0   root [0  0 1 | 1 -0 -0 | 0 1 -0]  at (0,0,0)   z 39..70.5
c_demo_arms      seq 1   root [1  0 0 | 0  1  0 |  0 0 1]  at (0,0,0)   y 31.7..65.9
```

**The arms are authored Y-up, and the root bone's rotation is the permutation that stands them up.**
Two of the three carry it; the one this project poses for the viewmodel does not, and its extents are
the same numbers with the axes cycled. The root's translation is zero in every case, so nothing is
being lost on the way to the camera — the model really is at the origin of its own space, and the
question is only which way up.

Two explanations tested and killed:

**A delta animation applied as absolute.** `bone_setup.cpp:379` returns a raw quaternion outright
while the Euler path honours `STUDIO_ANIM_DELTA`, so a raw-rotation delta applied as absolute would
replace a rest rotation with whatever the animation held. Our reader already defaults `rotation` to
`rest.Rotation` and only overwrites it when the animation carries one, which is
`bone_setup.cpp:392`'s rule exactly:

```c
if ( !(panim->flags & STUDIO_ANIM_ANIMROT) )
{
    if (panim->flags & STUDIO_ANIM_DELTA) q.Init( 0,0,0,1 );
    else                                  q = baseQuat;
}
```

**A delta SEQUENCE played instead of layered.** `STUDIO_DELTA` (0x4) marks a sequence the engine adds
on top of an already-posed skeleton rather than playing, and posing one alone builds a skeleton out
of differences — which produces exactly a bone left at identity. `StudioSequence.IsDelta` now reads
it and the viewer reports it. Sequence 1 is **not** a delta. Both checks are kept: they are one line
each and they turn a guess into a measurement.

**So the root rotation is being set to identity by ordinary animation data**, which leaves the
question the previous section raised and did not settle: whether merged sequence 1 is the sequence
the engine calls 1. The arms carry 74 merged sequences across 2 models and the merge here is
base-first; Source's `CVirtualModel` also deduplicates by name and resolves forward declarations
(`STUDIO_OVERRIDE`, which this project does read). The next measurement is to list our merged table
with each entry's name and source group and compare index 1 against what the model itself calls it —
a name, not a number, is what makes the comparison possible.

---

## Four real bugs, and the gap is now arithmetic rather than mystery

*(20 August 2026, continued)*

**1. Merged sequence 1 is `r_handposes`.** Dumping the merged table by NAME rather than by number is
what broke this open — a demo carries `m_nSequence`, and a number can only be compared against
another number:

```
c_soldier_arms.mdl: 98 merged, [0] g0 'c_soldier_arms', [1] g1 'r_handposes',
                    [2] g1 'dh_idle' act ACT_PRIMARY_VM_IDLE, [3] g1 'dh_fire' ...
c_demo_arms.mdl:    74 merged, [0] g0 'c_demo_arms',    [1] g1 'r_handposes',
                    [2] g1 'b_draw' act ACT_MELEE_VM_DRAW, [3] g1 'b_idle' act ACT_MELEE_VM_IDLE ...
```

Index 1 is a one-frame pose holder on every arms model, and it is what the viewer was playing — the
"1 frames at 0 cycles a second" in the log all along. The real viewmodel animations start at 2 and
carry `ACT_*_VM_*`. Playing the idle instead changes everything about the pose:

```
seq 1 (r_handposes)   x -24.3..24.3  y 31.7..65.9  z -9..6.8    root identity, at (0,0,0)
seq 3 (ACT_MELEE_VM_IDLE) x -2.1..36.1 y -17.5..19.8 z -39.3..3.8 root permuted, at (6.8,-4.4,-71)
```

The second is viewmodel-shaped: it extends forward, it is centred laterally, and its root carries
both the standing permutation and a translation.

**2. The activity lookup was asked before the model was packed**, so it answered −1 every time and
read as "this model has no viewmodel idle" when it has one at index 3. The sequence table does not
exist until `Add` has run. Separating "no packed frames" from "baked, no sequence table" from "no
such activity" is what found it — one −1 for three different faults is not a measurement.

**3. `Instances` CLEARS the list it fills.** Posing the arms into `_viewmodelInstances` and then the
weapon into the same list threw the arms away. Both go in one call now.

**4. The viewmodel list was cleared after the draw**, so it survived exactly one frame — and a
capture is taken while paused, when the pose step does not run. Every capture therefore got an empty
list and the pass reported `instances 0, camera False`. The list is owned by the pose step exactly
like the world's, and first person being off is said by dropping the camera instead.

### Where it stands, in numbers

The pass now draws two instances at the right place. Logged from inside it:

```
viewmodel pass: drawing 2 at c_demo_arms row(-313.5, -1398.1, 140), c_sniperrifle row(-313.5, -1398.1, 140)
```

The spectated player is at `(-314, -1398, 68)` and a standing eye is 72 above the feet, so 140 is the
eye exactly. **Placement is correct and confirmed.**

And the model is still not on screen, for a reason that is now arithmetic. The posed extents are
`x 0..36` forward and `z -39..+4`, so the hands sit about 39 units below the eye and 36 in front:

```
atan(39 / 36) ≈ 47 degrees below the view axis
viewmodel FOV 54 degrees -> 27 degrees of half-height
```

**It is below the bottom edge of the frame by about twenty degrees.** That is a specific quantity to
explain rather than a mystery, and it points at the root translation the animation carries —
`(6.8, -4.4, -71)`. Seventy-one units is close to the 72 an eye stands above the feet, which is the
next thing to check: whether the engine composes a viewmodel animation's root against the entity
origin at all, or whether these animations are authored about the player's origin and the −71 is
meant to be cancelled by placing the entity at the feet rather than the eye.

Note the log line that would have said this hours ago and did not exist: **where the instance
actually is in world space.** Packed, posed, instanced and listed were all confirmed repeatedly; not
one of them says where the thing ended up, and "off screen" and "nowhere" are indistinguishable from
all of them.

### What the SDK says is missing, and whether this data uses it

Three mechanisms sit between an animation's bone tracks and the pose the engine draws, and this
project implements none of them. Rather than implement blind, each was made askable:

| Mechanism | Where | Used by the arms idle? |
|---|---|---|
| `CalcZeroframeData` — fills unmentioned bones from a compressed span table | `bone_setup.cpp:985` | **no** |
| `CalcLocalHierarchyAnimation` — reparents a bone for the animation's duration | `bone_setup.cpp:990`–1008 | **no** |
| `STUDIO_DELTA` sequences — layered rather than played | `studio.h`, `AccumulatePose` | **no** |

`StudioAnimation.Unimplemented` reads `numlocalhierarchy` and `zeroframecount` straight out of
`mstudioanimdesc_t` (offsets 72 and 90 of a 100-byte struct, counted against the SDK), and the viewer
prints them beside the sequence. All three are absent from the animation being posed, so none of them
explains the placement. **An unimplemented mechanism the data never exercises is not a bug**, and
being able to say which is which is worth more than another guess.

Also checked and eliminated:

- **`CTFViewModel::CalcViewModelView`** overrides the placement, and everything it adds is zero by
  default: the lowered-weapon angle, the inspect offset and the min-mode offset
  (`tf_use_min_viewmodels`). The owner notes these are all recent additions — inspecting was the
  only one that existed when he stopped playing — so for the era corpus they cannot apply at all.
- **Parenting.** `CTFPlayer::CreateViewModel` calls `FollowEntity( this, false )`, which parents
  without bone-merging, so a local origin would be relative to the player. It does not apply here:
  `DT_BaseViewModel` is `BEGIN_NETWORK_TABLE_NOBASE` and carries no `m_hMoveParent` at all, so a
  demo's viewmodel has no parent to be relative to.
- **Bone remapping across model groups.** `CalcVirtualAnimation` remaps an included animation's bone
  indices through `pAnimGroup->masterBone`; `PropModels.PoseOf` does the same through
  `StudioBones.Remap` for every group but the base.
- **Position and rotation decode.** Both branches match `CalcBonePosition` and `CalcBoneQuaternion`,
  including the delta rules and the "no track keeps the rest value" case.

So the pose comes entirely from bone tracks this reader handles, and the sequence is a real 51-frame
animation at 0.6 cycles a second rather than the one-frame holder it was playing before. What remains
unexplained is the root translation that animation carries, `(6.8, -4.4, -71)`, and its arithmetic
consequence: hands 39 below the eye and 36 in front, about 47 degrees below a view axis with 27
degrees of half-height.

---

## It draws. The cause was packed-but-not-uploaded.

*(20 August 2026)*

`EntityModelSet.Add` fills **this process's** copy of the geometry. The renderer keeps its own on the
GPU and receives it only when `Device3D.UploadModels` is called. The world's props do that whenever
their set grows:

```csharp
if (grew && _device is { } device)
{
    device.UploadModels(_models);
}
```

`AddViewmodel` called `Add` and threw the return value away. So the arms were packed, posed,
instanced, transformed correctly, submitted in the right pass with the right camera — and drawn
against geometry the GPU did not have.

**The renderer said so on every frame**, in a line written long before any of this:

```
WARN [render] a model was posed but the renderer has no geometry for it
```

with the comment above it reading "a posed model with no batches means the renderer's copy of the
packed set is older than the caller's, which draws nothing and reports nothing". It was right, it was
specific, and it went unread through four rounds of investigation.

### What that cost, and what the order should have been

Everything below was verified before the actual cause was found. Each was worth verifying and none
of them was the fault:

| Checked | Result |
|---|---|
| model file present in the VPK | 39,928 bytes of `.mdl`, plus `.vtx` and `.vvd` |
| packed into the model set | yes, after the load-set fix |
| sequence | was `r_handposes`, now a real 51-frame idle |
| zero-frame data, local hierarchy, delta sequences | none used by this animation |
| bone remap across model groups | implemented, matches `masterBone` |
| position and rotation decode | matches `CalcBonePosition` / `CalcBoneQuaternion` |
| instance transform | `tip36` lands within a unit of the predicted eye + 36·forward |
| the viewmodel pass runs | yes, drawing 2 |
| depth | cleared as a test, no change |
| face culling | disabled as a test, no change |

**The one measurement that would have gone straight to it is the one the code already made.** Reading
the renderer's own warnings before adding new instrumentation would have skipped every row of that
table. This project's rule is "logs are the debugger"; the corollary earned here is that the logs
already written are the first place to look, not the last.

### Still to do

- **The arms are the wrong class.** Spectating a sniper draws `c_demo_arms`, because `ViewmodelAt`
  matches on an owner handle and a SourceTV demo carries one viewmodel per player. It picks a
  viewmodel that is not the followed player's.
- The weapon draws in the same pass and is not visible in the capture, which may be the same
  owner-matching fault selecting a weapon for the wrong player.
- Bob, lag and shake are deliberately absent; see the top of this document.

## An unowned viewmodel is only anybody's on a demo that names nobody

With the arms finally on screen, they were the wrong class: following a sniper drew a demoman's.

`ViewmodelAt` accepted `OwnerEntityIndex is null` as matching whoever asked. That is right for a
point-of-view recording — one viewmodel, no owner, because a client receives only its own — and
wrong for SourceTV, which carries one per player and names each. Measured on z1800 at tick 40000:

```
player 4 class 3: c_soldier_arms owner 4     right
player 7 class 7: c_pyro_arms    owner 7     right
player 2 class 2: c_demo_arms    owner none  WRONG
player 9 class 8: c_demo_arms    owner none  WRONG
```

One viewmodel of thirty-seven failed to resolve an owner, and the unowned rule handed that one to
every player who had none of their own.

The distinction is a property of the DEMO, not of an entity: if any viewmodel anywhere in the
recording names an owner, an unowned one belongs to nobody. `_viewmodelsNameOwners` is computed once
when the timeline is built, so the answer cannot vary with the tick being drawn. Afterwards:

```
player 2 class 2 (sniper):  c_sniper_arms  owner 2
player 4 class 3 (soldier): c_soldier_arms owner 4
player 7 class 7 (pyro):    c_pyro_arms    owner 7
player 9 class 8 (spy):     c_spy_arms     owner 9
player 1 (SourceTV camera): none
```

Class-correct throughout, and the SourceTV camera correctly gets nothing rather than borrowing
somebody's hands.

**Two players still resolve none**, which is the honest state: their viewmodel never decoded an owner
and there is now nothing to fall back to. Drawing no hands is better than drawing another player's,
and it is visible in the log rather than silent.
