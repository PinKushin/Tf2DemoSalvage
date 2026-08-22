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
| **Fog never rendered** (2026-08-21, B139) | `SceneFog` decoded per tick, kept on the timeline, and mentioned nowhere in `Tf2DemoSalvage.Viewer3D`. Every consumer of `FogAt`/`FogSamples` in the repository is a test |

Every one was found by **looking at the output**. None was found by the tests covering the code, and
in the first two cases those tests kept passing while the feature did nothing.

**The fourth was found a different way, and the way is worth copying: by asking what CONSUMES the
value.** A conformance suite existed for fog and had four tests — every one asserting Valve's shader
source and then checking arithmetic transcribed into helper functions in the same file. `Squared(0.5f)
.ShouldBe(0.25f)` tests that squaring squares. Rewriting it to compare against *ours* found there was
no *ours*.

So: **a grep for the consumers is cheaper than a rendered-artefact test and catches this exact
class.** `SceneFog` is a type; nothing in the renderer's assembly mentions it. That check is now a
gap marker with a control — it sweeps the assembly for members naming the type, and separately
asserts the same sweep finds a scene type the renderer really does use, so "not found" cannot be an
artefact of looking in the wrong place ([[an-empty-search-needs-a-control]]).

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
