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


#### B13 — narrowed to one array property, `m_hViewModel`

With float formatting normalised, the value differential runs clean and names a single property.
Snapshot 0, entity 2: **81 properties on each side, 80 of them identical**, and one differs.

| Index | Property | Oracle | Ours |
|---|---|---|---|
| 701 | `DT_BasePlayer.m_hViewModel` | `[9548541464807]` | `[2]` |

`m_hViewModel` is an `Array` with `ElementCount = 2`, so its count field is
`floor(log2(2)) + 1 = 2` bits — which is what this parser uses. **The width formula is not the
bug.** Every property before index 701 in the same entity matches, so alignment entering the
array is correct.

The two renderings are not directly comparable yet, and that is the immediate obstacle: this
parser's `PropertyValue.ToString` prints an array as `[count]`, so `[2]` means "two elements"
and says nothing about their values, while the oracle prints element contents. One of two things
is true and the dump cannot currently distinguish them:

1. The count is read correctly as 2 and the *elements* are decoded at the wrong width.
2. The count is misread, and the oracle's single large value is one element where this parser
   found two.

The oracle's value is suggestive: 9,548,541,464,807 is about 2^43, far wider than an entity
handle, which hints its own rendering is a concatenation or an `i64` and not a plain element.
**Do not conclude from it** — read `SendPropValue`'s `Display` impl for the array case before
drawing anything from that number.

**Next step, concretely:** make the C# dumper print array elements rather than a count, re-run
the same diff, and read index 701 on snapshot 0 entity 2. That single line decides between the
two possibilities above, and it is the last unknown standing between here and continuous
decoding.


#### B13 — correction: `m_hViewModel` was a rendering artefact, not a difference

The previous section was wrong, and the way it was wrong is worth keeping.

`m_hViewModel` appeared to differ — oracle `[9548541464807]`, ours `[2]` — and that was read as
a possible count mismatch. Reading the oracle's `Display` impl for `SendPropValue::Array` before
acting on it shows the two renderings were never comparable:

```rust
write!(f, "[")?;
for child in array { write!(f, "{child}")?; }   // no separator
```

The oracle **concatenates elements with no separator**, so `[9548541464807]` is several elements
run together, not one 43-bit value. Ours printed `[2]`, an element *count*, from
`PropertyValue.ToString`. Neither side was showing element values. With both dumpers emitting
the same concatenated form, **index 701 matches** and entity 2 is clean.

That is the research-before-code rule doing its job at the smallest scale: a number that looked
like evidence was an artefact of two different `ToString` implementations, and the check cost one
grep.

#### B13 — a real bug, but not this one: unsigned 32-bit values wrap negative

With arrays rendered comparably, the first genuine value difference is snapshot 0, entity 3 —
75 properties each side, two differing:

| Index | Oracle | Ours |
|---|---|---|
| 618 | `4294967295` | `-1` |
| 619 | `4294967295` | `-1` |

Both are `0xFFFFFFFF`. The bits consumed are identical, so **this is not the desync** — but it is
a real defect: `PropertyValue` stores integers as `int`, and a 32-bit *unsigned* property cannot
be represented, so it wraps to `-1`. The oracle stores `i64` and reports the true value.

Filed here rather than fixed in passing, because changing `PropertyValue`'s integer width touches
every decoder path and deserves its own change with its own tests.

**The desync is still unlocated.** Every difference found so far consumes the same bits. The
search continues from the first entity whose *bit consumption* diverges, which none of the
value comparisons has yet exposed — the next measurement should be the reader's bit offset after
each entity, compared against the oracle's, rather than the values themselves.


### B13 resolved as misdiagnosed — a dropped message, not a desync

**My snapshot 19 is identical to the oracle's snapshot 20**, all fifty entities, every index and
every value. Searching the oracle's snapshots 19 through 25 for our snapshot 19 finds exactly
one match, and it is off by one.

| Oracle snapshot | Entities | Matches our #19? |
|---|---|---|
| 19 | 51 | no |
| **20** | **50** | **identical** |
| 21 | 53 | no |

So there is no desynchronisation in the entity decoder. **One entire `svc_PacketEntities`
message is being dropped**, and everything downstream was misnumbered from that point — which is
why "entity 1 of snapshot 19" showed indices that matched but values four ticks later. It was a
different snapshot.

That retracts most of the B13 analysis above. The property definitions, the array count widths,
the coordinate flag precedence, the value comparisons — all were investigating a decoder that was
reading correctly the whole time. Worth leaving in place rather than deleting, because the trail
shows how a numbering error masquerades as a bit-level fault for three rounds of investigation.

**Where it actually lives.** The corpus walker skips any message whose `Body.IsEmpty`, so a
message whose `LengthBits` decoded as zero disappears silently. The oracle's snapshot 19 carries
51 entities, so that message is real and its body is not empty — meaning either its header was
misread, or the message never reached the decoder because `NetMessageReader` mis-parsed the
packet containing it.

**This is a message-layer defect, not a schema or entity one**, which is a different subsystem
from everything B12 and B13 have touched so far.

**Next step:** count `svc_PacketEntities` messages produced per `dem_packet` against the oracle,
including the ones currently skipped for an empty body, and find the first packet where the
counts differ. Then read that packet's header fields rather than its entity body.


### B13 root cause — unimplemented messages truncate their whole packet

Found. Network messages carry **no length prefix**, so an unimplemented type cannot be stepped
over — `NetMessageReader` stops at the first one and abandons the rest of that packet. Every
`svc_PacketEntities` sitting after it is lost, which is exactly the dropped snapshot.

Measured over the first 200 packets of `z1800.dem`: **131 stop early.**

| Message | Packets stopped |
|---|---|
| `svc_TempEntities` | 57 |
| `svc_Sounds` | 37 |
| `svc_Prefetch` | 34 |
| `svc_SignOnState` | 1 |
| `svc_VoiceInit` | 1 |
| `svc_SetView` | 1 |

This was always visible in the reader's own diagnostics — `NetMessageReadResult.StoppedAt`
records it, and the `default` case says so in as many words. Nothing was hidden; the entity
investigation simply never asked the message layer whether it had delivered everything.

**That is the lesson worth keeping from B12 and B13 together.** B12 was a real decoder bug found
by differential. B13 looked identical from the outside — same symptom, same tooling, same
narrowing — and was a missing feature one subsystem upstream. Three rounds of bit-level analysis
went into a decoder that was already correct, because the first question asked was "which bit is
wrong" rather than "did every message arrive".

**The fix is ordinary implementation work, not reverse engineering.** These six messages need
decoding, or at minimum exact-width skipping so the reader can continue past them.
`svc_TempEntities`, `svc_Sounds` and `svc_Prefetch` account for 128 of the 131 stops and are
where to start. `demostf/parser` implements all of them.


### B13 closed — every corpus demo decodes end to end

Eleven message types later, the entity stream runs to completion on all four demos.

| Demo | Snapshots decoded | Frames | Stops |
|---|---|---|---|
| `z1800` | 14,385 | 14,386 | none |
| `etf2l-12025-pov` | 118,280 | 118,282 | none |
| `etf2l-12030-stv` | 99,999+ (probe cap) | 120,913 | none |
| `serveme-627619` | 73,182 | 73,183 | none |

Implemented, each unblocking the next: `svc_TempEntities`, `svc_Sounds`, `svc_Prefetch`,
`svc_SetView`, `svc_SignOnState`, `svc_VoiceInit`, `svc_UserMessage`, `svc_EntityMessage`,
`svc_VoiceData`, `svc_SetPause`. Every one is length-prefixed or fixed-width — none required
reverse engineering, and none was a decoder fix.

**Two false conclusions this closed.**

The 332-snapshot wall that stopped two unrelated demos at the identical number looked
structural, and was: both hit `svc_UserMessage` at packet 336, and losing that packet's
snapshot broke the delta that followed. One measurement found it — printing the packet index of
each remaining stop — after three rounds of bit-level analysis found nothing.

And a test asserting that POV recordings carry no full snapshot at all, justified by scanning
2,000 consecutive deltas without finding one. Never true. The full snapshot was there, behind an
unimplemented message. **That scan was evidence about the reader and was read as evidence about
the format** — B13's own mistake, committed inside a test.

**Still unimplemented, none present in this corpus:** `svc_BspDecal`, `svc_CmdKeyValues`,
`svc_File`, `svc_FixAngle`, `svc_GetCvarValue`, `svc_Menu`.


### Generality evidence — nine modern demos, zero failures

Measured 2026-08-08, on files the parser had never seen, immediately after B13 closed:

| Metric | Total |
|---|---|
| Demos | 9 |
| Snapshots decoded | ~821,000 |
| Entity updates | ~14.8 million |
| Property values | ~94 million |
| Packet stops | 0 |
| Decode errors | 0 |

Snapshot count equalled `dem_packet` count exactly in every file, which is the strong form: not
"mostly worked" but "every packet yielded its snapshot". Three maps were new to the corpus, and
one demo came from `br.tf2pickup.org`, a platform not previously represented.

**What this does and does not establish.** It establishes that the schema-driven decode
generalises across maps, servers and platforms within the modern era — which was the project's
central bet, and the first real evidence for it. It establishes nothing about older builds. Per
D5 the corpus still has no pre-2020 specimen, so the era axis remains completely untested.


