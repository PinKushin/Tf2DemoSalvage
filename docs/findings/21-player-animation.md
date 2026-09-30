# 21 — Player animation

How a demo says what a player is doing, which is: it does not. This is the account of finding that
out, and of the four separate things that had to be right before a player stood up and moved.

## The demo carries no animation state at all

**Evidence class: measured**, across the whole committed corpus, 2007 to 2026.

Every playing player reports `m_nSequence` absent and `m_flCycle` at zero. One distinct value of
each, over 244,951 samples on z1800 alone and thousands on every other demo in the corpus.

This was checked rather than assumed, and the check was worth making: the reasoning that a demo
*should* carry none — TF2 computes animation client-side in `CTFPlayerAnimState` — is a statement
about the client, not about the wire, and `m_nSequence` and `m_flCycle` genuinely do live on
`DT_BaseAnimating`, which a player is. The measurement is what closed it.

The owner put it best: a demo is delta-compressed entity state, roughly "tick-speed git diffs". It
carries what was **sent**, and animation is **derived**, so it never enters the diff.

## Almost none of the animation is in the player model either

**Evidence class: measured**, from the installed game.

```
scout.mdl                        306 sequences,    2 local animations of 1 frame
  scout_user_animations.mdl        1 sequence,     1 animation
  scout_animations.mdl           377 sequences, 1012 animations, 5.0 MB
  scout_workshop_animations.mdl   90 sequences,   95 animations, 2.9 MB
soldier.mdl                      361 sequences,    2 local animations
  soldier_animations.mdl         419 sequences,  858 animations, 5.4 MB
```

Reached through `studiohdr_t.numincludemodels` at 336 and `includemodelindex` at 340, entries of
eight bytes. The offsets are counted from `studio.h`'s published field order and anchored on
`numbodyparts` at 232, which this project had already verified against real files — and
`medkit_small` reporting **zero** included models is the control that says they are not landing on
arbitrary data.

The two local animations are a single frame each: the reference pose, and nothing else.

## Sequences merge by label, and the base model's are placeholders

**Evidence class: read from published source**, `virtualmodel_t::AppendSequences`
(`public/studio_virtualmodel.cpp:142`), then measured.

A sequence number in a demo indexes a *merged* list spanning the base model and everything it
includes. The merge is by **label**, base model first, each include contributing only names not
already present.

Implemented that way, every named sequence a class has resolved to one frame:

```
layer_reload_standing_arms_primary_start: group 0 anim 0 1f | group 2 anim 61 21f
armslayer_ITEM1_fire:                     group 0 anim 0 1f | group 2 anim 331 25f
layer_dieviolent:                         group 0 anim 0 1f | group 2 anim 805 65f
```

The player model holds the **name** of everything it can play with an empty animation behind it,
and keeping the first occurrence keeps the stub. Valve's merge replaces on collision when the
existing entry carries `STUDIO_OVERRIDE` — 0x0800, which `studio.h` calls "a forward declared
sequence (empty)" — in place, so the index a demo sends keeps meaning the same thing.

Measured effect on the scout: 469 merged sequences, and sequences resolving to real multi-frame
animation went from **153 to 425**.

## An animation model numbers its own bones

**Evidence class: measured**, then confirmed against `bone_setup.cpp:966`.

This is the one that produced the most convincing wrong answer. With sequences resolving and the
GPU skinning them, players drew hunched and half-turned — "sitting up but not standing".

The measurement that found it applies the matrices the card is about to use, on the processor, and
reports the extents. An overhead camera cannot tell a bad pose from a bad shader; this separates
them in one line:

```
soldier, before: x 56.3  y 66.4  z 64.8   (roughly cubical)
soldier, after:  x 39.2  y 39.8  z 78.7   (a standing player is about 25 x 48 x 83)
```

The cause is that an animation model has its own bone list in its own order, and its animation data
indexes **that**. Valve remap every animation through `masterBone`, which `studio.h` describes as
mapping a local bone to a global one:

```c
int j = pAnimGroup->masterBone[panim->bone];
```

Matched by name, the remap is total: 76 of 76, 82 of 82, 86 of 86, 92 of 92 bones. Applying the
indices unremapped moves the right joints by the wrong amounts, which is why it looked like a pose
rather than like a failure.

## A model is lit at its illumination centre, not its origin

**Evidence class: measured**, then found in `studio.h`.

Players and props turned black in some places and recovered in others. A model takes the ambient
cube of the leaf it stands in, and a player's origin is at its **feet** — a point resting exactly on
a floor plane lands in the solid leaf beneath it, which carries no light at all.

