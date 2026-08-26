---
name: conformance-test-before-implementation
description: "Write the conformance test citing Valve's source first, then unit/integration/UI tests, then the implementation; and escalate SDK to Rust parser to decompiler when the SDK cannot answer."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 9b3a8b35-1dc8-47b0-a320-73b01288f10c
  modified: 2026-08-26T01:02:11.296Z
---

**Order of work on anything that reproduces engine behaviour: conformance test, then the ordinary
tests, then the code.**

**Why:** a parity test written after the implementation describes what was built rather than what
the engine does — the one thing it must never do. Written first, with its citation, it is the place
"what does Valve actually do" gets recorded before any code exists to bias the answer. The owner
made this a standing rule after a session where five defects were all found by reading the SDK late.

**Four sources, and this is a MENU rather than a ladder.** Pick whichever holds the answer and skip
the others; walking them in order wastes time when the question already names its source.

1. `source-sdk-2013` at `F:/src/source-sdk-2013` — shaders, formats, math, message lists. Cite it in
   comments; that convention is why `S125` is disabled repository-wide.
2. **demostf/parser** (Rust) — the demo container and entity decode. Read to cross-check, never
   port. **Skip it outright for anything about rendering; it has never drawn a pixel.**
3. **Valve Developer Community wiki** — conventions the SDK does not spell out. Secondary.
4. **A decompiler** — the closed material system, TF2's own shaders, anything the SDK omits.
   **Reach for it readily; the owner wants this used, not avoided.**

**"Not in the published SDK" is not the end of the line** — it is a signal to pick a different
source, not to guess. `$modblend` is the worked example: TF2 ships it in real VMTs and no published
shader declares it, so a decompile is the right next step.

**The decompiler constraint is REPOSITORY SIZE, not law.** The owner's position is that the legal
question is not a practical concern for this work. What is real: decompiler projects and output are
enormous, a folder committed once lives in the history for ever, and it cannot then be moved to
another disk. So run it with project and output paths under a temp directory outside every git tree,
and carry back only hand-written notes — a constant, a field order, a formula, a line saying where it
came from. Never paste a decompiled function into source.

**Two instruments, not interchangeable.** `SdkCoverageTests` generates the denominator from the SDK
by extraction and can never go stale; the hand-written conformance suites carry the semantics and the
cost of each gap. Only the generated half catches a MISSING feature; only the hand-written half
catches a WRONG one — an extraction cannot tell you `$detail` uses the wrong blend mode.

Related: [[read-the-spec-before-measuring-our-data]], [[measure-every-hop-before-blaming-one]].

---

## The reason is ENUMERATION, not ceremony — B135, 2026-08-21

The owner, after four divergences were found one at a time by staring at screenshots:

> *"dont just implement or fix, conf test then implement/fix. you would have found the divergence if
> you went to conf tests first"*

**That is the argument, and it is stronger than "write the test first because tests are good."**
Writing a conformance test forces the engine's behaviour to be *enumerated* — you have to go and read
what it does across the whole feature to know what to assert. Reacting to a symptom only ever finds
the one thing that showed, and each fix then exposes the next.

Measured: B135 was four divergences at once — pass order, cull mode, depth writes, and a depth bias —
in the overlay path. They were found across an evening, one per screenshot, each fix revealing the
next symptom. **Two more turned up in the minute it took to start writing the conformance test**, and
neither had produced a symptom anyone had noticed: overlay render order (four layers packed into
`m_nFaceCountAndRenderOrder`, parsed here and then ignored) and overlay fade (`LUMP_OVERLAY_FADES`,
lump 60, not read at all).

**So the order is not test-then-code for its own sake.** It is: go and read the whole of what the
engine does for this feature, write it down as assertions with citations, and only then look at what
we do. The divergences fall out of the reading. Fixing from a picture finds one at a time, in the
order the pictures happen to reveal them, which is the slowest possible sequence.

**The tell that this rule is being skipped:** editing renderer code with a citation in the commit
message and no test beside it. A citation in prose does not redden when someone changes the value
back, and it does not enumerate anything.

---

## Read the coverage report before re-deriving the gap — 2026-08-25

Asked which world lumps the engine loads that we do not (B194), I grepped `Mod_Load*` out of a
shipped `engine.dll` and worked out that vertex normals were missing.

**`docs/SDK-COVERAGE.md` already said "27 of 66" and already named `LUMP_VERTNORMALS`.** The
generated instrument had the answer before the measurement started. The owner's response was the
rule:

> *"yep thats why i say conformance tests first too"*

**So the enumeration argument has a second half.** Writing the conformance test first enumerates the
engine's behaviour — and once written, the generated half of that instrument *keeps* the enumeration,
so the next question of the form "what are we missing" is a file to read rather than a measurement to
take. Re-deriving it by hand is slower, produces a subset, and cannot be checked against anything.

**Check the generated report first whenever the question is "what is missing".** Measurement can only
find data that is wrong; it cannot find a feature that was never implemented.

Related: [[read-the-spec-before-measuring-our-data]], [[an-uncoverable-gap-is-usually-your-reader]].
