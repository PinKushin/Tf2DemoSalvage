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
| **9 October 2007** | **11** | TF2 build 3258 — the launch build. `Exe build: 18:14:51 Oct  9 2007 (3258)`, `PatchVersion=1.0.0.5`. | **Measured** |
| **on or before 15 Nov 2007** | **13** | TF2 patch notes for that day: *"Added backward compatibility code to allow demos recorded with protocol 12 to continue to be playable under protocol version 13."* The build shipped that day **is** protocol 13, so 13 exists by then — but the note is a repair, and a repair lags the break that caused it. See below. | **Bounded** |
| **19 March 2008** | **14** | `engine.dll` build 3420: `StartRecording` writes the literals `3` and `14` into the header it is about to record. Dated from the binary, without launching anything. | **Measured** |
| **19 March 2008** | **14** | TF2 build 3420. `Exe build: 20:17:35 Mar 19 2008 (3420)`, `PatchVersion=1.0.2.2`. | **Measured** |
| **4 June 2009** | 15 | TF2 build 3862. The client's own `version` reports `Exe build: 13:52:56 Jun 4 2009 (3862)`. | **Measured** |
| **15 June 2011** | **16** | TF2 build 4604. `Exe build: 13:46:52 Jun 15 2011 (4604) (440)`, `Exe version 1.1.5.8`. | **Measured** |
| **25 March 2013** | **24** | TF2 build 1729296. `Exe build: 17:24:29 Mar 25 2013 (5252) (215)`. | **Measured** |
| 21–23 July 2020 | 24 | ETF2L match demos, dated by their league metadata | **Measured** |
| 7 Aug 2026 | 24 | demos.tf and serveme downloads | **Measured** |

**Every one of these was established the same way and it is worth stating the method once:** run
the period client, read its `version`, record a demo, and check the demo header agrees. Three of
them were additionally dated *before* the client was ever launched, by reading the build string
out of `bin/engine.dll` — see `DECISIONS.md` D30, which also explains why that costs 4 MB rather
than a 3–5 GB download when the archive is a ZIP.

**Two gaps remain, and both are narrow:**

| Gap | Window | Width |
|---|---|---|
| **12–13** | 9 Oct 2007 → 15 Nov 2007 | five weeks, both inside it |
| **17–23** | 15 Jun 2011 → 25 Mar 2013 | twenty-one months |

Protocol 11 at launch was a surprise. The March 2008 build reports 14, and the launch build was
expected to report 14 as well — three protocol versions came and went in TF2's first five months,
which is a faster cadence than anything later in its history.

**The changelog closed most of that window on 2026-08-11.** The 15 November 2007 patch names
protocols 12 and 13 in the same sentence, which puts **both** inside the five weeks after launch —
against four candidate patches, on 25 and 31 October and 1 and 7 November.

**That patch is a ceiling, not a date, and the first version of this section got it wrong.** The
note describes *adding backward compatibility code* — a repair. A repair lags the break: 13 had
to have shipped, players had to have found their protocol-12 recordings unplayable, and that had
to have been reported, before anyone wrote compatibility code. So 13 was live for some days at
least before 15 November, and 12 is pushed further back still, toward the 25 October patch.

The same patch carries a *second*, separate demo repair — **"Fix for broken .dem file
playback"** — which is the corroboration. Two independent demo fixes in one patch describes a
demo system that had been broken in the field, not one that broke that morning.

The compatibility shim is the only one of its kind in TF2's notes: **no patch note anywhere
documents the 13→14 bump**, or 15→16, or any later one. Valve wrote this note because they had
shipped a user-visible fix, not because bumping the protocol was newsworthy — which is exactly
why the note trails the event it lets us date.

`z1800.dem` carries no date. It was originally guessed at ~2015 from its protocol numbers, which
was **wrong** — protocol pairs date nothing, since 3/24 spans at least 2015 to 2026. It is now
placed at 2020 or later by two independent means: seasonal cosmetic names in its string tables,
and a game-event schema fingerprint identical to the 2020 ETF2L demos (below).

---

## Protocol-conditional format changes

### Confirmed in code and exercised by a demo

### The one boundary Valve's own engine calls breaking

Read out of `engine.dll` build 3420 on 2026-08-11. `ReadDemoHeader` accepts a demo when its
network protocol is **12 or above**, and rejects anything below:

| transition | breaking, per the engine | consequence |
|---|---|---|
| **11 → 12** | **yes** | a launch-era demo is refused by every later client |
| 12 → 13 → 14 | no | one engine plays all three |

