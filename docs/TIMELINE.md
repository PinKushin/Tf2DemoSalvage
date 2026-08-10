# TF2 demo format timeline

What changed in the `.dem` format, when, and **how we know**. Every row carries its evidence,
because on this project the difference between measured and assumed has repeatedly been the
difference between a working parser and a plausible wrong one.

This is a working document. It grows as demos and sources arrive, and rows move up the evidence
scale as they are confirmed — a row never silently improves.

## Evidence grades

| Grade | Meaning |
|---|---|
| **Measured** | Observed directly in a corpus demo. The strongest thing here. |
| **Sourced** | Read from Valve's own code or headers — `proto_version.h`, the SDK — but not yet exercised against a demo. |
| **Bounded** | Known true at two points, with the change somewhere between them. |
| **Inferred** | Reasoned from other facts. Weakest, and named as such. |

---

## Dated anchors

The corpus's fixed points. Everything else is positioned relative to these.

| Date | Protocol | Source | Grade |
|---|---|---|---|
| **4 June 2009** | 15 | TF2 build 3862. The client's own `version` reports `Exe build: 13:52:56 Jun 4 2009 (3862)`. | **Measured** |
| 21–23 July 2020 | 24 | ETF2L match demos, dated by their league metadata | **Measured** |
| 7 Aug 2026 | 24 | demos.tf and serveme downloads | **Measured** |

`z1800.dem` carries no date. It was originally guessed at ~2015 from its protocol numbers, which
was **wrong** — protocol pairs date nothing, since 3/24 spans at least 2015 to 2026. It is now
placed at 2020 or later by two independent means: seasonal cosmetic names in its string tables,
and a game-event schema fingerprint identical to the 2020 ETF2L demos (below).

---

## Protocol-conditional format changes

### Confirmed in code and exercised by a demo

| Change | Boundary | Evidence | Grade |
|---|---|---|---|
| **Message type field is 5 bits, not 6** | somewhere in 16–23 | Protocol 15 is five bits, 24 is six, both decoded. Source sizes it by `2^NETMSG_TYPE_BITS > SVC_LASTMSG`; 2009's highest id was `svc_GetCvarValue` at 31. | **Bounded** |
| **`SendPropType` renumbered** — `DPT_VectorXY` inserted at 3, pushing String/Array/DataTable up one | somewhere in 16–23 | `dt_common.h` differs between the `orangebox` and `tf2` SDK branches; both numberings decoded against real demos. | **Bounded** |
| **String table length becomes a varint** | above 23 | `proto_version.h` (`NET_MAX_PAYLOAD_BITS went away`), and both forms decoded. | **Measured** |
| **Steam id rendering: Steam2 → Steam3** | between 2009 and 2020 | `userinfo` carries `STEAM_0:0:0` in 2009 and `[U:1:…]` in modern demos. | **Measured** |

**Neither of the first two appears in `proto_version.h`**, which is the single most important
caveat on this page: Valve's own list of era differences does not contain all of them. Both were
found only by decoding a demo old enough to break.

### Sourced from `proto_version.h`, not yet exercised

Valve ships this file in the *current* SDK because the live engine still reads old demos. Each
constant names the last build **without** the change — `PROTOCOL_VERSION_17` is "MD5 in map
version" and the MD5 appears at 18.

| Constant | Change | Implemented here |
|---|---|---|
| `PROTOCOL_VERSION_23` | `NET_MAX_PAYLOAD_BITS` went away | yes |
| `PROTOCOL_VERSION_22` | sound index was 13 bits | yes — `svc_Prefetch` width |
| `PROTOCOL_VERSION_21` | before the special DSP shipped | no — sound |
| `PROTOCOL_VERSION_20` | old-style dynamic model loading | no |
| `PROTOCOL_VERSION_19` | post-Halloween sound flag extra bit | no — sound |
| `PROTOCOL_VERSION_18` | pre-Halloween sound flag extra bit | no — sound |
| `PROTOCOL_VERSION_17` | MD5 in map version | yes |
| `PROTOCOL_VERSION_REPLAY 16` | Replay shipped; `svc_ServerInfo` gains a flag | yes |
| `PROTOCOL_VERSION_14` | create string tables compression flag | yes |
| `PROTOCOL_VERSION_12` | unlabelled | no |

**Protocol 14 matters more than it looks.** TF2 shipped on the Orange Box engine in October 2007,
which is pre-15 — so TF2's own 2007–2008 demos carry no compression flag. Implemented, untested,
and the corpus has nothing that old.

---

## Era fingerprints

Facts that are not protocol-conditional but differ visibly by era. Useful for **dating an
undated demo**, which is how `z1800.dem` was placed.

