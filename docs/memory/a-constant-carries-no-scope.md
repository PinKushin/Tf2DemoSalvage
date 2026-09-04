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
`m_DepthBias_Decal = -262144` is real (`materialsystem_config.h:226`, and the string ships in
`MaterialSystem.dll` beside `mat_depthbias_decal`), it really is `glPolygonOffset`'s `units`
(`togl/linuxwin/dxabstract.h:966`), and 262144 being 2¹⁸ makes it exactly 1/64 of a 24-bit depth
range — a chosen number, not a tuned one. Every claim in the case for adopting it was true and
cited. It was applied to the wrong surfaces three times anyway (2026-08-14, 2026-08-21 twice), and
each time the owner saw markings floating in mid-air.

**The unasked question was which surfaces Valve applies it to, and it is answerable by grep:**
`EnablePolyOffset` is declared once in the whole SDK, on `IShaderShadow` (`ishadershadow.h:255`);
`IMaterialSystem`, `IMatRenderContext`, `IMesh` and `IShaderAPI` offer no polygon-offset entry point
at all; nothing outside `stdshaders` calls the one that exists; and `lightmappedgeneric_dx9.cpp` —
which is what an `info_overlay` ordinarily is — never calls it. A polygon offset in Source is a
property of the SHADER. The constant governs bullet holes and sprays.

**Why it kept winning the argument.** Two empirical refutations existed ("restoring this floats every
decal"), and an observation invites the reply that the picture was wrong for some other reason. A
cited constant reads as settled in a way an uncited one does not, so the side with the citation won
against the side with the evidence. The arithmetic was published in B70 the same day and simply not
read: window depth goes as z ≈ 1 − N/d, so an offset Δz moves a surface Δd ≈ Δz·d²/N — at
`VIEW_NEARZ` 7, a marking 500 units out tests as though it were at 236.

**How to apply:** before matching a Valve constant, find the code that READS it and establish which
surfaces, passes or objects reach that code. A constant carries no scope, so "this is Valve's value"
is only half a claim. When a documented refutation exists, answering it requires a mechanism, not a
better-sounding reason the earlier attempt was invalid — and the owner's standing direction is to
"look at the sdk and decomp to confirm anything you think about valves code", which is what turns an
argument into a reading.

## The same rule applies to OUR constants, in both directions — D94, 2026-08-25

The entry above is about adopting a value. The mirror case is merging two, and it nearly cost two
recorded decisions.

Three declarations of `StallSeconds = 0.03` sat in `SoundCache`, `MomentScene` and `MainForm`, and
they read as a plain DRY violation — three copies of one number, only one carrying a reason. I was
one edit away from unifying them into a shared project when reading the declarations showed that two
of the three say, in their own remarks, exactly why they are separate: one is applied to a single
decode blocking the draw thread, one to a single step of a scene rebuild, one to a whole frame.

**Three symbols that agree on a number are three judgements, not one fact repeated.** The test for
merging is not "are the values equal" but "is the REASON the same". Merging them would have tied
independent judgements together so that tuning either silently moved the other — which is what the
separation was written to prevent, and what the merge would have been justified as preventing.

**And the real defect was the mirror image, found in the same read.** `ReportSlowMoment` compared a
WHOLE moment against `MomentScene.StallSeconds`, whose own documentation says "applied to one step
of a scene rebuild". Borrowing a symbol whose stated meaning is narrower than your use is the same
error as adopting Valve's decal bias for the wrong surfaces — a scope mismatch wearing a citation.

**Two cheap questions, both answered at the declaration site, neither asked:** before merging two
equal constants, read what each is applied TO; before borrowing one, read whether its documentation
describes your use.

Related: [[nothing-is-closed]], [[arithmetic-settles-disputes]], [[a-filed-design-choice-may-not-be-one]],
[[parity-is-the-search-not-the-defence]], [[never-revert-without-asking]],
[[one-place-or-it-drifts]].
