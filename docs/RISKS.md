# Anticipated blockers

Written up front, deliberately, because every expensive surprise so far was cheap
to find and would have been costly to hit mid-implementation: the demo's real
date, the `dem_stop` terminator, ETF2L's link rot, FACES-vs-BRUSHES.

Ordered by *when it bites*, not by severity. Same confidence tags as `SPEC.md`.

---

## B1. User messages are not self-describing — **CONFIRMED**, and it threatens a Phase 1 goal

**This is the most important item here.** `ROADMAP.md` §3 lists chat extraction as
Phase 1 output. Chat arrives as a *user message* (`SayText2`), and VDC is explicit
that user messages, unlike game events, carry no schema:

> user messages aren't automatically serialized or unserialized, that has to be
> done manually … both the client and server code must be updated whenever a user
> message changes

So a user message is a name, a payload size registered in code, and raw bytes. The
demo does **not** describe their layout the way it describes entities. This is the
one place where the project's central premise — the file is self-describing —
does not hold.

Consequences:

- We can always recover *which* user message fired and its raw bytes.
- Interpreting the bytes requires a per-TF2-version table of user message layouts,
  reverse-engineered or taken from prior art. That is exactly the "small table of
  documented quirks per version" D1 predicted, but for a layer we assumed would be
  generic.
- Payloads are capped at 255 bytes, which bounds the problem usefully.

**Mitigation:** treat user messages as opaque by default and decode only the
handful that matter (`SayText2` for chat, and whatever the viewer needs). Do not
attempt a general user-message decoder. Record each decoded layout with the
protocol range it was verified against.

## B2. Game events *are* self-describing — **DOCUMENTED**, and this is good news

Counterweight to B1. Game events are defined in resource files and the descriptor
list is transmitted (`svc_GameEventList`), so a demo carries the schema for its own
events. Field types are documented and small:

| Type | Wire |
|---|---|
| string | zero-terminated |
| bool | 1 bit |
| byte | 8-bit unsigned |
| short | 16-bit signed |
| long | 32-bit signed |
| float | 32-bit |

Event names are at most 32 characters. So kills, captures, round outcomes — the
things that make a readable dump interesting — are decodable generically. Do these
before touching user messages.

## B3. Layer 2 message IDs — **RESOLVED 2026-08-07**

We do not yet know the message-ID bit width at network protocol 24, nor the full
ID→type mapping. Everything above depends on it. No public authoritative source
found; `tf-demo-parser` is the reference.

Resolved from `tf-demo-parser`'s `MessageType` enum: the type field is **6 bits**, matching
Source's `NETMSG_TYPE_BITS`, and the full id→type table is recorded in `SPEC.md`. The SDK
could not help — `protocol.h` and `netmessages.h` are not in source-sdk-2013 — so prior art
was the only route, and every value is pinned by an explicit test because of that.

What replaced it as the structural constraint: **messages carry no length prefix**, so an
undecodable message blocks everything after it in that packet. Support is strictly
incremental. Game events and string tables are the exceptions — both carry an explicit bit
length and can be stepped over.

## B4. SendTable flattening — **IMPLEMENTED 2026-08-08**, still the highest-risk code

VDC documents `SPROP_CHANGES_OFTEN` as reordering the property list and
`SPROP_EXCLUDE` as removing inherited properties. Neither is cosmetic: the
flattened, sorted property list is what entity deltas index into. Get the ordering
wrong and the decoder reads real values into the wrong fields — it will not crash,
it will just be wrong.

**Implemented**, with each ordering rule given its own test rather than one end-to-end check,
and verified on the corpus: changes-often properties form an unbroken prefix across all 362
classes in all three demos, and no class exceeds `MAX_DATATABLE_PROPS`. See `SPEC.md`.

**The risk is reduced, not eliminated.** Those checks confirm the ordering is *self-consistent*
and matches the documented rules. They cannot confirm it matches what TF2 actually did, because
nothing here decodes an entity yet — the first real test is whether `svc_PacketEntities`
consumes exactly the right number of bits. If flattening is subtly wrong, that is where it
surfaces, and it will surface as plausible values rather than an error.

**So the cross-parser differential test still matters, and should land with entity decode
rather than after it.**

