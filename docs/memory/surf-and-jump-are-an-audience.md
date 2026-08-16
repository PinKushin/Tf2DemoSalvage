---
name: surf-and-jump-are-an-audience
description: "The parser is partly for surf and jump communities documenting old runs, which sets what must decode exactly."
metadata: 
  node_type: memory
  type: project
  originSessionId: 9b3a8b35-1dc8-47b0-a320-73b01288f10c
  modified: 2026-08-16T16:00:56.599Z
---

**TF2's surf and jump communities are a named audience for this parser** (owner, 2026-08-16). Old
runs live in demos the live client can no longer play, and those communities want them documented —
which is the same problem this project exists for, from a direction that was not written down before.

**It is not the same "surf" as `SURF_*`.** Those are texinfo bits in `bspflags.h` — sky, nodraw,
hint, bumplight — and have nothing to do with the game mode. The collision is worth naming because a
session that reads one as the other will build the wrong thing confidently.

**What a run needs, in order of how load-bearing it is:**

- **`dem_usercmd`** — view angles and `sidemove`/`forwardmove` per tick. This IS the strafe, and it
  is what separates a documented run from a video of one. Already decoded (see the usercmd work).
- **Position and velocity per tick.** Position comes from `m_vecOrigin`; a recording player's own
  velocity is in `DT_LocalPlayerExclusive`, and derived speed from position deltas is an
  approximation of it, not the same number.
- **Tick timing.** A run's time is a tick count, so an off-by-one in tick attribution is a wrong
  record, not a rounding difference.
- **Zone and timer events**, which on most surf/jump servers are plugin-driven rather than engine
  entities — so they arrive as user messages or as `func_button`/trigger entity state, not as a
  documented message.

**Why it matters for priorities:** the viewer's rendering can be approximate and still be useful
here; the numbers cannot. A wrong material is a cosmetic defect for this audience and a wrong tick,
angle or origin is a falsified record.

Related: [[read-the-spec-before-measuring-our-data]], [[decode-must-be-total]].
