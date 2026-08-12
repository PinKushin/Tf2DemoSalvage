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

### `dem_usercmd` payload — the recording player's input

Bit-packed, delta-coded against a **default-constructed `CUserCmd`** rather than against the
previous command (`CInput::EncodeUserCmdToBuffer` constructs `nullcmd` on every call), so each one
decodes independently. Every field is a presence bit followed by the field when set.

| Order | Field | Width when present | Value when absent |
|---|---|---|---|
| 1 | `command_number` | 32 | **1**, not 0 |
| 2 | `tick_count` | 32 | **1**, not 0 |
| 3–5 | `viewangles[0..2]` | 32 each, IEEE float | 0 |
| 6–8 | `forwardmove`, `sidemove`, `upmove` | 32 each, IEEE float | 0 |
| 9 | `buttons` | 32, `IN_*` flags | 0 |
| 10 | `impulse` | 8 | 0 |
| 11 | `weaponselect` | 11 (`MAX_EDICT_BITS`) | 0 |
| 11a | `weaponsubtype` | 6 (`WEAPON_SUBTYPE_BITS`) | 0 |
| 12 | `mousedx` | 16, **signed** | 0 |
| 13 | `mousedy` | 16, **signed** | 0 |

Field 11a's presence bit exists **only when field 11's is set** — it is the one conditional
presence bit in the layout.

`entitygroundcontact` follows in the SDK but is `#if`-guarded out for TF2; the guards differ
between the read and write halves, which is recorded in
[findings/09](findings/09-valve-implementation.md). `random_seed` is never sent — the reader
derives it from `command_number` via MD5.

**The trailing bits to the byte boundary are not zero and are not derivable.** They are
stale bits of the previous command, preserved by `bf_write`'s read-modify-write tail; 99.8% of commands end
three bits short of a byte and those bits take every value from 0 to 7. A byte-exact rewrite must
carry them. See [findings/01](findings/01-container.md).

`dem_consolecmd`'s payload is a single null-terminated string.

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

### Message ids — **DOCUMENTED** (from prior art, 2026-08-07)

Taken from `tf-demo-parser`'s `MessageType` enum. Not available from Valve: `protocol.h` and
`netmessages.h` are absent from source-sdk-2013, so prior art is the only route (RISKS B3).

**The type field is 6 bits**, matching Source's `NETMSG_TYPE_BITS`. Each `dem_packet` payload
is a bit stream of `[6-bit type][message body]` repeated.

| Id | Message | Id | Message |
|---|---|---|---|
| 0 | `net_NOP` (Empty) | 18 | `svc_SetView` |
| 2 | `svc_File` | 19 | `svc_FixAngle` |
| 3 | `net_Tick` | 21 | `svc_BSPDecal` |
| 4 | `net_StringCmd` | 23 | `svc_UserMessage` |
| 5 | `net_SetConVar` | 24 | `svc_EntityMessage` |
| 6 | `net_SignonState` | 25 | `svc_GameEvent` |
| 7 | `svc_Print` | 26 | `svc_PacketEntities` |
| 8 | `svc_ServerInfo` | 27 | `svc_TempEntities` |
| 10 | `svc_ClassInfo` | 28 | `svc_Prefetch` |
| 11 | `svc_SetPause` | 29 | `svc_Menu` |
| 12 | `svc_CreateStringTable` | 30 | `svc_GameEventList` |
| 13 | `svc_UpdateStringTable` | 31 | `svc_GetCvarValue` |
| 14 | `svc_VoiceInit` | 32 | `svc_CmdKeyValues` |
| 15 | `svc_VoiceData` | | |
| 17 | `svc_Sounds` | | |

Ids 1, 9, 16, 20 and 22 are unused at this protocol. A parser should reject them rather than
guess.

**The structural consequence, and it shapes the whole implementation:** messages are *not*
length-prefixed. There is no way to skip one you cannot parse — the next message begins
wherever the previous one's body ended, so the stream can only be walked by decoding every
message well enough to know its width. Adding message support is therefore strictly
incremental: until a type is implemented, everything after its first occurrence in a packet
is unreachable.

### `net_Tick` (id 3) — **CONFIRMED**

