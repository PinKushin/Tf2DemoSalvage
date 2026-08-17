---
name: death-is-ef-nodraw-not-an-animation
description: TF2 never animates a dying player; death is EF_NODRAW on the player plus a separate CTFRagdoll entity.
metadata:
  type: project
---

TF2 has **no death animation for the player model**, and this is a fact about Valve's code rather
than something this project has yet to implement.

`CMultiPlayerAnimState::HandleDying` exists and sets `ACT_DIESIMPLE`, but `m_bDying` can only be set
by `PLAYERANIMEVENT_DIE`, and that event is raised **nowhere** in the `game/` tree — its handler is
`Assert( 0 ); // Should be here - not supporting this yet!`. Checked with `PLAYERANIMEVENT_JUMP` as a
control, which does return real raise sites, so the zero is a fact about the code and not about the
search ([[an-empty-search-needs-a-control]]).

What actually happens is at the end of `CreateRagdollEntity`, `tf_player.cpp:15637`:

```cpp
// Turn off the player.
AddSolidFlags( FSOLID_NOT_SOLID );
AddEffects( EF_NODRAW | EF_NOSHADOW );
```

The corpse is a separate `CTFRagdoll` entity with physics. With ragdolls disabled in-game the player
just vanishes after one frame of the model in its **reference pose** — hands at the sides, no
sequence playing. That single frame is the model drawn with no activity, not a T-pose bug.

**One exception, and it is gated on the ragdoll.** `StateThinkDYING` calls
`RemoveEffects( EF_NODRAW | EF_NOSHADOW )` — commented `// still draw player body` — but only when
`m_hRagdoll` is non-null. So the body is re-shown only once a corpse exists to justify it. Until
this project builds ragdolls (B58), that condition is false for every death it can render, which is
why `ScenePlayer.Drawn` is `IsDrawn && alive` and becomes `IsDrawn` alone when B58 lands.

**Why it mattered:** dead players were gated on `IsVisible` (the PVS) rather than `IsDrawn`
(`EF_NODRAW`), so corpses kept drawing. Once activity selection began reading `m_fFlags`, a corpse
with `FL_ONGROUND` clear was drawn as `ACT_MP_JUMP_FLOAT` — a 17-second respawn animated as a rocket
jump, and it was the owner who spotted that no such jump is possible. Measured: 535 dead
player-ticks drawn, 322 removed by `EF_NODRAW` alone, 213 by the ragdoll gate above.

**"Dead" and "not drawn" are different sets, and reading only life state gets both wrong.**
`EF_NODRAW` also hides a player mid-taunt or riding a teleporter, and a dead player is legitimately
re-shown during the deathcam. Follow the effect the engine tests, not the state it implies.

See [[bone-merge-sends-no-position]] for the other case where the engine's drawing decision is not
where you would look for it, and [[output-level-assertion-or-it-is-not-done]] — this was caught by
watching a demo, never by the tests covering the code.
