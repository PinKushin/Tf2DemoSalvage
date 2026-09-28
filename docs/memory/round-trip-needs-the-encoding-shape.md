---
name: round-trip-needs-the-encoding-shape
description: Which optional fields a message sent is not recoverable from the decoded values; the decoder has to record it or the demo cannot be rebuilt.
metadata: 
  node_type: memory
  type: project
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-09T03:54:31.905Z
---

A delta-coded message decodes to values that don't say which fields were on the wire. Re-encoding by
"send a field when it differs from the previous record" is wrong in a way only a bit comparison
against the original can see.

Measured on `svc_Sounds`: that rule came out exactly 12 bits short per occurrence, always a multiple
of a field's own width — the engine compares positions at full precision, the decoder sees them
quantised, so two sounds land in the same cell and the field looks redundant when the sender didn't
think so.

**Fix:** a lossless decoder records the encoding SHAPE alongside values (a field mask). Adding it took
the round trip from hundreds of mismatches to zero across 11,989 sounds and five protocols.

**How to apply:** when writing an encoder for a delta-coded message, don't infer presence — carry it.
Pick the sabotage carefully — narrowing a shared width still round-trips through VALUES and fails
only against the original demo's BITS; comparing against the original bits is what makes a round trip
evidence rather than a tautology. Related: [[fixtures-are-the-weak-point]],
[[read-the-encoder-not-the-decoder]], [[numeric-decoding-traps]].
