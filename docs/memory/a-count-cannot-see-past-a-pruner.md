---
name: a-count-cannot-see-past-a-pruner
description: Waiting for a file count to grow fails silently once retention caps the folder; compare the set.
metadata:
  type: project
---

**A test that waits for "more files than before" stops working the moment something prunes the
folder.** The viewer keeps its twenty most recent captures and prunes *after* writing, so at the cap
a new picture replaces an old one and the count is identical on both sides.
`F12WritesAPictureOfWhatTheViewerDrew` waited on `Shots().Length > before.Length` and timed out with
"F12 produced no picture" against a picture sitting on disk.

Fixed by comparing the set: `Shots().Except(before).Any()`, and taking the newest by name rather than
`Single()`, since the prune can also remove one between two listings.

**Why:** it is intermittent by construction — correct until the folder fills, then wrong for ever —
so it reads as flake, and it appeared here only after a second capture test made the cap arrive
sooner. The count is not faithful to the question, which is "did a file that was not there before
appear".

**How to apply:** anywhere a directory has retention ([[one-place-or-it-drifts]] put the prune next
to the writer, which is right and is also what creates this), compare identities, never counts. The
same shape applies to log files and to `~/measurements/` on the boxes. Related:
[[instrument-bugs-outnumber-decoder-bugs]], [[an-empty-search-needs-a-control]].
