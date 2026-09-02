---
name: sound-the-demo-does-not-carry
description: Footsteps and landing sounds are client-predicted and appear in no demo; svc_Sounds carries only what the server sent.
metadata:
  type: project
---

`SoundPopulationProbe` reports **`footstep-like names: 0`** on both a solo POV recording and a full
STV match. The population is ambience, physics impacts, doors, item pickups and voice lines.

**Why:** Source predicts footsteps and landings on the client, so they never travel. `svc_Sounds`
carries what the SERVER chose to emit. The owner's own observation is the confirming detail: the
fall-damage *voice line* is heard while the landing thud is not, because that one is server-sent.

This also explains a count that looked wrong: a solo movement demo carries **89** sounds across
6,826 ticks, against 23,772 in a real match. The quiet one is quiet because one player alone
triggers almost nothing server-side, not because the decode is losing anything.

**How to apply:** before hunting a missing sound in the decode or the playback path, run the probe
and ask whether the demo contains it at all.

## The second half of this was WRONG, corrected 2026-09-02

This entry used to end: *"reproducing footsteps would mean synthesising audio from movement, surface
and speed — authoring rather than replay"*. **A footstep is not derived from movement. It is an
animation event authored into the model at a fixed cycle.** Measured on
`models/player/heavy_animations.mdl`: 44 events are number 7001 with options `left` or `right`,
alternating through the walk and run cycles, and `C_TFPlayer::FireEvent` (`c_tf_player.cpp:9066`)
answers them with a ground surface lookup and `UpdateStepSound`.

So the inputs are the model's own data, the map's own surface and the player's velocity — replay,
like everything else the viewer draws. It stays open for its SIZE (the event traversal plus a
ground trace plus `surfaceproperties`), not because it would be invention.

**The general fault: "we would have to synthesise it" is a claim about a mechanism you have not
read yet.** The first half of this entry — that the demo carries no footstep sounds — was measured
and is right. The second half was an inference about how the client makes them, written in the same
confident register, and it parked the feature for the wrong reason. See
[[an-impossibility-claim-expires]] and [[a-filed-design-choice-may-not-be-one]].

Related: [[ask-whether-the-data-arrived]], [[an-empty-search-needs-a-control]],
[[measure-the-output-not-the-capability]].