## B14 — game event field type 7 is disputed and unexercised — CLOSED, this parser was wrong

Two readings of the same wire value, disagreeing by 64 bits rather than by a value:

| Source | Meaning of type 7 | Bits read |
|---|---|---|
| This parser | `UInt64` | 64 |
| `demostf/parser` | `Local` | **0** |

**The reference parser was right.** Settled 2026-08-09 by arithmetic rather than by authority,
which is why it could be settled at all without an old demo in hand.

**What decided it.** The reference implementation declares the type field
`#[discriminant_bits = 3]`. Three bits reach 7 and stop. The reading this parser used came from
CS:GO's protobuf field ordering — `val_string` at 2 through `val_uint64` at 8 and `val_wstring`
at 9 — an ordering that *needs* a wider field than the wire provides. It cannot be the numbering
in use here, and no demo was required to know that. The comment two lines above this project's
own enum had said "Three bits on the wire" the entire time.

Valve's own ordering corroborates it. `igameevents.h`: "Valid data types are string, float, long,
short, byte & bool. If a data field should not be broadcasted to clients, use the type 'local'."
That is 1 through 6, then 7. The protobuf types are a CS:GO-era addition, from after the message
layer stopped being hand-packed bits.

**Why it never showed up.** A histogram of every field in every corpus demo's event list:

```
demostf-cp_process_f12      t1=110 t2=41 t3=88 t4=437 t5=162 t6=46
demostf-cp_snakewater_final1 t1=110 t2=41 t3=88 t4=437 t5=162 t6=46
demostf-koth_product_final  t1=110 t2=41 t3=88 t4=437 t5=162 t6=46
etf2l-12025-pov-2020-07-21  t1=109 t2=41 t3=70 t4=426 t5=162 t6=46
etf2l-12030-stv-2020-07-23  t1=109 t2=41 t3=70 t4=426 t5=162 t6=46
serveme-627619-stv          t1=110 t2=41 t3=88 t4=437 t5=162 t6=46
z1800.dem                   t1=109 t2=41 t3=70 t4=426 t5=162 t6=46
```

No type 7 anywhere. TF2's shipped event definitions use no local fields, so the bug was latent in
every demo this project can currently reach — exactly the shape D5 predicts for era-axis defects.

**A side result worth keeping.** The histogram is an era fingerprint. `z1800.dem` matches the two
2020 ETF2L demos exactly (109/41/70/426/162/46) and differs from the 2026 demos (110/…/88/437).
That is independent corroboration of the redating recorded in `docs/memory/`, arrived at from the
event schema rather than from map assets.

**Fixed:** `GameEventValueType.UInt64` is now `Local`, reads zero bits, and produces a field with
no value rather than a number. The trace prints it as `local`, because rendering a null as an
empty string is indistinguishable from a field that carried an empty string.

**The regression test measures the field behind it, not the field itself.** A local field has no
value, so asserting on it cannot separate the two readings; a wrong width does not return a wrong
answer, it desynchronises what follows. The fixture also pads the event body past the broken
read's reach — without that padding the 64-bit read simply ran off the end and the event was
reported truncated, which is a failure for the wrong reason and would not have caught a wrong
width that happened to stay in bounds.

## B15 — a byte-count length can overflow before it can be checked

`svc_CmdKeyValues` declares its payload length as a 32-bit **byte** count. Multiplying an
implausible one by eight overflows `int` before the result can be compared against anything, so
the reader threw `OverflowException` on real demos rather than reporting a problem.

Found the honest way: implementing `svc_FixAngle` with a single 49-bit read — `ReadUInt32` tops
out at 32 — misaligned the stream, and a later message was then read as `svc_CmdKeyValues` with
a garbage length. So the overflow was a symptom of a bug three messages away, which is the usual
shape here.

Fixed on both counts. `svc_FixAngle` reads its flag and three angles separately, and byte-count
bodies now go through a helper that rejects a length the packet cannot hold, reporting it as a
**stop** rather than throwing. Reported as a stop specifically because an impossible length
means either that message's layout is wrong or the reader reached it misaligned — both belong in
`StoppedAt` where they are visible, not silently skipped.

With both fixed, all four corpus demos report **zero packet stops** across their full length.


## B16 — network message id 1 occurs in the corpus and has no known layout

The ETF2L POV demo contains a packet whose first message is **id 1**, 119 bits in. Both this
parser and `demostf/parser` omit that id from their message tables, so both treat it as
unrecognised and abandon the rest of the packet.

**Found by the trace writer**, which reports an unreadable message at the position it occurred.
The summary dump had been reading this demo for weeks without ever surfacing it — an aggregate
has nowhere to put "and one packet made no sense".

**A guess was tried and rejected.** Source's `netmessages.h` defines `net_Disconnect = 1` with a
reason string, so the obvious move is to read a string. Doing that moved the failure from bit
119 to bit 357 and landed on message id 61, which is not a valid type — so the body is not a
bare string here, or id 1 does not mean disconnect at this protocol. Reverted rather than
shipped: a decoder that consumes the wrong number of bits produces plausible garbage, which is
worse than an honest stop.

**What is known:** one packet, in one demo, at tick 0, immediately after a run of
`dem_consolecmd`. The packet after it decodes cleanly and carries `[P-REC] Recording...`, which
suggests the demo was recorded by the P-REC client plugin. That is a lead — a client-side
recorder may write something the dedicated-server message table does not describe.

**To settle it:** read `netmessages.h` from a Source SDK checkout for the true id-1 body, and
check whether P-REC injects anything of its own. Do not implement on the `net_Disconnect`
assumption again without evidence; it has already failed once.

The cost is bounded and visible: one packet of 118,282 in one of seven demos, reported in place
rather than hidden.


### B16 resolved — it was `svc_BspDecal` overreading, not an unknown message

Closed, and wrongly diagnosed twice on the way.

**What it actually was.** `svc_BspDecal`'s body is:

| Field | Width |
|---|---|
| three presence bits, then `SPROP_COORD` per present axis | variable |
| texture index | **9** |
| entity-and-model present | **1** |
| entity index, model index — only if that flag is set | **11**, **13** |
| low priority | 1 |

This parser read three unconditional 16-bit fields where 9 + 1 + (0 or 24) + 1 belong: an
overread of 14 to 38 bits. Everything after it in that packet was garbage, and the garbage
happened to decode as message id 1 in one demo and id 33 in another.

**Why it took two wrong diagnoses.**

The first was reading the symptom as the cause — "id 1 is a message we do not implement" — and
then guessing at `net_Disconnect`'s body, which moved the failure rather than fixing it. That
guess was reverted, correctly, but the framing survived and shaped the writeup.

The second was subtler and is the one worth keeping: **the trace could not show the message that
caused it.** Sixteen message types were consumed for alignment only and emitted nothing, so the
failing block rendered as "no messages, stopped after 124 bits" — which reads as a packet corrupt
from its first bit. Recording those as `SkippedMessage` made the answer immediate: the block
showed `svc_bspdecal bits 118` and nothing else, and a single decal as the first message of the
first packet is not plausible content.

**The mistake underneath both.** `svc_BspDecal` was implemented from the reference parser's
**struct**, whose fields are `u16`, rather than from its **`BitRead` impl**, which uses 9, 11 and
13. A struct is a program's in-memory shape and says nothing about the wire. Read the reader.

**What made it visible.** Two point-of-view demos failing at nearly the same offset with
*different* ids. A genuine unknown type gives the same id every time; two different ids in the
same structural position is misalignment. That comparison was only possible because a second POV
demo was fetched specifically to test the hypothesis — the owner's suggestion, and the thing that
turned one anecdote into a pattern.

All seven corpus demos and both fetched POV demos now trace with zero stops.


## B17 — the message type field is not always six bits, and nothing said so

**Found and fixed 2026-08-09, on the first demo old enough to show it.**

Source writes each message's type in a field sized by `2^NETMSG_TYPE_BITS > SVC_LASTMSG`. In
2009 the highest id was `svc_GetCvarValue` at 31, so five bits sufficed. `svc_CmdKeyValues` (32)
and `svc_PaintmapData` (33) arrived later and forced six. This parser hardcoded six.

**Why it was invisible until now.** It is the one era difference that is *not* in Valve's
`proto_version.h` (D20), so it cannot be found by reading that file. And `demostf/parser`, the
reference implementation cross-checked against throughout this project, declares
`#[discriminant_bits = 6]` — it hardcodes the same assumption and cannot read a protocol-15 demo
either. Neither of the two best available sources contained the answer.

**How it presented, which is worth recording because it named nothing useful.** The first stop
was `Unrecognised message id 52 at bit 638`, inside the signon. Downstream: 11,002 unreadable
packets out of 11,007, a decoded server protocol of **25,482** against a header saying 15, an
empty map name, and zero game event definitions. Nothing in that pointed at a field width.

The tell, in hindsight, is arithmetic — the same move that settled B14. **Five bits cannot
produce an id above 31.** An unrecognised id of 52 is not a message this parser has not
implemented; it is proof the reader is not aligned to the type field at all.

**The fix has an ordering constraint worth stating.** The width cannot come from
`svc_ServerInfo`, because ServerInfo is itself a message and reading it requires the width
already. It comes from the demo header, which is the only source available before the first
message is read. `NetDecodeState.NetworkProtocol` is seeded there and defaults to 24.

