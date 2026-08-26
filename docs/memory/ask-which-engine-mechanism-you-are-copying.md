---
name: ask-which-engine-mechanism-you-are-copying
description: Source had two free cameras; the audit cited the wrong one and a citation makes a wrong reference look settled.
metadata:
  type: project
---

The free camera flew at 600 units a second, a number reasoned from the keyboard-repeat defect it
replaced rather than from the engine. The parity audit found that correctly, then offered
`CalcDemoViewOverride` (`view.cpp:153`) as the reference — **the engine's demo-playback camera**, so
apparently the obvious match for a demo viewer. 320 u/s at scale 1.

The owner picked the other one:

> *"the correct speeds to use are spectator speeds, im pretty sure, idk what the demo cam speed is
> even actually for becasue ive never seen a tf2 server which has spectators off really"*

and they were right for a reason the question never surfaced: **`cl_demoviewoverride` ships `"0"`**,
so it is a feature almost nobody has ever switched on. The roaming spectator is what a demo viewer is
actually imitating, and its numbers are four times different — 960 u/s, via
`FullObserverMove` → `FullNoClipMove( sv_specspeed 3, sv_specaccelerate 5 )` and a
`sv_maxspeed × factor` clamp.

**The danger is specifically that a citation makes a wrong reference look settled.** An uncited number
invites the question "where did that come from"; `view.cpp:153` closes it. Had this shipped, the free
camera would have been wrong at a *quarter* of the correct speed with a source comment defending it,
and the next reader would have had no reason to look.

**How to apply:** before citing an engine mechanism, ask whether it is the ONLY one for that job, and
check what its enabling convar defaults to — a mechanism that ships off is rarely the one users
experience. When there are two, which one we copy is a decision to record (D102), not a detail to
pick. Related: [[name-the-reading-you-picked]], [[a-default-is-not-a-constant]],
[[ask-valve-before-designing-not-after]], [[a-divergence-is-asked-not-documented]].
