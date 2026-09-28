---
name: padding-is-not-zero
description: "Bit-padding to a byte boundary carries stale bits of the previous write, so it must be read rather than recomputed."
metadata: 
  node_type: memory
  type: project
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-09T03:53:59.858Z
---

Any message not ending on a byte boundary has padding bits, and in Source those bits are NOT zero —
`bf_write` composes its tail dword preserving bits outside the mask, and never clears the buffer
first. So bits a write doesn't cover keep whatever was already there.

Measured on `dem_usercmd`: 385,236 commands, 99.8% ending 3 bits short of a byte, and **75.3% of
non-zero pads are bit-for-bit what the previous command wrote at the same offsets.**

**Why:** makes a byte-exact rewrite impossible from decoded values alone, and fails silently — every
field still decodes correctly, nothing looks wrong until compared byte for byte with the original.
Same family as [[round-trip-needs-the-encoding-shape]].

**The correction matters more than the finding** — first written up as a leak/uninitialised memory,
asserted rather than tested, though the SAME paragraph already noted the pattern looked like buffer
reuse. Separating condition: buffer reuse predicts the previous command's bits at those offsets;
foreign memory doesn't. Nothing escapes the file that the file didn't already contain.

**How to apply:** when adding a codec for a bit-packed payload, read residual bits into the record and
write them back rather than zero-padding; put a corpus-wide round-trip property on it immediately —
that's what caught this. When explaining where odd bytes come from, name competing mechanisms and
find the one measurement that separates them before writing anything down. See
[[fallbacks-do-not-make-guesses-safe]].

Related: [[fixtures-are-the-weak-point]], [[read-the-encoder-not-the-decoder]].