Substantially de-risked 2026-08-07 by reading `dt_common.h` from the SDK, which is
authoritative where VDC is prose: 17 `SPROP_` flags rather than the 8 VDC documents,
plus exact bit widths (`SPROP_NUMFLAGBITS` 17, `MAX_DATATABLE_PROPS` 4096,
`DT_MAX_STRING_BITS` 9, `MAX_ARRAY_ELEMENTS` 2048) and the `DPT_` type ids. The three
`SPROP_COORD_MP*` variants are absent from VDC entirely and are almost certainly what TF2
uses for player positions — a decoder built from VDC alone would decode every position
wrongly and never crash. See `SPEC.md`.

## B5. No public wire spec for entity decode — **UNDOCUMENTED**, narrowed 2026-08-08

Established during the spec consolidation: VDC describes entity networking
conceptually but publishes no bit layout for `svc_PacketEntities`, no delta-index
encoding, no property ordering rules. More reading will not produce one.

**Narrowed:** the *schema* half is now solved. `dem_datatables` parses against all three
demos — 517 tables, 362 classes, 5,441 properties — and the trailing class count agrees with
`svc_ServerInfo`'s independently reported `MaxClasses`. What remains undocumented is the
*delta encoding*: how `svc_PacketEntities` addresses entities, and how a flattened property
list is ordered.

**Mitigation:** prior art plus byte-level experiment. Licence checked 2026-08-07 —
`tf-demo-parser` is **MIT OR Apache-2.0**, so reading *and* porting are both
permitted with attribution. Our "don't port" rule is an engineering preference
(understand the format), not a legal constraint, and can be relaxed deliberately if
a specific piece proves too error-prone.

## B6. Newer demo protocols add commands we have never seen — **OPEN**

`dem_customdata` exists in later demo protocol versions. Its command value is
unverified and deliberately absent from `DemoCommandType`. Our corpus is entirely
demo protocol 3, so any newer demo will hit an unrecognised command byte and throw.

**Mitigation:** the throw is correct behaviour — loud, not silent. Add the value
when a specimen exists to verify it against, never from a guess.

## B7. No historical corpus, and no test that would notice — **CONFIRMED**

Per D5: pre-2020 demos are unobtainable. RGL launched 2019, ETF2L's archive has
rotted to a July 2020 floor, ESEA's STVs were expiry-dated and never archived,
sizzlingstats is gone, and the owner's own drives have been reformatted since.

**Mitigation:** none available. The schema-driven design has to be right by
construction because it cannot be verified. Stated here so nobody mistakes a green
suite for era coverage.

## B8. Cross-parser oracle — **toolchain installed 2026-08-08**

`parse_demo` outputs a JSON *summary* (header, players, scoreboard), not per-tick
data. Useful as a first oracle; insufficient for entity-level diffing, which would
need a small harness over their `DemoTicker` API.

D2 bans Rust *from this codebase*. A test-only oracle is outside that, and the owner decided
to proceed: **rustup 1.29.0 is installed natively on Windows** (not WSL, per their stated
preference), giving rustc/cargo 1.97.1 on the `x86_64-pc-windows-msvc` host. MSVC BuildTools
2022 was already present, so the default toolchain links without extra setup.

`tf-demo-parser` remains the oracle of choice over `UntitledParser`: the latter is C# and
needs no toolchain, but targets HL2 and Portal rather than TF2.

**Largely resolved 2026-08-07.** [UntitledParser](https://github.com/UncraftedName/UntitledParser)
is a Source demo parser written in **C#** and licensed **MIT** (GitHub reports NOASSERTION
only because the file is `LICENSE.txt`; the text itself is plain MIT). A same-language oracle
can be a project reference in the test suite — no Rust toolchain, no cargo, no WSL. It targets
HL2 and Portal rather than TF2, so it is the weaker oracle for our format, but it removes the
toolchain objection entirely.

Licences of both references, for the record:

| Project | Language | Licence |
|---|---|---|
| `tf-demo-parser` | Rust | MIT OR Apache-2.0 (per crates.io metadata) |
| `UntitledParser` | C# | MIT |

**If Rust is ever actually needed, install rustup natively on Windows — not in WSL.**
Owner's call, 2026-08-07. WSL adds a filesystem translation penalty on `/mnt/c` that makes
builds against this repo noticeably slower, and there is no reason to pay it: rustup runs
natively on Windows and `cargo build` produces the same binary. The one thing that genuinely
does need WSL is libFuzzer for D8's coverage-guided runs, because the toolchain there is
Linux-only — that is a separate concern from a Rust build and should not drag Rust into WSL
with it.

