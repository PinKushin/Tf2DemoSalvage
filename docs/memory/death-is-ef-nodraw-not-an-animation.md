---
name: death-is-ef-nodraw-not-an-animation
description: "TF2 never animates a dying player; death is EF_NODRAW on the player plus a separate CTFRagdoll entity — covers why a timer's construction argument says nothing about how long it actually runs once a per-frame think can restart it, why a newly drawn thing that is not exactly the demo's networked entity needs its own index rather than borrowing one, and why a rule the engine states twice in two different functions can differ at the edges so reusing one helper for both silently applies the wrong subject's rule."
metadata: 
  node_type: memory
  type: project
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-09T03:53:10.265Z
---

TF2 has **no death animation for the player model** — a fact about Valve's code, not something
unimplemented. `PLAYERANIMEVENT_DIE` is raised NOWHERE in `game/`; its handler is `Assert(0); //
Should be here - not supporting this yet!` (checked against `PLAYERANIMEVENT_JUMP` as control, which
does return real sites — the zero is a fact about the code, not the search).

`CreateRagdollEntity` (`tf_player.cpp:15637`) ends: `AddSolidFlags(FSOLID_NOT_SOLID);
AddEffects(EF_NODRAW | EF_NOSHADOW);` — the corpse is a separate `CTFRagdoll` with physics. With
ragdolls disabled, the player vanishes after one frame in reference pose (not a T-pose bug).

**One exception, gated on the ragdoll:** `StateThinkDYING` calls `RemoveEffects(EF_NODRAW |
EF_NOSHADOW)` (`// still draw player body`) only when `m_hRagdoll` is non-null. Until this project
builds ragdolls (B58), the body stays hidden — `ScenePlayer.Drawn` is `IsDrawn && alive`, becomes
`IsDrawn` alone once B58 lands.

**Why it mattered:** dead players were gated on `IsVisible` (PVS) rather than `IsDrawn` (`EF_NODRAW`),
so corpses kept drawing — a corpse with `FL_ONGROUND` clear got drawn as `ACT_MP_JUMP_FLOAT`, a
17-second respawn read as a rocket jump. Measured: 535 dead player-ticks drawn, 322 removed by
`EF_NODRAW` alone, 213 by the ragdoll gate.

**"Dead" and "not drawn" are different sets** — `EF_NODRAW` also hides a taunting/teleporting player,
and a dead player is legitimately re-shown during deathcam. Follow the effect the engine tests, not
the state it implies.

Related: [[bone-merge-sends-no-position]], [[output-level-assertion-or-it-is-not-done]].

---

## `a-restarted-timer-is-not-a-lifetime`

**Finding where a timeout is SET isn't finding how long it lasts** — read the per-frame think too.
`cl_ragdoll_fade_time` defaults to 15 (`c_tf_player.cpp:514`) and `CreateTFRagdoll` calls
`StartFadeOut(cl_ragdoll_fade_time)` — cited confidently, wrongly. The think re-arms it every frame
the corpse is on screen, at **a third** of the convar:
```cpp
if ( IsRagdollVisible() ) { StartFadeOut( cl_ragdoll_fade_time.GetFloat() * 0.33f ); return; }
```
(`c_tf_player.cpp:1532-1545`).
So a watched corpse never fades; one out of view expires 5 seconds later. Both halves of "15 seconds"
are wrong. **A lifetime depending on visibility is a CAMERA question**, can't be baked into a
timeline computed once.

**The correct-looking alternative was worse:** drawing each corpse for as long as its entity existed
(no invented number) put 57 bodies on a 12-player map at once — server keeps one ragdoll per player
until next death. "Use what the demo says" isn't automatically conservative.

General shape: grep every write to the timer field before believing its constructor argument. Related:
[[parity-is-the-search-not-the-defence]]; distinct from [[a-default-is-not-a-constant]] (value vs.
restart).

## `a-new-entity-must-not-borrow-an-index`

**When adding something drawn that isn't exactly the demo's entity, give it its own index.**
`EntityModelSet` keys pose/skinning/visibility by entity index, and a demo reuses indices briskly —
sharing crashed `ArgumentOutOfRangeException` on the first corpse frame.

This project already had the pattern: `ViewmodelScene` uses 4096..4098; corpses (B318) now take
2048..4095 — the engine does the same (a client-ragdoll's index is at/above `MAX_EDICTS`, colliding
with nothing server-sent).

**Offsetting isn't enough — the index must be unique per OBJECT**, not just per slot, or the second
occupant of a reused slot gets the first one's caches. Key on position-in-list or the crash returns,
rarer.

**No test caught this** — twelve assemblies and UI suite green across two gate runs; `--measure`
found it in seconds via a real playback run. Run the viewer over a real demo after adding anything
drawn.

Related: [[output-level-assertion-or-it-is-not-done]], [[wire-faithful-is-not-state-faithful]].

## `a-shared-helper-may-hold-another-functions-rule`

**Before reusing a helper for a rule the engine states twice, check both agree — including the
`default` branch.** Team-to-skin exists twice in TF2: `C_TFPlayer::GetSkin` (switch, `default: nSkin
= 0` = RED, `c_tf_player.cpp:7807-7817`) and `C_TFRagdoll::CreateTFRagdoll` (`if RED else BLU` = BLU
with no team, `c_tf_player.cpp:712-719`). Identical for
RED/BLU, diverge at the edge — calling `PlayerSkin.ForTeam` from the ragdoll looked like DRY and was a
divergence (B315).

**Tell:** a bare `else` against an explicit `default:` — different Valve authors, different days,
real difference not stylistic. Caught by `Skin_ForNoTeamAtAll_IsBlu`, written before the code
([[conformance-test-before-implementation]]). Related: [[a-property-name-needs-its-declaring-table]],
[[parity-is-the-search-not-the-defence]].
