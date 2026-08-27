---
name: replace-all-is-a-claim-about-every-site
description: "A replace-all reports success for matching SOME sites; when several places must change together, count them."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-08-27T19:24:02.458Z
---

**A replace-all edit says "I changed every occurrence of this PATTERN", which is not the same as
"I changed every place that needed changing".** It reports success either way, and the gap is
invisible.

Measured 2026-08-27 adding a `float4` to the shader's `Material` struct. Three arrays had to grow
together — `NoDetail`, which sizes the constant buffer, and both branches of the per-material array.
The pattern ended `]);`. Two sites end that way; the third ends with a bare `]` because it is the
first arm of a ternary. **Two of three grew, and the tool said all occurrences were replaced.**

The result was a 64-float array copied into a 68-float buffer. `Map.WriteDiscard` renames the
allocation each time, so the unwritten tail was different every frame: the whole scene flashing
between two colours, and a write landing four floats early on unrelated constants. The owner saw it
in seconds — *"the colors are kinda doing a disco now"* — and their second remark was the diagnosis:
*"it actually looks like it might be trying to do more than one debug view at once"*, which is what
a garbage `float4` read as flags looks like.

**How to apply:** when a change requires N places to move together, establish N first and verify N
afterwards. Count the sites, or — better — make the disagreement impossible to ship: this ended with
`SetMaterial` throwing when an array's length disagrees with the shader struct's, naming both
numbers. A comparison per batch is nothing against a corruption that only some drivers punish, and
[[padding-is-not-zero]] is the same family — memory you did not write holds what was there before.

**This was the third instance of the same trap in one file.** A comment recording the previous two
did not prevent the third; a check would have. See [[one-place-or-it-drifts]] — and note that a
comment is not "one place", it is a description of one.