| Measure | 2009 (protocol 15) | 2020 | 2026 |
|---|---|---|---|
| `svc_ServerInfo` max classes | **232** | 362 | **363** |
| String tables declared | **16** | 20 | 20 |
| Game event definitions | **156** | 401 | **414** |
| Event field types (`string`/`float`/`long`/`short`/`byte`/`bool`) | **59/17/24/170/75/18** | 109/41/70/426/162/46 | 110/41/88/437/162/46 |
| `userinfo` record size | 132 bytes | 132 | 132 |

All measured 2026-08-10. **Four independent measures, and they agree** — which is what makes this
usable for dating rather than merely interesting.

**The game-event fingerprint is the sharpest dating tool found so far.** `z1800.dem` matches the
2020 demos exactly and differs from the 2026 ones, which is what confirmed its redating —
arrived at from the event schema rather than from map assets, and therefore independent of them.

**The 2020 and 2026 demos differ by a single networked class** (362 vs 363) and by thirteen game
event definitions. That is a finer resolution than expected — an era fingerprint does not need a
protocol bump to be visible, so this technique should keep working between protocol versions,
which is where dating is otherwise hardest.

**How to use it:** an undated demo's numbers are compared against this table. `z1800.dem` matches
the 2020 column on all four measures and differs from 2026 on three of them, which is what placed
it — independent of the cosmetic-asset dating that reached the same answer.

---

## Things that did *not* change

Worth recording, because the project's central bet is that these are rare.

- **The container.** 1072-byte header, flat command stream, 5-byte command headers. Identical in
  2009 and 2026, and `demostf/parser` has **zero** version conditionals in its container layer.
- **The `userinfo` record**, at 132 bytes in both eras.
- **`instancebaseline`**, present and used identically in both.
- **Game event encoding.** The 3-bit value type field and its meanings are the same across the
  corpus.
- **BSP version 20**, stable since 2006 — relevant to Phase 2/3 rather than parsing.

---

## Open questions

| Question | What would answer it |
|---|---|
| Where exactly did the message type field widen? | One demo in protocols 16–23 |
| Where exactly was `DPT_VectorXY` inserted? | Same demo |
| Does a protocol-14 demo really lack the compression flag? | A TF2 2007–2008 demo |
| What are the four sound-related boundaries? | Implementing `svc_Sounds`, then an old demo |
| Why do modern demos carry `MVMResetPlayerStats` in ordinary matches? | Unknown; possibly a renumbered id |

**One demo in protocols 16–23 would settle the first two at once.** That is roughly TF2
2010–2013, so league archives from that window — ESEA, ETF2L, ozfortress — are the highest-value
acquisition for this document. Such a demo is both the answer *and* the regression test, which is
why it beats any amount of reading.

### Engine branches date the protocol ranges — and pin both open boundaries to October 2011

TF2 did not stay on one engine. From the Valve Developer Community's own page: it *"originally
runs on Source 2007 ... later upgraded to Source 2009 in 2009, then **Source Multiplayer in
October 2011**, Source 2013 Multiplayer in 2013 (during SteamPipe), and finally has its own
branch since 2022."*

Combined with the protocol ranges each branch used:

| Engine branch | Protocols | TF2 dates |
|---|---|---|
| Source 2007 | ≤ 14 | Oct 2007 – 2009 |
| Source 2009 | **15–16** | 2009 – **Oct 2011** |
| Source Multiplayer | **18–23** | **Oct 2011** – 2013 |
| Source 2013 Multiplayer | 24 | 2013 – 2022 |
| TF2 branch | 24 | 2022 – |

**Both open boundaries collapse onto the October 2011 engine change.** The reasoning:

- the 2009 demo is protocol 15, Source 2009, and has **neither** the six-bit message type nor
  `DPT_VectorXY` — measured;
- modern demos are protocol 24, Source 2013, and have **both** — measured;
- `DPT_VectorXY` entered the engine line with Left 4 Dead in late 2008, but **Source 2009 never
  received it** — which the 2009 demo proves directly;
- TF2 inherited the L4D-era engine work when it moved to **Source Multiplayer in October 2011**,
  the jump from protocol 16 to 18.

So "somewhere in 16–23" becomes **at the 16 → 18 transition, October 2011**. Still bounded rather
than measured, but the window went from eight protocol versions to one engine change.

**What this changes about acquisition.** A demo from *before* October 2011 should be protocol 15
or 16 and use the old numbering; one from *after* should be 18+ and use the new. Either confirms
the boundary — so the target is no longer "anything from 2010–2013" but specifically **a demo
from either side of October 2011**, and the two together would settle it outright.

