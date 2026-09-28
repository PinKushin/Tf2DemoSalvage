---
name: negative-model-indices-are-dynamic
description: "A negative m_nModelIndex is a dynamic model; the EVEN ones are networked in the DynamicModels string table, and that is where every cosmetic lives."
metadata: 
  node_type: memory
  type: project
  originSessionId: 9b3a8b35-1dc8-47b0-a320-73b01288f10c
  modified: 2026-08-14T18:10:46.470Z
---

**`m_nModelIndex` is a SIGNED 13-bit field; negative means dynamic model, half of which are in the
demo.** `SendPropModelIndex` is signed (`dt_send.h:715`); dynamic index is `-2 - index`
(`ivmodelinfo.h:90`): **odd** is client-only, genuinely absent from a demo; **even** is networked as
entry `(dynamic index) >> 1` of the `DynamicModels` string table.

**Why:** a prior doc comment reasoned the opposite — "a negative index is client-only precache,
absent from a demo of someone else's session" — true of the odd half, false of the even half, which
is where EVERY cosmetic in every modern demo lives. Measured on cp_process: 35 of 36 live
`CTFWearable` entities carry a negative EVEN index, all present in `DynamicModels`. Players drew
bare-headed while ordinary props resolved fine.

**How to apply:** the table name is in no published header (engine-side) — get it from the demo's own
string table list. Never halve odd indices as a fallback — it lands on a real entry of the networked
table and draws a confidently wrong model.

Related: [[nothing-is-closed]], [[fallbacks-do-not-make-guesses-safe]].
