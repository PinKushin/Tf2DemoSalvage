---
name: ask-whether-the-data-arrived
description: "Before analysing a decoder bit by bit, check that every message actually reached it — three rounds of bit-level analysis went into a decoder that was already correct"
metadata: 
  node_type: memory
  type: project
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-09T03:54:12.789Z
---

**Symptom:** entity decoding desynchronised partway through a demo, at a different point per file,
with bit-level-looking errors (impossible class ids, negative property indices).

**Cause:** none of that. Network messages carry no length prefix, so an unimplemented type can't be
stepped over — the reader stopped and abandoned the rest of the packet, silently dropping the
`svc_PacketEntities` behind it. The decoder was correct throughout.

**The question that would have found it immediately:** did every message arrive? Three rounds of bit
analysis (property definitions, array widths, coordinate flags, differentials) probed code with
nothing wrong with it, while `NetMessageReadResult.StoppedAt` recorded the answer the whole time. Ten
message types later, every corpus demo decodes end to end — not one was a decoder fix.

**Why the differential misled:** a dropped message renumbers every snapshot after it, indistinguishable
from values read at the wrong width. What broke the deadlock: our snapshot 19 was byte-identical to
the oracle's snapshot 20 — one off-by-one match falsifying every bit-level hypothesis at once. **A
differential can't tell you streams are misaligned unless you look for an offset.**

**Two more failures of the same shape:** a 332-snapshot wall identical in two unrelated demos, found
by printing the packet index of each stop (both hit `svc_UserMessage` at packet 336); a test asserting
POV demos carry no full snapshot after scanning 2,000 deltas — the snapshot was there, behind an
unimplemented message. Both are **a measurement of the reader's reach mistaken for a fact about the
format** — establish the reader saw all of it before concluding anything about the data.

Related: [[research-before-code]] — the layouts for all ten messages were in the reference
implementation, no experiment needed.
