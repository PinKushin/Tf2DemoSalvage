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
