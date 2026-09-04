# 48 — A corpse describes itself in two integers

**Subject:** `DT_TFRagdoll` — what a demo says about a death, and what the client invents.

Every corpse in every match this project has ever decoded was read correctly and drawn not at all.
159 of them in one 30-minute match. The reason is not in the decode, and finding it settled a
question that had been filed under physics for months.

## `NOBASE` is the whole story

```cpp
IMPLEMENT_CLIENTCLASS_DT_NOBASE( C_TFRagdoll, DT_TFRagdoll, CTFRagdoll )
```

`c_tf_player.cpp:518`. Read-from-source.

Almost every networked entity in Source inherits `DT_BaseEntity` and through it the fields anything
drawable needs — `m_nModelIndex`, `m_nSkin`, `m_nBody`, `m_vecOrigin`, `m_angRotation`. `NOBASE`
declares a table that inherits **nothing**. A corpse sends its class, its team, its resting origin,
a force vector, and about a dozen booleans naming the manner of death. It does not send one field
that says what it looks like.

The same declaration appears on `CBaseViewModel`, and for a viewer it has the same consequence: a
generic prop path asks the entity for a model index, is told nothing, and correctly draws nothing.
**The corpses were never described.**

## The client has the identical problem

It is worth stating plainly, because the instinct on finding a missing field is to look for a decode
bug. There is none to find. The game client receives exactly what we receive. It derives the rest:

```cpp
TFPlayerClassData_t *pData = GetPlayerClassData( m_iClass );
if ( pData ) nModelIndex = modelinfo->GetModelIndex( pData->GetModelName() );

if ( nModelIndex != -1 )
{
    SetModelIndex( nModelIndex );
    if ( m_iTeam == TF_TEAM_RED ) m_nSkin = 0; else m_nSkin = 1;
}
```

`C_TFRagdoll::CreateTFRagdoll`, `c_tf_player.cpp:689-720`. Read-from-source.

Two integers in, an appearance out. `m_iClass` is sent in **four bits** and `m_iTeam` in **three**
(`tf_player.cpp:375-467`) — seven bits, and they carry the entire visual identity of a dead player.

Note what the guard covers. `SetModelIndex` and both skin assignments are inside one
`if ( nModelIndex != -1 )`, so a class that names no model leaves the corpse with **no skin either**.
The two lines read as independent and are not; a reimplementation that sets the skin unconditionally
diverges only in a case that never arises in practice, which is the kind that survives review.

There is no compiled-in table of class model paths anywhere in the SDK. `GetPlayerClassData` returns
a `TFPlayerClassData_t` parsed at runtime from `scripts/playerclasses/<name>.txt` inside the game's
own VPKs (`tf_classdata.cpp:14-39, 232-268`) — so **the model a class wears is shipped data, not
code**, and a viewer without the game installed genuinely cannot know it. The single literal
`.mdl` string in that file is the `TF_CLASS_UNDEFINED` fallback, `"models/player/scout.mdl"`, with
Valve's comment: *"Undefined players still need a model"*.

## The same rule, written twice, disagreeing

TF2 computes team-to-skin in two places. They agree on the only two values that occur in a real
game and differ outside them:

```cpp
// C_TFPlayer::GetSkin, c_tf_player.cpp:7807-7817
case TF_TEAM_RED:  nSkin = 0; break;
case TF_TEAM_BLUE: nSkin = 1; break;
default:           nSkin = 0; break;

// C_TFRagdoll::CreateTFRagdoll, c_tf_player.cpp:712-719
if ( m_iTeam == TF_TEAM_RED ) m_nSkin = 0; else m_nSkin = 1;
```

A player of no team falls to RED. A corpse of no team falls to BLU. A switch in one place and an
if/else in the other, different authors on different days, and the difference is real rather than
stylistic.

**This is a fact about the engine and not duplication to be tidied.** The obvious implementation of
the corpse reuses whatever team-to-skin helper the codebase already has, which is the player's — and
that is a divergence with no symptom anybody would ever photograph. Recorded because a later reader
looking at two identical-seeming expressions will want to merge them.

## What a corpse actually persists

Reading only `CreateTFRagdoll`'s last lines gives a wrong answer with a citation attached:

```cpp
StartFadeOut( cl_ragdoll_fade_time.GetFloat() );   // :869, and the convar defaults to 15
```

