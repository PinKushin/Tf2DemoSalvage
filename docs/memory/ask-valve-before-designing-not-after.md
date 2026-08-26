---
name: ask-valve-before-designing-not-after
description: A design defended by taste gets undone by the next person's taste; check the engine first and cite it.
metadata:
  type: feedback
---

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

**How to apply:** `CLAUDE.md` already says the order is **conformance test, then unit tests, then
implementation**, and that a conformance test written afterwards *"becomes a description of what was
built, which is the one thing a parity test must never be."* I had written the unit tests first. The
tell is noticing you are about to justify a design decision in a comment using the words "because I"
— at that point the question is whether the engine already answered it.

**And when the engine's own answer is unreachable, say so in the same breath.** `fps_max` and the
host frame loop are engine code; `source-sdk-2013` ships no `engine/host.cpp`. What the published
headers still establish is what it *cannot* be — flag that as inference, not reading.

Full write-up with the quoted source: `docs/findings/39-the-engines-frame-clocks.md`.

Related: [[valve-parity-is-the-first-principle]], [[conformance-test-before-implementation]],
[[read-the-spec-before-measuring-our-data]], [[a-divergence-is-asked-not-documented]].