Both permit reference *and* copying with attribution. Also worth noting as evidence rather
than reassurance: UntitledParser supports many Source engine versions, which is direct proof
that D1's "small table of quirks per version" is a workable pattern and not an optimistic
assumption.

## B11. Five string tables per demo are LZSS-compressed — **CONFIRMED**

`modelprecache`, `soundprecache`, `instancebaseline`, `ParticleEffectNames` and `Scenes` all
arrive compressed in every corpus demo. They are skipped cleanly via their length prefix, so
the cost is those tables rather than the stream.

Two of them matter later. `instancebaseline` holds the default property values entities are
deltaed against, which entity decoding will need. `modelprecache` maps model indices to
paths, which the viewers will want.

**Mitigation:** implement LZSS decompression when entity decode needs the baselines. Source's
variant is documented in community sources and is small — a 4-byte `LZSS` magic, a size
header, then flag-byte-driven literal/reference pairs. Not urgent until then.

## B9. Memory pressure at corpus scale — **anticipated, not yet measured**

A 75 MB demo yields ~120,000 commands. `DemoCommandReader` yields
`ReadOnlyMemory` windows rather than copies specifically to avoid this, but entity
decode will materialise per-tick state and that is where allocation will actually
show up. D2's rule applies: profile before reaching for anything exotic.

## B10. Git LFS bandwidth on a public repo — **known tradeoff**

Per D11: LFS bandwidth is billed to the repository owner beyond 1 GB/month, while
ordinary clones are free. A popular public repo would want demos in release assets
instead. Volume problem, not a correctness one.

## B12 — entity decoding desynchronises inside `CTFPlayer`, corpus-confirmed

**Status: open, precisely located, not yet diagnosed.** The entity decoder passes every
hand-built fixture and fails on real demos, which is exactly the split those fixtures cannot
resolve on their own.

Probing `z1800.dem`'s opening full snapshot, entity by entity:

| Entity | Update type | Class | Flattened props | Props read | Bit position |
|---|---|---|---|---|---|
| 0 | Enter | `CWorld` | 53 | 0 | 28 |
| 1 | Enter | `CTFPlayer` | 740 | 11 | 376 |
| 17 | **Leave** | `CBaseCombatCharacter` | 315 | 0 | 404 |

Entities 0 and 1 are right: the class ids resolve to sensible names, and a player carrying 11
changed properties in an opening snapshot is plausible. Entity 17 is not — a *full* snapshot
contains only enters, so a `Leave` there means the reader was already misaligned. The
divergence is therefore **inside `CTFPlayer`'s 11 properties, between bit 28 and bit 376**.

Two candidates, in order of suspicion:

1. **Flattened property order** (see B4). A wrong order selects a property of the wrong width,
   which desynchronises rather than returning a wrong value. `CTFPlayer` flattens to 740
   properties here; that number has never been checked against an independent implementation.
2. **A value encoding whose width is wrong for one specific property.** `SPROP_VARINT` was one
   such case and is now handled — flag 32 means `SPROP_NORMAL` on a float but a varint-encoded
   integer on an int, and reading a varint as a fixed-width field consumes the wrong number of
   bits. Fixing it did not move the failure point, so at least one more remains.

**What is verified and what is not.** The coordinate encodings, the value decoders, entity
index deltas, property index deltas, update types and the removal list all pass fixtures built
from the SDK's write path, and each has been confirmed to fail when deliberately broken. None
of that establishes agreement with what TF2 actually emits. The corpus is the only instrument
that can, and it currently says no.

**Next step is the differential harness, not more fixtures.** `parse_demo.exe` decodes
`z1800.dem` successfully, so a correct answer exists to compare against — see
`docs/DIFFERENTIAL.md`. Comparing `CTFPlayer`'s flattened property list against the oracle's,
name by name, will settle candidate 1 immediately.

### B12 update — the differential settles it: right set, wrong order

The harness in `tools/differential/` compared `CTFPlayer`'s flattened list against
`demostf/parser`. The result is narrow and useful:

- Both lists hold **741 properties**, of which **235 are array elements**.
- The **sets of names are identical** — nothing is unique to either side.
- The **order** differs, first at **index 20**.