This is the compatibility code the 15 November 2007 note describes, still in place four months
later. It also fixes the compatibility line's position for good: Valve set it immediately above
protocol 11 and never moved it again, which is exactly the population this project exists to read.

The container version is validated separately and accepts **2 or 3**.

### The user message table is a separate axis, and it moves independently

Read from the registration order in six shipped clients. The table lives in the **game DLL**;
the protocol number lives in the **engine**. Neither dates the other.

| build | protocol | registers | ids | ends at |
|---|---|---|---|---|
| 2007, 2008 | 11, 14 | 29 | 0–28 | `PlayerStatsUpdate` |
| 2009 | 15 | 41 | 0–40 | `CheapBreakModel` |
| 2011 | 16 | 49 | 0–48 | `PlayerBonusPoints` |
| March 2013 | 24 | 66 | 0–65 | `MVMLocalPlayerWaveSpendingValue` |
| July 2026 | 24 | 79 | 0–78 | `BuiltObject` |

The last two rows share a protocol number and differ by thirteen entries, so **protocol 24 cannot
select a name table** above id 50 — `RDTeamPointsChanged` was inserted at 51 after March 2013. See
`RISKS.md` B29. A **second** registration block of six Novint Falcon haptics messages follows the
game's in every build from 2009 on, which is where the corpus's unnamed ids came from.

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

| Measure | 2007 (11) | 2008 (14) | 2009 (15) | 2011 (16) | 2013 (24) | 2020 (24) | 2026 (24) |
|---|---|---|---|---|---|---|---|
| `svc_ServerInfo` max classes | **216** | **216** | **232** | **256** | — | 362 | **363** |
| String tables declared | **16** | **16** | **16** | 16 | 16 | 20 | 20 |
| Game event definitions | — | — | **156** | — | — | 401 | **414** |
| Event field types (`string`/`float`/`long`/`short`/`byte`/`bool`) | — | — | **59/17/24/170/75/18** | — | — | 109/41/70/426/162/46 | 110/41/88/437/162/46 |
| `userinfo` record size | 132 bytes | 132 | 132 | 132 | 132 | 132 | 132 |

All measured 2026-08-10. **Four independent measures, and they agree** — which is what makes this
usable for dating rather than merely interesting.

**Max classes is non-decreasing, and that is the useful shape:** 216, 216, 232, 256, … 362, 363.
It grows as TF2 gains entity types and never shrinks, so it bounds a demo's age from below. Note
that 2007 and 2008 **tie** at 216 — the count is monotonic but not strictly increasing, so it can
place a demo in a range and cannot always separate adjacent builds.

**The string table count is worthless for dating and it looked promising.** Sixteen in 2007, 2008,
2009, 2011 and 2013; twenty in 2020 and 2026. Five eras spanning six years with an identical
number, which was briefly treated as an era discriminator and would have produced a confident
wrong answer for any demo in that span. What differs is the table *names*, not how many there are.
Recorded here as a caution: a fingerprint that fails to move across five samples is not a
fingerprint, and finding that out required the samples rather than reasoning.

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


---

## Pre-registered prediction for a protocol-14 demo

Written **before** such a demo was obtained, so the outcome can falsify the model rather than be
explained by it. A 2008 Orange Box build is being acquired at the time of writing.

Every protocol-conditional branch in the parser, and what each does at 14 versus the 15 already
validated:

| Branch | Protocol 15 (validated) | Protocol 14 | Differs? |
|---|---|---|---|
| String table compression flag | present | **absent** | **YES — the only one** |
| Message type field width | 5 bits | 5 bits | no |
| `SendPropType` numbering | old | old | no |
| `svc_ServerInfo` replay flag | absent | absent | no |
| Map hash | 4-byte CRC | 4-byte CRC | no |
| `svc_Prefetch` index | 13 bits | 13 bits | no |
| String table / temp entity length | fixed | fixed | no |

**The prediction: a protocol-14 demo decodes end to end with zero stops, exercising exactly one
branch that has never run against real data** — the missing compression flag in
`svc_CreateStringTable`, from `PROTOCOL_VERSION_14`.

**What would falsify the model.** Any failure *not* in string table decoding. String tables are
load-bearing, so a wrong compression-flag rule desynchronises everything after them and the trace
stops in a `svc_createstringtable` block. A stop anywhere else — in `svc_ServerInfo`, in the
schema, in entity decode — means an era difference exists between 14 and 15 that neither
`proto_version.h` nor this project knows about, exactly as happened for B17 and B18 between 15
and 24.

