# Format specification, consolidated

Single place for what we actually know about the `.dem` format, gathered from the
public sources and — where possible — checked against real bytes rather than
taken on faith.

**Every claim carries a confidence tag.** This is the point of the document. A
consolidated spec that blurs "verified against `z1800.dem`" with "a wiki said so"
is worse than no spec, because it launders assumptions into facts.

| Tag | Meaning |
|---|---|
| **CONFIRMED** | Verified against real bytes in `tools/corpus/demos/z1800.dem`. Reproduce with `tools/inspect_demo.py walk`. |
| **DOCUMENTED** | Stated by a public source, not yet checked against bytes here. |
| **CONCEPTUAL** | Public sources describe the mechanism but not the wire encoding. |
| **UNDOCUMENTED** | No public authoritative source found. Must be derived from prior art or reverse engineering. |
| **OPEN** | We don't know, and it matters. |

Sources used: [demboyz `DemFormat.md`](https://git.botox.bz/CSSZombieEscape/demboyz/src/commit/3858162c9c0fb0988e30f61de526ebfe85eb1e2f/docs/DemFormat.md)
(documents the TF2 build active July 2015 — **not** our specimen's era, but the same
protocol pair, which is the part that matters),
[VDC: Networking Entities](https://developer.valvesoftware.com/wiki/Networking_Entities),
and [demostf/parser](https://github.com/demostf/parser) read for cross-checking
behaviour, not for code.

---

## Layer 1 — Container envelope

### Header — **CONFIRMED**

Fixed 1072 bytes, little-endian, no alignment padding. Every offset below was
read out of `z1800.dem` and matched the documented layout exactly.

| Offset | Type | Field | `z1800.dem` |
|---|---|---|---|
| 0 | `char[8]` | File stamp | `HL2DEMO\0` |
| 8 | `int32` | Demo protocol | 3 |
| 12 | `int32` | Network protocol | 24 |
| 16 | `char[260]` | Server name | `FACEIT.com register to play here` |
| 276 | `char[260]` | Client name | `SourceTV Demo` |
| 536 | `char[260]` | Map name | `koth_harvest_final` |
| 796 | `char[260]` | Game directory | `tf` |
| 1056 | `float32` | Playback time (s) | 863.265 |
| 1060 | `int32` | Playback ticks | 57,551 |
| 1064 | `int32` | Playback frames | 14,386 |
| 1068 | `int32` | Signon length | 912,640 |

The `char[260]` fields are fixed-width and NUL-padded, **not** length-prefixed —
read the full 260 bytes and truncate at the first NUL. Treat trailing bytes past
the NUL as undefined; do not assume they are zero.

> **Protocol numbers do not date a demo.** — **CONFIRMED**
>
> The demboyz writeup documents demo protocol 3 / network protocol 24 as current in
> July 2015, and it was. But TF2 kept that pair for years, and this file proves it:
> protocol 3 / 24 carrying `sum20_fire_fighter_style1` and `@20_handsome_devil`,
> Summer 2020 cosmetics, alongside RGL medals and Competitive Mode voice lines.
> `z1800.dem` is from **mid-2020 or later**, not 2015.
>
> To date a demo, read the seasonal asset names out of its string tables — Valve
> names event items after the year they shipped, so they are self-dating. The
> protocol pair tells you which decode quirks apply, and nothing about age.

Signon length is 912,640 of 8,964,241 bytes — **10.2% of the file is the embedded
schema**. That figure is the project's whole premise made concrete: the demo
carries its own entity layout, which is why it can be decoded without agreeing
with any particular TF2 build.

### Command stream — **CONFIRMED**

After the header, the file is a flat sequence of `[command header][payload]` with
no index, no table of contents, and no back-pointers. It is strictly
forward-parsed; there is no way to seek to tick N without walking.

Command header, **5 bytes** at demo protocol 3:

| Type | Field |
|---|---|
| `uint8` | Command type |
| `int32` | Tick |

> **Protocol-gated.** Later demo protocols add a `playerSlot` byte to this
> header. At protocol 3 it is absent — confirmed, because assuming 5 bytes walks
> the entire 8.96 MB file and lands exactly on the final command. A 6-byte
> assumption desynchronises immediately. This is exactly the kind of quirk D1
> predicted would need a per-version table.

| Command | Value | Payload | Count in `z1800.dem` |
|---|---|---|---|
| `dem_signon` | 1 | `democmdinfo_t` + 2×`int32` seq + `RawData` | 3 |
| `dem_packet` | 2 | `democmdinfo_t` + 2×`int32` seq + `RawData` | 14,386 |
| `dem_synctick` | 3 | *(none)* | 1 |
| `dem_consolecmd` | 4 | `RawData` | 0 |
| `dem_usercmd` | 5 | `int32` outgoing seq + `RawData` | 0 |
| `dem_datatables` | 6 | `RawData` | 1 |
| `dem_stop` | 7 | *(none)* | 1 (short header, see below) |
| `dem_stringtables` | 8 | `RawData` | 1 |

`RawData` is `int32 size` followed by `size` bytes.

`democmdinfo_t` at protocol 3 is **76 bytes**: one `Split_t` of `int32 flags`
plus six `Vector`s (view origin, view angles, local view angles — each duplicated
for a second POV slot), 3×`float32` each. Confirmed by walking: any other size
desynchronises the stream within a few commands.

### Command ticks are not a single timeline — **CONFIRMED**

Found 2026-08-07 by reading the first text dump of `z1800.dem`, which is exactly what a
readable dump is for.

| Command | Tick |
|---|---|
| `dem_signon` (all three) | 70718 |
| `dem_synctick` | 0 |
| `dem_packet` (first → last) | 6 → 57,549, monotonic |
| `dem_stop` | 57,551 (= header `playback_ticks`) |

**A signon command's tick is not on the demo's timeline.** All three carry an identical
70,718 — larger than the demo's entire declared length — which is the server's own tick
counter at the moment the signon data was written, not a position within the recording.
`dem_synctick` is what establishes the timeline at 0, which is precisely its documented job.

So: do not assume ticks increase monotonically across the whole command stream, and do not
derive playback position from a signon command. Packet ticks alone are monotonic, and they
start at a small non-zero value (6 here) rather than 0.

### Two findings from the walk

**1. `dem_packet` count matches the header exactly.** 14,386 commands against a
declared 14,386 frames. That is the strongest single validation available for the
container layout — an off-by-one in any payload size would drift and never land
on the nose.

**2. Every TF2 demo ends one byte short of a complete `dem_stop` header.** — **CONFIRMED**

The file's last command is `0x07` (`dem_stop`) followed by **three** bytes, not the four a
tick field needs.

This was first recorded as "`z1800.dem` is truncated by one byte." **That was wrong**, and
the correction matters because it changes the rule from "tolerate this damaged file" to
"this is the normal terminator." Three demos from three unrelated servers, in both POV and
SourceTV flavours, all end identically — and the three bytes present decode as the low bytes
of the demo's own tick count:

| demo | trailing bytes | value | header `playback_ticks` |
|---|---|---|---|
| `z1800.dem` | `cf e0 00` | 57,551 | 57,551 |
| ETF2L 12025 (POV) | `32 d7 01` | 120,626 | 120,626 |
| ETF2L 12030 (STV) | `67 d8 01` | 120,935 | 120,935 |

The absent byte is the tick's most significant one, always `0x00` because a tick count never
approaches 2^24. So TF2's writer emits `dem_stop` plus a tick and the file ends one byte
early, every time.

Consequences:

- **Reaching EOF while reading a `dem_stop` header is the normal end of a demo.** A parser
  that demands the full 5-byte header rejects every valid TF2 demo. `demostf/parser`'s
  `RawPacketStream` detecting "incomplete data" as a stop condition is the same
  accommodation.
- **`dem_stop`'s tick equals the header's `playback_ticks`**, which is a free consistency
  check worth asserting.
- This is *not* a salvage fixture, as previously claimed. It is the baseline.

---

## Layer 2 — Network messages inside `dem_packet`

**PARTIAL.** The payload of each `dem_packet` is a bit-packed stream of network
messages, read with the same bit reader as everything else. Message IDs and
layouts are what change between TF2 versions — this is layer 2 in `ROADMAP.md` §1
and the reason the project needs a per-version quirk table rather than one fixed
decoder.

Not yet mined from the sources. **OPEN** for now:

- The message ID width at protocol 24, and the full ID → type mapping.
- Which messages carry the data Phase 1 needs (`svc_PacketEntities`,
  `svc_GameEvent`, `svc_GameEventList`, `svc_CreateStringTable`,
  `svc_UpdateStringTable`, `svc_UserMessage`, `net_Tick`).
- Whether protocol 24 uses varints anywhere on these paths — see below.

### Game events — **DOCUMENTED**, and decodable generically

Game events are defined in resource files and their descriptor list is transmitted
(`svc_GameEventList`), so a demo carries the schema for its own events — the same
self-describing property that makes entity decode possible. Field types are small
and fixed:

| Type | Wire encoding |
|---|---|
| `string` | zero-terminated |
| `bool` | 1 bit |
| `byte` | 8-bit unsigned |
| `short` | 16-bit signed |
| `long` | 32-bit signed |
| `float` | 32-bit |

Event names are at most 32 characters. Kills, captures and round outcomes all arrive
this way, so the readable-dump goal is reachable without reverse-engineering anything.

### User messages — **CONFIRMED not self-describing.** See `RISKS.md` B1

The exception to the project's central premise. VDC is explicit that user messages
"aren't automatically serialized or unserialized" and that client and server code must
both change when one changes. A user message is a name, a size registered in code, and
opaque bytes — the demo does not describe its layout.

This matters because **chat is a user message** (`SayText2`), and chat extraction is a
stated Phase 1 output. Recovering *which* message fired and its raw bytes is always
possible; interpreting the bytes needs a per-version table. Payloads are capped at 255
bytes, which bounds the problem.

Decode game events first. Treat user messages as opaque except the specific few that
matter.

### The varint question — **OPEN, and it has already cost us**

`Tf2DemoSalvage.Core.Primitives.VarInt` exists, is tested, and is
mutation-clean. Whether TF2 at network protocol 24 needs it is still unresolved.

What is established: the encoding is not original to Source. GoldSrc (1998) and
Source (2004) both predate protobuf's 2008 release, and the original netcode used
hand-rolled bit packing. `bf_read::ReadVarInt32` appears in `bitbuf.h` in the
protobuf-adoption era (~2011–12) and is present in the Source 2013 SDK that TF2
builds from — but presence in the SDK is not the same as use on the paths we
decode, and TF2's `NET_Messages` at this vintage are still the older `bf_read`
style rather than protobuf messages.

Nothing in the container layer uses varints: every length in layer 1 is a plain
`int32`. **CONFIRMED** by the walk.

Settle this when layer 2 is mined. Until then, do not build anything else that
assumes varints.

---

## Layer 3 — Entity schema and delta encoding

### What the public sources actually give us

**CONCEPTUAL.** This is the important result of the consolidation pass, and it
cuts against the instinct to keep reading before coding.

VDC's *Networking Entities* page describes the **mechanism** thoroughly — what
SendTables are, how properties are declared, how transmission filtering works —
but it is written for mod authors adding networked entities in C++. **It does not
document the wire format.** There is no bit layout for `svc_PacketEntities`, no
delta-index encoding, no description of how the flattened property list is
ordered.

So: no more reading will produce an authoritative bit-level spec for entity
decode, because one is not publicly published. That part has to come from prior
art (`demostf/parser`) cross-checked against our own bytes. **UNDOCUMENTED.**

### Constants and flags — **DOCUMENTED**

| Limit | Value |
|---|---|
| Max networked entities | 2048 |
| Max networked members per entity | 1024 (array elements count individually) |
| Max serialised data per entity per update | 2 KB |
| Typical snapshot rate | ~20/sec |

### Hard constants from the SDK — **DOCUMENTED (authoritative)**

Read from `source-sdk-2013/src/public/dt_common.h` on 2026-08-07. These are Valve's own
values, not community reconstruction, and they settle several bit widths outright:

| Constant | Value | Why it matters |
|---|---|---|
| `SPROP_NUMFLAGBITS` | 17 | Width of the flags field when a SendProp is transmitted. |
| `MAX_DATATABLE_PROPS` | 4096 | Bounds the property index — 12 bits. |
| `DT_MAX_STRING_BITS` | 9 | String length field width; buffer is 512. |
| `MAX_ARRAY_ELEMENTS` | 2048 | Bounds array length — 11 bits. |

`SendPropType` enum order gives the on-wire type ids: `DPT_Int` 0, `DPT_Float` 1,
`DPT_Vector` 2, `DPT_VectorXY` 3, `DPT_String` 4, `DPT_Array` 5, `DPT_DataTable` 6.

Note a discrepancy worth not papering over: VDC says 1024 networked members per entity,
the SDK says `MAX_DATATABLE_PROPS` 4096. These are probably different limits (per entity
versus per table). Trust the SDK constant for bit widths; treat VDC's figure as prose.

### The full flag set — **DOCUMENTED (authoritative)**

**VDC documents eight of these. There are seventeen**, and the ones VDC omits are
precisely the ones that change how a value is encoded — which is the difference between a
decoder that works and one that silently produces wrong numbers.

| Flag | Bit | Notes |
|---|---|---|
| `SPROP_UNSIGNED` | 1<<0 | |
| `SPROP_COORD` | 1<<1 | |
| `SPROP_NOSCALE` | 1<<2 | |
| `SPROP_ROUNDDOWN` | 1<<3 | |
| `SPROP_ROUNDUP` | 1<<4 | |
| `SPROP_NORMAL` | 1<<5 | |
| `SPROP_EXCLUDE` | 1<<6 | |
| `SPROP_XYZE` | 1<<7 | **Not in VDC.** |
| `SPROP_INSIDEARRAY` | 1<<8 | **Not in VDC.** |
| `SPROP_PROXY_ALWAYS_YES` | 1<<9 | **Not in VDC.** |
| `SPROP_CHANGES_OFTEN` | 1<<10 | Reorders the flattened list. |
| `SPROP_IS_A_VECTOR_ELEM` | 1<<11 | **Not in VDC.** |
| `SPROP_COLLAPSIBLE` | 1<<12 | **Not in VDC.** |
| `SPROP_COORD_MP` | 1<<13 | **Not in VDC.** Multiplayer coord encoding. |
| `SPROP_COORD_MP_LOWPRECISION` | 1<<14 | **Not in VDC.** |
| `SPROP_COORD_MP_INTEGRAL` | 1<<15 | **Not in VDC.** |
| `SPROP_ENCODED_AGAINST_TICKCOUNT` | 1<<16 | **Not in VDC.** |

The three `COORD_MP` variants are the sharp ones: TF2 is a multiplayer game, so player
positions almost certainly use them rather than plain `SPROP_COORD`. A decoder built from
VDC alone would not know they exist and would decode every position wrongly.

`SPROP_` flags affecting decode:

| Flag | Effect on the wire |
|---|---|
| `SPROP_UNSIGNED` | No sign bit. |
| `SPROP_COORD` | World-coordinate compression: 0.0 costs 2 bits, up to 21 bits otherwise. |
| `SPROP_NOSCALE` | Full 32-bit float, no compression. |
| `SPROP_NORMAL` | Normal in [-1, +1], 12 bits. |
| `SPROP_ROUNDDOWN` / `SPROP_ROUNDUP` | Clamp range by one bit unit at one end. |
| `SPROP_CHANGES_OFTEN` | **Reorders the SendTable index.** Not cosmetic — it changes property ordering, so a decoder that ignores it reads the wrong fields. |
| `SPROP_EXCLUDE` | Removes an inherited property from a derived table. |

`SPROP_CHANGES_OFTEN` and `SPROP_EXCLUDE` are the two that will bite: both mean
the flattened property list is not simply "base table then derived table in
declaration order."

### Why the live client rejects old demos — **DOCUMENTED**

VDC confirms the mechanism behind the project's premise: on connect, client and
server exchange class lists, and a client missing a server class refuses with
*"Client missing DT class"*. The July 2023 `DT_ObjectDispenser` failure is the
same family — the client validates incoming tables against its own compiled-in
definitions.

A standalone parser never performs that check. It has no compiled-in schema to
disagree with.

---

## What this changes about the plan

1. **Container parsing is ready to implement.** Layer 1 is fully confirmed
   against real bytes. No further research needed.
2. **Stop adding primitives speculatively.** Varint is the cautionary case: built,
   tested, fuzzed, still unproven as necessary. The next primitive should be
   pulled by a decoder that demonstrably needs it.
3. **Entity decode will not be settled by reading.** No public bit-level spec
   exists. Plan for prior-art cross-checking plus byte-level experiment against
   `z1800.dem`, and budget accordingly.
4. **The one-byte shortfall is normal, not a salvage case.** Corrected after two more
   demos showed the identical ending. Parsers must accept it as the terminator.
5. **The corpus is modern-era only, and D5 needs rewriting.** `z1800.dem` was
   assumed to be a rare ~2015 specimen; it is from 2020 or later. Combined with
   modern demos being freely obtainable from demos.tf, the position is now: the
   *modern* corpus is easy and the *historical* corpus is empty, where D5 treated
   both as one scarcity problem. Nothing about the schema-driven design changes —
   it is still the hedge — but the corpus plan should say what it actually means.
6. **Date demos by their assets, never their protocol numbers.** Cheap, exact, and
   it would have caught this immediately.

## Still to mine

- demboyz `DemFormat.md`: the `dem_usercmd`, `dem_datatables`, and
  `dem_stringtables` payload structures were not detailed in the section fetched.
- `source-sdk-2013/src/public/dt_send.h` and `src/public/tier1/bitbuf.h` — the latter
  should settle the coordinate and normal encodings exactly, including the `COORD_MP`
  variants above.
- **TF2 patch notes are public back to 2007** (Team Fortress Wiki update history). Useful
  for correlating protocol or schema changes to dates once we know which builds matter —
  the reverse of the mistake made earlier, where a date was inferred from a protocol
  number.
- **What the SDK does *not* contain:** `protocol.h` and `netmessages.h` are absent from
  source-sdk-2013 (confirmed against its file tree, 2026-08-07). Only `dt_common.h`,
  `dt_send.h` and `tier1/bitbuf.h` are shipped. So the SDK settles the entity-schema
  layer and contributes nothing to layer 2's message ids — those still need prior art.
  This bounds the SDK's usefulness precisely rather than leaving it hopeful.
- VDC *Networking Events & Messages* — game events and user messages.
- VDC *Networking Entities* §"Mismatched Class Tables" — directly relevant to the
  client-rejection story.
- `demostf/parser` message and entity modules, for the undocumented layer 3.