Fifteen seconds, apparently. It is not. `C_TFRagdoll::ClientThink` re-arms the timer at a **third**
of that on every think the corpse is on screen, and returns:

```cpp
if ( IsRagdollVisible() )
{
    …
    StartFadeOut( cl_ragdoll_fade_time.GetFloat() * 0.33f );
    return;
}
```

`c_tf_player.cpp:1532-1545`. Read-from-source.

So **a corpse being looked at never fades at all**, and one that has left view expires 4.95 seconds
later. Both halves of the obvious reading are wrong, and the correction changes the shape of the
problem as well as the number: a lifetime that depends on what the camera can see cannot be computed
once when the demo is decoded. It belongs to whatever is drawing.

`IsRagdollVisible` turns out to be cheap and camera-only — a two-unit box at the corpse's origin
against the view cluster and the frustum, with no reference to whether the corpse was drawn:

```cpp
Vector vMins = Vector(-1,-1,-1);
Vector vMaxs = Vector(1,1,1);
Vector origin = GetAbsOrigin();

if( !engine->IsBoxInViewCluster( vMins + origin, vMaxs + origin) ) return false;
else if( engine->CullBox( vMins + origin, vMaxs + origin ) ) return false;
return true;
```

`c_tf_player.cpp:1350-1367`.

**How much this matters was measured rather than assumed, and the answer is: it is the feature.**
Drawing a corpse for as long as its ENTITY exists sounds conservative and is not — the server keeps
one ragdoll per player and destroys it only when that player next dies (`UTIL_Remove`,
`tf_player.cpp:15602`), so bodies accumulate all match. On `serveme-627619-stv-2026-08-07`: **36
simultaneously alive a quarter of the way through, 43 at half, 57 at three quarters**, against a
twelve-player roster. Under `ClientThink`'s rule the same three moments hold **4, 2 and 4**.

**And "fade" does not fade, at the defaults.** The alpha ramp everyone pictures —
`SetRenderMode( kRenderTransAlpha )`, alpha down at 600 a second — sits inside
`if ( m_bFadingOut == true )` (`:1513-1527`), and `m_bFadingOut` is set in exactly one place: the
`if ( cl_ragdoll_forcefade.GetBool() )` branch at `:1534`. That convar is
`ConVar cl_ragdoll_forcefade( "cl_ragdoll_forcefade", "0", FCVAR_CLIENTDLL )` (`:515`). So with stock
settings a corpse does not fade out at all — it is simply gone on the frame its timer expires. The
function is named for the branch nobody runs.

Two other windows were tried first and both were wrong in instructive ways. Ending at the corpse's
last UPDATE drew each one for a single tick — because 158 of the 159 receive exactly one update,
which is itself the finding: everything `DT_TFRagdoll` carries is a fact about the instant of death,
and the corpse is a client-side simulation from then on. Adding the PVS `Leave` to the delete barely
moved the count (57 to 61), because SourceTV's camera follows the action and corpses lie in it.

## The pose has two branches and the famous one is the rare one

`RagdollSpawn` is the name that turns up when anyone asks how a corpse is posed, and it is reached
only for the LOCAL player:

```cpp
if ( !pPlayer->IsLocalPlayer() && pPlayer->IsInterpolationEnabled() )
{
    Interp_Copy( pPlayer );
    SetAbsAngles( pPlayer->GetRenderAngles() );
    GetRotationInterpolator().Reset();
    m_flAnimTime = pPlayer->m_flAnimTime;
    SetSequence( pPlayer->GetSequence() );
    m_flPlaybackRate = pPlayer->GetPlaybackRate();
}
else
{
    // This is the local player, so set them in a default pose ...
    int iSeq = LookupSequence( "RagdollSpawn" );
    if ( iSeq == -1 ) { Assert( false ); iSeq = 0; }
    SetSequence( iSeq );
    SetCycle( 0.0 );
}
```

`c_tf_player.cpp:757-784`. Read-from-source.

**In a SourceTV recording there is no local player at all**, so every corpse in one takes the first
branch and inherits the dying player's sequence, cycle, animation time and playback rate. In a POV
recording exactly one corpse per match — the recorder's own — takes the second. The neutral standing
pose is the exception, not the rule, and a reader who found `RagdollSpawn` first would implement the
minority case and think it general.