**Secondary outcome regardless of the above:** the era-fingerprint table gains a fourth column,
and with it a pre-Source-2009 anchor. Max classes, string table count and game event definitions
should all be *lower* than 2009's 232 / 16 / 156 if the build is genuinely older.

**If `version` reports 15 rather than 14**, the trip teaches nothing new about the format and the
build is redundant with the one already in the corpus. That check costs one console command and
should be run before recording anything.

### Outcome, 2026-08-10: FALSIFIED, and the failure was worth more than the confirmation

```
Protocol version 14
Exe version 1.0.2.2 (tf)
Exe build: 20:17:35 Mar 19 2008 (3420)
```

**The prediction failed, on its own stated terms.** The falsification condition was "any failure
not in string table decoding", and the schema — `dem_datatables` — did not parse at all:

```
System.IO.EndOfStreamException : Requested 8 bits at bit offset 686782, but only 2 bits remain.
```

**It was first reported here as confirmed, and that was a measurement error.** The message stream
does decode clean — 12,608 commands, 22,176 trace lines, no stops — and that was checked and
reported as though it covered the demo. It does not: `--trace` without `--entities` never parses
the schema, so the check was blind to the half that failed. *Choosing where to look is part of
the measurement.*

**Cause: the property bit-count field is six bits at protocol 14, not seven.** Found by
comparison rather than by reading a spec, in four steps:

1. The parse read **one table** where the 2009 demo reads 334, so the desync is immediate, not at
   the tail. The end-of-stream error was where it finally ran out, not where it went wrong.
2. Both eras' first table is `DT_AI_BaseNPC` with 12 properties, and properties 0 and 1 cost
   **identical** bits in both — so the reader was still synchronised at property 2.
3. The raw bits said by how much: protocol 14 at bit 597 is protocol 15 at bit **598**. One bit
   fewer, in `type(5) + name + flags(16) + low(32) + high(32) + bits(N)`.
4. Setting N to 6 yields **308 tables and 216 server classes** — and `svc_ServerInfo` in the same
   file independently reports `max_classes 216`. Two unrelated parts of the file agreeing is what
   makes this measured rather than fitted. At seven it yields one table and nonsense, and six
   breaks the 2009 demo, so the rule is genuinely era-specific.

Not in `proto_version.h`, like B17 and B18. Six bits holds 0–63, ample for anything Source sends;
the seventh arrived with room to spare rather than out of need, which is presumably why the change
went unrecorded.

**With that fixed, the original prediction's substance does hold.** The demo now decodes end to
end including its schema, and the never-executed branch ran: 16 `svc_createstringtable` messages
on the ≤14 side of `CompressionFlagProtocol`, 45 `svc_prefetch` on the 13-bit index. The era axis
has its pre-Source-2009 anchor. But it took a code change to get there, which is exactly what the
prediction said would not be needed.

**One registered secondary prediction was wrong, and it is the more interesting half.** The
fingerprint was expected to be *lower than 2009 across the board*:

| | 2008 (proto 14) | 2009 (proto 15) | modern (proto 24) |
|---|---|---|---|
| `max_classes` | **216** | 232 | 275 |
| string tables | **16** | 16 | 16 |

Max classes fell as expected. **String table count did not — it is 16 in all three eras, eighteen
years apart.** So the table *set* is far more stable than the class list, and a count that was
being treated as an era discriminator discriminates nothing. Dating by it would have produced a
confident wrong answer for any era. The names differ where the count does not, so the discriminator
worth using is the table *names*, not how many there are.

**A container-level difference, not predicted and not in `proto_version.h`:** this demo carries
**no `dem_stringtables` command at all**. Both the 2009 and 2013 demos carry exactly one. At
protocol 14 the tables arrive only as `svc_CreateStringTable` in the signon stream. Any code that
assumes a `dem_stringtables` command exists — as a place to recover tables from, or as a checkpoint
— is assuming something an entire era does not provide. Same discovery shape as B17 and B18.

**Grade: Measured.** Console output and parser output, both quoted.


---

## Pre-registered prediction for a Source MP era demo (protocol 16–23)

Written **before** the client was launched. A retail build stamped `Exe build: 17:24:29 Mar 25
2013`, `PatchVersion=1729296`, is extracted and unrun at `F:\tf2-builds\tf2-2013`.

**The gap this aims at.** The corpus has protocol 15 (2009) and protocol 24 (`z1800.dem`). Every
protocol between them is unrepresented, and that span contains **four of the parser's seven
protocol boundaries** — every one of them implemented from reading `proto_version.h` and another
parser, none executed against a demo that sits on the far side. The date is chosen because it
straddles TF2's SteamPipe transition, so the build is either the last of Source Multiplayer or the
first of Source 2013.

