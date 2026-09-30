---
name: a-weapons-world-model-is-its-items
description: The client rebuilds a TF weapon's third-person model from its valid item over the networked index; a wearable keeps the networked one.
metadata:
  type: project
---

`C_TFWeaponBase::GetWorldModelIndex` caches `GetModelIndex( GetWorldModel() )` over `m_iWorldModelIndex`
(`tf_weaponbase.cpp:3597-3607`). For a valid item `GetWorldModel` = `model_world` if declared, else
`GetPlayerDisplayModel` verbatim, `""` included (`:681-701`); only an invalid item reaches the script. So no wire
index is what a third-person weapon draws, and an item answering `""` draws NOTHING (fists 5/195, four spellbooks).
`C_TFWearable::GetWorldModelIndex` is the opposite: the networked index (`tf_item_wearable.cpp:453-509`).

**Why:** keeping the wire for a weapon whose item named "" resolved it to `m_nModelIndex`, the carrier's first-person
hands (B105, 2026-09-30). The one corpus case was holstered, so `WeaponVisibility` hid it: resolved is not shown.
**How to apply:** `WeaponPropModels.Resolve` tells weapon from wearable by `WeaponState`. Unported, same path: an
item with NO `model_player` (Gunslinger 142, B.A.S.E. Jumper 1101) and `model_world` (4 weapons) — RISKS B105.
See [[an-empty-schema-value-is-an-answer]], [[the-client-builds-what-the-demo-omits]].
