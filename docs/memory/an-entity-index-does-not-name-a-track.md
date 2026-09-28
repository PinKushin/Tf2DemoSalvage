---
name: an-entity-index-does-not-name-a-track
description: "The engine reuses edict slots, so a lookup keyed on entity index alone returns whichever track was written last — it needs the tick as well; and an index is not a NAME either, since a userinfo entry is a client slot and entity = slot + 1."
metadata: 
  node_type: memory
  type: project
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-10T23:02:35.345Z
---

**`DemoTimeline.TrackFor(entity)` keeps ONE track per entity index, and a match has far more entities
than indices.** `demostf-cp_process_f12` carries **1,669 `CTFProjectile_Rocket` tracks** across a
couple thousand edict slots, so index 407 names many different rockets and `_trackByEntity[407]`
holds the last one written.

**B375:** a rocket trail needed the spawn tick to replay history after a seek. `TrackFor(407)` at
tick 51122 returned a *later* rocket whose `FirstTick` exceeds 51122, `age` clamped to zero, the
`ticks > 0` guard failed, the replay silently did nothing — no exception, no log.

**It has happened three times, the third in an INSTRUMENT.** B370: `jitter` reported "fewer than
three samples" for a door whose chosen track was long dead. B389: a `cycle` probe printed a full
animation table for entity 141, which owns seven tracks (one per round restart) — a wrong instrument
answer is worse than the renderer's, since a table of plausible numbers gets quoted.

**How to apply:** `DemoTimeline.TrackFor(entityIndex, tick)`, never the index-only overload — selects
via `ScenePropTrack.Alive`, the same bound `At`/`Held` use. `TracksFor(entityIndex)` lists every
occupant for reports ("nothing recorded" vs "seven tracks, none covering your tick" send the reader
different places); probes go through `EntityTracks.Select`. **Don't fall back to index-only when
nothing is alive** — a guess is the same failure with an extra step. Print the selection window and
the model — `EndTick` ≠ last keyframe, so `[first..lastKeyframe]` can exclude an accepted tick (B243).

General shape: [[key-a-lookup-on-the-question]]#lookups-must-match-exactly — the key must identify
the thing asked about, not the slot. Fails as a plausible answer, not an error
([[instrument-bugs-outnumber-decoder-bugs]]).

---

## `an-entity-index-is-not-a-real-name` — and the roster that names one was itself a slot short

A nameplate in a capture names whoever is in frame, not whoever holds the camera — B397 compared the
wrong players' views repeatedly, camera guessed from what was visible.

**The roster was wrong too:** `RosterBuilder` took the `userinfo` entry index as the entity index;
the entry index is the CLIENT SLOT, entity = slot + 1 (entity 0 is the world,
`UTIL_PlayerByIndex`, `game/server/util.cpp:565`). Every name sat one entity short — `--spectate
<name>` landed on the neighbour; a "resolved" verdict naming entity 7 "nezay" was actually abelll.
Fixed B398.

**How to apply:** confirm an index with the `roster` probe, then `--spectate <name>` or user id. Check
any index-to-name mapping against something that must hold (POV header name at
`RecorderEntityIndex`; f12 entity 2 = Beleleu). Never declare a divergence resolved off an
owner-unconfirmed capture. Related: [[instrument-bugs-outnumber-decoder-bugs]].
