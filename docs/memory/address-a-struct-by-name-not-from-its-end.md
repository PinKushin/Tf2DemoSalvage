---
name: address-a-struct-by-name-not-from-its-end
description: `contents.Length - 4` was right until a float4 was appended; the material constant buffer has now bitten four times, and the fix is a named offset plus a guard that nothing has moved past it.
metadata:
  type: project
---

**A field addressed from the END of a buffer is correct exactly until something is appended after
it.** Fourth occurrence in this project's material constant buffer, 2026-09-04.

The per-batch category colour was written straight into the mapped buffer:

```csharp
target[contents.Length - 4] = colour.Red;
```

True while `categoryColour` was the shader struct's last float4. Appending `tintControl` sent the
category colour into the tint controls instead — whose `x` is read as `$blendtintbybasealpha`, so
every model took the tint branch against a garbage mask and **drew pure white**.

## The three earlier ones, same buffer

- A buffer created 160 bytes wide against a declared 192. It WORKED, because the driver tolerated
  the out-of-bounds read and write.
- `categoryColour` added to two of the three arrays that feed the buffer, because a replace-all
  matched two. The owner saw it immediately — *"the colors are kinda doing a disco now"*.
- A copy sized from a hardcoded sixteen floats after the struct grew by five float4s.

Each was fixed by correcting that instance. Fixing the instance does not fix the class.

## What actually stops it

- **Name the offset**, derived from the struct rather than from an array's length.
- **Guard it**: `CategoryColourRed + 4 != NoDetail.Length - 4` throws naming both numbers. The
  pre-existing length check catches an array that grew without the struct; this catches the struct
  growing without the offsets, which had no check at all.
- **Verify the guard by moving the constant** and watching it throw. A guard that has never fired is
  a guard nobody has read.

## And the tests that caught it were about something else

Two REFLECTION pixel tests went red. Nothing in the paint work's own suite could have — every one of
its assertions sits upstream of the buffer, and all of them stayed green.

That is the argument for keeping tests that measure a whole DRAW rather than only the value under
construction: a shared resource's corruption appears in whatever else reads it, never in the feature
that corrupted it.

Related: [[a-pass-must-establish-its-own-state]] — the same shape one layer up.
