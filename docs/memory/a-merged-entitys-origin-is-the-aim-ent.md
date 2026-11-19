---
name: a-merged-entitys-origin-is-the-aim-ent
description: "A bone-merged entity's origin is GetAimEntOrigin: the parent's WorldSpaceCenter under EF_BONEMERGE_FASTCULL (every wearable and weapon), else the parent's origin. Unmerged bones build from it (B69)."
metadata:
  node_type: memory
  type: project
  modified: 2026-11-18T00:00:00.000Z
---

**Where a bone-merged item stands is not its parent's origin.** `CalcAbsolutePosition` sends an
`EF_BONEMERGE` entity to `MoveToAimEnt` (`c_baseentity.cpp:4387-4391`), and
`C_BaseAnimating::GetAimEntOrigin` (`c_baseanimating.cpp:5986-5999`) answers: with
`EF_BONEMERGE_FASTCULL` the parent's `WorldSpaceCenter()` and `GetRenderAngles()`; otherwise the first
merged bone aligned, or the parent's origin when nothing merged.

**Why it matters:** every bone the merge does not write is built from that origin. An item matching
none of its wearer's bones (the spellbook's lone `mvm`) sits at the middle of his hull, 41 up (31
ducked). Taking the parent's origin put it at his feet, and B69 was filed for years as an attachment
problem it never was.

**How to apply:** every wearable and combat weapon fast-culls (shared `Spawn`,
`econ_wearable.cpp:116`, `basecombatweapon_shared.cpp:229`), so read the flag wire-or-class like
`EF_BONEMERGE`. The rule lives in `ParentChain.AimEntOffset`; a parent needs `SceneProp.Collision` for a
centre. An `EF_BONEMERGE` entity never reaches `GetParentToWorldTransform`, so it never reads
`m_iParentAttachment` either.