**And the copy is harder than it looks, for a reason particular to this format.** A player's sequence
and cycle are not networked at all — `DT_TFPlayer` strips them and the client rebuilds them from
movement (see `47-a-players-animation-is-not-on-the-wire.md`). So `pPlayer->GetSequence()` is reading
a client-side computation, which means a demo player cannot copy the corpse's pose off the wire
either: it has to run the same animation and sample it at the instant of death.

## What is recorded and what is invented

The line between them is sharper here than almost anywhere else in the format, and it is not where
one would guess.

| | networked | derived by the client |
|---|---|---|
| model | — | `m_iClass` through the class scripts |
| skin | — | `m_iTeam` |
| body groups | — | copied off the player, under `if ( !m_bFeignDeath \|\| m_bWasDisguised )` |
| resting origin | `m_vecRagdollOrigin` | — |
| angles | — | the player's render angles, reached through the networked `m_hPlayer` |
| head, torso and hand scale | **`m_flHeadScale`, `m_flTorsoScale`, `m_flHandScale`** | **overwritten from the player whenever the player still exists** |
| **gibbed** | **`m_bGib`** | — |
| burning, electrocuted, dissolving, gold, ice, ash, cloaked, feign, disguised | each its own bool | — |
| manner of death | `m_iDamageCustom` | — |
| cosmetics worn | `m_hRagWearables`, eight ehandles — **and the client only ever hides them** | the attachments, built from the PLAYER's wearable list instead |
| **death animation or physics** | — | **an unrecorded coin flip** |

The last row is the only genuinely unrecoverable one:

```cpp
if ( !m_bIceRagdoll && !tf_always_deathanim.GetBool() && (RandomFloat( 0, 1 ) > 0.25f) )
    iDeathSeq = -1;
```

`c_tf_player.cpp:829-832`. Three quarters of eligible deaths discard the animation and fall as
physics, decided by a draw on the recording client's own random stream and stored nowhere. Same
class as the client-predicted footsteps: not a decode gap, a thing that was never in the file.

### "Eligible" is doing far more work in that sentence than it looks

The coin flip is the famous half. The gate in front of it decides almost everything, and it is a
`switch` with two cases and no default:

```cpp
switch ( nCustomDeath )
{
case TF_DMG_CUSTOM_HEADSHOT_DECAPITATION:
case TF_DMG_CUSTOM_TAUNTATK_BARBARIAN_SWING:
case TF_DMG_CUSTOM_DECAPITATION:
case TF_DMG_CUSTOM_HEADSHOT:
    iDeathSeq = pRagdoll->LookupSequence( "primary_death_headshot" );
    break;
case TF_DMG_CUSTOM_BACKSTAB:
    iDeathSeq = pRagdoll->LookupSequence( "primary_death_backstab" );
    break;
}

return iDeathSeq;
```

`CTFPlayerShared::GetSequenceForDeath`, `tf_player_shared.cpp:13441-13455`. Read-from-source.
**Every death that is not a headshot, a decapitation or a backstab returns -1** — no death animation
exists for it, and the corpse goes straight to physics. TF2 ships exactly two death animations,
`primary_death_headshot` and `primary_death_backstab`.

`m_bBurning` is passed into the function and never used: the burning branch is present and
**commented out** (`:13437-13440`), one more conditionally-dead thing in this corner of the engine.

Measured on three demos, counting corpses whose `m_iDamageCustom` is one of the five eligible
ordinals:

| demo | corpses | eligible | would animate (¼) |
|---|---|---|---|
| `serveme-627619-stv-2026-08-07` (comp 6v6) | 159 | **0** | 0 |
| `20120707-0042-koth_idioteque_a3` | 457 | 22 | ~6 |
| `20140607_2350_koth_pro_viaduct_rc4` | 147 | 5 | ~1 |

```bash
dotnet run --project tools/Tf2DemoSalvage.Probe -c Release -- corpses <demo>
```

**So about one corpse in a hundred plays a death animation.** The comp match scores zero because a
6v6 runs no sniper and no spy — and that is the control on the whole measurement, not a curiosity:
the observed `m_iDamageCustom` values there are `0×138, 31×9, 28×7, 25×5`, which is
`TF_DMG_CUSTOM_NONE`, `STANDARD_STICKY`, `ROCKET_DIRECTHIT` and `AIR_STICKY_BURST` — a soldier and
demo match, exactly. The two koth demos, which do carry snipers and spies, are where ordinals 1 and 2
appear at all. A field decoding to its default for everything would have looked identical to a real
zero, and the spread is what separates them.

