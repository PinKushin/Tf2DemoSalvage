---
name: a-schema-key-nobody-reads-is-a-lead
description: Diff the keys the game's data files declare against the keys this repo reads; a large count on the left and zero on the right is a whole unimplemented mechanism.
metadata:
  type: project
---

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
[[shipped-data-settles-what-closed-code-cannot]].

**Get the key name from the FILE, never from the C++ accessor.** `GetWorldmodelBodygroupOverride`
suggested `use_model_bodygroup_override`, which returned zero and nearly filed an implemented-looking
feature as absent; the schema spells it `wm_bodygroup_override`. The control that caught it was
`player_bodygroups` returning 747 — [[an-empty-search-needs-a-control]] applies to shipped data
exactly as it does to a grep over source.

**And check whether the mechanism can fire at all before filing a gap.** The same session found 102
items declaring `additional_hidden_bodygroups` and none of them reachable: the style arm needs a
subscribed Steam inventory, which a spectating live client also lacks. A precondition check costs one
call chain and converts a plausible defect into a settled question — see
[[a-filed-design-choice-may-not-be-one]].
