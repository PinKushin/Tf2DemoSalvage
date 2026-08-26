# 38 — Which players an overview may show, and why the origin is special

**Written 2026-08-26, as `MapOverview` was deleted** (D98). The code implementing these rules is
gone with the flat markers; the rules are Valve's and outlive it, because the markers return as a
free-camera option and will need every one of them again.

**Evidence class: read from published source** — `game/client/game_controls/MapOverview.cpp` — with
one line of owner reasoning marked as such.

---

## There are TWO `CanPlayerBeSeen`s, and TF2 uses the one without the origin check

**The first draft of this chapter got this wrong, and the owner caught it:**

> *"i dont think thats valve, that was the rust imp i think"*

He was right that the attribution was bad, though not about where it came from — and checking it
turned up something better than either reading.

**`CMapOverview::CanPlayerBeSeen`** (`game/client/game_controls/MapOverview.cpp:367`) is the base
class, and it does have the origin check:

```cpp
// don't draw ourself
if ( localPlayer->GetUserID() == (player->userid) )
    return false;

// Invalid guy.
if( player->position == Vector(0,0,0) )
    return false; 

// if local player is on spectator team, he can see everyone
if ( localPlayer->GetTeamNumber() <= TEAM_SPECTATOR )
    return true;

// we never track unassigned or real spectators
if ( player->team <= TEAM_SPECTATOR )
    return false;

// ... then three mp_forcecamera branches
```

**`CTFMapOverview::CanPlayerBeSeen`** (`game/client/tf/vgui/tf_overview.cpp:278`) overrides it
entirely — and drops the origin check:

```cpp
// don't draw ourselves
if ( localPlayer->entindex() == (player->index+1) )
    return false;

// if local player is on spectator team, he can see everyone
if ( localPlayer->GetTeamNumber() <= TEAM_SPECTATOR )
    return true;

// we never track unassigned or real spectators
if ( player->team <= TEAM_SPECTATOR )
    return false;

// ingame and as dead player we can only see our own teammates
return ( localPlayer->GetTeamNumber() == player->team );
```

| rule | base `CMapOverview` | **TF2 `CTFMapOverview`** |
|---|---|---|
| don't draw ourself | by `userid` | by `entindex` |
| **origin check** | **yes** | **no** |
| spectator sees everyone | yes | yes |
| no unassigned / spectators | yes | yes |
| `mp_forcecamera` | three branches | replaced by same-team-only |
| `Drawn` | **no** | **no** |
| `IsAlive` | **no** | **no** |

**Three corrections to the first draft, all of them the same kind of error.** It listed four rules
as "Valve's, in Valve's order" when: the function has seven branches, not four; `Drawn` and
`IsAlive` appear in **neither** version and were ours, written into a Valve-cited list; and the
origin check belongs to the base class, which **TF2 does not use**.

**So "the origin check is what Valve does" is false for TF2 and true for HL2 and CS.** That
distinction is the whole finding, and it is the shape
`docs/memory/read-the-sdk-for-the-whole-mechanism.md` warns about: finding the function is the easy
half, and a base class is not the class the game runs.

## `mp_forcecamera`, and why TF2 stopped consulting it

The base class's last three branches read a cvar; TF2's override does not. The cvar is
`game/shared/gamevars_shared.cpp:24`:

```cpp
ConVar mp_forcecamera( "mp_forcecamera",
#ifdef CSTRIKE
    "0",
#else
    "1",
#endif
    FCVAR_REPLICATED, "Restricts spectator modes for dead players", MPForceCameraCallback );
```

**`FCVAR_REPLICATED`: the server sets it and clients obey.** Its values (`shareddefs.h:509`):

| value | name | meaning |
|---|---|---|
| 0 | `OBS_ALLOW_ALL` | all modes, all targets |
| 1 | `OBS_ALLOW_TEAM` | own team and first person only, no PIP |
| 2 | `OBS_ALLOW_NONE` | no spectating after death — fixed camera, fade to black |

A callback clamps anything out of range back to `OBS_ALLOW_TEAM`, so the setting cannot be put into
an undefined state from the console.

