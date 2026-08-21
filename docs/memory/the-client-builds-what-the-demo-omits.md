---
name: the-client-builds-what-the-demo-omits
description: The first-person weapon model is a client-side entity, so no demo contains it; the item definition index plus items_game.txt is the bridge.
metadata:
  type: project
---

**A demo does not contain everything that is on screen.** The weapon you see in first person is
`C_ViewmodelAttachmentModel`, created with `InitializeAsClientEntity` (`econ_entity.cpp:1153`) — no
edict, no entity index, nothing networked. It is bone-merged onto the arms by the client at the
moment the weapon is drawn. Searching a demo for it finds nothing, and that absence is correct.

**What the demo does carry is enough to rebuild it.** `DT_ScriptCreatedItem.m_iItemDefinitionIndex`
names an item, and `items_game.txt` turns that into a model through `model_player`, inherited along
the `prefab` chain — stock weapons are four lines and a prefab, so reading only the definition
answers for almost nothing. **Twenty-two of fifty-six held weapons on z1800 send no index at all**,
where the fallback is the stock item for the weapon's class, matched on `baseitem` + `item_class`.
The two rules together resolved 56 of 56.

**Why it matters beyond weapons:** the same shape covers the HUD, tracers, muzzle flashes, and any
`CLIENTCLASS`-only effect. When something obviously visible in the game turns out to be absent from
the demo, the question is not "which field did we miss" but "does the client make this itself" — and
if it does, the demo will carry the INPUT to that construction rather than its result.

**How to apply:** before hunting a field, check whether the thing is created client-side. `grep` for
the class in `client/` with no matching `server/` definition, or for `InitializeAsClientEntity`.
Then find what the client reads to build it, and read the same thing. Shipped data files are usually
where that lands ([[shipped-data-is-a-source]]), and an absent networked value normally means the
default rather than "unknown" ([[sentinels-conflate-unknown-with-answer]]).
