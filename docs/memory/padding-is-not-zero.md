---
name: padding-is-not-zero
description: Bit-padding to a byte boundary carries uninitialised writer stack, so it must be read rather than recomputed.
metadata:
  type: project
---

Any message whose fields do not end on a byte boundary has padding bits, and in Source those bits
are **not zero**. `bf_write` composes its partial tail dword with a read-modify-write that preserves
bits outside the mask, and callers hand it uninitialised stack buffers — `CDemoRecorder::RecordUserInput`
declares a plain `byte buffer[256]` and never clears it. Whatever was in that slot ends up in the file.

Measured on `dem_usercmd`, 2026-08-11: 385,236 commands, 99.8% ending three bits short of a byte,
those bits taking every value from 0 to 7. Narrower in some demos than others (the 2013 recording
only ever emits 0 or 7), which is the signature of leftovers from a previous longer write.

**Why:** it makes a byte-exact rewrite impossible from decoded values alone, and it fails in the
worst available way — every field still decodes correctly, so nothing looks wrong until the rebuilt
file is compared byte for byte. Same family as [[round-trip-needs-the-encoding-shape]]: information
that is in the file but not in the values.

**How to apply:** when adding a codec for any bit-packed payload, read the residual bits into the
record and write them back, rather than letting the writer zero-pad. Put a round-trip property over
the whole corpus on it immediately — that is what caught this on the first run, and no amount of
field-level assertion would have. If a payload genuinely always pads to zero, that is a measurement
to make, not an assumption to start from.

Related: [[fixtures-are-the-weak-point]], [[read-the-encoder-not-the-decoder]].
