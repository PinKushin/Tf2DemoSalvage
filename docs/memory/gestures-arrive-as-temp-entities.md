---
name: gestures-arrive-as-temp-entities
description: A reload or flinch is a CTEPlayerAnimEvent temp entity, not an animation layer; a POV demo lacks the recorder's own.
metadata:
  type: reference
---

**A player's reload, flinch and attack animations arrive as TEMP ENTITIES.** `CTEPlayerAnimEvent`
(`tf_player.cpp:324`, `DT_TEPlayerAnimEvent`) carries the player, a `PlayerAnimEvent_t` and a data
word; `TE_PlayerAnimEvent` broadcasts it to everyone who can see that player.

**They are the ONLY source**, because `overlay_vars` is excluded from the player's send table — see
[[the-player-send-table-excludes-the-animation]]. Looking for a player's `m_AnimOverlay` in a demo
finds nothing, ever, and that is not a decode failure.

**Measured, `z1800.dem`: 40,288 of them** — the most common temp entity in the file by an order of
magnitude, ahead of 3,601 `CTEFireBullets`. 762 plain reloads, 925 reload loops, 287 reload ends,
4,228 primary attacks, 2,298 jumps.

**That distribution is the control on the enum offset.** The loop/end pair beside a smaller plain
count is the shotgun and sniper reload shape, which is what a real match looks like; a misread
enum would not land on a plausible one.

**The POV asymmetry, and it is a fact about the format rather than a gap.** `TE_PlayerAnimEvent`
calls `filter.RemoveRecipient( pPlayer )` for every event except the custom gestures and
`SNAP_YAW`, because a player predicts their own. **So a POV recording carries every other player's
gestures and none of its own; a SourceTV recording carries all of them.** A first-person viewer
following the recorder of a POV demo will see no gestures on that one player and should not treat
it as a bug.

**Two lookups that are not interchangeable.** A gesture names an ACTIVITY
(`ACT_MP_GESTURE_FLINCH_CHEST`), and the engine resolves it with `SelectWeightedSequence`, which
matches activity and breaks ties on `actweight`. `Studio_LookupSequence` matches a LABEL. No
sequence is labelled with an activity name, so asking the label lookup returns −1 for every gesture
on every model — silently, with a green suite either side of the gap.

**Not every event is a gesture.** `PLAYERANIMEVENT_JUMP` drives the main sequence; mapping it to a
layer would hang a jump on every player's arms. It is the second most common event in the corpus.

Related: [[a-player-is-client-side-animated]], [[output-level-assertion-or-it-is-not-done]].
