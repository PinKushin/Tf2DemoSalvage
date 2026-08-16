---
name: record-both-points-of-view
description: "Record every era specimen as a POV and SourceTV pair of the same session — the pairing is what turns \"looks like a parser bug\" into a proven writer bug"
metadata: 
  node_type: memory
  type: project
  originSessionId: 9b3a8b35-1dc8-47b0-a320-73b01288f10c
  modified: 2026-08-10T17:13:07.632Z
---

**Record both, always.** POV and SourceTV of the *same session* are different writers over
identical events, so a difference between them is a difference in the writer — and that is a
control no single file provides.

It has already paid twice on the same day:

- **B24, the 64 KiB schema cap.** The protocol-11 SourceTV demo's `dem_datatables` is exactly
  65,536 bytes and ends mid-table. Alone that reads as a parser bug. The **POV of the same
  session carries 85,063 bytes and parses**, which proves the schema is genuinely larger and
  SourceTV cut it. Confirmed on a second map — cp_gravelpit also stops at exactly 2^16.
- **The missing `dem_stringtables` at protocol 14.** Seen first on a POV demo, where it could
  have been a quirk of that recording. The protocol-14 SourceTV demo lacks it too, so it is a
  property of the era rather than of the mode.

## The pair is also two DIFFERENT datasets, not just two writers

Added 2026-08-16, and it changes how a missing field should be read.

**TF2 splits a player's state across network tables by AUDIENCE.** A "local" table is sent only to
the player it describes; a shared table goes to everyone else. So a POV demo and a SourceTV demo of
the same instant genuinely do not contain the same fields, by design:

| Field | Local (the player's own client) | Shared (observers, SourceTV) |
|---|---|---|
| `m_flChargeLevel` (übercharge) | full precision, `SPROP_NOSCALE` | **12 bits over 0..100** |
| disguise | `m_nDesiredDisguiseTeam/Class` | `m_nDisguiseTeam/Class` only |
| cloak timing | `m_flStealthNoAttackExpire`, `m_flStealthNextChangeTime` | absent entirely |

The medigun is the sharpest case: the direct send in the always-sent table is **commented out**, so
there is no unconditional charge level at all — only one of the two sub-tables, chosen by audience.

**The rule that follows: before concluding a field is missing, establish which table it lives in and
whose recording this is.** "Absent from an STV demo" is documented behaviour for anything local, not
evidence of a decode failure — and, in the other direction, a field decoded from a POV demo may have
a *different precision* than the same field from STV, so the two are not interchangeable
measurements.

That is a refinement of the control described above rather than a contradiction of it. The pair is
still the control for writer behaviour; it is not a control for field presence.

Pinned by `LocalTableConformanceTests` and `UnimplementedGameplayEntityConformanceTests`.

## What differs by mode, structurally

POV carries `dem_usercmd` (one input record per tick) and `dem_consolecmd`; SourceTV carries
neither. That is most of the size difference: 0.18–0.23 MB/min for SourceTV on a listen server
against 0.34–0.92 for POV.

SourceTV also records as a **virtual client**, so its `userinfo` slot holds `BOT (SourceTV)` and
no account id. **Prefer SourceTV for anything going into a public repository.**

## Recording them costs nothing extra

No dedicated server and no period `srcds` binary is needed — the client packs ship
`tf/bin/server.dll` and the engine exports `tv_enable`, `tv_record`, `tv_maxclients`:

```
tv_enable 1        // BEFORE the map loads, or SourceTV never attaches
map <mapname>
tv_record stv<year>
```

## The limit of a local pair

A listen server is effectively LAN, so POV and SourceTV should agree almost exactly. **That says
the parser is consistent across modes and nothing about real STV demos** — an internet relay
carries delay and its own interpolation, and competitive players have long known its picture can
differ from what the player saw. Do not cite a local pair as evidence about that.

See `docs/RECORDING_CHECKLIST.md` for what to actually do while recording; the era specimens were
ad hoc before it, which is why voice had no coverage until 2007 and why the voice-slot mapping is
still open for want of two speakers in one demo.
