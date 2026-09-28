---
name: address-a-struct-by-name-not-from-its-end
description: `contents.Length - 4` was right until a float4 was appended; the material constant buffer has now bitten four times, and the fix is a named offset plus a guard that nothing has moved past it.
metadata:
  type: project
---

**A field addressed from the END of a buffer is correct exactly until something is appended after
it.** Fourth occurrence in the material constant buffer, 2026-09-04. The per-batch category colour
was written `target[contents.Length - 4] = colour.Red;` — true while `categoryColour` was the shader
struct's last float4. Appending `tintControl` sent the category colour into the tint controls (`x` =
`$blendtintbybasealpha`), so every model took the tint branch against a garbage mask and **drew pure
white**.

Three earlier hits, same buffer: a buffer created 160 bytes wide against a declared 192 (worked — the
driver tolerated out-of-bounds); `categoryColour` added to two of three feeding arrays via a
replace-all match, disco colours; a copy sized from a hardcoded 16 floats after the struct grew by
five float4s. Fixing each instance didn't fix the class.

**What actually stops it:**
- Name the offset, derived from the struct rather than an array's length.
- Guard it: `CategoryColourRed + 4 != NoDetail.Length - 4` throws, naming both numbers.
- Verify the guard by moving the constant and watching it fire — an unfired guard is unread.

**The tests that caught it were about something else** — two reflection pixel tests went red; nothing
in the paint work's own suite could, since all its assertions sit upstream of the buffer. Argument for
keeping tests that measure a whole DRAW, not just the value under construction — a shared resource's
corruption shows up wherever else reads it.

Related: [[a-pass-must-establish-its-own-state]] — same shape, one layer up.
