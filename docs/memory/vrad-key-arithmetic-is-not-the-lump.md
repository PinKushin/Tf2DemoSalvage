---
name: vrad-key-arithmetic-is-not-the-lump
description: "Reading vrad's light-key conversion does not tell you what scale the compiled lump holds — measure the map."
metadata: 
  node_type: memory
  type: project
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-08-27T04:41:21.361Z
---

**Deriving a lump's numeric scale from the compiler's source is not reading the source, it is
predicting from it.** vrad's `LightForString` (`utils/vrad/lightmap.cpp:1088`) converts a `light`
key with `pow(r/255.0, 2.2) * 255`, and that `* 255` genuinely says the light key becomes a
nought-to-255 value. It does **not** say `LUMP_WORLDLIGHTS` on a shipped map contains numbers near
255.

Measured on `cp_process_final` 2026-08-27: **sky light 2.313, brightest leaf ambient sample 2.938.**
Both lumps sit in Valve's overbright range — above white, nowhere near 255. The predicted mismatch
between a nought-to-255 world light and a nought-to-one `TexLightToLinear` ambient cube **does not
exist**, and a fix aimed at it would have scaled a correct value into a wrong one.

**Why it was convincing, which is the part worth remembering.** The theory explained every symptom
of [[a-hole-is-not-always-a-drawing-fault]]-style washed-out viewmodels: point lights would survive
the mismatch because `LocalLights.Falloff` divides by distance squared and absorbs a factor of 200
at any room distance, while `emit_skylight` is directional, receives no falloff, and reaches a
shader that multiplies it only by a Lambert term. Excess light, only where the sun reaches, worst at
the eye. A hypothesis that predicts the observations is still a hypothesis.

**How to apply:** when a question is "what range does this data occupy", the answer is in the
compiled file, not in the tool that wrote it. Read the lump. It is usually a dozen lines and a
`dotnet test` away, and it is the same rule as
[[nothing-is-closed]] pointing the other way — read the source to learn the
MECHANISM, measure the data to learn the VALUES. Confusing which question you have is what makes a
wrong answer feel cited.

Kept as `LightScaleConformanceTests`, asserting the measured truth (both lumps in the overbright
range) rather than the defect, so the test survives whatever fixes the bug. See
[[a-test-can-outlive-its-design]].
