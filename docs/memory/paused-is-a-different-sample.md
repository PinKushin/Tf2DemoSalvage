---
name: paused-is-a-different-sample
description: "A paused Source client draws last-received positions with no cl_interp delay, so a paused frame is a DIFFERENT pose from the playing one at the same tick — and every --shot capture is a paused frame"
metadata:
  type: project
---

**`engine->IsPaused()` clears `s_bInterpolate` for every entity at once**
(`C_BaseEntity::InterpolateServerEntities`, `client/c_baseentity.cpp:3226`);
`IsInterpolationEnabled()` returns that flag (`c_baseentity.h:2156`), and `BaseInterpolatePart1`
answers it with `MoveToLastReceivedPosition()` and `INTERPOLATE_STOP` (`c_baseentity.cpp:2845`). So
pausing does not freeze the picture — it CHANGES it, by the whole interpolation window, to whatever
the last update stated (B399).

**Why it matters for every comparison against the real game:** the owner's golden screenshots come
from `demo_gototick <tick> 0 1`, which pauses. A viewer that applies the `cl_interp` delay to a
static capture is comparing a pose eight ticks older than the client's. On the f12 demo that drew a
rocket-jumping soldier still airborne beside a ledge where the real client had already landed him,
and it was chased for a long session as a rocket spawn-position bug (B397).

**How to apply:** any still capture — ours or TF2's — is a PAUSED frame, so sample with
interpolation off and compare like with like. When a divergence looks like a constant positional
offset along an entity's direction of travel, measure it in TICKS of that entity's own motion before
looking for a geometry or attachment cause; the `jitter` probe prints the keyframes to divide by.
And note the two populations differ: on that demo the players' updates applied two ticks after
arrival while the rocket's applied at arrival, so one delay produced two different-looking errors.
Related: [[an-entity-index-is-not-a-real-name]], [[check-at-the-owners-moment]].
