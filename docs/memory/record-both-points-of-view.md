---
name: record-both-points-of-view
description: "Record every era specimen as a POV and SourceTV pair of the same session — the pairing is what turns \"looks like a parser bug\" into a proven writer bug"
metadata: 
  node_type: memory
  type: project
  originSessionId: 9b3a8b35-1dc8-47b0-a320-73b01288f10c
  modified: 2026-08-10T17:13:07.632Z
---

**Record both, always.** POV and SourceTV of the SAME session are different writers over identical
events — a difference between them is a difference in the writer, a control no single file provides.

Paid twice same day: a 64KiB schema cap on a protocol-11 SourceTV demo read as a parser bug alone; the
POV of the same session carries 85,063 bytes and parses, proving SourceTV cut it (confirmed on a
second map). A missing string-table command seen first on POV could have been a quirk; the SourceTV
pair lacks it too, proving it's a property of the era, not the mode.

## The pair is also two DIFFERENT datasets, not just two writers

**TF2 splits a player's state across network tables by AUDIENCE** — a "local" table sent only to the
player it describes, a shared table sent to everyone else. Übercharge, disguise state, and cloak
timing all have this shape; the medigun's direct send is literally commented out in the always-sent
table.

**Rule: before concluding a field is missing, establish which table it lives in and whose recording
this is.** "Absent from an STV demo" is documented behaviour for anything local, not a decode
failure — and a field from POV may have different precision than the same field from STV.

Pinned by `LocalTableConformanceTests` and `UnimplementedGameplayEntityConformanceTests`.

## What differs by mode, structurally

POV carries `dem_usercmd`/`dem_consolecmd`; SourceTV carries neither — most of the size difference.
SourceTV records as a virtual client (no account id) — **prefer SourceTV for anything going into a
public repository.**

## Recording costs nothing extra

No dedicated server needed — client packs ship `server.dll`:
```
tv_enable 1        // BEFORE the map loads
map <mapname>
tv_record stv<year>
```

## The limit of a local pair

A listen server is effectively LAN, so POV/SourceTV should agree almost exactly — that says the
parser is consistent across modes, and nothing about real internet-relayed STV demos (which carry
delay and their own interpolation). Do not cite a local pair as evidence about that.

See `docs/RECORDING_CHECKLIST.md` for the actual recording procedure.
