---
name: era-axis-is-measured
description: "Five TF2 protocols dated exactly by running period clients — and the rule that a protocol number dates nothing, with the corpus itself as the counterexample."
metadata: 
  node_type: memory
  type: project
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-09T03:53:50.420Z
---

The era axis is measured, not inferred — five protocols, each dated by running the period client,
reading `version`, recording a demo, checking the header agrees:

| Date | Protocol | Build |
|---|---|---|
| 2007-10-09 | **11** | 3258, launch build |
| 2008-03-19 | **14** | 3420 |
| 2009-06-04 | **15** | 3862 |
| 2011-06-15 | **16** | 4604 |
| 2013-03-25 | **24** | 1729296 |

Gaps: 12–13 (Oct 2007→Mar 2008), 17–23 (Jun 2011→Mar 2013).

**Date a candidate build before downloading:** `engine.dll` carries build date as a plain string, so
static dating needs no launch. Archive.org serves single-member downloads from a **ZIP** (4MB), not a
7z (solid, can't partially decompress — fails as HTTP 200 with zero bytes; check size, not status).
Detail: D30.

**What each era changes:** ≤14 no string-table compression flag, 6-bit schema bit-count (B23), no
`dem_stringtables`; ≤15 5-bit message type, old `SendPropType` numbering — **but at 15 only build
3862**: later protocol-15 builds write six bits and VectorXY, so at 15 the demo decides (B440,
[[a-protocol-can-hide-two-builds]]); 16 first with replay flag; ≤22 13-bit `svc_Prefetch`; ≤23 fixed
rather than varint lengths.

**Fingerprints:** `max_classes` is non-decreasing (216,216,232,256,362,363) — bounds age from below,
but 2007/2008 tie. **String table COUNT dates nothing** — 16 at protocols 11,14,15,16,24 and even
2020+; briefly treated as a discriminator, would have given a confident wrong answer.

---

## `a-client-dates-a-protocol-a-demo-does-not`

Owner, correcting progressively: *"even running the period client doesnt actually date the demo... if
someone like me uses a old client you can make new demos on old protocols... you can only guestimate
where the protocol updates landed within small windows."*

**A demo's protocol says which protocol it speaks — nothing more.** It doesn't bound recording date;
an old client still runs and records. **The corpus itself is the counterexample** — every gcor era
specimen was recorded on a period client in 2026 (`tf2-2007-build3258-pov-cp_granary.dem` speaks
protocol 11, weeks old).

| Question | Answered by |
|---|---|
| Which protocol does this file speak? | the demo's header |
| Do we hold a specimen of protocol N? | the corpus |
| When did protocol N land? | changelogs, forum posts — **estimated windows** |
| When was THIS demo recorded? | its own content (assets, map versions, filename) |

**Never infer a recording date from a protocol number, either direction.** Dating serves the write-up,
not the parser — a protocol spans many weekly builds and its edges can't be pinned to a week ("trying
to date it to the week is going to be practically impossible"). See [[a-changelog-dates-the-complaint]].

---

## `z1800-is-modern-not-2015` — the mis-dating that made the rule

Established 2026-08-07 by reading `z1800.dem`'s bytes. Demboyz dates protocol 3/24 to July 2015 — TF2
kept that pair for years. `z1800.dem` carries protocol 3/24 AND `sum20_fire_fighter_style1` (Summer
2020), `etf2l_2018_bronze`, Competitive Mode voice lines — it's from **mid-2020 or later**, not
2015-2016 as earlier docs claimed.

**Date demos from seasonal asset names** (self-dating); protocol numbers only tell decode quirks.

**The file is truncated by exactly one byte** — final `dem_stop` header has 3 of 4 tick bytes,
harmless (`dem_stop` has no payload), but EOF there must be treated as normal end, not corruption.

**Server identity is self-declared** — `Server Name` is the `hostname` cvar, free text; bytes only
support "the server called itself X", not who ran it.

**Why it mattered:** the corpus was believed a rare mid-2010s specimen; it's modern-era, meaning zero
pre-2020 demos existed at the time. D5 was rewritten 2026-08-07: modern demos are abundant, self-
recorded ones the only correctness ground truth; historical demos are genuinely scarce (period-client
recordings above fix that).

**A demo names its own map file** (added 2026-08-08): `downloadables` string table contains
`maps\<name>.bsp`, and `svc_ServerInfo` carries a 16-byte map hash — D9's resolver can use these
rather than inferring from the map name, catching a same-name-wrong-version community map.

---

Related: [[hl2sdk-branches-are-per-era-headers]], [[where-the-game-and-clients-live]],
[[record-both-points-of-view]].