| Index | Oracle | Ours |
|---|---|---|
| 20 | `m_flEncodedController.001` | `DT_CollisionProperty.m_vecMinsPreScaled` |
| 23 | `DT_BCCLocalPlayerExclusive.m_flNextAttack` | `DT_CollisionProperty.m_vecMaxs` |
| 24 | `m_hMyWeapons.000` | `DT_CollisionProperty.m_nSolidType` |

That is B4's predicted failure exactly, and it clears several suspects at once. The schema
parser reads the tables correctly, exclusions are applied correctly, and array elements are
expanded correctly — all three would change the *set*, and the set matches. The fault is
confined to the sequencing rules in `SchemaFlattener`.

The specific suspect is rule 2: **where a non-collapsible child's group lands**. This parser
hoists such a group ahead of the referencing table's own properties. The oracle emits
`m_flEncodedController`'s elements at index 20, immediately after `DT_BasePlayer.m_fFlags`,
and defers `DT_CollisionProperty` — the opposite placement. Note also that
`m_flEncodedController.000` is absent from both lists, so whatever rule drops it is already
agreed on.

Fixing this needs no new fixtures. `tools/differential/` regenerates both lists, and the fix is
right when the diff is empty for every class, not just `CTFPlayer`.


### B12 closed — the changes-often partition is a swap, not a stable partition

Fixed. The flattener's final step moves `SPROP_CHANGES_OFTEN` properties to the front, and it
does so by **swapping** each one with whatever occupies the boundary:

```
boundary = 0
for i in 0..n:  if flat[i].changes_often:  swap(i, boundary); boundary += 1
```

This parser used a stable partition instead. That is the intuitive choice and it is wrong: a
swap displaces the boundary element to position `i` rather than shifting the block along, so
**the tail comes out deliberately scrambled**. Traced on six properties:

```
[s1 f1 s2 f2 s3 f3] -> swap(1,0) -> [f1 s1 s2 f2 s3 f3]
                    -> swap(3,1) -> [f1 f2 s2 s1 s3 f3]
                    -> swap(5,2) -> [f1 f2 f3 s1 s3 s2]
```

Both forms put changes-often properties first, in the same order, which is why the existing
tests passed a wrong implementation for as long as they did — they only ever asserted the head.
Only the tail separates them.

**Verification.** `tools/differential/` now reports **zero differences on every class of all
four corpus demos** — 49,945 properties for `z1800`, 49,944 for each ETF2L demo, 53,977 for the
2026 serveme demo, roughly 204,000 in total, every one at the same index as the oracle.

With that fixed, real demos decode. Opening full snapshots:

| Demo | Entities | Property values | Bits |
|---|---|---|---|
| `etf2l-12030-stv` | 824 | 7,515 | 312,036 |
| `serveme-627619` | 598 | 6,652 | 280,046 |
| `z1800` | 545 | 7,442 | 258,542 |

`z1800`'s 278 player origins span x −1480..8864 and z −1..952, which is a plausible extent for
`koth_harvest_final` and the first confirmation that `SPROP_COORD_MP` decodes correctly against
real data rather than against a fixture.

**Still open, and unrelated:** delta snapshots reference entities established through the
`instancebaseline` string table, which is LZSS-compressed and not yet decompressed. That blocks
continuous decoding past the opening snapshot, and blocks POV demos entirely — they carry no
full snapshot at all, since the recording begins mid-match.


## B13 — continuous decoding stops after 62 to 205 snapshots

**Open.** With the flattening order fixed (B12) and Snappy landed, decoding now runs from the
opening full snapshot through many consecutive deltas — where it previously managed none — then
desynchronises. Measured across the corpus, decoding 500 snapshots from the first full one:

| Demo | Snapshots decoded | Stops with |
|---|---|---|
| `z1800` | 62 | reader asked for 32 bits with 19 remaining |
| `etf2l-12030-stv` | 125 | property index −935,059,892 in class 443 |
| `serveme-627619` | 205 | entity 1511 updated without ever entering |

**The three failures are one cause, seen at three depths.** A class id of 443 is impossible —
the schema declares 362 — and a negative property index is a UBitVar read from misaligned bits.
Both mean the reader was already lost when it produced them. "Updated without entering" is the
same thing surfacing earlier, since a desynchronised entity index names a slot nothing entered.

