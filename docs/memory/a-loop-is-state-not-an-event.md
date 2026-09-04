---
name: a-loop-is-state-not-an-event
description: A looping sound persists until stopped, so anything that replays events or restarts on a change silences it — two separate bugs in one feature.
metadata:
  type: project
---

A one-shot happens at a tick. A **loop holds until something stops it**, and every part of the
viewer that treats it as an event instead produces silence — never an error, never a failing test.

Two bugs of this shape in one feature (B173 follow-ups, 2026-08-24):

- **`SoundSchedule.Advance` is a cursor over events**, so nothing ever started the six
  `ambient_generic` hums that begin at tick 4 of cp_process. The map load alone is enough: seven
  seconds is 466 ticks, so the first `Advance` lands past both the tick-4 start and the tick-334
  round restart, and the next mention is four minutes later. Fixed with `LiveAt(tick)` — the last
  un-stopped sound per (entity, named channel) — plus `Repositioned`, which is true on the FIRST
  call as well as after a seek. `Jumped` is not, because there is nothing in flight to silence.
- **A soundscape restart threw away loops nothing had changed.** `UpdateAudioParams` restarts on
  `entIndex` and `StartNewSoundscape` zeroes every target — but `AddLoopingSound` reclaims the
  matching slot first (`c_soundscape.cpp:1100-1133`), keeping its current volume, under a comment
  stating the reason: *"will reuse existing entry (fade from current volume) if possible / this
  prevents pops"*. Unpositioned loops always reuse; positioned ones only where the target agrees
  within 0.1 units. Matched on wave and pitch, never volume.

**Why it is worth a rule.** cp_process has 21 entities naming `Gorge.Outside`, so the selection
crossed between them every few hundred milliseconds against a three-second crossfade. The outdoor
ambience never rose above about a fifth of its volume **while the log showed the correct soundscape
chosen the entire time**. Every instrument said the feature worked.

**How to apply:** whenever a pass replays or re-establishes sound, ask what should be PLAYING at
this instant rather than what HAPPENED at it. And on a seek, a reposition or a first frame, that
question has to be asked at all — see [[a-pass-must-establish-its-own-state]]. The engine never
faces this because a live client starts the source once and it simply runs; a viewer that can jump
anywhere does. Related: [[parity-is-the-search-not-the-defence]] — `UpdateAudioParams` alone gives
the wrong answer, and the rule that matters is one function further on.
