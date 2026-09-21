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
five of `ExplosionCore_Wall`'s eight children and is the largest single piece still missing. *(Built the same day —
see below — and `rockettrail_burst` draws again, as a trail this time.)*

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

## `render_sprite_trail` exists only in the binary

`C_OP_RenderSpriteTrail` is in `particles.lib`, which the SDK ships compiled and the client links statically. **There
is no source for it anywhere**, so everything below was read out of `client-live-x86.dll` (Ghidra project
`tf2usermsg`), and the statement-for-statement reproduction lives in `ParticleSpriteTrails`'s remarks rather than
here.

**The way in was the parameter names, not the op's name.** The string `render_sprite_trail` (`0x10b5b0c8`) has no
code reference at all, and neither do `max length` or `min length` — because a DMX unpack table is not initialised
data. It is filled at static-init time by a run of `MOV dword ptr [abs32], imm32` (`C7 05 …`), one per field of
`{ name, default, type, offset, size }`, so a search for pointers to the strings in `.data` finds nothing and a search
for the pointer's four bytes inside `.text` finds the initialiser (`0x107b626b`). Decoding that run gave the members:

| field | offset | default |
|---|---|---|
| `animation rate` | `this+0x58` | `0.1` |
| `length fade in time` | `this+0x5c` | `0` |
| `max length` | `this+0x60` | `2000` |
| `min length` | `this+0x64` | `0` |

The function that answers the table (`0x107b5ee0`, a `GetUnpackStructure`-shaped virtual) is what locates the vtable,
and the vtable's render slot leads to the loop that computes `1 / m_flDt` — **exactly 1 when `m_flDt` is exactly
0.0**, an equality against `_DAT_10926e30` — and calls the per-particle builder `FUN_107b8d30` once a particle.

What the builder does, in behaviour rather than code:

- **One quad a particle, not a ribbon.** A trail is a streak from where the particle IS back along where it WAS, as
  long as its speed times its own `TRAIL_LENGTH`, and as wide as its radius or its length, whichever is less.
- **The length is `rsqrt(|d|² + 1e-10) · |d|²`, never a square root** — `rsqrtss` and one Newton step, the `3.0`
  at `DAT_1092706c` and `0.5` at `DAT_10926e38`, with the `1e-10` guard at `DAT_1092704c`. The same sequence is
  `_SSE_RSqrtInline` (`vector.h:2224`); it is shared here as `VectorMath.ReciprocalSqrt`.
- **The maximum is clamped before the minimum**, so a minimum above the maximum wins.
- **A particle at rest is not skipped.** The guard leaves its length about `1e-5 · (1/dt) · TRAIL_LENGTH`, which is
  above zero, so the engine emits a quad of zero area — six indices and all. Only a zero FADE (a particle on the
  step it was born, with a fade-in) or a zero alpha byte skips one.
- A material with no sheet takes the rectangle `{0, 0, 1, 1}` (`DAT_10c37d10`); the head takes its bottom edge and
  the tail its top.

### `Trail Length Random`, and the decompiler that said its exponent was dead

`C_INIT_RandomTrailLength` (`FUN_107be5c0`) sets the `TRAIL_LENGTH` the builder multiplies by. Every explosion child
that uses the trail renderer declares it — `Explosion_CoreFlash` with `length_min 0.33`, `length_max 0.4`,
`length_random_exponent 1`. **The first reading of it was wrong**: Ghidra's pseudocode showed a plain lerp with the
exponent loaded and never used, which reads as a dead parameter. The disassembly has `FLD [this+0x34]`, `FLD r`,
`FXCH`, `CALL pow` — the decompiler had dropped the x87 stack setup and with it the call's arguments. So:

```
TRAIL_LENGTH = pow( r, length_random_exponent ) · ( length_max − length_min ) + length_min
```

Both bounds default to `0.1`, the same value the collection gives a particle no initializer touches — which is what
`rockettrail_burst` draws with, since it declares none.

