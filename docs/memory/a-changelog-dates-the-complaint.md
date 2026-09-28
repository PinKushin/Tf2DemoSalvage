---
name: a-changelog-dates-the-complaint
description: "Valve's notes date their build exactly; only a note describing a REPAIR lags the thing it describes"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 9b3a8b35-1dc8-47b0-a320-73b01288f10c
  modified: 2026-08-11T17:19:18.596Z
---

TF2's 15 Nov 2007 note — *"Added backward compatibility code to allow demos recorded with protocol
12 to continue to be playable under protocol version 13"* — was misread as dating protocol 13
exactly. It describes a repair, not the ship date: protocol 13 had to ship, a player had to hit the
break, and someone had to report it first. Owner: *"most of the changelogs are done on the day of
update with valve and tf2"* — so an **"Added X"** note dates X exactly (the build shipping is the
event), but **"Fixed X" / "Added compatibility for X"** only upper-bounds X.

**How to apply:** ask whether a note announces or responds. Announcements date exactly — TF2's user
message table is dated from feature announcements (`RDTeamPointsChanged` → Robot Destruction, 8 July
2014), resolving the protocol-24 name-table ambiguity in RISKS B29. Repairs bound from above only.
Code can also ship dark before the feature using it is announced, so prefer evidence needing no date
at all — a late id's presence in a demo proves the late table regardless.

Related: [[era-axis-is-measured]], [[nothing-is-closed]], [[research-before-code]].
