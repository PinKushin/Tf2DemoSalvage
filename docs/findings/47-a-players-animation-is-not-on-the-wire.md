# 47 — a player's animation is not on the wire

**Evidence: read from source, confirmed by measurement on the corpus.**

The most common assumption about a demo is that it records what happened. For a TF2 player's
animation it records almost none of it, and the exclusions are deliberate and listed in one place.

## What TF2 strips from a player

`CTFPlayer`'s send table, `tf_player.cpp`:

```
SendPropExclude( "DT_BaseAnimating", "m_flPoseParameter" ),      // 769
SendPropExclude( "DT_BaseAnimating", "m_flPlaybackRate" ),       // 770
SendPropExclude( "DT_BaseAnimating", "m_nSequence" ),            // 771
SendPropExclude( "DT_BaseAnimatingOverlay", "overlay_vars" ),    // 774
SendPropExclude( "DT_ServerAnimationData", "m_flCycle" ),        // 779
SendPropExclude( "DT_AnimTimeMustBeFirst", "m_flAnimTime" ),     // 780
```

Six fields: which animation, how far through it, how fast, the pose parameters that steer it, and
the whole layer array. `CTFPlayerAnimState` rebuilds every one on the client.

**Valve wrote down why the layer array is separable.** `BaseAnimatingOverlay.cpp:82`, immediately
above the table that holds it: *"These are in their own separate data table so CCSPlayer can
exclude all of these."* Counter-Strike wanted it out; TF2 took the same door.

## Confirmed on the corpus, with a control

The overlay array's length prop appears in `z1800.dem` on sentries (lengths 2, 3 and 4),
teleporters, dispensers, sappers and taunt props — and on no player. The control matters: an
absence found by searching a decompiled dump is a fact about the dump until something that must be
there turns up in it.

**It also caught the dump being blind.** The same dump prints no repeated-subtable elements at all
— zero `m_Attributes.000.*` lines in a file full of cosmetics with attributes — so it could not
have shown overlay elements even if a player had them. The length prop is what made the answer
readable.

## Where the animation comes from instead

| What | Mechanism |
|---|---|
| sequence | `CMultiPlayerAnimState::ComputeSequences`, from activity and speed |
| cycle | `m_bClientSideAnimation` — which IS sent — plus `FrameAdvance` |
| layers | `CTEPlayerAnimEvent` temp entities |
| pose parameters | `ComputePoseParam_MoveYaw` / `_AimPitch` / `_AimYaw` |
| playback rate | left at 1; only a taunt or the item-testing bot moves it |

**`m_bClientSideAnimation` is the interesting survivor.** `CTFPlayer::CTFPlayer` calls
`UseClientSideAnimation()` unconditionally (`tf_player.cpp:953`), so it is always set, and it is
what tells a client to run `FrameAdvance` on that entity every frame. A demo reader that honours it
gets a moving player; one that does not gets a statue whose position interpolates, because the
cycle it reads is a zero that was never sent.

## The gestures, and a POV asymmetry worth knowing

`CTEPlayerAnimEvent` (`tf_player.cpp:324`) carries a player, a `PlayerAnimEvent_t` and a data word.
**Measured in `z1800.dem`: 40,288 of them**, the most common temp entity in the file by an order of
magnitude — ahead of 3,601 `CTEFireBullets`.

| count | event |
|---|---|
| 4228 | `ATTACK_PRIMARY` |
| 2298 | `JUMP` |
| 2196 | `FLINCH_CHEST` |
| 1439 | `ATTACK_PRE` |
| 1320 | `ATTACK_POST` |
| 925 | `RELOAD_LOOP` |
| 762 | `RELOAD` |
| 287 | `RELOAD_END` |

**That distribution is itself evidence the enum ordering is right.** The loop/end pair beside a
smaller plain-reload count is the shotgun and sniper reload shape; an enum read at the wrong offset
would not land on a plausible one.

**`TE_PlayerAnimEvent` drops the player's own events.** For everything except the custom gestures
and `SNAP_YAW`, it calls `filter.RemoveRecipient( pPlayer )`, because a player predicts their own.
**So a POV recording carries every other player's gestures and none of its own; a SourceTV
recording carries all of them.** A first-person viewer following the recorder of a POV demo sees no
gestures on that one player, and that is the format rather than a defect.

## And the weapon switch is not a gesture at all

`PlayerAnimEvent_t` names no draw and no holster. A weapon change shows in FIRST person as the
viewmodel's own `ACT_VM_DRAW`, mapped per weapon type (`tf_weaponbase.cpp:4294`); in third person
the player simply holds a different model. Anyone looking for a third-person switch gesture will
look for ever.

**The viewmodel advances itself**, by a third mechanism distinct from both of the above:
`C_BaseViewModel` computes `elapsed_time * GetSequenceCycleRate(…) * GetPlaybackRate()` every frame
(`c_baseviewmodel.cpp:197`), with no list membership and no networked flag. It is also the only
place in the engine that clamps a finished one-shot to `0.999f` rather than to 1.
