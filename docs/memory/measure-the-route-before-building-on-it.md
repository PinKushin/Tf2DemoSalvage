---
name: measure-the-route-before-building-on-it
description: "A planned data route is a guess until measured. \"leaf → leaffaces → dispinfo\" reaches none of cp_badlands' 1191 displacement faces, and one query said so before any code was written."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-09T03:53:22.111Z
---

**Before building on a route through the data, measure that the route arrives** — one query costs
minutes; discovering it from a symptom costs a rewrite.

A collision plan's step ("leaf → `LUMP_LEAFFACES` → faces → `dispinfo`") reaches ZERO of
`cp_badlands`' 1,191 displacement faces — a displacement's base quad isn't its terrain, so the
compiler files it under no leaf at all; narrowing must be by BOUNDS. Had this been discovered as a
symptom ("terrain collision does nothing"), it would look exactly like a wrong primitive.

**The measurement needs a control** — zero displacement faces reached is also what a wrong offset
produces. The same walk reached 12,654 flat faces, and 13,845 − 1,191 = 12,654 exactly, confirming
it's the format, not the reader.

**How to apply:** when a plan says "A names B", write the query counting how many A actually name a
B, on real data, first. Include the negative class as a control. Keep the measurement as a permanent
test (`LeafDisplacementReachTests`) so nobody re-attempts the route.

**Same session, other side:** two test premises were wrong about the MAP, not the code — a dropped
box stopping mid-terrain, a brush trace correctly reporting `startsolid` inside carved geometry. Both
times the code was right and the prediction was a guess about unlooked-at geometry.

Related: [[nothing-is-closed]], [[a-filed-design-choice-may-not-be-one]],
[[instrument-bugs-outnumber-decoder-bugs]].

---

## `a-schema-key-nobody-reads-is-a-lead` — 747 on the left, zero on the right

**The denominator method works on the game's shipped DATA, cheaper than on its code.** Take a key
the game's own files declare, count it, grep the repo for it:
```
grep -c '"player_bodygroups"' items_game.txt      # 747
grep -rn "player_bodygroups" --include=*.cs .     # nothing
```
That pairing found B352: cosmetics never removed the body parts they replace — every hat sat on hair
it's modelled to cover, twelve players a frame, green suite.

**Cheaper than the engine-method sweep** ([[parity-is-the-search-not-the-defence]]) — the denominator
is a file, no citation matching, and the count states the stakes before a line is read.

**Get the key name from the FILE, never the C++ accessor** — `GetWorldmodelBodygroupOverride`
suggested a wrong key name that returned zero and nearly filed an implemented feature as absent; the
schema's real spelling returned 747.

**Check whether the mechanism can fire at all before filing a gap** — 102 items declaring another
attribute were unreachable because the feature needs a subscribed Steam inventory a spectating live
client also lacks. A precondition check converts a plausible defect into a settled question.
