---
name: bone-merge-sends-no-position
description: "Cosmetics and carried weapons send no origin, no model index and no moveparent — EF_BONEMERGE means they take the owner's bone matrices by name."
metadata: 
  node_type: memory
  type: project
  originSessionId: 9b3a8b35-1dc8-47b0-a320-73b01288f10c
  modified: 2026-08-14T18:11:08.966Z
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

**How to apply — and which field says so depends on what the entity is.** A `CTFWearable` sends
`moveparent` (the WIRE name; the member is `m_hMoveParent`, declared with `SENDINFO_NAME`) and no
`m_fEffects` at all. A carried `CTFRocketLauncher` sends `m_fEffects` with `EF_BONEMERGE` and no
parent. Either rule alone covers half the problem while looking complete, because the half it misses
simply does not draw.

**Ownership is not attachment.** A syringe knows which medic fired it through the same
`m_hOwnerEntity`; treating that as attachment claimed 220 syringe projectiles as worn items. Read
the owner handle only once `EF_BONEMERGE` has said the entity is merged.

Handles are not entity indices: index is the low `MAX_EDICT_BITS` (11) bits, and
`INVALID_NETWORKED_EHANDLE_VALUE` must be tested against the WHOLE value first — its low 11 bits are
2047, an ordinary-looking slot (`client/recvproxy.cpp:90`).

The merge itself is what `StudioBones.Remap` already does. Filed as B63; account in
`docs/findings/22-bone-merged-attachments.md`.

Model resolution is a second, separate gap: see [[negative-model-indices-are-dynamic]].

Related: [[read-the-encoder-not-the-decoder]] — the encoder states that the zero is deliberate,
which no amount of staring at absent fields would have.
