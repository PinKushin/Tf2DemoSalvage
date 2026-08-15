---
name: read-the-spec-before-measuring-our-data
description: On a rendering defect, read Valve's shader or header for the thing being drawn BEFORE measuring this project's data; measurement confirms our data is right and says nothing about what was never implemented.
metadata:
  type: feedback
---

**A visual defect means read the SDK for that feature FIRST. Not after the theories run out.**

**Why:** measuring this project's own data can only find data that is wrong. It cannot find a
feature that was never implemented, because every number will be correct — and it will look like
progress the whole time. One session, one capture point:

- Six measurements of the model — bodygroup tags, vertex spans, `.vvd` fixups, `.vtx`/`.mdl`
  pairing, material indices, instance census. Every one correct. The model was never wrong.
- Four renderer theories about the wall stripes before anyone asked the BSP what they were.
- The answers, each found in minutes once the right file was opened:
  `stdshaders/unlittwotexture_ps2x.fxc` (two textures MULTIPLIED, alpha forced to 1),
  `imaterialsystem.h:180` (MATERIAL_CULLMODE_CCW, so front faces are clockwise),
  `imaterial.h:369` (`$nocull` is MATERIAL_VAR_NOCULL, a per-material flag).

The owner had already made this a standing rule, in CLAUDE.md and in
[[read-the-sdk-for-the-whole-mechanism]], and had to repeat it. That is the actual failure: the rule
was known and applied late.

**How to apply.** On any "it looks wrong" report, before writing a probe or a log:

1. Name the shader or subsystem responsible — the VMT's shader name, the material flag, the engine
   routine.
2. Open Valve's file for it. `F:/src/source-sdk-2013`, `stdshaders/` for shaders, `public/` for the
   flags and enums. Reading published source is not decompilation.
3. Only then measure, and measure the gap between what that file says and what this project does.

**The tell that this is being skipped:** a series of measurements that all come back correct. Three
in a row means the question is wrong, not the data. Stop and go read.

Related: [[measure-every-hop-before-blaming-one]] is the same discipline for OUR chain; this one is
for the part of the chain that is Valve's and was never built.