So this is **not** the entity-baseline gap it resembles. `instancebaseline` decodes now, and it
supplies default *property values* per class — it never carried the entity-to-class mapping,
which only an Enter provides. A baseline would not fix a misread bit offset.

**What that leaves.** Some encoding consumed in a delta is still read at the wrong width. The
opening snapshot decodes perfectly on all four demos, so the encodings it exercises are right;
the fault is in something deltas reach that a full snapshot does not. Candidates, untested:

- Array properties. `ReadArray` sizes its count with `ClassIdBits(ElementCount)`, which is the
  same `floor(log2)+1` the class id uses, and that has never been checked against the oracle.
- `SPROP_VARINT` on a property whose value crosses a varint byte boundary — the fixtures only
  cover small values and one large one.
- The delete/leave paths removing entities the decoder should forget.

**The differential is the instrument again**, exactly as in B12. `parse_demo` decodes these
files completely, so a per-snapshot comparison of entity ids and property indices will localise
the first divergence rather than leaving it to be guessed at.


### B13 — what research has ruled out, and what to read next

Two fix attempts moved the measurement not at all, which means the model of the format is
wrong rather than the code. Recording what has been checked so that the next session reads
rather than guesses.

**Ruled out by reading the reference:**

- **Array count width.** `demostf/parser` uses `log_base2(element_count) + 1`, and its
  `log_base2` is `bit_width - 1 - leading_zeros`, i.e. floor. That matches `ClassIdBits` after
  the B12 fix, so array counts are right.
- **Signon packets being special.** The oracle parses `Signon` and `Message` packets through
  the same `MessagePacket::parse`, so entity messages in the signon are not treated differently
  and must not be skipped here either.
- **Entity-baseline availability.** `instancebaseline` decodes now. It carries default property
  values per class and never carried the entity-to-class mapping, which only an Enter provides.

**Fixed while looking, though it was not the cause:** coordinate flags are strict first-match in
the engine — `COORD`, then `COORD_MP`, then `LOWPRECISION`, then `INTEGRAL` — not independent
modifiers. A property carrying `COORD_MP` and `LOWPRECISION` together reads five fraction bits,
not three. No property in the current corpus carries both, so the numbers did not move, but the
old code was wrong against the SDK.

**Still to read, in this order:**

1. `ParserState::handle_packet_entities` in the oracle — what it does with entities *after*
   parsing, particularly the `update_baseline` flag and the two baseline slots. This parser
   ignores both, and a baseline swap that changes how a later delta is interpreted would look
   exactly like this.
2. The SDK's `CBaseClientState::ReadPacketEntities` and `CEntityReadInfo`, for anything between
   entities that this parser does not consume.
3. The oracle's own tests under `src/demo/`, which encode expectations no prose states.

**Then build the per-snapshot differential**, as in B12: compare entity ids and property indices
snapshot by snapshot against `parse_demo` and let the first divergence name itself. That is what
settled the flattening order in one diff after days of guessing.


### B13 — the differential localises it to one property read

`tools/differential/snapshots.rs` dumps every entity update snapshot by snapshot from the
oracle; `DumpFlattened.cs snapshots` does the same here. Diffing them on `z1800.dem` names the
first divergence exactly.

**Snapshots 0 through 18 match line for line — 1,133 entity updates, every entity index, update
type, class id and property index identical.** The first difference is snapshot 19:

| Snapshot | Entity | Oracle | Ours |
|---|---|---|---|
| 19 | 1 | `4,17` | `4,17` — matches |
| 19 | 2 | `16,17` | `17` |
| 19 | 6 | `14,15,16,17,703` | `14,15,16,17` |

The properties involved:

| Index | Property |
|---|---|
| 16 | `DT_TFNonLocalPlayerExclusive.m_angEyeAngles[1]` |
| 17 | `DT_BaseEntity.m_flSimulationTime` |
| 703 | `DT_TFPlayer.m_bSaveMeParity` |

**What this rules in.** Entity 1 of snapshot 19 matches, so the reader is correctly aligned
*entering* entity 2 — and then reads a first property index of 17 where the oracle reads 16.
Identical bits cannot decode to different values through identical `UBitVar` code, so alignment
must already differ by the time that field is read. The only thing between them is the tail of
entity 1: its last property value, `m_flSimulationTime` at index 17.

