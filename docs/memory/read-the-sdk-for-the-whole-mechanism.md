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

**It happened three times in one session on 2026-08-31, and the shape was READ-side versus
WRITE-side.** A demo's per-entity baselines were being investigated. `CL_CopyNewEntity` — how an
entering entity is DECODED against a baseline — was read out of a decompilation of `engine.dll`,
carefully and correctly. How a baseline is STORED was then reasoned about rather than read, and
produced three confident wrong answers in a row:

- "rebuilding from the baseline on every Enter fixes it" — ran it, changed nothing;
- "our missing baselines are in the unparsed `dem_stringtables`" — they are not in it;
- "the engine checkpoints every entity it decoded" — it does not, and a conformance test citing the
  reference parser said so before the experiment did.

The store side was **twelve lines further down the same function already open**, and it settled all
three in five minutes once looked at: the store lives in the entering path only, and it saves
`RecvTable_MergeDeltas( table, fromBuf, update, newBuf )` — the merge against whichever baseline was
used, class baseline included. That last clause was the actual defect, and no amount of reasoning
from the read side would have produced it.

**How to apply, sharpened:** when a mechanism has two sides — read/write, encode/decode, send/receive
— reading one of them is half the research, and the half you skipped is where the surprise is. If an
experiment falsifies a hypothesis about a mechanism, that is the signal to go and read the other
side, not to form a second hypothesis. Related: [[a-bug-is-a-divergence-search-first]].

## The line you came for is usually below the one that changes its meaning

B276, and it is the sharpest instance so far. `AddBaseAnimatingInterpolatedVars` was printed to the
terminal TWICE in one session while answering "which variables are animation-latched":

```c
int flags = LATCH_ANIMATION_VAR;
if ( m_bClientSideAnimation )
    flags |= EXCLUDE_AUTO_INTERPOLATE;
AddVar( &m_flCycle, &m_iv_flCycle, flags, true );
```

The answer taken was the last line — cycle, pose parameters, encoded controller. The flag two lines
above was read past both times, **while this very memory was being cited elsewhere in the same
session**. It is the whole rule: a client-side-animated entity's cycle is never interpolated, and
`AddVar` enforces that by placing the variable past `m_nInterpolatedEntries`, the bound
`Interp_Interpolate` loops to. This project had been interpolating it for years; a viewmodel stopped
animating and the owner found it, not a test.

**Two things generalise.**

- **A flag being SET is a different fact from a flag existing.** Reading a header of `#define`s
  teaches nothing; `|= FOO` on the line above your answer changes what your answer means.
- **The signal was in what was READ, not in what was written.** The flag never reached a comment, a
  commit or a diff, so no review of the change could have caught it — and that is why
  `.claude/hooks/flag-unread.ps1` watches tool OUTPUT, firing once per flag per session on a flag
  that is composed or tested rather than merely named.
