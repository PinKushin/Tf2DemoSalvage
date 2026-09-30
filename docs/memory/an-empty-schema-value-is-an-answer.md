---
name: an-empty-schema-value-is-an-answer
description: items_game.txt's prefab merge writes an item's own "" over its prefab's value; ItemSchema's prefab search skips empties unless a reader opts in.
metadata:
  type: project
---

`MergeDefinitionPrefab` applies the prefabs, then `RecursiveInheritKeyValues` SETS every key the item itself
declares over them, an empty string included (`econ_item_schema.cpp:2909`, `:2967`). The Half-Zatoichi's
`"anim_slot" ""` therefore hides `weapon_sword`'s `item1`: the engine reads -1 and a soldier's katana stays
melee. `ItemSchema.Search` treats "" as absent and walks on into the prefab. B105 opted `anim_slot` in
(`emptyAnswers: true`; empty scalar values kept at parse).

**Why:** the wrong answer is the prefab's value — plausible, and nothing errors.
**How to apply:** before trusting an inherited key, count `"<key>" ""` over a prefab in items_game.txt (18
`model_player` today, unmeasured). A reader of such a key needs `emptyAnswers: true` and a test row with the
empty override. See [[a-default-is-not-a-constant]].
