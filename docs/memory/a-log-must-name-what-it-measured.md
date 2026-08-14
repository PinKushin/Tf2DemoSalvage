---
name: a-log-must-name-what-it-measured
description: A log that reports the wrong quantity is worse than none — it is trusted exactly as much as a correct one.
metadata:
  type: feedback
---

A log line is an instrument. **One that measures the wrong quantity gets trusted exactly as much as
one that measures the right quantity**, so it does not merely fail to help — it misdirects, and it
does so with authority.

**Why:** the owner put it as the cost of overlogging — "logs that are measuring the wrong thing
being used as the measure for what you want to measure". It has now happened repeatedly in one
project:

- `"material not found; tried …"` fired when the VMT resolved fine and its TEXTURE did not. Reading
  it literally sent an investigation into path joining and archive mounting, both of which were
  correct.
- `"baked frame 0 of 1"` printed for every skinned player however it was moving. True, and about a
  quantity nobody wanted.
- `"skinning … over 2 animations"` printed the local animation count while the number beside it was
  computed from 469 merged sequences.
- `"fastest 1990 units a second"` was the probe's own 2000-unit filter being hit, not a speed.
- An extents line said `ON ITS SIDE` for models that were correct, because it kept its wording when
  a second kind of model arrived.
- A seam probe indexed baked frames a skinned model does not have and **crashed the viewer** — a
  diagnostic taking down the thing it was meant to explain.

**How to apply:** name the quantity in the message, and say which case you are in when one line
serves two. Prefer "VMT missing" and "VMT found, texture missing" over one "not found". When a
second kind of subject starts flowing through a log, re-read what its words now claim. And a log
that can be wrong about its subject is worse than no log, because the absence of a line invites a
measurement while a wrong line ends one.

Related: [[logs-are-the-debugger]] is why the logging exists at all, and
[[instrument-bugs-outnumber-decoder-bugs]] is the same failure in tests and probes.