So the fault is a **value width**, not an index encoding. Some property's value is read at the
wrong number of bits, the indices continue to look plausible for a while, and the error
accumulates until it becomes fatal at snapshot 62.

This also explains why snapshots 0-18 are clean: whatever encoding is misread does not appear,
or appears with a width that happens to agree, until then.

**Next step is narrow now.** Instrument the decoder to print the bit offset after each property
value in snapshot 19, entity 1, and compare against what the layout implies. `m_flSimulationTime`
and `m_angEyeAngles[1]` are the two definitions to check against the SDK first — read their
flags and bit counts out of the schema and confirm which encoding branch they take.


#### B13 — property definitions checked, and what they eliminate

Every property appearing in snapshot 19's diverging entities, read out of the schema and
checked against the oracle's own `FloatDefinition::new` precedence:

| Index | Property | Type | Flags | Encoding it selects |
|---|---|---|---|---|
| 4 | `m_nTickBase` | Int | `0x0400` | signed, 32 bits |
| 9 | `m_vecOrigin` | VectorXY | `0x0404` | `SPROP_NOSCALE`, two 32-bit floats |
| 10 | `m_vecOrigin[2]` | Float | `0x0C04` | `SPROP_NOSCALE`, 32 bits |
| 13 | `m_vecOrigin` | VectorXY | `0x4400` | `COORD_MP_LOWPRECISION` |
| 14 | `m_vecOrigin[2]` | Float | `0x4C04` | `COORD_MP_LOWPRECISION` — beats the `NOSCALE` bit also set |
| 15 | `m_angEyeAngles[0]` | Float | `0x0C00` | range, 8 bits, −90…90 |
| 16 | `m_angEyeAngles[1]` | Float | `0x0C00` | range, 10 bits, 0…360 |
| 17 | `m_flSimulationTime` | Int | `0x0401` | unsigned, 8 bits |
| 703 | `m_bSaveMeParity` | Int | `0x0001` | unsigned, 1 bit |

Index 14 is worth noting: it carries both `COORD_MP_LOWPRECISION` and `SPROP_NOSCALE`, and the
first-match rule means the coordinate encoding wins. This parser now agrees, but only because
that precedence was fixed while investigating — it is exactly the shape of bug being hunted, and
it is real in this corpus even though it was not the cause here.

**Every one of these selects the same branch in both parsers**, so the fault is not in the
definitions of the properties that appear in the diverging entities. That leaves two
possibilities:

1. A property earlier in the same snapshot, in an entity that *matched*, whose value width is
   wrong in a way that does not disturb its own indices. Entity 1 of snapshot 19 reports
   `4,17` in both parsers, so its indices are right — but its property *values* have never been
   compared, only its indices.
2. The snapshot's trailing structure — the removal list, or the update-baseline flag — consuming
   a different number of bits.

**The next measurement is values, not indices.** `snapshots.rs` and `DumpFlattened.cs snapshots`
both print property indices only, which is what made snapshots 0-18 look identical. Extending
both to print decoded values would show whether entity 1's `m_flSimulationTime` actually agrees
or merely lands at the right index.


#### B13 — value comparison is wired up, and blocked on float formatting

Both dumpers now print decoded values (`index=value`) rather than bare indices, which is the
measurement the previous section called for. Spot-checking snapshot 0, entity 1:

```
oracle: 4=12735,9=(288, 2312),10=69.03125,11=0.35294342,12=269.91202,...
ours:   4=12735,9=(288, 2312),10=69.031,  11=0.353,     12=269.912,...
```

**The values agree.** Integers, vectors and floats all match — this parser simply prints fewer
digits, because `PropertyValue.ToString` formats floats as `0.###` for readability in text
dumps. That is right for a dump and wrong for a differential.

So the comparison is one change away from being usable: the dumper must print floats
round-trippably (`"R"` or `G9`) rather than going through `ToString`. Until then a textual diff
reports every float as a difference and drowns the real one.

**State of the hunt.** Indices match for 1,133 consecutive entity updates and then drift at
snapshot 19; values match wherever they have been compared so far, which is only snapshot 0.
The next run should diff values across snapshots 0-19 with float formatting normalised, and
read the *first* line where a value differs — that names the property whose width is wrong,
which is what the index-level diff cannot do.
