---
name: a-constant-carries-no-scope
description: Quoting Valve's decal bias with a file and line said nothing about which surfaces it applies to; ask what a value is applied TO before matching it.
metadata:
  type: project
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

Related: [[nothing-is-closed]], [[read-the-spec-before-measuring-our-data]],
[[arithmetic-settles-disputes]], [[a-filed-design-choice-may-not-be-one]],
[[read-the-sdk-for-the-whole-mechanism]].
