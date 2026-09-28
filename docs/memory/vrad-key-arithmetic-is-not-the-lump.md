---
name: vrad-key-arithmetic-is-not-the-lump
description: "Reading vrad's light-key conversion does not tell you what scale the compiled lump holds — measure the map."
metadata: 
  node_type: memory
  type: project
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-09T03:54:53.438Z
---

**Deriving a lump's numeric scale from the compiler's source is predicting from it, not reading it.**
vrad's key conversion genuinely scales a light value to 0-255 — that does NOT say the compiled lump
on a shipped map contains numbers near 255. Measured: sky light 2.313, brightest ambient sample 2.938
— both in Valve's overbright range, nowhere near 255. A predicted scale mismatch between two lumps
did not exist, and a fix aimed at it would have scaled a correct value into a wrong one.

**Why convincing:** the theory explained every symptom of washed-out lighting — point lights survive
because falloff divides by distance squared, directional sky light has no falloff and reaches a
shader multiplying only by a Lambert term. A hypothesis predicting the observations is still a
hypothesis.

**How to apply:** when the question is "what range does this data occupy", the answer is in the
COMPILED FILE, not the tool that wrote it. Read the source to learn the MECHANISM; measure the data to
learn the VALUES — confusing which question you have is what makes a wrong answer feel cited.

See [[nothing-is-closed]], [[a-test-can-outlive-its-design]].
