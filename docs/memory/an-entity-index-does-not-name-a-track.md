---
name: an-entity-index-does-not-name-a-track
description: "The engine reuses edict slots, so a lookup keyed on entity index alone returns whichever track was written last — it needs the tick as well, and it fails silently by returning a plausible wrong answer."
metadata: 
  node_type: memory
  type: project
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-10T23:02:35.345Z
---

**`DemoTimeline.TrackFor(entity)` keeps ONE track per entity index, and a match has far more
entities than indices.** `demostf-cp_process_f12` carries **1,669 `CTFProjectile_Rocket` tracks**
across a couple of thousand edict slots, so index 407 names many different rockets over the match and
`_trackByEntity[407]` holds the last one written.

**How it failed (B375).** A rocket's trail needed the projectile's spawn tick to replay its history
after a seek. `TrackFor(407)` at tick 51122 returned a rocket from *later* in the match, whose
`FirstTick` is past 51122, so `age = Math.Max(0, 51122 - first)` clamped to zero, the `ticks > 0`
guard was false, and the replay silently did nothing. The trail simply never appeared — no exception,
no log, and a first attempt at the fix looked like it had made things worse.

**It has now happened three times, and the third was an INSTRUMENT.** B370: `jitter` reported "fewer
than three samples" for a door because its chosen track was long dead at the tick asked for. B389:
`cycle 20130518_0313_cp_granary_blu_blu 141 8200` printed a full animation table for entity 141 while
141 at that tick is `main_entrance_door.mdl` — that index owns **seven** tracks, one per round
restart. A probe's wrong answer is worse than the renderer's, because a picture that looks wrong gets
investigated and a table of plausible numbers gets quoted.

**How to apply: `DemoTimeline.TrackFor(entityIndex, tick)`, never `TrackFor(entityIndex)`.** The
tick-aware overload exists since B389 and selects with `ScenePropTrack.Alive`, the same bound `At` and
`Held` apply — do not write the range comparison again, because two expressions that must agree stop
agreeing. `TracksFor(entityIndex)` lists every occupant, which is what a report needs when the answer
is null: "nothing was ever recorded for that index" and "seven tracks, none covering your tick" send
the reader to opposite places. Probes go through `EntityTracks.Select`, which does both.

**Do not fall back to the index-only lookup when nothing is alive.** If no occupant covers the tick,
nothing of that entity is being drawn then and there is no track the answer belongs to — a guess is
the same failure with an extra step.

**Print the window you selected with, and print the model.** `EndTick` is the bound `Alive` tests; the
last keyframe is a different number, so `[first..lastKeyframe]` can exclude a tick the selection
accepted (B243). And a diagnostic that never names its subject's model gives the reader no way to
notice it is describing something else.

**The general shape** is [[key-a-lookup-on-the-question]] and its `lookups-must-match-exactly`
section: the key has to identify the thing being asked about. An index that the engine recycles
identifies a SLOT, the same way a store index identifies a slot and not a particle
([[a-computed-offset-is-a-guess-the-file-can-answer]] is the file-format cousin). And it fails the
way these always do — a plausible answer rather than an error, so nothing points at the lookup
([[instrument-bugs-outnumber-decoder-bugs]]).
