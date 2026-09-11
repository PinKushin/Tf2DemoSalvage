---
name: research-before-code
description: "Check Valve's source before writing or changing anything, and let it outrank every other authority including the owner's recollection and mine."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-09T03:32:56.907Z
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

**A confirming instance rather than a correction: this project's own history is the strongest case
for the rule, at a larger scale than any single bug.** The owner, 2026-08-24, on the corpus and
dating gaps: *"i was going to make this with or without demo examples, and pray it worked for
untested demos, because there is plenty of information available to reverse the changes and account
for them without actually having to have a demo from every protocol, or client ever. we did most of
our demo decode work before we ever had a launch tf2 client, but it worked as soon as we passed a
demo in because the changes had all been documented online or by referencing earlier sdk's."*

The decode logic was built and believed correct **before any demo existed to test it against**,
purely from published changelogs and earlier SDK branches, and it worked on the first real file.
That is the loop above run at the scale of an entire subsystem rather than one field: research first,
and a specimen to test against is corroboration, not a precondition. See `docs/DECISIONS.md` D5 —
an open corpus or dating gap is not blocked work, and treating one as blocking is the category error
this project already made once (`z1800.dem`, dated wrong from its protocol number before anyone
read what was actually inside it).

**How to apply:** before writing an expected value, ask what program WROTE the file and whether that
program is published — prefer the encoder to the decoder. State the hypothesis, find the passage
that settles it, then code. Related: [[decode-must-be-total]],
[[read-the-encoder-not-the-decoder]],
[[nothing-is-closed]], [[era-axis-is-measured]].

---

## `valve-publishes-bitbuf` — bit-level wire questions are a read, not a decompile

`ValveSoftware/source-sdk-2013` contains `src/tier1/bitbuf.cpp` — the real `bf_write` and
`bf_read`, including `WriteBitCoord`, `WriteBitCoordMP`, `WriteBitAngle`, `WriteUBitVar` and
the varint helpers. Fetch it with:

```bash
gh api repos/ValveSoftware/source-sdk-2013/contents/src/tier1/bitbuf.cpp?ref=master --jq .content | base64 -d
```

(The `mp/src/...` path 404s; it is `src/tier1/...`.)

**This outranks a decompile for anything in tier1**, and it is the encoder rather than a
decoder, which is the side that states intent — see [[read-the-encoder-not-the-decoder]].
`WriteBitCoordMP` settled the field order and the in-bounds predicate on 2026-08-11 in one
read, after six hypotheses had been tested against the corpus.

What it does **not** cover: anything TF2-specific or engine-internal that never shipped in
the SDK — the demo container, `svc_` message framing, `SendTable` flattening. Those still
need the corpus, a second parser, or a disassembler.

**How to apply:** before reaching for Ghidra on a bit-level question, check whether the code
is in tier1 or in the public game code. Only drop to a decompile for engine binaries.
