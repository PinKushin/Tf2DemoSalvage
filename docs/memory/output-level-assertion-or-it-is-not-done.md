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