**After the fix, the same demo:** zero stops, protocol 15, map `cp_badlands`, 156 event
definitions, 10,998 entity snapshots, 70 game events across 13 types, and the recording player
resolved by name out of the `userinfo` table.

**Boundary SETTLED at 15→16, 2026-08-10.** It was open — 15 measured at five bits, 24 at six, the
flip somewhere in 16–23 — and the code guessed 15 as the last five-bit protocol on the reasoning
that 16 is where Replay shipped. The guess was right, and it is now measured rather than reasoned.

A June 2011 client (build 4604) records at **protocol 16**, and its demos decode end to end:
11,131 commands in the POV and 3,769 in the SourceTV, no stops, no undecoded markers. **That
result is only possible at six bits.** A five-bit read at protocol 16 desynchronises the first
message of the signon, which is exactly the wreckage this entry describes — so the demo
distinguishes the two widths on its first packet, and it chose six.

The failure being loud is what made the guess tolerable in the meantime; it is not what made it
correct. Contrast B14, which was latent for the entire life of the project.


## B18 — the property type enum was renumbered, and neither list mentions it

**Found and fixed 2026-08-09, minutes after B17 and by the same demo.**

`DPT_VectorXY` was inserted at position 3 in Valve's `SendPropType`, pushing the three types
above it up by one. Confirmed by diffing `public/dt_common.h` between the `orangebox` and `tf2`
branches of `alliedmodders/hl2sdk`:

```
2009     Int=0 Float=1 Vector=2            String=3 Array=4 DataTable=5
current  Int=0 Float=1 Vector=2 VectorXY=3 String=4 Array=5 DataTable=6
```

Reading a 2009 schema with the current numbering turns every nested table into an array. The
schema is where entity decoding begins, so the file becomes unreadable a few hundred bits in —
`SendTableParser.Parse` died at bit 705,065 of a 705,072-bit payload, having consumed almost
exactly everything and then read one field too many.

**How it was localised, which generalises.** Not by reading — by diffing the *same table* across
eras. `DT_AI_BaseNPC` has 12 properties in both a 2020 demo and the 2009 one, so its bit offsets
are directly comparable:

```
z1800  [124->409] type 6 flags 0x1000 baseclass dt:DT_BaseCombatCharacter
2009   [124-> 235] type 5 flags 0x1000 baseclass elems:68
```

Identical name, identical flags, identical start offset — and a type value one lower. That
pinpoints the field in one line of output. A parser that only reports "failed at bit N" cannot
do this; a differential across two specimens of the same structure can. Same technique that
settled the flattening order (D12), applied across eras rather than across implementations.

**Absent from `proto_version.h`**, like B17. Two of the four era differences found by decoding a
real old demo are invisible in Valve's own enumeration of era differences, which sets the ceiling
on what D20 can be trusted to cover: it lists what the *engine* branches on, not what changed.

**Boundary SETTLED at 15→16, 2026-08-10, by the same demo that settled B17.** Protocol 15 uses the
2009 numbering and 24 the current one, both measured; the change was somewhere in 16–23.

The protocol-16 demo parses its schema with the **current** numbering — 256 server classes, and
every decoded entity property matching the class it was read for. Under the 2009 numbering every
nested table reads as an array, so the schema dies a few hundred bits in and no entity decodes at
all. Getting a whole schema and matching properties is not something the wrong numbering produces.

So `DPT_VectorXY` was inserted between protocols 15 and 16 — the same boundary as the message type
width, which is consistent with both being part of whatever wire change earned protocol 16.


## B19 — a fourth era difference, and this one is silent

The `userinfo` string table stores a *rendered* Steam id, and the rendering changed:

| Era | Format |
|---|---|
| 2009 | `STEAM_0:0:0` (Steam2) |
| current | `[U:1:1234567]` (Steam3) |

Cosmetic rather than structural — nothing downstream fails on it, which is precisely why it is
worth pinning. B17 and B18 announced themselves by destroying the decode. This one would quietly
reshape any output keyed on the id, and a test that only knew the Steam3 shape would have called
a correct 2009 read a failure.

Both forms are now accepted, alongside `BOT`. The assertion stays narrow — reading the field at
the wrong offset produces leftover bytes from the name or friends field, which is text but
matches none of the three shapes.


## B20 — a corpus helper that yielded nothing, and the tests built on it passed anyway

`CorpusEntityDecodeTests.Snapshots` built its `NetDecodeState` without the demo's protocol. For
every protocol-24 demo that is harmless. For the protocol-15 demo it meant the message reader
found no messages at all (B17), so the helper yielded **zero** snapshots — and every test built
on it iterated zero times and passed.

**This is the failure mode that is hardest to notice, and the corpus made it likely rather than
unlucky.** A test whose loop body never runs reports success identically to one that ran and was
satisfied. Nothing was red, no assertion was weakened, and the entity-decode suite silently
stopped covering the one demo it had just been extended to cover.

The general shape: a shared helper that filters, and a new corpus entry the filter silently
excludes. Adding a specimen does not extend coverage on its own — the helpers have to reach it.
Two other helpers had the same defect and were fixed when they failed loudly
(`CorpusPlayerTests`, `CorpusSchemaTests`); this one did not fail, which is why it was found
later and by accident, while diagnosing something else.

**Guard added rather than just the fix.** The tracker tests assert `ActiveEntities` is not empty
and that coordinates were found, per demo and named. A helper that yields nothing now fails on
the demo it yielded nothing for, instead of quietly agreeing.

**Watch for this whenever a demo is added to the corpus.** The check is not "do the tests still
pass" — it is "did the count of things each test examined go up".


## B21 — the two output writers are the least-tested code in the project

Core mutation run, 2026-08-09: **85.50% overall**, above the gate. The per-file breakdown is the
finding, and the score hid it:

| File | Killed | Survived | No coverage | Score |
|---|---|---|---|---|
| `DemoJsonLinesWriter.cs` | 16 | 19 | **47** | **19.5%** |
| `DemoTraceWriter.cs` | 40 | 29 | 13 | **48.8%** |
| `DemoTextDumper.cs` | 81 | 8 | 15 | 77.9% |
| `DemoScan.cs` | 21 | 1 | 1 | 91.3% |
| `EntityTracker.cs` | 15 | 1 | 0 | 93.8% |
| `Snappy.cs` | 83 | 1 | 0 | 98.8% |

**The trace is the primary deliverable (D18) and is the second-worst covered file in the
repository.** Every decoder it depends on scores in the nineties. That is exactly backwards, and
no aggregate would have shown it — 85.5% passes.

**One defect shape explains almost all of it.** The writer tests assert that output is
*trace-shaped* or *JSON-shaped* — that it contains `block dem_`, that lines start with `{` — and
never that a given field carries the right value. So mutating field after field survives:

```
x2  WriteField(writer, "map", Quote(header.MapName));          // trace
x2  json.WriteString("client", header.ClientName);             // json lines
x4  json.WriteNumber("tick", tick);
```

Every one of those is a mutant that swaps or blanks a value the reader is meant to trust. A
report that names the wrong map is worse than one that fails.

**Three survivors are worth more than the rest, because they are logic rather than transcription:**

- `Quote()`'s escape cases — `"`, `\`, `
`, `
` all survive being mutated away. Escaping is
  the difference between a trace that can be read back and one that cannot, and a server name
  with a quote in it is not hypothetical.
- `options.EntitySnapshotLimit <= 0 || snapshots < options.EntitySnapshotLimit` — the "zero means
  all" rule, which is the same rule that survived in the CLI until this run's sibling found it.
- The progress throttle condition, `scanned % ProgressInterval == 0 || scanned == commands.Count`.

**Do not chase the transcription mutants one by one.** The right fix is one test per writer that
pins a whole small output against an expected string, built from a hand-made header and a couple
of commands — a golden-output test. That kills the field mutants as a class and stays readable,
where forty individual assertions would not.


## B22 — mid-game joins are invisible, and one literal makes it worse than missing — FIXED

**Owner-reported, 2026-08-09:** players dropping and rejoining mid-match is routine in TF2 and
always has been. This parser does not see them.

Three linked defects, in the order they must be fixed.

### 1. `DemoScan` reads only `CreateStringTableMessage`

`userinfo` is created once, during signon. Everyone who connects later arrives as an
`svc_UpdateStringTable`, which `DemoScan.CollectPlayers` ignores outright. So the roster in the
text dump, the JSON Lines output and the trace is **the signon roster, not the match roster**.

Invisible until now because every corpus check asks whether the roster is *plausible* — names
non-empty, ids in range, count under 64 — and a truncated roster passes all of them. The same
shape as B20 and B21: the assertion cannot tell a complete answer from a partial one.

### 2. An update names its table by id, and nothing records names

`UpdateStringTableMessage` carries `TableId` and `Entries`, no name. `NetDecodeState` records
only capacities, by creation order. There is currently no way to ask "is this update for
`userinfo`?"

### 3. `ReadUpdate` hardcodes `fixedUserData: false`

```csharp
ReadEntries(ref bodyReader, entryCount, maxEntries, false, 0)
```

The *create* path reads that flag and its width from the wire, per table. The update path passes
a literal. If `userinfo` sets fixed user data size, its updates decode with the wrong entry
layout — plausible-looking garbage player records rather than an obvious failure.

**Verify before fixing.** Does `userinfo` set the flag, and does any corpus demo actually carry
`userinfo` updates? A SourceTV match demo should, given mid-game joins are routine.

### It gates more than the roster

Static entity baselines arrive in the **`instancebaseline`** string table and are *rewritten*
during a match, through this same unhandled update path. So:

```
B22  ->  mid-game joins in the roster
     ->  instancebaseline updates visible
           ->  static baselines
                 ->  EntityTracker's documented gap closes
                       ->  Phase 2 viewer has real starting state
