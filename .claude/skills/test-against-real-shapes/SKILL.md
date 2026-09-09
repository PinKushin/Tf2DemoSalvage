---
name: test-against-real-shapes
description: >
  Choose the SUBJECT of a test or measurement so it is as close to real data as the question
  allows, even when the data is synthetic. Use when writing a test, fixture or probe for anything
  the engine also does — physics, decode, rendering, animation — and especially when a fixture is
  about to be invented rather than taken from a real file. Also use when a fixture's result is
  steering a design decision.
---

# Test against real shapes

**A synthetic fixture is not the same as an invented subject, and conflating the two cost an
afternoon.** D38 says synthetic fixtures come first, and it is right: a hand-built input has ground
truth where a corpus test only has a second reading of the same file. That rule is about where the
EXPECTED VALUE comes from. It says nothing about what the subject should be, and the two questions
are independent.

The owner, 2026-09-07, after an afternoon of tuning a physics solver against a two-unit cube on one
tilted triangle:

> *"it has to be a parity issue, you need to actually run the demo to check or make synthetic test
> demos that run"*

and

> *"things should probably be tested with as close to real data as possible even when its synthetic
> data"*

## The rule

**Write the expectation yourself. Take the SUBJECT from something the engine actually handles.**

A cube is not a ragdoll. A two-bone skeleton is not a player. A three-line diff is not a patch. When
the subject is invented, a green test proves the code handles a shape nothing will ever hand it, and
a red one sends you tuning against geometry that does not exist.

## What went wrong, so the shape is recognisable

A ragdoll solver was measured on a synthetic cube resting on a synthetic slope. The fixture said the
body slid, so a friction pass was written; the fixture said it slid faster, so the friction was
rewritten; five reversals later the fixture was still the only instrument. **Every one of those
decisions was made about a shape the engine has never simulated.**

The first run against a real `.phy` ragdoll on real map collision said something the cube could not:
five corpses, none of them settling, a symptom the cube never showed because a cube has no joints.

**The tell:** a fixture that has to be adjusted to keep agreeing with the code. If a test's bounds
move more than once for the same subject, the subject is probably wrong.

## How to do it here

**A PROBE is usually the right instrument, not a test** (D126, and `tools/Tf2DemoSalvage.Probe`).
It runs the production path against real installed content, costs no Git LFS, never runs in the
gate, and iterates in seconds. `corpse-drop` is the worked example: real map collision, a real
model's `.phy`, stepped at the demo's own tick rate, no viewer and no desktop.

**Real does not mean the corpus.** The corpus suite is slow and cannot run on the measurement boxes
at all, and most of it was deliberately removed to get the gate down. Real content already on the
machine — an installed map, a shipped model, the game's own `scripts/` — is realer than a fixture
and costs the suite nothing, because a probe is not in the suite.

**When a test genuinely needs a subject, take it from a real file and keep it small.** A real
model's ragdoll has seventeen bodies and joints that constrain each other; a cube has neither, and
the difference is where the bugs live.

**And when the viewer is the only way to see it, that is a smell.** It loads textures, materials and
lightmaps before the thing under test runs at all, takes the desktop, and orphans itself when
killed — three "hangs" in one session were a detached viewer or a wait on the machine-wide lock,
none of them the code. Build the probe instead.

## What this does NOT say

It does not say prefer corpus tests. It does not say a synthetic fixture is wrong — the expected
value should still be one you wrote, because that is what gives a test ground truth. It says the
thing being tested should be a thing the engine has.
