---
name: sound-the-demo-does-not-carry
description: Footsteps and landing sounds are client-predicted and appear in no demo; svc_Sounds carries only what the server sent.
metadata: 
  node_type: memory
  type: project
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-09T03:53:36.243Z
---

A sound census reports zero footstep-like names on both a solo recording and a full match — the
population is ambience, physics impacts, doors, pickups, voice lines.

**Why:** Source predicts footsteps/landings client-side; they never travel. `svc_Sounds` carries only
what the SERVER emitted — confirmed by the fall-damage voice line (server-sent, present) vs. the
landing thud (client-predicted, absent). Also explains a count that looked wrong: a solo movement demo
carries far fewer sounds than a real match, because one player alone triggers almost nothing
server-side.

**How to apply:** before hunting a missing sound in decode/playback, run the probe and ask whether the
demo contains it at all.

## The second half of this was WRONG, corrected 2026-09-02

Originally ended claiming footsteps "would mean synthesising audio from movement... authoring rather
than replay." **Wrong — a footstep is an animation EVENT authored into the model at a fixed cycle**
(measured on `models/player/heavy_animations.mdl`: 44 events numbered 7001 alternate `left`/`right`
through the walk/run cycles, answered by `C_TFPlayer::FireEvent`, `c_tf_player.cpp:9066`, with a
ground surface lookup and `UpdateStepSound`). So the inputs are the
model's own data, the map's surface, and velocity — replay, like everything else. Stays open for its
SIZE, not because it would be invention.

**General fault: "we would have to synthesise it" is a claim about a mechanism you haven't read
yet.** The first half (demo carries no footstep sounds) was measured and right; the second was an
unread inference in the same confident register. See [[filing-a-divergence-is-not-fixing-it]],
[[a-filed-design-choice-may-not-be-one]].

Related: [[ask-whether-the-data-arrived]], [[instrument-bugs-outnumber-decoder-bugs]],
[[measure-the-output-not-the-capability]].