### One deduction that narrows it without a demo

`PROTOCOL_VERSION_REPLAY = 16`, and Replay shipped in mid-2012. So **protocol 15 spans June 2009
to 2012** — a three-year era, which is why a 2009 demo and a 2011 demo would likely both be
protocol 15.

Both open changes are *network format* changes, and a protocol number only moves when the wire
format does. So neither happened inside the protocol-15 era: both are at 16 or later. That rules
out the possibility that they arrived quietly during those three years.

### Searched and came up empty — do not repeat

**The TF2 wiki's patch archive does not record protocol changes.** Established with a control
rather than assumed:

| Query | Hits |
|---|---|
| `Scout` | 808 — search works |
| `protocol` | 3, all a custom mission |
| `insource:protocol` | 0 — **unsupported, and silently returns zero** |

That last row is worth its own warning: `insource:` needs CirrusSearch, which this wiki does not
run, so it returns an empty result rather than an error. A search that cannot work looks exactly
like a search that found nothing — the same false-negative shape as the VsTest runner scoring
1.27% and a non-matching Stryker glob.

**Steam's own news archive does not record them either.** Checked properly: 3,920 news items back
to June 2008, 4.9 million characters of patch notes, pulled from Steam's public news API — which
is what SteamDB displays. Controls confirm the corpus is searchable (`Fixed` in 1,331 items,
`demo` in 344, `network` in 77). **`protocol` appears in zero.**

What the patch notes *do* record is the symptom, never the cause:

| Date | Note |
|---|---|
| 2010-07-20 | "Fixed old demos with different **data tables** thinking the SourceTV player entity was burning" |
| 2013-02-28 | "Fixed a problem that was preventing some older demos from being played" |
| 2013-07-12 | same wording again |

**So the changelog route is exhausted, and the reason is structural:** Valve documents the fix,
not the format change. The people who noticed a format change first were the ones reviewing demos
daily — the competitive scene — so `teamfortress.tv` threads, dated by their post IDs, are the
remaining source. The engine emits an explicit error naming both numbers
(`demo network protocol N outdated, engine version is M`), and a forum post quoting it carries an
exact protocol pair with a date attached.

---

## Sources

- Valve's `proto_version.h` and `dt_common.h`, from `alliedmodders/hl2sdk` — branch `tf2` for
  current, `orangebox` for 2009. The `orangebox` branch ships **no TF2 game code**, which is why
  some era questions cannot be answered from source at all.
- `game/shared/tf/tf_usermessages.cpp`, for the user message table.
- `demostf/parser`, as a cross-check — but note it hardcodes six-bit message types and the modern
  property numbering, so it cannot read a protocol-15 demo. It is a reference for modern demos,
  not for the era axis.
- The corpus itself, which has now falsified more assumptions than any document.


---

## Acquisition leads, and what each would actually settle

Searched 2026-08-10. Recorded so the same ground is not re-covered.

| Lead | Would settle | Status |
|---|---|---|
| A demo from either side of **October 2011** | the message-type width **and** the `SendPropType` renumbering, together | **the priority.** Nothing found yet |
| `teamfortress.tv` thread *"2007 Team Fortress 2 (aka Orange Box) Client/Server"* (`/21291/`) | the **protocol 14** compression flag | found; see caveats below |
| GotFrag archives | old competitive demos | pages are on the Wayback Machine; binary downloads almost never are |

### The Orange Box client lead

A community redistribution of the 2007 launch build, mirrored from a Facepunch thread as
`OBTF2Client.zip` via torrent and Mediafire. Launch-era TF2 is pre-protocol-15, so a demo
recorded on it would exercise `PROTOCOL_VERSION_14` — the string table compression flag, which is
implemented here and has never run against real data.

**Two caveats, stated rather than buried.** It is an unofficial redistribution of the game — a
commenter in the thread asks outright whether running it risks a VAC ban — which is a different
provenance from the archive.org build the 2009 demo came from. And the links are eleven years old,
so they may simply be dead.

### Searching `teamfortress.tv`

Its search **does not do phrase matching** — quoted strings are OR-ed across terms, so
`"engine version is"` returns map-discussion threads. That makes hunting the engine's exact error
message (`demo network protocol N outdated, engine version is M`) impractical through the site
search, even though that string would carry an exact protocol pair and a date.

Better angle for a future attempt: the site orders threads by date, and TF2 moved to Source
Multiplayer in **October 2011**. Threads from that fortnight in `TF2 General Discussion` are where
a break would have been reported, and can be read directly rather than searched for.