`studiohdr_t.illumposition` at offset 92 exists for exactly this; `studio.h` calls it the
"illumination center". Sampling there took unlit models from seven to three, and the three that
remain are end-of-round banners parked outside the map at (−14483, 14242, −14475), which are
legitimately in the void.

**A wrong turn worth recording:** this hypothesis was raised early, tested by sampling forty units
higher, and dropped when that changed nothing — on a camera framing that contained no animated
prop. The idea was right and the experiment was blind. A negative result from an instrument that
cannot see the effect is not evidence.

## A dead player is drawn where they are watching

**Evidence class: measured**, cause read from `player.cpp`.

Players appeared stacked — "two soldiers in a ball". A corpse is still on a team, so a team check
keeps it, and a dead player's entity **follows whoever they spectate**, so it draws standing inside
the living player it is watching. Several of them heap onto one.

`m_lifeState` answers it: 3 bits, 0 alive, 1 dying, 2 dead, and it is in `DT_BasePlayer` rather than
`DT_LocalPlayerExclusive`, so it is present for every player in any recording.

**Absent means ALIVE.** Zero is `LIFE_ALIVE`, and a delta-compressed format only sends what changed,
so a player who has not died has never sent the property. Reading absence as "unknown, do not draw"
would hide everyone alive — the same trap that had already made every health pack static, where
absent `m_nSequence` was read as "no animation" when it meant "sequence 0".

The last position held while alive is kept and used until respawn, which leaves a body roughly where
it fell — a standing stand-in until ragdolls are simulated.

## Speed has to be derived, and that is not a shortcut

**Evidence class: read from published source**, `server/player.cpp:8117`.

`m_vecVelocity[0..2]` sit inside `DT_LocalPlayerExclusive`, sent through
`SendProxy_SendLocalDataTable`. So a SourceTV recording carries **nobody's** velocity, because
SourceTV is not any of the players, and a point-of-view recording carries only the recorder's.

Differencing recorded positions is therefore the only thing that works generally — the sole option
for every player in an STV demo and for eleven of twelve in a POV one. It measures the same quantity
the engine uses: `GetOuterXYSpeed` is `vel.Length2D()`.

Sampled over a tenth of a second rather than a tick, because a tick is 15 milliseconds of
interpolated position and differencing two adjacent samples measures the interpolator. Valve
interpolate their own ground speed over `flGroundSpeedInterval = 0.1`, which was arrived at here
independently.

## What is implemented, and what is not

Standing against running, which is `HandleMoving` comparing horizontal speed against
`MOVING_MINIMUM_SPEED` (0.5 units a second, `base_playeranimstate.h`). The cycle is advanced from
demo time the way `C_BaseAnimating::FrameAdvance` advances it, because a player's cycle is not sent
either. Measured on a soldier: 22 frames at 1.429 cycles a second, phase 0.571 → frame 12 at one
tick and frame 16 ten ticks later, which is the 4.7 frames the rate predicts.

Not implemented, and each of these draws a player standing or running instead:

- **Per-class playback rate.** Every class plays the same run sequence; `m_flMaxGroundSpeed` from
  `GetCurrentMaxGroundSpeed` drives the rate, so a heavy currently runs with a scout's footfalls.
- **Ducking**, which needs `FL_DUCKING` from `m_fFlags` — not decoded here yet.
- **Aiming, jumping, swimming, taunting, the loser state**, and the weapon-specific variants.
- **Upper-body layering.** The engine composes a lower-body sequence with an aim layer; this plays
  one sequence whole, so players do not point where they are shooting.

## Why players are skinned on the GPU and props are not

**Evidence class: arithmetic.**

```
medkit_small   1 animation,    30 frames,  1,608 corners
scout        469 sequences, 35,209 frames, 23,442 corners  = 825,369,378 corners baked
```

About seventy gigabytes for one model. Baking every frame is right for a pickup and impossible for
a player, so the budget decides: a model whose animations fit is baked and drawn by picking a vertex
range, and one whose do not is skinned per draw with its bone matrices in a constant buffer, which
is `IMaterialSystem::LoadBoneMatrix` and what the engine does for everything.

The engine has only that second path. Baking is this project's own optimisation and the divergence
is deliberate; the cost is two paths that can drift, and the mitigation is that the choice between
them is made by measurement in one place rather than by classifying models.

## The legs run the wrong way, and it is the blend grid rather than the decode

