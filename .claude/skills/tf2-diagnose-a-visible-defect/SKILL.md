---
name: tf2-diagnose-a-visible-defect
description: Use when the owner reports something LOOKING wrong — a door, a ragdoll, an animation, a missing model, a jitter, anything he saw on screen. Fixes the order of investigation so the first measurement is taken through the path the renderer actually calls, and stops a component measurement from being reported as the behaviour he can see.
---

# Diagnosing something the owner saw

**He is the only instrument that has looked at it.** Every number here comes from code that might be
measuring the wrong object, and on 2026-09-09 four instruments in a row did — while he watched a spawn
door open once and stay open.

## The order, and the first step is not a measurement

1. **Write down his words verbatim** in the risk entry before anything else. *"the spawn doors are just
   staying open after they open once"* is a different defect from *"the doors are slow"*, and the
   session that conflated them chased a rate error for hours. His phrasing carries the mechanism:
   "stays open" is coverage, "slow" is rate, "glitch open then closed" is direction, "didn't draw until
   a player used it" is existence.

2. **Read the engine for that mechanism.** Every branch, and the overrides. This is `valve-parity-audit`
   and `block-fix-without-parity` already; it is step 2 rather than step 1 only because his words decide
   WHICH mechanism to read.

3. **Measure at the OUTPUT first.** Not at the component. See below — this is the step that keeps being
   skipped.

4. **Only then look at components.** A component measurement is not wrong; it is about a component.

## The output is `PropsAt`, not `At`

`ScenePropTrack.At` is the sampler. **The viewer draws what `DemoTimeline.PropsAt` hands it**, which is a
CACHED `SceneProp` rebuilt only when a scheduler says the answer moved. Those are two mechanisms that
have to agree, and when they disagree the screen is wrong while every sampler measurement is right.

Measured that day on `20130518_0313_cp_granary_blu_blu`:

| measured through | said |
|---|---|
| `At` — phase at every history entry | exact on 7,065 of 7,068 |
| `At` — range covered | every door reaches both ends of its stated range |
| `At` — worst discontinuity | 9.019 → 3.290 units |
| **`PropsAt` — what was handed to the renderer** | **458 of 15,728 ticks disagreed with the track, by up to 12.47 units** |

The first three were true. None was about what was drawn.

**The cheapest honest output measurement** is a drawn-vs-sampled sweep: walk ticks, call
`timeline.PropsAt(tick, into)`, and compare each prop against what its own track answers for that tick.
It is in `jitter`'s `Drawn`. A count of disagreements plus the worst delta plus a count of
drawable-but-absent props answers "is the screen being told the truth" in one number, and it needs no
model of any curve.

**For anything the sampler cannot express** — a missing model, a wrong material, a contorted skeleton —
the output is a picture: `--shot` with `TF2VIEW_CAMERA`, per `docs/memory/take-your-own-screenshot.md`.

## A duration cannot test a curve

If the symptom is about SPEED, do not compare a drawn motion against a constant-speed ramp. A hermite
eases out of a held position whenever the third sample equals the second, so its time between two
positions exceeds a straight line's **with nothing wrong** — and that ease-in is exactly what "very close
to the door by the time it opens" looks like. Three duration metrics failed on this before one worked.

**Assert where the interpolation collapses to an identity instead.** At a fraction of zero
`Lerp_Hermite` returns its own sample whatever the tangents are (`interpolatedvar.h:845`), so the drawn
value at `changetime + delay` must equal that entry's value exactly, for every entry, with no curve model
involved. Full reasoning: `docs/memory/the-interpolation-pair-is-found-by-changetime.md#a-fraction-of-zero-is-an-oracle`.

## Before reporting anything back to him

- **Name which path the number came through.** "Phase is exact" and "the screen is right" are different
  claims and only one of them is about his complaint.
- **If nobody has looked, say so in those words.** `ask-drawn-claim-has-drawn-evidence.ps1` enforces it at
  commit time; saying it in the message costs nothing and prevents the third announcement of a fix he
  then falsifies by looking.
- **A green suite is not a look.** Four announcements went out that day over a green gate.

## Traps this project has actually hit here

- **Three correct measurements in a row means the question is wrong, not the data**
  (`docs/memory/nothing-is-closed.md#read-the-spec-before-measuring-our-data`). The tell fired four times that day and was
  not read.
- **An instrument that disagrees with his eyes is the suspect, not his eyes.** The first door survey
  reported the doors drawn FASTER than stated — the opposite of his report — because it measured packet
  cadence rather than door speed. It would have been filed as "no defect found".
- **A fix whose test cannot fail.** Sabotage it: a one-tick clock correction reproduced nothing and
  reddened nothing, and the fault needed changetimes far above the live ones to appear at all.
- **An entity index does not name a track.** Entity 328 owns eight on that demo, so `TrackFor` hands back
  one of them and a per-entity probe reports "fewer than three samples". Select on index PLUS tick,
  through `Alive`.
- **Symptoms that share a shape may not share a mechanism.** "Doors stay open", "cabinets don't draw
  until used" and "weapons missing" all look like *not drawn until it changes*; the first was the
  scheduler and the others were measured to be nothing to do with it (zero props drawable-but-absent).
