---
name: bone-merge-sends-no-position
description: "Cosmetics and carried weapons send no origin, no model index and no moveparent — EF_BONEMERGE means they take the owner's bone matrices by name."
metadata: 
  node_type: memory
  type: project
  originSessionId: 9b3a8b35-1dc8-47b0-a320-73b01288f10c
  modified: 2026-08-14T17:54:43.380Z
---

**An entity attached to a player carries no position on the wire, and that is correct rather than
missing.** `CTFWearable` (hats, badges) and carried weapons (`CTFRocketLauncher`, `CTFShovel`) all
decode with `Origin()` null. One carried weapon's complete property set is `m_hOuter, m_nSequence,
m_iState, m_fEffects, m_flSimulationTime, m_flNextPrimaryAttack, m_flNextSecondaryAttack,
m_iBuildState`.

**Why:** `CBaseCombatWeapon::Equip` calls `FollowEntity`, which sets `EF_BONEMERGE` (`0x001`,
`public/const.h:284`) and then explicitly zeroes local origin and angles
(`shared/baseentity_shared.cpp:2360`). A merged entity has no transform of its own — the client
matches the child model's bones to the parent's **by name** and uses the parent's matrices. Sending
an origin would be sending zero.

**How to apply:** do not look for `m_hMoveParent`/`moveparent` on these — it is absent, and chasing
it cost a round of work. The owner is `m_hOwnerEntity`. The merge itself is what `StudioBones.Remap`
already does. What is genuinely missing is model resolution for the ones that send no
`m_nModelIndex` (41 of the origin-less entities do send it) and picking which carried weapon is
active. Filed as B63; account in `docs/findings/22-bone-merged-attachments.md`.

Related: [[read-the-encoder-not-the-decoder]] — the encoder states that the zero is deliberate,
which no amount of staring at absent fields would have.
