---
name: wire-faithful-is-not-state-faithful
description: A decoder offering two views of one entity lets a caller silently pick the wrong one; the accumulator read the wire list for months.
metadata:
  type: project
---

`DecodedEntity.Properties` is **what the snapshot carried**. `EntityDecoder.EffectiveProperties` is
**what the entity is** — the same list laid over the class's instance baseline, because an entering
entity is a delta against that baseline and omits everything equal to it (`CL_CopyNewEntity`).

`EntityStateTable.Apply` read the first for months. Fixed 2026-08-21, B132.

**Why:** the two are the same type, and on the demos anyone looks at they hold the same values. A
player resends origin, health and team constantly, so the baseline only supplies values that arrive
again a second later — applying baselines changed **no property count on any corpus demo**. The
difference is total only for an entity whose whole state IS its baseline: `CFogController` enters
once at tick 1 with fifteen properties, **none on the wire**, and is never mentioned again. It sat in
the table of every demo holding nothing but its class name. 19 of 195 entities were empty that way.

The trace writer had been fixed to call `EffectiveProperties` earlier and its commit noted
"DemoTimeline has always done this" — true of applying the baseline string table to the decoder,
false of reading the merged result. Half a fix reads exactly like a whole one.

**How to apply:**

- When a type exposes two accessors for "the same" data, the doc comment distinguishing them is not
  enough. Make the wrong one unreachable: `EntityStateTable` now **requires** an `IEntityBaselines`
  in its constructor, with `EntityBaselines.None` for fixtures. An optional dependency would let a
  caller rebuild the defect by omission.
- Suspect this whenever an entity has a plausible-but-empty state. Class name present with zero
  properties is the signature — the class id rides on the update itself, so it survives.
- The cross-check that settles it: **the trace and the accumulated table, on the same packet**. They
  came from one decoder and disagreed.
- Confirm a decode against something outside this project when one exists. Fog is networked by the
  demo and authored in the map's BSP entity lump, and they matched — see
  [[two-recordings-of-one-value]]. Pick the specimen that can falsify: viaduct's 213/174/221 fixes
  the colour byte order, a grey map cannot.

Related: [[measure-the-output-not-the-capability]], [[output-level-assertion-or-it-is-not-done]],
[[one-place-or-it-drifts]], [[instrument-bugs-outnumber-decoder-bugs]].
