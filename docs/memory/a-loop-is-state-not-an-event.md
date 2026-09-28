---
name: a-loop-is-state-not-an-event
description: A looping sound persists until stopped, so anything that replays events or restarts on a change silences it — two separate bugs in one feature.
metadata:
  type: project
---

A one-shot happens at a tick; a **loop holds until something stops it** — treating it as an event
produces silence with no error and no failing test.

Two bugs of this shape (B173 follow-ups, 2026-08-24):
- **`SoundSchedule.Advance` is a cursor over events**, so nothing started the six `ambient_generic`
  hums beginning at tick 4 of cp_process — the first `Advance` (7s = 466 ticks) lands past both the
  tick-4 start and the tick-334 restart. Fixed with `LiveAt(tick)` (last un-stopped sound per
  entity+channel) plus `Repositioned` (true on first call too; `Jumped` is not).
- **A soundscape restart threw away loops nothing had changed.** `UpdateAudioParams` restarts on
  `entIndex`, but `AddLoopingSound` reclaims the matching slot first (`c_soundscape.cpp:1100-1133`,
  *"reuse existing entry (fade from current volume) if possible / this prevents pops"*), matched on
  wave+pitch, not volume.

**Why worth a rule:** cp_process has 21 entities naming `Gorge.Outside`, crossing between them every
few hundred ms against a three-second crossfade — outdoor ambience never rose above ~1/5 volume while
the log showed the correct soundscape the entire time.

**How to apply:** whenever a pass replays sound, ask what should be PLAYING at this instant, not what
HAPPENED at it — and ask it on every seek/reposition/first-frame
([[a-pass-must-establish-its-own-state]]). A live client never faces this (starts once, runs); a
seekable viewer always does. Related: [[parity-is-the-search-not-the-defence]].
