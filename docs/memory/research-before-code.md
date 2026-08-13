---
name: research-before-code
description: Check Valve's source before writing or changing anything, and let it outrank every other authority including the owner's recollection and mine.
metadata:
  type: feedback
---

**Check the source first, every time, before doing anything.** The loop is: hypothesis, research
from first sources or decomp, refine, confirm the sources do not already answer it, then test.

**The source outranks everything else, including the owner's assertions and my own reasoning.** The
owner's instruction, verbatim in intent: if they say something wrong or misremembered, say so and do
the right thing. Deference to a stated belief that the code contradicts is not politeness, it is a
defect waiting to ship.

A local clone lives at `F:\src\source-sdk-2013` — outside every repository, per the rule that
Valve's source and decompiler output never enter this tree or its history. Grep it; it answers in
one command what several web fetches could not.

**Measured cases, all from one session:**

- The owner said loose files override VPKs. `gameinfo.txt` lists the VPKs above the loose mod path,
  and the folklore is half-right for a different reason — `tf/custom/*` is listed FIRST, which is
  why HUDs win. Working code was nearly inverted to match the recollection. They corrected it
  themselves; the point is that the file already had the answer before anyone spoke.
- Props drew at half brightness. A measurement said props averaged 0.2309 against the world's
  0.4704, and that was explained as a missing gamma step, because `0.23 ^ (1/2.2)` is 0.495 and also
  lands near 0.47. The shader settles it: `cOverbright 2.0f`, and the ratio was 2.04. Two curves
  through one point, and only the source distinguishes them.
- Displacement lightmap coordinates are ASSIGNED from the corner ordering, never projected through
  `lightmapVecs`. Projecting looked obviously right and put 219 of 578 faces outside their own
  lightmap.

**How to apply:** before writing an expected value, ask what program WROTE the file and whether that
program is published — prefer the encoder to the decoder. State the hypothesis, find the passage
that settles it, then code. Related: [[decode-must-be-total]],
[[read-the-encoder-not-the-decoder]], [[valve-publishes-bitbuf]],
[[binaries-answer-what-the-sdk-cannot]].