64-bit body: a 32-bit tick, then two 16-bit values (frame time and its standard deviation).
Layout from `tf-demo-parser`, verified against all three corpus demos.

Source scales the frame-time fields by 100,000. `tf-demo-parser` keeps them raw and applies no
scale, so that constant is engine convention rather than something verified here — our decoder
exposes both the raw values and the converted seconds so a caller need not accept it.

### The container clock and the server clock are different — **CONFIRMED**

`net_Tick`'s tick does **not** equal the tick in the enclosing `dem_packet` command header.
Measured over the first 200 packets of each corpus demo:

| Demo | Recorded | Offset (server − demo) | Spread |
|---|---|---|---|
| `etf2l-12025-pov` | client-side | 12,636 – 12,640 | **4** |
| `etf2l-12030-stv` | server-side | 25,662 | **0** |
| `z1800` | server-side | 12,728 | **0** |

The container's tick counts from the start of the recording; `net_Tick` carries the server's
own absolute tick. Same reason `dem_signon` commands sit at implausible ticks (`z1800`'s are
70,718 against a 57,551-tick demo).

**The spread is the interesting part.** SourceTV demos are recorded server-side, so the offset
is *exactly* constant across hundreds of packets. The point-of-view demo is recorded
client-side, so its offset jitters by a few ticks as the client's clock drifts against the
server's with latency. Expect a constant offset from STV and a small varying one from POV; a
large or growing spread means the decoder has lost the stream, not that the clocks disagree.

That constancy is also the strongest cross-check available at this layer: two tick fields
encoded completely differently — a little-endian int32 in the container versus 32 bits read
from a bit stream at an arbitrary offset — agreeing to the tick, every packet.

### `svc_ServerInfo` (id 8) — **CONFIRMED**

No length prefix, and it is the first message in a SourceTV demo's signon stream, so it gates
everything behind it: the string tables, the class list, the game event definitions and the
entity schema. Every field width has to be exact or none of that is reachable.

| Field | Width | Notes |
|---|---|---|
| network protocol | 16 | Matches the demo header's. |
| server count | 32 | Spawn counter, bumped on map change. |
| is SourceTV | 1 | |
| is dedicated | 1 | |
| map CRC | 32 | |
| max classes | 16 | **Load-bearing:** entity class ids are sized from this. |
| map hash | 16 bytes | 4 bytes at protocol ≤ 17. |
| player slot | 8 | |
| max players | 8 | |
| interval per tick | 32 (float) | 0.015 for TF2's 66.67 tick rate. |
| platform | 8 (char) | `l` or `w`. |
| game, map, skybox, server name | NUL-terminated strings | |
| is replay | 1 | Protocol ≥ 16 only. |

Verified against all three corpus demos, and the cross-checks are strong because they come
through unrelated paths:

| Demo | Map (header vs ServerInfo) | Skybox | STV flag | Tick rate |
|---|---|---|---|---|
| `etf2l-12025-pov` | `cp_process_final` = `cp_process_final` | `sky_trainyard_01` | false | 66.67 |
| `etf2l-12030-stv` | `cp_process_final` = `cp_process_final` | `sky_trainyard_01` | true | 66.67 |
| `z1800` | `koth_harvest_final` = `koth_harvest_final` | `sky_harvest_01` | true | 66.67 |

The map name agreeing with a fixed-offset header field is the headline check, but the skybox is
the more persuasive one: `sky_harvest_01` for Harvest and `sky_trainyard_01` for Process are
map-appropriate values nothing but a correctly aligned read would produce. The SourceTV flag
being false on exactly the point-of-view demo is a third independent agreement.

The protocol ≤ 17 and ≤ 15 branches are implemented from the reference parser and have **no
specimen behind them** — the corpus is entirely protocol 24. They are pinned by tests so the
intended behaviour is fixed, but they are not verified.

**Relevant to D9's map resolver:** this message carries both the map name and a 16-byte map
hash, which is exactly what is needed to confirm a downloaded community map is the same version
the demo was recorded on. Same name, different version is the main hazard there.

### Trivially decoded, and useful for reaching further

`svc_Print` (7) is one string. `net_StringCmd` (4) is one string. `net_SetConVar` (5) is an
8-bit count followed by that many name/value string pairs. All three appear early in a
point-of-view demo's signon — its stream opens with `svc_Print`, not `svc_ServerInfo` — so
implementing them is what makes ServerInfo reachable in POV demos at all.

