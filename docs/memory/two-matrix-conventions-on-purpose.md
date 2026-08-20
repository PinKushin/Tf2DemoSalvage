---
name: two-matrix-conventions-on-purpose
description: Bones stay in Valve's column-vector 3x4 and reach the shader raw; the model matrix is row-vector 4x4. Crossing is a real boundary, and it belongs in one place.
metadata:
  type: project
---

**This renderer speaks two matrix conventions deliberately, and `MatrixConvention` is the boundary.**

- **Valve's `matrix3x4_t` transforms a COLUMN vector.** Twelve floats, translation in column three.
  Bones and `mstudioattachment_t.local` are both this, and skinning uses them RAW: the shader does
  `dot(boneRows[row], float4(position, 1))`, which is that formula exactly. Nothing is converted for
  skinning and nothing should be.
- **The model matrix transforms a ROW vector.** Sixteen floats, translation in row three, declared
  `row_major float4x4`. `PropTransform.ToMatrix` already produces it.

So using a Valve transform AS a model matrix — an attachment point, say — is a transpose plus a
translation move. That is the cost of having two conventions, not a workaround for a wrong one.

**Why it is written down:** the owner pushed back with "if our matrix conventions are wrong, we
should fix those not transpose around them", which was the right challenge. Checking it found the
conventions sound and the real defect elsewhere: the conversion existed in **two places with two
pieces of code** and no statement anywhere of which layout was which. Two implementations of one
boundary is how they come to disagree, and a disagreement here produces a plausible placement rather
than an error.

**How to apply:** never transpose inline. Call `MatrixConvention.ToModelMatrix`, `.Concatenate`
(Valve's `ConcatTransforms`, kept in Valve's convention because that is the form both operands
arrive in), or `.Multiply`. **Test with a ROTATION** — a missing transpose is invisible on a pure
translation, and so is a reversed multiply order, so a test using only offsets passes against both
bugs ([[a-test-can-outlive-its-design]] on the wider point).
