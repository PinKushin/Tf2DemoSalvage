---
name: layer2-is-a-dependency-chain
description: Network messages have no length prefix, so decoding is strictly ordered — implement whatever currently blocks the stream, not whatever seems most useful
metadata:
  type: project
---

**Network messages carry no length prefix.** The next message begins wherever the previous
one's body ended, so there is no skip: an undecodable message makes everything after it in
that packet unreachable. Established 2026-08-07 while building layer 2.

The practical consequence governs the whole work order. **Implement whatever is currently
blocking the stream, not whatever looks most valuable.** Frequency counts are misleading —
`svc_ServerInfo` appears once per demo and gated the entire signon stream, including the
entity schema.

**Two exceptions**, both worth knowing because they behave differently: game events and string
tables carry an explicit bit length. They can be stepped over even when their contents cannot
be read, so implementing their *framing* alone unlocks whatever follows.

## The signon chain, as actually measured

The first signon command is ~110–130 KB in every corpus demo. Progress on it:

| After implementing | Messages read | Stops at |
|---|---|---|
| `net_Tick` only | 0 | `ServerInfo` |
| `ServerInfo`, `Print`, `StringCmd`, `SetConVar` | 2 | `CreateStringTable` |
| string tables | ~20 | `ClassInfo` |
| `ClassInfo` | 23–24 | `SignonState` |

**Signon ordering differs by demo kind**, which is not obvious and cost a debugging cycle:
SourceTV demos open with `svc_ServerInfo`, point-of-view demos with `svc_Print`. ServerInfo
was unreachable in the POV demo until the trivial `svc_Print` existed.

## Where it stands and what is next

Regular gameplay packets stop at **`svc_PacketEntities`** in roughly 90% of cases. That is not
another message — it is layer 3, and it needs three things that do not exist yet:

1. **`dem_datatables` parsing.** The SendTable schema is a *demo command*, not a net message,
   so nothing in layer 2 touches it. This is the embedded schema the whole project premise
   rests on.
2. **Property-list flattening** — base tables merged, `SPROP_EXCLUDE` applied, then
   `SPROP_CHANGES_OFTEN` reordering. Entity deltas index into that flattened list, so the
   ordering *is* the contract. See `RISKS.md` B4.
3. **Delta decoding**, including the `SPROP_COORD_MP` variants the Source SDK documents and
   VDC does not.

Each is comparable in size to all of layer 2. The failure mode is also worse: wrong flattening
order yields plausible numbers rather than an error, so build the cross-parser differential
harness alongside it rather than after. See [[tests-before-codecs]].
