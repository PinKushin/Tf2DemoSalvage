# 20 — How the client turns stored values into a moving picture

A demo states where things were at the moments the server sent an update. A renderer draws at
whatever moment the frame lands on, which is almost never one of those. Everything below is about
that gap, and all of it is **read from published source**.

## The storage shape is the same one we chose

`CInterpolatedVarEntryBase` in `src/game/client/interpolatedvar.h` is a value plus a `changetime`,
held in an `m_VarHistory` list. That is a keyframe, and the client keeps a history of them per
networked variable rather than a value per tick.

`COMPARE_HISTORY` compares entries by value, the same de-duplication `ScenePropTrack.Add`
performs. So this project's storage is not a departure from the engine — it arrived at the same
shape for the same reason.

**The divergence is lifetime.** The client streams, so it discards:

```cpp
RemoveOldEntries( gpGlobals->curtime - interpolation_amount - 2.0f );
```

roughly a two-second window. This project decodes the whole demo before playing any of it and
keeps everything, which is what makes reverse playback cost the same as forward — see D-notes on
playback. The engine cannot run a demo backwards for exactly this reason.

## Three quantities, three different rules

Getting any of these wrong produces motion that looks like a broken model rather than a broken
interpolator, which is why they are worth stating separately.

### Position: linear

Ordinary interpolation between the bracketing entries.

### Angles: quaternions, never per component

`mathlib.h:661`:

```cpp
// YWB:  Specialization for interpolating euler angles via quaternions...
template<> FORCEINLINE QAngle Lerp<QAngle>( float flPercent, const QAngle& q1, const QAngle& q2 )
```

It converts both angles to quaternions, slerps, and converts back. Two consequences:

- **Taking the short way round is a property of slerp, not an extra rule.** 350° to 10° passes
  through zero rather than turning 340° the other way, and nothing had to special-case it.
- **Hermite is refused outright for angles.** `lerp_functions.h:104` specialises
  `Lerp_Hermite<QAngle>` to plain interpolation with the comment *"Can't do hermite with QAngles,
  get discontinuities"*.

The conversions are `AngleQuaternion` and `QuaternionAngles` in `mathlib_base.cpp`, transcribed
rather than rederived: a QAngle is (pitch, yaw, roll) about three axes in a particular order, and a
plausible reconstruction is wrong only for some inputs — the worst kind of wrong.

`QuaternionAngles` carries Valve's own note of a singularity near pitch ±90. It is real and it is
inherent: pointing straight up, yaw and roll describe the same rotation and the split between them
is arbitrary.

### Animation cycle: looping, and only when the sequence loops

`LoopingLerp<float>` in `lerp_functions.h`:

```cpp
if ( fabs( flTo - flFrom ) >= 0.5f )
{
    if (flFrom < flTo) flFrom += 1.0f; else flTo += 1.0f;
}
float s = flTo * flPercent + flFrom * (1.0f - flPercent);
s = s - (int)(s);
if (s < 0.0f) s = s + 1.0f;
```

A cycle runs 0 to 1 and wraps. A gap of half a cycle or more means the animation **looped** rather
than jumped, so the smaller value belongs to the next repetition. Without this rule a looping model
plays forwards and then rewinds through its entire animation at every loop point.

The engine applies it conditionally — `c_baseanimating.cpp:4472`:

```cpp
m_iv_flCycle.SetLooping( IsSequenceLooping( GetSequence() ) );
```

so a one-shot animation does not wrap.

**A sequence change is a cut, not a blend.** Two animations share no timeline, so 0.9 in one and
0.1 in the next are not two points on one curve — and it is precisely the loop rule that would
otherwise fire on those unrelated numbers. The engine resets the variable instead
(`m_iv_flCycle.Reset()`).

## What is deliberately not implemented

**Hermite interpolation for position.** The engine's default is `Lerp_Hermite`, which needs three
points. This project interpolates linearly.

It is a real gap and it is recorded as one rather than described as parity. Two things make it a
small one: Valve falls back to linear for angles by its own choice, and exposes
`INTERPOLATE_LINEAR_ONLY` as a supported mode for everything else — so linear is a shape the engine
also produces. The visible difference is smoothness through a direction change, most noticeable on
something fast and curving, which in TF2 means projectiles.

**`cl_interp` as a concept.** The client deliberately renders *in the past* — by default 100 ms —
so it always has a later keyframe to interpolate towards. This project has the whole demo in hand,
so the later keyframe always exists and there is nothing to delay for. Worth stating because the
absence looks like an oversight otherwise: the engine's interpolation delay is a consequence of
streaming, not a property of correct playback.