**Its draw is not keyed by particle in the engine.** The scalar path indexes the random table with
`( m_nRandomSeed + m_nRandomQueryCount++ ) & 0xfff`, the collection's running count at `+0x1fe8`, where the other
initializers here key by `PARTICLE_ID`. This project keys it by particle anyway, because the table's contents are
its own rather than Valve's, so no per-particle value could match whichever index were used — the distribution is
what can match, and particle keying keeps a seek reproducible.

## `Alpha Fade and Decay` was read wrong, and the heart of every explosion was invisible

With the trail renderer drawing, the flash still did not appear. The `particles burst ExplosionCore_Wall` probe said
why without a picture: `Explosion_CoreFlash`, `Explosion_Dustup` and `Explosion_FlyingEmbers` sat at **alpha 0 from
the step they were born**.

The operator had been written from B373's reading, **flagged interpolated at the time**: alpha rises from zero to
`start_alpha`, is held there, and falls linearly to `end_alpha`. `Explosion_CoreFlash` declares `start_alpha 0`, so
on that reading it is held at zero for its whole life.

`C_OP_FadeAndKill::Operate` (`FUN_107aeb00`) does something else on every one of those points:

```
life = age · rcp( lifetime )                                    // rcpps, no refinement
if ( in_start  ≤ life < in_end  )  alpha = lerp( S(t), initial · start_alpha, initial )
if ( out_start ≤ life < out_end )  alpha = lerp( S(t), initial, initial · end_alpha )
S(t) = 3t² − 2t³ on t clamped to [0, 1]                         // 3.0 at 0x10b51fa0, 2.0 at 0x10b51f90
```

and **nothing is written outside the two windows** — its stores are masked by the window tests. So `start_alpha` is
not a level; it is where the fade-in STARTS, as a fraction of the particle's own alpha. `Explosion_CoreFlash`'s
fade-in is `0..0`, an empty window under `≤ … <`, and its fade-out starts at 0.7: it keeps its initializer's alpha
for 70% of its life, where the old reading drew it at zero throughout.

**It changes every rocket too.** `rockettrail` declares `start_alpha 1` over `0..0.1`, so its fade-in runs from
`initial · 1` to `initial` — a constant. The old reading faded every plume in from nothing at the rocket; the engine
does not, and the fade-out is a smoothstep rather than a line.

The old tests asserted the old reading and passed, which is what a test written from the same interpolation does.
Their replacements were each reddened by putting one half of the old reading back: holding the level reddens the
two outside-the-window tests, and a linear ramp reddens the two smoothstep ones.

*Evidence class: read from the shipped binary's disassembly for the renderer, the initializer and the operator, with
addresses; measured through the probe for the zero alpha; read from the shipped `.pcf` for every declared value.*

## The flash was centred on the wall, and the wall hid half of it

With the fade corrected, the first capture showed the flash — cut off along a straight horizontal line through its
middle, with nothing above it. **A straight edge where a sprite meets a wall is not by itself a defect**: none of the
explosion's materials declares `$depthblend`, four of the six are plain `UnlitGeneric`, so TF2 depth-tests these quads
against the wall too. The question was why the edge ran through the CENTRE.

`Explosion_Flash_1` is one camera-facing sprite that grows to a radius of about 200 in its tenth of a second. Centred
on a wall that the camera looks down at, the upper half of that quad leans into the wall and is hidden. And the file
says it is not centred on the wall: it declares `Position Modify Offset Random` with `(50 0 0)` **in local space**,
which this project did not implement.

`C_INIT_PositionOffset` is in `particles.lib`, so it was read out of the binary the same way. Its factory is the one
`FUN_107c70d0` registers at `0x10c38920`; the factory's `CreateInstance` allocates `0x4c` bytes with vtable
`0x10b5ba4c`, whose `InitNewParticlesScalar` slot holds `FUN_107bc1b0`. The unpack table's initialiser names the
members — `offset min` `+0x2c`, `offset max` `+0x38`, `control_point_number` `+0x44`, `offset in local space 0/1`
`+0x48`, `offset proportional to radius 0/1` `+0x49` — and the function is:

