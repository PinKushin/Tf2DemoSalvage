# 59 — a tracer is a counter and two control points

**Read 2026-09-21, from the SDK and then from `client.dll`** (B415). Finding 57 rebuilt a shot's directions from its
seed. This one follows each bullet to the screen.

## Which bullets draw one

The client half of `CTFPlayer::FireBullet` (`tf_player_shared.cpp:10276`) traces the bullet itself and then:

```cpp
if ( trace.fraction < 1.0 )
{
    ...
    static int tracerCount;
    if ( ( ( info.m_iTracerFreq != 0 ) && ( tracerCount++ % info.m_iTracerFreq ) == 0 ) || penetrate-all )
        UTIL_ParticleTracer( GetTracerType() [+ "_crit"], vecStart, trace.endpos, entindex(), TRACER_DONT_USE_ATTACHMENT, true );
}
```

Three consequences:

- **The counter is one static for the whole client.** Whether a given bullet draws depends on every bullet the client
  traced before it, from every player. A tracer builder therefore has to walk the whole demo in fire order. Asking
  about one shot on its own gives a wrong answer. The counter starts at zero here, as a freshly started client's does.
  A client that has already played something is at some other phase, and no demo records which.
- **A miss does not count.** The increment sits inside `fraction < 1.0`, so a bullet that reaches the end of its range
  neither draws nor advances the counter.
- **`m_iTracerFreq` is 2, except for the minigun, which keeps 4.** `FX_FireBullets` sets 2 for every weapon but the
  minigun. The minigun keeps `FireBulletsInfo_t`'s default of 4 (`shareddefs.h:719`). Both share the one counter.

`GetTracerType` is the player's active weapon's (`baseplayer_shared.cpp:1923`). `CTFWeaponBase::GetTracerType`
(`tf_weaponbase.cpp:2849`) returns the script's `TracerEffect` with `_red` or `_blue`, `BrightTracer` for a minigun with
none, and otherwise `CBaseEntity`'s NULL. The crit suffix comes from `m_bCritical` only. `m_bCurrentAttackIsCrit` is a
prediction field that is never networked, so it is false for every player but the local one.

## Where it starts, and what the first-shot branch is not

`C_TEFireBullets::PostDataUpdate` calls `FX_FireBullets( NULL, … )`. **With `pWpn` NULL, two branches that look live
are dead for every bullet in a demo:**

- the first-shot accuracy bonus, whose guard is `iBullet == 0 && pWpn`. Finding 57 filed it as missing, but it never
  runs for these bullets.
- the viewmodel start, `pLocalPlayer && !ShouldDrawThisPlayer() && … && pWpn`.

So the tracer starts at the active weapon's world-model `muzzle` attachment when the shooter is not dormant, and at
`m_vecOrigin` when there is none.

## The two control points

`UTIL_ParticleTracer` dispatches `"ParticleTracer"`. `ParticleTracerCallback` (`fx_tracer.cpp:131`) creates the effect
through `DispatchParticleEffect( index, start, end, VectorAngles( end − start ), shooter )`. On the entity branch,
`ParticleEffectCallback` (`c_particle_system.cpp`) sets control point 0 to the start, control point 1 to the end, and
control point 0's orientation from those angles, **before the effect first simulates**. It creates nothing for a
dormant shooter. It also opens with `if ( !player ) return;`, so the question of whether SourceTV has a local player
matters here. It does, and finding 58 records that correction.

## `move particles between 2 control points`, out of `client.dll`

Every tracer system uses it (`bullet_tracer01_red`: one particle, radius 2, `effects\tracer1.vmt`,
`render_sprite_trail`, speed 5000). The initializer ships only in `particles.lib`.

- Its name string is `Move Particles Between 2 Control Points` at `0x10b5c414`, referenced only from the factory
  object at `0x10c39a08`.
- The factory's `CreateInstance` (`0x107bf830`) allocates `0x40` bytes with vtable `0x10b5c37c`.
- Slot 24, the scalar initializer, is `FUN_107bf510`.
- The unpack table is filled by static-init `MOV`s at `0x1007d64d`:

| member | offset | type | default |
|---|---|---|---|
| minimum speed | `+0x2c` | float | "1" |
| maximum speed | `+0x30` | float | "1" |
| end spread | `+0x34` | float | "0" |
| start offset | `+0x38` | float | "0" |
| end control point | `+0x3c` | int | "1" |

Per particle, as disassembled:

```
end   = GetControlPointAtTime( end control point, CREATION_TIME )
if ( end spread > 0 )     end += RandomVectorInUnitSphere() · spread
delta = end − XYZ;  dist = sqrtss( delta · delta )
if ( start offset > 0 )   start += delta · offset / ( dist + FLT_EPSILON );  recompute delta, dist
speed = ( max − min ) · r + min
LIFE_DURATION = dist / ( speed + FLT_EPSILON )           FLT_EPSILON is 0x34000000, read at 0x10992f80
PREV_XYZ      = XYZ − delta · ( speed / dist ) · dt
```

**The particle lives exactly as long as the trip takes**, which is how a tracer ends at the wall and not past it.

**The start-offset branch contains a Valve bug.** The moved start is written back with x at `+0`, but y and z at `+4`
and `+8` (`0x107bf7c6`, `0x107bf7d6`). Those are the neighbouring particles' x in the four-wide SoA block, not this
particle's y and z at `+0x10` and `+0x20`. So the particle itself moves only in x, and up to two neighbours have their x
overwritten. The first half is reproduced; the second is not, because this store is not four-wide. No tracer declares
an offset.

## Measured

On `demostf-cp_process_f12`, through `LoadedMap.Read`, the viewer's own load: **2,176 shots, 7,957 tracers**. The effect
names are the scattergun's (7,400 before crits), the minigun's `bullet_tracer01` (204), the pistols' (206) and the
shotguns' (20), and every one of them has a definition. The arithmetic agrees with the counter rule: 1,506 scattergun
shots × 10 pellets ÷ 2 ≈ 7,530, and 204 minigun shots × 4 ÷ 4 = 204. The rifle's 26 shots draw nothing, because its
script declares no `TracerEffect`.

What is still wrong is listed in B415: the muzzle, player hitboxes, an item's own `tracer_effect`, and
`mult_bullets_per_shot`.
