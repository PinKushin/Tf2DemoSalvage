# 57 — a hitscan shot is a seed, and the seed needs Valve's own RNG

**Read 2026-09-20, from the SDK and then from `vstdlib.dll`** (B415). The owner had reported that hitscan effects,
explosion particles and impact decals are all missing; the census in B415 says f12 alone carries 2,176 `CTEFireBullets`,
2,786 `CTETFExplosion` and 2,368 decal events, of which the timeline consumes none.

## The wire carries no bullet paths

`DT_TEFireBullets` (`c_tf_fx.cpp:36`) is nine fields:

```
m_vecOrigin, m_vecAngles[0], m_vecAngles[1], m_iWeaponID,
m_iMode, m_iSeed, m_iPlayer, m_flSpread, m_bCritical
```

No directions, no impact points, no tracer endpoints. The client reconstructs the whole shot:

```cpp
void C_TEFireBullets::PostDataUpdate( DataUpdateType_t updateType )
{
    if ( updateType == DATA_UPDATE_CREATED )
    {
        m_vecAngles.z = 0;
        FX_FireBullets( NULL, m_iPlayer + 1, m_vecOrigin, m_vecAngles, m_iWeaponID,
                        m_iMode, m_iSeed, m_flSpread, -1, m_bCritical );
    }
}
```

**`m_iPlayer + 1`** — the wire carries the player one below the entity index. Reading it straight would attribute every
shot to the wrong player, and on a full server it would still be *a* player, which is the kind of wrong that looks right.

## What the client does with it

`FX_FireBullets` (`tf_fx_shared.cpp`), per bullet of the weapon script's `m_nBulletsPerShot`:

```cpp
RandomSeed( iSeed );                                    // reseeded EVERY bullet
float flVariance = 0.5f;                                // or mult_spread_scale_first_shot on an accurate first shot
x = RandomFloat(-flVariance, flVariance) + RandomFloat(-flVariance, flVariance);
y = RandomFloat(-flVariance, flVariance) + RandomFloat(-flVariance, flVariance);
fireInfo.m_vecDirShooting = vecShootForward + (x * flSpread * vecShootRight)
                                            + (y * flSpread * vecShootUp);
fireInfo.m_vecDirShooting.NormalizeInPlace();
pPlayer->FireBullet( pWpn, fireInfo, bDoEffects, nDamageType, nCustomDamageType );
++iSeed;                                                // the next bullet's seed
```

Three details that a reconstruction gets wrong by default:

- **Two draws per axis, not one.** `RandomFloat + RandomFloat` is a triangular distribution — a uniform one would put
  too many pellets at the edge of the cone.
- **`fireInfo.m_iTracerFreq = 2`** for everything except the minigun: a tracer on every SECOND bullet.
- **The seed is re-applied per bullet and incremented after**, so a shotgun's pellets are a seed *run*, not one draw.

A buckshot weapon with fixed spread enabled takes `g_vecFixedWpnSpreadPellets` instead of the random draws.

## So the RNG has to match bit for bit, and it is not in the SDK

`public/vstdlib/random.h` declares `CUniformRandomStream` with `m_idum`, `m_iy`, `m_iv[NTAB]` and `#define NTAB 32` — the
shape of Numerical Recipes' `ran1`, a Park–Miller generator with a Bays–Durham shuffle — **and not one constant**.
`random.cpp` ships closed.

**Settled in the disassembly of `vstdlib.dll`** (TF2's own, `bin/x64`, imported as the Ghidra project `tf2vstdlib`). The
function's own assert string names the file it came from: `src\vstdlib\random.cpp`.

`CUniformRandomStream::GenerateRandomNumber` @ `0x18000f110`:

```c
iVar7 = iVar7 * 0x41a7 + (iVar7 / 0x1f31d) * -0x7fffffff;
if (iVar7 < 0) { iVar7 = iVar7 + 0x7fffffff; }
...
uVar4 = (int)(iVar6 + (iVar6 >> 0x1f & 0x3ffffffU)) >> 0x1a;   // j = iy / NDIV
m_iy     = m_iv[j];
m_iv[j]  = m_idum;
return m_iy;
```

| constant | as read | value |
|---|---|---|
| IA | `0x41a7` | 16807 |
| IQ | `0x1f31d` | 127773 |
| IM | `-0x7fffffff`, and the `+=` on the negative branch | 2147483647 |
| NTAB | the `< 0x20` guards | 32 |
| NDIV | `>> 0x1a` with the `0x3ffffff` round-toward-zero idiom | 2²⁶ = 67108864 |

**IR is not in the binary, and that is not a missing constant.** Textbook `ran1` is
`idum = IA*(idum - k*IQ) - IR*k`, which the compiler folded to `idum*IA - (idum/IQ)*IM` — valid exactly because
`IA*IQ + IR == IM` (`16807*127773 + 2836 == 2147483647`). Reading the fold as "IR is 0" would be wrong; it is absorbed.

`CUniformRandomStream::SetSeed` @ `0x18000fb10` is two writes: `m_iy = 0`, `m_idum = -|seed|`.

`CUniformRandomStream::RandomFloat` @ `0x18000f9b0`:

```c
iVar1 = GenerateRandomNumber(this);
fVar2 = (float)((double)iVar1 * _DAT_180031df8);
if (_DAT_180031e08 < (double)fVar2) { fVar2 = DAT_180031e00; }
return (param_2 - param_1) * fVar2 + param_1;
```

Read out of memory rather than assumed:

| address | type | value | |
|---|---|---|---|
| `0x180031df8` | double | `4.656612875245797E-10` | AM, which is 1/2147483647 |
| `0x180031e08` | double | `0.99999988` | the clamp compare |
| `0x180031e00` | float | `0.9999999` (`0x3f7ffffe`) | RNMX, substituted |

**The widening is load-bearing**: the product is rounded to `float` FIRST and then widened back to `double` for the
comparison. Comparing in double throughout would clamp on a different set of inputs.

The warm-up is `NTAB + 7` = 39 iterations filling `m_iv` backwards, which Ghidra shows unrolled eight ways.

*Evidence class: read from the SDK for the call path, read from the shipped binary's disassembly and its memory for every
constant.* *Not established*: `RandomInt`'s own mapping, and whether `mult_spread_scale_first_shot` appears on any
weapon a demo in this corpus carries.
