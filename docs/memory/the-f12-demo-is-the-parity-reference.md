---
name: the-f12-demo-is-the-parity-reference
description: "Hold the demo constant across a comparison and announce any change; f12 is today's reference because the owner knows it, and familiarity is earned rather than fixed."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-08-26T03:43:52.579Z
---

## The trigger is BOOTING, not comparing

Booting the viewer on an unfamiliar map (name matched something mentioned in passing) led to a
reported defect being diagnosed as a regression twice, corrected twice, and only after switching to
f12 did the owner say it was pre-existing: *"another example of why you use the FUCKING RIGHT
REFERENCE."*

**Any launch the owner is going to LOOK AT uses f12, unless he named a different demo.** Not "when
comparing" — when booting. By the time a comparison is recognised as one, the wrong subject is
already on screen. Tell: the owner describing a defect on a map neither of you knows well.

## The rule is HOLD THE SUBJECT, not "always f12"

Owner: *"its not a hard rule forever, if you play another demo enough then i can use it for checks
too, but the problem comes when you change demos in the middle... i dont realize immedietly that im
watching a different demo."*

**The defect is the SWAP, not the choice.** Announce the demo by name every time it changes, in the
message. A SIMILAR-looking map is the dangerous case (two near-identical map compiles), not the safe
one — an obvious swap gets noticed, a subtle one doesn't.

**Familiarity is earned, not fixed** — any demo watched enough becomes a reference. **The MAP counts
as the subject too, and changing it is the expensive half** — every map has its own normal, and
swapping discards the remembered baseline.

## Why f12 is the one today

Owner: *"we use the f_12 demo for parity to the old code checks, because that is the demo i know the
best outside of my era specimins."* On a UI question the owner's eye is the instrument, and it only
works on a subject he knows — a random demo only ever gets "something looks off", where an evening
goes.

**Measured:** picking an unfamiliar demo for a before/after check produced five "regressions",
six falsified hypotheses, none of it the refactor — the demo was simply unexamined AND one the live
TF2 client refuses outright (B201, schema drift).

**How to apply:**
- Parity/before-after/"did I break it" — use f12.
- Era questions — use the gcor specimens (dated exactly).
- An unfamiliar demo is for finding NEW defects, never judging a change.
- Before reporting a regression from an unfamiliar demo, run the OLD build on the same file first.

## `z1800.dem` is DAYTIME harvest, and that is why it is the founding specimen

Picking a Halloween-night map for a grass-visibility check burned four captures on illegible terrain.
Owner: *"idk why you were not using the z1800 demo, its daytime harvest... process_f12 is good too,
since it stresses different than the harvest which is mostly static according to you."*

- `z1800.dem` is harvest in DAYLIGHT — the file for anything needing to be SEEN on that map.
- Pick the map by what it STRESSES, using a census already taken (harvest and granary exercise
  opposite halves of a feature). When a measurement says two maps differ in kind, verify on both.
- Say what a map is "mostly" from the data, not impression.

Wait on the FILE (`until [ -f … ]`), never a listing — the viewer takes 20-30s to boot.

Related: [[ask-which-input-differs-before-bisecting]], [[a-picture-is-assertable]],
[[record-both-points-of-view]], [[author-the-specimen-the-corpus-lacks]],
[[point-the-camera-from-the-data]].