*Reported by the owner, 2026-08-14: "the bones of the models allow the model to be facing right,
but the feet and legs to bend 180 degrees the wrong way — you actually see it ingame with scout
sometimes on their double jump, but the other characters basically never get crazy legs in game."*

The body facing correctly while the legs do not rules out an orientation fault: one transform
places the whole model, so if it were wrong the torso would be wrong too.

The animation decode was checked against the SDK first and is faithful. `CalcBoneQuaternion`
(`bone_setup.cpp:374`) branches `STUDIO_ANIM_RAWROT` → `Quaternion48`, `RAWROT2` → `Quaternion64`,
`ANIMROT` → three run-length Euler channels scaled by `rotscale` and added to the bone's `rot`
unless the animation is a delta — which is what `StudioAnimation` does. `ExtractAnimValue`
(`bone_setup.cpp:339`) is

```cpp
while (panimvalue->num.total <= k) { k -= panimvalue->num.total; panimvalue += panimvalue->num.valid + 1; ... }
if (panimvalue->num.valid > k) v1 = panimvalue[k+1].value * scale;
else                          v1 = panimvalue[panimvalue->num.valid].value * scale;
```

and ours is the same walk with the same `remaining < valid ? remaining + 1 : valid` selection.

**What is missing is a layer above: pose parameters.** `StudioSequences` says so outright — it
reads `mstudioseqdesc_t::anim` at index `y * groupsize[0] + x` with both clamped, and takes the
CORNER. For a health pack bobbing or a door sliding that is the whole grid. A player's movement
sequence is a nine-way blend, and its corner is one fixed direction — so the legs run that
direction no matter which way the body faces, which is exactly the reported picture.

TF2 drives it from two parameters, `move_x` and `move_y`
(`Multiplayer/multiplayer_animstate.cpp:1413`), computed in `ComputePoseParam_MoveYaw` (:1575):

```cpp
float flYaw = flAngle - m_PoseParameterData.m_flEstimateYaw;
flYaw = AngleNormalize( -flYaw );
flYaw = SnapYawTo( flYaw );
vecCurrentMoveYaw.x =  cos( DEG2RAD( flYaw ) );
vecCurrentMoveYaw.y = -sin( DEG2RAD( flYaw ) );
```

`flAngle` is the direction of travel and `m_flEstimateYaw` the body's facing, so the pair is the
unit vector of movement **in the body's own frame** — and TF2 snaps it to eight compass points
first (`SnapYawTo`, :1443, thresholds 23/67/113/157).

