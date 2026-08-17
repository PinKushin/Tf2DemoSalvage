---
name: pov-demos-are-pvs-limited
description: A POV demo contains only what the server transmitted to that one player, so most of the map's props are simply absent; judge the renderer on a SourceTV demo.
metadata:
  type: project
---

A POV `.dem` is one client's **received** packet stream. The server transmits an entity to a client
only when it passes the PVS check, so a POV recording physically cannot contain entities the
recorder could not see. Fly the free camera to the other end of the map and the medkits, ammo packs
and control points there were never in the file.

Measured 2026-08-16 on `tf2-2013-build1729296-pov-cp_badlands.dem` (the UI-test demo) against
`demostf-cp_process_f12-2026-08-08-2207.dem` (SourceTV), same build of the viewer:

| | badlands POV | process STV |
|---|---|---|
| studio props drawn, peak | **16** | **94** |
| `cap_point_base` in one frame | never above **2** | **5** |
| `medkit_small` in one frame | up to 4 | 7 |

The badlands timeline holds 5 cap points, 20 `ammopack_small` and 14 `medkit_small` **over the whole
recording** — they exist, just never at once. That is the shape of a PVS-limited stream, not a
decode gap.

Valve's side: `FL_EDICT_PVSCHECK` is the default transmit state — `CBaseEntity::SetTransmitState`
returns it at `game/server/baseentity.cpp:4025` and `UpdateTransmitState` falls through to it at
`:4096`. Entities opt **out** of PVS (always-transmit); they do not opt in.

**Why this is worth a memory: it imitates a regression perfectly.** It presented as "all the props
went away — the cap point, the health packs, the ammo packs" and consumed a session of bisecting
skin retention, track identity and the draw-loop skip counters, all of which were healthy. The one
question that would have ended it immediately is *which demo*, because every earlier screenshot had
been of a SourceTV recording.

**So: verify rendering on an STV demo.** Use a POV demo only when the point is the recorder's own
view. [[record-both-points-of-view]] is the same distinction from the writer's side.

Related: [[an-empty-search-needs-a-control]] — "no props here" was a fact about the input, not about
the code, and a second demo was the control that showed it.