Each parser constant, and which side of it a 16–23 demo falls:

| Constant | Boundary | At 15 (validated) | At 16–23 | At 24 (validated) |
|---|---|---|---|---|
| `CompressionFlagProtocol` | 14 | present | present | present |
| `FiveBitTypeProtocol` | 15 | 5-bit type | **6-bit type** | 6-bit type |
| `VectorXyProtocol` | 15 | old numbering | **new numbering** | new numbering |
| `PrefetchWidthProtocol` | 22 | 13 bits | **13 or 14, by side** | 14 bits |
| `TempEntitiesVarIntProtocol` | 23 | fixed 17-bit | **fixed 17-bit** | varint |
| `VarIntLengthProtocol` | 23 | fixed | **fixed** | varint |

**The prediction, in two cases, because `version` decides which.**

- **Protocol 23.** Every branch above is already exercised on one side or the other by the two
  demos in hand, so a 23 demo should decode **end to end with zero stops and no new branch taken**.
  Its value is then confirmatory, not exploratory: it is the first evidence that the 16–23 span
  behaves as interpolated rather than as assumed. That is worth having precisely because nothing
  currently distinguishes "correct across the gap" from "untested across the gap".
- **Protocol 24.** The build sits after the transition, the demo is redundant with `z1800.dem` for
  boundary purposes, and the finding is a **date bound**: protocol 24 was already live on
  2013-03-25, which narrows the 23→24 change to before that date.

**What would falsify the model.** A stop at protocol 23 in any message type, since by the table
above no branch flips between 15/24 and 23 that is not already covered from both sides. Such a
stop means a boundary exists inside 16–23 that neither `proto_version.h` nor this project knows
about — the same discovery shape as B17 (message type width) and B18 (`SendPropType` renumbering),
both of which were found exactly this way and neither of which is in Valve's own list.

**Secondary outcome regardless:** a third row in the era-fingerprint table, between 2009's
232 / 16 / 156 and the modern figures. Max classes and game event definitions should fall between
the two if the model of steady growth holds; a value outside that range is itself a finding.

**Run `version` first.** It costs one console command and it decides which of the two predictions
above is being tested — recording a demo before knowing which is how a result gets fitted to
whichever story survives.

### Outcome, 2026-08-10: neither case. Protocol 16, and the gap has its first specimen

```
Protocol version 16
Exe version 1.1.5.8 (tf)
Exe build: 13:46:52 Jun 15 2011 (4604) (440)
```

A June 2011 build, found by widening the search after the March 2013 candidate came back at 24.
**Protocol 16 is inside the 16–23 gap** — the first client obtained from it.

**Why 16 is the most valuable single value in that range.** It is not merely "somewhere in the
middle": it sits exactly on a boundary this project implemented and could never execute.
`svc_ServerInfo`'s replay flag is read as `protocol > 15`, so **16 is the first protocol that has
it**. Every other corpus demo tests that rule from far away — 14 and 15 from below, 24 from well
above — and a boundary is only really tested at the value where it changes.

It also produces a combination of protocol-conditional rules that no demo in the corpus can:

| Rule | Boundary | At 15 | **At 16** | At 24 |
|---|---|---|---|---|
| String table compression flag | 14 | present | present | present |
| Schema bit-count width | 14 | 7 | **7** | 7 |
| Message type field | 15 | 5-bit | **6-bit** | 6-bit |
| `SendPropType` numbering | 15 | old | **new** | new |
| `svc_ServerInfo` replay flag | 15 | absent | **present, first** | present |
| `svc_Prefetch` index | 22 | 13-bit | **13-bit** | 14-bit |
| Temp entity / table lengths | 23 | fixed | **fixed** | varint |

The middle four are what make it worth having: **new type numbering and a six-bit message type
together with a 13-bit prefetch index and fixed lengths.** Nothing in the corpus holds that
combination, and it is precisely the interpolation the 15-to-24 jump forced this project to assume.

**The `Exe build` fingerprint is now explained rather than merely observed.** Earlier entries noted
that 2011 and 2013 print two trailing numbers where 2007 and 2008 print one. This build resolves
them: `(4604) (440)` is the build number and the **Steam appid**. So the second field is an appid,
the change happened between March 2008 and June 2011, and the "fingerprint" is a logging change
rather than anything structural. Worth recording precisely because a suggestive pattern that turns
out to mean something ordinary is the kind that otherwise gets quoted as evidence later.

**Grade: Measured.** Console output from the client, quoted verbatim.