**TF2's default is 1, and that is why the override hardcodes what it does.** The base class branches
three ways on the cvar; `CTFMapOverview` simply returns
`localPlayer->GetTeamNumber() == player->team`, which **is** the `OBS_ALLOW_TEAM` branch. It did not
drop a feature so much as stop consulting a setting it always expects to be 1. CS:S defaults to 0,
which is why the general form lives in the shared base.

## `userid` versus `entindex`, and why the two versions differ

`MapPlayer_t` carries both (`mapoverview.h:82-83`):

```cpp
int index;   // player's index
int userid;  // user ID on server
```

The base class tests `localPlayer->GetUserID() == player->userid`; TF2 tests
`localPlayer->entindex() == (player->index+1)`. **The `+1` confirms `index` is 0-based** — a player
slot — **while `entindex()` is 1-based**, because entity 0 is worldspawn.

**Why they differ is inference, not a stated reason** — no comment in either file says so. A first
draft of this chapter called the userid version "the more robust one and TF2's the cheaper one",
which the owner corrected:

> *"uniwue per connection also means we think a player recconecting because they dropped is a new
> player not the same player reconnecting"*

**That is right, and it makes this a trade-off rather than a ranking.** Unique-per-connection cuts
both ways: it never confuses two players, and it never recognises one. A player who drops and
reconnects — routine in competitive TF2, which is this project's audience — comes back with a new
userid and is a stranger to any userid-keyed check.

| identity | reused across players? | survives a reconnect? |
|---|---|---|
| `userID` | no | **no** — new connection, new id |
| `entindex` | **yes** — it is a slot | no |
| `guid` (SteamID) | no | **yes** |

**We decode all three**, which puts us ahead of both Valve functions: `PlayerInfo` in
`Tf2DemoSalvage.Core` carries `UserId` (offset 32), `SteamId` from the `guid` field (offset 36) and
the entity index. The engine's own `player_info_t` has the same three (`public/cdll_int.h:77-81`),
plus `GetPlayerForUserID` to map between them — so Valve holds them too and simply picks per site.

**So identity is a question with three answers here, and the right one depends on the question
asked.** "Is this the recorder" wants the entity index within a file. "Is this the same human as
before the drop" wants the guid, and nothing else will do.

**We DO need "don't draw ourself", and the first draft of this chapter said otherwise.** It claimed
the rule was moot because a demo viewer has no local player. The owner:

> *"we sometimes do have a local player, in pov demos ... and we need to implement dont draw
> ourself"*

**A POV demo has a recorder, and we already decode it** — `IEyeSource.RecorderEntityIndex`, which
`SpectatorView` reads to follow the recording's own camera. So the local player exists here whenever
the demo is a POV recording, and is simply absent on SourceTV.

**Which means our version needs BOTH branches, not neither**: skip the recorder when there is one,
and skip nobody when there is not. That is one more state than either Valve function has, because
neither ever runs without a local player.

**Use the entity index, not a userid.** We decode `RecorderEntityIndex` and `ScenePlayer.EntityIndex`
and have no userid at all, so TF2's form is the one that maps onto what we hold — with the caveat
above that an index is a reused slot, which for a *recorded* demo is a non-issue since the recorder
does not change mid-file.

## The outline is a better precedent than the overview, and it is networked

**The owner's lead, and it is a better one than `CanPlayerBeSeen`:**

> *"the markers are something valve doesnt have to deal with in the way we are doing them, but valve
> does have an outline that only outlines living players, so valve may have something we can use for
> that whithout the full guard too"*

`CanPlayerBeSeen` governs a 2D spectator *panel*; our markers are drawn over a 3D world. Valve's
glow/outline system is the thing that actually decorates players in the world, so its rules are the
closer analogue.

**`m_bGlowEnabled` is NETWORKED** — `RecvPropBool( RECVINFO( m_bGlowEnabled ) )`,
`c_basecombatcharacter.cpp:180`, with `m_bClientSideGlowEnabled` beside it as the local-only
counterpart. **A networked property is in the demo**, which means a viewer can read what the server
actually said to outline rather than deciding for itself. That is the same shape as every other
"the client builds what the demo omits" question in this project and it has not been explored.