```

A remark about players rejoining turned out to gate the entity model. Nothing in the code
connects the two.

### A near miss worth recording

`Corpus.Players()` walks every command of every demo, and stopping at the first `userinfo` table
looked obviously safe — "the table is created once, everything after is waste". It is wrong for
exactly the reason above. What caught it was asking the owner rather than reasoning about the
code.


### B22 resolution, 2026-08-10

**Verified before changing anything**, because two of the three suspected defects turned out
differently than reasoning suggested.

**Measured across the corpus.** Every demo carries `userinfo` updates — z1800 has 18 updates
holding 42 entries. Two demos are literally missing a player:

| demo | from create | new in updates | true roster |
|---|---|---|---|
| `demostf-koth_product_final` | 18 | +1 | **19** |
| `z1800` | 25 | +1 | **26** |

Several more have *stale* records rather than missing ones, where a player reconnected into an
existing slot — a failure a count cannot see.

**Defect #3 was not real.** `ReadUpdate`'s hardcoded `fixedUserData: false` is *correct* for
`userinfo`: its update entries decode to exactly 132 bytes, matching `PlayerInfo.RecordBytes`,
across every demo. The literal remains wrong in principle for a table that does set the flag, but
nothing in the corpus is being corrupted by it. Recorded rather than "fixed" on suspicion.

**What the fix needed instead.** `UpdateStringTableMessage` names its table only by creation-order
id, so `NetDecodeState` now records table *names* alongside capacities. And update entries carry
no text at all — so the entity index had to come from `entry.Index`.

That is safe because **the entity index *is* the entry index**: in a create message each entry
also carries its index as decimal text, and the two agree on every entry of every corpus demo.
Using the index for both paths is what lets creates and updates share one code path
(`RosterBuilder`) instead of diverging.

Where an entry carries text and it *disagrees* with the index, the entry is skipped rather than
resolved by preference — a disagreement means one of the two readings is wrong, and a missing
player is a better failure than a confidently wrong one. That guard immediately caught a
synthetic fixture in `DemoTextDumperTests` which had been writing text `"1"` at index 0.

**Entries with no user data are skipped, not removed.** They mark a slot being vacated, and the
question this answers is "who played in this match", not "who is connected now". A slot later
reused overwrites the record, which is correct for both readings.

Still open, and now unblocked: `instancebaseline` updates use the same path (62 in one demo, 13
in the 2009 one), so static entity baselines are the next step — see `DECISIONS.md` D24 and the
baseline research.

## B23 — the schema's bit-count field is six bits before protocol 15 — FIXED

A protocol-14 demo threw `EndOfStreamException` parsing `dem_datatables`, having consumed all but
two bits of an 85,848-byte payload. The message stream decoded perfectly: 12,608 commands, no
stops. Only the schema failed.

**The first report of this demo said it decoded end to end, and that was wrong.** The trace was
checked for stop markers and found clean — but `--trace` without `--entities` never touches the
schema, so the check could not see the failure. A measurement that cannot observe the thing it is
asked about returns a clean result, and the clean result is worthless. Same family as B20.

**The end of the stream is not where the bug is.** Consuming 686,782 of 686,784 bits reads like an
off-by-one at the tail; it is not. The parser read **one** table where the 2009 demo reads 334,
then wandered through garbage for the rest of the payload and stopped when it ran out. The last
bit consumed says only where the wandering ended.

Found by differential comparison against the 2009 demo, which is the same method that settled B18:

- Both files' first table is `DT_AI_BaseNPC`, 12 properties. Properties 0 and 1 cost identical
  bits in both — 285 and 188 — so the reader was still synchronised entering property 2.
- The raw bits located the discrepancy exactly: protocol 14 at bit 597 holds what protocol 15
  holds at bit **598**. One bit fewer, somewhere in
  `type(5) + name + flags(16) + low(32) + high(32) + bits(N)`.
- `188 = 5 + 96 + 16 + 32 + 32 + 7` accounts for every bit of property 1 under the modern layout,
  so N is the only free field.

**N is 6 at protocol 14 and 7 from 15 on.** Cross-checked against an unrelated part of the same
file rather than against the hypothesis that produced it: at six bits the schema yields **216
server classes**, and `svc_ServerInfo` independently reports `max_classes 216`. At seven it yields
one table. Six breaks the 2009 demo, so the rule is era-specific rather than a universal fix.

Absent from `proto_version.h`, like B17 and B18. Six bits holds 0–63, enough for any property
Source sends, so the widening bought headroom rather than fixing a limit — no reason to write it
down, and no way to find it except by decoding a demo old enough to carry it.

**Two corpus tests encoded modern assumptions and had to be corrected, not relaxed:**

- `Container_EveryCorpusDemo_WalksCleanlyAndAgreesWithItsHeader` required exactly one
  `dem_stringtables` command from every demo. Protocol 14 carries **none** — the tables arrive
  only as `svc_CreateStringTable` in the signon stream. Now asserted in both directions by era,
  because a modern demo that stopped carrying it would still be a real regression.
- `CorpusNetMessageTests` decoded packets with `NetMessageReader.Read(payload)` and no protocol,
  defaulting to 24 and a six-bit message type where protocol 14 and 15 write five. **This had
  been silently wrong for the 2009 demo the whole time**: reading six bits where five were written
  yields the same value whenever the sixth bit is zero, which for a first message it usually is.
  The test passed by coincidence until a demo arrived where the coincidence did not hold.

## B24 — SourceTV truncates the schema at 64 KiB on TF2's launch build — HANDLED, NOT FIXABLE

The protocol-11 SourceTV demo throws parsing `dem_datatables`, three bits from the end of a
**65,536-byte** payload. The POV recording of the *same session* carries **85,063 bytes** and parses
cleanly.

**That pair is the whole diagnosis.** One file alone reads as a parser bug; two recordings of one
session, differing only in writer, say the schema is genuinely larger than 64 KiB and SourceTV cut
it. Nothing in the parser can recover what was never written.

**It is not an interrupted recording**, which was the first thing suspected:

- 65,536 is exactly 2^16 — an early stop yields an arbitrary size
- `dem_datatables` sits in the **signon block at the start** of the file, where a truncated capture
  cannot reach
- the frame check is exact: 3,897 packets against 3,897 declared
- the file ends with `dem_stop`

**Handled by refusing, not by guessing.** `SendTableParser.Parse` now catches the overrun and
throws `InvalidDataException` naming the truncation, rather than letting an `EndOfStreamException`
about bit offsets escape. A partial schema is worse than none: flattening a half-read table
produces property indices that address real fields at wrong positions, which is the failure mode
that makes a demo look decoded while every value is wrong.

Everything else about the file works — 3,903 commands, all messages, chat, events, string tables.
Only entities are unavailable. `Corpus.FilesWithSchema()` excludes it from tests that need a
schema, and `LaunchBuildSourceTv_TruncatesItsSchemaAtSixtyFourKilobytes` asserts the truncation
directly so the exclusion is a recorded finding rather than a silent skip.

**Confirmed on a second recording, and the cap is map-independent.** A separate protocol-11
SourceTV demo on **cp_gravelpit** — different session, different map, different schema — truncates
at exactly 65,536 bytes as well:

```
cp_granary    STV   65,536   truncated
cp_gravelpit  STV   65,536   truncated
cp_granary    POV   85,063   parses
```

Two independent recordings landing on the identical power of two is not a schema that happens to
exceed a limit; it is the writer's buffer. One specimen left open whether cp_granary was simply a
large map, and the second closes it. The gravelpit demo lives in `tools/corpus/local/` rather than
the committed corpus: it is a second specimen of an era already represented, and the finding it
supports is recorded here.

## B25 — a UBitVar one step wider than it needs to be, on 0.16% of modern snapshots — OPEN

**Found by re-encoding, and by nothing else, because both forms decode to the same number.**

`EntityDecoder.EncodeEntities` reproduces 13,942 of 13,973 entity snapshots bit for bit across
the corpus (2026-08-11). Every demo recorded before 2013 is at 100%. The 31 exceptions are all
modern, and they split into two kinds:

| Kind | Count in the sample | Status |
|---|---|---|
| Difference past the last bit written | 3 of 6 inspected | Not a decode error — see below |
| Wider UBitVar selector at a LEAVE update's entity index | 3 of 6 inspected | **Open** |

**Trailing slack is not a defect.** A packet entities body states its length in bits and the
sender builds it in bytes, so the stated length can run past the last meaningful bit, and nothing
requires the leftover to be zero. The re-encoder pads with zeros; the demo sometimes has something
else there. The removal list terminates on a clear bit, so those bits are never read.

**The wider selector is genuinely unexplained.** At the entity index preceding a LEAVE update:

```
wire  011100011000     selector 2 -> 12-bit payload
ours  101100011010     selector 1 ->  8-bit payload
```

Both decode to the same entity index, which is precisely why no other test could see it — the
decode is correct, and only the re-encode disagrees. Two readings are possible and the corpus
cannot yet separate them:

1. The engine's `WriteUBitVar` is not canonical on this path, and picks a wider bucket under some
   condition (a LEAVE update is the only shape observed so far, which is suggestive but is three
   samples).
2. The value being encoded is not the delta this project computes, and happens to agree modulo the
   payload width.

**Not papered over.** A heuristic that widens the selector for LEAVE updates would take the number
to 100% and prove nothing; the encoder writes the canonical form and the report carries the
exceptions. Resolving it wants the engine's writer, in the manner of
`docs/memory/read-the-encoder-not-the-decoder.md` — an encoder states intent that a decoder only
implies.

## B25 update, 2026-08-11 — the wider UBitVar is carried now, and a measurement disagreement is open

**The width is recorded rather than guessed.** `UBitVar.Read` reports the payload width the sender
chose and `DecodedEntity.IndexPayloadBits` carries it, so an index sent at twelve bits where eight
would do is written back at twelve. This is the same answer the format has demanded everywhere
else: which encoding the sender picked is not recoverable from the value, so the shape has to
travel with it.

**Two other things were wrong in the original entry and are corrected here.**

The rate was understated. B25 said 0.22% of snapshots, measured over the first 900 commands of each
demo. Over whole demos it is nearer 3%, and on some files far more — the shape is commoner later in
a match than at the start, which is exactly what a sample from the opening minute cannot see.

What was called "trailing slack" was mostly this same bug wearing a disguise. A canonical encoder
produces a *shorter* body, so the first difference appears past the end of our content and reads as
a padding difference. Fixing the width made those disappear rather than moving them.

**The instruments disagreed, and the reason was a measurement bug — mine, not the decoder's.**
`CorpusEntityRoundTripTests` compared the whole *stated* body length. `EncodeEntities` is given
entities, not the sender's buffer, so it zero-fills anything past its last field — and the
comparison was reading that zero-fill against whatever the sender actually left there. Over whole
demos it reported 96.87%; comparing the **content** it writes, it is **99.59%** (1,035,847 of
1,040,124). The assembly writer never had the problem because it carries those bits explicitly on
a `slack` line.

The leftover is a fact about the format and is now reported as one: **32,407 snapshots end before
their stated length, 3,474,371 bits in total.** A body is measured in bits and built in bytes.

**The residue is one family now, and three hypotheses are dead.** Isolating the exact property
whose bits diverge, rather than the snapshot, leaves:

```
  148  Vector flags=0x2400  SPROP_COORD_MP
   19  Vector flags=0x8400  SPROP_COORD_MP_INTEGRAL
    3  Float  flags=0x8804
