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

### The name Valve's code uses is not the name Valve's data uses

`tf_fx_explosions.cpp:88` and every stock weapon script say **`ExplosionCore_wall`**. The system that ships is
**`ExplosionCore_Wall`**, with a capital W — measured with the `particles` probe, whose search is a byte compare:

```
'ExplosionCore_wall' named by 0 of 134 files
'ExplosionCore_Wall' appears in PARTICLES/EXPLOSION.PCF
'ExplosionCore_MidAir' appears in PARTICLES/EXPLOSION.PCF
```

The probe is not lying — `ExplosionCore_MidAir` is the control and it matches exactly, so the search works and the
case really does differ. TF2 draws the effect, so the engine's particle lookup is case-insensitive.

**This project already agrees, by accident rather than by decision**: `ParticleSystems.Read` builds its map with
`StringComparer.OrdinalIgnoreCase` (`ParticleSystems.cs:115`), as does `ParticleEffects` (`:321`). Recorded here so
that neither is "tidied" to `Ordinal` — the symptom would be every wall explosion silently drawing nothing, while
airbursts, which match exactly, kept working.

*Measured, with a control.*

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

## An explosion is nine systems, and eight of them are the children

`ExplosionCore_Wall` and `ExplosionCore_MidAir` declare **no renderers, no initializers and no operators**. They are
pure parents; all the work is in eight and nine children respectively. A reader that resolved the parent and drew it
would draw nothing and conclude the system was empty.

| child of `ExplosionCore_Wall` | material | renderer | emitter |
|---|---|---|---|
| `Explosion_Debris001` | `effects/debris/debris_chunk` | `render_animated_sprites` | `emit_continuously` |
| `Explosion_Dustup` | `effects/softglow_translucent` | `render_sprite_trail` | `emit_continuously` |
| `Explosion_CoreFlash` | `effects/softglow` | `render_sprite_trail` | `emit_instantaneously` |
| `Explosion_FloatieEmbers` | `effects/brightglow_y_nomodel` | `render_sprite_trail` | `emit_instantaneously` |
| `Explosion_Smoke_1` | `effects/smokelit2/smoke2lit` | `render_animated_sprites` | `emit_instantaneously` |
| `Explosion_Flash_1` | `effects/sc_brightglow_y_nomodel` | `render_animated_sprites` | `emit_instantaneously` |
| `Explosion_FlyingEmbers` | `effects/circle2` | both | `emit_instantaneously` |
| `Explosion_Flashup` | `effects/softglow` | `render_sprite_trail` | `emit_instantaneously` |

**So one of eight drew**, and the first picture of an explosion in this viewer was fifteen hard orange polygons on a
wall — `Explosion_Debris001`, the only child whose renderer AND emitter were both implemented.

Two separate faults, and only the picture separated them.

### A system whose renderer is not implemented was drawn as one that is

`ParticleEffects.Gather` looked for `render_animated_sprites` and passed a **null** renderer onwards when it found
none. `ParticleSprites.Build` does not skip a system for that — it draws it with whole-texture UVs, no sheet and a
default animation rate. A `render_sprite_trail` particle is stretched along its velocity by the engine, so drawing
it as a billboard is not an approximation of it; it is a different shape.

Fixed: a system this project cannot draw draws nothing. **It costs `rockettrail_burst`**, which declares the same
renderer — a rocket keeps its smoke and its fire and loses a glow it was drawing wrongly. `render_sprite_trail` is
five of `ExplosionCore_Wall`'s eight children and is the largest single piece still missing.

### `emit_instantaneously` was not implemented, and an explosion is made of it

Six of the eight children use it. `ParticleEffect.Emit` handled `emit_continuously` alone, so those six emitted
nothing — indistinguishable from a material that failed to resolve.

Its parameters, read from the shipped `.pcf` rather than guessed (`Explosion_Smoke_1`):

```
num_to_emit = 8             num_to_emit_minimum = -1
emission_start_time = 0     maximum emission per frame = 100
```

**`num_to_emit_minimum` of −1 is "no range", not "emit none"** — the count is exactly `num_to_emit`, and a
non-negative value makes it a random draw in `[minimum, num_to_emit]`. Every emitter in the explosion path
declares −1.

With it implemented, the smoke appears. *Confirmed by looking*, at `cp_process_f12` tick 21880.

## What is not established

- **Whether the 178 entity-bearing blasts are players.** `bIsPlayer` needs the entity's class, and only the count
  of blasts carrying *an* index has been measured.
- **Water.** `UTIL_PointContents( vecOrigin ) & CONTENTS_WATER` decides `ExplosionWaterEffect`, and this project
  does not evaluate BSP contents at a point.
- **Whether any recording anywhere sets `m_iCustomParticleIndex`.** One demo says no. It is decoded and carried
  regardless, so the day one does, the value is there.
- **`GameRules()->TranslateEffectForVisionFilter( "particles", pszEffect )`**, the last thing
  `TFExplosionCallback` does before dispatching. Unread.
- **`render_sprite_trail`**, which five of the eight children declare and this project does not implement. It is
  the largest piece still missing from an explosion, and it also costs `rockettrail_burst`.
- **Why `Explosion_Flash_1` does not draw**, although both its renderer and its emitter are now implemented and
  its material resolved. Unlike the smoke, nothing of it appeared.
- **The debris chunks' size and tint.** They draw, and they dominate the picture in a way TF2's do not.
- **Which `.pcf` the engine actually loads.** `ExplosionCore_` appears in five files —
  `explosion.pcf`, `explosion_high.pcf`, `explosion_dx90_slow.pcf`, `explosion_dx80.pcf` and `bigboom.pcf` — and
  which one wins is a `particles_manifest.txt` and detail-level question this has not looked at.
