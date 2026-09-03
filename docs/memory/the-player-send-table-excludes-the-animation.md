---
name: the-player-send-table-excludes-the-animation
description: TF2 strips sequence, cycle, layers, pose params and playback rate from a player's send table; the client rebuilds all of it.
metadata:
  type: reference
---

**A TF2 player's animation is almost entirely absent from the wire, on purpose.** `tf_player.cpp`,
in `CTFPlayer`'s send table:

```
SendPropExclude( "DT_BaseAnimating", "m_flPoseParameter" ),      // 769
SendPropExclude( "DT_BaseAnimating", "m_flPlaybackRate" ),       // 770
SendPropExclude( "DT_BaseAnimating", "m_nSequence" ),            // 771
SendPropExclude( "DT_BaseAnimatingOverlay", "overlay_vars" ),    // 774
SendPropExclude( "DT_ServerAnimationData", "m_flCycle" ),        // 779
SendPropExclude( "DT_AnimTimeMustBeFirst", "m_flAnimTime" ),     // 780
```

`CTFPlayerAnimState` rebuilds every one of them on the client.

**Why this matters more than it looks.** A measurement of what a demo carries for a player will
report zero for all of these and be RIGHT, so it is easy to conclude the decode is broken, or that
the value is simply unused. Neither. The field is not sent, and the answer has to be reconstructed
the way the client reconstructs it.

**Where each one comes from instead:**

| What | Where the client gets it |
|---|---|
| sequence | `CMultiPlayerAnimState::ComputeSequences`, from the activity and speed |
| cycle | `m_bClientSideAnimation` plus `FrameAdvance` — see [[a-player-is-client-side-animated]] |
| animation layers | `CTEPlayerAnimEvent` temp entities — see [[gestures-arrive-as-temp-entities]] |
| pose parameters | `ComputePoseParam_MoveYaw` / `_AimPitch` / `_AimYaw` |
| playback rate | left at 1; only a taunt or the item-testing bot changes it |

**What IS sent about a player's animation** is small: `m_bClientSideAnimation` itself, the eye
angles, the flags, and the entity's position and velocity the state machine reads.

**Note the shape of the mistake this prevents.** Three separate defects this session were
"the value reaches the renderer as zero" — and for a player, zero is what the wire says because the
wire says nothing. The question is never "why is the decode wrong"; it is "which client mechanism
fills this in, and have we implemented it".

Related: [[read-the-spec-before-measuring-our-data]], [[decoding-a-field-is-not-honouring-it]].