```

Nothing else. Every other kind of property re-encodes exactly on every demo.

**It is not item or loadout data.** That was the standing guess and the classes refute it — scene
entities, animation overlays, sprite trails, projectiles, ammo packs. None carry economy
attributes, and the versions of TF2 with no item system fail nothing at all.

**Ruled out by experiment, not by argument:**

| Hypothesis | Test | Result |
|---|---|---|
| Property index deltas use a non-minimal UBitVar | carry the width, as entity indices do | **confirmed and fixed** — removed 97 of 267 |
| The in-bounds bit is not "narrow when it fits" | invert the rule | 267 to 80,438, so the rule is right nearly always |
| The fraction is truncated, not rounded | truncate and mask as `bf_write` does | no change at all |

**Resolved by adopting Valve's rule and recording what it cannot derive.** Two changes, in that
order, because the order is the point.

First, the sign predicate became Valve's: `signbit = (f <= -COORD_RESOLUTION)`, not "is the value
negative". Taken from `bf_write::WriteBitCoordMP` in `src/tier1/bitbuf.cpp`. It scored *worse* on
its own — 13,966 exact became 13,965 — and was adopted anyway, because using a rule known to be
wrong on the grounds that it fits the corpus better is fitting to the corpus. A correct rule that
produces more mismatches is evidence about the values reaching it.

Second, that evidence was chased. A value-only round trip — encode a decoded coordinate, decode it
again, compare — passes on **1,001,048 of 1,001,048** components. So the encoder and decoder agree
about every value, and what remained could only be a *choice*: which of two encodings of the same
value the sender used. `DecodedProperty.CoordShape` now records the in-bounds bit per component and
the encoder honours it, exactly as `IndexPayloadBits` does for index deltas.

```
                       content re-encoded exactly
before                 1,007,612 of 1,040,124   96.87%   (measurement bug included)
after the measurement fix                       99.59%
after property index widths                     99.95%   (at a 900-command sample)
after Valve's sign rule + recorded coord choice  1,039,144 of 1,040,124   99.91%
```

**980 snapshots (0.09%) still do not re-encode, and the failure has a precise shape.** Every one
is a *delta* snapshot with removals, our re-encode is exactly 2 bits longer than the demo's stated
body, and our bits match the wire up to that stated end. Dumping the tail from where the entity
section stops:

```
wire = 10111011000        11 bits
ours = 1011101100000      13 bits
```

Ours is the wire plus two zeros. Both decode to the same removal — flag set, index 110.

**Writing more bits than the wire is a bug, not a format quirk.** It is at the very end of a body
and the assembly writer truncates to the stated length, so demos still rebuild byte-exactly — but
producing bits nothing asked for means the model of this section is wrong somewhere upstream.

**The arithmetic constrains the answer.** Our model says one removal costs a flag bit, an 11-bit
index and a terminator: 13 bits. The wire spends 11. The sender cannot simply have overrun its own
stated length, because the bits after a body belong to the next message and a demo that did that
would not play. So one of these is true:

1. The index is not 11 bits here. A width of 10 plus flag, with the terminator omitted because the
   body ends, is exactly 11 — the only reading that fits without an overrun. Forcing 10 globally
   removed every content mismatch but made *more* snapshots overlong, so it is not a constant.
2. The entity section before it is 2 bits longer than ours, and our bits match only because the
   difference is absorbed where the two sections meet.

**Answered, by reading the engine.** `engine.dll` from the 2013 build, in Ghidra, contains the
deletion writer with `bf_write` inlined. Two constants this project had guessed correctly — the
index is 11 bits, the terminator is one clear bit — and one behaviour it had never modelled:

```c
if ( bitsAvailable - currentBit < 0xb ) {   // fewer bits left than the field is wide
    currentBit = bitsAvailable;             // consume the remainder
    overflowFlag = 1;                       // and write NOTHING
}
```

**`bf_write` silently gives up when the buffer fills.** The flag bit is written first, then the
index is refused for want of room. So a body can legitimately end with a set flag and no index
behind it: the engine intended a removal, ran out of buffer, and stopped. The terminator is
refused for the same reason.

That is the whole of the two bits. Our decoder read an index out of the unwritten remainder —
inventing a deletion that never happened, with a plausible entity number and no error anywhere —
and our encoder then wrote flag, index and terminator where the demo had flag and nothing.

**Both sides now model it.** The decoder stops when fewer than eleven bits remain *after* reading
the flag, and the encoder omits a terminator that will not fit. Note the ordering: an earlier
attempt refused to read the flag unless a whole entry fit, which also discarded the last
legitimate removal of a body ending exactly after it. The engine writes the flag first, so the
check belongs after it.

**Result: 366 snapshots that previously failed to decode now decode**, 1,040,124 to 1,040,490
across whole demos, and 282 of those re-encode exactly. 1,064 still do not, so this was a
mechanism rather than the last one.

**Four earlier hypotheses, all reverted, all wrong:** a bounds check before the flag, a constant
10-bit index, a width derived from `max_entries`, and sender truncation of the message itself.
The last is worth restating precisely: the *message* is never truncated, but a *field inside it*
can be.


## B26 — the Damage user message has an older layout at protocol 14 and below — RESOLVED

`svc_UserMessage/Damage` is what draws a damage number in a POV demo, and it is the only record of
the *direction* incoming damage came from — entity positions say where everyone stood, this says
which of them hurt you and by how much.

The layout is Valve's, from `CHudDamageIndicator::MsgFunc_Damage` in
`src/game/client/tf/tf_hud_damageindicator.cpp`:

```c
damage.iScale = msg.ReadShort();     // 16 bits
msg.ReadLong();                      // 32 bits, read and discarded by the game
if ( !msg.ReadOneBit() ) return;     // 1 bit: does a position follow
msg.ReadBitVec3Coord( vecOrigin );   // 3 presence bits, then the axes that were sent
```

**The discarded long still has to be read.** The game throws it away, but it occupies 32 bits, and
skipping it takes the position from the wrong place — producing a plausible coordinate rather than
an error.

**Protocol 14 and below send a different message: one byte of damage, then the vector.** No
damage-type long, no bit saying whether a position follows, and the vector is always there.

**The hypothesis this entry used to carry was wrong, and worth keeping for the shape of the error.**
It guessed TF2 had inherited HL2's Damage message, which reads a byte of armour, a byte of damage,
a long, and a vector — "48 bits before the vector where the modern form has 49", so the two would
sit one bit apart and a wrong guess would produce a plausible position. Reading the HL2 file
instead of recalling it kills that immediately: it does not send a vector at all, it sends three
raw `WRITE_FLOAT`s, and the whole message is a fixed 144 bits. It was never a candidate for a
77-bit body. That is *research before code* — the guess was the cheap part, skipping the
verification was what would have cost.

**What actually identified the layout was arithmetic on lengths, before any bytes were read.** The
protocol-14 bodies are 77 bits and 72 bits. A `BitVec3Coord` is three presence bits plus its axes,
and an axis is 22 bits with a fraction or 17 without, so a full vector is 69 and one bare axis
makes it 64 — leaving exactly 8 bits of header either way. The same five-bit step appears between
the modern 118 and 113, which is what says the two eras share a vector encoding and differ only
ahead of it. The leading byte then reads 36, 40, 50, 44 across the demo, and TF2 damage looks like
that.

**Two defects, and the second is the one to remember.** The layout was wrong, but the check that
should have caught it was also wrong: the decoder accepted `BitsRead <= bodyBits`, and the modern
layout fits *under* 77 bits. So 20 of the demo's 24 messages reported invented fields — `damage=16164`
against a game whose largest single hit is about 450 — while the other 4 overran and were refused.
A stated length that is exact in bits (these bodies end mid-byte, which proves it) must be checked
with `==`. A lenient bound does not tolerate rounding, it accepts any layout short enough.

**Verified against two decoders that share nothing with this one.** At tick 280 the camera is at
(-1012.4, 6068.7, -398.5) from the container's `democmdinfo` prologue, an explosion sound is at
(-1008, 6064, -352) from `svc_sounds`, and the damage origin reads (-1061.5, 6127.0, -355.0).
Across the corpus the damage origin now sits a median 57 units from the camera at protocol 14 and
21–57 at every later era, with no message beyond 140 — the distribution an era gets when it is
right, and self-damage from a rocket jump is what puts the origin on top of the player. Before the
fix the protocol-14 demo produced no complete vector at all.

**Protocol 11 was an interpolation for about an hour, and now is not.** The committed protocol-11
files carry no Damage message at all — nobody was hurt in them — so the rule below 14 initially
rested on nothing. Closed by recording one deliberately: a soldier next to a resupply cabinet,
rocket-jumping into himself for 52 seconds, which produces 43 damage messages in a 460 KB file.
All 43 decode, at the same 77 and 72 bits as protocol 14, with damage 27–46 and origins a median
79 units from the camera. `tf2-2007-build3258-pov-damage.dem`, local corpus.

**That is the cheapest evidence available on this axis and it generalises.** A period client that
runs can be made to emit any message on demand — the question "does this era send X differently"
does not need a matching competitive demo to turn up, it needs someone to do the thing that sends
X. Protocols 12–13 and 17–23 still have no specimen, so those remain interpolations, but for every
era whose client runs a missing message is now a recording task rather than a search.

Measured at 11, 14 and 15. The change is between 14 and 15.

## B27 — array elements lost their encoding shape, so arrays re-encoded wider — FIXED

Found 2026-08-11 by enlarging the local corpus with thirteen demos.tf recordings chosen for map and
mode variety. The gate that caught it, `TheEntitySectionEncodesToExactlyWhatItDecodedFrom`, compares
our decoder's consumed bits against our encoder's produced bits for the entity section alone, so it
cannot be confused by the removal list.

**111,219 of 111,228 agree exactly. Nine do not, and all nine produce MORE than they consumed:**

```
    4  +3 bits
    4  +15 bits
    1  +6 bits
