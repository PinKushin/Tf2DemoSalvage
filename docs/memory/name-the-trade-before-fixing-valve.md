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

## The one qualification: the trade may have been against a platform that is gone

The owner's caveat, and it stops the rule becoming an absolute:

> *"some of the optimizations may be dx 9 only or earlier, and rely on bugs which existed then but
> dont exist now, but we will find those when they cause issues with the dx11 rendering"*

**So "name the trade" has a second possible answer: the trade was against Direct3D 9, and the other
side of it no longer exists.** That is not Valve being wrong; it is a correct decision whose
premise expired. Transcribing it faithfully then produces the wrong picture on DX11, and the fix is
to reproduce the *intent* rather than the mechanism.

**The tell is specific and worth recognising:** a faithful transcription that misbehaves on DX11
while the reasoning behind it is sound. At that point the question changes from "what is this trading
against" to "what did Direct3D 9 do here that Direct3D 11 does not".

Already met on this project: the decal bias constants. `m_DepthBias_Decal = -262144` is a D3D9-era
value, and the two APIs do not agree on what a depth bias even is — D3D9's `D3DRS_DEPTHBIAS` is a
float added to depth, while D3D11's is an integer scaled by a factor the **buffer format** decides.
The number therefore cannot mean the same thing in both, whatever the format (D48).

Classic candidates to expect: the D3D9 half-texel offset for screen-space quads, which is wrong on
DX11; anything working around a driver behaviour rather than an API rule; and render-state defaults,
which differ between the two APIs and were often left unset deliberately.

**Console paths are the same hazard with a visible marker, which makes them the easy case.** The
owner: *"i know there are some video game console optimizations like that"*. Source is full of
`#if defined( _X360 )` and `_PS3` blocks, and they optimise for hardware this project is not on.

Two met while reading for B135, neither of which means anything on PC:

```cpp
#if defined( _X360 )
    pRenderContext->PushVertexShaderGPRAllocation( 32 ); //lean toward pixel shader threads
#endif
```

— `CSimpleWorldView::Draw`, partitioning the Xbox 360's unified shader registers between vertex and
pixel work, a knob PC hardware does not expose. And in `DecalModulate_dx9.cpp` the vertex-texture
path is chosen under `#ifndef _X360`.

**So check the guard before transcribing.** A `_X360` or `_PS3` block is an answer to a different
machine's question, and the PC branch beside it is the one to read. Unlike the DX9-era traps these
announce themselves, so the only way to be caught is not to look.

Related: [[nothing-is-closed]], [[read-the-spec-before-measuring-our-data]],
[[a-filed-design-choice-may-not-be-one]].
