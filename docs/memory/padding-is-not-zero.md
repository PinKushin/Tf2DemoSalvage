---
name: padding-is-not-zero
description: Bit-padding to a byte boundary carries stale bits of the previous write, so it must be read rather than recomputed.
metadata:
  type: project
---

Any message whose fields do not end on a byte boundary has padding bits, and in Source those bits
are **not zero**. `bf_write` composes its partial tail dword with `dword1 ^= (mask1 & (curData ^
dword1))`, which preserves every bit outside the mask, and `StartWriting` never clears the buffer.
So bits a write does not cover keep whatever was already there.

Measured on `dem_usercmd`, 2026-08-11: 385,236 commands, 99.8% ending three bits short of a byte,
those bits taking every value from 0 to 7. **150,606 of the 199,929 non-zero pads — 75.3% — are
bit-for-bit what the previous command wrote at the same absolute offsets.**

**Why:** it makes a byte-exact rewrite impossible from decoded values alone, and it fails in the
worst available way — every field still decodes correctly, so nothing looks wrong until the rebuilt
file is compared byte for byte. Same family as [[round-trip-needs-the-encoding-shape]]: information
that is in the file but not in the values.

**The correction is worth more than the finding.** The first write-up called this uninitialised
process memory and described it as a leak. Non-zero and varying is consistent with several
mechanisms, and the alarming one got asserted rather than tested — while a sentence in the same
paragraph already said the distributions looked like leftovers from a previous longer write. The
separating condition was cheap: buffer reuse predicts the previous command's bits at those offsets,
foreign memory does not. Nothing escapes the file that the file did not already contain.

**How to apply:** when adding a codec for any bit-packed payload, read the residual bits into the
record and write them back rather than letting the writer zero-pad, and put a corpus-wide
round-trip property on it immediately — that is what caught this on the first run. When explaining
*where* odd bytes come from, name the competing mechanisms and find the one measurement that
separates them before writing any of it down. See [[fallbacks-do-not-make-guesses-safe]].

Related: [[fixtures-are-the-weak-point]], [[read-the-encoder-not-the-decoder]],
[[two-recordings-of-one-value]].
