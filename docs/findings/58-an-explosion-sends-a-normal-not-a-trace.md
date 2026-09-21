# 58 — an explosion sends a normal, not a trace, and its sentinel is 65535

**Read and measured 2026-09-20** (B415). The owner had reported explosion particles missing; the census said
`demostf-cp_process_f12-2026-08-07` carries 2,786 `CTETFExplosion` and the timeline consumed none of them.

## The wire

`DT_TETFExplosion` (`tf_fx.cpp:136`, and the demo's own schema agrees field for field):

```cpp
SendPropFloat ( SENDINFO_NOCHECK( m_vecOrigin[0] ), -1, SPROP_COORD_MP_INTEGRAL ),
SendPropFloat ( SENDINFO_NOCHECK( m_vecOrigin[1] ), -1, SPROP_COORD_MP_INTEGRAL ),
SendPropFloat ( SENDINFO_NOCHECK( m_vecOrigin[2] ), -1, SPROP_COORD_MP_INTEGRAL ),
SendPropVector( SENDINFO_NOCHECK( m_vecNormal ), 6, 0, -1.0f, 1.0f ),
SendPropInt   ( SENDINFO_NOCHECK( m_iWeaponID ), Q_log2( TF_WEAPON_COUNT )+1, SPROP_UNSIGNED ),
SendPropInt   ( SENDINFO_NAME( m_nEntIndex, entindex ), MAX_EDICT_BITS, SPROP_UNSIGNED ),
SendPropInt   ( SENDINFO_NOCHECK( m_nDefID ), -1 ),
SendPropInt   ( SENDINFO_NOCHECK( m_nSound ), -1 ),
SendPropInt   ( SENDINFO_NOCHECK( m_iCustomParticleIndex ), -1 ),
```

**The origin is three floats and the normal is one vector.** A reader that looks for `m_vecOrigin` as a vector finds
no such property and places every blast at the world origin, with nothing to report.

**`entindex` is `SENDINFO_NAME`d**, so the field is `m_nEntIndex` and the wire name is `entindex` —
`docs/memory/wire-names-are-strings.md` again, and a third time in this project.

## Mid air is a magnitude test, and Valve's comment is literally true

`TFExplosionCallback` (`tf_fx_explosions.cpp:74-84`):

```cpp
// Cannot use zeros here because we are sending the normal at a smaller bit size.
if ( fabs( vecNormal.x ) < 0.05f && fabs( vecNormal.y ) < 0.05f && fabs( vecNormal.z ) < 0.05f )
{
    bInAir = true;
    angExplosion.Init();
}
else
{
    VectorAngles( vecNormal, angExplosion );
    bInAir = false;
}
```

**The send table settles that the comment is not hand-waving.** Six bits per component over `[-1, 1]` puts the
representable values at `2k/63 − 1`, and no integer `k` gives zero — the nearest are ±0.0159, with a step of 0.0317.
An exact zero genuinely cannot be sent, so a threshold is the only way to read the server's intent, and 0.05 sits
comfortably above the step.

**So "in mid air" costs no trace.** It is the server declining to name a surface and the client recognising the
smallness that survived quantisation. *Arithmetic, over a read send table.*

## `INVALID_STRING_INDEX` is 65535, and reading it as −1 invents a finding

`networkstringtabledefs.h:17`:

```cpp
const unsigned short INVALID_STRING_INDEX = (unsigned short )-1;
```

It is assigned into an `int` and sent through a **signed** 32-bit `SendPropInt`, so what arrives is **65535**.

Measured with −1 as the sentinel: **2,492 of 2,786 explosions, 89%, appeared to name their own particle effect** —
which read as a real finding about modern TF2, and would have made the `ParticleEffectNames` string table the
priority. Measured with 65535: **zero do.** Nothing failed either way; the wrong number was plausible, had no
exception behind it, and pointed at a fortnight of work on a table this recording never uses.

`docs/memory/sentinels-conflate-unknown-with-answer.md` is about a sentinel meaning "unknown" where the default was
the answer. This is the other direction: a sentinel with the **wrong value**, where every real value looks like the
sentinel's opposite.

## What `demostf-cp_process_f12` actually holds

| | count | of 2,786 |
|---|---:|---:|
| explosions | 2,786 | |
| in mid air (`\|normal\|` under the threshold on all three axes) | 449 | 16% |
| against an entity (`entindex` neither 2047 nor −1) | 178 | 6% |
| naming their own particle | **0** | 0% |

*Measured on one demo, through the production `DemoTimeline`.* The effect name therefore comes entirely from the
weapon script for this recording, in the three-way choice `TFExplosionCallback` makes: `ExplosionWaterEffect` in
water, `ExplosionPlayerEffect` when the blast hit a player **or was in mid air**, `ExplosionEffect` otherwise — and
`"ExplosionCore_wall"` before any of them when no script is found.

Read out of the shipped scripts by the `weapon-script` probe rather than guessed:

```
TF_WEAPON_ROCKETLAUNCHER   ExplosionEffect=ExplosionCore_wall
                           ExplosionPlayerEffect=ExplosionCore_MidAir
                           ExplosionWaterEffect=ExplosionCore_MidAir_underwater
```

**So the default and the weapon's `ExplosionEffect` happen to agree for a rocket, and the player/air branch does
not.** Skipping the weapon scripts would have drawn `ExplosionCore_wall` for the 449 mid-air blasts, which is most
of what a soldier does.

## Two encodings for "no entity", and the comment says which demos

`RecvProxy_ExplosionEntIndex` (`tf_fx_explosions.cpp:222-229`):

```cpp
// The 'new' encoding for INVALID_EHANDLE_INDEX is 2047, but the old encoding
// was -1. Old demos and replays will use the old encoding so we have to check
// for it. The field is now unsigned so -1 will not be created in new replays.
m_hEntity = (nEntIndex == kInvalidEHandleExplosion || nEntIndex == -1)
    ? INVALID_EHANDLE : ClientEntityList().EntIndexToHandle( nEntIndex );
```

`kInvalidEHandleExplosion` is `MAX_EDICTS - 1` = 2047 (`c_tf_fx.h:10`). **This is a comment Valve wrote FOR a demo
player**, which is rare enough to be worth keeping: a reader that knows only 2047 hands an old demo's blast −1 to
look up, and one that knows only −1 looks up edict 2047. Both then take the wrong branch of `bIsPlayer` and draw the
wrong effect. Note also that the modern field is `SPROP_UNSIGNED`, so −1 cannot arrive from a current server at all
— the check is there purely for recordings.

## What is not established

- **Whether the 178 entity-bearing blasts are players.** `bIsPlayer` needs the entity's class, and only the count
  of blasts carrying *an* index has been measured.
- **Water.** `UTIL_PointContents( vecOrigin ) & CONTENTS_WATER` decides `ExplosionWaterEffect`, and this project
  does not evaluate BSP contents at a point.
- **Whether any recording anywhere sets `m_iCustomParticleIndex`.** One demo says no. It is decoded and carried
  regardless, so the day one does, the value is there.
- **`GameRules()->TranslateEffectForVisionFilter( "particles", pszEffect )`**, the last thing
  `TFExplosionCallback` does before dispatching. Unread.
