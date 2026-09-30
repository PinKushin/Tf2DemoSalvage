---
name: a-weapons-world-model-is-its-items
description: The client rebuilds a TF weapon's third-person model from its valid item over the networked index; a wearable keeps the networked one.
metadata:
  type: project
---

`C_TFWeaponBase::GetWorldModelIndex` caches `GetModelIndex( GetWorldModel() )` over `m_iWorldModelIndex`
(`tf_weaponbase.cpp:3597-3607`). For a valid item `GetWorldModel` = `model_world` if non-NULL ("" wins too), else
`GetPlayerDisplayModel` verbatim, `""` included (`:681-701`); only an invalid item reaches the script. So no wire
index is what a third-person weapon draws, and an item answering `""` draws NOTHING (fists 5/195, four spellbooks).
`C_TFWearable::GetWorldModelIndex` is the opposite: the networked index (`tf_item_wearable.cpp:453-509`).

**Why:** keeping the wire for a weapon whose item named "" resolved it to `m_nModelIndex`, the carrier's first-person
hands (B105, 2026-09-30). The one corpus case was holstered, so `WeaponVisibility` hid it: resolved is not shown.
**How to apply:** `WeaponPropModels.Resolve` tells weapon from wearable by `WeaponState`, and asks a weapon's
`model_world` (`WeaponModels.WorldDisplayModel`) first; first person stays `For` = `model_player` (`econ_entity.cpp:1167`).
Only the Hot Hand's `model_world` differs (`w_slapping_glove`): a pyro holds one in lcor `tf2-2026-pub-pov-clean`, 148
ticks from 5133, wire already `w_`, where the port drew `c_` until 2026-09-30. `C_TFWeaponBuilder` (stock
sapper) draws `objects.txt`'s `Playermodel` for its owner, its item otherwise — both `c_sapper.mdl`. A dropped weapon is
no econ entity: it draws the wire. Unported: an item with NO `model_player` (Gunslinger 142, Jumper 1101) — RISKS B105.
See [[an-empty-schema-value-is-an-answer]], [[the-client-builds-what-the-demo-omits]].
