# 62 — An effect without a class is a delta

**Question.** A sentry gun fires. The server dispatches `TF_3rdPersonMuzzleFlash_SentryGun` with the sentry's
entity index and its muzzle attachment (`tf_obj_sentrygun.cpp:1562`). Why did every one of the 45 on f12 decode
with entity 0 and attachment 0?

## The first reading: a dispatch was taken as its constructor

`CEffectData`'s constructor sets `m_flScale` to 1 and `m_hEntity` to `INVALID_EHANDLE`. The first port read an
unsent field as that constructor value. The server's `Impact` dispatches then split into 233 on players and
none on the world. That cannot be right. TF's `ImpactCallback` returns on a null entity
(`tf_fx_impacts.cpp:32`), so a world impact whose entity is unsent would draw nothing, and in the game sentry
bullets do mark walls. The world is entity 0. A zero is exactly what a delta leaves out, so the receiver must turn
"unsent" into 0 through `RecvProxy_EntIndex` (`effect_dispatch_data.cpp:31`). *Arithmetic from the source.* After
that correction, two world impacts appeared on f12, with surfaceprop 3 and the sentry's damage bits.
*Measured.*

The muzzle flashes still read 0, and entity 0 is the world, not a sentry.

## The pair gave it away

Each muzzle flash is the second of two dispatches the sentry sends in the same message. The first is the
`Tracer` that `FireBullets` sends, and it carries the same sentry and the same attachment. Only the flags differ:
the tracer's are 2 (`TRACER_FLAG_USEATTACHMENT`), the flash's are the sentry's level. So the fields reading zero
were exactly the fields the two dispatches share. *Differential, measured.*

The decoder already knew one half of the rule: an effect may leave out its class id and reuse the previous
effect's. The other half is what that means. An effect with no class id is written as a delta against the
previous effect's data (`WriteAllDeltaProps( lastEvent->pData, … )`), not against zero. Its wire list is the
change, not the effect. *Inferred from the data; the engine's own temp-entity writer and parser were not read.
The Ghidra engine project was locked.*

With the previous effect's state laid under each class-less effect, every flash reads its sentry: entity 502 or
407, attachment 1 or 4. These match their tracers. *Measured.*

## What it touched

The rule applies to every temp entity class, so it lives in `EntityDecoder.DecodeTempEntities`. There,
`DecodedTempEntity.State` is the effect as received, and `Properties` stays what the wire carried, because
re-encoding a demo must reproduce its bits. Every feed now reads `State`: blood, shots, decals, explosions,
gestures and dispatches. So does the scan that names a dispatch, since a second dispatch of the same effect omits
its unchanged name. This is the same split as an entity's baseline (`wire-faithful-is-not-state-faithful`).
