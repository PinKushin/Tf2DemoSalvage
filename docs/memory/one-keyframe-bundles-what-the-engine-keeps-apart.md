---
name: one-keyframe-bundles-what-the-engine-keeps-apart
description: A ScenePose carries interpolated quantities AND state that changes on its own schedule, so a timestamp that suits one ruins the other — key the list by arrival, carry the applied time alongside.
metadata:
  node_type: memory
  type: project
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-02T00:00:00.000Z
---

**The engine keeps one interpolation history PER VARIABLE; this project keeps one keyframe per
entity per packet.** That difference is invisible until something needs a timestamp, and then it
decides the design.

B273. The engine stamps a history entry with the entity's own clock — `GetSimulationTime()` for
origin and angles, `GetAnimTime()` for the cycle and pose parameters — never with the packet. The
obvious fix was to key our keyframe list by that applied time. **It broke immediately, and a corpus
test named the reason**: an entity that does not simulate keeps one simulation time for minutes, so
every state change it made collapsed onto a single tick. `NoDrawTrackTests` failed with *"entity 654
was never handed over — hiding that never ends is deletion wearing a flag"*.

**Because a `ScenePose` is two kinds of thing at once.** Position and angles are interpolated
quantities that want the engine's changetime. Visibility, render mode, skin, body and weapon state
are current values that change on their own schedule and must stay in the order the demo stated
them. One timestamp cannot serve both, and the engine never has to choose because those fields are
not in a history at all — they are just fields on the entity.

**The shape that works: key the list by ARRIVAL, carry the applied time alongside.** Arrival is the
only monotonic key and it dates the state; a parallel `_appliedAt` dates the interpolated
quantities. The causality rule — a client cannot be pulled toward an update it has not received —
still tests arrival. Two questions, two numbers, neither standing in for the other.

**And the same reasoning bounds what is left undone.** The animation clock disagrees with the
simulation clock by more than eight ticks on 95.5% of the updates carrying both, so honouring it
needs a second history rather than a different stamp — filed as B274 with its measured cost rather
than bodged into the same list.

**Before changing what a timestamp MEANS, list everything the field is used for.** Here the
keyframe tick was also the list key, the ordering of state changes, the lifetime bound, and the wake
schedule. Only one of those wanted the new meaning.

Related: [[a-tick-encoded-value-expires]], [[a-pass-must-establish-its-own-state]],
[[wire-faithful-is-not-state-faithful]].
