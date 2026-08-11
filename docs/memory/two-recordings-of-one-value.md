---
name: two-recordings-of-one-value
description: When a format stores the same value twice by unrelated routes, comparing them tests a decode against the engine rather than against your own reading.
metadata:
  type: project
---

A demo stores the recording player's view angles **twice**: `democmdinfo_t` writes them as plain
little-endian floats ahead of every packet, and `dem_usercmd` writes them bit-packed behind presence
bits. Neither path can see the other. Measured 2026-08-11: **329,969 of 330,853** packets carry
angles bit-identical to the last user command before them, 99.7%.

**Why:** fixtures and round-trip properties both test a codec against the same interpretation of the
spec that produced it — they cannot falsify a misreading shared by both halves. A second, unrelated
encoding of the same quantity can, because the engine wrote both and this project wrote neither.
It is [[differential-beats-fixtures]] without needing another parser.

**How to apply:** before writing a new decoder, look for whether the value appears anywhere else in
the file by a different route. Compare as **bits**, not with a tolerance — nothing computes these
values, so an epsilon only widens the test enough to hide a real disagreement. Expect a rate rather
than an equality when the two are sampled at different frequencies (input is sent faster than
snapshots), and state the measured rate in the test so a drop is visible.

Related: [[read-the-encoder-not-the-decoder]], [[fixtures-are-the-weak-point]].
