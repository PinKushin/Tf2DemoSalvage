---
name: TF2 barely uses dynamic lights
description: Explosions, muzzle flashes and fire allocate no dlight in TF2; only the recorder's own Dragon's Fury fireball does
type: project
---
**Rule:** before you add a dlight for an effect, read that effect's client code. TF2 usually allocates none. The
exception is the local player's own Dragon's Fury fireball.

**Why:** B425 was filed on the belief that explosions, muzzle flashes and burning players should light the scene.
Reading the source settled it:

- `C_BaseExplosionEffect::Create` has `//FIXME: CreateDynamicLight();`.
- `C_TFPlayer`, `C_TFWeaponBase` and `CTFViewModel` override the base muzzle-flash elight away.
- Nothing allocates a light for fire.

Across 19 demos, the allocators that could fire (`CTEDynamicLight`, `light_dynamic`, `EF_DIMLIGHT`) drove zero lights.

**How to apply:**
- Treat "X should light the scene" as a claim to check in the SDK, not a gap to fill.
- The list, decay and model ranking are ported in `DynamicLights`.
- What is still open is in B425.

See `docs/findings/66-tf2-barely-uses-dynamic-lights.md`.