```

**The demo names are the diagnosis.** Attributing each failure to its file splits them into two
populations that have nothing to do with each other:

| bits | demos | shape |
|---|---|---|
| +3, +6 | `ctf_tinybine` | delta snapshots, 13–14 entities, last is Delta with 1 property |
| +15 | `pass_coastal_rc8`, `pass_sanctum_a2a` | **both PASS Time maps**, and both a full and a delta snapshot each |

### The `ctf_tinybine` group looks like the writer giving up

In four of the five, the snapshot's stated length exceeds what the decoder consumed by **exactly
32 bits** — 2704 against 2736, 2332 against 2364, 3001 against 3033, 3347 against 3379. That is the
`bf_write` signature already documented in `docs/findings/09-valve-implementation.md`: a field that
did not fit was abandoned rather than truncated, so the message ends early and the trailing bits
are whatever they were.

If that reading is right, the encoder is not wrong so much as *too complete* — it writes the form
the sender intended rather than the one the sender managed. Reproducing that faithfully means
recording that the writer gave up, which is the same "record the encoding shape" problem that has
recurred six times elsewhere.

The fifth case contradicts it and is the one to look at first: `consumed 3112, stated 3109`. The
decoder consumed **three bits more than the snapshot claims to hold**, which no amount of writer
overflow explains.

### The PASS Time group is a genuine encoder defect

This one has no overflow in it at all. `consumed 117298, produced 117313, stated 117298` — the
decoder consumed exactly the stated length, and the re-encode is fifteen bits longer. Same for
`pass_sanctum` at 147161.

So something in a PASS Time schema encodes wider than it decoded. The mode ships entity classes no
other map has, and PASS Time is the first non-standard mode ever added to this corpus — it had
never been exercised before these downloads. Fifteen bits is a specific enough number to be one
property, or three of five.

**Next step**: narrow to the property. The snapshots are large (215 and 290 entities), so the way in
is to re-encode entity by entity and find which one's width disagrees, then which of its properties.

### Why this matters beyond nine snapshots

The residue was **zero** across a corpus of thirteen demos that were all 6v6 competitive on five
maps. Thirteen more recordings, chosen for variety rather than volume, found it immediately. The
lesson is the same one the user message layer taught on the same day: **a corpus that is uniform in
shape cannot falsify assumptions that its shape makes true.**

### Resolved, same day

**Both groups were one bug: an array's elements each carry a coordinate shape, and none of them
were kept.** `ReadArray` called the overload of `ReadValue` that discards the shape, so every
element decoded, threw its shape away, and re-encoded as shape 0 — the original width only when
the sender happened to have used the shape a shapeless encoder derives.

That explains the sizes without any further theory. `m_trackPoints` on the PASS Time maps holds 16
elements and came out **+15**; `m_vecPoints` on `ctf_tinybine` holds 1 and came out **+3**. The
`bf_write` overflow reading of the `ctf_tinybine` group was wrong — the 32-bit shortfall against
the stated length was real but incidental, and had nothing to do with the mismatch.

`DecodedProperty` now carries `ElementShapes`, and after the fix **111,228 of 111,228 snapshots
re-encode to exactly the bits they decoded from**, across all thirty demos.

**How it was found matters more than the fix.** A snapshot-wide bit count says nothing about which
of 290 entities caused it, so `EntityDecoder.EntityEndBits` was added — each entity's decode end
offset — and re-encoding growing prefixes narrows a mismatch to one entity, then one property.
That diagnostic is kept; it is what turns "nine snapshots disagree" into "class 240, `m_trackPoints`".

**Regression coverage is synthetic, deliberately.** The gate that caught this walks the local
corpus, which is git-ignored and invisible to CI, so `ArrayElementShapeTests` asserts the same
property against a hand-built schema. Verified by sabotage — and the sabotage exposed a second
lesson: a re-encode comparison whose *original* came from our own encoder cannot catch an error
applied uniformly, because it appears in both sides. Only the shape-survival assertion fails.
That test now says so rather than implying a correctness guarantee it does not provide.

## B28 — `VoiceMask` is 9 bytes at launch and 33 today, and only the modern width is implemented — RESOLVED

Found 2026-08-11 by reading `usermessages->Register` out of six shipped clients rather than from
any SDK. Valve registers each user message with a byte size, and `VoiceMask` writes
`VOICE_MAX_PLAYERS_DW` dword pairs — audible mask, server-banned mask — then one flag byte, so
`size = 8 × dwords + 1`:

| build | registered size | implied `VOICE_MAX_PLAYERS` |
|---|---|---|
| 2007 launch, 2008 | **9** | 32 |
| 2009 | **17** | 64 |
| 2011, 2013, 2026 | **33** | 128 |

`UserMessageBody.VoiceMaskDwords` is 4, i.e. 33 bytes only. A protocol-11 or 14 `VoiceMask` is a
quarter that width and a protocol-15 one is half.

**Severity is low and the reason is worth stating: it fails safely, and not by luck.** The reader
requires *exact* consumption (`==`, not `<=`), so a 9-byte body simply refuses the 33-byte layout
and the message is reported by id with no fields. Under a `<=` check it would have read sixteen
dwords of adjacent bits and reported them as mute state — plausible numbers, no error. That rule
was adopted for `Damage` under B26 and has now paid for itself twice, the second time on an era
and a message nobody was examining.

**Not yet observed firing**, because no corpus demo has been shown to contain a pre-2011
`VoiceMask` — it is sent on voice state changes and the era demos are short listen-server
recordings. So this is a known gap rather than a known failure.

**Fix, when done, is a width chosen by era**, which is the same table-selection problem as B29 and
should land with it rather than as a second one-off protocol conditional.

**Resolved 2026-08-11**, and it did land with B29. `UserMessageBody.VoiceMaskDwordsFor` selects
1 dword pair at protocol ≤ 14, 2 at 15, and 4 above — the three registered sizes, 9 / 17 / 33
bytes, from the table above. Still not observed firing on the corpus, so this is a layout the
binaries state rather than one a demo has exercised: the era specimens are short listen-server
recordings and none of them contains a pre-2011 `VoiceMask`. The exact-consumption rule remains
what makes an era mismatch refuse rather than fabricate.

This entry stayed marked OPEN after the code shipped, which is its own small lesson: a risk
register is only as good as the pass that closes entries, and nothing in the build can catch a
stale one.

## B29 — protocol 24 cannot select a user message name table above id 50 — RESOLVED

The user message id table belongs to the **game DLL**, and the protocol number belongs to the
**engine**. They move independently, and protocol 24 has now spanned thirteen years.

Measured from the binaries on 2026-08-11:

| build | registers | ids | notes |
|---|---|---|---|
| 2007, 2008 | 29 | 0–28 | ends at `PlayerStatsUpdate`; no haptics block |
| 2009 | 41 | 0–40 | ends at `CheapBreakModel` |
| 2011 | 49 | 0–48 | ends at `PlayerBonusPoints` |
| **March 2013** | **66** | **0–65** | **no `RDTeamPointsChanged`** |
| **July 2026** | **79** | **0–78** | ends at `BuiltObject` |

Both 2013 and 2026 are protocol 24. `RDTeamPointsChanged` was inserted at id 51 some time after
March 2013 — the string appears nowhere in that build — so **every id from 51 up means something
different in the two, while carrying the same protocol number**. Concretely, id 69 is
`HapSetDrag` in March 2013 and `PlayerLoadoutUpdated` in 2026.

`UserMessageNames.Lookup` keys on protocol alone, so it cannot distinguish them. Its type-level
remarks also state the table was transcribed from the 2013 SDK; the binaries show it matches the
**2026** client entry for entry, so the comment is wrong about its own provenance even though the
data is right for modern demos.

**Currently invisible, for a good reason.** The refusing-layout gate withholds a name whenever a
known layout does not fit, which is what leaves the March 2013 demo reporting `#69` rather than a
confident `PlayerLoadoutUpdated`. That is correct behaviour arrived at without knowing this risk
existed. It only covers ids this project has layouts for, though — an id in 51–65 with no layout
would be named from the wrong table silently.