**The consequence for anyone planning work here: the pose problem is physics, not animation.** Both
the death animation and the copied player sequence are worth almost nothing to how a corpse looks;
what makes a body lie down is `InitAsClientRagdoll`, and every corpse in that comp match takes it.

**Three networked fields are conditionally vestigial, which is a shape worth naming.** The corpse
sends its own `m_flHeadScale`, `m_flTorsoScale` and `m_flHandScale`, and `CreateTFRagdoll` opens by
throwing them away:

```cpp
if ( pPlayer )
{
    m_flHeadScale  = pPlayer->GetHeadScale();
    m_flTorsoScale = pPlayer->GetTorsoScale();
    m_flHandScale  = pPlayer->GetHandScale();
}
```

`c_tf_player.cpp:702-707`. The values on the wire survive only when the player entity has already
gone — a disconnect, or a corpse arriving outside the recorder's PVS. So they are neither dead nor
authoritative, and a reader that trusted them would be right most of the time and wrong exactly when
the player is missing, which is the hardest case to notice. Not the same as `$modblend`, which no
code reads at all; this is a field with one live branch out of two.

The angles are the mirror image — nothing is sent, and the client reaches back through the networked
`m_hPlayer` for `GetRenderAngles()`. Both facts point the same way: **for a corpse, the player entity
is part of the format.**

**And one field is fully vestigial, with Valve saying so in the declaration.** `m_hRagWearables` is
eight networked ehandles that look exactly like the corpse's cosmetics list:

```cpp
CUtlVector<CHandle<CEconWearable > > m_hRagWearables;		// These look like they are no longer used?
```

`c_tf_player.h:1132`. The client's only use of it is inside `EndFadeOut`, hiding them
(`AddEffects( EF_NODRAW )`, `SetMoveType( MOVETYPE_NONE )`, `:1652-1660`); the server only `Remove()`s
them (`tf_player.cpp:401-408`). Nothing ever draws from it. A corpse's hats come from
`CreateBoneAttachmentsFromWearables`, which walks the **player's** wearable list
(`c_tf_player.cpp:10169-10251`).

This is the third time in one function that the field or line which looks like the answer is not:
the skin (the player's rule, not the ragdoll's), the pose (`RagdollSpawn`, the local-player branch),
and now the cosmetics. `CreateTFRagdoll` is unusually rich in plausible wrong answers, and the reason
seems to be its age — it carries several mechanisms that were replaced without the old ones being
deleted.

**`m_bGib` is not part of that and the two get conflated.** Gibbing is networked
(`RecvPropBool( RECVINFO( m_bGib ) )`, `:524`) and read rather than guessed. The coin flip is
death-animation against plain ragdoll physics; gibs are a separate branch chosen before either
(`C_TFRagdoll::OnDataChanged`, `:1157-1275`). Measured: 71 of 159 corpses in
`serveme-627619-stv-2026-08-07` are gibbed — 45%, which is high enough that treating it as an edge
case would be visibly wrong.

## The counting mistake, kept because it is instructive

The first probe over that demo reported **87** corpses. The audit had recorded **299**. The right
answer is **159**, and the 87 came from keying corpses by entity index alone — slots are reused
briskly and every reuse collapsed silently into its predecessor, halving the subject while the probe
looked authoritative. The serial is what separates them, which is the same field
`ScenePropTrack.Continues` exists for and the same lesson as B92.

The 299 could not be reproduced and nothing survives to say how it was counted. It has been replaced
with the measured number and the command that produces it.

## Evidence

Read-from-source throughout for the engine behaviour, `c_tf_player.cpp` at the lines cited. The
counts are measured on `serveme-627619-stv-2026-08-07` with:

```bash
dotnet run --project tools/Tf2DemoSalvage.Probe -c Release -- corpses serveme-627619-stv-2026-08-07
```

**Not established:** whether the live-player branch (`pPlayer->GetPlayerClass()->GetModelName()`,
preferred over `m_iClass` when the player entity is still around) can ever resolve to a different
model than the class table gives. Both routes end at the same data and differ only through
`m_iszCustomModel`, which nothing in stock TF2 appears to set outside Mann-vs-Machine — interpolated,
not measured.
