---
name: era-axis-is-measured
description: Five TF2 protocols dated exactly by running period clients — 11/14/15/16/24 — plus the two gaps left and the cheap way to date a candidate build before downloading it
metadata: 
  node_type: memory
  type: project
  originSessionId: 9b3a8b35-1dc8-47b0-a320-73b01288f10c
  modified: 2026-08-10T17:12:34.878Z
---

The era axis is no longer inferred. Five protocols, each dated by running the period client,
reading `version`, recording a demo, and checking the demo header agrees:

| Date | Protocol | Build |
|---|---|---|
| 2007-10-09 | **11** | 3258, `PatchVersion=1.0.0.5` — TF2's launch build |
| 2008-03-19 | **14** | 3420, `1.0.2.2` |
| 2009-06-04 | **15** | 3862 |
| 2011-06-15 | **16** | 4604, `1.1.5.8` |
| 2013-03-25 | **24** | 1729296 |

**Gaps: 12–13** (five months, Oct 2007 → Mar 2008) and **17–23** (Oct… Jun 2011 → Mar 2013).
Protocol 11 at launch was a surprise — 14 was expected, so three versions came and went in TF2's
first five months, a faster cadence than any later period.

## Date a candidate before downloading it

`bin/engine.dll` carries the build date as a plain string (`Exe build: 18:14:51 Oct  9 2007`), so
a candidate is datable **statically** — no launching, no Steam, no unpacking. Archive.org serves a
single member out of a **ZIP** (4 MB instead of 3 GB) and **not** out of a 7z: a solid archive
cannot be partially decompressed and the request fails as **HTTP 200 with a zero-byte body**.
Check the size, not the status. Full detail in `DECISIONS.md` D30.

The `Exe build` trailing numbers are `(build) (appid)` — one number in 2007/2008, two from 2011.
A logging change, not a structural one. Worth knowing precisely because it looked like a
structural fingerprint first.

## What each era actually changes

Every protocol-conditional rule in the parser is keyed at 14, 15, 22 or 23. So **protocol 11
needed no new rules at all** — it exercises every branch on its old side simultaneously. The
boundaries that matter:

- **≤14**: no string table compression flag, **6-bit** schema bit-count field (B23), no
  `dem_stringtables` command in the container at all
- **≤15**: 5-bit message type, old `SendPropType` numbering, no `svc_ServerInfo` replay flag
- **16**: first protocol *with* the replay flag — which is why 16 was the single most valuable
  value in the 17–23 gap to obtain
- **≤22**: 13-bit `svc_Prefetch` index; **≤23**: fixed rather than varint lengths

## Fingerprints: one works, one is a trap

`max_classes` is **non-decreasing**: 216, 216, 232, 256, 362, 363. It bounds a demo's age from
below. Note 2007 and 2008 **tie**, so it ranges rather than separates.

**The string table COUNT dates nothing** — 16 at protocols 11, 14, 15, 16 and 24; 20 in 2020+.
Five eras across six years with an identical number. It was briefly treated as a discriminator and
would have given a confident wrong answer for anything in that span. The table *names* differ; the
count does not. A measure that fails to move across five samples is not a fingerprint, and only
the samples showed that.

Related: [[z1800-is-modern-not-2015]] for why protocol numbers date nothing on their own, and
[[proto-version-h-enumerates-the-boundaries]] for the boundaries Valve did write down — which
notably excludes B23 and the missing `dem_stringtables`.