### `svc_PacketEntities` (id 26) — **CONFIRMED**, header and body

| Field | Bits |
|---|---|
| max entries | 11 |
| is delta | 1 |
| delta-from tick | 32, only when delta |
| baseline index | 1 |
| updated entries | 11 |
| body length | 20 |
| update baseline | 1 |
| body | *length* bits |

The body carries entity index deltas, update types and property values, and all three now
decode. The message's explicit bit length still isolates the body first, which contains the
damage: a malformed body cannot read past its declared length into whatever follows.

**Both index encodings are `previous + delta + 1`** — entity indices and property indices
alike. Dropping the `+ 1` still yields monotonic indices that still address real properties, so
a demo decodes into a coherent-looking match that is quietly wrong. Any fixture for this needs
at least two consecutive items; with one, correct and broken predict the same observation.

An entering entity carries a class id at `floor(log2(classCount)) + 1` bits followed by a
10-bit serial number. Note **floor, not ceil** — the two agree on exact powers of two and on
small counts, so a fixture with a handful of classes cannot tell them apart. A real demo's 362
classes needs 9 bits, and the ceiling form says 10.

A delta carries no class id, so the class must be remembered from whichever snapshot the entity
entered on. Removals are listed after the updates, and **only on a delta** — reading that list
unconditionally consumes bits belonging to whatever follows a full snapshot.

Entity indices use **UBitVar**: a 2-bit selector choosing a 4, 8, 12 or 32-bit payload. A
different trade from a varint — bit-granular with four fixed widths rather than byte-granular
and unbounded — so an index delta of 3 costs six bits against a varint's eight. Update types
are a 2-bit discriminant: delta 0, leave 1, enter 2, delete 3.

Measured across the corpus:

| Demo | First reachable snapshot | Later snapshots |
|---|---|---|
| `etf2l-12030-stv` | 824 of 1,241 entities, 312,036 bits | ~17 entities, ~3,300 bits |
| `z1800` | 545 of 799 entities, 258,542 bits | ~38 entities, ~3,000 bits |

**z1800's first snapshot is 258,542 bits = 32,318 bytes**, against a first `dem_packet` payload
of 32,435 bytes seen in the text dump — the difference being `net_Tick` and the other messages
sharing that packet. Two unrelated measurements agreeing.

Coverage of gameplay packets jumped from roughly 2% of payload bits to **54.8%** (POV) and
**94.0%** (SourceTV) purely from no longer stopping here.

One correction worth recording: an early version of the corpus test compared `delta from` to
the *container's* tick and failed. Those are different clocks — `delta from` is on the server's
clock, like `net_Tick`, and the two differ by a constant offset. The test was wrong, not the
parser.

### Property value encodings — **CONFIRMED**

