---
name: a-synthetic-packet-without-serverinfo-is-protocol-0
description: SyntheticDemo.Packet with no ServerInfo writes protocol 0; events decode 9 bits off and vanish. Use PacketAfter.
metadata:
  type: project
---

`SyntheticDemo.Packet` without a ServerInfo first writes at protocol 0. Event packets then decode 9 bits off and are
DROPPED silently: a timeline test's events never arrive, and the test can pass on nothing. Found 2026-09-29 (B112,
bb646e33). **Use `PacketAfter`** (after a ServerInfo).

**Why:** a dropped event looks like "no event happened", not an error — see [[ask-whether-the-data-arrived]].
**How to apply:** any synthetic demo test asserting on events: build with `PacketAfter`; add a control that some event
DID arrive ([[instrument-bugs-outnumber-decoder-bugs]]).

**Real demos too, whenever a reader starts late.** `CorpusEntityRoundTripTests` read from `dem_datatables` on, so its
state never saw the first signon's ServerInfo: protocol-24 temp entities read at the 17-bit width, 10,642 cascade
snapshots threw into a silent `continue`, and B443's "removal list" mismatch was that (decode census, 2026-09-30). One
state, from the first command, as the trace writer and the timeline read.
