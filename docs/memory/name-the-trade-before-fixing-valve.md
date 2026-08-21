---
name: name-the-trade-before-fixing-valve
description: An apparent defect in expert code is usually a trade whose other side is invisible at the site; name it before changing anything of Valve's.
metadata:
  type: feedback
---

**Before changing anything of Valve's, name what it is trading against. If you cannot name it, you
do not understand it well enough to change it.**

The owner's analogy, 2026-08-21:

> *"if you were to just randomly come across quakes fast inverse square root function, you would
> immediately notice it isnt a perfect approximation and probably call it a bug, try to fix it, but
> that would be wrong and bad to do, because then quake will start rendering at a snails pace, im
> sure theres a bunch of that in valves code."*

**Why it matters here:** every local signal on `0x5f3759df` says defect — a magic constant, a
truncated Newton iteration, a measurably wrong answer. The thing it buys, a reciprocal square root per
vertex per frame, appears nowhere in the function. Expert code concentrates the reasoning somewhere
other than the line you are reading.

The owner's grounds for the standing rule (D46): Valve hires extremely well and their non-TF2 work is
robust and well optimised; TF2's rough edges are **accretion** — features bolted beside old ones and
never revisited — which looks different from a bad decision.

**How to apply:**

- **The asymmetry is what makes this cheap.** Reproducing something correct costs nothing;
  "fixing" something correct costs a defect plus the hours to find it again. Two of them went that
  way on 2026-08-21.
- **When their value misbehaves, suspect our variables first.** Valve's `-262144` decal bias was
  declared wrong twice. Both times our depth buffer was the wrong format, so D3D scaled the constant
  by a data-dependent factor instead of the fixed `1/2^24` it is calibrated for (D48) — and then a
  stray `SetDecalBias` was overwriting the state anyway, so it had never once been in effect.
  See [[never-revert-without-asking]].
- **Things here that looked wrong and were not:** `SHADER_POLYOFFSET_DECAL` as an enum rather than a
  float; the decal bias expressed in raw buffer units rather than world distance; an overlay's face
  list including faces at 45° to its own basis (B134); `m_nFaceCountAndRenderOrder` packing two
  fields into one short.
- **If it still looks wrong after the trade is sought and not found, write it down rather than
  changing it.** `docs/findings/` exists for recorded puzzlement, and a wrong conclusion kept with
  what killed it is worth more than a silent "correction".

Related: [[nothing-is-closed]], [[read-the-spec-before-measuring-our-data]],
[[a-filed-design-choice-may-not-be-one]].