Implemented and round-trip tested: signed and unsigned integers (sign-extended from the
property's own width), `SPROP_NOSCALE` floats (bit-exact by design), range-encoded floats,
`SPROP_NORMAL`, vectors, `VectorXY`, and length-prefixed strings.

Note the string convention differs from the message layer: entity strings carry a **9-bit
length prefix**, while network messages use NUL termination. Confusing them desynchronises the
entity rather than failing.

**The coordinate encodings are implemented.** They were the last to land and threw rather than
guessed until then, because `m_vecOrigin` and `m_vecPunchAngle` use `SPROP_COORD_MP` — a flag
the SDK documents and VDC does not mention at all — and a wrong coordinate is a plausible
position in the one field a viewer exists to draw.

| Field | Bits |
|---|---|
| in bounds (not on `SPROP_COORD`) | 1 |
| integer present | 1 |
| fraction present (`SPROP_COORD` only) | 1 |
| sign | 1 |
| integer, minus one | 11 in bounds, else 14 |
| fraction | 5, or 3 at low precision |

Two details carry the risk. **The integer is stored minus one**, because a present integer is
never zero — that case is carried by the presence bit. **The in-bounds bit selects the integer
width.** Both decode to a plausible position when wrong.

The integral variant is not a subset of the others: it has no fraction bits, and reads the sign
only when an integer is present, where the non-integral variants always read it.

**Flag 32 is overloaded.** It is `SPROP_NORMAL` on a float and `SPROP_VARINT` on an integer.
Nothing in the schema disambiguates it but the property's own type, and reading a varint as a
fixed-width field consumes the wrong number of bits.

Verified against real demos rather than fixtures alone: `z1800`'s opening snapshot yields 545
entities and 7,442 property values, with 278 player origins spanning x −1480..8864 and
z −1..952 — a plausible extent for `koth_harvest_final`.

Still **OPEN**:

- Which messages carry the data Phase 1 needs (`svc_PacketEntities`,
  `svc_GameEvent`, `svc_GameEventList`, `svc_CreateStringTable`,
  `svc_UpdateStringTable`, `svc_UserMessage`, `net_Tick`).
- Whether protocol 24 uses varints anywhere on these paths — see below.

### Messages that only need stepping over — **CONFIRMED**

Ten types are decoded far enough to consume their exact width, which is all the reader needs to
continue. Each is length-prefixed or fixed-width, and every layout came from the reference
implementation rather than from experiment.

| Message | Layout |
|---|---|
| `svc_Prefetch` | index, 14 bits (13 below protocol 23) |
| `svc_SetView` | entity index, 11 bits |
| `svc_SetPause` | 1 bit |
| `svc_SignOnState` | state (8), spawn count (32) |
| `svc_VoiceInit` | codec string, quality (8), sample rate (16) **only when quality is 255** |
| `svc_VoiceData` | client (8), proximity (8), length (16), payload |
| `svc_Sounds` | reliable (1); if reliable, length (8); else count (8) then length (16); payload |
| `svc_TempEntities` | count (8), length (varint above protocol 23, else 17), payload |
| `svc_UserMessage` | type (8), length (11), payload |
| `svc_EntityMessage` | entity index (11), class id (9), length (11), payload |

Two conditional shapes are worth restating, because reading the wrong one consumes the wrong
number of bits and loses everything behind it. `svc_Sounds`'s reliable flag changes **two**
fields at once: a reliable message implies a single sound and shrinks its length field from 16
bits to 8. `svc_VoiceInit` transmits a sample rate only at quality 255; otherwise it is implied
by the codec name, 22050 for celt and 11025 for anything else.

**Implementing these is what made the entity stream decodable end to end.** Messages have no
length prefix, so each unimplemented type discarded the remainder of its packet — including any
`svc_PacketEntities` behind it. See `RISKS.md` B13.

### `SayText2` chat — **CONFIRMED**

Chat arrives inside `svc_UserMessage` as payload type **4**. Two shapes share the message and
nothing flags which is present:

| Shape | Body after the two header bytes |
|---|---|
| Player message | channel key (`TF_Chat_All`, `TF_Chat_AllDead`, …), sender, text |
| Server or plugin | text only, starting with a colour code |

**The shape is decided by looking at a byte**: a value in 1..8 there is a colour code, so the
simplified form is present. Reading the simplified form as the full one takes the message itself
as the channel key and loses it. Both occur in every corpus demo — server plugin lines are the
simplified form.

**Chat text carries inline colour codes** that must come out. Two kinds: control characters up
to 8, which stand alone, and `` introducing a **six-digit hex colour**. Stripping only the
marker leaves six stray characters mid-sentence, which reads as corruption rather than as a
colour.

The body sits behind an 11-bit length, so a chat line that fails to parse costs one line and
cannot desynchronise the packet — this decoder returns null rather than throwing, and rather
than emitting a blank line that would read as somebody saying nothing.

### Which event fields name a player — **PARTIAL**

Resolving a `user_id` to a name needs to know which fields hold one, and the answer is not
"every small integer". Established by allowlist rather than by inference, because inference was
tried and produced wrong names on real data:

| Resolves to a player | Does not |
|---|---|
| `userid`, `attacker`, `assister`, `patient`, `healer`, `player` | everything else |

Two failures drove that. `damageamount=14` rendered as a player because 14 damage collided with
user id 14. And `inflictor_entindex` resolved to a player when an inflictor is usually a weapon
or projectile entity. Neither was caught by a fallback for unknown ids — in both cases the value
was perfectly valid, it simply was not a player reference.

**Entity-index fields are deliberately left raw.** They do address entities, but most entities
an event names are not players, and resolving them selectively would need an entity-to-class map
this section does not have.

**An absent player is a large sentinel**, not a null or a negative: an unassisted kill sends an
`assister` at or above 16384. Printed as `none`.

Marked PARTIAL because the list is drawn from the events these seven demos fire. A field naming
a player in an event none of them contains is not in it yet, and the failure mode is mild — an
unresolved number rather than a wrong name.

### The `userinfo` player record — **CONFIRMED**

A fixed 132-byte C struct written straight to the wire, carried as the user data of each
`userinfo` string table entry. Position matters more than content: a field read at the wrong
offset still yields text, just the wrong text.

| Offset | Size | Field |
|---|---|---|
| 0 | 32 | name, NUL-padded |
| 32 | 4 | `user_id`, little-endian |
| 36 | 32 | Steam id, rendered (`[U:1:1234567]`, or `BOT`) |
| 68 | 8 | extra, friends id |
| 76 | 32 | friends name — zero in every demo measured |
| 108 | 1 | is fake player |
| 109 | 1 | is HLTV |
| 110 | 2 | is replay, padding |
| 112 | 20 | custom file hashes, files downloaded |

**Two identifiers, and they are not the same number.** Game events carry `user_id`; entities are
addressed by index. The entity index is the string table **entry's name**, not a field in the
record — so this table is the join between the event log and the entity stream. Using one where
the other belongs attributes events to the wrong player and nothing fails, because both are
small integers and both are usually valid. Confirmed distinct on real demos: entity 3 is user 5,
entity 7 is user 12.

Two details that bite. The name field's NUL is padding rather than a terminator, so a name
filling all 32 bytes has none at all and a reader scanning for one drops its last character. And
`user_id` is **not** byte-swapped, unlike some Source fields — reading it big-endian turns user 1
into 16,777,216.

Verified across all seven corpus demos: 13 slots each, names and Steam ids well-formed, UTF-8
names intact.

### Every message type is now decoded — **CONFIRMED**

All sixteen defined types are implemented, so no message discards the remainder of its packet.
A test walks the enum and fails if a type is ever added without a case, which is the defect
behind `RISKS.md` B13 turned into a guard.

The last six, added together:

| Message | Layout |
|---|---|
| `svc_FixAngle` | relative flag (1), three 16-bit angles |
| `svc_File` | transfer id (32), name, requested flag (1) |
| `svc_GetCvarValue` | cookie (32), name |
| `svc_Menu` | kind (16), length (16), payload **in bytes** |
| `svc_CmdKeyValues` | length (32), payload **in bytes** |
| `svc_BspDecal` | three presence bits, then `SPROP_COORD` per present axis, then 3×16 + 1 |

Two things in that table bite. `svc_Menu` and `svc_CmdKeyValues` state their length in **bytes**
while every other length in this format is in bits — reading one as bits consumes an eighth of
the payload and leaves the rest to be misread as messages. And `svc_BspDecal` is the only
variable-width message here: its coordinates use the same `SPROP_COORD` encoding entity origins
do, so the decoder is shared rather than reimplemented.

### How much varies by protocol version — **CONFIRMED**

This is the project's central bet, so it is worth stating as a measurement rather than a hope.
Reading every protocol-conditional branch in `demostf/parser` — a parser covering TF2's history
— the **entire message layer varies in four places**:

| What | Rule |
|---|---|
| `svc_Prefetch` index width | 14 bits above protocol 22, else 13 |
| `svc_ServerInfo` map hash | 16 bytes above protocol 17, else a 4-byte CRC |
| `svc_ServerInfo` replay flag | present above protocol 15 |
| `svc_CreateStringTable` and `svc_TempEntities` length | varint above protocol 23, else fixed |

**All four are implemented here.** The container layer — the demo header and command stream —
has **zero** version conditionals in that parser at all.

> **This section is no longer the whole story, and the correction matters more than the original
> claim.** "Four places" was measured by reading `demostf/parser`, which is a modern parser: it
> hardcodes six-bit message types and the current property numbering, so it **cannot read a
> protocol-15 demo at all**. Reading it could only ever enumerate the differences it already
> handled.
>
> Decoding a real 2009 demo found three more — the message type field width (RISKS B17), the
> `SendPropType` renumbering (B18), and the string table compression flag (D20) — of which the
> first two appear in *neither* `demostf/parser` nor Valve's own `proto_version.h`.
>
> The bet still holds: the list is short, and the container really is invariant. But the honest
> count is around eight, not four, and the lesson is that **a second implementation can only tell
> you about the eras it was built for.** See `docs/TIMELINE.md` for the current list with
> evidence grades.

That is the strongest evidence so far for `ROADMAP.md` §1: the parts a parser must hardcode
change rarely, and the part that changes constantly (the entity schema) travels inside every
demo. It does not prove an old demo will decode — see `DECISIONS.md` D5, the corpus has no
pre-2020 file — but it says what would have to be wrong for one to fail, and the list is short.

### `svc_ClassInfo` (id 10) — **CONFIRMED**

`count` (16), a `create on client` flag (1), then — only when that flag is clear — `count`
entries of `class id` (log2(count)+1 bits), class name, table name.

Small message, outsized importance: **an entity's class id is read at a width derived from
`count`, not transmitted.** A wrong count would not fail here; it would misread every entity in
the demo.

The `create on client` flag asks the client to build the list from its own compiled-in classes.
A standalone parser cannot honour that — it is exactly the coupling this project exists to
avoid — so if a demo ever sets it, the class list must come from `dem_datatables` instead. None
of the corpus does.

### String tables — **CONFIRMED**

`svc_CreateStringTable` (12): name, `max entries` (16), `entry count` (log2(maxEntries)+1),
then a body length, a fixed-user-data flag, a compressed flag, and the body.

**The length is a varint at protocol > 23**, a fixed 20-bit field below it. This is the first
confirmed use of varint encoding in TF2 demos, settling a question left open since that
primitive was written.

Entries are encoded against a **rolling history of the last 32 strings**: an entry may copy the
first *n* bytes of a recent string and transmit only its differing tail. That makes the decoder
stateful within a table — one wrong entry corrupts every later entry that back-references it,
rather than failing where the mistake was.

Per entry: an `index follows` bit (else an explicit index), a `has text` bit, and a `has user
data` bit. Fixed-size tables state their payload width in **bits**; variable-size ones state a
**byte** count. Reading the wrong unit desynchronises the table rather than throwing.

`svc_UpdateStringTable` (13): table id (5), a changed-count flag (1, else a 16-bit count), a
20-bit length, then entries in the same encoding.

What the corpus contains — 20 tables per demo, 15 of which decode:

| Table | Contents |
|---|---|
| `downloadables` | **`maps\cp_process_final.bsp`** — the map file the demo needs. |
| `decalprecache` | 133 entries, `decals/concrete/shot1_subrect` and similar. |
| `userinfo` | 33 entries; player identity lives in the per-entry user data. |
| `Materials`, `lightstyles`, `VguiScreen`, … | Precache data. |

The five that do not decode — `modelprecache`, `soundprecache`, `instancebaseline`,
`ParticleEffectNames`, `Scenes` — are **LZSS-compressed**. They are skipped cleanly via the
length prefix, so they cost those tables and nothing else. Decompression is not implemented.

**Directly relevant to D9's map resolver:** `downloadables` names the map file explicitly, and
`svc_ServerInfo` carries a 16-byte hash to confirm the version. A resolver need not infer
anything from the map name.

### The signon stream is a dependency chain — **CONFIRMED**

Nothing in layer 2 is length-prefixed except game events and string tables, so signon must be
decoded strictly in order. Each message implemented unlocks the next, and the ordering is not
the same in every demo:

| Demo kind | Signon opens with |
|---|---|
| SourceTV | `svc_ServerInfo` |
| Point of view | `svc_Print` |

That single difference meant ServerInfo was unreachable in the POV demo until `svc_Print`
existed. Measured progress on the first signon command, which is ~110–130 KB in every demo:

| After implementing | Messages read | Stops at |
|---|---|---|
| net_Tick only | 0 | `ServerInfo` |
| ServerInfo, Print, StringCmd, SetConVar | 2 | `CreateStringTable` |
| string tables | ~20 | `ClassInfo` |
| ClassInfo | **23–24** | `SignonState` |

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

### `dem_datatables` — **CONFIRMED**

A demo *command*, not a network message, and the payload the whole project rests on. One
continuous bit stream with no per-table length, so a single wrong field width turns every
later table into noise.

```
repeat while a 1 bit is read:
    needs_decoder   1 bit
    name            NUL-terminated string
    prop_count      10 bits
    per property:
        type        5 bits      (SendPropType)
        name        NUL-terminated string
        flags       16 bits     <- SPROP_NUMFLAGBITS_NETWORKED, not 17
        then exactly one of:
            DataTable, or the exclude flag set -> referenced table name (string)
            Array                              -> element count (10 bits)
            anything else                      -> low (f32), high (f32), bits (7)
then:
    class_count     16 bits
    per class:      id (16 bits), class name (string), table name (string)
```

**The flags width is the trap**, and the SDK names it misleadingly:
`SPROP_NUMFLAGBITS_NETWORKED` is 16 and is what goes on the wire;
`SPROP_NUMFLAGBITS` is 17 and counts a flag the SDK marks server-side only. The 17-bit
constant has the more prominent name.

Measured across the corpus:

| Demo | Tables | Classes | Properties | changes-often | exclusions |
|---|---|---|---|---|---|
| `etf2l-12025-pov` | 517 | 362 | 5,441 | 55 | 25 |
| `etf2l-12030-stv` | 517 | 362 | 5,441 | 55 | 25 |
| `z1800` | 517 | 362 | **5,442** | 55 | 25 |

`z1800` having one property more is expected — it is a different TF2 build, and it is exactly
the per-version variation the schema-driven design exists to absorb.

Cross-check: the trailing class count is **362 in all three demos, matching the `MaxClasses`
that `svc_ServerInfo` reports** through a completely different path in the signon stream.

**Not every table is `DT_`-prefixed.** Source auto-generates a table per array property:
`_ST_<prop>` for the element send table and `_LPT_<prop>` for its length proxy, plus tables
named directly after the property. Seeing `_LPT_m_AnimOverlay_15` come out is itself evidence
of a correctly aligned read.

### Flattening — **CONFIRMED**

Entity deltas address properties by *position* in a flattened list, so this ordering is the
contract. Three rules produce it, each easy to get backwards:

1. **Exclusions are gathered first**, over the whole reachable hierarchy, before any property
   is emitted. A table can exclude a property from a table it has not referenced yet, so
   resolving them lazily applies some too late.
2. **`SPROP_COLLAPSIBLE` children inline at the point of reference. Non-collapsible children
   do not** — their whole list is appended *before* the referencing table's own properties.
3. **`SPROP_CHANGES_OFTEN` properties move to the front by a stable partition**, not a sort.
   Relative order within each group is part of the contract; an unstable sort satisfies
   "changes-often first" while corrupting the addressing.

Properties carrying `SPROP_INSIDEARRAY` are array element templates. They are attached to the
array that follows them, never emitted in their own right.

Measured on the corpus:

| Demo | `CTFPlayer` flattens to | changes-often | contributing tables | largest class |
|---|---|---|---|---|
| `etf2l-12025-pov` | 740 | 20 | 36 | 1,227 |
| `etf2l-12030-stv` | 740 | 20 | 36 | 1,227 |
| `z1800` | **741** | 20 | 36 | 1,227 |

Three checks, all on real data rather than fixtures: the changes-often properties form an
unbroken prefix for every one of the 362 classes in every demo; no class exceeds the SDK's
`MAX_DATATABLE_PROPS` of 4,096; and `z1800` flattening to one property more is the same
single-property build difference seen in the raw table counts, carried through consistently.

The first flattened properties of `CTFPlayer` are `DT_Local.m_flDucktime`,
`DT_Local.m_flFallVelocity`, `DT_Local.m_vecPunchAngle` — plausibly the things that change
most often for a moving player, which is what the flag is for.

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
| `SPROP_NUMFLAGBITS_NETWORKED` | 16 | **Width of the flags field on the wire.** |
| `SPROP_NUMFLAGBITS` | 17 | All flags including the one that is *not* networked. Not a wire width. |
| `MAX_DATATABLE_PROPS` | 4096 | Bounds the property index — 12 bits. |
| `DT_MAX_STRING_BITS` | 9 | String length field width; buffer is 512. |
| `MAX_ARRAY_ELEMENTS` | 2048 | Bounds array length — 11 bits. |

**Corrected 2026-08-08.** An earlier version of this table gave `SPROP_NUMFLAGBITS` (17) as the
wire width for the flags field. That is wrong, and it would have desynchronised every SendTable:
the SDK defines `SPROP_NUMFLAGBITS_NETWORKED` (16) as "the ones which are networked", with the
17th bit — `SPROP_ENCODED_AGAINST_TICKCOUNT` — marked server-side only. 17 is the more
prominently named constant, which is the trap.

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
| `SPROP_ENCODED_AGAINST_TICKCOUNT` | 1<<16 | **Not in VDC, and never transmitted** — "server side only" per the SDK. It is the 17th flag, which is exactly why the networked width is 16. |

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


## Every message reports itself — **CONFIRMED**, 2026-08-10

Phase 1's finish line for the message layer. **No message type in the corpus renders anonymously
any more.** `SkippedMessage` still exists, and should: it is what an unrecognised type falls back
to, and a trace that hid one would describe a healthier file than the one on disk. But nothing in
eight demos across two protocol eras reaches it.

A corpus test enforces this rather than a note claiming it: `NoMessageIsAnonymous` scans the
rendered trace for the shape a skip produces — a bare `svc_name bits N` — and fails naming the
demo and the type.

### What "reported" means, and what it does not

Three tiers, and the distinction is deliberate:

| Tier | Types | Rationale |
|---|---|---|
| Fully decoded | entities, game events, string tables, chat, `svc_Sounds`, `svc_TempEntities`, most of `svc_UserMessage`, `net_Tick`, `svc_ServerInfo`, `svc_ClassInfo`, `svc_Print`, `svc_SetConVar`, `svc_StringCmd` | the content is the point |
| Header reported | `svc_EntityMessage`, `svc_VoiceData`, user message types with no known layout | the body's layout is defined by the receiving class, or is a codec payload; neither can be decoded generically |
| Fully reported | `svc_Prefetch`, `svc_FixAngle`, `svc_SetView`, `net_SignonState`, `svc_BspDecal`, `svc_VoiceInit`, `svc_File`, `svc_GetCvarValue` | small enough that the fields *are* the message |

**The middle tier moved twice, and both moves were earned rather than reclassified.**
`svc_Sounds` and `svc_TempEntities` sat here on the grounds that `demostf/parser` declines to
decode them — which was a fact about that parser's priorities, not about the format. Both are now
decoded: temp entities off demostf's own `tempentities.rs`, sounds off Valve's
`public/soundinfo.h`, since no second implementation of that one exists to check against.

**`svc_VoiceData` has since moved too, and the tier table above understates it.** "Voice data is a
codec payload and decoding it is a different project" was true when written and is no longer.
Its *framing* is fully decoded — the Steam-codec wrapper resolves to a steamID plus typed
sub-packets at 1452 of 1452 payloads exactly, and CELT/Speex turn out to carry no framing at all —
and the codec payloads behind it now decode to real PCM for two of the three codecs: every Opus
chunk (3969 of 3969) and every Speex frame (272 of 272). CELT alone still refuses most frames, for
reasons that are neither framing nor mode parameters; see `RISKS.md` B33 and
`findings/02-net-messages.md`.

What genuinely cannot be moved by effort is `svc_EntityMessage`: its body is laid out by the
receiving entity's class, and there is no generic reading of it.

`CorpusCodecCoverageTests` measures the split in bits rather than asserting this table is true,
and it is deliberately not a gate: a gate would be set to today's number and then defended.

### The 2009 demo, message by message

```
11000  net_tick              10998  svc_packetentities     5739  svc_empty
  231  svc_sounds              106  svc_usermessage          70  svc_gameevent
   53  svc_tempentities         32  svc_prefetch             29  svc_fixangle
   21  svc_entitymessage        16  svc_createstringtable    15  svc_updatestringtable
   15  svc_stufftext             4  svc_bspdecal
```

Every line names a type. Before this pass, 375 of those were anonymous.
