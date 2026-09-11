---
name: conformance-test-before-implementation
description: "Write the conformance test citing Valve's source first, then unit/integration/UI tests, then the implementation; and escalate SDK to Rust parser to decompiler when the SDK cannot answer."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 9b3a8b35-1dc8-47b0-a320-73b01288f10c
  modified: 2026-09-09T03:42:50.564Z
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

Related: [[nothing-is-closed]].

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

Related: [[nothing-is-closed]], [[the-denominator-decides-what-can-be-lost]].

---

## `ask-valve-before-designing-not-after` — a design defended by taste gets undone by taste

**The owner, 2026-08-26, mid-refactor:** *"and how does valve handle these timings?"*

Asked while I was designing a two-clock type for frame timing — tests already written, reasoning
already done, none of it checked against the engine. The design survived unchanged. **The
justification did not**, and that was the valuable part.

Before: *"I'd rather not merge these two clocks; they're stamped at different moments and B209 has
open pacing questions."* An argument from the code we already have, which cannot tell you whether the
code we already have is right.

After: **Valve keeps six distinct time quantities and names each by what it obeys** — `realtime`
follows `host_timescale`, `Plat_FloatTime` deliberately does not, `frametime` versus
`absoluteframetime` is paused versus not, and `curtime` has three documented meanings by context
(`public/globalvars_base.h`). Its own demo free camera, `CalcDemoViewOverride`, flies by
`absoluteframetime` (`view.cpp:153`), and `cl_showfps` reads the same one (`vgui_fpspanel.cpp:166`).
**Merging them would be the divergence.**

**Why this matters beyond the one case:** a design defended by taste gets undone by the next person
with different taste. A design defended by a citation is a fact somebody has to argue with. The
comment that says *"two clocks, because Valve keeps several and here is where it says so"* survives a
refactor; *"two clocks, because I thought about it"* does not.

**It also validated something already there.** B174 had independently arrived at "the meter reads the
camera's clock rather than starting its own" — reasoned out with no citation, and correct. Checking
turns a lucky guess into a documented match.

**How to apply:** the order above is conformance test, then unit tests, then implementation, because
a conformance test written afterwards *"becomes a description of what was built, which is the one
thing a parity test must never be."* I had written the unit tests first. The tell is noticing you are
about to justify a design decision in a comment using the words "because I" — at that point the
question is whether the engine already answered it.

**And when the engine's own answer is unreachable, say so in the same breath.** `fps_max` and the
host frame loop are engine code; `source-sdk-2013` ships no `engine/host.cpp`. What the published
headers still establish is what it *cannot* be — flag that as inference, not reading.

Full write-up with the quoted source: `docs/findings/39-the-engines-frame-clocks.md`.

---

## `decide-home-and-parity-before-writing` — both answers, before the code, in the commit

The owner, 2026-08-25:

> "every time we implement a couple of new things, we have to go back and fix all the archetectural
> and parity issues, the going back over and over is the annoying part."

**The ratio is the evidence.** The initial MVP switch took a day. Undoing the drift that grew back
took another. Bringing the same code to Valve's conventions took a third. Three days of rework
against a couple of features.

**Why:** the project already requires a conformance test with its citation BEFORE implementation,
and that works. Nothing said the same about **structure**, so two questions got answered by
proximity instead — new code went where its neighbours were and copied what was already there.
`AddViewmodel` was written into `MainForm` for exactly that reason, and it cost three viewmodel bugs
their testability.

**How to apply — answer both before writing, and put the answers in the commit:**

1. **What is the engine's arrangement for this job?** One grep of `source-sdk-2013`. If Valve models
   it as a game system, a presenter or a per-frame pass, take that shape and preferably that name —
   `SoundscapeSystem` (`C_SoundscapeSystem`) and `UpdateClientSideAnimations`
   (`C_BaseAnimating::UpdateClientSideAnimations`) are named for their originals so the parity is
   checkable by the next reader rather than rediscoverable.
2. **Which project does it belong in, and can it be tested there?** "The viewer, because that is
   where the caller lives" is the drift starting. A misplaced type takes its tests with it, and those
   tests are what stop the next regression.

**Both are cheap before and expensive after.** A divergence written into a NEW type reads as
deliberate, which is harder to spot than one left in an old method. The going-back is not caused by
the refactors — it is caused by the two minutes not spent when the code was written.

Recorded as a decision in `docs/DECISIONS.md` under D89.

Related: [[valve-parity-is-the-first-principle]], [[output-level-assertion-or-it-is-not-done]].