**And TF2's own eligibility rules confirm the pattern**, `C_TFPlayer::ShouldShowPowerupGlowEffect`
(`c_tf_player.cpp:11482`):

```cpp
if ( pLocalPlayer->IsAlive() && this != pLocalPlayer && GetTeamNumber() != pLocalPlayer->GetTeamNumber() )
    return flHealth <= 0.3 && pLocalPlayer->IsLineOfSightClear( this, IGNORE_ACTORS );
```

Liveness, not-self, team — plus line of sight, which the overview never checks.

**Three spellings of "don't draw ourself" in three Valve functions**, which answers the owner's
question about identity better than any one of them does:

| where | test |
|---|---|
| `CMapOverview::CanPlayerBeSeen` | `localPlayer->GetUserID() == player->userid` |
| `CTFMapOverview::CanPlayerBeSeen` | `localPlayer->entindex() == (player->index+1)` |
| `C_TFPlayer::ShouldShowPowerupGlowEffect` | `this != pLocalPlayer` |

**None is canonical — each uses whichever identity is cheapest where it stands.** So the right
question for us is not "which does Valve use" but "which do we hold", and the answer is the entity
index: we decode `RecorderEntityIndex` and `ScenePlayer.EntityIndex` and have no userid at all.

## Why the origin check is still right for us

**Superseded by the measurement below — kept because the reasoning is sound and was worth testing,
and because the conclusion it reaches is right for a different pipeline than ours.** What follows is
why it looked necessary; the section after it is why it is not.

**An entity that exists without a position sits at (0,0,0), and (0,0,0) is a real place.** Depending
on where the mapper put the world it can be mid-air, inside a wall, or under the floor. The owner's
reading, which is the clearest statement of it:

> *"valve does the no draw at orgin thing because it doesnt want dead players or spectators drawn in
> the map or sky somewhere if the origin is not under the map"*

So the dot is not merely meaningless — **it is convincing**. That is the same failure the spectator
rule prevents, which is why they sit together in the base class.

**And our situation is not TF2's, which is why dropping it with TF2 would be wrong.** `CTFMapOverview`
runs inside a live game against entities the server is actively networking: a player being drawn at
all implies the client has a position for them. **A demo viewer reconstructs entities from a
recording**, across eras, from files the live client refuses — so "exists but has no position yet" is
an ordinary state here and a rare one there. The rule TF2 could afford to drop is one we cannot.

**Exact equality on all three axes**, as the base class writes it. A tolerance would swallow a
player legitimately standing near the origin, which plenty of maps put geometry at.

## MEASURED: the check guards a state our decoder cannot produce

**The owner asked the question the citation could not answer** — *"why are we doing an orgin check
if valve doesnt?"* — and then *"yes measure that because the reasoning is sound"*. Measured
2026-08-26, two ways, and they agree.

**Structural, and this is the decisive one.** `DemoTimeline.PlayersAt` builds its list with:

```csharp
if (!player.IsVisible || player.Origin() is not { } origin)
    continue;
```

**A player whose origin does not decode is never emitted as a `ScenePlayer` at all.** `origin` is a
pattern-matched non-null value, so `ScenePlayer.X/Y/Z` always come from a real decoded position.
**Our pipeline cannot produce a phantom (0,0,0) from a missing position** — which is precisely the
failure the check existed to catch. The guard sat downstream of a filter that had already removed
everything it was looking for.

**Empirical, as a cross-check.** `CorpusPlayerOriginTests` walks a sample chosen per the owner's
instruction — real matches only, one or two per era, no era specimens, both points of view:

```
demos 7, ticks sampled 287
players at exactly (0,0,0): 0
```

Seven demos spanning 2012 to 2026. Zero. On its own that sample is thin — 40 ticks per demo mostly
miss the pre-spawn moment that would produce such a player — which is why the structural argument
carries the weight and this only fails to contradict it.

