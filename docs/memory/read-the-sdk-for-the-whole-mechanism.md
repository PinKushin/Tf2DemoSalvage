---
name: read-the-sdk-for-the-whole-mechanism
description: Reading the SDK for how a feature is DECLARED and then inventing how it WORKS is the same as not reading it; find the routine that implements it.
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 9b3a8b35-1dc8-47b0-a320-73b01288f10c
  modified: 2026-08-14T19:33:53.026Z
---

**Read the SDK routine that IMPLEMENTS the behaviour, not just the one that declares it.** Finding
the flag, the send-prop or the constant is the easy half and feels like having done the research.

**Why:** the owner called this out after two avoidable defects in one session, both in bone merging.
`FollowEntity` and `EF_BONEMERGE` were read from the SDK, correctly — and then the merge itself was
written from scratch:

- Unmatched bones were given the worn model's REST pose in its own model space. Valve's
  `CBoneMergeCache::MergeMatchingBones` copies only the matches, because the worn model has already
  run its own full `SetupBones` — so an unmatched bone holds a place walked down its OWN hierarchy
  from its parent, which may itself have been merged. The invented fallback tore items across the
  map: a `ghostly_gibus` matched 1 bone of 8, seven stayed at the origin, and the triangles between
  stretched from the scout's head to his feet as a flat sheet.
- Worn models were left to the ordinary bake-vs-skin budget, so every cheap cosmetic was baked and
  had no bones at all to merge onto. Hats drew at ankle height.

The file was `src/game/client/bone_merge_cache.cpp`, about forty lines, and it answers both.

**How to apply:** after finding the flag, grep for what consumes it and read that. "I read the SDK"
is only true of the specific routine that was opened. Related:
[[read-the-encoder-not-the-decoder]] and [[research-before-code]] — same failure, one level deeper:
the hypothesis was verified for the declaration and assumed for the mechanism.
