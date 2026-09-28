---
name: surf-and-jump-are-an-audience
description: "The parser is partly for surf and jump communities documenting old runs, which sets what must decode exactly."
metadata: 
  node_type: memory
  type: project
  originSessionId: 9b3a8b35-1dc8-47b0-a320-73b01288f10c
  modified: 2026-08-16T16:00:56.599Z
---

**TF2's surf and jump communities are a named audience for this parser.** Old runs live in demos the
live client can no longer play, and those communities want them documented — the same problem this
project exists for, from a direction not written down before.

**Not the same "surf" as `SURF_*`** — those are texinfo bits (sky, nodraw, hint), unrelated to the
game mode; the collision is worth naming so a session doesn't build the wrong thing confidently.

**What a run needs, most load-bearing first:** `dem_usercmd` (view angles, sidemove/forwardmove — IS
the strafe, already decoded); position from `m_vecOrigin` and true velocity from
`DT_LocalPlayerExclusive` (derived speed from position deltas is only an approximation); tick timing
(a run's time is a tick count, so an off-by-one is a wrong record); zone/timer events (usually
plugin-driven, arriving as user messages or trigger entity state, not a documented message).

**Why it matters for priorities:** rendering can be approximate here; the numbers cannot. A wrong
material is cosmetic; a wrong tick/angle/origin is a falsified record.

Related: [[nothing-is-closed]], [[decode-must-be-total]].
