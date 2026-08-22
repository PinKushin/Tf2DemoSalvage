---
name: output-level-assertion-or-it-is-not-done
description: A green unit suite says a component works when called; it never says production calls it. Three no-ops shipped this way in one session.
metadata:
  type: feedback
---

**Anything that produces output is not finished until an assertion has read that output on a real
demo.**

**Why:** a unit test proves a component behaves when handed values the test chose. It says nothing
about whether production calls it, or with what. That gap shipped **three no-ops in one session**,
each with a fully green suite:

| What shipped | Why the tests missed it |
|---|---|
| Dumper kill annotation matched `int` | game event fields are typed by their definition; `customkill` arrives as a **byte**, so the pattern matched nothing |
| Kill feed annotated nothing at all | the section resolved every field through a renderer returning **strings**, so the numeric lookup returned null on all 407 lines |
| `m_flPlaybackRate` never applied | decoded, retained and unit-tested — and read by no production code, so every animation played at rate 1 |
Every one was found by **looking at the output**. None was found by the tests covering the code, and
in the first two cases those tests kept passing while the feature did nothing.

## "Decoded but not drawn" is NOT this bug — the distinction cost a wrong filing

**Owner's correction, 2026-08-21**, after fog and gestures were filed as further instances:

> *"the decode should basically be completely done for the most part, the core parser got to 100%
> demo decode before i even started anythign else, I required a real demo to be parsed to our quake
> code then recompiled byte identical into a new demo file"*

So the decoder was finished and validated by round trip before any drawing existed. **Every value
the format carries is decoded, and a long list of them is not yet drawn. That is the architecture
working, not a defect.**

| | what happened | how it is found |
|---|---|---|
| **a no-op** (this entry) | production code was SUPPOSED to read a value and did not, so a feature believed finished silently does nothing | looking at the output |
| **not yet drawn** | decode complete by design, drawing not started | reading the backlog |

`m_flPlaybackRate` is the first kind: the animation path existed, should have used it, and every
animation ran at rate 1 while the feature was thought done. Fog and gestures are the second — no fog
or gesture rendering code exists to have missed anything.

**The test cannot tell them apart, and neither can a grep. What tells them apart is whether anything
CLAIMED the feature was done.** For fog something did: four conformance tests counted as *parity* in
`docs/CONFORMANCE.md`, asserting Valve's shader source and then arithmetic transcribed into helpers
in the same file. That is the defect worth filing (B139) — a gap ledger reporting parity for a
feature with no implementation.

**How to apply:** the consumer sweep — asking what reads each decoded type — is a **backlog query**
and a good one, seconds to run. Treat a result as a bug only when something already asserts the
feature works. Related: [[decode-must-be-total]], [[engine-accepts-authored-demos]].

**How to apply:** write the component tests as usual, then add **one** assertion against the rendered
artefact for a corpus demo — the text the dump produces, the poses the timeline builds, the frame the
renderer picks. One test, and the only one that can fail when the wiring is wrong. When it exists,
verify it by manipulation: break the wiring and watch the output test go red while the unit tests
stay green. That pair is the proof it measures something the others cannot.

The same rule from the other side: **a passing test whose inputs were written by whoever wrote the
code proves the two agree**, not that either matches the demo.

Distinct from [[measure-the-output-not-the-capability]], which is about a *report* built from a
predicate rather than from the artefact. This one is about the *test suite* — the failure is not a
wrong number, it is a feature that never ran.

Related: [[real-data-hides-bugs-small-inputs-expose]], [[logs-are-the-debugger]].
