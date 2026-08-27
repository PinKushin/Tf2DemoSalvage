---
name: negative-model-indices-are-dynamic
description: "A negative m_nModelIndex is a dynamic model; the EVEN ones are networked in the DynamicModels string table, and that is where every cosmetic lives."
metadata: 
  node_type: memory
  type: project
  originSessionId: 9b3a8b35-1dc8-47b0-a320-73b01288f10c
  modified: 2026-08-14T18:10:46.470Z
---

**`m_nModelIndex` is a SIGNED 13-bit field and a negative value is a dynamic model, half of which
are in the demo.** `SendPropModelIndex` is `SendPropInt(..., SP_MODEL_INDEX_BITS, 0)` — flags 0,
signed (`public/dt_send.h:715`). `ivmodelinfo.h:90` gives the rule: dynamic index is `-2 - index`;
**odd** is client-only and genuinely absent from a demo, **even** is networked and is entry
`(dynamic index) >> 1` of the **`DynamicModels`** string table.

**Why:** this project had the opposite conclusion written down in a doc comment with reasoning —
"a negative index is a model the recording client precached for itself, so a demo of someone else's
session carries no entry for it". True of the odd half, false of the even half, and the even half is
where **every cosmetic in every modern demo** lives. Measured on cp_process: 35 of 36 live
`CTFWearable` entities carry a negative index, all even, all present in `DynamicModels`. Players drew
bare-headed while every ordinary prop resolved fine, so it read as "cosmetics are not recorded".

**How to apply:** the table name is in no published header — it is engine side. Get it from the demo,
which lists its own string tables (`decalprecache DynamicModels EffectDispatch instancebaseline
modelprecache ParticleEffectNames Scenes ...`). Never halve the odd ones as a fallback: it lands on a
real entry of the networked table and draws a confidently wrong model.

Related: [[nothing-is-closed]], and
[[fallbacks-do-not-make-guesses-safe]] — the null for the odd half is the right answer, not a gap.
