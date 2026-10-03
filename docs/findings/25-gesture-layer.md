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

## What the event reads that no demo carries (B112, 2026-09-29)

The mapping was right from the start and still drew the wrong reload, because two of its inputs were
never filled. `DoAnimationEvent` picks the reload's air-walking form from `m_bInAirWalk`
(`tf_playeranimstate.cpp:1141`, `:1154`, `:1167`) and the double jump's loser form from
`IsLoser()` (`:1196`), and neither is a field on the wire: the first is the animation state's own
memory, the second a rule over the round, the winning team and the player.

**`m_bInAirWalk` is a latch with one setter and five clearers** — read from published source. Only
`HandleJumping` sets it: `( vz > 300 || m_bInAirWalk || grapple ) && !bInDuck`, then on the ground
and latched it clears, waist deep it clears, in the air it sets (`:1446-1472`). The double jump
forces it off (`:1193`), and `ClearAnimationState` (`:114`) clears it on a respawn and on every frame
`Update` gives up on the player — dead, `EF_NODRAW`, **dormant**, or a custom model without the
class's animations (`multiplayer_animstate.cpp:1381-1395`). A dormant player stays in the client's
animation list (it leaves only in `PostDataUpdate` and `UpdateOnRemove`), which is why the dormancy
test in `ShouldUpdateAnimState` is live rather than defensive.

**`m_bDying` is a dead guard in TF2** — read from published source, then measured on the corpus.
`ShouldUpdateAnimState` keeps animating a dead player while it is set (`return IsAlive() || m_bDying`,
`multiplayer_animstate.cpp:1394`), and the only line that sets it is the base's `PLAYERANIMEVENT_DIE`
case, which begins `Assert( 0 )` under "not supporting this yet" (`:299-305`). TF's `DoAnimationEvent`
has no case for the event and hands it down, so a server that sent one would keep a dead player
animating. None does: z1800's 40,288 player animation events include 192 spawns and no death, and the
2011 and 2013 SourceTV specimens (154 and 165 events, 4 spawns each) have none either. The spawn is the
control — every respawn follows a death, so a server that announced deaths would show them in similar
numbers. The count is of `m_iEvent 8` lines in the text trace, which prints only what changed from the
event before it, and any run of deaths would have to begin with one. So the dead branch is `!IsAlive()`
alone, which is what the timeline asks.

**The wrong turn it corrected.** This project already kept a latch, for the body's air-walk, and it
looked like the engine's: set on a fast rise, cleared on landing. It latched while ducked and cleared
on any landing. The engine's duck test guards the whole block, so a crouched rise never latches and a
latched player who crouches keeps it through a crouched landing — a reload begun crouched after a
rocket jump is the air-walking one. The body and the reload now read one latch, as the engine's do.

**The class script's half stays out of the latch.** `bValidAirWalkClass` gates the block, so a class
whose script sets `DontDoAirwalk` never latches, and the medic's is the only script that sets it,
measured. The timeline cannot
read scripts, so a gesture the latch changed carries the base class's choice beside it and the scene
picks — the same split the body's air-walk already had.

**Measured, with a control** — `CorpusPlayerGestureTests`: on z1800, 28 of the 1985 reload gestures
that reach a sampled player take the air-walking form (scout 6, soldier 6, demoman 16), and a second
reading of the raw wire that shares only the decoder with the timeline sets its latch at exactly those
28 reload events and no others of 2002.

**Two instruments lied on the way, both caught by asking why a number moved.** The synthetic demo's
event packets were written at protocol 0, because the writer takes its protocol from `svc_ServerInfo`
and only the first packet carried it; `svc_TempEntities`' length is 17 bits there and a VarInt at 24,
so every event decoded nine bits out, as "no class", and the timeline silently dropped them all. And
the census first reported 2004 reloads, then 1985: its key included the player's class, and the old
timeline carried a stale reload across a respawn into another class, so one gesture counted twice.

## The landing is made by state, not by the slot (B437, 2026-10-02)

**What was believed:** landing is a REPLACEMENT — when a jumping player is back on the ground, swap the
jump slot's gesture for `ACT_MP_JUMP_LAND`. That was built for B284, where a scout's full-body
`ACT_MP_DOUBLEJUMP` kept playing after he landed, and it fixed that case.

**What killed it:** reading `HandleJumping` to the closing brace (read from published source,
`tf_playeranimstate.cpp:1427-1537`). `RestartGesture( GESTURE_SLOT_JUMP, ACT_MP_JUMP_LAND )` appears
twice, at the two places a STATE clears — the air-walk latch ending on the ground, and `m_bJumping`
ending on the ground under `bNewJump` — and neither looks at the slot. An ordinary jump,
`PLAYERANIMEVENT_JUMP`, plays no gesture at all: it only sets `m_bJumping`. So the replacement rule had
nothing to replace for every jump but a double jump. **Measured in the timeline's output on z1800**,
sampled every 200 ticks: 0 landings on any non-scout under the old rule, 2,738 after (differential, by
sabotage); 161 air-walk landings.

**Two engine facts worth keeping.** The two landings have different class gates — the jump's is
`bNewJump` (the soldier and medic set `DontDoNewJump`), the air-walk's has none and is reached only by a
class that air-walks — so a soldier lands a rocket jump with the gesture and a plain jump without it. And
the jump's bookkeeping is the `else` of the air-walk block: while a player is latched his jump is neither
timed out nor landed, and a latched player who ducks falls out of both branches into
`return m_bJumping || m_bInAirWalk`, which leaves `idealActivity` at `ACT_MP_STAND_IDLE` — TF2 draws him
standing, not crouching.

