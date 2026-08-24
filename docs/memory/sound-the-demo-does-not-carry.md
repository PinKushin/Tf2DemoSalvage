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
and ask whether the demo contains it at all. Reproducing footsteps would mean synthesising audio
from movement, surface and speed — authoring rather than replay, and a deliberate decision (B172).
Related: [[ask-whether-the-data-arrived]], [[an-empty-search-needs-a-control]],
[[measure-the-output-not-the-capability]].
