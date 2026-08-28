---
name: a-lazy-cache-makes-reading-a-write
description: "Memoising on first read turns every reader into a writer; publish an immutable snapshot on assignment instead, which is also what the engine does."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-08-28T03:18:48.321Z
---

A `Dictionary` filled lazily on first read is a **write on the read path**, and a read path is
exactly where concurrency is least expected. `ServerConVars.Number` memoised its parse; the demo is
decoded off the UI thread while the free camera reads `sv_maxspeed` every frame on the UI thread,
so the gate threw *"Operations that change non-concurrent collections must have exclusive access …
corrupted its state"* out of `FreeFlightPath.SpeedPerSecond` — inside a movement test with nothing
to do with threading.

The fix is not a lock and not `ConcurrentDictionary`. **Parse on assignment and publish a whole
immutable snapshot**, swapped into one `volatile` field. Readers take the field once and see either
the state before a message or the state after it, never a mixture, with nothing on the read path to
synchronise. Type the snapshot's collections as `IReadOnlyDictionary` so reintroducing the bug is a
**compile error** rather than a race.

**Why:** it was also the parity answer, which is the part worth remembering. Valve's
`ConVar::InternalSetValue` converts to a float on assignment and stashes it in `m_fValue`, so
`sv_maxspeed.GetFloat()` reads a field. The lazy version's own doc comment *claimed* that shape and
the implementation had drifted from it — see [[valve-parity-is-the-first-principle]]. Following the
engine would have avoided the defect outright.

**How to apply:** when a value is expensive to derive and cheap to store, derive it where the input
changes, not where the output is wanted. Ask "who else holds a reference to this object" before
writing anything inside a getter. And verify such a fix by restoring the *original* broken code
rather than inventing a plausible sabotage — a fabricated one here (writing a fresh key into a map
nobody else mutates) stayed green through 800,000 iterations, while the real one failed three runs
out of three. See [[instrument-bugs-outnumber-decoder-bugs]].