**Fix requires dating a protocol-24 demo**, which the header cannot do: no build number is
carried. Do not add a protocol conditional here; the discriminator is not the protocol.

### The discriminator exists, and it is a date — added 2026-08-11

The owner's point: **the message names are features, and features are dated in changelogs.** So
the table dates itself, and the boundary is a single row.

`RDTeamPointsChanged` is the *only* insertion separating the two protocol-24 tables. Robot
Destruction entered TF2 with `rd_asteroid` in the **8 July 2014** patch, as a Mann Co. beta map.
Therefore:

| a protocol-24 demo recorded | uses | ids 51+ mean |
|---|---|---|
| before 8 July 2014 | the 66-entry March 2013 table | `SpawnFlyingBird` at 51, `HapSetDrag` at 69 |
| after | the 79-entry modern table | `RDTeamPointsChanged` at 51, `PlayerLoadoutUpdated` at 69 |

Three further additions in the same span are datable the same way and can corroborate a
placement — `BonusDucks` (Scream Fortress 2014, 29 Oct 2014), `EOTLDuckEvent` (End of the Line,
8 Dec 2014), `QuestObjectiveCompleted` (Gun Mettle, 2 July 2015). **Observing any of them proves
the modern table** without needing a date at all, which is the more robust form: a demo carrying
`QuestObjectiveCompleted` is necessarily post-2015 regardless of what its header says.

So the implementation has two tiers, and the cheap one is decisive on its own:

1. **Direct evidence.** If the demo contains an id that only one candidate table can explain, use
   that table. The highest id observed already bounds it — a demo reaching id 78 cannot be the
   66-entry build.
2. **Fallback.** Where no such id appears, ids 0–50 are identical in both tables and can be named
   safely; 51 and above stay withheld, which is the behaviour today.

That reduces B29 from "we cannot know" to "we can usually know, and we degrade to the current
correct-but-quiet behaviour when we cannot". See `findings/05` for the reasoning and the caveat
that these dates bound the insertion from *above*.

### B29 narrowed, 2026-08-11 — the era tables are in, and only protocol 24 is still ambiguous

`UserMessageNames` now selects a table per era, with the haptics block appended from 2009 on.
Measured on the committed corpus, four of the five long-unnamed ids resolve:

| demo | was | now |
|---|---|---|
| 2009 POV | `#40`, `#44`×2 | `CheapBreakModel`, `HapSetDrag`×2 |
| 2011 POV | `#41`, `#52`×2 | `CheapBreakModel`, `HapSetDrag`×2 |
| 2011 STV | `#41` | `CheapBreakModel` |
| March 2013 POV | `#69`×3 | **`#69`×3, unchanged** |

**The remaining case is the whole of B29 and nothing else.** Protocols 11–16 are each one measured
build, so their tables are decided. Protocol 24 is not one era, and `#69` stays a number because
the current table would call it `PlayerLoadoutUpdated` — a one-byte message — and the body is 32
bits, so the refusing-layout gate withholds the name. That is the correct answer arrived at
without the era machinery, and it is also the reason this is low priority: **the failure mode is
already quiet rather than wrong.**

What remains is the ids in 51–65 that this project has *no* layout for. There the gate cannot fire,
and an early protocol-24 demo would be named from the modern table silently. None appear in the
corpus, so this is unexercised rather than known-broken.

**The fix is unchanged and now cheap:** pick between the two tables using the ids the demo actually
carries — any of `RDTeamPointsChanged`, `BonusDucks`, `EOTLDuckEvent` or `QuestObjectiveCompleted`
proves the modern table on sight, and the highest id observed bounds it from the other side. That
is demo-level evidence, so it belongs to whatever assembles the decode, not to a function that sees
one id and a protocol number.

### B28 resolved, 2026-08-11 — the width follows the era, and the corpus cannot confirm it

`VoiceMaskDwordsFor` now returns one dword pair at protocol 14 and below, two at 15, and four
above — 9, 17 and 33 bytes, matching the registered sizes in the 2007/2008, 2009 and 2011+
clients. The field *count* follows the width too, so a 9-byte body yields one `can_hear`/`muted`
pair rather than four read off the end of the span.

**Stated plainly: no demo validates this.** Neither corpus contains a single `VoiceMask` — not in
the ten committed demos, not in the local set. The message is sent on voice state changes and
none of the recordings caught one. So the evidence is the registered size in six binaries plus
the tests, and that is all it is.

That makes the tests carry the whole weight, so they are built to fail for the right reason. The
theory asserts each era **refuses its neighbours' widths**, not merely that it accepts its own —
a decoder accepting any of 9, 17 or 33 everywhere would pass an accepts-its-own test at every row
while being precisely the bug. And one case reads a launch-era body field by field, because width
and field count are separate mistakes.

**The 2011 boundary is where the measurement is, not necessarily where the change is.** 2009 and
2011 are the nearest specimens on either side, so protocols 16 and above share the modern width
until a build between them says otherwise. Same shape as every other bracketed boundary here.

### B29 resolved, 2026-08-11 — the body picks the table, and only when it is decisive

**Every user message in the committed corpus now has a name.** The last one, `#69` in the March
2013 demo, reads `HapSetDrag`.

The fix is a second candidate rather than a heuristic. `UserMessageNames.Alternate` returns what
the March 2013 table calls an id — but only at protocol 24, and only above id 50, since the two
tables agree exactly below the `RDTeamPointsChanged` insertion. `UserMessageBody.Decode` reaches
for it **only when the primary name's layout has refused the body**, which is already this
project's standing evidence that a name is wrong.

| case | result |
|---|---|
| primary layout accepts | primary name — unchanged for every modern demo |
| primary has no layout | primary name — nothing contradicts it |
| primary refuses, alternate fits | **alternate name** |
| both refuse | neither claimed, id reported bare |

**Why not the obvious fix.** Withholding names across the disagreement band was the first idea and
it is worse than the problem. `z1800.dem` carries ids **53, 57, 70 and 71** — squarely inside that
band — and it is a modern demo, so all four are named correctly today. Blanket withholding would
destroy four right answers to guard against a case the March 2013 demos do not even exercise:
they carry nothing above id 29 except the one at 69. Evidence beats caution when the evidence is
available.

**A registered size is a layout.** Making this work needed a falsifier for messages whose contents
are never decoded, and Valve supplies one: the size in the `Register` call. `HapMeleeContact` is
registered at **zero bytes**, so a body of any width refutes it outright. Two haptics messages now
carry width-only layouts for exactly this purpose — they read nothing and prove a great deal.

**What is still not solved, stated plainly.** Ids in 51–65 where *both* candidates are registered
variable-length and neither has a layout remain undecidable from the body alone, and the modern
table wins by default. None occur in either corpus. Deciding those needs the demo *dated* — the
string-table cosmetic fingerprint that placed `z1800.dem` — which is a larger piece of work and
now the only thing between this and a complete answer.

## B30 — `svc_EntityMessage` is the last opaque payload, and naming it needs the class — RESOLVED

