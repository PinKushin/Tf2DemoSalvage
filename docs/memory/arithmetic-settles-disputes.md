---
name: arithmetic-settles-disputes
description: "A field's bit width constrains which numbering can be in use; check that before treating a format dispute as needing new evidence."
metadata: 
  node_type: memory
  type: project
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-09T03:54:10.382Z
---

A dispute over game event field type 7 (RISKS B14) was recorded as unsettleable without an old demo —
neither reading was exercised by the corpus. Settled 2026-08-09 with no new data: **the type field is
three bits.** The reading used came from CS:GO's protobuf ordering (`val_uint64` eighth,
`val_wstring` ninth), which doesn't fit in three bits — excluded by counting, not sourcing. The
project's own enum carried the comment "Three bits on the wire" two lines above the mistake.

**Why:** a disputed field is often over-constrained already — a width, terminator, max count, or
alignment can rule out a candidate outright, cheaper than sourcing a specimen. Especially suspect any
answer imported from a *later* version of the same format: protobuf-era renumbering has no bearing on
a hand-packed bit era.

**How to apply:** before recording a format question as needing external evidence, write down what
the surrounding bits already fix and check every candidate against it.

See [[numeric-decoding-traps]] for values wrong but plausible. Related: [[fixtures-are-the-weak-point]],
[[layer2-is-a-dependency-chain]].
