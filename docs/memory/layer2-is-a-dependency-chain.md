---
name: layer2-is-a-dependency-chain
description: Network messages have no length prefix, so decoding is strictly ordered — implement whatever currently blocks the stream, not whatever seems most useful
metadata:
  type: project
---

**Network messages carry no length prefix** — the next message begins wherever the previous body
ended, so an undecodable message makes everything after it in that packet unreachable. Established
2026-08-07.

**Consequence: implement whatever is currently blocking the stream, not whatever looks most
valuable.** Frequency counts mislead — `svc_ServerInfo` appears once per demo and gated the entire
signon stream. **Two exceptions:** game events and string tables carry an explicit bit length, so
their framing alone can be stepped over even unread.

## The signon chain, as actually measured

| After implementing | Messages read | Stops at |
|---|---|---|
| `net_Tick` only | 0 | `ServerInfo` |
| `ServerInfo`, `Print`, `StringCmd`, `SetConVar` | 2 | `CreateStringTable` |
| string tables | ~20 | `ClassInfo` |
| `ClassInfo` | 23-24 | `SignonState` |

**Signon order differs by demo kind** — SourceTV opens with `svc_ServerInfo`, POV with `svc_Print`;
ServerInfo was unreachable in the POV demo until the trivial `svc_Print` existed.

## Where it stands and what is next

Regular gameplay stops at **`svc_PacketEntities`** ~90% of the time — layer 3, needing:
1. `dem_datatables` parsing (a demo COMMAND, not a net message — the embedded schema the project
   premise rests on).
2. Property-list flattening (base tables merged, `SPROP_EXCLUDE`, then `SPROP_CHANGES_OFTEN`
   reordering — the ordering IS the contract, RISKS B4).
3. Delta decoding, including `SPROP_COORD_MP` variants the SDK documents and VDC doesn't.

Wrong flattening order yields plausible numbers, not errors — build the cross-parser differential
harness alongside it, not after. See [[tests-before-codecs]].
