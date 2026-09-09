---
name: an-entity-index-does-not-name-a-track
description: The engine reuses edict slots, so a lookup keyed on entity index alone returns whichever track was written last — it needs the tick as well, and it fails silently by returning a plausible wrong answer.
metadata:
  type: project
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

**How to apply:** key on the index PLUS the tick. Scan for the track whose keyframes span the tick
being drawn:

```csharp
track.Keyframes[0].Tick <= tick && tick <= track.Keyframes[^1].Tick
```

Do it when an effect is created rather than per frame, and it costs nothing.

**The general shape** is [[lookups-must-match-exactly]] and [[key-a-lookup-on-the-question]]: the key
has to identify the thing being asked about. An index that the engine recycles identifies a SLOT, the
same way a store index identifies a slot and not a particle
([[a-computed-offset-is-a-guess-the-file-can-answer]] is the file-format cousin). And it fails the
way these always do — a plausible answer rather than an error, so nothing points at the lookup
([[instrument-bugs-outnumber-decoder-bugs]]).
