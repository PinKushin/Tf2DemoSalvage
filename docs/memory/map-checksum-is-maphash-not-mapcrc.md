---
name: map-checksum-is-maphash-not-mapcrc
description: "DemoTimeline.MapCrc is an unidentified 32-bit field, not the map version check; the version check on every era is MapHash (4 bytes pre-2013, 16 from 2013), through BspMapChecksum.Matches"
metadata:
  type: reference
---

**`DemoTimeline.MapCrc` is not the field that decides a map's version.** Finding 43
(`docs/findings/43-what-identifies-a-map.md`) found `svc_ServerInfo` carries two fields on old
protocols; this project named the 32-bit one `MapCrc` and the four-byte one `MapHash`, then chased
`MapCrc` for a day. **The map checksum is `MapHash`.** `MapCrc` (`0x534EEB7C` on the 2007 granary
specimen) remains unidentified and does not match anything.

**So one field answers every era.** `MapHash` is four bytes pre-2013 and sixteen (MD5) from 2013 on,
and `BspMapChecksum.Matches(file, recorded)` already disambiguates by length — feed it `MapHash`
regardless of era, never a value built from `MapCrc`. `PeriodMapChecksumTests` and
`BspMapChecksumConformanceTests` are the confirmed uses.

**Why this matters:** it looks like the natural reading is the reverse — `MapCrc` sounds like THE
checksum and `MapHash` sounds like a newer, better one. Building D162's version check from `MapCrc`
would have shipped a check that always disagrees with the real map, on every pre-2013 demo, with no
test catching it unless that test also used the wrong field. Caught before writing any code, by
reading `docs/findings/` first — [[valve-parity-is-the-first-principle]].