Measured 2026-08-11 with `CorpusCodecCoverageTests` over **40 demos**. Thirty-nine report
**0.00%** opaque. One does not:

```
rgl-pug-2026-08-10-pov.dem: 11,520 of 3,857,112 payload bits opaque (0.30%)
```

A hundred times every other demo, and it is entirely `svc_EntityMessage` — **590 of them, all
identical: class 1, 8 bits, type 1**. Class 1 in that demo is `CBaseAnimating`.

**The meaning is settled and comes from published source.** `src/game/shared/baseentity_shared.h`
carries `#define BASEENTITY_MSG_REMOVE_DECALS 1`, and every `ReceiveMessage` in the SDK opens with
`int messageType = msg.ReadByte()`. So these are 590 instances of *remove all decals from this
model* — one type byte, no payload. Nothing is unread; the byte is already exposed as
`EntityMessage.MessageType`.

**What is missing is the name, and the obstacle is real rather than effort.** `1` is
`BASEENTITY_MSG_REMOVE_DECALS` for most classes and `PLAY_PLAYER_JINGLE` for `C_BasePlayer`. The
same byte means two things, so naming it is a claim about which handler applies, and that needs
the **class id resolved to a class name** — which the schema has and neither `EntityMessage` nor
the trace's message-formatting switch currently sees.

**The fix, scoped:** give the entity-message trace line the class name it already prints for
entities elsewhere (`CBaseAnimating(1)`), then map (class family, type byte) to a name over the
SDK's closed set of five handlers — `C_BaseEntity`, `C_BasePlayer`, `C_RopeKeyframe`, `C_Tesla`,
`C_EnvScreenEffect`. TF2's `game/client/tf/` overrides `ReceiveMessage` **not at all**, so the
inherited set is the whole set and this closes rather than opens.

**Do not "fix" this by reclassifying the bucket.** `CorpusCodecCoverageTests` counts an entity
message's whole body as opaque, and subtracting the eight bits it already interprets would take
the number to zero without anyone learning what the messages say. The instrument's own rule
applies: content interpreted, not merely consumed at the right length.

### B30 resolved, 2026-08-11 — the class resolves the byte

`EntityMessageNames.Lookup(className, messageType)` names the type byte, and the trace now prints
the class by name rather than by number:

```
svc_entitymessage entity 309 class CBaseAnimating(1) bits 8 type 1 BASEENTITY_MSG_REMOVE_DECALS;
```

**The class is a required argument, not a convenience.** Value 1 is
`BASEENTITY_MSG_REMOVE_DECALS` to most handlers and `PLAY_PLAYER_JINGLE` to `C_BasePlayer` — same
byte, same position, nothing in the body to separate them. Naming it without the class would be a
claim about which handler applies, which is the failure the user message era gate exists to
prevent. Where no schema is in hand the number still prints bare and claims nothing.

**The player test is a suffix match, and that has its own control.** TF2 ships
`CTFPlayerResource` and `CTFPlayerDestructionLogic`, neither of which is a `C_BasePlayer`, and a
substring test would misname both. Verified by sabotage: switching `EndsWith` to `Contains` fails
`AClassMerelyContainingPlayerIsNotAPlayer` and nothing else.

**The set is closed rather than sampled.** The SDK has eighteen `ReceiveMessage` overrides, and
`game/client/tf/` overrides it **not at all**, so TF2's set is the inherited one — `C_BaseEntity`,
`C_BasePlayer`, `C_RopeKeyframe`, `C_Tesla`, `C_EnvScreenEffect`. Only the two constants above are
reachable by anything in the corpus.

**What this does not do, deliberately.** `CorpusCodecCoverageTests` still counts an entity
message's whole body as opaque. The bucket was left alone on purpose — moving it would zero the
number by bookkeeping. The bits are now *interpreted* rather than merely consumed, and the
instrument should be changed only with that argument made explicitly, not as a side effect.

## B31 — 63 of 1397 Opus chunk blocks do not consume exactly — RESOLVED

Found 2026-08-11 while resolving the `svc_VoiceData` framing ([findings/02](findings/02-net-messages.md)).

The outer Steam framing is settled — 1452 of 1452 payloads consume exactly. The layer inside a type
`0x06` sub-packet is modelled as repeated `u16 chunk length, u16 sequence, <chunk length> bytes`,
and that consumes exactly for **1334 of 1397** blocks.

**The 63 that do not are unexplained.** There is a distinct population of 1-byte chunks — 147 of
them against a normal size range of 78–86 — and the plausible story is that a 1-byte chunk is a
marker with a different shape, perhaps not followed by a sequence field at all. That is a
hypothesis. It has not been tested, and it is recorded here rather than in the findings for exactly
that reason.

**Severity is low but the failure mode is not benign.** Unlike an exact-consumption check on a
message that then refuses, a mis-framed chunk hands the Opus decoder bytes that are not a frame
boundary. Opus will usually reject those, but "usually" is not a guarantee, and a decoder that
accepts them produces audible noise rather than an error. So the fix must be to establish the real
shape, not to skip the blocks that fail.

**What would settle it**: dump the 63 failing blocks with their chunk sequences and look at what
precedes the miss, and cross-check against a demo with a single continuous speaker where the
sequence numbers should be contiguous. The 2018-era archive currently downloading is CELT rather
than Steam voice, so it does not help here; the modern demos.tf pulls do.

**Resolved 2026-08-11, and the recorded hypothesis was wrong.** This entry guessed that the
1-byte chunks were "a marker with a different shape, perhaps not followed by a sequence field at
all". They are not. Every one of the 147 one-byte chunks carries the payload `0x68` — a valid
Opus TOC byte, the same one that leads the 78-86 byte chunks — so they are ordinary minimal Opus
packets and nothing about them is special.

What the misses actually were, found by dumping the failing blocks rather than reasoning about
them: **all 63 ended with exactly 2 bytes remaining, and those two bytes were `FFFF` every
time.** A block may end with a `0xFFFF` sentinel read through the chunk-length field, occupying
the length field alone with no sequence number and no data behind it.

Both wrong readings fail badly and differently, which is why this had to be established rather
than assumed. Treating `0xFFFF` as a chunk length asks for 65535 bytes that are not there;
treating the block as malformed discards audio that is perfectly well formed.

With the sentinel handled, **1452 of 1452 payloads and all 3969 chunks consume exactly**, and
exactly 63 report the terminator — the same 63. Recorded in
[findings/02](findings/02-net-messages.md).

## B33 — CELT rejected most frames: the build was missing ENABLE_POSTFILTER — RESOLVED

Opened and closed 2026-08-11. All **1085 of 1085** CELT frames in the corpus now decode, zero
failures, zero silent. Full history in [findings/02](findings/02-net-messages.md).

**The cause was one compile-time flag, and it was mine, not the data's.** libcelt 0.11.3's decoder
contains:

```c
if (ec_dec_bit_logp(dec, 1))        /* frame uses the postfilter? */
{
#ifdef ENABLE_POSTFILTER
   ... read octave, pitch, gain, tapset ...
#else
   RESTORE_STACK;
   return CELT_CORRUPTED_DATA;      /* <-- bail, immediately */
#endif
}
```

The build deliberately left `ENABLE_POSTFILTER` off to match upstream's default, so **every frame
whose postfilter bit was set returned `CELT_CORRUPTED_DATA` before decoding anything.** The frames
were valid the whole time; the decoder could not parse that branch. Enabling the flag took the
success rate from 43.7 % to 100 %.

**Two hard-won intermediate findings were real and still stand**, and were necessary to get here:

- **The parameters.** `VoiceEncoder_Celt::Init` uses `quality` as a direct index into a
  `{ rate, frame size, compressed length }` table at RVA `0x2f00c`; entry 3 is
  `{ 22050, 512, 64 }`, matching the corpus frame width. 22050 Hz at 512 samples is not a static
  mode, so `CUSTOM_MODES` is also required — without it `celt_mode_create` refuses outright.
  Independently corroborated: a public CS:GO voice-extraction implementation uses exactly
  22050 / 512 / 64-byte headerless frames for the same codec.
- **The framing.** `VoiceCodec_Frame::Decompress` is a bare loop over fixed-width frames, no
  header, no transformation — matching what this project already implemented.

**The wrong conclusion is kept here deliberately, because how it failed is instructive.** The
previous version of this entry stated that ~56 % of frames "are not CELT frames" and cited an
exhaustive ~31,000-configuration brute force over rate, frame size, offset and length as proof.
Every one of those measurements was accurate. The conclusion was still wrong, because **the entire
search space was the data, and the defect was in the decoder** — no amount of reparameterising the
input can reveal a branch the binary refuses to execute.

The tell was there and was misread: byte[1]'s high bit predicted failure *perfectly*, 474/474
versus 0/611, with not one exception. A perfect, exceptionless split across a thousand samples is
not what noisy real-world data looks like — it is the signature of a **deterministic branch**. It
was recorded as "a flag distinguishing two payload types" when it was the postfilter bit's
position in the range-coded stream, and the deterministic thing branching on it was libcelt's own
`#else return CELT_CORRUPTED_DATA`.

**The lesson worth keeping:** when a measurement partitions data perfectly, suspect the
instrument. This project's own rule about verifying by manipulation applies to the *tool* as well
as the code under test — the decoder was treated as fixed ground truth for the whole
investigation, and it was the variable.
