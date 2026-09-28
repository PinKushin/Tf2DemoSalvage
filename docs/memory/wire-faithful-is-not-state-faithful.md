---
name: wire-faithful-is-not-state-faithful
description: A decoder offering two views of one entity lets a caller silently pick the wrong one; the accumulator read the wire list for months.
metadata: 
  node_type: memory
  type: project
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-09T03:54:02.149Z
---

A decoded entity's raw properties are what the SNAPSHOT carried; its effective properties are what
the entity IS, laid over the class's baseline (an entering entity is a delta against that baseline
omitting equal values, `CL_CopyNewEntity`). `EntityStateTable.Apply` read the raw list for months.
Fixed 2026-08-21, B132.

**Why:** on demos anyone looks at, the two hold the same values (a player resends origin/health
constantly, so the baseline rarely adds anything — applying baselines changed no property count on
any corpus demo). The difference is total only for an entity whose whole state IS its baseline — one
entering once with fifteen properties, none on the wire ever again. It sat in every demo's entity
table looking empty.

**A half-fix reads exactly like a whole one** — a related writer had already been fixed with a commit
noting "has always done this", true of applying the baseline table but false of reading the merged
result.

**How to apply:**
- When a type exposes two accessors for "the same" data, make the wrong one unreachable by
  construction (require the baseline dependency, don't make it optional).
- Suspect this whenever an entity has a plausible-but-empty state — class name present with zero
  properties is the signature.
- Cross-check: the trace and the accumulated table, on the same packet, should agree.
- Confirm against something outside the project when possible (an authored map's own data matched).

**The same split recurs for temp entities:** an effect omitting its class is a delta against the
PREVIOUS effect in the message — tell: a field reading as the two values it shares with the prior
effect, not its own.

Related: [[measure-the-output-not-the-capability]], [[output-level-assertion-or-it-is-not-done]],
[[one-place-or-it-drifts]], [[instrument-bugs-outnumber-decoder-bugs]].
