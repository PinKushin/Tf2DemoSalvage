---
name: a-fallback-that-makes-sound-hides-itself
description: Twice in one feature, a fallback invented where the engine refuses produced audible-but-wrong output that nothing could detect except listening.
metadata:
  type: project
---

Two audio defects, same shape, found only by the owner listening:

- **`bIsAmbient` read as "play at full volume everywhere"** (B168). No published client/engine code
  reads it for gain — Valve expresses "global" via `SNDLVL_NONE` instead. Room tones audible across
  the map. *"the ambient sounds were way way too loud... and started playing at the start of the
  demo even though i was in free cam"*.
- **A positioned soundscape loop played at the listener when its position was missing** (B173). The
  engine SUPPRESSES it: `if ( positionIndex > 31 || !(m_params.localBits & (1<<positionIndex)) )
  return;` (`c_soundscape.cpp:797`). Seven copies of `machine_hum` stacked unattenuated in the ear
  (`Gorge.Inside` places seven, cp_process supplies no positions). *"its specifically the cpu sound
  it seems like"*.

**Why dangerous:** a fallback producing silence gets investigated; one producing SOUND is
indistinguishable from working — nothing in any log or test flags it. Both survived a green suite.

**How to apply:** when the engine refuses a case, refuse it too — before writing "if we lack X, use
Y" in an audio path, find what the engine does (often `return`). Related:
[[fallbacks-do-not-make-guesses-safe]], [[measure-the-output-not-the-capability]],
[[parity-is-the-search-not-the-defence]].