*(Both halves of that are wrong, and B103 corrected the code: `flAngle` is `AngleNormalize( m_flEyeYaw )`
and `m_flEstimateYaw` is the direction of travel, `atan2( vel.y, vel.x )`; the snap runs only under
`mp_slammoveyaw`, which is `"0"` and development-only. The pair is still travel in the body's frame.)*

**Both inputs are recoverable from a demo.** Direction of travel comes from consecutive positions,
which `DemoTimeline.SpeedAt` already differentiates for speed; the body's facing is
`m_angEyeAngles`, which is decoded. Nothing new has to come off the wire — this is emulation, like
the rest of `CTFPlayerAnimState`.

*Evidence class: read from published source, plus one owner observation of the drawn result. The
claim that it FIXES the picture is not yet measured.*

## The item, not the weapon's script, decides how a player holds it (B105, 2026-09-29)

**What was believed:** a weapon's stance is its script's `WeaponType`, and the item's `anim_slot` was a
residual for a handful of odd weapons — the Scottish Resistance, the Quickiebomb, the banners, the
Gunslinger, the Dragon's Fury. B105 filed it that way for six weeks.

**What the census said, before a line of the fix** (the `anim-slot` probe, which reads the item's slot and
the role `PlayerProps.Add` actually drew): the override touched every demoman in every recording with an
item index. `items_game.txt` gives the stock stickybomb launcher `"anim_slot" "primary"` through the
`weapon_stickybomb_launcher` prefab and the stock grenade launcher `"secondary"` through
`weapon_grenade_launcher` — the reverse of both scripts — so each launcher had been drawn in the other's
stance: z1800 spends 45,638 player-ticks on an overriding item, a modern f12 match 187,752. The six named
weapons were the tail, not the case.

**Two wrong turns the fix had to undo, both invisible to the tests that existed:**

- **A role pasted onto an activity name is not a table.** The body's activity was composed as
  `ACT_MP_RUN_` plus the role, which agrees with `ActivityList`'s tables for ten roles of twelve. The
  Cow Mangler's PRIMARY2 runs with the primary rows (`tf_weaponbase.cpp:3785`), and the all-class melee
  table is keyed MELEEALLCLASS while its rows say `_MELEE_ALLCLASS` (`:4145`). The engine never pastes:
  `TranslateActivity` hands `CalcMainActivity`'s bare answer to the weapon's table
  (`tf_playeranimstate.cpp:124-133`) — which the gesture path here had already learned (B284).
- **An empty value in `items_game.txt` is an answer.** The prefab merge writes an item's own keys over its
  prefabs' whatever they hold (`econ_item_schema.cpp:2909`, `:2967`), so the Half-Zatoichi's `"anim_slot" ""`
  hides `weapon_sword`'s `item1` and a soldier's katana stays melee. The port's prefab search treated empty
  as absent and read the slot as ITEM1 — the census's own column says 6 — and every katana it found was a
  demoman's, which `CTFKatana`'s override makes ITEM1 anyway (`tf_weapon_sword.cpp:577-587`). Only a soldier's
  would have shown it, and no recording here has one.

*Evidence class: read from published source (the rules), measured on the corpus (the census), read from
shipped data (the slots).*

## The recorder faced his spawn direction for a whole demo, and it was called a rocket jump (B442, 2026-09-30)

**What was believed:** B101's ground truth — the POV recording's own `CUserCmd`, sampled in the middle of
every long run of `forwardmove 450` — gave `move_x = 1` at seven ticks and `(−0.707, −0.707)` at 5541 and
5681, and those two were put down to rocket jumps, "where `forwardmove` and the direction of travel
legitimately disagree because the player is airborne". The test written that day failed on exactly those
two ticks from its first commit (built at a29a63c5, it still does). The demo is lcor, the gate is gcor, and
nothing ran it until the first full superset (B439).

**What killed it was the recorder's other inputs, which a POV demo also carries.** At 5541 he has
`FL_ONGROUND`, his networked `m_vecVelocity` is (−206.18, −122.84) — 240 units a second at −149.2°, a
soldier's full speed — and his command's view yaw is −150.7. He is running exactly where he looks. The eye
yaw the timeline fed `ComputePoseParam_MoveYaw` was −23.6, and the `move-yaw` probe found −23.6 at every
tick from 200 to 5700: the recorder faced the way he spawned for the whole recording.

**One member, two writers.** `C_TFPlayer` has one `m_angEyeAngles` and both exclusive receive tables write
it — `RecvPropFloat( RECVINFO( m_angEyeAngles[0] ) )` and `[1]` in `DT_TFLocalPlayerExclusive`
(`c_tf_player.cpp:3745-3746`) and again in `DT_TFNonLocalPlayerExclusive` (`:3764-3765`) — so each
component holds whichever table wrote it last. Every player's ENTER carries both tables, local first
(`tf_player.cpp:801`, `:804`; all thirteen players of the f12 SourceTV demo). After it one table speaks:
the non-local one for everyone but a POV recorder, the local one for him — 2,033 writes against one here,
13,227 against three (his three ENTERs) in `tf2-2026-pub-pov-clean`. `EntityState.EyeAngles()` read the non-local table first
whenever it held anything. That is exactly the shape c7d65f1b took out of `Origin()` in August, when
deltas stopped wiping entity state and "a stale entry in an earlier table won permanently"; the accessor
beside it kept the fixed order, and only the recorder ever had a stale entry to lose to.

**The seven good samples were good by coincidence**: he happened to be running within 23° of his spawn
facing, and their ±0.4 `move_y` was that gap rather than noise. With the last write per component all nine
are `move_x` 1.000. SourceTV demos change nowhere; every POV recorder's body now turns, pitches its torso
and runs where he looks, wherever it is drawn.

**The fixture had the wrong premise written into it.** `SyntheticPlayer.Schema` declared one exclusive
table, "never both — a fixture declaring both would describe a combination no recording contains", which is
the one combination every recording starts with. A synthetic test could not have written the ENTER that hid
this until the claim went.

**Still different for the recorder, and named rather than fixed here:** for the LOCAL player the engine's
animation reads `EyeAngles()` — `pl.v_angle`, the engine's view angles (`c_tf_player.cpp:4279-4284`) — and
`EstimateAbsVelocity` returns his networked velocity instead of differencing the interpolated origin
(`c_baseentity.cpp:5854-5858`). This project animates him like any other player. Both routes give `move_x`
1.000 at the nine samples; `move_y` differs by up to 0.13.

*Evidence class: read from published source (the one member, the local-player branch), measured on the
corpus (the frozen yaw, the table counts, the control at a29a63c5), arithmetic (the move values).*