**So TF2's version is right for us, and for a better reason than TF2 has.** Valve's base class needs
the check because `MapPlayer_t` is a *cache* that can hold a position for a player who has not sent
one; ours needs it not at all because the list is built by refusing those players outright. **Same
conclusion as `CTFMapOverview`, reached from the opposite direction.**

**What would change this**: a demo that genuinely networks (0,0,0) for a player. Then it is a real
position and skipping it would be the error — so if the marker reimplementation ever wants this
rule back, the trigger is evidence of that, not the reasoning above.

## Would keeping it be safer for mutation testing? No — it runs the other way

**The owner asked exactly the right follow-up:**

> *"will it being unguarded maybe cause mut survivors so it should stay guarded anyway?"*

**An unreachable guard CREATES survivors; removing it removes them.** Stryker mutates what is there.
Give it `if (player is { X: 0f, Y: 0f, Z: 0f }) continue;` on a path no input can reach, and every
mutant of that line — condition negated, branch removed, `0f` changed — behaves identically to the
original, because no demo produces an input that tells them apart. **No test can kill a mutant that
nothing distinguishes.** Delete the line and there is nothing to mutate and nothing to survive.

That is `docs/memory/mutation-score-is-not-the-goal.md` read forwards rather than backwards: *"a
surviving mutant is a real finding: either add the missing assertion, or the mutated code path
genuinely doesn't matter and can be deleted."* Here the second branch applies, and the measurement
above is the evidence for it — arrived at without needing to run Stryker at all.

**But there is a real guard, and it is the one that should be mutation-tested.** The filter that
actually does this work is in `DemoTimeline.PlayersAt`:

```csharp
if (!player.IsVisible || player.Origin() is not { } origin)
    continue;
```

**That line is reachable, load-bearing, and its mutants are killable** — a demo with a player who has
not sent an origin distinguishes guarded from unguarded, and `CorpusSceneTests` already records that
such players exist ("a 2008 SourceTV demo has two player entities and one of them is SourceTV's own
slot, which never sends an origin because it is not standing anywhere").

**So the useful question is not "should the marker keep its guard" but "does anything kill the
mutants of `PlayersAt`'s".** If a survivor turns up there, it is a genuine coverage gap in the filter
the whole pipeline depends on — which is worth more than the guard ever was.

## What this project got wrong about it, once

**The origin check was left out of the first implementation**, on the reasoning that a demo's
entities are read rather than networked and that `Drawn` already covered it. That reasoning was
never checked against anything and was recorded in a comment rather than raised as a question. The
owner asked for it back, and `docs/memory/a-divergence-is-asked-not-documented.md` exists because of
this and two sibling cases.

**Note what our `SpectatorTarget.CanObserve` does and does not cover**, because it is the surviving
implementation and it is not the same function: it checks team, `Drawn` and `IsAlive` — **not the
origin**. That is correct, because `CanObserve` answers "may I spectate this player" while
`CanPlayerBeSeen` answers "may I draw a marker for them", and they are different questions in Valve
too. **Whatever redraws markers must implement the origin rule itself; it is not inherited.**

## The marker-versus-model rule, which is ours rather than Valve's

Learned the hard way and worth carrying:

> **A player drawn as a MODEL must not also get a flat marker on top of it, and a player without a
> model must still get one, or they vanish.**

Asked in two places the answers drift, and they did: the markers went on being drawn over the models
the moment those started working, which hid whether the models were there at all — a working render
looking like a failed one.

**The dead are skipped in the marker pass for the same reason**, and this is where it is easiest to
get wrong. A player the engine would not draw has no model, and the rule is "no model means a dot" —
so removing dead players from the model pass *alone* turns every corpse into a marker gliding around
the map behind whoever it was spectating. The same defect, in a cheaper primitive.

## Team colours

Team two is RED and team three is BLU — the engine's own numbering, with nought unassigned and one
spectator. A player whose team has not arrived is drawn grey rather than guessed at: **a wrong team
colour is worse than none, because it is read as information.**
