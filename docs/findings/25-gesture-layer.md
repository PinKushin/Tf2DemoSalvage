# The gesture layer — how a demo says a player fired, reloaded, or jumped

The main sequence is what the body is *doing* — running, crouching, standing. A gesture is a thing
that happens *over* it: a muzzle flash of animation on the arms while the legs keep running, a
reload that plays once and ends, the little tuck of a double jump. TF2 computes the main sequence on
the client and sends none of it (see [21-player-animation.md](21-player-animation.md)); gestures are
different — they have an explicit trigger on the wire.

This file is the story of finding that trigger and, in particular, of a worry that turned out to be
unfounded — the kind the findings folder keeps deliberately.

## The trigger is a temp entity, not a field

Nothing on a player entity says "this player just fired." The event is a **temp entity**:
`CTEPlayerAnimEvent`, declared in `tf_player.cpp:340` as `DT_TEPlayerAnimEvent`, sent through
`svc_TempEntities`. Three properties (evidence: read from the SDK send table):

```cpp
SendPropEHandle( SENDINFO( m_hPlayer ) ),
SendPropInt( SENDINFO( m_iEvent ), Q_log2( PLAYERANIMEVENT_COUNT ) + 1, SPROP_UNSIGNED ),
SendPropInt( SENDINFO( m_nData ), ANIMATION_SEQUENCE_BITS ),
```

`m_hPlayer` is who did it, `m_iEvent` is a `PlayerAnimEvent_t`, and `m_nData` is a payload used by
only a few events (the activity for a voice-command gesture, the sequence for a custom one). The
project already decodes temp entities generically off the schema, so these three values fall out
without new decode code — the question was only what `m_iEvent` *means*.

## The worry: an enum ordinal is era-relative by construction

`m_iEvent` is transmitted as a raw ordinal, and an enum ordinal is the least stable thing you can
put on a wire. Insert one member in the middle of `PlayerAnimEvent_t` and every value after it
shifts by one — so a `6` read from a 2008 demo need not be the `6` the current SDK names. The field
*width* is era-relative too: `Q_log2( PLAYERANIMEVENT_COUNT ) + 1` grows as the enum grows (5 bits
when the enum had 30 members, 6 bits at 41). The width our decoder handles for free, because it
reads it from the demo's own send table. The *meaning* it cannot infer.

This was written into `RISKS.md` as the reason the event→slot mapping (B112 slice 3b) could not be
ported straight from one build's SDK. It was a correct description of the danger. It was also wrong
about this enum, and checking three eras is what killed it.

## What killed it: three SDK eras, and the enum is append-only

`PlayerAnimEvent_t` lives in `game/shared/Multiplayer/multiplayer_animstate.h`. AlliedModders' hl2sdk
keeps a branch per engine generation, so the same header can be read across TF2's history without a
decompiler (evidence: read from published source):

| Era | Branch | Enum members | Ends at |
|---|---|---|---|
| Orange Box, 2007–2011 | `hl2sdk/orangebox` | 0–29 | `VOICE_COMMAND_GESTURE` (29), then `COUNT` = 30 |
| 2013 | local `source-sdk-2013` | 0–40 | `ATTACK_PRIMARY_SUPER` (40), `COUNT` = 41 |
| Current | `hl2sdk/tf2` | 0–40 | identical to 2013 |

The three agree, member for member, on **0 through 29**. The modern builds add
`DOUBLEJUMP_CROUCH` (30), the stun trio (31–33), the PassTime trio (34–36), the CYOA-PDA trio
(37–39) and `ATTACK_PRIMARY_SUPER` (40) — every one of them **appended at the tail**, never inserted.
The shared prefix 0–22 is the base `CMultiPlayerAnimState` enum, older than TF2 and shared with
DoD:S and HL2MP; the TF-specific events begin at `ATTACK_PRE` (23) and only ever grew rightward.

So the ordinal is portable after all. Event `N` means the same thing in a 2008 demo and a 2024 one,
for every `N` that existed in 2008. A single mapping decodes the whole era axis; the only era effect
is *range* — an Orange Box demo cannot carry an event ≥ 30 because those did not exist — and range is
self-enforcing, since the narrower field cannot even represent them.

## The corpus agrees, once one sentinel trap is avoided

Measured across the committed era specimens (evidence: measured on the corpus), every observed
`m_iEvent` maps cleanly under the single enum:

- **2008 `cp_granary` (proto 14):** `{3,4,5,6,9,17}` — reload / reload-loop / reload-end, jump,
  flinch-chest, spawn. All in the stable prefix.
- **2011 `koth_viaduct` (proto 16):** adds `0` (primary attack). Same prefix.
- **2013 `cp_foundry` (proto 24):** `{0,3,4,5,6,9}`.
- **`z1800` (proto 24, 2020 or later):** the prefix plus `23` = `ATTACK_PRE`, `24` = `ATTACK_POST`,
  `29` = `VOICE_COMMAND_GESTURE` (with `m_nData` carrying the activity — 1502/1503/1505), `30` =
  `DOUBLEJUMP_CROUCH`, and `20` = `CUSTOM_GESTURE` (again with `m_nData`). Event 30 is the first
  modern-only value, and only a modern demo carries it — exactly as the append-only history predicts.

The one trap on the way there: a temp entity sends only the properties that differ from the previous
instance of the same temp entity, and `CTEPlayerAnimEvent` is a single persistent object reused for
every event. So an **absent** `m_iEvent` does not mean zero — it means *the same event as the last
one*. The first read of the distribution reported absent as `-1` and buried the truth; a heavy demo
is mostly sustained fire, so the absent bucket dominated (`z1800`: 3531 absent against 683 explicit
zeros) precisely because the event rarely changed. This is the sentinel trap recorded in
`docs/memory/sentinels-conflate-unknown-with-answer.md`, and decoding 3b for real will have to carry
the previous event forward rather than defaulting a missing field to zero.

## The lifecycle, once triggered

What a gesture does after it starts is era-clean and is already built (B112 slice 3a,
`Core/Scene/GestureLayer.cs`). `CMultiPlayerAnimState::UpdateGestureLayer`
(`multiplayer_animstate.cpp:1275`, the `CLIENT_DLL` branch) advances the layer's own cycle and, the
instant it passes one, either removes the gesture (`m_bAutoKill`) or freezes it on its last frame.
Because every rate factor on the standard `AddToGestureSlot` path is constant — playback rate 1,
gesture playback rate 1, cycle rate `1/duration` — the per-frame integration reduces exactly to
`cycle = elapsed / duration`, which is also the only form a seeking viewer can evaluate, since the
client's own frame times are not recorded. The composition of that layer over the main pose (additive
delta, per-bone weighted) is slices 1 and 2; see `Content/Assets/StudioPoseBlend.Layer` and
`StudioGestureWeights`.

## Open

Slice 3b — reading `m_iEvent` as a `PlayerAnimEvent_t`, carrying the persistent-instance state
across events, and running `DoAnimationEvent`'s event→slot+activity mapping — remains to be built,
but it is no longer blocked on an era question. The mapping is one table for all eras. Decompiling
the 2007/2008 launch client would upgrade the Orange Box row from "read from the cleaned OB SDK
snapshot" to "verified against the shipping binary" for the earliest protocols, where several demo
updates landed in a single year; the SDK and the corpus already agree, so this is confirmation
rather than discovery.
