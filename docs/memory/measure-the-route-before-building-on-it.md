---
name: measure-the-route-before-building-on-it
description: "A planned data route is a guess until measured. \"leaf → leaffaces → dispinfo\" reaches none of cp_badlands' 1191 displacement faces, and one query said so before any code was written."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-09T03:53:22.111Z
---

**Before building on a route through the data, measure that the route arrives.** One query, first,
costs minutes; discovering it from a symptom costs a rewrite.

**Why:** the displacement-collision plan's step 2 was *"leaf → `LUMP_LEAFFACES` → faces →
`dispinfo`"* — written from the format documentation and entirely reasonable. Measured on
`cp_badlands` it reaches **zero** of the 1191 displacement faces. A displacement's base quad is not
its terrain, so the compiler files it under no leaf at all; the narrowing has to be by BOUNDS.

Had the narrowing been built first, the symptom would have been "terrain collision does nothing",
which looks exactly like a wrong primitive — the expensive place to go looking.

**The measurement needs a control or it proves nothing about the format.** Zero displacement faces
reached is also what a wrong `dleaf_t` offset produces. The same walk reached **12,654 flat faces**,
and 13845 − 1191 = 12654 exactly, so every flat face is reachable and no displacement face is. That
is the format, not the reader. See [[instrument-bugs-outnumber-decoder-bugs]].

**How to apply:**

- When a plan says "A names B", write the query that counts how many A actually name a B, and run it
  on real data before writing anything else.
- Include the negative class as the control — here, the faces that are NOT displacements.
- Keep the measurement as a test rather than deleting it. `LeafDisplacementReachTests` asserts the
  zero, so nobody re-attempts the route, and it says so the day a map does put them in leaves.
- **A published tool is a source when the engine's own file is not.** `cmodel_disp.cpp` is not in the
  SDK; `vrad` building its own displacement list rather than using leaves was the hint that leaves
  were never the route.

**The same session, from the other side:** two test premises were wrong about the MAP rather than
about the code — a box dropped 512 units onto a vertex at z = 288 stops at 793, because the map
stacks terrain above terrain; and the space just above a displacement vertex is usually inside the
brush the terrain was carved from, so a brush trace correctly reports startsolid. Both times the
code was right and the prediction was a guess about geometry nobody had looked at. **When a
prediction about real data fails, ask whether the data is what you assumed before touching the
code.** [[nothing-is-closed]] is the same rule for inputs.

Related: [[nothing-is-closed]], [[a-filed-design-choice-may-not-be-one]],
[[instrument-bugs-outnumber-decoder-bugs]].

---

## `a-schema-key-nobody-reads-is-a-lead` — 747 on the left, zero on the right

**The denominator method works on the game's shipped DATA, not just on its code, and it is cheaper
there.** Take a key the game's own files declare, count it, then grep this repository for it:

```bash
grep -c '"player_bodygroups"' items_game.txt      # 747
grep -rn "player_bodygroups" --include=*.cs .     # nothing
```

That pairing — a large number on the left and zero on the right — found **B352** on 2026-09-05: a
player's cosmetics never removed the body parts they replace, so every hat sat on the hair it is
modelled to cover. Twelve players a frame, in every modern demo, with a green suite.

**Why it is cheaper than the engine-method sweep** ([[parity-is-the-search-not-the-defence]], and
the `parity <filter> <class>` probe): the denominator is a file rather than a class, no citation
matching is needed, and the count itself tells you how much is at stake. 747 items is a different
finding from 2 items, before a line is read.

**Where the answers were:** `items_game.txt`, `modevents.res`, VMTs, `.res` files — see
[[nothing-is-closed]].

**Get the key name from the FILE, never from the C++ accessor.** `GetWorldmodelBodygroupOverride`
suggested `use_model_bodygroup_override`, which returned zero and nearly filed an implemented-looking
feature as absent; the schema spells it `wm_bodygroup_override`. The control that caught it was
`player_bodygroups` returning 747 — [[instrument-bugs-outnumber-decoder-bugs]] applies to shipped data
exactly as it does to a grep over source.

**And check whether the mechanism can fire at all before filing a gap.** The same session found 102
items declaring `additional_hidden_bodygroups` and none of them reachable: the style arm needs a
subscribed Steam inventory, which a spectating live client also lacks. A precondition check costs one
call chain and converts a plausible defect into a settled question — see
[[a-filed-design-choice-may-not-be-one]].