```
if ( proportional )  min, max = min · RADIUS, max · RADIUS
offset = ( max − min ) · r + min                          per axis
if ( local )         offset = VectorRotate( offset, GetControlPointTransformAtTime( cp, CREATION_TIME ) )
XYZ += offset;  PREV_XYZ += offset
```

An explosion's control point faces the wall's normal, so `(50 0 0)` local is fifty units out from the wall. With it,
the flash is a whole disc. Both positions move, so the particle is displaced rather than launched.

### Two of Valve's initializers disagree about which way local Y points

`GetControlPointTransformAtTime` (`0x107a05a0`) builds the matrix from the control point's stored vectors — forward at
`+0x70`, up at `+0x7c`, right at `+0x88`, the member order `particles.h:999` declares — and **negates right** for the
second column, with an `XORPS` against `0x80000000` at `0x107a05de`. So in this initializer local +Y is LEFT.

`C_INIT_CreateWithinSphere`'s scalar path (`0x107bae50`) does not use that matrix. It multiplies its local speed's Y by
`m_RightVector` directly (`0x107bb325`), so there local +Y is RIGHT. **Two initializers in the same library, and
opposite conventions** — which is why this project reproduces each as it is rather than giving the control point one
"local to world" helper that would make one of them wrong. Nothing in the explosion yet declares a non-zero local Y
offset, so it changes no picture today; it is recorded because the helper is the obvious refactor.

### The sphere initializer's speed had two more branches

Reading `C_INIT_CreateWithinSphere` for the Y question showed two things `Place` did not do:

- **The speed draw is raised to `speed_random_exponent`** — the same `FLD [this+0x58]`, `FLD r`, `FXCH`, `CALL pow`
  as the trail length, at `0x107bb223`.
- **There is no outward speed at all unless `speed_max` is above zero** — `COMISS` against 0 and a `JBE` past the
  whole draw (`0x107bb1fa`). A declared minimum on its own launches nothing.

The unpack defaults, read from the initialiser rather than assumed: `speed_max` `"0"`, `speed_random_exponent` `"1"`
(the strings at `0x10926bec` and `0x10926be8`). Every explosion child declares an exponent of 1 and a positive
maximum, so neither changes an explosion; both would change the next effect that does not.

### "Why `Explosion_Flash_1` does not draw" was the capture, not the code

It had been filed as unexplained: renderer implemented, emitter implemented, material resolved, and nothing drawn at
tick 21880. **Its lifetime is exactly 0.1 seconds** — `lifetime_min` and `lifetime_max` are both `0.1` — which is under
seven ticks, and the blast it belongs to is at tick 21869. A still eleven ticks after the blast cannot show a flash
that died four ticks earlier. The captures above are three and seven ticks after it.

*Evidence class: read from the shipped binary's disassembly for both initializers, the transform and the defaults;
read from the SDK for the member order; read from the shipped `.pcf` for Flash_1's values; differential between the
two captures for the edge.*

## `Oscillate Scalar`, and a sine that is a parabola

The last function on the explosion's path this project did not run: `Explosion_Smoke_1` declares it on field 4,
ROTATION, with a rate of ±0.8 — a slow wobble on each smoke puff. `C_OP_OscillateScalar::Operate` (`FUN_107a5220`,
vtable `0x10b581cc`, factory `0x10c34cc0`) is a four-lane SIMD loop; per particle whose lifetime is above zero and
whose time — a life fraction when `start/end proportional` — lies in `[start, end)`:

```
arg   = proportional ? age · rcp( lifetime ) · freq · multiplier + phase
                     : freq · ( multiplier · curtime + phase )
field = SinEst01( arg ) · ( rate · dt ) + field            ALPHA alone clamped to [0, 1]
```

with frequency, rate, start and end each a per-particle draw at offsets 0, 1, 11 and 12 from the particle's id. **The
wave is `SinEst01SIMD`** (`ssemath.h:3129`, every constant checked in the binary), which is the parabola
`x(4 − 4x)` on each half of a period of 2 — *"sufficient for simple oscillation"*, in Valve's own comment — and not
the `Sin01SIMD` beside it that blends in a correction. At an argument of 0.25 the three give 0.75, 0.7071 for a true
sine, and 0.7078; the tests pin the first.

The unpack defaults are not the obvious ones: `oscillation field` defaults to **7** (ALPHA), `oscillation multiplier`
to 2, `oscillation start phase` to 0.5, and both proportional flags to on.

Nobody has looked at the wobble on screen; a still cannot show it.

## A direct hit is decided by the client's own entity list

`bIsPlayer` is the one input to the effect choice that is not on the wire: `TFExplosionCallback` asks
`C_BaseEntity::Instance( hEntity )->IsPlayer()` of whatever occupies `entindex` in the client's list when the blast
fires (`tf_fx_explosions.cpp:62-70`). It is resolved the same way here — against the entity table as the packet left
it, which is the same moment because a snapshot writes its entities before its temp entities — and by EXISTENCE, not
visibility, since the client keeps an entity that has left its PVS and so does this table.

**Measured on `demostf-cp_process_f12`: 118 of the 178 blasts that name an entity name a player.** The other sixty
are the control — they name something that is not a player. Every one of the 118 used to take the wall branch; for
the rocket launcher that is `ExplosionCore_wall` where the script's `ExplosionPlayerEffect` is
`ExplosionCore_MidAir`. Looked at, on blast #23 (tick 20634, a rocket into a Scout): the mid-air effect draws at the
player.

## What is not established

- **The explosion's SOUND.** `TFExplosionCallback` plays the weapon script's `ExplosionSound` from the client
  (`CLocalPlayerFilter`, `tf_fx_explosions.cpp:131-163`) — so the demo does not carry it, and nothing here plays it.
- **Water.** `UTIL_PointContents( vecOrigin ) & CONTENTS_WATER` decides `ExplosionWaterEffect`, and this project
  does not evaluate BSP contents at a point.
- **Whether any recording anywhere sets `m_iCustomParticleIndex`.** One demo says no. It is decoded and carried
  regardless, so the day one does, the value is there.
- **`GameRules()->TranslateEffectForVisionFilter( "particles", pszEffect )`**, the last thing
  `TFExplosionCallback` does before dispatching. Unread.
- **Draw order.** The engine walks a back-to-front sorted render list; the trail renderer here walks the store's
  order, as the sprite renderer does. Additive glows do not care, a translucent trail would.
- **The visibility scale** (`Visibility Proxy …`), which the engine folds into the render list's radius and alpha.
  Every explosion child declares proxy control point −1, which should mean "off" — *interpolated*, not read.
- **Whether `m_nRandomSeed` is fixed or varies per effect instance**, which decides whether two identical blasts
  draw identical trail lengths in TF2.
- **Operator strength** — each operator's own fade in and out (`operator start fadein` and its siblings), which
  scales what it does. Every explosion child declares zeros, which should mean full strength throughout;
  *interpolated*, and nothing here evaluates it.
- **The debris chunks' size and tint.** They draw, and they dominate the picture in a way TF2's do not.
- **Which `.pcf` the engine actually loads.** `ExplosionCore_` appears in five files —
  `explosion.pcf`, `explosion_high.pcf`, `explosion_dx90_slow.pcf`, `explosion_dx80.pcf` and `bigboom.pcf` — and
  which one wins is a `particles_manifest.txt` and detail-level question this has not looked at.
