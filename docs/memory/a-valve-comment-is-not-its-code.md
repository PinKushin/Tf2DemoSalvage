---
name: a-valve-comment-is-not-its-code
description: "A Valve comment can describe behaviour its own code does not have — \"Will always restart/crossfade positional sounds\" sits over code that stops and restarts at once. Port the statements, cite the comment only as a hint, and when a test's expectation came from a comment, read the branch before trusting it."
metadata:
  node_type: memory
  type: feedback
  modified: 2026-10-04T00:00:00.000Z
---

`AddLoopingSound` (`c_soundscape.cpp:1111`) says *"Will always restart/crossfade positional sounds"*. The code below it
stops the old sound and restarts the slot immediately at its current volume (`:1130-1144`) — no crossfade — and the
"always" is not a rule at all: it is the side effect of scanning the list backwards while new loops are appended
forwards, which crosses two same-wave loops over so both restart even when nothing moved. This project's mixer test
asserted the crossfade, from the comment; it encoded a behaviour the engine does not have (B463).

**Why:** a comment is the author's intent at one moment; the statements are what shipped. Parity is with the
statements.

**How to apply:**
- Quote comments as evidence of intent, never as the behaviour. Transcribe the branch.
- When an expected value came from a comment or a doc, re-derive it from the code before keeping the test.
- An order-insensitive comparison can agree with a bug that permutes: B464's real-map test compared the SET of places
  loops sounded at and passed with slots compacted, because ctf_well kept the same three places with the waves
  rotated. Compare the pair that the bug would break (wave AND place).
