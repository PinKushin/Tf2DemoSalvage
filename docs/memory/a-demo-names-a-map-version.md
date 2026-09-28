---
name: a-demo-names-a-map-version
description: A demo's map name does not identify the map — a mismatched .bsp produces defects that look exactly like rendering bugs, and the field that identifies a version is MapHash on every era, never MapCrc.
metadata:
  type: project
---

`cp_badlands` in 2017 is not `cp_badlands` in 2026. The viewer loads the map by NAME from the current
TF2 install, so an old demo renders against geometry it was never recorded on, and every consequence
looks like a rendering defect.

Measured 2026-08-27/28: three "regressions" reported against a 2017 badlands demo (grey roller doors,
players appearing out of nowhere, flickering doors) were **all** map-version mismatch, not the code
under suspicion. Owner: *"the bugs are probably from a mismatched map version… not a regression, just
something to document and fix."*

The check needs nothing invented: `CRC_MapFile` (`utils/common/bsplib.cpp:3774`) is CRC32 over every
lump except `LUMP_ENTITIES` (excluded so a server editing entities still matches clients), over raw
on-disk bytes. The expected value arrives on the wire as `svc_ServerInfo`'s `mapCRC`
(`ServerInfoMessage.MapCrc`), decoded but never compared to anything.

**How to apply:** on any visual oddity from a non-f12 demo, first ask whether the map matches, not
what the renderer did — see [[the-f12-demo-is-the-parity-reference]]. Until the CRC check exists
(D113), reproduce on f12 before calling anything a regression.

---

## `map-checksum-is-maphash-not-mapcrc` — the version check is MapHash on every era

**`DemoTimeline.MapCrc` is not the version field.** Finding 43 (`docs/findings/43-what-identifies-a-map.md`)
showed `svc_ServerInfo` carries two fields pre-2013; this project named the 32-bit one `MapCrc` and
the four-byte one `MapHash`, then chased `MapCrc` for a day. **The map checksum is `MapHash`.**
`MapCrc` (`0x534EEB7C` on the 2007 granary specimen) is unidentified and matches nothing.

One field answers every era: `MapHash` is 4 bytes pre-2013, 16 (MD5) from 2013 on, and
`BspMapChecksum.Matches(file, recorded)` already disambiguates by length — feed it `MapHash`
regardless of era. `PeriodMapChecksumTests` and `BspMapChecksumConformanceTests` are the confirmed
uses.

**Why it matters:** the natural reading is backwards — "Crc" sounds like the real checksum. Building
D162's version check on `MapCrc` would have shipped a check that always disagrees, on every pre-2013
demo, undetected unless the test used the same wrong field. Caught by reading `docs/findings/` before
writing code — [[valve-parity-is-the-first-principle]].
