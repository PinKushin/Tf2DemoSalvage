---
name: a-fallback-that-makes-sound-hides-itself
description: Twice in one feature, a fallback invented where the engine refuses produced audible-but-wrong output that nothing could detect except listening.
metadata:
  type: project
---

Two defects in the audio work, both the same shape, both found only by the owner listening:

- **`bIsAmbient` read as "play at full volume everywhere"** (B168). The flag is on the wire and **no
  published client or engine code reads it for gain** — Valve expresses "global" through
  `SNDLVL_NONE` instead. Result: room tones audible across the map. *"the ambient sounds were way
  way too loud... and started playing at the start of the demo even though i was in free cam"*.
- **A positioned soundscape loop played at the listener when its position was missing** (B173). The
  engine SUPPRESSES it — `if ( positionIndex > 31 || !(m_params.localBits & (1<<positionIndex)) )
  return;` (`c_soundscape.cpp:797`). Result: seven copies of `machine_hum` stacked unattenuated in
  the ear, because `Gorge.Inside` places seven and cp_process supplies no positions. *"its
  specifically the cpu sound it seems like"*.

**Why this shape is dangerous:** a fallback that produces silence gets investigated. A fallback that
produces SOUND is indistinguishable from a working feature — the demo plays, audio comes out, and
nothing in any log or test says the wrong thing is happening. Both survived a green suite.

**How to apply:** when the engine refuses a case, refuse it too. Before writing "if we do not have
X, use Y instead" in an audio path, find what the engine does in that case — the answer is often
`return`. A guess that makes noise cannot be caught by anything except a person listening, which is
the most expensive instrument this project has. Related:
[[fallbacks-do-not-make-guesses-safe]], [[measure-the-output-not-the-capability]],
[[parity-is-the-search-not-the-defence]].