**Remaining gap: 17–23.** Narrower than it was, and now bounded on both sides by measured
specimens rather than by inference. A build filling it must be stamped between **June 2011 and
March 2013**, and `bin/engine.dll` reads that date without launching anything (D30).

### Outcome, 2026-08-10: case two. Protocol 24, so a date bound rather than a demo

```
Protocol version 24
Exe version 1729296 (tf)
Exe build: 17:24:29 Mar 25 2013 (5252) (215)
```

**Measured.** The second registered case, exactly as written: the build is on the modern side, the
demo it would record is redundant with `z1800.dem` for boundary purposes, and the finding is a
bound on the calendar instead of a new protocol.

**Protocol 24 was already live on 2013-03-25.** That is earlier than the SteamPipe conversion it
was expected to arrive with, so 24 predates that change rather than shipping in it. The 23→24
boundary therefore sits **before** 2013-03-25, and the Source Multiplayer era closed earlier than
a SteamPipe-anchored guess would put it.

**The gap is not closed and this build cannot close it.** Protocols 16–23 remain unrepresented,
and with them four of the parser's seven boundaries stay unexecuted on their far side. What this
run does is narrow the search: a build that fills the gap must be stamped **between October 2011
and March 2013**, and `Exe build` in `bin/engine.dll` reads that date without launching anything —
so candidates can be dated before being downloaded in full.

**Grade: Measured.** Console output from a retail client, quoted verbatim.

*Incidental fingerprint, unverified:* this build stamps `Exe build` with **two** trailing numbers
where the 2008 build's format string carries one. If that holds across builds it dates a binary
from the string table alone, but two samples is not a rule.

## Hunting a protocol 17–23 specimen: what has been checked

The last unmeasured stretch is **15 Jun 2011 → 25 Mar 2013**, nine protocol numbers. Two routes,
and the second is now much cheaper than it was.

**Route A — a period demo.** The specimen itself. Checked 2026-08-11:

| source | verdict |
|---|---|
| `demos.igmdb.org` | **2018+ only** — ESEA 29–31, ETF2L 29–42, RGL 7–9, Insomnia 65/69. No 2011–2013 content. |
| ETF2L's own archive (`etf2l.org/demos/`) | **Best remaining lead.** ETF2L Seasons **11–13** fall inside the window (late 2011 → early 2013), and the league has run continuously since 2007. The listing is JS-rendered, so it needs a browser rather than a fetch. |
| teamfortress.tv request threads | List ETF2L S11/S12 and ESEA S11/S12 as obtainable, by request rather than direct download. |

**Route B — a period client, then record one.** This is how protocols 11, 14, 15, 16 and 24 were
measured, and it got cheaper on 2026-08-11: `StartRecording` writes the protocol into the header
as a **literal**, so a candidate `engine.dll` can be both dated and protocol-identified from about
4 MB, with no install and nothing launched. See `findings/01`.

That turns Route B from "download a build and run it" into triage: pull only `bin/engine.dll` from
any 2011–2013 build, `grep -a "Exe build"` it for the date, and read the constant for the
protocol. Candidates come from `DepotDownloader` against app 440 depot 441 with a period manifest.

**Either route settles several rows at once**, which is why this is the highest-value acquisition
left: the 5-bit message type boundary, the `SendPropType` renumbering, and both are currently
graded *bounded* across this same gap.

## Where the specimens came from

**Benroads kept demos from 2013 when nobody was keeping demos.**

The gap this document keeps naming — protocols 17 to 23, roughly the run-up to the Steampipe-era
format — exists because TF2's competitive scene of that period cast over Mumble rather than
recording, and because demos.tf did not exist yet to archive anything centrally. There was no
institution keeping them. What survives, survives because an individual chose not to delete it.

Benroads has around 53.5 GB spanning 2013 to 2017 and is sending a few per year. A few per year is
exactly the right shape for this corpus: the committed set grows for a **new protocol**, not for
volume, since GitHub's free Git LFS tier is 1 GiB a month and every CI job pays it. A single demo
from early 2013 is worth more here than a hundred from 2016, because it may carry a protocol
nothing else in the corpus exercises.

Also from that scene: the ESEA seasons 29–32 archive, which is where `cp_process_f12` came from.
Those are 2018–19 and therefore protocol 24, so they add maps and volume rather than era coverage.

Worth stating plainly, because it will matter to whoever reads this later: **the era axis of this
project is not the product of a plan.** It is the product of a handful of people who still had the
files. Every row above that is measured rather than interpolated exists because somebody kept
something they had no particular reason to keep.
