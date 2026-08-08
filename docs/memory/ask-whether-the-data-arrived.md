---
name: ask-whether-the-data-arrived
description: Before analysing a decoder bit by bit, check that every message actually reached it — three rounds of bit-level analysis went into a decoder that was already correct
metadata:
  type: project
---

**Symptom:** entity decoding desynchronised partway through a demo, at a different point in
each file, with errors that looked exactly like bit-level faults — impossible class ids,
negative property indices, entities updated without ever entering.

**Cause:** none of that. Network messages carry no length prefix, so an unimplemented type
cannot be stepped over; the reader stopped and abandoned the rest of its packet, silently
dropping the `svc_PacketEntities` behind it. The decoder was correct the entire time.

**The question that would have found it immediately:** *did every message arrive?* The first
question actually asked was *which bit is wrong*, and three rounds followed — property
definitions, array count widths, coordinate flag precedence, per-value differentials — all
probing code with nothing wrong with it. `NetMessageReadResult.StoppedAt` had been recording
the answer the whole time.

Ten message types later, every corpus demo decodes end to end. Not one was a decoder fix.

## Why the differential misled here

The per-snapshot differential that settled the flattening order (see
[[differential-beats-fixtures]]) pointed straight at the decoder here, because **a dropped
message renumbers every snapshot after it** and that is indistinguishable from values read at
the wrong width.

What broke the deadlock was noticing our snapshot 19 was byte-identical to the oracle's
snapshot 20 — one off-by-one match falsifying every bit-level hypothesis at once.

**A differential compares two streams; it cannot tell you they are misaligned unless you look
for an offset.** Check for one before reading the first difference as a defect.

## The generalisation

Two failures of the same shape, in one session:

- The 332-snapshot wall, identical in two unrelated demos. Found by printing the *packet index*
  of each remaining stop — both hit `svc_UserMessage` at packet 336 — not by more bit analysis.
- A test asserting POV demos carry no full snapshot, justified by scanning 2,000 deltas without
  finding one. The snapshot was there, behind an unimplemented message.

Both are **a measurement of the reader's reach mistaken for a fact about the format**. Before
concluding anything about the data, establish the reader saw all of it.

Related: [[research-before-code]], since the layouts for all ten messages were in the reference
implementation and none needed experiment.