**A vestigial field, and a wrong filing it caused.** B437 said a respawn should re-seat the feet because
`ClearAnimationState` clears `m_bCurrentFeetYawInitialized`. Nothing in either anim-state file ever reads
that field (written at `multiplayer_animstate.cpp:58` and `:141` only). The feet re-seat on
`m_flLastAimTurnTime <= 0`, which only `PLAYERANIMEVENT_SNAP_YAW` zeroes, raised from one console path
(`server/client.cpp:1393`). A cleared flag is not a behaviour until something reads it.

## A "finished migration" that was not, and where the class script belongs (B437, 2026-10-02)

**What was believed:** `DontDoNewJump` guards the old single `ACT_MP_JUMP`, its comment says "Remove
me once all classes are doing the new jump", and every shipped class has it false — so reading it
would reproduce a migration that finished. That was written into `PlayerActivity` as the reason not to.

**What killed it:** a measurement this project already had. `ClassAirwalkTests` reads the shipped class
scripts and asserts that exactly two set `DontDoNewJump` — the soldier and the medic. The two claims sat
in the same repository, one measured and one assumed, and only the measured one had a test. On z1800
the soldier's jumps were 5,615 frames of the split push-off and float where TF2 plays `ACT_MP_JUMP`
(differential: the same demo built with and without the scripts).

**Where the flag is read decides what can be exact.** The first port kept the class script in the
scene, because only the scene had the installed game, and filtered Core's output: a reload carried its
non-air-walk alternative, a landing carried which clear made it. That works for flags that only CHOOSE
between outputs. `bValidAirWalkClass` changes STATE — whether the air-walk block runs, and so whether
the jump's bookkeeping is suspended — and no output filter can undo a state machine that took the
wrong branch. Carrying the scripts INTO the decode (`IClassAnimationScripts`, read the way
`tf_classdata.cpp:187-188` reads them) made every branch exact and let the filters go.

**Valve's order in `TranslateActivity` is the player before the weapon.** `ActivityOverride` walks one
of four player tables — kart, competitive loser, loser, carrying — and only then the weapon's. Only the
weapon's had been ported, so 6,513 losing player-frames on z1800 ran like winners. And
`CROUCHWALK_LOSERSTATE` exists in none of the nine class models (measured from the shipped
`*_animations.mdl`), so the engine's model check on `bInDuck` fires for every humiliated loser: a check
that reads as defensive turns out to be live on stock content.

## The duck is the model's, and the event reads the player when it fires (B437, 2026-10-03)

**The crouch-walk check needed less than it was filed as needing.** The entry said it wanted the
weapon's role at decode time. Reading the chain again: the loser's and the carrier's tables rewrite
`ACT_MP_CROUCHWALK` to `_LOSERSTATE` / `_BUILDING_DEPLOYED`, and no weapon table has a row for either
name (the ported tables, compared row for row with the SDK, contain neither), so for those two tables
the model alone answers `SelectWeightedSequence( TranslateActivity( ACT_MP_CROUCHWALK ) )`. The
class model reader (`PlayerClassModels.HasCrouchWalk`) reads the model and its includes; measured on
the shipped install: all nine lack the loser's crouch walk, the engineer has the carrier's, and every
class answers yes for an unrewritten one. Evidence: read from published source, measured on the game's
files. On z1800 it changes nothing drawn — 10 ducking-loser player-frames, none latched in the air.

**Gestures moved from arrival to fire time.** `DoAnimationEvent` runs from `CL_FireEvents`, an
interpolation window after the temp entity arrived (`CL_QueueEvent`, B415), and reads `GetFlags()`,
the latch and `IsLoser()` then — values the client holds as RECEIVED, none interpolated. The timeline
used to read them on arrival and start the gesture there. Now the events queue with their fire tick
and fire twice a packet: before applying it, those due before its tick (the client fired them on frames
in between, holding the earlier packet), and after it, those due on it — both before that tick's
`HandleJumping`. The independent latch walk in `CorpusPlayerGestureTests` was moved to the same clock
and agrees on all 1,981 z1800 reloads (26 air-walking); sabotaged back to arrival, it fails.

**Then the weapon and the item, and the check stopped looking defensive.** Routing the held weapon,
its item and the team into the check let it run `TranslateActivity` whole. Measured on the shipped
models, 49 of the 117 class-and-role pairs have no sequence for the role's crouch walk — the PDA and
building tables exist only on the engineer, `_PRIMARY` is absent from the spy, `_ITEM1` from the soldier
and medic. The pairs real loadouts produce were all found to have it, which is how Valve ships the
models; the check matters for whatever a server hands a class it was never animated for, and for a
player holding nothing, whose bare `ACT_MP_CROUCHWALK` no model names at all.

**A wrong turn, killed by reading one more header.** `GetActivityOverride( iTeam, … )` loops
`GetNumAnimations( iTeam )`, and `m_PerTeamVisuals[2]` is null for any item without a `visuals_red`
block — which is nearly all of them, since 36 of the shipped `animation_replacement` blocks sit in the
base `visuals`. That read as "every item replacement is dead in `TranslateActivity`". It is not:
`GetNumAnimations` and `GetAnimationData` both route through `GetBestVisualTeamData`
(`econ_item_schema.h:1831-1854`, `:2240-2253`), which falls back to the base block. The item step is now
ported where `TranslateActivity` runs, body and gestures; `items_game.txt` names no `CROUCHWALK`
activity, so on stock content it never touches the duck. Evidence: read from published source; the
counts measured on the installed game.

## Open

Slice 3b is built (B282, B284, B350, B351) and the context is complete (above). What remains of B437:
the engine's sequence-0 answer for an activity the model lacks, and voice gestures named by activity
number.
