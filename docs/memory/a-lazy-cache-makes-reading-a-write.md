---
name: a-lazy-cache-makes-reading-a-write
description: "Memoising on first read turns every reader into a writer; publish an immutable snapshot on assignment instead, which is also what the engine does."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-08-28T03:18:48.321Z
---

A `Dictionary` filled lazily on first read is a write on the read path — exactly where concurrency is
least expected. `ServerConVars.Number` memoised its parse; the demo decodes off the UI thread while
the free camera reads `sv_maxspeed` every frame on the UI thread, throwing *"Operations that change
non-concurrent collections must have exclusive access... corrupted its state"* out of
`FreeFlightPath.SpeedPerSecond` — inside a movement test unrelated to threading.

**Fix:** not a lock, not `ConcurrentDictionary`. Parse on assignment, publish a whole immutable
snapshot into one `volatile` field — readers see either the pre- or post-message state, never a
mixture, nothing to synchronise. Type snapshot collections `IReadOnlyDictionary` so regression is a
compile error.

**Why:** it was also the parity answer. Valve's `ConVar::InternalSetValue` converts to float on
assignment and stashes it in `m_fValue` — `GetFloat()` reads a field. The lazy version's own doc
comment claimed that shape; the implementation had drifted from it
([[valve-parity-is-the-first-principle]]).

**How to apply:** derive an expensive value where the input changes, not where the output is wanted.
Ask "who else holds a reference" before writing inside a getter. Verify such a fix by restoring the
*original* broken code, not a fabricated sabotage — a made-up one here stayed green through 800,000
iterations while the real one failed 3/3. See [[instrument-bugs-outnumber-decoder-bugs]].
