---
name: a-stale-not-implemented-is-a-todo-list
description: A comment saying a feature is missing is read as work owed; four were false in one grep.
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-04T04:07:44.842Z
---

**Grep the source for "not implemented", "not reproduced" and "is a gap" before planning parity
work.** They read as a to-do list, and a stale one costs twice: the work looks undone, and the
reader who checks discovers the DOCUMENT was wrong rather than the code.

Found false in one pass on 2026-09-03, all four in doc comments:

- `StudioSequences.cs` — *"`AddSequenceLayers` is not implemented"*. Implemented in `EntityModels`,
  both passes, and B307 had just fixed a branch of it.
- `StudioLayout.cs` — *"B82 is open … a halo or a canteen sits at the wearer's feet."* Attachment
  parenting reads, carries and applies `m_iParentAttachment`.
- `SkeletonPose.cs` — *"the flag is not read by this project's `.mdl` parser yet"*, about
  `BONE_FIXED_ALIGNMENT`, in a file that branches on that flag eighty lines below.
- `EntityTracker.cs` — *"instance baselines are not implemented"*, while `BaselineBuilder` does
  them. That grep also turned up the bigger fact: **the type has no production caller at all.**

**Why it happens: a comment is written when the gap is real and nothing re-reads it when the gap
closes.** The implementer works in a different file. So the claim ages in place, sounding
authoritative.

**Search for them in the same session you plan from.** The mirror is
[[an-impossibility-claim-expires]] — a claim that something CANNOT be known, never re-read once it
can. Same failure, opposite sign. Related: [[a-measurement-recorded-as-a-conclusion-expires]].
