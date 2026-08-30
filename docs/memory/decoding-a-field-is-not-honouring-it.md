---
name: decoding-a-field-is-not-honouring-it
description: A newly decoded field can reach a consumer that cannot act on it, so the picture is unchanged and the tests are green.
metadata:
  type: project
---

Adding a decode is two jobs: reading the value, and reaching **every** place the engine consults it.
The first has obvious tests and the second has none, so a field can arrive, be carried through the
pose, be asserted on, and still change nothing on screen.

**Measured, 2026-08-29 (B221 → B231).** `m_nRenderMode` was decoded and routed to the render GROUP,
where `RenderGroups.For` classifies an entity at alpha 255 and mode 10 as translucent — and
translucent at alpha 255 draws solid. So the decode was correct, the group was correct, and the
picture was identical. The engine consults the same field in a second place this project had no
equivalent for:

```c
bool C_BaseEntity::ShouldDraw()          // c_baseentity.cpp:1437
{
    if ( m_nRenderMode == kRenderNone )  // some rendermodes prevent rendering
        return false;
    return (model != 0) && !IsEffectActive(EF_NODRAW) && (index != 0);
}
```

`EF_NODRAW` was already honoured in `IsDrawn`, one line away. The render mode was not, because
nothing had ever decoded it — so when it arrived it went to the consumer somebody was thinking
about rather than to all of them.

**Cost:** eighteen invisible `func_door` movers on `cp_fulgur` drawn as solid slabs, which is what
sent an evening into a "rotated grate".

**How to apply:** when a field is newly decoded, grep the SDK for **every** use of it, not the one
that motivated the work — `grep -rn m_nRenderMode` finds `ShouldDraw`, `IsTransparent`,
`ComputeFxBlend` and the leaf classifier, and they are four different decisions. Then ask which of
them this project already has a home for, and where the others belong. A field consulted in four
places and honoured in one is three quarters unimplemented, and the tests for the one look exactly
like the tests for all four.

Related: [[read-the-sdk-for-the-whole-mechanism]], [[output-level-assertion-or-it-is-not-done]],
[[half-a-mechanism-is-not-parity]], [[measure-the-output-not-the-capability]].
