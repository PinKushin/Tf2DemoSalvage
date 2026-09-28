---
name: bone-merge-sends-no-position
description: "Cosmetics and carried weapons send no origin, no model index and no moveparent — EF_BONEMERGE means they take the owner's bone matrices by name."
metadata: 
  node_type: memory
  type: project
  originSessionId: 9b3a8b35-1dc8-47b0-a320-73b01288f10c
  modified: 2026-08-14T18:11:08.966Z
---

**An entity attached to a player carries no position on the wire, and that's correct, not missing.**
`CTFWearable` and carried weapons all decode with `Origin()` null; a carried weapon's complete
property set is `m_hOuter, m_nSequence, m_iState, m_fEffects, m_flSimulationTime,
m_flNextPrimaryAttack, m_flNextSecondaryAttack, m_iBuildState`.

**Why:** `CBaseCombatWeapon::Equip` calls `FollowEntity`, which sets `EF_BONEMERGE` (`0x001`,
`public/const.h:284`) and explicitly zeroes local origin/angles (`baseentity_shared.cpp:2360`). A
merged entity has no transform of its own — bones match the parent's by NAME using the parent's
matrices. Sending an origin would send zero.

**How to apply — the telling field differs by entity type:** a `CTFWearable` sends `moveparent`
(`m_hMoveParent` via `SENDINFO_NAME`) and no `m_fEffects`; a carried `CTFRocketLauncher` sends
`m_fEffects` with `EF_BONEMERGE` and no parent. Either rule alone looks complete while missing half.

**Ownership ≠ attachment** — a syringe's `m_hOwnerEntity` names which medic fired it, not that it's
worn; treating it as attachment claimed 220 syringe projectiles as worn items. Read the owner handle
only after `EF_BONEMERGE` says the entity is merged.

**Handles are not entity indices:** index is the low 11 bits (`MAX_EDICT_BITS`);
`INVALID_NETWORKED_EHANDLE_VALUE` must be tested against the whole value — its low 11 bits look like
an ordinary slot (2047, `recvproxy.cpp:90`).

Merge itself is `StudioBones.Remap`. Filed B63, `docs/findings/22-bone-merged-attachments.md`. Model
resolution is a separate gap: [[negative-model-indices-are-dynamic]].

Related: [[read-the-encoder-not-the-decoder]] — the encoder states the zero is deliberate.
