---
name: a-constant-carries-no-scope
description: "Quoting Valve's decal bias with a file and line said nothing about which surfaces it applies to; ask what a value is applied TO before matching it."
metadata: 
  node_type: memory
  type: project
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-08-26T01:44:47.064Z
---

**A number copied from Valve's source with a correct citation is still a guess about scope.**
`m_DepthBias_Decal = -262144` is real (`materialsystem_config.h:226`), really is `glPolygonOffset`'s
`units` (`togl/linuxwin/dxabstract.h:966`), and 2¹⁸ is a chosen, not tuned, number. Every claim in
the case for it was true and cited — and it was applied to the wrong surfaces three times anyway
(2026-08-14, 2026-08-21 ×2), each time producing markings floating in mid-air.

**The unasked question was which surfaces Valve applies it to**, answerable by grep: `EnablePolyOffset`
is declared once, on `IShaderShadow` (`ishadershadow.h:255`); no other render interface exposes it;
nothing outside `stdshaders` calls it; `lightmappedgeneric_dx9.cpp` (an `info_overlay`'s usual
shader) never calls it. A polygon offset in Source is a property of the SHADER — the constant governs
bullet holes and sprays, not overlays.

**Why it kept winning:** a cited constant reads as settled in a way an empirical refutation does not.
The arithmetic (published B70 the same day, unread) settles it: window depth z ≈ 1 − N/d, so offset Δz moves
a surface Δd ≈ Δz·d²/N — at `VIEW_NEARZ` 7, a marking 500 units out tests as though at 236.

**How to apply:** before matching a Valve constant, find the code that READS it and which surfaces
reach that code. A constant carries no scope. Answering a documented refutation needs a mechanism,
not a better-sounding reason it was invalid — per the owner's standing direction to confirm against
SDK and decomp.

## The same rule applies to OUR constants, in both directions — D94, 2026-08-25

Three declarations of `StallSeconds = 0.03` in `SoundCache`, `MomentScene` and `MainForm` looked like
a plain DRY violation. Reading them showed two state, in their own remarks, why they're separate: one
applies to a single decode blocking the draw thread, one to one scene-rebuild step, one to a whole
frame. **Three symbols agreeing on a number are three judgements, not one fact repeated** — merge on
whether the REASON is the same, not whether the values are equal.

**The real defect was the mirror image:** `ReportSlowMoment` compared a WHOLE moment against
`MomentScene.StallSeconds`, documented as applying to one rebuild step — the same scope mismatch as
the decal bias, wearing a citation.

Related: [[nothing-is-closed]], [[arithmetic-settles-disputes]], [[a-filed-design-choice-may-not-be-one]],
[[parity-is-the-search-not-the-defence]], [[never-revert-without-asking]], [[one-place-or-it-drifts]].
