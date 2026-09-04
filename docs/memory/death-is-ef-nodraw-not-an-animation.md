---
name: death-is-ef-nodraw-not-an-animation
description: TF2 never animates a dying player; death is EF_NODRAW on the player plus a separate CTFRagdoll entity — covers why a timer's construction argument says nothing about how long it actually runs once a per-frame think can restart it, why a newly drawn thing that is not exactly the demo's networked entity needs its own index rather than borrowing one, and why a rule the engine states twice in two different functions can differ at the edges so reusing one helper for both silently applies the wrong subject's rule.
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

**Three more memories were folded into this one on 2026-09-04**, all about corpses specifically: a
fade timer that restarts every think it is looked at, a per-entity cache keyed on an index a demo
reuses, and a team-to-skin rule the engine states twice with a different default at the edge. Their
names are kept as headings below.

---

## `a-restarted-timer-is-not-a-lifetime`

**Finding where a timeout is SET is not finding how long it lasts.** Read the per-frame think as
well, because a timer something restarts has no relationship to the constant it was given.

`cl_ragdoll_fade_time` defaults to 15 (`c_tf_player.cpp:514`) and `CreateTFRagdoll` ends with
`StartFadeOut( cl_ragdoll_fade_time.GetFloat() )` (`:869`). Read that far and "a corpse lasts 15
seconds" is a cited, confident, wrong answer. The think is where it lives:

```cpp
if ( IsRagdollVisible() )
{
    …
    StartFadeOut( cl_ragdoll_fade_time.GetFloat() * 0.33f );
    return;
}
```

`c_tf_player.cpp:1532-1545`. The timer is re-armed every think the corpse is on screen, at **a
third** of the convar. So **a corpse being looked at never fades at all**, and one that has left view
expires five seconds later. Both halves of "15 seconds" are wrong, and the number and the visibility
dependence are exactly the two things that decide what a viewer sees.

**The consequence for design, not just for the number.** A lifetime that depends on visibility is a
CAMERA question and cannot be baked into a timeline computed once — which is what would have been
built from the convar alone, and it would have been wrong in a way no test of the timeline could
catch.

**And the correct-looking alternative was worse, which is the part to remember.** Having decided the
timer was too subtle, the obvious fallback was to draw each corpse for as long as its ENTITY existed
— no invented number, purely what the demo says. Measured, that put **57 bodies on the map at once
against a twelve-player roster**, because the server keeps one ragdoll per player until that player
next dies. "Use what the demo says" is not automatically the conservative choice: the server's
bookkeeping and the client's drawing are different lifetimes, and only one of them is what a viewer
saw.

**The general shape:** a construction-time `Start(x)` is a hypothesis about duration. Grep the field
the timer writes — here `m_fDeathTime`, four uses in the whole file — and read every one before
believing it. Same family as [[parity-is-the-search-not-the-defence]]; distinct from
[[a-default-is-not-a-constant]], which is about the value being a setting rather than about the
clock being restarted.

---

## `a-new-entity-must-not-borrow-an-index`

**When you add something drawn that is not exactly the demo's entity, give it an index of its own.**
`EntityModelSet` keys the pose, the skinning buffers and the visible set by entity index, and a demo
reuses indices briskly — slot 752 is a prop, then a corpse, then something else. Sharing that index
crashes: `ArgumentOutOfRangeException` inside `Skinning`, on the first frame with a corpse in view.

**Do not over-explain the mechanism, which I did.** The obvious story — a stale pose surviving a
model change — is wrong on its own, because `EntityModelSet` rebuilds the pose whenever the prop's
model path differs from the one it recorded for that index. The real interleaving is narrower and was
not chased down, because the fix does not depend on it. What is measured is that the crash appears
with the shared index and disappears with a private one.

**This project already had the pattern and the new code did not follow it.** `ViewmodelScene` puts
the arms and weapon at 4096..4098 with a comment saying why; corpses (B318) now take 2048..4095. The
engine does the same thing for the same reason — a ragdoll becomes a CLIENT-side entity through
`InitAsClientRagdoll`, and Source gives those indices at or above `MAX_EDICTS`, so they cannot
collide with anything the server sends.

**Offsetting the slot is not enough; the index must be unique per OBJECT.** Adding 2048 to a corpse's
entity index still gives the second occupant of a reused slot the first one's caches. Key on
something unique for the life of the timeline — the position in the list — or the same crash comes
back, rarer and harder to reproduce.

**Follow the index through everything that keys on it.** The corpse fade asks whether it was visible
last frame, and the renderer's visible set holds what it DREW. Left asking under the old slot it
would have reported every corpse unseen and expired them all on the wrong timer: no crash, no failing
test, just a mechanism that runs and is never right.

**No test caught this and the shape of that is worth knowing.** Twelve assemblies and the UI suite
were green across two full gate runs, and the crash was on the first frame with a corpse in view.
Nothing builds a scene where one index carries two models over time, and the corpus suites do not
render. **`--measure` found it in seconds** — a twenty-second playback run made for a performance
check. Run the viewer over a real demo after adding anything drawn, not only the suites.

Related: [[output-level-assertion-or-it-is-not-done]], [[wire-faithful-is-not-state-faithful]].

---

## `a-shared-helper-may-hold-another-functions-rule`

**Before reusing this project's helper for a rule the engine states twice, check that both engine
functions agree — including their `default` branch.** Two functions can compute the same thing for
every value anyone has looked at and diverge outside it, and the reuse then silently applies one
subject's rule to another.

Measured while implementing the corpse appearance (B315). Team-to-skin exists twice in TF2:

```cpp
// C_TFPlayer::GetSkin, c_tf_player.cpp:7807-7817 — what PlayerSkin.ForTeam implements
case TF_TEAM_RED:  nSkin = 0; break;
case TF_TEAM_BLUE: nSkin = 1; break;
default:           nSkin = 0; break;

// C_TFRagdoll::CreateTFRagdoll, c_tf_player.cpp:712-719
if ( m_iTeam == TF_TEAM_RED ) m_nSkin = 0; else m_nSkin = 1;
```

Identical for RED and BLU. A **player** with no team falls to RED; a **corpse** with no team falls
to BLU. Calling `PlayerSkin.ForTeam` from the ragdoll looked like DRY and was a divergence.

**Why this one hides so well.** Every symptom is at an edge nobody photographs, the helper already
carries a citation so it reads as settled ([[parity-is-the-search-not-the-defence]]), and the
suite stays green because no existing test supplies the odd value. The reviewer's eye is drawn to
whether the rule is right, not to whether it is the right subject's rule.

**The tell is a bare `else` against an explicit `default:`.** Valve wrote a switch in one place and
an if/else in the other — different authors, different days, and the difference is real rather than
stylistic. Whenever the engine spells a rule out twice, that is a fact about the engine, not
duplication to be cleaned up.

**Do not merge them afterwards either.** The comment on `RagdollAppearance` says why they are apart,
because a future reader will otherwise see two identical-looking expressions and unify them. That
comment is doing the same job as the test.

Caught by `Skin_ForNoTeamAtAll_IsBlu`, which was written before the code and failed against the
reuse — [[conformance-test-before-implementation]] earning its keep. Related:
[[a-property-name-needs-its-declaring-table]] (the same shape one layer down: a name is not enough,
you need the table it was declared in), [[parity-is-the-search-not-the-defence]].
