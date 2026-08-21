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

**2026-08-16 — the enum said these ids were "unused", and that was wrong in the expensive
direction.** `NetMessageType`'s comment claimed ids 1, 9, 16, 20 and 22 are unused at this protocol
and that "a stream producing one is malformed". Two independent things contradict it: this entry,
which records id 1 occurring in a real demo, and `public/inetmsghandler.h`, which declares handlers
for `SendTable` and `CrosshairAngle` — two of those five gaps. They are **unimplemented**, not
unused.

The distinction is the whole point. "Unimplemented" makes a stop this project's defect and keeps the
investigation open; "malformed" makes it the file's fault and closes it. The decoder's own behaviour
was right the entire time — it stops and says "not decoded yet" — so only the comment was wrong,
which is exactly the kind of confidently-repeated conclusion `docs/findings/` exists to catch.

`NetMessageConformanceTests` now checks the gaps against the engine's handler list rather than
against a sentence. The numbering came from client binaries and the names come from published
source, so the two are independent and neither can check itself.


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
in the 2009 one), so static entity baselines are the next step — see `DECISIONS.md` D27 and the
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

## B34 — coverage capture is cancelled by a 180-second RPC timeout — CAUSE FOUND, FIX OPEN

**The cause, measured 2026-08-12.** Stryker's Microsoft.Testing.Platform runner talks to its test
server over JSON-RPC, and that call has a **hard three-minute timeout**. The instrumented corpus
suite takes about 6 m 18 s, so the call is always cancelled, the server is discarded as crashed,
one retry fails the same way, and the run reports zero coverage:

```
[12:01:10 DBG] MtpRunner-2: Coverage mode enabled
[12:01:10 DBG] MtpRunner-2: Test server started successfully
[12:04:10 DBG] MtpRunner-2: Test run failed on attempt 1/2; discarding crashed server
System.Threading.Tasks.TaskCanceledException: A task was canceled.
   at StreamJsonRpc.JsonRpc.InvokeCoreAsync(...)
   at Stryker.TestRunner.MicrosoftTestPlatform.TestingPlatformClient.RunTestsAsync(...)
```

12:01:10 to 12:04:10 is 180 seconds exactly. **That is the whole explanation for the split**: the
synthetic project's instrumented suite finishes inside three minutes and captures 2169 mutations;
the corpus project's does not and captures 0, every single time.

**Two hypotheses died on the way, and both looked reasonable.** `additional-timeout` was raised
from 30 s to 900 s and changed nothing — it is a different timeout, applied per mutant test run,
and no value of it can help. And capture was not "collecting nothing": it collects normally right
up until the RPC is cancelled, which is why the failure is total rather than partial.

**Why it costs 18 hours downstream.** Without coverage Stryker cannot tell which tests touch a
mutant, so it runs the whole suite for every one. A killed mutant short-circuits on its first
failing assertion, which is why 183 were killed quickly — but a mutant that would SURVIVE must
complete the entire suite, so it always exceeds the per-mutant timeout instead. That is exactly
why the run reported `Survived: 0` alongside `Timeout: 1142`, a combination that looks like mass
hanging and is not.

**Not hangs.** Verified separately: `BitReader` throws `EndOfStreamException` when exhausted, so
every loop that reads bits terminates at the buffer end. The mutant the report blamed most
directly — `i++` to `i--` in `ReadClassInfo` — does not even compile here, because SonarAnalyzer
S2251 rejects it.

### The fix, which is not yet chosen

The instrumented suite has to finish inside 180 s, or not go through that path at all:

1. **Split the corpus test project** so each project's capture fits. This matches what D25 already
   did once, and is the only option that needs no cooperation from Stryker.
2. **Mutate in slices** with `--mutate`, so fewer instrumented sites means a cheaper capture. Two
   attempts to scope a run produced the full mutant set both times, so the glob syntax needs
   establishing first — Stryker resolves those patterns against the *source* project, and a
   pattern that matches nothing reports a clean run rather than an error.
3. **Mutate Core against the synthetic project only**, and treat the corpus suite as integration
   coverage rather than a mutation harness. 78 integration tests over real demos are a poor
   mutation harness at any speed, and this is what the PokemonBattleJournal agent independently
   suggested.

`test-case-filter` was tried as a way to shrink the capture and is **not honoured** by the MTP
runner: the run still reported all 99 tests found.

## B34 (original entry) — mutation coverage capture records nothing for the corpus project

**Symptom.** Stryker's coverage capture reports `0 mutations covered, 0 static mutations` for
`Tf2DemoSalvage.Corpus.Tests`, followed by `It looks like the test coverage capture failed.
Disable coverage based optimisation.` The synthetic project does not do this.

**Deterministic, not flaky** — measured across every run either project has ever had on the box:

| Run | Covered |
|---|---|
| 3 × `tf2-corpus` | **0** each |
| 3 × `tf2-core` | 1121, 1121, 2169 |

**Why it matters, in hours.** With no coverage data Stryker cannot tell which tests touch a
mutant, so it runs the whole suite for every one. That is the entire cost of the 18-hour run:
the initial test run took ~25 s and `additional-timeout` was 30000, allowing ~55 s per mutant,
and 1142 timeouts × ~55 s is 17.4 h against a measured `Time Elapsed 18:07:00`. The timeout has
since been lowered to 10000, which shortens the symptom without addressing this.

**What the timeouts were NOT.** The first reading of that run blamed unbounded loops in the
parser. The report says otherwise: the timing-out mutants are overwhelmingly ones that cannot
loop at all — 268 statement removals, 232 equality flips, 114 string mutations, and decisively
`OrderByDescending()` → `OrderBy()` and `Take()` → `Skip()`. A sort order cannot hang.
`Survived: 0` beside `Timeout: 1142` is the signature of a threshold effect, not of runaway
code. (Roughly 30 genuine unbounded-loop mutants do exist, and `DecodeProgress` now handles
those; it is not what made the run 18 hours.)

**What is measured about the cause.** Capture is catastrophically slower on this project.
Reproduced locally 2026-08-12: capture began at 23:38:03 and had not finished 27 minutes later,
with a single test-server process holding **3663 CPU-seconds** — against a suite that completes
normally in about 3 minutes. On the box, capture "completed" in 6 m 18 s and reported zero, which
is consistent with being abandoned rather than succeeding.

The plausible mechanism, **not yet confirmed**: capture instruments all 5516 mutants and records
every hit, and these tests drive the decoder across real demos, so the number of instrumented
hits per test is orders of magnitude above anything the synthetic project produces. That is the
one structural difference that tracks the split — the two test projects are otherwise near
identical (same SDK, same `xunit.v3` 3.2.2, both `Exe`, same runner, same config shape).

**Ruled out.** The `Tf2DemoSalvage.Audio` project reference — the failing runs predate it.
Missing LFS demos — the runner asserts the corpus is present before starting. Mutants not
reaching the tests — 183 were killed, so the mutated assembly was loaded and effective.

**Not yet established.** Whether capture terminates at all on this project given unlimited time,
and whether scoping the mutant set fixes it. Two attempts to narrow the run with `--mutate` both
produced the full 5516 mutants, so the scoping itself is unverified — Stryker's `mutate` globs
resolve against the *source* project rather than the test project, and a non-matching glob
reports a clean run rather than an error.

**Options, in the order worth trying.** Scope corpus mutation to a handful of files per run so
both the capture cost and the mutant count fall; or stop mutating against the corpus tests
entirely and rely on the synthetic project, which captures coverage correctly — the D25 split
already treats corpus as the slow cadence, and this would make that split absolute.

## B35 — 242 surviving mutants, a third of them in one file — OPEN

The first full `core` mutation run since the decoder grew: **54.26 %**, Killed 1631, Survived
242, Timeout 4, 1877 of 1879 mutants accounted for (the gap of 2 is RuntimeError mutants, which
the cleartext reporter names nowhere else). Coverage capture worked here — 2169 mutations
covered — which is why there are 4 timeouts rather than 1142; contrast B34.

**The score is the wrong number to react to.** The survivors are concentrated, not spread:

| Survivors | File |
|---|---|
| 86 | `Net/UserMessageBody.cs` |
| 32 | `Text/MessageAssembly.cs` |
| 31 | `Text/DemoAssembly.cs` |
| 15 | `Schema/SendPropDecoder.cs` |
| 11 | `Text/DemoTraceWriter.cs` |

Three files hold 149 of 242. Everything else in the project is in single digits. A 54 % score
reads like a broad quality problem and is not one.

By mutator: 57 string, 40 equality, 37 statement, 35 boolean. That is the signature of code
whose **outputs were never asserted precisely** — tests that prove the path ran without pinning
what it produced. A string mutant surviving means some message this parser renders could render
differently and no test would notice.

This is the third instance of the same lapse, after `GameEventCodec` (5 survivors) and
`StringTableCodec` (53) on 2026-08-07 — see `docs/memory/tests-before-codecs.md`. Each time the
code passed its corpus tests and looked finished, because a real demo exercises only the paths
it happens to use.

**Where to start:** `UserMessageBody.cs` alone is 36 % of the survivors and the best return.

## B36 — the overhead camera frames brushwork, not the play area — OPEN

**Found 2026-08-12**, while fixing the map not filling the viewport.

The camera fits the largest connected cluster of map geometry, which was measured at 91.1 % to
99.7 % of all points across nine shipped maps and reliably excludes the detached 3D skybox room.
See `docs/findings/10-maps.md` for the two rules rejected before it — a vertex percentile, which
cut real maps to about half their size, and the `sky_camera` entity, which is an exact marker for
the wrong thing.

**What is still wrong:** connectivity finds the map's body, not its interior. Geometry a player
can never see or reach is attached to the map and therefore inside the main cluster — the padding
behind the last-point spawn on `cp_process_final` is visible in the overview and invisible in the
game. A Source map has to be sealed against the void for `vvis` to compute visibility at all, so every
map has some of this.

Not all of it is unwanted: the boundary cliff at the back of second **is** seen, from the air, by a
soldier or demo mid-jump. The criterion is "what a player can see from anywhere they can reach",
and the jumping classes set that horizon well above the floor.

**The fix depends on work not done yet.** The demo states where players actually went, so once
tick-accurate playback lands the play area should come from the recording and the geometry cluster
becomes the fallback for the first frame, before any position is known.

**Not blocking.** The current framing is correct enough to read the map by, and the failure mode is
cosmetic — a margin of unreachable geometry around the edge, not a wrong picture.

## B37 — displacement terrain draws as its flat base quad — RESOLVED, BspTerrain reads DISP_VERTS

**Seen 2026-08-12**, on the first render of a downloaded `pl_vigil_rc9`: large flat slabs cover the
west and south of the map where the terrain should be.

A displacement in Source is a quad from the FACES lump subdivided into a heightfield, and the real
geometry lives in two other lumps — `DISPINFO` (26) and `DISP_VERTS` (33). `dface_t` carries a
`dispinfo` index; when it is not -1, the polygon in FACES is only the **base** the terrain is built
on.

So this reader draws the base. On an indoor map like `cp_process_final` that is invisible — there
are no displacements in the play area — and on an outdoor map it is a plain covering the detail
beneath it.

**Why it is not urgent:** for an overhead view a displacement's base quad is roughly where the
terrain is, so positions read correctly; it is the shading and the outline that are wrong. It also
makes those areas look *flatter* than they are rather than inventing geometry that is not there.

**The fix:** read `dispinfo` per face, and for a displacement expand the base quad through
`DISP_VERTS` instead of emitting it. The vertex count is `(2^power + 1)^2`, so the lump is
self-describing once `power` is read. Both lumps are LZMA compressed like every other.

Depends on nothing else; it is bounded work in `BspGeometry`.

## B38 — a downloaded map may not be the version the demo was recorded on — CLOSED, the premise was wrong

**Raised 2026-08-12** while wiring up map downloading, and **narrowed the same day** by the owner:
"the map name does contain versions a lot of the time, unless it is an official map, and officials
never break."

The original worry was that a mirror serves whatever version it currently carries, so a 2014 demo
would be drawn over a 2026 map — players walking through walls that did not exist yet. That failure
is real in principle and mostly cannot happen in practice, for two separate reasons.

**Community maps put the version in the file name.** Measured across the local corpus, **15 of 18
distinct map names carry an explicit version suffix**: `cp_process_f12`, `cp_gullywash_f9`,
`cp_metalworks_f7`, `pl_upward_f12`, `koth_cascade_rc1a`, `pass_sanctum_a2a`,
`pl_badwater_pro_v12`, `koth_ashville_final2`, `cp_snakewater_final1`. A revision is a new name, so
the name a demo records IS the version key, and fetching by it fetches the right file. The three
without a suffix are stock or near-stock.

**Official maps do not break demos, and the reason is worth stating exactly**, because it is what
closes this entry rather than merely shrinking it. Valve drops versioning from a map's final name
and then changes only bug fixes, which do not move geometry. But the deeper point is that **a demo
is the authority on where the player was.** It records positions; it does not re-simulate collision
against the map. So a player who reached somewhere they should not have — the skybox glitch on a
payload map being the standing example — is still shown up there when the map is later patched,
because the recording says that is where they were. The patch removes the way in, not the record of
having been in.

That inverts the original fear. The worry was that a revised map would make playback wrong; in fact
playback cannot desync from a map the way it can from a schema, because the map is scenery and the
positions are data. A revised map can only ever look slightly different around a stable geometry,
and official geometry is stable.

**What is left, and it is small:** a community mapper who revises without renaming. That is against
the convention every name above follows, and this project has no specimen of it.

So the fix is not version negotiation. It is to fetch by the exact name the demo gives, never
substitute a near-match, and say which file is being shown. A silently substituted map is the
failure that looks correct.

**The download API is not a risk here at all.** fastdl is a path GET against a plain directory tree,
with no versioning and no contract to drift against — mirrors have served servers this way since the
engine shipped, and old files stay reachable as long as the operator leaves them. Its tests are
mocked and deliberately do not hit a mirror: there is nothing to detect drift in, and hammering
someone's mirror to prove that a GET is still a GET is a cost with no measurement behind it.

**It is detectable, and that is the part worth doing first.** Two independent version markers exist:

- A BSP header carries `MapRevision`, which `BspHeader` already reads.
- Source's `ServerInfo` message carries the map's CRC, which the demo's signon data contains.

So the viewer can compare what the demo says the map was against what it just loaded, and *say* when
they disagree — which is far better than silently drawing the wrong world, and is worth having even
before any older version can be fetched.

**Fetching the right version is the harder half**, and belongs with the GCF/old-content branch.
Fast-download mirrors sometimes keep older revisions, and archives of competitive map versions
exist, but a mirror keyed only by map name cannot answer "the one with this CRC" without an index
that maps CRCs to files.

**Not blocking**, and the order is clear: detect and report the mismatch first, fetch the right
version second.

## B39 — blend materials draw only their first layer, so grass is missing — RESOLVED, $basetexture2 mixed by vertex alpha

**Found 2026-08-12**, by looking at the rendered map and asking where the grass went.

A displacement painted with a `WorldVertexTransition` material carries two textures and mixes them
per vertex. On `cp_process_final`:

| material | `$basetexture` | `$basetexture2` |
|---|---|---|
| `nature/blendgroundtograss007` | `dirtground009` | `grass_07` |
| `nature/blendrockgroundwallforest` | `rockwall001forest` | `grass_07` |

This project samples `$basetexture` only, so every blended surface draws as bare dirt or rock and
the grass never appears. It is not a large number of faces — 60 on process — but a displacement
covers a lot of ground, so it is most of the map's outdoor surface.

**The mix comes from the displacement's own vertex alpha**, in `DISP_VERTS`, which is the same lump
B37 needs for the real terrain shape. So the two are one piece of work: read `DISPINFO` and
`DISP_VERTS`, build the subdivided surface, and carry each vertex's alpha through to a shader that
lerps between the two textures.

**Not blocking**, and the failure is honest — a dirt-coloured field is visibly wrong rather than
subtly wrong, which is the right kind of missing feature to leave in place.

## B40 — tool materials are identified by path, not by a flag — OPEN (accepted)

**Found 2026-08-12.** 518 of `cp_process_final`'s 578 displacement faces are painted with
`tools/toolsinvisibledisplacement`: collision-only terrain the engine never draws. Drawn here it
covered the map's outdoor areas in black, because its texture is black.

Nothing in the data marks it. Its VMT declares `LightmappedGeneric` like any wall, and its texinfo
carries no `NoDraw` flag — the surface-flag filter that catches `toolsnodraw` and `toolstrigger`
passes it straight through.

So it is matched on the material path beginning `tools/`, which is the convention the engine, Hammer
and every map compiler share. **This is accepted rather than open work**, and recorded because a
path match looks like a hack until you know the flag route was tried and does not exist. If a
counter-example turns up — a real surface under `materials/tools`, or a tool material outside it —
this is where to start.

## B41 — large diffuse black areas over the map — RESOLVED, backface culling

**Reported 2026-08-12** from a screenshot with the affected regions highlighted: irregular, soft-edged
black patches spread across `cp_process_final`, in roughly the places the map has terrain.

**What has been ruled out by measurement, not by argument:**

- **Not unlit faces.** A face with no lightmap sampled the atlas at (0,0), which is padding and
  therefore black. Fixed by reserving a white texel — and the patches did not change.
- **Not holes from skipped tool displacements.** 518 of the map's 578 displacement faces use
  `tools/toolsinvisibledisplacement` and are correctly not drawn, but a coverage grid puts the area
  covered *only* by those at **5.1%** of the map. The patches are far larger.
- **Not dark lighting.** The lightmaps on displacement materials average 103 to 240 out of 255.

**The next measurement, which separates the two remaining candidates in one run:** disable the
lightmap multiply in the world shader so it returns albedo alone.

- Patches **gone** → the fault is in lightmap sampling: the atlas rectangle, or the coordinates,
  most likely for displacements whose base-quad coordinates run far outside 0..1 (values to 25.9
  were measured) and are clamped.
- Patches **remain** → the fault is texture resolution for those specific materials, and the next
  step is to report which material each black face uses.

### Resolved 2026-08-12: the terrain was being culled

Neither candidate. The black was the **absence of geometry**: D3D culls back faces by default, and
the grid this project builds when it subdivides a displacement winds the opposite way to the quads
the BSP supplies. Every terrain triangle was discarded and the background showed through — which is
pixel-for-pixel identical to a black texture, and is why three texture-and-lighting hypotheses all
failed to explain it.

**The user's observation is what identified it**, and it was one sentence: the black covered *the
whole ground area of mid and second*. Not patches correlated with a material or a lightmap — the
ground, exactly and only where displacements are, starting when displacements began to be
subdivided. A whole-region failure means geometry, not shading.

Culling is now off for the world rather than the winding being corrected, because winding is not
what this renderer relies on: which faces to draw is decided by their NORMAL, in `BspGeometry` and
`MapWorldBuilder`, where a downward-facing surface is dropped. Having the rasteriser make the same
decision from vertex order was a second source of truth that could disagree with the first, and did.

**The lesson worth keeping**: "it looks black" has two entirely different causes — a surface drawn
dark, and no surface at all. Every hypothesis tried here assumed the first. The question that
separates them is whether the affected area follows a *material* or follows a *region*.

## B43a — the content search path is hardcoded rather than read from gameinfo.txt — RESOLVED, GameSearchPath reads it

**Raised 2026-08-12**, immediately after the hl2 mount landed and by the owner's objection to how
it was found.

`GameArchives.Open` searches `tf/custom/*`, `tf`, `tf/tf2_textures_dir.vpk`, `tf/tf2_misc_dir.vpk`,
then `hl2` and its two archives. That list is **inferred**, and it is right for a stock TF2 install
only by coincidence.

**The engine reads it from `tf/gameinfo.txt`**, whose `SearchPaths` block declares exactly which
folders and archives are mounted and in what order. That file ships beside the VPKs this project
already opens. A mod, a different Source game, or an install with extra mounts would have a search
path this code cannot know, and the failure is the quiet kind: a material resolves to nothing and
the surface draws white or is skipped.

**How it was found is the point.** Three materials on `cp_process_final` resolved to nothing —
`GLASS/GLASSWINDOW008D`, `DEV/REFLECTIVITY_10B`, `PROPS/HAZARDSTRIP001A`. The first reading was that
material resolution had a bug, because those assets "did not belong" on a 2013 industrial map. They
do: mappers reuse content from anywhere in the install, and the job is to find it, not to treat it
as suspect. Adding `hl2` took the map from three unresolved to zero.

But the objection that followed is the durable one: **other parsers had solved this already, and
Valve declares the answer in a file.** Reading `gameinfo.txt` replaces a guess that happens to work
with the engine's own statement of intent. The project already has a KeyValues reader for VMTs.

**Not blocking.** A stock install resolves 100% of `cp_process_final`'s materials today.

## B42 — dark blobs over the map — RESOLVED, alpha-tested foliage drawn opaque

**The longest-running defect in this project, and it survived five wrong causes.** Kept in full,
because every one of them produced a plausible picture and the sequence is the lesson.

1. **Missing surface.** A coverage grid found 153 cells, 5.1% of `cp_process_final`, covered only by
   `tools/toolsinvisibledisplacement`. Read as holes.
2. **Static props not drawn.** The owner named the rock at mid. Props were read, placed and drawn —
   and the patches stayed.
3. **Props unlit.** They were, and lighting them changed nothing about the blobs.
4. **Wrong colour space.** A measurement said props averaged 0.2309 against the world's 0.4704 and
   that taking props through a gamma curve gave 0.4950 — five percent agreement, and wrong.
   `vrad` already applies that curve before writing.
5. **Overbright.** Genuinely a defect: the same 2.04 ratio was the engine's `cOverbright 2.0f`,
   which halves stored light and multiplies it back at draw. Fixed, and the blobs remained.

**The actual cause: foliage is alpha-tested.** Source draws leaves, grates and chain-link as flat
cards whose texture alpha cuts out the shape, enabling fixed-function alpha test and comparing
GEQUAL against `$alphatestreference`. Drawn opaque, a bush is a solid card the size of its quad,
filled with whatever RGB sits under the transparent region — which is black. Every "blob" was the
bounding quad of a tree or bush.

**Why it took five attempts:** every earlier cause was about how something was SHADED, and this one
was about a fragment that should not have been drawn at all. A shading explanation always fits a
dark region, so each hypothesis was confirmable and none was falsifiable by looking. What finally
separated them was that fixes 3, 4 and 5 all landed and the picture did not change — a shading
cause would have moved it.

**What would have found it sooner:** the material's own flag. `VmtMaterial.IsTransparent` existed
and was already carried as far as `MapTexture`, and the shader simply never looked at alpha. A
count of "how many drawn materials asked for alpha test, and does the renderer honour it" is a
question about the CODE rather than about the picture, and it was answerable at any point.

Confirmed by the owner: the trees are green.

## B41 — large diffuse black areas over the map — RESOLVED, backface culling

**Reported 2026-08-12** from a screenshot with the affected regions highlighted: irregular, soft-edged
black patches spread across `cp_process_final`, in roughly the places the map has terrain.

**What has been ruled out by measurement, not by argument:**

- **Not unlit faces.** A face with no lightmap sampled the atlas at (0,0), which is padding and
  therefore black. Fixed by reserving a white texel — and the patches did not change.
- **Not holes from skipped tool displacements.** 518 of the map's 578 displacement faces use
  `tools/toolsinvisibledisplacement` and are correctly not drawn, but a coverage grid puts the area
  covered *only* by those at **5.1%** of the map. The patches are far larger.
- **Not dark lighting.** The lightmaps on displacement materials average 103 to 240 out of 255.

**The next measurement, which separates the two remaining candidates in one run:** disable the
lightmap multiply in the world shader so it returns albedo alone.

- Patches **gone** → the fault is in lightmap sampling: the atlas rectangle, or the coordinates,
  most likely for displacements whose base-quad coordinates run far outside 0..1 (values to 25.9
  were measured) and are clamped.
- Patches **remain** → the fault is texture resolution for those specific materials, and the next
  step is to report which material each black face uses.

### Resolved 2026-08-12: the terrain was being culled

Neither candidate. The black was the **absence of geometry**: D3D culls back faces by default, and
the grid this project builds when it subdivides a displacement winds the opposite way to the quads
the BSP supplies. Every terrain triangle was discarded and the background showed through — which is
pixel-for-pixel identical to a black texture, and is why three texture-and-lighting hypotheses all
failed to explain it.

**The user's observation is what identified it**, and it was one sentence: the black covered *the
whole ground area of mid and second*. Not patches correlated with a material or a lightmap — the
ground, exactly and only where displacements are, starting when displacements began to be
subdivided. A whole-region failure means geometry, not shading.

Culling is now off for the world rather than the winding being corrected, because winding is not
what this renderer relies on: which faces to draw is decided by their NORMAL, in `BspGeometry` and
`MapWorldBuilder`, where a downward-facing surface is dropped. Having the rasteriser make the same
decision from vertex order was a second source of truth that could disagree with the first, and did.

**The lesson worth keeping**: "it looks black" has two entirely different causes — a surface drawn
dark, and no surface at all. Every hypothesis tried here assumed the first. The question that
separates them is whether the affected area follows a *material* or follows a *region*.

## B43b — duplicate of B43a, filed on a parallel branch — RESOLVED

**Raised 2026-08-12**, immediately after the hl2 mount landed and by the owner's objection to how
it was found.

`GameArchives.Open` searches `tf/custom/*`, `tf`, `tf/tf2_textures_dir.vpk`, `tf/tf2_misc_dir.vpk`,
then `hl2` and its two archives. That list is **inferred**, and it is right for a stock TF2 install
only by coincidence.

**The engine reads it from `tf/gameinfo.txt`**, whose `SearchPaths` block declares exactly which
folders and archives are mounted and in what order. That file ships beside the VPKs this project
already opens. A mod, a different Source game, or an install with extra mounts would have a search
path this code cannot know, and the failure is the quiet kind: a material resolves to nothing and
the surface draws white or is skipped.

**How it was found is the point.** Three materials on `cp_process_final` resolved to nothing —
`GLASS/GLASSWINDOW008D`, `DEV/REFLECTIVITY_10B`, `PROPS/HAZARDSTRIP001A`. The first reading was that
material resolution had a bug, because those assets "did not belong" on a 2013 industrial map. They
do: mappers reuse content from anywhere in the install, and the job is to find it, not to treat it
as suspect. Adding `hl2` took the map from three unresolved to zero.

But the objection that followed is the durable one: **other parsers had solved this already, and
Valve declares the answer in a file.** Reading `gameinfo.txt` replaces a guess that happens to work
with the engine's own statement of intent. The project already has a KeyValues reader for VMTs.

**Not blocking.** A stock install resolves 100% of `cp_process_final`'s materials today.

## B42b — props draw near-black; the "blobs" are lit wrongly, not missing — SUPERSEDED, see the note at the end

**Cause found, then found again.** This entry has been wrong twice and the history is kept because
each wrong answer looked exactly like the right one.

1. **Originally**: fuzzy black patches over 153 grid cells, 5.1% of `cp_process_final`, covered only
   by `tools/toolsinvisibledisplacement`. Read as missing surface.
2. **Then**: the missing surface turned out to be `prop_static` — the rock at mid being the case
   that named it. Props were read, placed, and drawn.
3. **Now**: the props draw, they have unmistakable rock and foliage silhouettes in a screenshot, and
   they are still nearly black. So the patches were never a hole in the last sense either.

**The current hypothesis, written before testing it: the vertex colours are in the wrong colour
space.** `BspLightmaps` takes a lightmap sample through its exponent and a gamma curve into display
space before the shader sees it. The `.vhv` bytes are passed straight through as `value / 255`. If
those bytes are linear, they need the same curve, and skipping it crushes everything toward black
while leaving the shading faintly present — which is what the screenshot shows.

**What supports it:** the props look uniformly dark whether they stand in open lit ground or in
shade. Lighting that were simply absent would leave them at texture brightness, which for the grey
rock textures on that map is much lighter than what is drawn. Lighting applied in the wrong space is
the shape that produces "dark everywhere, but not flat".

**What would settle it, and neither needs a screenshot:**

- Compare a placement's mean vertex colour against the lightmap luminance of the ground it stands
  on. Correct lighting agrees roughly; a missing gamma step is a consistent ratio.
- Apply the same curve `BspLightmaps` uses and measure the mean brightness of prop materials before
  and after. The dark-materials diagnostic already reports exactly that number.

**A second, unrelated observation from the same screenshot:** a dropship model sits well outside the
map at the top left. The skybox filter tests a placement's origin in X and Y only, so a prop parked
ABOVE the play area — where a skybox dropship would be — passes straight through. One line, once
confirmed.

**Not blocking, and worth not losing.** The patches are the oldest open thread in this project and
have survived four explanations; the pattern each time was reasoning from a static picture rather
than measuring. The two measurements above are the way out of that.

## B41 — large diffuse black areas over the map — RESOLVED, backface culling

**Reported 2026-08-12** from a screenshot with the affected regions highlighted: irregular, soft-edged
black patches spread across `cp_process_final`, in roughly the places the map has terrain.

**What has been ruled out by measurement, not by argument:**

- **Not unlit faces.** A face with no lightmap sampled the atlas at (0,0), which is padding and
  therefore black. Fixed by reserving a white texel — and the patches did not change.
- **Not holes from skipped tool displacements.** 518 of the map's 578 displacement faces use
  `tools/toolsinvisibledisplacement` and are correctly not drawn, but a coverage grid puts the area
  covered *only* by those at **5.1%** of the map. The patches are far larger.
- **Not dark lighting.** The lightmaps on displacement materials average 103 to 240 out of 255.

**The next measurement, which separates the two remaining candidates in one run:** disable the
lightmap multiply in the world shader so it returns albedo alone.

- Patches **gone** → the fault is in lightmap sampling: the atlas rectangle, or the coordinates,
  most likely for displacements whose base-quad coordinates run far outside 0..1 (values to 25.9
  were measured) and are clamped.
- Patches **remain** → the fault is texture resolution for those specific materials, and the next
  step is to report which material each black face uses.

### Resolved 2026-08-12: the terrain was being culled

Neither candidate. The black was the **absence of geometry**: D3D culls back faces by default, and
the grid this project builds when it subdivides a displacement winds the opposite way to the quads
the BSP supplies. Every terrain triangle was discarded and the background showed through — which is
pixel-for-pixel identical to a black texture, and is why three texture-and-lighting hypotheses all
failed to explain it.

**The user's observation is what identified it**, and it was one sentence: the black covered *the
whole ground area of mid and second*. Not patches correlated with a material or a lightmap — the
ground, exactly and only where displacements are, starting when displacements began to be
subdivided. A whole-region failure means geometry, not shading.

Culling is now off for the world rather than the winding being corrected, because winding is not
what this renderer relies on: which faces to draw is decided by their NORMAL, in `BspGeometry` and
`MapWorldBuilder`, where a downward-facing surface is dropped. Having the rasteriser make the same
decision from vertex order was a second source of truth that could disagree with the first, and did.

**The lesson worth keeping**: "it looks black" has two entirely different causes — a surface drawn
dark, and no surface at all. Every hypothesis tried here assumed the first. The question that
separates them is whether the affected area follows a *material* or follows a *region*.

## B43c — duplicate of B43a, filed on a parallel branch — RESOLVED

**Raised 2026-08-12**, immediately after the hl2 mount landed and by the owner's objection to how
it was found.

`GameArchives.Open` searches `tf/custom/*`, `tf`, `tf/tf2_textures_dir.vpk`, `tf/tf2_misc_dir.vpk`,
then `hl2` and its two archives. That list is **inferred**, and it is right for a stock TF2 install
only by coincidence.

**The engine reads it from `tf/gameinfo.txt`**, whose `SearchPaths` block declares exactly which
folders and archives are mounted and in what order. That file ships beside the VPKs this project
already opens. A mod, a different Source game, or an install with extra mounts would have a search
path this code cannot know, and the failure is the quiet kind: a material resolves to nothing and
the surface draws white or is skipped.

**How it was found is the point.** Three materials on `cp_process_final` resolved to nothing —
`GLASS/GLASSWINDOW008D`, `DEV/REFLECTIVITY_10B`, `PROPS/HAZARDSTRIP001A`. The first reading was that
material resolution had a bug, because those assets "did not belong" on a 2013 industrial map. They
do: mappers reuse content from anywhere in the install, and the job is to find it, not to treat it
as suspect. Adding `hl2` took the map from three unresolved to zero.

But the objection that followed is the durable one: **other parsers had solved this already, and
Valve declares the answer in a file.** Reading `gameinfo.txt` replaces a guess that happens to work
with the engine's own statement of intent. The project already has a KeyValues reader for VMTs.

**Not blocking.** A stock install resolves 100% of `cp_process_final`'s materials today.

## B42c — small fuzzy black patches where only tool displacements cover the map — RESOLVED by static props

**Left over from B41**, and measured before that one was solved: a coverage grid over
`cp_process_final` finds **153 cells — 5.1% of the map — covered only by
`tools/toolsinvisibledisplacement`**, with no other drawn surface beneath them.

Those faces are collision-only and the engine never draws them, so skipping them is right. What is
wrong is that nothing else fills the gap.

**The cause is static props, and the owner named it before the measurement did**: "there is a small
rock at mid scouts like me liked to sit on and play around". Invisible displacement is laid over
ground the mapper wants smooth to walk on, and what a player actually SEES standing there is a
`prop_static` — a rock, a crate, a fence — placed on top of it. So both earlier hypotheses were
wrong in the same way: the hole is not a surface drawn dark (B41's family) and not a surface
wrongly filtered (this entry's own first guess). It is a class of geometry this project did not
read at all.

Worth recording because the wrong guess was cheap to hold and expensive to test: the entry above
proposed enumerating faces per cell to decide between two filters, and neither filter was
implicated. **A coverage grid built only from faces cannot report the absence of something that is
not a face.** The instrument could not see the answer, which is why it kept pointing at the
candidates it could see.

**Half fixed.** `BspStaticProps` now reads the placements — model path, origin, angles, scale —
from the game lump. Drawing them needs the model chain (`.mdl` / `.vvd` / `.dx90.vtx`), which is
its own piece of work and is not done. Until it is, the patches remain.

## B44 — pre-2013 demos decode only one player entity — NOT A DEFECT, and the corpus said so

**Every demo in the corpus recorded before 2013 yields exactly one positioned player, including
SourceTV recordings that watched twelve.** Measured 2026-08-13 by `EraPlayerProbe`, accumulating
every packet in each file:

| demo | entities | `CTFPlayer` | positioned |
|---|---|---|---|
| tf2-2007-build3258-pov-cp_granary | 392 | 1 | 1 |
| tf2-2008-build3420-stv-cp_granary | 411 | 2 | 1 |
| tf2-2009-build3862-pov-cp_badlands | 336 | 1 | 1 |
| tf2-2011-build4604-stv-koth_viaduct | 214 | 2 | 1 |
| tf2-2013-build1729296-stv-cp_foundry | 863 | 2 | 1 |
| demostf-cp_gullywash_f9 (modern) | 601 | **10** | 9 |

**It is not an origin problem, which is what it looks like at first.** `Origin()` handles the
local/non-local table split and the modern XY-plus-Z shape, and on these files it succeeds on
every `CTFPlayer` it is given. The players are not losing their positions — they are not being
decoded as players at all. A POV demo finding one is what you would expect if only the recorder's
own entity existed; an STV demo finding two is not explicable that way.

**Hundreds of entities do decode**, so this is not a broken stream. Something about how entities of
that era are created, named or delta-decoded is dropping the rest.

Candidates, none tested:

- Class ids resolving differently for that era, so player entities are named as something else.
  Against this: `entities.All` shows no other player-shaped class, only `CTFPlayer` and
  `CTFPlayerResource`.
- Entity creation depending on baselines from `dem_stringtables` that are not being applied, so
  entities that never receive a full update are never created.
- A delta-decode path that silently stops after the first entity in a snapshot for older
  protocols.

### Resolved the same day, by reading the manifest

**There is no defect. Those demos contain one player.** Every era specimen was recorded by the
owner on a **listen server**, solo, to capture a protocol — `tools/corpus/manifest.json` says it
outright for each one: *"recorded by the owner 2026-08-10 on TF2 build 3258, listen server"*. The
SourceTV files show two because SourceTV is itself an entity.

The reasoning above was sound and its premise was never checked. "One player is not a plausible
number for a match" is true, and these are not matches. The instrument was right, the decoder was
right, and the corpus was documented — the assumption sat between them.

**One hypothesis was tested and killed before that**, which is the only part worth keeping as
method: instance baselines. An entity entering the potentially visible set is sent as a delta
against its class baseline, and `DemoTimeline` was not applying them where
`CorpusEntityDecodeTests` was. Wiring them in changed **no count on any file**, era or modern. They
are still applied now, because the format requires them and "it changed nothing measurable here"
is not evidence that it never will.

### What this does leave open

**The corpus has no multi-player demo before 2020.** Every era specimen is a solo listen-server
recording, so nothing in it can exercise playback with a full server across the era axis: crossing
players, entities entering and leaving the visible set, twenty-four origins at once. That is a gap
in the corpus rather than in the code, and it is the kind D5 already warns about — old specimens
are genuinely rare, and the ones that exist were made to date protocols rather than to watch a
match.

Until such a demo turns up, multi-player playback is verified on modern files only, and any claim
that a 2008 match plays back correctly is **interpolated**.

## B45 — team and class coverage varies from 0% to 100% — RESOLVED, every delta wiped the entity

**Where team and class actually live**, established 2026-08-13 and worth stating because the
obvious answer is wrong: they are not on the player entity. A positioned *modern* player carries
only `DT_BasePlayer.m_iHealth` of the three. Era demos do send `DT_BaseEntity.m_iTeamNum` on the
player, which is what made a player-entity reader look like it worked.

Both live on one `CTFPlayerResource` for the whole server, as arrays indexed by player slot.

### What the SDK says, and it rules out the easy explanations

`src/game/server/player_resource.cpp`:

```c
SendPropArray3( SENDINFO_ARRAY3(m_iTeam), SendPropInt( SENDINFO_ARRAY(m_iTeam), 4 ) ),
```

- **Always transmitted.** `UpdateTransmitState` returns `FL_EDICT_ALWAYS`.
- **Refreshed constantly.** `ResourceThink` runs every 0.1 s and `UpdateConnectedPlayer` sets
  `m_iTeam`, `m_iHealth`, `m_bAlive` and the rest for every connected player.
- **Four bits per element**, indexed 1..`MAX_PLAYERS`, which is the player slot and therefore the
  entity index for a player.

And `SendPropArray3` in `src/public/dt_send.cpp` shows an array is not a special wire form at all —
it is a DataTable with one independent `SendProp` per element, each named by
`DT_ArrayElementNameForIdx(i)`:

```c
for ( int i = 0; i < elements; i++ ) {
    pProps[i] = pArrayProp;
    pProps[i].SetOffset( i*sizeofVar );
    pProps[i].m_pVarName = DT_ArrayElementNameForIdx(i);
}
```

That matches the keys this project produces — `m_iTeam.003` — so the naming is right. It also means
each element deltas independently, so a snapshot carrying only some elements is correct and
expected; the accumulated table should still end up holding every element that ever changed.

### The measurement

Share of player sightings carrying a team, over the whole corpus:

| demo | team | class |
|---|---|---|
| tf2-2011-build4604-stv-koth_viaduct | **100%** | **100%** |
| tf2-2013-build1729296-pov-cp_badlands | 12% | 100% |
| tf2-2007-build3258-pov-cp_granary | 60% | 60% |
| demostf-cp_steel_f12 | 49% | 0% |
| demostf-cp_process_f12 | 0% | 20% |
| demostf-koth_ashville_final2 | 0% | 0% |

**One demo reaches 100% on both, so the arrays are found, named and read correctly.** The spread is
therefore not "the feature is unimplemented" — it is something about which elements survive
decoding, and it is not explained by the format, which transmits this data always and refreshes it
ten times a second.

### Three candidates eliminated, 2026-08-13

Checked against the SDK first and then against `demostf/parser`, which is the differential this
project has used before. **All three agree with what this decoder already does**, so none of these
is the cause:

1. **Array element naming.** `SendPropArray3` builds a plain `SendTable` whose children are normal
   props named `DT_ArrayElementNameForIdx(i)`. Those children are *not* inside-array props, so
   flattening them individually — producing `m_iTeam.003` — is correct. Only the other form,
   `SendPropArray` / `DPT_Array`, carries an element template.
2. **`SPROP_INSIDEARRAY` handling.** The flag means an element must be kept out of the flattened
   list. `SchemaFlattener` honours it (`InsideArrayFlag`, excluded alongside `IsExcluded`), and
   `demostf/parser` defines it identically as `InsideArray = 256` with the same comment.
3. **Array count width.** `demostf/parser` computes `log_base2(element_count) + 1`; this project
   computes `WireWidths.ClassId(n) = Log2Floor(n) + 1`. The same expression. This was the most
   promising of the three, because floor-plus-one and ceiling diverge at exact powers of two — but
   both implementations use floor-plus-one, so there is no divergence to find.

Remaining candidates, untested:

- `EntityStateTable` replacing rather than merging when the resource's serial number changes, so a
  recreated resource drops every element previously accumulated.
- Element updates landing on neighbouring indices, which would show as some indices always present
  and others never — the shape observed, and worth checking directly by dumping which indices a
  single demo ever sets.
- The resource being found by `OfClass(...).FirstOrDefault()` when more than one exists, so a stale
  or empty one is read. Not yet counted.

### The cause: a delta states no serial number, and the table read that as a new occupant

`EntityDecoder` reads a serial number only on an **Enter** update, because that is the only place
it appears on the wire — confirmed by `NUM_NETWORKED_EHANDLE_SERIAL_NUMBER_BITS` in
`src/public/const.h`, and by the structure of the update itself. For a Delta it passes zero.

`EntityStateTable.Apply` compared that zero against the stored serial, decided the slot had a new
occupant, and **threw away every property the entity had accumulated** — on every delta, for every
entity, for the whole demo.

Position survived because a delta usually resends an origin. Team did not, because it is sent once
and never again.

**Found by the owner scrubbing the viewer**, not by the suite: the demo showed team colours the
moment it opened and lost them the instant it was scrubbed. Opening reads the first frame, which is
still the Enter; scrubbing reads a later one, which is post-delta.

**The suite could not have found it, and the reason is a fixture.** There was already a test named
`ADeltaKeepsPropertiesEarlierSnapshotsSet`, asserting exactly the right property — and it passed,
before and after, because its helper gives the delta `SerialNumber: 1` to match the enter. The
decoder never produces that. Correct and broken predict the same observation for that input, which
is the "wrong condition" failure: the fix is a different input, not a stronger assertion.

### The measurement, after

| | before | after |
|---|---|---|
| era demos, all eight | 0–100%, mostly under 10% | **100%** |
| modern demos | 0–7% | **92%** |
| z1800 | 2% | 95% |

### The residual eight per cent was two separate things, and neither was a gap

**Backfill closed the first.** A player is sighted for a few frames before the resource first
mentions them. The whole demo is in hand, so the earliest stated team and class are carried
backwards to that player's first sighting — which a streaming parser cannot do, and which is why
taking team from `player_spawn` is a worse trade: a demo beginning mid-round has no spawn event to
read at all.

**The rest were spectators, and they were being drawn.** `TEAM_UNASSIGNED` is 0 and
`TEAM_SPECTATOR` is 1, against `TF_TEAM_RED = LAST_SHARED_TEAM + 1` making RED 2 and BLU 3. A
spectator and a SourceTV camera are `CTFPlayer` entities with real positions that follow the
action — so the viewer was drawing convincing dots where nobody stood, and the measurement was
counting them as missing.

Both corrected, the corpus reports **0% unknown on every file**: 100% playing on POV recordings,
92–95% playing with 5–10% watching on SourceTV ones, which is what a relay should look like.

Raised by the owner asking whether spectators could be told apart, after the same scrubbing session
that found the delta bug.

### Note on the workaround this nearly became

`demostf/parser` takes team from the `player_spawn` game event rather than from entity state. That
may well be a response to this same bug, and it carries a cost: a demo that begins mid-round has no
spawn event to read, so a player sits on a default team until the next one. Entity state has the
answer continuously — `player_resource.cpp` transmits with `FL_EDICT_ALWAYS` and refreshes every
0.1 seconds — so reading it is both cheaper and more correct once the accumulator is right.

The renderer reads these today and falls back to the player entity's own `m_iTeamNum`, so era demos
colour correctly and modern ones mostly do not. Until this is resolved, team colour on a modern
demo is unreliable and class is worse.

## B46 — ten static props draw unlit, and it is not a decode failure — OPEN, low value

Measured on cp_process_f12: **10 of 1,353 placed props have no baked vertex lighting**, and the
viewer draws those white.

**vrad never wrote one for them.** From `vradstaticprops.cpp`:

```c
// no need to write this file if we didn't compute the data
// props marked this way will not load the info anyway
if ( m_StaticProps[i].m_Flags & STATIC_PROP_NO_PER_VERTEX_LIGHTING )
    continue;
```

`STATIC_PROP_NO_PER_VERTEX_LIGHTING` is `0x40` in `gamebspfile.h`, and its comment says what
happens instead: *"in vrad, compute lighting at lighting origin, not for each vertex"*. So the prop
gets one colour sampled at `m_LightingOrigin` rather than a colour per vertex, and the absent
`.vhv` is correct rather than missing.

So this is not a reader defect. What is wrong is the fallback: white, where the right answer is a
single sampled colour.

**Why it is filed rather than fixed.** Two costs, against 0.7% of props:

1. **`m_Flags` moved between versions.** The modern `StaticPropLump_t` carries it as a late
   `unsigned int`; V4 had it as an `unsigned char` immediately after `m_Solid`. Reading it needs a
   per-version field map, which is exactly what `BspStaticProps` avoids by measuring its stride
   through division — a deliberate choice that has survived five protocol eras.
2. **The colour has to come from somewhere.** Sampling at the lighting origin means reading the
   leaf ambient lighting lump, which nothing in this project reads yet.

White is also a defensible fallback here: the props that carry this flag are typically small or
already bright, and the black blobs that prompted the original hunt turned out to be a test
artefact rather than these.

Worth doing when the leaf ambient lump is read for another reason. Not worth a version table on its
own.

## Register hygiene, 2026-08-13

**Numbers collided and states went stale**, both because entries were filed on parallel branches
that were merged without anyone reconciling them. B42 was used four times and B43 three, and
several entries stayed OPEN long after the work landed. Corrected in place rather than renumbered,
since the numbers are referenced from commit messages and findings: duplicates now carry a letter
suffix and say what they duplicate.

**The black-blob thread is the one worth reading end to end**, because it went through four
explanations and only the last was right:

1. **B41** — backface culling. Real, fixed.
2. **B42** — alpha-tested foliage drawn opaque, so every bush was a black card. Real, fixed.
3. **B42b** — props lit wrongly. Wrong: props were not yet drawn at all when this was filed, and
   drawing them did not remove the patches.
4. **B42c** — tool displacements covering ground with nothing behind them. Real, and fixed by
   reading static props.
5. **The remainder was never in the viewer.** The offscreen render target had no depth buffer, so
   draws landed in material-batch order and dark surfaces painted over foliage. Every picture it
   produced was read as evidence about the window for several sessions. The owner settled it by
   looking at the window, where the same map was fine. The target now has a depth buffer, and the
   authoritative picture is captured from the swap chain instead.

That last one is the most expensive mistake in this register: not a wrong hypothesis, but a
measuring instrument that manufactured the defect being measured.

## B47 — players are not interpolated, because their model is not networked — RESOLVED 2026-08-13

**Filed 2026-08-13.** Prop poses interpolate through `ScenePropTrack` — hermite, with Valve's time
renormalisation. Players do not, and the reason took a measurement to find.

`DemoTimeline.PlayersAt(double, …)` looks a player's entity up in the track table and falls back to
the stated frame position when there is none. On every demo in the corpus it always falls back:
**zero** player samples differ from a stated position.

The cause is in TF2's own client, not in this code. A player's model is never sent —
`tf_playerclass_shared.cpp:136`:

```cpp
const char *CTFPlayerClassShared::GetModelName( void ) const
{
	if ( m_iszCustomModel[0] ) return m_iszCustomModel;
	Q_strncpy( modelFilename, GetPlayerClassData( m_iClass )->GetModelName(), … );
	return modelFilename;
}
```

The client resolves it locally from `m_iClass` through the class data table; only `m_iszCustomModel`
travels on the wire. So a `CTFPlayer` carries no `m_nModelIndex`, `RecordProp` skips it, and no
track exists.

**This is not a reason for a second interpolator.** In the engine a player's position uses exactly
the same machinery as a rocket's: `AddVar(&m_vecOrigin, &m_iv_vecOrigin, LATCH_SIMULATION_VAR)` is
on `C_BaseEntity` (`c_baseentity.cpp:905`), and a player is a `C_BaseEntity`. TF2 adds one
interpolated variable of its own, `m_angEyeAngles` (`c_tf_player.cpp:3874`).

**The fix** is to build player tracks from the class table rather than from a model index, which
also supplies the player models the viewer will need anyway. `m_iszCustomModel` overrides it when
present.

**Resolved the same day.** A player is now recognised by class name and given a track with an
empty model path, kept in `PlayerTracks` rather than in `Props`. The poses are what the
interpolator needs; the model comes from the install through `PlayerClassModels`.

Keeping the two lists apart matters: a consumer walking `Props` to draw models would otherwise find
entries with no model and could only report them as missing assets, which is a false alarm about
the very thing that works.

The corpus test `PlayersAt_BetweenFrames_MovesThroughPositionsNoFrameContains` is no longer
`[Explicit]` and passes across the corpus. It went from measuring exactly zero to measuring the
fix, which is the shape a regression test should have.

## B48 — a hypothesis for TF2's end-of-demo freeze, from the interpolation code — UNTESTED

**Filed 2026-08-13, prompted by the owner's recollection** that playing a demo in TF2 ends with
things freezing and "kinda glitching out". Recorded as a hypothesis because it fits code that has
been read, and nothing more — no measurement against a running client has been made.

`GetInterpolationInfo` in `interpolatedvar.h` clamps the interpolation fraction:

```cpp
pInfo->frac = ( targettime - older_change_time ) / ( newer_change_time - older_change_time );
pInfo->frac = MIN( pInfo->frac, 2.0f );
```

A fraction above 1 is **extrapolation** — the client running past its newest sample by up to a full
interval, which during normal play covers a dropped packet.

At the end of a demo the stream stops but `curtime` does not. Every entity's newest sample stays
fixed while the target time advances, so the fraction climbs past 1, everything extrapolates on
past where it was last seen, and then clamps at 2.0 and stops dead. Sliding-then-freezing is what
that would look like.

**What would confirm or kill it:** watching a demo end in a client with `cl_interp` set very high
and very low. If the effect scales with the interpolation window, the mechanism is this one; if it
is identical either way, it is something else — most likely in engine demo shutdown, which is not
in the SDK.

**Why it is worth keeping even unresolved.** This project extrapolates nothing: `ScenePropTrack.At`
holds the last pose after the final keyframe rather than running past it. If this hypothesis is
right, that is a case where deliberately *not* copying the engine produces the better viewer, and
the reasoning behind that choice should be recoverable later.

Not to be confused with the trailing-block quirk in `IceCipher.DecryptAll`, which was found in the
same session and affects script files only — it has nothing to do with demo playback.

## B49 — black lids over rooms in the overhead view — OPEN, and it is roof removal, not lighting

**Filed 2026-08-13.** Solid black boxes sit over cp_process's last points and a few other rooms.
Three measurements, none of which needed a guess:

- **The category view (F9) says world brush.** Present, drawn, not missing, not displacement.
- **No surface in the map is unlit.** 12,230 visible surfaces, **zero** with an all-black
  lightmap. Lighting is not the cause.
- **The material is `tools/toolsblack`**, whose reflectivity vbsp itself records as
  `0.000 0.000 0.000` — 118 faces of it.

So the renderer is correct and the picture is wrong. Lighting multiplies the texture, and anything
times zero is zero: a perfectly lit black texture is black. Mappers cap rooms with `toolsblack` so
the skybox does not show through from inside; from below it reads as dark void above, and nobody is
ever above it to see that it is a slab.

**Do not fix this by skipping the material.** That was tried and reverted, and `MapWorld.cs`
carries the account: `toolsblack` is genuinely drawn behind windows, under grates and inside vents,
and removing it by name left 4.8 million square units showing the background through — read as dark
blobs, and survived four separate explanations about lighting before anyone checked.

**Orientation separates the two uses, and the numbers are clean:**

| Facing | Count | What it is |
|---|---|---|
| Up | 88 | Lids over rooms — only ever seen from above |
| Vertical | 30 | Window voids, grates, vents — must stay |
| Down | 0 | — |

An up-facing rule would therefore keep every case that broke the last attempt. But the right fix is
the roof-removal feature the viewer already half has (the depth cut), because the same problem
applies to any roof and not only to black ones: an overhead camera stands where no player does, and
what to hide is a property of the view rather than of the material.

**Parked deliberately** until model rendering lands, on the owner's call. The measurements above are
the expensive part and they are done.

## B50 — the alpha-test threshold is a guess, and `$alphatestreference` is ignored — OPEN, small

**Filed 2026-08-13**, while fixing the defect that hid every entity model.

The shader used to clip on alpha unconditionally, which was safe only because opaque map textures
have their alpha flattened to 255 on upload. Anything whose alpha is kept for another reason — a
self-illuminated material, or a model texture with an unused alpha channel of zeros — had every
pixel discarded. Entity models were drawn with correct geometry, correct transforms, correct
batches and a correct draw call, and were invisible.

**The gate is now verified against published source.** `BaseVSShader.cpp:925`:

```cpp
s_pShaderShadow->EnableAlphaTest( IS_FLAG_SET(MATERIAL_VAR_ALPHATEST) );

if( alphaTestReferenceVar != -1 && params[alphaTestReferenceVar]->GetFloatValue() > 0.0f )
{
    s_pShaderShadow->AlphaFunc( SHADER_ALPHAFUNC_GEQUAL, params[alphaTestReferenceVar]->GetFloatValue() );
}
```

Alpha testing happens **only** when the material sets the flag, which is what `$alphatest 1` does.
That much now matches.

**What does not match, and cannot be read from the SDK:**

- `$alphatestreference` defaults to `"0.0"` (`lightmappedgeneric_dx9.cpp:63`,
  `vertexlitgeneric_dx9.cpp:42`), and at zero Valve never calls `AlphaFunc` — so the threshold comes
  from the shader API's own default, which is in the closed implementation. Our hardcoded **0.5** is
  a convention, not a measured value.
- A material that *does* set `$alphatestreference` is cut at 0.5 by us regardless, so its foliage or
  grate will lose or keep the wrong pixels.

**The fix** is to read `$alphatestreference` in `VmtMaterial`, carry it in the material constants
beside the alpha-test flag, and use it when it is above zero — falling back to 0.5 only when the
material says nothing. Small, and it turns "matches the flag" into "matches the behaviour".

**Worth keeping for the shape of it.** This defect was invisible to every instrument: the counts, the
names, the matrices, the packed vertices and the batch ranges were all correct, and two probes
(twenty-times scale, depth disabled) both came back negative because the pixels were being discarded
after all of that. The thing that found it was reading the shader's own comment, which stated the
assumption it depended on.

## B51 — entity models draw unlit and blown out — OPEN, and it is the reason they look wrong

**Filed 2026-08-13.** Entity models render at the right places with the right materials, and look
like white blobs. Static props do not, and the difference is lighting.

A static prop carries baked per-vertex lighting from the map's `.vhv`, so its vertex colour darkens
it into the scene. An entity has none: this project gives it `1, 1, 1` and a lightmap coordinate of
`(0, 0)`, which lands on the atlas's reserved white texel. Full-brightness vertex colour times a
white lightmap washes the texture out — a medkit's teal case and red cross become a pale square.

**The occlusion story was wrong, and the mistake is worth recording.** The category view showed a
white square where each pickup stands, and white was read as map geometry covering it. The palette
has no white: `Terrain` is green, `Prop` orange, `Missing` magenta, `Brush` blue-grey, and the
diagnostic shader returns the vertex colour — so the white square *was* the model, drawn on top of
everything. The owner's flat statement that nothing covers those packs in game is what forced the
check, and reading `CategoryColour` took ten seconds against an hour of inference.

**What the engine does.** A dynamic model is lit from the light cache rather than from a lightmap:
`LightingState_t` in `istudiorender.h` carries `m_vecAmbientCube[6]` — "ambient, and lights that
aren't in locallight[]" — plus an array of local lights, sampled near the model's origin. That is
the same mechanism whether the model is a health pack or a player.

**Not a lightmap lookup.** A model does not have lightmap coordinates, which is precisely why the
engine has a separate path for it; adding one here would be inventing a mechanism Source does not
have.

Until it is implemented, every entity model in the viewer is overbright, and any judgement about a
model's texture or material is unreliable — three separate defects were attributed to materials
tonight before this was understood.

## B52 — buried geometry is drawn, because nothing culls by visibility — OPEN, structural

**Filed 2026-08-13** from an in-game comparison: the concrete around cp_process's mid point is
buried under the ground in TF2, and this viewer draws it as a slab over the surrounding surface.

**Nothing here culls by visibility.** The renderer keeps a face when its material resolves, its
surface flags are drawable, and its normal points upward; it has no notion of whether a player could
ever see it. The engine's answer is the BSP itself: `vvis` computes a potentially visible set per
leaf, and geometry sealed inside solid space is never submitted at all.

So a face buried under the ground is drawn, lands at nearly the same depth as the ground above it,
and which one survives is decided by material batching order. That is the same family as B49's black
lids — surfaces that exist in the file and are never seen in play — but the mechanism is different
and so is the fix.

**What this predicts, and is worth checking when it is fixed:** interior faces showing through
floors, surfaces inside sealed props, and the odd patch of terrain that flickers as the view moves.
Any of those seen now are probably this.

**The fix is the visibility lump.** A BSP carries `LUMP_VISIBILITY` alongside its nodes and leaves;
resolving each face's leaf and keeping only faces in leaves reachable from open space would remove
buried geometry without a heuristic. It is also the foundation a free camera wants, since a
first-person view needs the same question answered every frame.

**Not to be confused with B49.** There the surface is genuinely visible in play — a `toolsblack`
ceiling seen from below — and only an overhead camera has a problem with it. Here the surface is
never visible at all, and drawing it is wrong from every angle.

## B53 — models take ambient light only; direct lights are not applied — OPEN, named remainder

**Filed 2026-08-13**, immediately on fixing B51. Entity models are now lit from their leaf's ambient
cube and look plausible indoors, while an outdoor model stays noticeably dimmer than the same object
in TF2.

That is expected, and `istudiorender.h` says why in a comment on the field itself:

```cpp
Vector m_vecAmbientCube[6];   // ambient, and lights that aren't in locallight[]
```

**The cube is the ambient term only.** Direct lights — the sun above all — are carried separately in
`LightingState_t::locallight[]` as `LightDesc_t` entries, and applied on top. A health pack in
daylight gets most of its brightness from the sun, so a viewer with the cube alone renders it as
though it were in shade.

**What it would take.** `LUMP_WORLDLIGHTS` carries the map's lights, including the sun as a
directional `emit_skylight`. Applying the sun alone would close most of the visible gap, since it is
the one light that reaches most outdoor surfaces; point and spot lights matter far less at the scale
this viewer draws.

**Why it is filed rather than fixed now.** The ambient half is complete, tested and measured, and it
is the half that turns a white blob into a recognisable object. Adding direct light is a separate
piece of work with its own failure modes — shadowing above all, since an unshadowed sun lights the
inside of every building.

## B54 — colour maths happens in display space, not linear — OPEN, and it is the root of several

**Filed 2026-08-13**, after the owner observed that "we keep running into problems because we are
flattening stuff".

**What the engine does.** A lightmap sample is linear light. A texture is sRGB and is linearised on
sampling by an sRGB view. The shader multiplies them in linear space, applies the overbright factor
(Source's shaders multiply an LDR lightmap by two), and the hardware applies gamma once when writing
to an sRGB target.

**What this project does.** `BspLightmaps` applies the exponent *and* the gamma curve at decode, so
the lightmap arrives already in display space. Base textures are uploaded as plain `UNORM` and
sampled raw, so they are also display space. The shader multiplies two display-space values and
writes to a non-sRGB back buffer. Valve's doubling is then deliberately skipped, because on top of
gamma-corrected values it blows the map out to white.

Each of those compensates for the one before it. The result looks approximately right on a lit wall
and goes wrong wherever anything new is introduced:

- The ambient cube was written linear, per the format, and rendered nearly black against
  display-space lightmaps — fixed by gamma-correcting the cube, which is the *wrong* fix in a
  correct pipeline and the only possible one in this one.
- Alpha was flattened at upload to survive an unconditional clip, which then cost every decal its
  shape (fixed 2026-08-13).
- Brightness comparisons against the game are unreliable, so "too dark" and "too bright" cannot be
  used as evidence about anything else.

**What it would take**, in order, each step checkable against a capture:

1. Create the back buffer's render target view with an sRGB format, so the hardware applies gamma
   on write. The flip-model swap chain itself stays `UNORM`.
2. Create base-texture views with `_SRGB` formats, so sampling returns linear.
3. Stop applying the gamma curve in `BspLightmaps`; upload linear samples, which needs more range
   than eight bits — a half-float texture is the straightforward answer.
4. Restore Valve's overbright multiply in the shader.
5. Remove the gamma correction added to the ambient cube, which exists only to match step 3's
   current behaviour.

**Why it is worth doing rather than living with.** Every future lighting feature — direct lights
(B53), self-illumination, a first-person camera's exposure — has to be reconciled against whatever
space the pipeline is in. Doing it in the engine's space means Valve's own numbers can be used
directly, which is the whole reason this project reads the SDK.

## B55 — `$envmap` is not implemented, and 43 of 189 map materials ask for it — CLOSED 2026-08-19

**Closed.** Reflections are drawn. The census that named this — `$envmap` on 79 of
cp_process_final's 410 materials, its largest unimplemented parameter — now reports 43 unimplemented
with the largest at 66; `$envmap`, `$envmaptint` and `$basealphaenvmapmask` are gone from it, and
all ten of `EnvmapConformanceTests` activated.

**The expensive-looking half did not exist.** The obvious design is a nearest-by-position search at
load; vbsp did the assignment at compile time and this project only had to read it. Measured as two
independent recordings agreeing: 51 patched materials, all 51 naming one of the 43 placements in
`LUMP_CUBEMAPS`, and all 51 with the position in the material's name matching the position in its
`$envmap` value.

**Verified through the GPU, not only against map data.** A reflective material's pixel changes with
the surface normal — `(129, 115, 125)` facing up against `(69, 68, 69)` facing sideways — while a
matte material's is byte-identical both ways. The control is what makes the first row mean
something.

**Not verified: whether it looks right.** A pixel that changes with the normal says the cube is
sampled, not that the picture is correct.

Three things found on the way are recorded in `docs/findings/27-cubemap-placement.md`: a struct
three bytes larger than its declaration, a `$envmap` "half-float prerequisite" that was a fact about
the probe's own file preference, and — much the largest — that **every `Patch` material this project
had ever resolved was a no-op**, which is its own entry below.

The original text follows.

## B55 (original) — `$envmap` is not implemented, and 43 of 189 map materials ask for it

**Measured, not estimated.** On cp_process_f12, **42 of 189 materials declare `$envmap`** — 22% of
the map's surfaces, including every pane of glass, the polished floor tiles at both second points,
and the metalwork around them. The viewer implements none of it: a material's cubemap reflection
contributes nothing, so those surfaces render with their base texture and lightmap alone.

The owner identified this from the game's own behaviour before any of it was measured here:
control points are "very reflective and shiny", and running TF2 on DirectX 8.1 takes the shine off
control points and übercharges. That is exactly the shader-model fallback — dx8's `LightmappedGeneric`
drops the envmap pass — so the shine is envmap-sourced by construction, not inference.

**What this does NOT explain**, and the record is kept because the wrong conclusion was reached
twice on the way:

- The black disc at every control point is **not** this. It survived every check: no material
  failed to load (the log names only two absent tool materials and four vertex-lighting checksum
  mismatches), the category view draws that area as ordinary brush, `overlays/stain016` is the
  wrong size for it by three times, and widening the surface query to 160 units horizontally and
  1024 units down finds **no upward-facing world face at mid's centre at all**. Still open.
- A first pass reasoned "dx8.1 removes the shine and the map looks fine there, so the base texture
  is not black, so the envmap is not the cause". The owner corrected it: dx8.1 removing the *shine*
  is not a statement that the result looks correct. The inference was about a claim never made.

**Why it is worth doing properly rather than faking a specular term.** A cubemap in a Source map is
real baked data — `LUMP_CUBEMAPS` names the sample positions and the compiled `.vtf` faces are in
the map's own pakfile, which this project already reads for everything else. Approximating it with
a constant highlight would put a plausible shine in the wrong places, which is the failure mode this
project keeps finding: a result that looks like art direction rather than like a bug.

**A logging gap this exposed, and it is the more general finding.** Every material resolved, so the
log was silent while a control point drew as a black disc. The viewer logs what fails to *load* and
nothing about what a surface resolved *to* — which shader path it took, whether it declared an
effect that is unimplemented. A map is 189 materials; a one-line summary of the unimplemented
parameters they ask for would have named this in the first minute instead of after an hour of
probes. Same shape as `measure-the-output-not-the-capability`.

**Corrected 2026-08-13, and the correction is the point.** The 43 above was 42. The probe that
produced it counted materials whose VMT *text contains* `$envmap`, which is a substring — so
`$envmaptint` (18 materials) and `$envmapcontrast` (18) matched it too. It landed one away from the
right answer by luck, since most materials declaring the tints also declare the map itself.

The instrument that replaced it, `MaterialCensus`, counts declared parameter *names* and reports
every unimplemented one at load. Its first run named the gap this whole search missed:

```
48 unimplemented material parameters across 189 materials:
$vertexalpha x55, $vertexcolor x55, $envmap x42, $basealphaenvmapmask x24,
$basetexturetransform x19, $envmapcontrast x18, $envmaptint x18, $alpha x7,
$color x7, $nocull x5, $nodecal x5, $texcenter x5, $texoffset x5, $texrot x5,
$texscale x5, $texture2 x5, ... $AlphaTestReference x2 ...
```

**`$vertexcolor` and `$vertexalpha` are on 55 materials — more than `$envmap` — and are wholly
unimplemented.** Every overlay VMT read while chasing the black disc declared both, and neither
was noticed, because nothing was looking for parameters and nothing failed. That is a better
candidate for the disc than anything the probes proposed.

`$basetexturetransform` on 19 materials, plus `$texrot`/`$texscale`/`$texoffset`/`$texcenter` on 5,
is a second unimplemented family that rotates and scales a texture — worth holding against any
future report of a texture sitting the wrong way round, since one of those was already misdiagnosed
three times.

`$AlphaTestReference` on 2 materials is B50, now measured rather than assumed.

**`$color` and `$alpha` were on this list and are now implemented** (2026-08-18). They were the
easiest entries on it and were worth doing first for a reason worth recording: the renderer already
had a modulation constant, already uploaded it, and already multiplied by it — inside the
two-texture branch of a ternary, because that is where Valve's line
`baseColor * baseColor2 * g_DiffuseModulation` was found. The citation was correct and the
generalisation from it was not: every ordinary tinted surface had its colour decoded, uploaded, and
multiplied by nothing. `docs/findings/26-material-modulation.md`.

**The specification for `$envmap` now exists, written before any of it is built**
(`EnvmapConformanceTests`, 8 tests, 2026-08-18). It skips today and states what the engine does,
with citations, so it cannot become a description of whatever gets built. Three of its eight exist
because the obvious reading is backwards:

- `dcubemapsample_t.size` of **0 means the default size** (`bspfile.h:997`), and feeding it to
  `1 << (size - 1)` in C# gives `1 << 31` because the shift count is masked to five bits.
- `$envmapcontrast` defaults to **0** where 0 is normal, `$envmapsaturation` defaults to **1** where
  1 is normal — the pair defaults to opposite ends and means opposite things at the same number.
- `$basealphaenvmapmask` is **inverted**, annotated in Valve's own source as
  `specularFactor *= 1.0 - blendedAlpha; // Reversing alpha blows!`, and it costs the material its
  transparency because the alpha channel cannot also mean opacity.

Also pinned there: the reflection is **added** to the diffuse rather than blended
(`result = diffuseComponent + specularLighting`), the Fresnel term is `pow(1 - N·E, 5)` applied
**last** after tint/contrast/saturation, greyscale uses the Rec.601 luma weights rather than an
average, and the three mask sources are mutually exclusive by the shader's own SKIP list.

Run with `TF2DEMOSALVAGE_CHECK_SPEC=1` to execute those assertions against the SDK rather than
skipping them — every one is a claim about the engine, so all eight are checkable today, and they
are. A conformance test that only ever skips is unverified prose.

## B56 — the POV camera has no view interpolation, and no weapon models are drawn — OPEN, decided

**Two owner decisions, recorded so neither is relitigated:** the recorded view is to be
**interpolated the way the running game does it**, and **weapon models are to be rendered**.

**Where this bites already.** A POV demo does not network the recorder's own eye angles — the
client already knew them — so of the corpus, the eight single-player POV demos still report one
distinct yaw for their one player after the `m_angEyeAngles` fix. That is correct rather than
broken: those angles live in `dem_usercmd` and `democmdinfo_t`, which this project already parses
and the scene layer does not yet consume.

**`demo_interpolateview` is not in the SDK.** A whole-tree grep of source-sdk-2013 returns nothing;
it is an engine ConVar in `engine.dll`, the same category as the overlay renderer and for the same
reason. So its exact behaviour is not readable from source and must be measured, not remembered.

> **Amended 2026-08-16.** This cited `source-sdk-is-cloned-locally` for "why an empty grep is an
> answer rather than a failed search". **That memory does not exist**, and the principle as stated is
> the one that produced five wrong absence claims in this project — TF2's game code, `$modblend`,
> `moveparent`, the haptics block, and the container header.
>
> The claim about `demo_interpolateview` itself still holds: the tree really does return nothing and
> it really is an engine ConVar. What changes is the justification. An empty search is evidence only
> once a **positive control in the same sweep** shows the search could have found something — see
> `an-empty-search-needs-a-control`, which replaces the missing reference.

What is known and worth holding: it governs the **camera** between the per-frame `democmdinfo_t`
samples, and a community report ties an incorrect setting to a viewmodel reload animation glitch
(teamfortress.tv/66600). That second claim is **unverified here** — the thread was not read, and
the bug could as easily be a viewmodel cycle problem as a view interpolation one. Do not build on
it without checking.

**Shape it must take, and this is the owner's standing rule rather than a preference.** One place
turns recorded view samples into a camera pose, with interpolation as a flag on it. Not view logic
in the POV path and again in the free-camera path: anything copied between two files goes out of
sync, which is exactly how `m_angRotation` came to be read for players in one place while the
comment naming `m_angEyeAngles` sat in another.

The same rule is why the eye-angle fix is a single line at the pose rather than a field set on
`ScenePlayer`: the pose already feeds the interpolator, so position and angle cannot drift apart.

## B57 — player animation lives in included models, and cannot be baked — RESOLVED 2026-08-14

**Where it is.** A player model carries almost no animation of its own. Measured:

```
scout.mdl                        306 sequences,    2 local animations of 1 frame
  scout_user_animations.mdl        1 sequence,     1 animation
  scout_animations.mdl           377 sequences, 1012 animations, 5.0 MB
  scout_workshop_animations.mdl   90 sequences,   95 animations, 2.9 MB
soldier.mdl                      361 sequences,    2 local animations
  soldier_animations.mdl         419 sequences,  858 animations, 5.4 MB
```

Reached through `studiohdr_t.numincludemodels` at 336 and `includemodelindex` at 340, entries of
eight bytes (`mstudiomodelgroup_t`: a label index and a name index, both relative to the entry).
Offsets counted from `studio.h`'s field order and anchored on `numbodyparts` at 232, which this
project had already verified against real files. **medkit_small reports zero included models**,
which is the control that says the offsets are not landing on arbitrary data.

The two local animations are the reference pose — the thing that stands a player upright (B?, see
the animation commit). Everything a player actually does is in the included models.

**How a sequence number resolves.** `virtualmodel_t::AppendSequences`
(`public/studio_virtualmodel.cpp:142`) merges sequences **by label**: the base model's local
sequences first, then each included model appends only those whose names are not already present.
So `m_nSequence` indexes that merged list, and resolving it means walking the same merge.

The useful consequence: a virtual sequence maps to a *(group, local sequence)* pair, and that
group's own model holds both the sequence description and the animation it names. The virtual
ANIMATION list never has to be built.

**Baking is out for players, and the arithmetic is not close.** A health pack is one animation of
thirty frames over 1,608 corners. A scout is 1,012 animations over 23,442 corners; baking even a
tenth of them at thirty frames would be tens of gigabytes. The bake budget added for props
(B?, `MaximumBakedCorners`) would silently degrade every player to one frame, which is exactly the
state they are in now.

So players need the transform done per frame on the GPU: bone matrices in a constant buffer and
the skinning in the vertex shader, which is `IMaterialSystem::LoadBoneMatrix` and what the engine
itself does. `StudioBones` already produces the matrices and `StudioVertex` already carries the
indices and weights; what is missing is the renderer side.

**And the poses are not networked — measured, not assumed.** Across the whole committed corpus,
2007 to 2026, every playing player reports `m_nSequence` absent and `m_flCycle` at zero: one
distinct value each, over 244,951 samples on z1800 alone. So there is nothing on the wire to
replay, and `CTFPlayerAnimState` has to be emulated rather than read. So even with the data reachable and the renderer able to skin, choosing the
right sequence is a separate emulation problem. Ordered: reach the data, skin on the GPU, then
emulate the choice.

## B58 — jiggle bones and ragdolls, neither of which is rigid-body physics — OPEN, not urgent

**Dangling cosmetics and the floppy fish are jiggle bones, not physics.** `studio.h` defines
`STUDIO_PROC_JIGGLE` (5) with `mstudiojigglebone_t` and the `JIGGLE_IS_FLEXIBLE` /
`JIGGLE_IS_RIGID` flags: a per-bone spring whose stiffness, damping, length and angular
constraints are baked into the model. The client solves it every frame in `CJiggleBones`, after
ordinary bone setup and on the same matrices.

So it needs no physics engine, no collision and no broadphase — it is a procedural pass over bone
transforms and drops into the bone pipeline this project is building for GPU skinning. Cost is a
handful of springs per model.

**Ragdolls: the first version of this entry was wrong, and the correction is the useful part.** It
claimed "nothing about the resulting pose is networked" and concluded ragdolls may be unfixable.
That was asserted from reasoning about client-side simulation rather than from reading the wire
format, and reading it says otherwise. `DT_TFRagdoll` (`c_tf_player.cpp:517`) sends:

```
m_vecRagdollOrigin, m_vecForce, m_vecRagdollVelocity, m_nForceBone, m_hPlayer,
m_bGib, m_bBurning, m_bElectrocuted, m_bFeignDeath, m_bWasDisguised, m_bOnGround,
m_bCloaked, m_bBecomeAsh, m_iDamageCustom, m_iTeam, m_iClass, m_hRagWearables,
m_bGoldRagdoll, m_bIceRagdoll, m_bCritOnHardHit, m_flHeadScale, m_flTorsoScale
```

That is the complete initial condition — where it starts, the impulse, which bone took it, whether
it is on the ground — plus every visual variant a death can have. Only the simulation from that
point onward is client-side.

So a ragdoll is reproducible in principle, and **it is wanted**: frag-video makers care about death
animations, which is much of what a frag video shows. What remains unverified is whether Source's
VPhysics reproduces the same fall from the same start, and that is a measurement rather than an
argument.

Neither blocks player animation. Filed so the distinction is not rediscovered as "we need a physics
engine", which is the wrong conclusion for jiggle bones and only half the question for ragdolls.

**B57 resolved 2026-08-14.** Players are skinned on the GPU from the merged sequence table, posed
through Valve's bone remap, lit at their illumination centre, and advanced from demo time. The full
account is `docs/findings/21-player-animation.md`, including the two wrong turns — an illumination
hypothesis dropped on a blind experiment, and a bone remap whose absence produced a plausible pose
rather than an obvious failure.

What remains is not decoding but emulation, and is tracked as B61.

## B59 — what "useful to frag video makers" actually means, in 2026 — OPEN, scoping

Recorded because the owner named ragdolls as wanted "for frag vid makers", and the right scope for
that depends on what those people already use rather than on what sounds impressive.

**Lawena is no longer viable** — the tool this project already cites in its texture-quality notes,
alongside Chris' maxquality and mastercomfig. The current workflow is several tools together:

| tool | what it does |
|---|---|
| HLAE | camera control, `mirv_campath` for smoothing SourceTV demos |
| SparklyFX | depth, per-element visibility and colour, several streams in one take, FFMPEG encode |
| Méliès | automation: VDM generation, demo scanning, gameinfo installs |
| SVR / Source Demo Render | fast recording straight to uncompressed AVI |

**Read as a list of gaps, the interesting ones are not rendering.** Every tool above drives the
GAME and records its output, which is why they need gameinfo installs, VDM scripts and config
swapping at all. A standalone parser has a different shape: it already has the camera, already
decodes the events a demo scanner is looking for, and needs no game running to do either.

**The owner's priority is render output first**, then element isolation, then camera, then
automation — SVR, SparklyFX, HLAE, Méliès. Implementation need not follow that order, but the
value does, and it is not the order this entry first proposed.

Worth knowing how close the first one is. A recorder needs three things this project already has:
an offscreen target, a capture path (`--shot`), and a frame rate it can be pinned to — 24, 30 and
60 all exist as of the frame-limit work. The fourth is the one that is usually got wrong and is
right here by construction: **animation advances from DEMO time, never from frame time**, so
rendering at 24 frames a second produces correct motion rather than slow motion. A recorder built
on a viewer that advanced per rendered frame would need resampling, which is where judder comes
from.

What is missing for it is an encoder and a frame-sequence loop, not a renderer.

So the capabilities worth aiming at, ordered by fit with what exists here rather than by the
owner's value ordering above:

- **Camera paths and smoothing.** HLAE's `mirv_campath` exists because a SourceTV camera snaps
  between players. This viewer owns its camera outright (D35) and interpolates already.
- **Demo scanning.** Finding the kills is what Méliès automates; the event stream is already
  decoded here.
- **Element isolation.** SparklyFX records separate streams so an editor can composite. A renderer
  that draws from a scene graph can do this without recording twice.

Ragdolls sit under the first two rather than beside them: a death animation is what a frag clip is
of, so getting one wrong is visible in exactly the footage this would be used for.

None of this is committed scope. It is here so "relevant to frag video makers" is a statement about
measured tooling rather than a guess, and so the next person does not rebuild Lawena.


## B60 — the small ammo pack is the only 31-frame pickup, and the only one that stalls — OPEN, lead

The owner reported the small ammo pack pausing once per rotation, and reported the health packs as
perfect. Measured across every pickup variant, including the ones cp_process does not contain:

```
medkit_small 30    medkit_medium 30    medkit_large 30
ammopack_medium 30 ammopack_large 30   ammopack_small 31
```

**The one model with a different frame count is the one misbehaving.** That is a correlation rather
than a cause, and four checks on the arithmetic came back clean: its last frame really is identical
to its first (measured at zero units apart), its sequence really carries `STUDIO_LOOPING`, the phase
advances uniformly to one part in 1e14 with no discontinuity at the wrap, and its entity yaw never
changes so the spin is animation-driven.

Worth knowing before chasing it further: a full 360 degree turn returns every vertex to where it
started, so "the last frame equals the first" cannot distinguish a duplicated endpoint from a
complete revolution. If the ammo pack's 31 frames are 31 intervals of a full turn rather than 30
intervals plus a repeat, dropping the last frame removes real motion — which would read as a hitch
from the opposite cause to the one it was meant to fix.

**One measurement settles it**, and it is the angle between consecutive frames rather than the
identity of the endpoints. Take a vertex well off the axis and measure the turn from frame to
frame. If the step is 360/30 = 12 degrees, there are thirty intervals and frame 30 is a duplicate
endpoint, so dropping it is right and the stall is elsewhere. If the step is 360/31 = 11.6 degrees,
there are thirty-one intervals, frame 30 is real motion, and dropping it removes a frame's worth of
rotation once per turn — which is a stall from the opposite cause to the one the drop was meant to
fix, and would explain why only this model shows it.

The owner has deprioritised it until players are done. Filed with the numbers so it restarts from
one measurement rather than from nothing.


## B61 — the rest of CTFPlayerAnimState — OPEN, emulation rather than decode

Players stand and run. Everything else about what they are doing is still computed wrongly, and
none of it is a decoding problem: the demo says nothing about any of it by design, so each is a
piece of `CTFPlayerAnimState` to port.

In rough order of how visible each is:

- **Per-class playback rate.** Every class plays the same run sequence at its authored rate.
  `m_flMaxGroundSpeed`, from `GetCurrentMaxGroundSpeed`, scales it — so a heavy at 230 units and a
  scout at 400 should not have the same footfalls. Currently they do, and a heavy looks light-footed.
- **Ducking.** `HandleDucking` reads `FL_DUCKING` out of `m_fFlags`, which this project does not
  decode at all yet. A crouching player is drawn standing.
- **Upper-body aim layering.** The engine composes a lower-body sequence with an aim layer driven by
  pose parameters; this plays one sequence whole, so nobody points where they are shooting.
- **Jumping, swimming, taunting, the loser state**, and the weapon-specific variants — a demoman
  holding a shield has different sequences from one holding a launcher, and `m_hActiveWeapon` is
  another decode not done here.

The measurement that closed B57 also bounds this one: speed separates moving from still cleanly
across the era demos, so the inputs an animation state needs are derivable even where they are not
sent. What is missing is the state machine, not the data.

## B62 — a material can name no `$basetexture` at all — OPEN, and the first diagnosis was wrong

Player eyes draw as the missing-texture chequer. **The reason is that the eye material has no base
texture to find**, which is not what this entry first said.

```
"EyeRefract"
{
    "$Iris"               "models/player/shared/eye-iris-blue"
    "$AmbientOcclTexture" "models/player/shared/eye-extra"
    "$CorneaTexture"      "models/player/shared/eye-cornea"
    "$lightwarptexture"   "models/player/shared/eye_lightwarp"
```

`EyeRefract` composes an eye from an iris, a cornea normal map, an occlusion map and a light warp.
There is no `$basetexture` in it, so a loader that asks only for that finds nothing and reports the
material as unresolved — which it is not.

**The first version of this entry blamed DirectX-level texture variants**, having noticed that
`MODELS/PLAYER/SHARED/DXLEVEL80/EYEBALL_L.VTF` exists while the plain path does not. That
observation is true and irrelevant: those are the low-shader-model fallbacks, and the real textures
are the ones named above. The hypothesis was published on the strength of a suggestive filename
rather than on reading the material, which is the same shape of mistake as the illumination one.

The fix is to take the texture a shader actually uses when it names no base one — `$Iris` here,
which is the eye's colour. Doing that generally means knowing, per shader, which parameter carries
the thing a viewer without that shader should draw.

**A wrong log cost most of the time before that.** `Register` reported "material not found, tried …"
when every candidate came back with a null TEXTURE, which is a different failure. Corrected to say
what it knows. Note also that neither of `Resolve`'s specific warnings fired here — no missing VMT,
no missing VTF — and that silence was the clue: the material resolved and simply named no base
texture.

**The material census (B55) would not have caught this either.** It reports what a map's materials
ask for, and model materials do not go through it.

Also not started, and unrelated: **cosmetics and weapons are not drawn at all.** Only the base
player model is. Wearables are separate entities (`m_hMyWearables`) and a weapon is its own model,
so that is a feature rather than a texture defect.

## B63 — bone-merged attachments are the whole of cosmetics and carried weapons — OPEN, emulation

**A player draws as a bare class model** with no hat, no badge and nothing in hand. Not a decode
gap: the entities are all present and every bit of them is being read.

Measured on `demostf-cp_process_f12-2026-08-07.dem` at its own midpoint tick, live entities with no
origin include `CTFWearable 37/37`, `CTFRocketLauncher 3/3` and `CTFShovel 3/3`. One carried
weapon's entire property set is `m_hOuter, m_nSequence, m_iState, m_fEffects, m_flSimulationTime,
m_flNextPrimaryAttack, m_flNextSecondaryAttack, m_iBuildState` — no origin, no model index, **and no
`moveparent`**.

That is deliberate. `CBaseCombatWeapon::Equip` calls `FollowEntity`, which sets `EF_BONEMERGE`
(`0x001`) and zeroes local origin and angles, because a merged entity takes its parent's bone
matrices by bone NAME rather than transforming from a position of its own
(`shared/basecombatweapon_shared.cpp:987`, `shared/baseentity_shared.cpp:2360`,
`public/const.h:284`). At that tick 32 entities carry the flag and 60 name an owner.

Work needed, none of it in the decoder:

- Owner link — `m_hOwnerEntity` for weapons; wearables to be measured separately.
- Bone merge itself, which `StudioBones.Remap` already does by name.
- Model resolution for the merged entities that send no `m_nModelIndex` — 41 of the origin-less
  ones do send it, the rest presumably resolve through the attribute container's item definition.
- Active-weapon selection, since a player carries several and holds one.

Full account, including two wrong diagnoses that survived a round of work each, in
`docs/findings/22-bone-merged-attachments.md`.

## B64 — a player's movement sequence is a blend grid we take the corner of — OPEN, emulation

**The legs run one fixed direction whatever way the body faces**, reported as "the model faces
right, but the feet and legs bend 180 degrees the wrong way".

Not a decode fault. `CalcBoneQuaternion` and `ExtractAnimValue` were checked against
`bone_setup.cpp:374` and `:339` and `StudioAnimation` matches both. The gap is a layer up:
`StudioSequences` reads `mstudioseqdesc_t::anim` at `y * groupsize[0] + x` and takes the corner,
which is the whole grid for a prop and one extreme direction for a player's nine-way movement
blend.

TF2 sets `move_x`/`move_y` (`multiplayer_animstate.cpp:1413`) in `ComputePoseParam_MoveYaw`
(`:1575`) as the unit vector of travel in the body's own frame, snapped to eight compass points by
`SnapYawTo` (`:1443`). Both inputs are already available here — travel direction by differentiating
position as `SpeedAt` does, body facing from `m_angEyeAngles`.

Work: read `mstudioposeparamdesc_t` for the parameter ranges, map the pair to grid coordinates,
blend the four surrounding animations rather than taking `anim[0]`, and compute the pair per player
per frame. Related to B61, which covers the rest of `CTFPlayerAnimState`.

Account in `docs/findings/21-player-animation.md`.

## B65 — one player on BLU draws in RED — OPEN, not yet measured

Reported 2026-08-14 with cosmetics working: a single player on the blue team draws red. Skin comes
from the team (`m_nSkin = (team == TF_TEAM_RED) ? 0 : 1`) and the team is read from the player
resource's `m_iTeam.<slot>` with a fallback to the entity's own team property. One wrong player out
of twelve is the signature of that fallback firing for one slot, but this has not been measured and
the alternative — a stale resource entry — would look identical.

## B66 — speckling on player models — OPEN, bodygroups are not applied

Reported as "weird glitchy dots on them, but that maybe the lod or something". A candidate with a
known mechanism: an equipped cosmetic HIDES part of the base model through bodygroups, and this
project does not read `studiohdr_t.bodyparts` at all. The base body then draws inside the cosmetic
and the two z-fight, which speckles. Unmeasured; LOD is the other candidate and neither has been
ruled out.

## B67 — the posed player skeleton is not upright — OPEN, and it is upstream of B64 and B63

**Measured, cp_process, soldier at a mid-match tick:**

```
posed models/player/soldier.mdl: 26922 of 26922 corners weighted, 86 bones,
  extents x 55.4 y 62.3 z 22.8 (z from -11.6 to 11.1)
```

A standing TF2 player is roughly 25 x 48 x 83. This is a blob centred on the origin. The raw
model measures `x 47.9 y 84.5 z 24.8` and is authored lying along Y, so the pose is changing the
shape without standing it up.

Everything else being chased follows from it:

- Legs bending the wrong way (B64's symptom) is a badly posed skeleton, not only a blend choice.
- Every worn item sits at ankle height, because the bone it merges onto is itself near the origin:
  soldier `bip_head` poses to `(-1.4, -25.8, 0.6)` when its REST position is `(0, 75.2, -1.1)`.
  Scout is the same shape, rest `(0, 73.5, -1.4)`.

**What has been ruled out**, each read against the SDK rather than assumed:

- Flag handling in `CalcBoneQuaternion` (`bone_setup.cpp:374`) — matches.
- The run-length decoder `ExtractAnimValue` (`:339`) — same walk, same selection.
- `AngleQuaternion` (`mathlib_base.cpp:2016`) — our `FromEuler` matches it term for term.
- Bone ordering — no model lists a parent after its child.

So the fault is in how this project COMPOSES those pieces, which is homegrown: `StudioBones.RestPose`
walks the hierarchy itself rather than porting `Studio_BuildBoneChain`/`CalcBoneToWorld`, and
`StudioAnimation.Pose` substitutes animated values into a bone list rather than following
`CalcAndAddPose`. Each piece was verified in isolation and the composition never was.

**Recommendation, and the owner raised it first:** port the bone setup path faithfully rather than
continuing to repair an approximation of it. The pieces already ported are correct and can be kept;
what needs replacing is the assembly around them.

### B67 amended — the evidence is contradictory and the first conclusion was overstated

The commit that filed B67 asserted the posed skeleton is broken. That is not established. Two
measurements disagree and the screenshots side with the second one.

**What the extents report says**, soldier at a mid-match tick, all three axes:

```
posed soldier: x -12.9..42.5  y -48.3..14.1  z -11.6..11.1
posed scout:   x  -9.8..46.4  y -39.3..11.9  z -19.6..9.5
```

No axis is near the 83 a standing player needs.

**What the screen says:** players draw upright and recognisable, with hats on heads in several
captures. Some limbs are splayed wider than they should be, and some worn items are on the floor —
so something IS wrong, but not "the skeleton never stands up".

**What has been eliminated, each measured rather than assumed:**

- The blend is not the cause. Posing the same frame with and without pose parameters gives nearly
  the same extents (`z 22.7` against `24.0`), so resolving the grid changed the direction the legs
  run and not the shape of the skeleton.
- The matrix path matches Valve throughout: `FromQuaternion` against `QuaternionMatrix`
  (`mathlib_base.cpp:1885`), `Concatenate` against `ConcatTransforms` (`:658`) including the
  translation column, the chain against `Studio_BuildMatrices` (`bone_setup.cpp:4559`), `FromEuler`
  against `AngleQuaternion` (`:2016`), plus the flag handling and run-length decode checked earlier.
  There is no approximation left to point at, which weakens the case for rewriting the composition.
- The gibus skeleton is fine and its merge walks it correctly.

**The open question is whether the extents report measures what the GPU draws.** It applies bone
matrices to `_raw`, which is `ModelFrames.Geometry[0]` — and for a skinned model that is a BAKED
frame produced by posing the base model's local animation, not the bind pose. For a player the
local animation is the reference pose so the two are nearly the same, which is why this has not
obviously exploded; but "nearly" is doing real work in an argument that concluded a skeleton was
broken. Until the instrument is shown to measure the drawn vertices, its disagreement with the
screen is not evidence about the skeleton.

**Next step, and it is an instrument step rather than a fix:** make the report skin exactly the
vertices uploaded to the GPU, or drop it in favour of reading back what the shader produced.
Deciding what to rewrite before that is settled would be choosing a cause to fit a number that has
not been shown to mean anything.

### B67 amended again — vertices and bones agree, and both are Y-up

Three measurements of `models/player/soldier.mdl`, read straight from the files:

```
vertices: 9626 corners, x -24..24   y -0..84.5   z -10.1..14.7
bones:    bip_pelvis (root) at (0, 42.4, -0);  bip_head at (0, 75.2, -1.1)
hull:     (-10.1,-25.8,-3.6) to (14.7,25.8,84.5)
```

**The vertices and the bones agree**: the model is 84.5 units tall along **Y**, and the head bone is
75 up that axis. `bip_pelvis` is a ROOT, so its bone-to-world is its raw `pos` from the file with no
chain and no rotation applied — the file itself says Y.

That eliminates the transposition theory. There is no disagreement between the two readers that
skinning depends on, and `mstudiobone_t`'s offsets were checked against `studio.h` field by field.

The hull disagrees, and it is the least trustworthy of the three: its offsets were derived here by
counting fields rather than verified against a known value, and its ranges are a PERMUTATION of the
vertex ranges (`-10.1..14.7` and `84.5` both appear in both), which is what a four-byte slip would
produce. Treat the hull line as unproven until an offset in it is confirmed the way `illumposition`
at 92 and `numbones` at 156 already were.

**So the open question is now well posed:** the model data is self-consistent and Y-up, this project
draws it faithfully, and the result is a player lying down in the world — which is exactly the
owner's report that "the player models feet are always facing up". Static props are unaffected and
draw upright, so whatever supplies the standing orientation is specific to animated player models
rather than to the loader.

Candidates, none measured yet:

- A `$upaxis Y` compile, with something in the engine's load path applying the correction.
- The stand-up rotation living in an animation this project is not applying — the posed z span is
  23 where standing needs 83, so nothing currently supplies it.
- A root transform the engine composes that this project does not (`Studio_BuildMatrices` takes
  `angles`/`origin` and builds a `rotationmatrix` the root bone is concatenated with; this project
  applies its instance matrix in the shader instead, and if the two are not equivalent for a
  Y-up model that is where it would show).

The third is the one to measure first: it is the only place where this project's arrangement
deliberately differs from the engine's.

### B67 — what has been eliminated, so nobody repeats it

Every item below was measured, not reasoned about. The remaining fault is NOT in any of them.

| Checked | Against | Result |
|---|---|---|
| `FromQuaternion` | `QuaternionMatrix` (`mathlib_base.cpp:1885`) | matches, all nine terms |
| `Concatenate` | `ConcatTransforms` (`:658`) | matches, rotation and translation |
| bone chain | `Studio_BuildMatrices` (`bone_setup.cpp:4559`) | same structure |
| `FromEuler` | `AngleQuaternion` (`:2016`) | matches term for term |
| `CalcBoneQuaternion` flags | `bone_setup.cpp:374` | matches |
| `ExtractAnimValue` | `:339` | same walk, same selection |
| `mstudiobone_t` offsets | `studio.h` | field by field |
| `.vvd` fixup table | `vertexFileHeader_t` | handled; position offset 16 of 48 correct |
| the blend | posing with and without pose parameters | `z 22.7` against `24.0` — not the cause |
| bone ordering | every model probed | no parent listed after its child |

**The one fact left to explain:** player model vertices AND bones are both 84.5 tall along Y, while
static and animated PROPS read by the same code are tall along Z (`resupply_locker` 113.2,
`cappoint_hologram` 171.5, `medkit_small` 17.2). So the difference is in the data rather than in the
reader, and applying a real animation does not stand a player up — the posed z span is 23 where
standing needs 83, and the shape changes from the rest pose, so an animation IS being applied.

A yaw-only instance matrix cannot supply the missing rotation, and neither can
`Studio_BuildMatrices`, whose `rotationmatrix` is built from the entity's own angles and origin —
which for a player is yaw and a position at their feet. So the rotation comes from somewhere not yet
found, and "port the bone setup faithfully" would not by itself produce it.

**Next measurement, and it should come before any more code:** take one player model and one prop
through the identical path, dumping the root bone's `pos` and `quat` straight from the file. The
prop stands and the player does not, from the same reader, so the difference is visible in those
sixteen bytes or it is not in the loader at all.

### B67 — the discriminator, measured: props carry a root up-axis rotation and players do not

Same reader, four models, root bone only:

```
resupply_locker  body        quat (0.707,0,0,0.707)  euler (1.571,0,0)   exactly +90 deg about X
medkit_small     Scene_Root  quat (0.707,0,0,0.707)  euler (1.571,0,0)   exactly +90 deg about X
scout            bip_pelvis  quat (0.985,0,0,0.175)  euler (2.789,0,0)   159.8 deg
soldier          bip_pelvis  quat (0.997,0,0,0.082)  euler (2.977,0,0)   170.6 deg
```

Every quaternion is unit length, which retires the "wrong offset" worry for good: these four floats
really are the rotation.

`1.571` is pi/2. A prop's root bone carries the **up-axis conversion** — the rotation studiomdl
bakes in for a model authored Y-up — and this project applies it, which is exactly why props stand
up and why nothing about them ever looked wrong.

A player's root carries no such thing. 159.8 and 170.6 degrees are the pelvis's own bind
orientation, not a Y-to-Z conversion, and they differ per class, which a fixed axis conversion
never would.

So the two model kinds are NOT equivalent and never were: props are self-standing because the
correction lives in their data, and players need it from somewhere else. Every measurement in this
entry is consistent with that — player vertices Y-tall at 84.5, player bones Y-tall at 75, props
Z-tall after posing.

**What this does NOT yet say** is where a player's correction comes from in the engine. Candidates,
unmeasured:

- The animation data supplies it and this project is applying the wrong animation, or applying it
  to the wrong bones. Against this: the posed shape does differ from rest, so something is applied.
- The engine's own `SetupBones` composes a transform for animated entities that this project skips.
  `Studio_BuildMatrices` builds its `rotationmatrix` from the entity's angles and origin, which for
  a player is yaw and a position at the feet — that alone cannot stand a Y-up skeleton up, so if it
  is the answer, the angles being passed are not what is assumed here.
- The player's rest skinning matrix already contains the correction, and only its TRANSLATION was
  checked (it was ~zero, which a pure rotation about the origin also gives). **This is the cheapest
  one left and it should be measured first:** print the 3x3 of `RestPose(scout).Matrices[0]`. If it
  is the identity the model genuinely rests lying down; if it is a quarter turn the correction is
  already in hand and the fault is downstream of it.

### B67 RESOLVED — a substring match on a sequence name

Last measurement, the 3x3 of the rest skinning matrix, which had never been looked at:

```
scout   bone 0: [1 0 0] [0 1 0] [0 -0 1]
soldier bone 0: [1 0 0] [0 1 0] [0 -0 1]
locker  bone 0: [1 0 0] [0 1 0] [0 -0 1]
```

The identity, for players and props alike. So `poseToBone` exactly cancels each root's rotation and
the prop's pi/2 does nothing at rest — props stand up because their VERTICES are Z-tall, and players
lie down because theirs are Y-tall. At rest every model draws precisely as authored.

**A TF2 player's reference pose is authored lying down.** That is a normal thing in Source character
pipelines: the reference SMD is a T-pose on its back and every real animation is authored standing.
Nothing in the loader can or should correct it.

**Therefore the standing orientation can only come from the animation, and ours does not supply
it.** The posed z span is 23 where standing needs 83, while the posed shape DOES differ from the
rest shape — so animation data is being read and applied, and it is either the wrong data or applied
to the wrong bones.

That is the whole of B67 now, and it is one question rather than a symptom list. The two candidates,
in the order they should be measured:

1. **The bone remap.** `masterBone` renumbers an included animation's bones onto the base skeleton,
   and this project applies it only when `where.Group != 0`. A wrong or skipped remap moves the
   right rotations to the wrong joints, which is "scrambled rather than absent" — the exact
   signature here, since the shape changes without standing up. `StudioBones.Remap` is already
   tested in isolation; what is NOT tested is that the group a sequence resolves to is the group
   whose bones the remap was built from.
2. **Which animation is being read.** A player's sequences live in included models; if the group or
   local index is off, a real animation is decoded from the wrong file and produces a plausible,
   wrong pose.

Both are cheap to measure against the rest pose: pose scout with a known standing sequence and
report the z span. Standing is 83, and nothing else is.

## B67 RESOLVED — `Find` matched a sequence name by substring

**One line, and it explains every player symptom in this file.**

`PropModels.SkinnedModel.Find` looked a sequence up with `Contains` rather than equality, returning
the earliest label in the merged table that merely EMBEDS the wanted name. Measured on a scout:

```
Find("Stand_PRIMARY") -> sequence 9, label "AttackStand_PRIMARY"
real  stand_PRIMARY   -> sequence 175
```

`AttackStand_PRIMARY` contains `Stand_PRIMARY`, sorts earlier, and won every time. So an idle player
was posed with an ATTACK animation — and a TF2 attack sequence is an upper-body layer meant to be
ADDED to a base pose. Played alone as an absolute pose it leaves the skeleton near its reference,
which for a player is lying on its back.

That is why the evidence looked contradictory for so long: animation data really was being read and
applied, so the posed shape differed from the rest shape, and the model still never stood up.

Posed heights, scout, bones only:

| Sequence | Z span |
|---|---|
| reference pose | 14 |
| `AttackStand_PRIMARY` (what was being played) | 23 |
| `stand_PRIMARY` | 59 |
| `run_PRIMARY` | 68 |

Valve's own lookup is `stricmp` — exact. The fix is one `string.Equals`.

**Downstream of this, and expected to resolve with it:** B64's crazy legs, and the worn items sitting
at ankle height, since `bip_head` was down there with the rest of the skeleton. B63's merge, B64's
blend grid and the pose parameters were all correct and are unaffected.

**The wrong turns, kept, because four separate confident conclusions were wrong before this one:**
an up-axis conversion, an axis transposition in the readers, a broken bone composition worth
rewriting wholesale, and the blend grid. Each was filed with evidence; each was retracted by a
later measurement. What finally worked was comparing a player against a prop through the same
reader, then asking the lookup what it actually returned rather than assuming it returned what was
asked for.

## B68 — decals hover off the walls at a large offset — OPEN, found by the free camera

Visible the moment a perspective view existed: cp_process's red wall bands are drawn floating in
front of the brickwork rather than on it, by enough to read as a separate object. From the top-down
view this was invisible, which is why it survived every screenshot until now.

**Not the depth bias.** The rasteriser state that pulls a decal toward the camera changes depth
only; a decal standing off its wall in space is geometry placed wrongly, not sorted wrongly.

Most likely in how the overlay's plane is positioned — the offset along the face normal, or the
basis origin being applied in the wrong units or the wrong space. `MapWorld` builds the quad from
the overlay's basis, and the corner order there was already wrong once (settled by matching texture
aspect to quad aspect).

**The owner's hypothesis, recorded because it is a good one:** that this and the worn item still
sitting on the floor share a cause, both being things placed relative to something else and landing
at an offset. Worth checking before assuming two separate faults, though the two paths are
different code — a decal comes from the map's overlay lump and a worn item from a bone merge.

## B69 — an item with one unmatched root bone cannot merge, and lands at the wearer's feet — OPEN

Measured on cp_process. Cosmetics with a real skeleton now sit correctly:

```
ghostly_gibus_Scout   z 62.8..74.6   (a scout's head)
soldier_pot           z 61.4..71.1
bargain_britches      z  6.7..50.2
ninja_boots           z -0.5..15.1
```

But `hwn_spellbook_complete.mdl` has exactly ONE bone, named `mvm`, with no parent. It can match
nothing on a player, so the merge contributes nothing and the item is placed by the wearer's
transform alone — which is the player's ORIGIN, at their feet. Seven of them exist in this demo.

**The engine almost certainly does not bone-merge these.** `m_iParentAttachment` travels beside
`moveparent` (`server/baseentity.cpp:287`), and Source parents an entity to a named ATTACHMENT POINT
on the parent's model — `mstudioattachment_t`, a named transform relative to a bone — rather than
merging skeletons. That is the mechanism this project has not implemented, and it is what a
single-bone item needs.

Next step is to read `mstudioattachment_t` and check whether these items' owners carry an attachment
whose index matches what the entity sends.

### B68 REOPENED — the placement is correct, so the cause is not what was committed

**The fix committed for this was wrong and has been reverted.** It changed the decal depth bias,
and a depth bias cannot move geometry: it changes the depth value written, never the screen
position, so it can never produce a visible offset with parallax. The owner described a spatial
offset in the first sentence and the wrong mechanism was reached for anyway.

**Measured since, and it clears the geometry outright:**

```
PLACE median 0.00 units from the face plane, 396 of 491 within 8 units
```

Overlay origins sit exactly on the faces they are pinned to. `OverlayPlacementTests` has asserted
this all along and passes; the quads are where they belong.

So what is on screen is not a decal in the wrong place. The remaining candidates:

1. **A decal winning depth tests it should lose**, drawing over nearer geometry and so appearing to
   float in front of it. The reverted change made this WORSE if so, since Valve's `-262144` is a
   larger push than the tuned `-10000`.
2. **The wall itself not drawing**, which the owner also reported in the same screenshot — a decal
   correctly placed on a face whose brushwork is missing looks exactly like a floating decal.

**The decisive experiment, and it should come before any more code:** set the decal bias to ZERO
and look. Z-fighting means the geometry is coincident and the bias was only ever hiding it, which
proves candidate 1. Decals still hanging in space with no z-fighting proves the surface behind them
is absent, which is candidate 2 and an entirely different bug.

### B70 — the decal bias is a deliberate deviation from Valve, to be undone with real cameras

Recorded separately from B68 because it is a real future requirement rather than a bug.

`DefaultDecalBias` is `-10000` where Valve's `m_DepthBias_Decal` is `-262144`. That retune was
correct and necessary for the orthographic map view: a depth bias is a fraction of the depth RANGE,
and an orthographic projection spreads that range evenly over a whole map's height, where Valve's
constant is tens of world units.

**When the viewer gains real cameras — third person, point of view, the frame-maker's free fly —
those are perspective, and the value must return to Valve's**, because under perspective most of the
range sits near the camera and the constant means what Valve intended.

Two things learned from getting this wrong once already:

- Do not gate it on a flag the caller passes. A defaulted `perspective: false` silently restores the
  orthographic value for every camera someone forgets to annotate. Derive it from the matrix: under
  this project's row-vector convention an orthographic projection leaves `m[3]`, `m[7]` and `m[11]`
  zero, and a perspective one puts 1 in `m[11]`. There is no third case.
- Verify it against a picture before calling it fixed. The first attempt was committed as a fix for
  B68 on reasoning alone and changed nothing visible.

### B68 — the experiment settles it: the decals are fine, the WALLS are missing

Ran with the decal depth bias set to zero. **No z-fighting and no flicker**, confirmed by the owner
looking at it.

That kills the depth explanation outright. Coincident geometry with no bias z-fights; these do not,
so the decals are not sitting on a surface at all — there is nothing behind them to fight with.

Everything now agrees on one story:

- Overlay origins measure `median 0.00 units from the face plane`, so placement is right.
- The tail of that same measurement is the tell: only `396 of 491` pairings are within 8 units, so
  about ninety-five sit well away from any face they name.
- On screen, decals whose wall IS drawn look perfect — cp_process's "REDSTONE CARGO" lettering and
  its arrow sign sit flat and correct. Only the coloured bands float.
- The owner reported missing wall geometry in the same screenshot, independently.

**So this is not a decal bug. It is a brush face bug wearing a decal's clothes:** a correctly placed
decal on a wall that was never drawn looks exactly like a floating decal, and that is what has been
chased all evening.

Renamed in effect — the question is now "which brush faces is `MapWorld` dropping, and why", and the
decal path is exonerated. Starting points, none measured:

- The face filters in `MapWorld` — the height cut, the area bounds, and whatever discards nodraw and
  tool textures. A filter too eager takes real walls with it.
- Faces belonging to brush ENTITIES rather than the world model. `func_brush` and friends live in
  other BSP models, and a reader that walks only model 0 draws the map minus every door, every
  moving platform and a good deal of trim — which is the shape of what is missing.

The second is the stronger candidate: the bands in question are team-coloured trim, exactly the kind
of thing mapped as a separate brush entity.

## B71 — brush-model entities are decoded and then skipped, so doors never draw — OPEN

**The owner's observation, and it is exactly right:** the rolling doors were supposed to arrive the
same way the health and ammo packs did, and they do. Nothing is lost on the wire.

A `func_door` is a networked entity like any other. What differs is its model reference: a pack is
`models/items/medkit_small.mdl`, a door is `*12` — an inline BSP submodel. `ScenePropTrack.Classify`
already recognises the leading asterisk, and the probe run earlier listed `*1, *2, *5, *6, *7` among
the props at a tick, so they reach the scene layer intact.

They are dropped by the renderer, in two places:

```csharp
if (prop.Kind != SceneModelKind.Studio || _byModel.ContainsKey(prop.ModelPath))   // packing
if (prop.Kind != SceneModelKind.Studio || Batches(prop.ModelPath, frame).Count == 0)   // drawing
```

So every brush-model entity in every demo is decoded, tracked, interpolated and then discarded for
not being a `.mdl`. Doors, moving platforms, the cart on payload maps, anything mapped as brushwork
that moves.

**What it needs:** a submodel index `*N` names a range of faces in the BSP's models lump —
`firstface`/`numfaces` — which is geometry this project already reads for the world. Drawing one is
building those faces into a batch like any other and placing it at the entity's networked origin and
angles, which the timeline already carries. The face reading, the material path and the lightmap are
all done; what is missing is the models lump and a second geometry source in `EntityModelSet`.

Worth noting the entity lump is separately unused: `BspEntities` is referenced by nothing outside
its own file, so map-placed `prop_dynamic` models are not instantiated either. That is a different
gap with a similar smell, and it is NOT what makes the doors missing — the doors are networked and
already in hand.

### B71 amended — the brushwork IS drawn, baked into the static world at its compiled position

Measured, cp_process_f12, from the world build:

```
world: 11186 brush faces, 60 terrain faces, 1222475 prop triangles, 0 faces with no material;
1030 of the surfaces read belong to entity models rather than the world
```

`BspSurfaces` walks the whole faces lump, and a submodel's faces follow the world's in it — so all
1030 reach the builder and are baked into the static vertex buffer like any wall.

**So nothing is missing. Everything is in the wrong place and cannot move.** A door is drawn where
it was compiled; compiled retracted, it sits inside the ceiling and reads as absent. That is why
removing the normal cull helped the walls and did nothing for the doors.

This changes the work entirely. Not a second geometry source — a separation:

1. **Exclude faces above the world model's range from the static world build.** `models[0].FaceCount`
   is the boundary and is already read.
2. **Build each referenced submodel's faces as its own geometry**, keyed by its `*N` name, so the
   entity path can find it — the same path health packs already take.
3. **Place it by the entity's networked origin and angles**, which `ScenePropTrack` already carries
   and interpolates. A door then opens because the demo says it opens.
4. **Relax the two `Kind != SceneModelKind.Studio` guards** that currently drop `*N` at packing and
   at drawing.

The wrinkle worth stating before anyone starts: world faces are lit by LIGHTMAP and the entity path
lights by ambient cube. Moving brushwork through the entity path as-is loses its lightmap, so a
door would be flat-lit against a lightmapped wall. The engine lightmaps brush entities too, so this
is a real divergence rather than a detail — it needs either lightmap coordinates carried into the
entity vertex format, or the world shader used with a per-instance transform.

**Estimated honestly:** steps 1 to 4 make doors appear and move. The lighting question is a separate
decision that should be made deliberately rather than discovered.

## B68 RESOLVED — an overlay's face list is what to CLIP against, not a list to choose from

The map answered it, after four wrong guesses at the renderer. cp_process_f12's stripes are
overlays — `overlays/stripe_red` 45 times and `concrete/stripe_blue` 43, the two most used in the
map — and what distinguishes them from a sign is how much they span:

```
overlays/stripe_red  names 1 to 18 faces, median 3
signs/redstone       names 2 to 2 faces
```

The builder took the FIRST face sharing an orientation and drew a single flat quad from the
overlay's own corners. Correct for a sign on one wall, which is why the lettering and the arrows
always looked right. For a stripe wrapping a building the quad is a flat plane cutting straight
through it where the wall turns, hanging in the air on both sides.

The engine clips the overlay polygon against every face it names and draws a fragment per face.
Now so does this: Sutherland-Hodgman against each face's edge planes, the fragment dropped onto
that face's plane, textured from the overlay's orthonormal basis — clipping makes points that were
never corners, so the four corner UVs cannot be interpolated — and lit from that face's own lightmap
rectangle.

`222 decals placed across 54 materials, 0 lying flat on nothing`, against a previous run that
skipped every overlay whose first orientation match was the wrong wall.

**The wrong guesses, kept, because four is worth remembering:** a decal offset in the reader, the
depth bias, faces removed by the normal cull, and entity brushwork placed without its origin. Each
was plausible, each was committed or nearly committed, and each was killed by a measurement —
`median 0.00 units from the face plane`, no z-fighting at zero bias, and no model in the map
carrying a non-zero origin. The answer was in the BSP the whole time, in a face list being reduced
to one entry.

**And a bug inside the fix, caught by the existing tests:** the inward normal of an edge is the face
normal crossed with the edge, and which way that points depends on the outline's winding, which a
BSP carries both of. Assuming one clipped every fragment to nothing — indistinguishable from an
overlay missing its face, invisible in the counts, visible only as decals silently vanishing. It is
settled per edge against the face's centroid.

## B72 RESOLVED — a leaked depth state made models draw with no depth writes

**The owner connected the symptoms before the code was looked at, and was right:** a medkit drawing
over the medic from every angle, and a player's eyes drawing through the back of his head with the
back of the head visible from the front, are one fault.

`Device3D` sets the writing depth state, calls `_world.Draw`, and draws models afterwards.
`WorldRenderer.DrawTranslucent` — the world's last pass — sets a READ-ONLY depth state and never
restores it:

```csharp
context.OMSetDepthStencilState(_depthReadOnly, 0);
```

That is correct for what it is doing. Glass must not stop what is behind it from drawing, which is
why the state exists. It is wrong for everything drawn after it, and models are drawn after it.

With no depth writes:

- **Within one model**, its own triangles stop occluding each other, so whichever was submitted last
  wins. On a head that is the eyes through the skull and the back of the head over the face.
- **Between models**, distance stops mattering and submission order decides, so a medkit on the
  ground draws over a medic standing in front of it however the camera moves.

Fixed by setting the writing state at the top of the model pass rather than by restoring it inside
`DrawTranslucent`. A pass that depends on a state should establish it, not trust the previous pass
to have tidied up — the same reasoning that made the decal bias derive from the matrix rather than
from a caller's flag.

**Worth noting how long this hid.** It has been true for as long as models have been drawn, and from
directly overhead it is nearly invisible: a player seen from above has little of himself behind
himself, and a medkit is small. The free camera made it obvious within minutes, which is the second
defect of the evening that existed the whole time and only became visible once there was a camera
that could look at things.

## B73 — bodygroups are selected at LOAD time, so every entity sharing a model gets the same one

Body parts now contribute one model each rather than all of them, which stopped all three capture
point labels drawing at once. But the selection happens when the model is read, with `m_nBody` of
zero, so every entity sharing a `.mdl` shows the same alternative — the owner's "they are not the
right ones, but it's only a single one".

**The shape of the problem:** bodygroup varies per ENTITY and geometry is packed per model PATH.
cp_process's three capture points share one model and need three different label meshes.

**Valve does not repack.** The engine keeps every body part's meshes and chooses which to draw per
entity, which is the same arrangement this project already uses for team skins: one copy of the
geometry, a per-instance lookup at bind time, and a player who switches team is right on the next
frame with nothing rebuilt.

So the fix is to follow the skin pattern rather than the frame pattern:

1. Pack every model of every body part again, as before, but record for each batch which
   `(part, model)` it came from — the packing already groups by material, so this is one more field.
2. Decode `m_nBody` from `DT_BaseAnimating` and carry it on `ScenePose` beside `Skin`.
3. At draw time, skip any batch whose `(part, model)` is not what `(body / base) % nummodels`
   selects for that part. `StudioModel` already computes exactly that in `Select`.

`SelectedModels` and the load-time `body` parameter come out again when this lands; they were the
cheap half of the change and they are what makes the readers agree, so the lockstep note on
`StudioModelInfo` needs to move to wherever the per-batch tagging ends up.

**Related and probably the same fix:** the owner reports the wrong points showing owned at the
start — on 5CP two points begin owned by each team with only mid neutral. That is the control
point's team driving which label and colour it shows, so it needs `m_nBody` and the entity's team
together, not one of them.

## B74 — the mid capture point appears close up and vanishes as the free camera backs away — OPEN

Reported precisely, which makes it tractable: present in the orthographic view, present in the free
camera when near, gone within a short distance of backing away. Distance-dependent visibility means
something is culling against the camera, and the world is rebuilt whenever the free camera moves
(`_worldIsStale`), so a cull evaluated at build time is re-evaluated on every move — which is
exactly how a thing can come and go while nothing about it changes.

**Where to look, in order:**

1. `MapWorldBuilder.Build` takes an `area` and drops any surface not touching it (`Touches`), and
   `AppendProps` applies the same bounds to placed props. `MainForm` passes `_map.MainBounds`,
   which should be constant — but that is worth confirming rather than assuming, since it is the
   only bounds test in the path and the symptom is a bounds test behaving like a frustum.
2. Whether the capture point is a placed static prop, a `prop_dynamic` from the entity lump, or a
   networked entity. The three take different paths and only one of them passes through `area`.
   Note the entity lump is read by nothing (B71), so if it is a `prop_dynamic` it should be absent
   ALWAYS rather than sometimes — which would make this a different object than assumed.
3. The near and far planes, `NearZ` 7 and `FarZ` 28000. Far is well beyond a map, so it should not
   be this, and saying so is cheaper than wondering later.

**Not yet measured, and no theory should be committed before it is.** The last four attempts at a
similarly-shaped symptom were all wrong, and what settled it was asking the map what the object was
rather than reasoning about the renderer.

### B73 amended — the packing is right, so the fault is at the draw

Measured on the last run, all three links separately:

```
model:  cappoint_hologram.mdl — 1 body part, base 1, 4 alternatives, 9 meshes
demo:   cappoint_hologram.mdl — bodies 0, 2, 3 across the tracks
packed: bodygroups models/effects/cappoint_hologram.mdl: 1 parts, 9 batches spanning 4 alternatives
```

So the model offers four signs, the demo says which each point wants, and the packer keeps all four
in separate batches. `Shows` reduces to `alternative == body` for this model, because the single
part has base 1 and four alternatives.

**And every point still draws the "?" sign, which is alternative zero.** The selection is therefore
not reaching or not being applied at the draw, and the remaining suspects are all in that last hop:

- `ModelFrames.BodyParts` arriving null at the instance, which makes the
  `bodyParts is { Count: > 0 }` guard skip the filter entirely and draw every batch. It is passed
  positionally beside `SkinSwaps` and both are nullable, so a mis-ordered argument would compile
  and silently disable the feature.
- `ScenePose.Body` being lost between `PropsAt` and `ModelInstance` — the probe read it off the
  TRACK, not off the instance, so the two have not been shown to agree.
- The blended pass drawing the sign while the opaque pass draws all four, or the reverse. Both
  passes were given the body, but only one has been reasoned about.

**The next measurement, and it is one line:** log `bodyParts?.Count` and `body` inside `DrawModel`
for the hologram. Every hop before that one is now measured; this is the only one that is not.

### B75 — a test suite that steals the desktop, and nothing in it is a UI test

`ReferenceParser.Run` started the differential oracle with both streams redirected but without
`CreateNoWindow`. Windows allocates a console window for a console program regardless of
redirection, and **a new console window takes the foreground**. The differential suite runs the
oracle once per demo, so a full run fires a burst of window activations into whatever the person at
the machine is doing — reported for real on 2026-08-14 as clicks landing in a browser mid-run.

Fixed by setting `CreateNoWindow = true`.

**Worth generalising: the machine-wide lock does not protect against this.** The lock serialises
agents against each other; it does nothing about a run stealing focus from the *human*, which
CLAUDE.md calls out as the direction that matters more. And nothing about this suite looks like a UI
test, so nobody thought to check it. **Any `Process.Start` of a console program in a test needs
`CreateNoWindow`**, whether or not the project has a user interface.

**Second instance of B75, same day, different mechanism.** `FullScreenTests` constructs a `MainForm`
and calls `SetFullScreen(true)`, on the stated grounds that the form is never shown so the suite
needs no display. Full screen later grew an `OverlayWindow` for the transport bar, and `Show` puts a
real window on the desktop regardless of whether its owner is visible — so five tests each opened a
window that then sat there doing nothing. The overlay is now shown only when the form is visible,
which is also the runtime-correct rule.

Both halves of B75 are the same failure: **a test that was genuinely headless when written, and
stopped being headless because of a change somewhere else that nobody thought of as touching tests.**
The doc comment asserting headlessness is not a guard, it is a claim — and it kept being quoted long
after it went stale. A real guard would fail the run instead: something that notices a visible window
or a console allocation during the suite.

### B76 — the UI suite loses to the rest of the suite for the machine

**Not a parallelism setting inside the UI assembly — that is already serial, and correctly so.**
`dotnet test` on a solution starts one `testhost` process **per project, concurrently**, and no
`[NonParallelizable]` reaches across process boundaries. Measured: four testhosts — Corpus, Content,
Viewer3D and UiTests — all created between 20:40:21 and 20:40:23.

The corpus suite reads 774 MB of
local demos while the UI suite launches the viewer, loads a 100 MB map and waits 20 seconds for a
window. Under that load the window does not make it, and `GetMainWindow` fails with

```
The viewer's main window did not appear within 00:00:20.
```

which reads as "the viewer will not start" and is really "the machine was busy". Measured
2026-08-14: solution-wide run **4 failed of 10**; the same project alone, same commit, **2 failed of
10**, and neither remaining failure is a launch failure.

**The machine-wide lock cannot fix this** — it serialises agents against each other, and this is one
`dotnet test` competing with itself. Run the UI project in its own invocation:

```
run-exclusive.ps1 dotnet test tests/Tf2DemoSalvage.Viewer3D.UiTests/...csproj
```

Raising the timeout would be the wrong fix: it hides contention behind a longer wait and makes every
genuine launch failure cost more.

**Still failing after that, and open:** `TransportUiTests` — the speed readout does not follow the
shuttle buttons into reverse (2 tests). `TransportBar.cs` is untouched on this branch, so this is
not a regression from the work merged here; it needs its own look.

**The two survivors are the test's fault, not the bar's** (owner's read, and the evidence agrees).
`TheSpeedReadoutFollowsTheShuttleButtonsIntoReverse` fails at its second step, where it demands the
label read exactly `speed 2x` after one press of faster — an exact-string coupling to a format the
bar is free to change, and the only assertion in the method that is not loose. The reverse check
below it is `Contains("reversed")`, which is why the comment's off-by-one (five slower steps from 2x
land on −0.5x, not the −0.25x it claims) never showed up as a failure. Both need rewriting against
the ladder in `TransportBar.Speeds` rather than against strings.

### B77 — a player's yaw is stored twice and the copies disagree

Found by running the whole solution in one command (1597 tests), which is now possible.

`PlayersAt` builds `ScenePlayer` positionally and the argument list stopped at `LifeState`, so
**`Yaw` took the record's default of zero**: every player in a frame faced due east, whatever they
were looking at. The track path gained eye angles and this one never did. That is the shape a
default takes whenever it is also a legitimate value — nothing can report a missing yaw, because
zero IS a yaw.

Two fixes landed, each exposing the next:

1. Carry the yaw at all. It then read **220.997** where the track held **−139.003** — the same
   direction, a full turn apart, because the wire sends 0..360 and everything else here normalises
   to (−180, 180]. Anything comparing or interpolating the two is wrong by 360 at the wrap.
2. Normalise it. Still mismatched, and now by an amount that is not a whole turn.

**The remaining difference looks like interpolation against instantaneous.** `track.At(tick)`
interpolates between keyframes; the frame path reads the entity's value at that tick. Both are
defensible and they are not the same number, which means the real defect is that **the yaw is
recorded in two places at all**. The fix is for `PlayersAt` to read the track rather than keep its
own copy — one source, as with everything else here — but that is a change to the shape of the
scene layer and wants doing deliberately, not at the end of a session.

Note the feedback loop: this test takes 7.5 minutes on the corpus, so guessing costs far more than
reading. `docs/memory/two-recordings-of-one-value.md` is the entry that predicted this class.

### B78 — the shell's status test asserts an empty viewer's status line

`TheDeviceComesUpAgainstARealAdapter` expects the status bar to read exactly `Direct3D ready.`. It
passed while the fixture launched with no demo. Now that every UI test shares one viewer with a map
open, the status line has moved on to what it last reported about loading, and the test fails
against a viewer that is working perfectly.

The device coming up is worth asserting; the status bar at one instant is not the way to do it. The
viewer logs the device creation, so the log is the durable instrument.

**B77 and B78 closed.**

B77's remaining difference was **dead players**. Their entity follows whoever they are spectating,
so the track holds that player's position and that player's facing — and the scene layer already
knew this for position, keeping the last pose held while alive so a body stays where it fell. Yaw
was simply not included in that, which is one idea applied to half its data. `diedAt` now records
the facing alongside the position, and the test asserts only living players, because demanding a
corpse match its track is demanding that bodies swing round to face whatever the camera watches.

Worth keeping: **the yaw is still recorded in two places**, and the reason that is tolerable now is
that they mean different things — the track is where the entity is, the frame is where the player
was left. That distinction is the design, not a duplication, and the comments say so at both ends.

B78 was deleted rather than repaired, on the owner's read, which was right: every test in the UI
assembly now shares one viewer with a map open, so a device that failed to create takes the whole
assembly down. `ViewportPictureUiTests` reads the swap chain back and counts lit pixels, which is a
strictly stronger claim than a status string — and the status string had stopped being true anyway,
since it is a live readout that moves on once a demo loads.

### B73 closed — the pose was rebuilt without the body number

The hop nobody had measured was the last one, and it was `ScenePropTrack.At`. Asked for a pose
*between* keyframes it constructs a new `ScenePose` field by field — and `Body` was not in the list,
so it took the record's default of zero. Every capture point drew alternative zero, the "?" sign,
while the demo (bodies 0, 2, 3), the model (4 alternatives) and the packer (9 tagged batches) all
measured correct. On a keyframe the stored pose is returned whole, so the value was right at every
instant anyone had checked it and wrong at every instant anyone had *looked* at it.

Measured before and after, at the draw:

```
before:  drawing cappoint_hologram.mdl: body 0, 1 parts, 9 batches spanning 3 alternatives
after:   drawing cappoint_hologram.mdl: body 3 … body 0 … body 2
```

**Second instance of this exact shape today.** `ScenePlayer.Yaw` was the other: a record built field
by field, one field forgotten, and a default that is itself a legitimate value — so nothing can
report the omission, because zero IS a body and zero IS a yaw. Worth stating as a rule: **when a
type is rebuilt rather than copied, the rebuild is a list that must be checked against the type, and
a defaulted field is silent by construction.**

`ScenePropTrackTests.EveryDiscreteFieldSurvivesInterpolation` now covers every discrete field, and
samples strictly between keyframes because on one the defect is invisible. Verified by manipulation:
red with `Body = from.Body` removed, green with it restored.

The `[render] drawing …` line stays. It costs one log line per model per distinct body and it is the
instrument that would have found this in minutes rather than across two sessions.

### B79 — the BLU point draws every beam at once, and it is a regression

Confirmed by eye after B73: each point now shows the RIGHT sign, and the BLU one draws all of its
lighting tails/spotlights simultaneously. The owner reports this was fixed earlier in the session,
so it is a regression rather than an unfinished piece — and the only change between those two states
is B73 itself.

That narrows it usefully. Before B73, `At` returned body zero for every interpolated pose, so every
point drew alternative zero and nothing else: one label each, and no way to see a fault in any other
alternative. Carrying the real body number is what made alternatives 2 and 3 visible for the first
time, so **this is most likely a pre-existing fault in those alternatives that was previously
unreachable**, not damage done by the fix. Stated as a hypothesis, not a finding.

The measurement that has NOT been explained, and is the place to start:

```
model (probe):  cappoint_hologram.mdl — 1 part, base 1, 4 alternatives, 9 meshes
draw (log):     9 batches spanning 3 alternatives
```

**Four alternatives, three distinct tags across nine batches.** If two alternatives' meshes carry
the same `(part, model)` tag, then selecting one draws the other's geometry too — which is exactly
"all the beams at once" for whichever team collides. `StudioModel.ReadMeshes` assigns those tags and
is where to look; `HatSkeletonProbe` now dumps every mesh's `part`/`alt` for comparison against the
model, and needs `TF2_FOLDER` set to run.

Note for whoever picks this up: the probe answers with no viewer and no desktop, in seconds. Do that
before launching anything.

**B79, first measurement — the tag-collision hypothesis is dead.** The model's meshes carry:

```
9 meshes: [0,1,2] alt 0   [3,4,5] alt 2   [6,7,8] alt 3
```

Alternative **1 has no meshes** — an empty bodygroup, the "blank" option a mapper uses for "show
nothing". So four alternatives across three tags is correct and complete rather than a collision,
and it lines up exactly with the bodies the demo sends (0, 2, 3). Alt 0 is the "?" sign, 2 and 3 are
the team signs.

That also means **each sign is three meshes by construction**, and `Shows` selects exactly one
alternative's three. So the extra beams cannot be another alternative of this model leaking through,
which is what the previous entry guessed.

What is left, in the order worth checking:

1. **A different model.** The scene draws `5xcappoint_hologram` and `5xcap_point_base`; the beams may
   belong to the base, or to a light/sprite entity that is drawn regardless of ownership.
2. **The three meshes of one alternative are not all sign.** If one of them is the beam and it is
   authored per-state, all three drawing together is correct and the fault is elsewhere entirely.
3. **`m_nSkin`, not `m_nBody`.** A team colour in TF2 is usually a skin family rather than a
   bodygroup, and the pose carries `Skin` separately.

The probe now dumps this in seconds with `TF2_FOLDER` set and `--logger "console;verbosity=detailed"`
— without the logger the output is swallowed and the test merely passes, which is how this looked
like "the probe did not run".

**B79, second and third measurements — the hologram is innocent, and the skin was being lost.**

The filter was measured at the draw rather than reasoned about:

```
drawing cappoint_hologram.mdl: body 3, drawing 3 of 9 batches
drawing cappoint_hologram.mdl: body 0, drawing 3 of 9 batches
drawing cappoint_hologram.mdl: body 2, drawing 3 of 9 batches
```

Three of nine, for every body — exactly one alternative's three meshes. The hologram cannot be
drawing every state's beam, so whatever is doubled is not this model's bodygroups.

Asking the OTHER model at a capture point found something real:

```
cap_point_base.mdl: 1 mesh, 1 body part with 1 alternative, 3 SKIN FAMILIES
```

Three families is neutral, RED and BLU — the base disc carries its team colour as a **skin**, not a
bodygroup. And `ScenePropTrack.At` was dropping `Skin` in exactly the way it dropped `Body`: absent
from the rebuilt pose, defaulting to family zero, so every capture point base drew the same colour
however the demo set it. Fixed in the same place, verified by manipulation.

**Third instance of the field-forgotten shape in one session** (Yaw, Body, Skin), so the test no
longer checks fields one at a time: `EveryDiscreteFieldSurvivesInterpolation` now asserts the whole
pose survives with `ShouldBe(held with { X = … })`, which fails for any future field added to
`ScenePose` and forgotten in `At` the moment it carries a non-default value.

The hologram itself has **one** skin family, so `m_nSkin` can do nothing there — the sign is
bodygroup-driven and correct. Still unexplained, and the next thing to look at: whether the three
meshes of one alternative are sign-plus-beams by construction (in which case there is no defect in
the hologram at all and the doubled beams belong to a light or sprite entity), or whether two
hologram entities are being drawn at one point.

**B79, fourth measurement — everything about the SELECTION is correct, so the geometry is next.**

The model names its own alternatives, which settles what they are:

```
[0] cappoint_hologram_neutral_reference.smd   3 meshes
[1] ''                                        0 meshes   (blank bodygroup)
[2] cappoint_hologram_redteam_reference.smd   3 meshes
[3] cappoint_hologram_blueteam_reference.smd  3 meshes
```

And at the draw, across every baked frame and every body:

```
body 0: drawing 3 of 9 batches — kept [550:additive, 551:additive, 552:opaque]   ×30 frames
body 2: drawing 3 of 9 batches — kept [553:additive, 554:additive, 555:opaque]   ×30 frames
body 3: drawing 3 of 9 batches — kept [556:additive, 557:additive, 558:opaque]   ×30 frames
census models/effects/cappoint_hologram.mdl: 5 instances, bodies 0, 2, 2, 3, 3
```

Five instances for a five-point map, two RED, two BLU, one neutral. Three of nine kept for every
body on every frame. Materials classified identically for all three teams — two additive and one
opaque each. **Nothing in this project's chain distinguishes BLU from RED**, and the owner reports
RED and neutral correct with BLU drawing the neutral sign and its own at once, and the BLU point
rendering dark.

Correct batch count, correct materials, wrong picture leaves the **vertex ranges**. A batch names a
span of the packed model buffer; if those spans are computed wrong the draw renders another
alternative's triangles while reporting itself as blue's. Offsets accumulate through the pack, so
the LAST alternative is where drift appears first — and blue is alternative 3, the last one with
geometry. It also explains "dark", since the wrong span brings the wrong texture coordinates with it.

**Next measurement, and it needs no viewer:** log each kept batch's start and count alongside its
material, and check that alternative 3's span begins where alternative 2's ends and covers exactly
its own three meshes' vertices. `EntityModels` packs them; the seam to check is where a mesh's
vertex offset is turned into a batch range for a baked frame.

**B79, fifth measurement — the pairing is sound and alternative 2's mesh sizes are not.**

The `.vtx` and `.mdl` were suspected of disagreeing about the empty bodygroup, which would pair each
mesh's corners with another mesh's vertex range. They do not: corner counts track vertex counts
consistently across all nine meshes, so the two walks agree.

What the same log shows instead:

```
alt 0 (neutral):  74v/204c,  50v/144c,  74v/204c
alt 2 (red):      50v/144c, 436v/1728c, 436v/1728c
alt 3 (blue):     92v/306c,  50v/144c,  92v/306c
```

Every alternative carries a 50-vertex mesh, which is presumably the shared beam. The signs are 74
for neutral and 92 for blue — and **436 for red**, five times either. Three signs of the same shape
do not differ by that much.

And the arithmetic is suggestive: 74 + 50 + 74 + 92 + 50 + 92 = **432**, four short of 436. That is
what a mesh looks like when its vertex count spans the WHOLE model's vertex array rather than its
own slice — it would draw every alternative's triangles from one batch, which is the symptom.

Alternative 2 is also the one immediately after the empty bodygroup, so the empty model remains the
prime suspect for whatever produces the wrong count — just not through the pairing.

**Next:** read `mstudiomodel_t.vertexindex` and `numvertices` for each of the four alternatives, and
each mesh's `vertexoffset`/`numvertices` under them, straight from the file in the probe. If
alternative 2's model claims the whole array, the fault is in what this project reads for a model
that follows an empty one. If the file really says 436, the fault is not in the reading at all and
the sign genuinely is that dense.

**B79, sixth measurement — every alternative is a LIT logo and a DARK logo at the same place.**

The per-mesh material names, which nothing had printed until now:

```
alt 0: cappoint_logo_neutral   cappoint_beam_neutral   cappoint_logo_neutral_dark
alt 2: cappoint_beam_red       cappoint_logo_red       cappoint_logo_red_dark
alt 3: cappoint_logo_blue      cappoint_beam_blue      cappoint_logo_blue_dark
```

Mapping is exactly right, and so is everything else measured: the `.vvd` has **0 fixups and 1354
vertices**, matching 198 + 0 + 922 + 234 from the four alternatives; the `.mdl` byte offsets chain
0 → 9504 → 9504 → 53760 without a gap; red really is a denser sign at 922 vertices.

So no reader is wrong. What the names reveal is the SHAPE of the thing: each sign is a coincident
PAIR — a lit logo and a `_dark` logo occupying the same space, with vertex counts to match (74/74,
436/436, 92/92) — plus a shared beam.

This project draws the `_dark` one in the opaque pass and the lit one additively, at identical depth,
with no bias between them. Which of the two is visible is therefore decided by z-fighting, and that
is camera- and precision-dependent. It accounts for "the blue capture point is the only one
rendering dark", for the appearance of two signs at once, and it is the same class as the owner's
observation that the wall stripes sit at different distances from the wall depending on where you
stand.

**Next, and it is a reading task rather than a measurement:** open the three `_dark` VMTs and their
lit counterparts in the game files and find what separates them — `$selfillum`, `$additive`,
`$ignorez`, or a proxy driving alpha from the point's state. Whatever Valve uses to decide which of
the pair shows is what this project is missing, and it will be in the material rather than in the
model, which is why six measurements against the model found nothing wrong with it.

### B79 answered — `Modulate` was being drawn opaque

Valve's own materials, read from the game files:

```
cappoint_logo_blue.vmt       "UnLitTwoTexture" { $additive 1 … }
cappoint_logo_blue_dark.vmt  "Modulate"        { $modblend .63  $mod2x 1 … }
cappoint_logo_red_dark.vmt   "Modulate"        { $modblend .43  $mod2x 1 … }
```

**`Modulate` declares neither `$translucent` nor `$additive`**, and its `$alpha` is written by a Sine
proxy rather than being a constant below one — so every predicate this project had said "opaque". A
shader whose entire purpose is to multiply what is behind it was therefore painted as solid
geometry, directly over the lit sign it exists to shade. That is the dark slab.

It explains the asymmetry that made this so hard to place: **blue's `$modblend` is .63 against red's
.43**, so the same defect is far more visible on BLU — which is why six measurements of a perfectly
symmetric model, selection, span and material mapping found nothing, and why the owner saw one team
broken and the other fine.

Fixed as its own blend kind rather than folded into translucency, because the factors differ:
`Modulate` is `DEST_COLOR × ZERO` and `$mod2x` is `DEST_COLOR + SRC_COLOR`, which doubles the
product so mid grey leaves the destination unchanged. Both now classify into the blended pass and
pick their state per batch alongside additive and alpha.

Measured after: `558:modulate2x` where it previously read `558:opaque`, for all three teams.

**The general lesson, and it is the third time this session:** a predicate that answers a question
about a material by looking only for the flags this project already knew about will call anything
unfamiliar by the default — and "opaque" is a legitimate answer, so nothing can report it. The
material's SHADER NAME is a declaration in its own right and was never being read.

### B80 — `UnLitTwoTexture` is not implemented, so the capture point beam is grey stripes

Backface culling fixed the signs (owner: "the culling for the signs is working perfectly"), and what
remained is a grey striped column standing where the beam belongs. It is not an extra light: it is
the beam drawn with only half of its material.

`cappoint_beam_blue.vmt` is `UnLitTwoTexture` with `$basetexture` = `cappoint_beam_lines` — the grey
stripes — and `$texture2` = `cappoint_beam_blue`, which carries the colour. Valve's own pixel shader,
`stdshaders/unlittwotexture_ps2x.fxc`:

```hlsl
HALF4 baseColor  = tex2D( BaseTextureSampler,  i.baseTexCoord.xy );
HALF4 baseColor2 = tex2D( BaseTextureSampler2, i.baseTexCoord2.xy );
HALF4 result = baseColor * baseColor2 * g_DiffuseModulation;
float alpha = 1.0f;
```

Two textures MULTIPLIED, each with its own coordinates, times `$color`, and **alpha forced to one**.
This project samples the first only, so the stripes arrive without their colour and without the
second texture's shape.

The logo materials are the same shader and look right by luck: their `$basetexture` IS the logo and
`$texture2` is a detail overlay, so dropping the second texture loses subtlety rather than the
subject.

**To implement, in the order they matter:**

1. Sample `$texture2` and multiply, per the shader above. The pieces exist — `MapAssets` already
   decodes a second texture for world blend materials — but the operation differs: a blend material
   LERPS by vertex alpha, this MULTIPLIES.
2. `$texture2transform` / `$basetexturetransform` as real transforms. They are separate coordinate
   sets in the shader, not a shared one.
3. Material proxies, which is the other thing measured missing: the lit logo runs a Sine on `$color`
   (.8 to 1) and the dark one a Sine on `$alpha`, and both beams run TextureScroll on a transform.
   Nothing pulses or scrolls without them, which is why the owner reported "the CP brightness didn't
   seem to change at all". Proxies are a general mechanism, not a capture-point feature.

**B80, why only BLU — Valve authored the blue beam with its two textures the other way round.**

```
blue:    $basetexture cappoint_beam_lines    $texture2 cappoint_beam_blue     scroll $basetexturetransform
red:     $basetexture cappoint_beam_red      $texture2 cappoint_beam_lines    scroll $texture2transform
neutral: $basetexture cappoint_beam_neutral  $texture2 cappoint_beam_lines    scroll $texture2transform
```

Red and neutral name the COLOUR as `$basetexture`; blue names the STRIPES. Since this project draws
`$basetexture` and ignores `$texture2`, red and neutral come out right **by accident** and blue comes
out as the grey striped column the owner reported. The scroll proxy follows the swap — whichever
transform belongs to the lines texture — so the authoring is internally consistent.

**In the engine the difference cannot be seen**, because the shader multiplies:
`baseColor * baseColor2`, and multiplication is commutative. Valve's inconsistency is therefore
harmless there and becomes a one-team defect only in a renderer that drops one of the two textures.

That is the whole shape of this class of bug, and it is worth stating plainly: **a gap in what this
project implements is invisible until it meets an asset that leans on the part we skipped.** Two of
the three beams leaned the other way, which is why this looked like a blue-specific mystery for a
session and a half rather than a missing shader.

### B81 — the material census covers the world and not the props

The shader census prints `every shader the map's materials name is implemented` for cp_process, and
that is true of the WORLD's materials only. Props and entity models register their materials through
a separate path (`PropModels.Register`) after the census has run, so they are never counted.

**The materials behind the whole capture point investigation are prop materials.** `Modulate` and
`UnLitTwoTexture` live on `cappoint_hologram.mdl`, so the census would have reported a clean map
while a capture point drew as a dark slab — the exact failure the census exists to prevent, in the
one place it does not look.

The parameter census has the same hole, which is worth stating because its output looked complete:
the `$texture2 x5, $nocull x5` line that answered this session came from the world pass over a map
whose brushwork happens to use those materials too. A model-only parameter would have been silent.

**Fix:** accumulate each prop material's shader name and declared keys as `Register` resolves them —
the resolver already returns both — and census the combined set after props load rather than before.
Cheap, and it turns a report that reads clean into one that is.

Note the shape, since it is now familiar: **an instrument that covers most of its subject reads
exactly like one that covers all of it.** Same as a report built only from failures, and same as a
predicate that answers "opaque" for everything it does not recognise.

### B82 — items parented to an ATTACHMENT are not implemented, so they sit at the wearer's feet

The owner reports a halo at a medic's feet and an MvM canteen not rooting to its player. Measured
from the model rather than guessed:

```
hwn_spellbook_complete.mdl: 1 bones; [0]mvm<-ROOT
```

**One bone, named `mvm`, and it is a root.** No player skeleton has a bone by that name, so
`MergeMatchingBones` matches nothing and the item is placed by the wearer's transform alone — which
on a player is their feet. The gibus and other head cosmetics work because their bones DO share
names with the player's.

So this is not a bone-merge defect. These items are not bone-merged at all: the engine parents them
to a named attachment point on the wearer, which is `mstudioattachment_t` in the model and
`m_iParentAttachment` on the entity. Neither is read here, so every such item falls back to the
origin, and it will be every "all class" cosmetic of this shape — halos, canteens, spellbooks.

**Read before implementing**, per the rule this session earned: `mstudioattachment_t` carries a name,
the bone it hangs off and a local matrix; `CBaseEntity::SetParent` takes an attachment index and
`C_BaseAnimating::GetAttachment` composes it against the bone's world matrix. Confirm both against
the SDK before writing any of it — the attachment's matrix is stored relative to its bone, and
applying it in world space instead puts the item somewhere plausible and wrong.

Note the tell: a single-bone model whose bone name matches nothing is diagnostic on its own. Worth a
log line when a worn item merges ZERO bones, since that is exactly the case that cannot work and
currently draws in silence.

**B79 and the beam half of B80 are closed, confirmed on a current build.** The capture points show
the right sign per team, the signs are readable rather than see-through, and the BLU beam no longer
draws as a grey striped tower. Four separate defects, each found by reading Valve's files rather
than by measuring ours:

| Defect | Answer, and where it was read |
|---|---|
| Every point drew "?" | `ScenePropTrack.At` rebuilt the pose without `Body` |
| Points drew as dark slabs | `Modulate` drawn opaque — `cappoint_logo_*_dark.vmt` |
| Signs unreadable, both sides at once | back faces not culled — `imaterialsystem.h:180`, `imaterial.h:369` |
| Grey striped tower on BLU only | `UnLitTwoTexture` half-implemented, and props had no second texture — `unlittwotexture_ps2x.fxc` |

**Two process notes, both earned the hard way.**

The "fixed" claim was made from a screenshot of the RED point and believed for an hour. Evidence
about one case, conclusion about another — the same error as reading a keyframe and concluding
about an interpolated pose. **A per-team defect needs a screenshot per team.**

And the owner then looked at a STALE BUILD and reported it unfixed, which sent this back to
theorising about geometry. Several builds were launched in a row and every screenshot lands in one
folder distinguished only by timestamp. Cheap fix available: the viewer already logs at startup, so
logging its own build time, and stamping captures from the same clock, would make "which build am I
looking at" answerable instead of assumed.

Still open in B80: material proxies. The transforms and modulation are plumbed and sit at identity,
so nothing scrolls and nothing pulses.

### B83 — the capture point base draws almost black, worst under BLU

Not the hologram and not the sign: the DISC, `cap_point_base.mdl`. The owner allows that models are
dull until specular exists, and reports this as almost black rather than dull.

Measured, so the lookup is not the suspect. The model is one mesh with three skin families, and they
resolve exactly as they should — `(family * references) + reference` matches Valve's
`pSkinref(skin * numskinref + material)`:

```
skin family 0: cap_point_base, cap_point_base_red, cap_point_base_blue
skin family 1: cap_point_base_red, …
skin family 2: cap_point_base_blue, …
```

All three materials are `VertexLitGeneric` with `$bumpmap`, `$envmap env_cubemap`,
`$normalmapalphaenvmapmask 1`, `$envmaptint [1 1 1]`, and:

```
cap_point_base.vmt        $selfillum 0
cap_point_base_red.vmt    $selfillum 1
cap_point_base_blue.vmt   $selfillum 1
```

**The teams' materials are the SELF-ILLUMINATED ones and they are the dark ones**, which is the
wrong way round and is the strongest clue here. `$selfillumtint` is absent from all three and this
project defaults it to (1,1,1), matching the engine, so the tint is not it.

**What to compare, in this order:**

1. `$normalmapalphaenvmapmask 1` says the envmap mask is in the NORMAL map's alpha — which means the
   BASE texture's alpha is free to be the self-illum mask. If this project reads base alpha as
   something else, or feeds the wrong channel into the self-illum lerp, a self-illuminated surface
   goes dark exactly where it should glow.
2. `$envmap` is unimplemented (B55, 42 of 189 materials). Missing specular explains dull, not black —
   unless the envmap mask channel is being consumed by another path.
3. Read `stdshaders/vertexlitgeneric_dx9_helper.cpp` for how the two interact before changing
   anything. Both features read alpha channels, and which channel belongs to which is the whole
   question.

The asymmetry is the lever: neutral and team share a mesh, a bump map and an envmap, and differ only
in `$selfillum` and the base texture. Anything that treats those two identically cannot be the cause.

### B84 — players never chose a movement animation, and never blended one

Found by a reflection test written to stop a class of bug rather than a bug: every field of a
`ScenePose` is set to a non-default value, run through `ScenePropTrack.At`, and asserted to come
back. It named three fields the rebuild dropped — `Speed`, `MoveX`, `MoveY` — within a minute of
existing.

Two of those are read by the renderer:

- `MainForm` picks an animation with `SequenceFor(model, speed)`, and a null `Speed` skips that block
  entirely, so a running player keeps whatever sequence the demo last stated.
- `EntityModels.PoseValues` reads `move_x` and `move_y` off the pose, and (0, 0) is the standing
  corner of a nine-way movement grid.

**The values existed the whole time.** `PlayersAt` computes all three and writes them to
`ScenePlayer`; the renderer reads them from `SceneProp.Pose`, which nothing ever wrote them to. One
quantity, computed onto one type and read off another, so both layers of the animation — which
sequence, and where in its blend — sat at their defaults with no error anywhere.

Filled in `PropsAt` rather than at the keyframe, because all three are functions of where the entity
was a tenth of a second ago: that is a question about the TRACK, and a keyframe carrying them would
be wrong at every tick between two.

**Fifth instance of the same shape this session** — after `Yaw`, `Body`, `Skin` and the census's
`$modblend`. A value with a legitimate default, in a record built member by member, read somewhere
that cannot tell the difference. The reflection test now covers every field of `ScenePose` including
ones nobody has added yet, which is what the hand-written version could not do: it compared against
an object built in the test, so a new field defaulted on both sides and passed.

**Corrected by the owner: legs were already moving, so the heading "never animated" was wrong.** A
player's demo carries no sequence, so `Math.Max(0, -1)` selects sequence ZERO and the cycle advances
from playback time — the legs move because something is always playing, not because anything was
chosen. What was missing is narrower and worth stating exactly:

- **which** animation plays was never decided, because `SequenceFor(model, speed)` was skipped;
- **where in its blend grid** was always the standing corner.

So the failure was never "no animation". It was "always the same animation, at one point of its
grid", which looks like movement and cannot be told from correct movement without knowing what the
player was doing. That is the same trap as every other entry here: the wrong behaviour is a
plausible one.

Whether it now runs CORRECTLY is still a question for eyes — moving legs were never the evidence.

### B85 — LINQ in the per-tick entity walk, noted rather than changed — OPEN

**Not a defect, and recorded so it is not rediscovered as one.** The question was whether this
project already spends frame time in LINQ. Measured across the repeated paths, it does not — with one
exception worth writing down before it grows.

Every LINQ call site in a path that runs more than once per file load:

| Site | Runs | Cost |
|---|---|---|
| `EntityStateTable.cs:105` — `OfClass` is a `Where` over the dictionary's values | once per class, per moved tick | one enumerator per call |
| `DemoTimeline.cs:360` — `entities.OfClass(ResourceClass).FirstOrDefault()` | per moved tick | the same allocation again |
| `WorldRenderer.cs:1465` — `_sortedTranslucent` | inside `UploadGeometry`: world build and every resize | not per frame |
| `WorldRenderer.cs:2277` — `ReleaseTextures` | teardown | irrelevant |
| `PropModels`, `MapAssets`, `LightmapAtlas`, `MaterialCensus`, `MapWorld` | asset load | irrelevant |

So the only real one is `OfClass`, at roughly two enumerators per moved tick — order 80,000 small
allocations over a full demo. **That is a load-time cost, not a frame-budget one**, and nothing has
profiled it, so it stays as it is. The per-frame render loop and the per-vertex world build contain
no LINQ at all, which is the part that would have mattered.

**What changed instead is the analyzer.** SonarAnalyzer's S3267 rewrites a filtering `foreach` into
`Where`, and it is an error in this repo — which means it would push allocation into the decode and
draw loops as they grow, with no argument available at the call site. It is now off under
`managed/**` and left as an error everywhere else, so tests, asset loading and the SDK reference keep
being pushed toward LINQ and the two hot paths are not.

The scope is per assembly rather than per method deliberately: the boundary between hot and cold
moves every time a method is extracted, and a rule that has to be re-argued at each refactor gets
switched off wholesale in the end.

**If this is ever revisited**, the fix is a non-allocating `OfClass` — an index of entities by class
maintained on insert, which `EntityStateTable` is already the right place for — not a rewrite of the
call sites. Do it behind a profile, not behind this note.

### B86 RESOLVED — a VTF format constant pointed at the wrong format

**`VtfFormat.Dxt1OneBitAlpha` was 26. The engine's value is 20; 26 is `IMAGE_FORMAT_UVLX8888`.**
Found by `ImageFormatConformanceTests` the first time it ran, 2026-08-16.

**Why it survived: the enum is almost entirely implicit.** `public/bitmap/imageformat.h` assigns a
number to exactly two of its forty members — `IMAGE_FORMAT_UNKNOWN = -1` and
`IMAGE_FORMAT_RGBA8888 = 0`. Every other format is defined by its POSITION in the list, so
`DXT1_ONEBITALPHA = 20` cannot be checked by reading a line; it has to be counted. Counting by hand
is how one arrives at 26, which is a real format four places further down.

**It cost in both directions, and neither is an error.**

- A VTF declaring **20** — a genuine DXT1-with-one-bit-alpha texture — matched nothing in our enum,
  fell through to `Unknown`, and was reported as an unsupported format. The surface drew untextured.
- A VTF declaring **26** would have had a 32-bit uncompressed `UVLX8888` image decoded as 4-bit
  block compression, at one eighth the byte count. Not subtle on screen, and still not an exception.

**The general lesson, which is why this suite exists.** A constant taken from a list that numbers
itself cannot be verified by reading; it has to be derived by counting the same way the compiler
does. `SourceSdk.Enumerators` now models C's rule — start at zero, an explicit assignment resets the
counter, each member takes the next value — and the test that would have failed silently against a
two-entry extraction has a control that says so: `IMAGE_FORMAT_ABGR8888` must come back as 1.

**What is still not covered.** Eight of forty formats are decoded, which is deliberate — TF2's
content is overwhelmingly DXT1 and DXT5 — and the reader reports an unsupported format rather than
guessing at one. That gap is a decision and the count is asserted so it stays one.

### B87 — the test suite is still slower than it needs to be — OPEN, deferred by the owner

**Two causes were found and fixed on 2026-08-16; a third is noted here and NOT done.**

Fixed: `Tf2DemoSalvage.Content.Tests` had no `AssemblyTestPolicy.cs` at all, and
`Tf2DemoSalvage.Viewer3D.Tests` deliberately opted out of parallelism as a "UI assembly" — a
rationale that expired when the UI tests moved to `Tf2DemoSalvage.Viewer3D.UiTests`. Its own comment
dated itself: "today's four tests construct forms without showing them", written when the assembly
had four tests and left in place while it grew to 278, none of which construct a form.

| Suite | Before | After |
|---|---|---|
| `Content.Tests` (361) | 1 m 11 s | **22 s** |
| `Viewer3D.Tests` (278) | 1 m 59 s | **46 s** |

**Still outstanding, and the reason this entry stays open:**

- **`Corpus.Tests` takes 40 s over gcor alone**, and the full lcor run is around 30 minutes. Worth
  measuring whether demos are re-read per test rather than once per fixture.
- **Both fixed suites are still dominated by repeated asset work** — the same BSP decompressed and
  the same VTFs decoded by many tests. Parallelism hid that rather than removing it; a cached
  per-map fixture would cut it again.
- **Nothing enforces the policy file's presence.** Its absence from Content cost minutes and
  reported nothing, because a serial run and a parallel run produce identical pass/fail output. A
  test asserting that every unit and integration assembly carries both attributes would make the
  next omission fail rather than just cost.

**The measurement note that matters more than the numbers.** Caching the SDK reference's file
crawls and regex results was tried first, on the assumption that reading thousands of files under
`src/game` dominated. Measured: 553 ms before, 532–648 ms after. Noise. The cost was test-host
startup and the caching bought nothing — it is kept as a bound, labelled honestly as unmeasured
benefit rather than a win. The real cost was in a place nobody had looked, which is the usual
outcome of guessing at a profile.

**2026-08-16, measured in the real game: `mat_fullbright 1` changes nothing about the disc.**
Owner captured the lit/fullbright pair. That is a null result and it is the useful kind, because it
falsifies the standing theory rather than adding another candidate.

**Confirmed by the owner as engine behaviour, not a property of one capture**: fullbright does
nothing to capture points in the real Source engine either. So this is not "our screenshot happened
to be unlit" — the capture point materials are unlit by design, in the game, and always have been.

**Fullbright flattens lighting. A surface it does not change is a surface that was never lit.** So
the ambient cube cannot be the cause — an unlit material ignores the light cache entirely, and B83
has been chasing a lighting explanation for a material that has no lighting term. That also explains
why adding half-Lambert to the sun term did nothing: there is no diffuse term to modify.

Where this points instead: the disc's own shader and blend. The capture point family already turned
out to use `UnLitTwoTexture` for the beam (B80), whose pixel shader is
`baseColor * baseColor2 * g_DiffuseModulation` with alpha forced to 1 — a MULTIPLY. Two textures
multiplied is dark by construction if the second texture is wrong, missing, or defaulted to
something near black, and "almost black" is exactly what a multiply against an unbound texture
looks like.

**Next step is therefore a material question, not a lighting one**: what shader the disc's VMT names,
what its second texture resolves to, and whether this project binds it. Not another ambient
experiment.

**And the comparison is now clean.** Both the real game and this viewer draw the disc unlit, so
every lighting difference between them is irrelevant to B83 — which removes the settings confound
below for this bug specifically. If the real one is bright and ours is almost black while neither is
lit, the difference is in the textures or the blend and nothing else.

### The screenshots say envmap, and B55 was dismissed for the wrong reason

**What the captures actually show**: the BLU point is polished chrome — a mirror-bright ring and
dish with a cyan core, reflecting the sky. That is not a bright base texture. It is a **cubemap
reflection**, and it explains the fullbright null result exactly, because `$envmap` is a reflection
term and fullbright does not touch it.

**B55 already says `$envmap` is unimplemented** and that the owner identified it from the game's own
behaviour: control points are "very reflective and shiny", and DirectX 8.1 takes the shine off them
because dx8's `LightmappedGeneric` drops the envmap pass. Every part of that describes these
screenshots.

**So why does B55 explicitly rule itself out for the black disc?** Because it looked for the wrong
object. Its check was "no upward-facing WORLD FACE at mid's centre at all" — widened to 160 units
horizontally and 1024 down — and that is correct and irrelevant: **the disc is a prop, not
brushwork.** `cap_point_base.mdl` is a model. A survey of world faces was never going to find it, and
finding nothing was read as evidence against the cause rather than as evidence about the search.

**B81 is the missing link.** The census that would have named `$envmap` on this surface covered the
world and not the props — 1,034 prop materials it never looked at — so the one instrument that
reports unimplemented parameters was blind to exactly this object. B55's conclusion, B81's blind
spot and B83's symptom are one story: the material was never examined.

**The concrete next check, and it is a measurement rather than another theory**: with B81's census
now covering props, load a capture point map and read what it reports for the cap point materials.
If `$envmap` appears there, B83 is B55 on a prop and the two close together.

**Prediction worth stating before looking**, so it can be wrong: the disc's base texture is a dark
or mid grey metal, and essentially all of its apparent brightness in game is the reflection. That is
what "almost black" means here — not a broken material, but a correct one missing the term that
supplies most of its light.

### The pattern is POSITIONAL, not by team — and that is the decisive observation

**Corrected by the owner, and it kills the team explanation outright.** This entry has said "worst
under BLU" throughout, which reads as a team-colour problem. It is not: **the darkness does not
improve when RED caps the point.** What is actually dark is the **second and last points on BLU's
side of the map** — positions, not owners. (The last point is indoors and has not been inspected up
close yet, so it is the less certain half of that.)

**That single fact reconciles everything above**, and it is why the envmap explanation survives in a
modified form:

- **In the real game** the disc's brightness is a cubemap reflection. It looks bright wherever it
  stands, and fullbright does not touch it — which is what the captures show.
- **In this viewer** there is no `$envmap` (B55), so the disc is lit by the lightmap and the leaf's
  ambient cube instead. Its brightness therefore tracks **where it is**, and a point under cover or
  indoors gets a dark ambient cube and goes almost black.

So this project has not lost the disc's brightness uniformly; it has **substituted a positional term
for a reflective one**, which is exactly the failure mode that produces "some of them are fine". A
uniform loss would have been noticed immediately. Team ownership never entered into it, and reading
the pattern as a team problem sent this entry after `$basetexture`/`$texture2` swaps for a while.

**The decisive check is now a measurement with a stated prediction.** Log the ambient cube this
renderer samples at each capture point's origin on the same map. If the two dark points return a
markedly lower cube than the ones that look acceptable, the substitution above is confirmed and B83
closes into B55 — the fix being to implement `$envmap` rather than to adjust any lighting. If the
cubes are all similar, this explanation is wrong and the difference is in the material after all.

**Falsified within the hour, by the owner: BLU's second point is OUTSIDE, exactly like RED's.** So
"under cover gets a dark ambient cube" cannot be the explanation either — two outdoor points on the
same map, one dark and one not. That is the second hypothesis in this entry killed by an observation
rather than by a measurement, and both died to the same kind of fact: what the owner can see and the
code cannot.

**The remaining lead is per-prop and comes from this entry's own log.** B55 recorded "four
vertex-lighting checksum mismatches" in passing. A static prop is lit by baked per-prop vertex
colours in a `.vhv`, and `PropModels.Lighting` guards them with the model's checksum:

```csharp
catch (InvalidDataException failure)
{
    // Includes the checksum guard: lighting baked against a different build of the
    // model would light the wrong parts of it. Unlit is the honest fallback.
    ViewerLog.Warn("props", $"reading {path}", failure);
    return null;
}
```

That fits every constraint the other two failed. It is **per prop**, so it hits particular capture
points and not others; it is independent of team, because ownership does not change which file was
baked; and it is independent of indoor or outdoor, because a checksum is not a place. Four
mismatches is the right order of magnitude for "the second and last points on one side".

**What it does not yet explain** is the direction: the fallback returns null and the colour path
uses **white** where there is no lighting, which should make a prop too BRIGHT rather than too dark.
So either the fallback is not what these props take, or something downstream multiplies that white
by a term that is itself dark. Recorded as an open question rather than smoothed over, because the
last two theories were both plausible and both wrong.

**Then the constraint that makes it tractable: the map is SYMMETRIC.** The owner's point is that
nothing is built on one side and not the other — so RED's second point and BLU's second point are
mirror images with the same model, the same materials and the same surroundings. That eliminates
every material explanation at a stroke: identical materials cannot render differently.

**But a symmetric map is not symmetrically LIT, and that is the resolution.** Geometry mirrors; the
sun does not. `LUMP_WORLDLIGHTS` carries one sun direction for the whole map, so vrad bakes one side
brighter than the other and the baked result is asymmetric even where the brushwork is identical.
Everything fits:

| Constraint | Sun-direction asymmetry |
|---|---|
| symmetric map, identical materials | baked light differs anyway — geometry mirrors, the sun does not |
| both points outdoors | irrelevant; what matters is which way the sun faces |
| ownership does not change it | vrad baked it long before anyone capped |
| only some points affected | the ones on the shaded side |
| the real game looks fine | the disc's brightness there is a reflection, so its lighting barely matters |

So the earlier framing was the right variable with the wrong reason: not "indoors versus outdoors"
but "toward the sun versus away from it". This project substitutes a lighting term for a reflective
one (B55), and that substitution is only invisible where the lighting happens to be generous.

**The measurement is unchanged and the prediction is now sharper**: sample the ambient cube and the
baked prop lighting at each capture point's origin. The dark points should be the ones on the side
the sun faces away from, and the sun's direction is readable from the map's own worldlights rather
than guessed.

**Four hypotheses, three dead, and the pattern in how they died is worth more than any of them**:
every one was falsified by something the owner could see and no instrument here reports — the
fullbright behaviour, the team independence, the second point being outdoors, the map's symmetry.
That is the argument for the conformance tests added alongside this entry. A fallback that fires on
real corpus data should fail a test, not write a line in a log that gets read an hour later.

**Confound to control for before comparing any screenshot.** The reference captures come from the
owner's own in-game config, which is NOT the highest-settings target this renderer aims at — modern
TF2 config files ship inside VPKs rather than as `.cfg`, so the owner's custom config tooling cannot
express the high-end settings to test against. Any difference between a capture and this viewer may
therefore be a settings difference rather than a defect. Differences in a surface that is UNLIT are
still meaningful, since most of the settings axis is lighting and shadow quality.

### B83 addendum — the shine is a SETTING, and the stripes are a skin

**Two more captures, on a max-settings config, and the discs are matte.** Same map, same points, same
team states as the chrome captures earlier in this entry. The only difference is the graphics
configuration. So "the capture point is polished chrome" is not a fixed ground truth to match — it is
what one configuration produces, and another produces a flat grey dish.

That changes what B83 can even claim. This entry has been comparing our almost-black disc against a
mirror-bright one and calling the difference a defect; against these captures the target is much
closer to what we draw. **The envmap contribution has to become a setting in this viewer rather than
a fixed goal**, and until it is, no screenshot comparison of this surface means anything on its own.
The owner's read is that they *should* be shiny and that a pure default config needs checking to
settle it — so the target itself is not yet established.

**A separate defect visible in the same captures, and this one is unambiguous.** The owned RED and
BLU points carry ring markings that, per these shots, belong to the UNOWNED point only. That is not
lighting and not reflection: `cap_point_base.mdl` carries **three skin families** and the hologram
above it **four bodygroups** — neutral, empty, red, blue — both measured directly from the model
earlier in this project.

So the capture point's team appearance is selected by skin family and bodygroup, exactly the
mechanism B73 was about: bodygroups were being chosen at LOAD time, so every entity sharing a model
got the same one. B73 is closed for props generally; whether the capture point's per-point skin
follows the point's owner is a different question and is not currently tested.

**Which makes this the cheaper half of B83 to settle**, because it needs no rendering theory at all:
read the demo's capture point entities, read the skin each one is sent, and check the model draws
that family. Both halves of that are already implemented — `m_nSkin` is decoded (B73 era) and
`StudioSkins` reads the table — so the question is only whether they are connected.

### B83 second addendum — the rings are on EVERY point, and the "unambiguous defect" was not one

**Withdrawn by observation, 2026-08-16.** The owner, on ultra settings: the ring markings are on every
capture point, owned or not — lighter on an owned one, not absent. Two fresh captures show a RED-owned
disc and a BLU-owned disc, both with the rings, both with a coloured glow in the centre rather than a
team-marked surface.

So the paragraph above claiming the rings "belong to the UNOWNED point only" is false, and with it the
inference that the skin family is being chosen wrongly. Nothing about skins or bodygroups is
established as broken by these captures. What the earlier shots showed was the same rings at a
different brightness, read as presence versus absence.

**Fourth falsified hypothesis about this one surface, and they share a shape.** Ambient cube, envmap
on a prop, indoor shadowing, sun asymmetry, and now the skin family — each was a mechanism that would
explain the appearance, proposed from a screenshot, and each died the moment the owner looked at the
game rather than at the capture. The lesson is the one already in memory under
`ui-tests-run-every-time` and the UI section of the global standards: **an appearance claim that
cannot be checked by looking is a question, not a finding**, and this entry has now generated five
findings that were questions.

Concretely, for whoever picks this up: **stop proposing mechanisms for the capture point.** The two
things worth doing are (1) establish a target at a stated graphics configuration, since the shine is
config-dependent and no comparison means anything without one, and (2) verify `m_nSkin` reaches the
draw call as a directly measured fact rather than as an explanation for something seen in a picture.
The second is cheap and independent of every appearance question here.

### B83 third addendum — Valve's source states the answer, and always did

**The capture point's appearance rule is published.** `team_control_point.cpp:569`, in
`InternalSetOwner`, three consecutive lines:

```cpp
SetModel( STRING(m_TeamData[m_iTeam].iszModel) );
SetBodygroup( 0, m_iTeam );
m_nSkin = ( m_iTeam == TEAM_UNASSIGNED ) ? 2 : (m_iTeam - 2);
```

**Three mechanisms at once**, not the one this entry kept theorising about. The model can be swapped
per team, bodygroup 0 takes the raw team number, and the skin is a remap of it. With
`TEAM_UNASSIGNED` 0 and `TF_TEAM_RED` 2, the skin is **0 for RED, 1 for BLU, 2 for unowned** — all of
it arithmetic on published constants, nothing measured, nothing inferred.

Note that bodygroup and skin use *different* encodings of the same fact, three lines apart: the
bodygroup gets 0/2/3 and the skin gets 0/1/2. Using one where the other belongs selects a
valid-looking bodygroup that is wrong, with no error.

**This closes the "is it connected" question as a question, and it should never have been open.**
B83 ran to five falsified hypotheses across a month — ambient cube, envmap on a prop, indoor
shadowing, sun asymmetry, and a skin defect that did not exist — every one proposed from a screenshot
and every one killed by the owner looking at the game. The answer was in a file this project had
already cloned, under a belief that TF2's game code is closed which was written down three times and
checked none.

What remains is genuinely measurable and does not depend on how anything looks: does a capture
point's networked `m_nSkin` reach its draw call as ownership changes during a demo? That is an
integer comparison, immune to the graphics configuration that invalidated every screenshot argument
here. `ControlPointAppearanceConformanceTests` holds it, skipping, alongside the derived mapping.

**The generalisable rule, and it is now in memory as `tf2-game-code-is-in-the-sdk`:** when a
question is about what the game DOES, read the game. Measuring the picture can only tell you the
picture disagrees, and it will keep suggesting mechanisms for as long as anyone is willing to
propose them.

### B83 RESOLVED (the measurable half) — the skin was filtered out before anything could draw it

**Answered 2026-08-16, and the answer was no.** A capture point's networked `m_nSkin` did not reach
its draw call — and not because the renderer ignored it.

`ScenePose.Skin` was structurally 0 for every entity in every demo this project has ever parsed, and
zero is a legitimate skin, so nothing could report it.

> **Correction, later the same day.** This entry originally said `EntityState.NetworkedProperties` is
> a **whitelist** and that the property "was discarded before the scene layer saw it". That is false,
> and it was my own diagnosis.
>
> `EntityStateTable.Apply` writes **every** decoded property into the state unconditionally, and
> `NetworkedProperties` has no production consumer — only tests read it. `EntityState.Skin()` would
> have answered correctly all along. **The entire defect was one missing line**, the
> `Skin = state.Skin() ?? 0` in the pose construction, while the *clone* in `ScenePropTrack` copied
> `Skin` faithfully under a comment explaining why losing it draws every entity in family zero.
>
> Adding `m_nSkin` to the list was still worth doing, for a different reason than the one given:
> that list is the set of names `SendPropConformanceTests` checks against the SDK's send tables, so a
> name absent from it has nothing verifying it is real. Not retention — coverage.
>
> Found while auditing for the inverse defect, "consumed but not retained", which cannot exist
> because no retention gate exists. The audit's real yield was four production property names absent
> from that inventory and therefore unchecked.

**That comment is the sharpest part of this.** It was written when `Body` went missing from the same
rebuild, and it says: *"a record constructed field by field, one field forgotten, and a default that
is also a legitimate value so nothing can report it"*. Third instance of that exact shape —
`ScenePlayer.Yaw`, then `ScenePose.Body`, now `ScenePose.Skin` — and **the third was introduced by
the fix for the second**, which added `Skin` to the clone and not to the construction.

**Measured, not assumed.** `cp_foundry` carries skins 0, 1 and 2 in a 38 / 5 / 2 split and `z1800` in
42 / 15 / 1 — exactly the three values `team_control_point.cpp:569` produces. So 7 entities in one
demo and 16 in the other were drawing with the wrong material.

Held by two tests at two levels, since one would not have caught it: `RetainedPropertyTests` locks
the whitelist **as a whole list**, and `CorpusEntitySkinTests` proves a real demo's distinct skins
survive into the poses. The second was verified by manipulation — with the pose line reverted it
reports exactly one distinct skin.

**What this says about the rest of B83.** The entry ran to five falsified appearance hypotheses over
a month. The one question deliberately framed so it could *not* be argued from screenshots — a skin
is an integer, immune to graphics configuration — took twenty minutes once someone looked. The
remaining open item is the shine, which is config-dependent and still has no established target.

### B88 — a static written by every map load, and the real signal it obscured

**Found by the full-suite gate, not by any individual run**, which is the only way this class of
defect surfaces. `PropModels.RejectedPropLighting` was a static property assigned on every
`MapAssets.Load`. Viewer3D.Tests runs its fixtures in parallel (B87), so with several maps loading at
once the value belonged to whichever finished last — and a test asserting on it was reading another
test's map. It passed alone and failed in the gate.

Now carried on the load's own result as `MapAssets.RefusedPropLighting`. The general rule it violated
is older than this project: **a static is a variable shared with every future caller, including the
ones running at the same time.** The parallelism policy warns about exactly this and the static was
added anyway, three commits after that policy was written.

**The signal it obscured is worth keeping.** The failing run reported **two** refused prop lighting
files. `cp_process_final` has none, so those two came from a different map loaded by another fixture
in the same run — meaning some map in the test set does ship baked prop lighting this project
declines. That is the same phenomenon B55 recorded as "four vertex-lighting checksum mismatches" and
which B83 then chased.

Which map is not known, because the number arrived through the very static that made it
unattributable. Recorded so the observation is not lost: **a refusal exists somewhere in the test
corpus**, and the per-load list now makes it attributable the moment anyone looks.

### B89 — the full gate runs the UI suite against 1,700 competing tests

**`dotnet test` on the solution runs test ASSEMBLIES concurrently**, so
`Tf2DemoSalvage.Viewer3D.UiTests` — which launches the viewer and drives a real window — executes
while Content, Corpus and Viewer3D.Tests saturate the machine. Measured 2026-08-16: the UI suite
passes in 2 seconds alone and failed one of eight at 10 seconds inside the full gate.

**`run-exclusive.ps1` does not help here and it is worth being clear why.** That lock serialises this
machine against OTHER agents; it says nothing about what a single `dotnet test` invocation does with
itself. The rule that a UI suite takes the desktop has always been about not sharing — and running it
beside a CPU-saturating suite is the same sharing by another route.

**The gate must therefore run in two phases**: everything except the UI project, then the UI project
alone, both inside one lock. A single `dotnet test Tf2DemoSalvage.slnx` is not a valid way to run
this suite and has been used as one throughout this session.

**What is NOT yet established, and must not be assumed**: whether the failure is a synchronisation
defect in the UI test itself. This project's standing rule is that flake is a defect in
synchronisation or in the app and never noise, so "it was busy" is a description rather than a
diagnosis. The two-phase split removes the contention; if a failure survives it, the test is waiting
on the clock somewhere instead of on a condition. Which test failed was not captured, because the
gate's output was filtered to summary lines — itself worth fixing before the next run.

**B89 amended, 2026-08-16 — the diagnosis was confounded and should not stand as written.**

The owner was running TF2 in the background, testing a config in another session, throughout the
runs above. That was not known when the conclusion was drawn, and it breaks it.

What was claimed: the UI suite failed because `dotnet test` runs assemblies concurrently, and
"it survives the split, so it was starved rather than waiting on a clock". What is actually
supported: the UI suite failed in one run and passed in another, with an uncontrolled variable
between them. The game was using 30-45% CPU in the owner's own screenshots, so the machine load was
not the test suite's alone and the two runs are not comparable.

**The two-phase gate is still right**, because the reasoning for it does not depend on that failure:
`dotnet test` on a solution genuinely does run assemblies concurrently, and a UI suite genuinely
should not share. That argument stands on its own. What does not stand is calling the observed
failure evidence for it.

**And a hazard this exposed that matters more than the diagnosis.** A UI suite drives the real
desktop with synthesized input. `run-exclusive.ps1` serialises this machine against other AGENTS; it
knows nothing about the owner's own running game. A UI phase firing while TF2 is focused does not
fail — it delivers clicks and keystrokes into TF2, which is the same failure the global rules
already describe for two agents and has simply never been written down for the human case.

So the UI phase needs the desktop free of the owner too, and there is currently nothing that checks
it. Whether the right answer is a foreground check, a prompt, or simply not running UI tests
unattended is open.

### B91 — a fuzz finding that does not reproduce, and the report that hid it — PARTLY CLOSED

**A snappy crash artifact sat unnoticed on fuzz-box from 2026-08-15 00:04 until 2026-08-16.** Nine
bytes:

```
08              uncompressed length 8 (varint)
00 ff           literal tag, length 1, one byte
fc              literal tag, 0xFC >> 2 = 63 -> a 4-byte length follows
fe ff ff 7f     that length: 0x7FFFFFFE, so the literal length is int.MaxValue
09              trailing byte, never reached
```

**It does not reproduce.** `Snappy.cs` has not changed since before the crash, and today the same
bytes on the same box execute in **3 ms** and preserve nothing. On Windows x64 the decoder refuses
with the documented `InvalidDataException`. Kept as a regression fixture regardless —
`SnappyFuzzRegressionTests` pins the documented refusal, because the input remains the only artifact
of whatever happened.

**The most likely cause is the harness's wall-clock assertion, and that is a real weakness.**
`SnappyFuzzTarget` fails an input that takes longer than 5 seconds, on the correct reasoning that no
fuzzer-sized input can take that long without a non-advancing loop. But it is **wall clock on a
shared 1-OCPU box**, so it measures the machine as well as the code.

The dated correlation, offered as correlation and not proof: TcgDex's log entry of 2026-08-16 17:05
found a **runaway watcher waking every 5 seconds on that 1-OCPU box**, spawned by something of ours,
which could never exit because `pgrep -f libfuzzer-dotnet` matches the watcher's own command line.
It was alive on 2026-08-15, the reboot on 2026-08-16 killed it, and the input that "crashed" then
runs in 3 ms now. That is suggestive and it is not a measurement.

**The reporting defect is the part that is fixed, and it is the more important one.** The runner
counted `~/findings-<target>` — a directory that persists across runs — so once anything landed
there it printed

```
FINDINGS: snappy has 1 crash artifact(s).
```

on **every** subsequent run, for ever. That line then answers "was anything ever found", which is not
a question anyone asks at the end of a run, and a warning that fires every time is one nobody reads.
It is now counted before and after each target so the line reports what **this run** produced;
pre-existing artifacts get a quieter note that says to triage or delete them.

**Same family as the fuzz workflow's seed crash above:** in both, a signal that should mean "look
here" was indistinguishable from the background, so nobody looked. A finding nobody triages is worth
exactly as much as no finding.

**Left open:** whether the 5-second budget should be CPU time rather than wall clock, or whether a
timing failure should be reported as a distinct outcome from a thrown exception. Both would have
made this a two-minute diagnosis instead of a day and a half.

### B93 — DXT textures are decoded on the CPU and uploaded eight times larger — OPEN

**Raised by the owner asking whether more should be pushed onto the GPU.** The answer for the map
load is mostly no — it is I/O, LZMA decompression and pointer-chasing parses, none of which suit a
GPU — but the question found something better than it was looking for.

`VtfTexture.Decode` expands **DXT1, DXT3 and DXT5 into 32-bit BGRA on the CPU**
(`VtfTexture.cs:310-320`), and `WorldRenderer` creates every world texture as
`R8G8B8A8_UNORM[_SRGB]` (`WorldRenderer.cs:2688`).

**D3D11 samples BC1/BC2/BC3 natively — they are the same formats.** So the decode is work done to
make the result worse:

| | shipped | uploaded |
|---|---|---|
| DXT1 | 0.5 bytes/pixel | 4 bytes/pixel — **8×** |
| DXT5 | 1 byte/pixel | 4 bytes/pixel — **4×** |

Three costs at once: CPU time decoding, VRAM holding the expansion, and PCIe bandwidth uploading it.

**Feasible, because the textures are not atlased.** `ArraySize = 1` — each is its own resource, so a
block-compressed format drops straight in. That is the thing that usually blocks this.

**One real caveat, and it is not a blocker.** `MipLevels = 0` asks D3D to generate the mip chain, and
it cannot do that for block-compressed formats. VTFs ship their **own** mip chain, so the upload
would use Valve's authored mips instead of generated ones — arguably more correct, since those are
what the game samples, but it is a change in the upload path rather than a format swap.

**Connects to B90.** Texture decode is part of what makes the map load long enough to freeze the
window, so removing it shrinks the problem B90 is about, without being a substitute for taking the
work off the UI thread.

Not started. It touches the texture pipeline and the result has to be looked at — this is exactly the
class of change where a passing assertion says nothing about whether the map still looks right.

### B92 RESOLVED — the model was the discriminator, and it was wrong in both directions

**Fixed and measured on 2026-08-16.** Identity is now the serial number, which is the engine's own
rule and the one `EntityStateTable` already applied a few files away.

**The measurement, on `z1800.dem`, by swapping the discriminator and rebuilding:**

| Rule | Tracks | Slots holding more than one track |
|---|---:|---:|
| No identity check at all | 784 | 179 |
| **Model path (shipped until today)** | **907** | 190 |
| **Serial number (now)** | **849** | 216 |

**Read those two rows together, because they confirm the prediction in both directions at once.**

- **The model rule missed handovers.** Serial finds 216 slots that changed occupant; model found 190.
  Twenty-six slots changed hands without changing model — two rockets, two of the same prop — and
  their positions were appended to the previous occupant's track.
- **The model rule invented splits.** It produced 907 tracks against identity's 849, so **58 tracks
  were one object cut in half** by a model change that never changed the object.
  `team_control_point.cpp:569` calls `SetModel` on every capture, and players change model on every
  class change.

So the shipped behaviour both merged distinct objects and shattered single ones, which is what
"a proxy that disagrees with real identity in both directions" means concretely.

**A test I wrote for this could not fail, and I caught it by sabotage.** `CorpusTrackIdentityTests`
asserted that some slot held more than one track — but tracks are also removed on a Delete update, so
that holds whether or not the identity check works. Disabling `Continues` entirely left it green. It
is deleted rather than repaired: a corpus assertion sensitive to this needs to compare two
implementations in one run, and a weak one that looks like coverage is worse than none. The rule is
covered by four unit tests on `ScenePropTrack.Continues`; the table above is the evidence that it
matters.

That happened minutes after auditing the suite for exactly this failure, which is the honest measure
of how easy it is.

### B90 — the map is loaded on the UI thread, so the window exists and answers nothing — OPEN

**Found by auditing tests that assert a policy**, after a roster test was discovered certifying a
real bug. This is the same shape in a different file, and it is still live.

A slot is reused, so something has to decide whether a new occupant is the same object. Two places
do this and they disagree:

| Site | Discriminator |
|---|---|
| `EntityStateTable` | **serial number** — the engine's own identity |
| `DemoTimeline.RecordProp` | **model path** |

The timeline's own comment states the rule and then gives the example that breaks it:

> *"A slot is reused, so the model is what identifies the occupant. A rocket that explodes frees its
> index for the next one, and appending that one's positions to the old track would draw a rocket
> flying between two unrelated places."*

**Two consecutive rockets have the same model.** So for the exact case the comment describes — the
one that motivated the check — the discriminator cannot separate them, and the second rocket's
positions are appended to the first's track. The result is a rocket that teleports from where the
last one exploded to where the next one spawned, drawn as one continuous object.

It is worst precisely where it matters most: a firefight, where the same weapon fires repeatedly and
its projectiles cycle through a small set of entity slots.

**The fix is to use what the state table already uses.** `RecordProp` holds the `EntityState`, which
carries `SerialNumber`, so the identity is in hand — `ScenePropTrack` would need to carry it and the
comparison would move from model to serial. Small change; the reason it is not made here is that
`RecordProp` is private with eight parameters and has no unit test, so doing it properly means
building that seam first, and an audit is the wrong moment to improvise one.

**Not fixed also means not confirmed on real data.** The reasoning above is from reading the code and
the engine's identity model; nobody has yet watched a rocket teleport. That check is cheap once the
seam exists — count tracks whose poses contain an implausible jump.

**Why the audit found it and the suite did not.** There is no unit test for track building at all;
the logic is exercised only through corpus runs, where a merged rocket track is invisible unless
somebody is looking at rockets. The comment asserting the policy was the only statement of intent,
and it was wrong on its own example.

### B90 — the map is loaded on the UI thread, so the window exists and answers nothing — OPEN

**Found by CI going red, and the report named the wrong thing.** The UI job failed with
`System.TimeoutException : UIA Timeout` inside `Application.GetMainWindow`, which reads as a viewer
that will not start. It starts fine.

`MainForm` opens the demo and calls `LoadMap` **synchronously on the UI thread**. Between the window
being created and that finishing, there is a window handle attached to a thread that pumps no
messages — and UI Automation's `ElementFromHandle` requires the owning thread to pump. UIA gives up
after a few seconds of its own and throws `COMException 0x80131505`, which escapes `GetMainWindow`'s
wait rather than being retried by it. **A two-minute budget therefore expired in five seconds.**

The numbers, both from CI:

| Run | Outcome |
|---|---|
| 2026-08-15 19:31 | 8 passed, **2 m 30 s** |
| 2026-08-16 20:31 | UIA Timeout at **5 s** |

Nothing about startup changed between them. The merge added roughly 193 lines of work to
`MapAssets`, the load crossed UIA's patience, and a passing suite became a failing one with no
message pointing at the cause.

**The test-side change is in and is not the fix.** `ViewerApplication.Launch` now retries through the
COM timeout against the same `LaunchTimeout`, which is synchronising on the condition — "the process
is answering" — rather than on a clock, and costs a responsive viewer nothing. It is explicitly not
a flake workaround: the failure is deterministic given a slow enough load, and rerunning would be
the wrong response.

**The real defect is the application's, and a human sees it too.** A window that exists and ignores
input for the length of a map load is a frozen window, on CI and on a desktop. The fix is to load off
the UI thread, or to show the window only once loading is done — the first is better, because it can
show progress. Not done here: this branch exists to unbreak `main`, and moving map loading to a
background thread touches device creation and is its own change.

**Worth generalising.** Two budgets were in play and only one was visible in the code. The test
declared a two-minute wait and believed that was the limit; the shorter, undeclared limit belonged to
a layer underneath and won. Any wait built on someone else's client has that shape — check what the
layer below does when the thing it is waiting on stops answering, because its answer overrides yours.

### B71 RESOLVED — brush entities draw and move, and the count is exact

The last step landed the wiring. Measured on cp_process_f12 with autoplay:

```
[assets] 200 brush entities built from the map's models lump
[render] world: 10482 brush faces, 60 terrain faces, 1222475 prop triangles, 0 faces with no
         material; 704 faces held back for entity models rather than baked into the world
[props]  asked for 235, produced 235; skipped 0 not-studio [none], 0 no-batches [none]
```

That last line read `produced 94; skipped 141 not-studio` before, and every one of the 141 was a
door, gate or moving brush. Static brush faces fell by exactly the 704 now drawn as entities.

**The 704 is not the 1,030 measured earlier, and the difference is the reader rather than the map.**
The visibility and degenerate-face checks run before the boundary check, so 326 entity-model faces
were already being dropped for other reasons and are counted in neither number.

**One assumption died to the SDK on the way, and it is the useful part of this entry.** The first
draft treated `dmodel_t::origin` as the rotation pivot and wrote rotating doors down as a known gap.
It is not the pivot — `public/bspfile.h` annotates the field `// for sounds or lights` — and vbsp
has already done the work:

```c
// origin brushes are removed, but they set
// the rotation origin for the rest of the brushes
// in the entity.  After the entire entity is parsed,
// the planenums and texinfos will be adjusted for
// the origin brush
```

(`utils/vbsp/map.cpp`.) A mapper's origin brush becomes the entity's `origin` keyvalue and the
entity's brushes are shifted relative to it; without one the vertices are world-space and the origin
is zero. Both reduce to `world = entityOrigin + R × vertex`, which is the transform the entity path
already applied. Had the guess been "fixed" instead of checked, every closed door would have moved.

Evidence class: read from published source (vbsp, bspfile.h, `C_BaseEntity::DrawBrushModel`), and
measured on the corpus for the counts.

### B94 — a gate travels into the floor instead of up into its frame — OPEN

**The owner's observation, watching cp_process play back**, and the first real defect found by having
brush entities draw at all: the gates animate, but one of them moves DOWN into the floor rather than
up into the top of its frame. Their guess at a cause is that a number of players were around it at
the time.

Not diagnosed. What is worth writing down before anyone starts is that three explanations fit and
they are distinguishable:

1. **The movement is right and the geometry is offset.** A door whose faces are placed relative to a
   pivot the viewer is not applying would travel correctly and start in the wrong place, which reads
   as travelling the wrong way when the start and end are both near the frame.
2. **The origin is right and belongs to another entity.** Entity slots are reused, and the player
   correlation the owner noticed points here — a track that took a neighbour's origin would move a
   door by whatever that neighbour did.
3. **The demo really does say down.** A `func_door` moves along its own `movedir`, and a gate that
   retracts downward is an ordinary thing for a mapper to build.

Explanation 3 is the control and has to be excluded first, because it is the one where nothing is
wrong. The demo's own `m_vecOrigin` for that entity over time answers it without any rendering
involved: dump the track and read whether Z falls.

**Measured 2026-08-17. All three excluded, and the result is a contradiction rather than a cause.**

* **The demo says up.** Every moving brush entity on cp_process rests at its lowest Z and rises.
  A mapper's downward gate would rest at its highest.
* **The geometry is placed correctly.** Submodel 80 compiles -64 to 80 about its own origin — an
  origin brush at the shutter's centre, 144 units tall against 145 units of travel. With a resting
  origin of 640 that is 576..720 closed and 721..865 open, which is `origin + vertex` working.
* **Nothing sits at world zero.** A brush entity that never received `m_vecOrigin` would default to
  (0,0,0), which is floor level near the middle of the map and an exact match for the symptom. No
  brush track has a keyframe there.

**A contradiction was recorded here and it was my own measurement error.** The first pass reported
only THREE moving brush entities, all at the map's extremes, and concluded the owner's shutter could
not be among them. That came from the wrong file: `Corpus.Demo("cp_process")` returns the first name
containing the fragment, the local corpus holds two cp_process recordings from different servers
(`br.tf2pickup.org` and `na.serveme.tf #627716`), and the viewer had been driven on the second while
the test measured the first. A conclusion drawn across two demos is not a conclusion.

**Re-measured on the recording actually watched: 27 tracks over 13 distinct submodels** — 78, 80, 81,
132, 135, 137, 139, 141, 143, 144, 146, 185 and 186 — resting at 584, 640, 648, 696 or 744 and each
rising about 144 units. Several submodels carry more than one track because a round reset deletes and
recreates the entity, changing its serial number. So the demo carries gate motion across the whole
map, near second included, and there is no contradiction to explain.

Every one of the 27 still rests at its LOWEST Z and rises, so the mapper explanation stays dead.

**What the geometry says, and why it is not yet a verdict.** Submodel 80 spans -64 to 80 about its own
origin — an origin brush at the shutter's centre, not its base. The demo's resting origin is the
closed position, so closed puts the shutter half below that point: at a rest of 640, 576..720. That
looks like the symptom, but the engine computes the same `origin + vertex`, so it only means the
doorway itself sits at 576..720. It is a bug only if this project's transform disagrees with the
engine's, and nothing measured so far says it does.

**Still open, and what would settle it:** the viewer placed at the same spot as the owner's TF2
reference capture — `pos -625.75 -1702.36 689.03`, `ang -0.62 -37.58 0` — with the two pictures
compared. Every remaining question is about agreement with the engine, which no amount of reading
either file can answer.

One instrument theory is recorded as wrong so it is not retried: tracks split on serial number, and a
serial does not change when an entity leaves and re-enters the PVS — only when its slot is reused —
so PVS churn alone does not fragment a door's track.

Worth carrying forward regardless: **SourceTV is PVS-limited too**, just far less than a POV. A door
that moves while the STV camera is elsewhere is absent from the file, so any count of "how many moved"
is a statement about coverage as much as about decoding.

### D-lighting — brush entities are to be lit the way the engine lights them

**Owner's decision, stated plainly:** the lighting should be done as Valve does it. Brush entities are
lightmapped by the engine; the viewer's entity path lights by ambient cube, so every door now draws
flat against lightmapped walls.

This was raised as an open choice in the B71 amendment and is no longer one. It is not scheduled
next — the owner put the dark control points ahead of it — but the direction is settled, and the
implementation is the one the amendment named: lightmap coordinates carried into the entity vertex
format, or the world shader used with a per-instance transform.

### B83 RESOLVED (the dark capture point) — nearest-sample against a set compressed for interpolation

**The owner's correction is what located it.** The report was not "capture points are dark" but "ONE
capture point is dark, and its neighbours are fine" — which rules out a missing lighting term
outright, because an absent term darkens every instance equally. Work had already begun on local
lights under the wrong reading.

**The existing instrument could not see it.** The dark-model warning dedupes on model path, so the
five capture points sharing `cap_point_base.mdl` collapse to one report and a bright one reporting
first silences a dark one for ever; it also only fires at exactly zero, and this one was dim. A
per-instance line keyed on entity index made the comparison possible at all.

cp_process is mirror-symmetric, and the pair disagreed:

| entity | position | nearest | blended |
|---|---|---|---|
| #323 BLU 2nd | (-1664,-2176,768) | **0.1042** | **0.2886** |
| #328 RED 2nd | (+1664,+2176,768) | 0.3516 | 0.3520 |

**Reading both leaves out of the BSP gave the mechanism.** Leaf 2843 and leaf 498 each hold 16
samples, and vrad scatters their positions non-symmetrically — so the *nearest* sample to the query
is a 0.037 one on the BLU side and a 0.513 one on the RED side. Same geometry, different winner.

**The blend is published, and this project had written that it was not.** `BspAmbientLight` said the
weighting "is in the closed engine" and "cannot be transcribed", and argued nearest was a defensible
decision rather than a guess. It is `Mod_LeafAmbientColorAtPos` in
`utils/vrad/leaf_ambient_lighting.cpp` — `factor = 1 / (dist² + 1)`, normalised by the total.

**Nearest is not a coarse version of it.** `CompressAmbientSampleList` deletes every sample the blend
can already predict to within 3 in gamma space, so a map's stored samples are a deliberately sparse
set that only reconstructs the original function when interpolated. Nearest reads back an arbitrary
survivor of that thinning. That is why the error was 3.4x rather than a few percent.

**The owner's second hypothesis was also right, and the official map settles it.** The remaining gap
after the fix is real map data:

| | BLU 2nd | RED 2nd |
|---|---|---|
| cp_process_f12 | 0.2886 | 0.3520 |
| cp_process_final | 0.3842 | 0.3547 |

The release build agrees to within 8%; f12 is 18% out and its leaf carries a 0.0365 sample the final
version does not. A lighting quirk in that beta, which our lookup was amplifying enormously.

The last-point asymmetry (0.143 vs 0.212 on f12, 0.117 vs 0.190 on final) is present in **both**
builds and is therefore map data, not a defect.

Evidence class: read from published source (vrad), measured on two builds of one map.

### B95 — local lights are applied and contribute almost nothing — FIXED

#### The cause, 2026-08-20 — vrad divides intensity by 255 on write, and we divided again

`lightmap.cpp:1647`, under a comment of Valve's asking why the scale is what it is:

```cpp
VectorScale( dl->light.intensity, (1.0 / 255.0), wl->intensity );
```

**vrad works in 0–255 linear and stores 0–1.** The runtime multiplies by 255 to get back, which is the
counterpart of the unexplained 255 in `ColorRGBExp32ToVector` — divide on write, multiply on read, and
Valve flags both. An ambient cube reaches the shader as `linear / 255`, so in the cube's units a
light's contribution is simply `stored / falloff`, with no scale at all.

`LocalLights.IntensityScale` was `1/255` — that same factor a second time, in the same direction. Now
1.

**Measured on `koth_harvest_final`, the same frame before and after:**

```
bounce 0.1875  ->  with direct 0.1928      becomes  1.5385
bounce 0.0723  ->  with direct 0.0732      becomes  0.2924
bounce 0       ->  with direct 0.0011      becomes  0.2678
```

On screen: the spy's knife reads as steel rather than a silhouette, the sleeve is white, and the
gloves — black in TF2, so still dark — show knuckles and a highlight where they were a flat shape.

#### How it was found, because the obvious routes all failed

Four measurements, each ruling out the story the previous one suggested:

1. **The two terms reported apart.** One number could not say whether nothing was near or everything
   near contributed nothing.
2. **The map's lights characterised.** The innocent explanation — that most lights are surface lights,
   which carry no falloff and are rightly excluded — is false here: 126 of 136 are eligible
   spotlights, two of them 127 units overhead and inside their cones.
3. **Lightmap against ambient cube, in the shader's space.** 0.214 against 0.2358, agreeing within a
   tenth, which cleared the cube and put the fault on the light side. The first version of this
   comparison used the lightmap's STORED value and reported 231.8x, one edit from "fixing" a correct
   decoder.
4. **Every decoded light joined to its entity BY ORIGIN** and compared against the authored `_light`
   key through vrad's own arithmetic. Guessing the pairing gave per-channel factors of 102, 90 and 78
   — not constant, the signature of comparing two different lamps. Joined properly, one light came
   back short by exactly 255, and the spotlights by 171 because they carry
   `_fifty_percent_distance` and so take vrad's other branch, which never applies the ratio-at-100
   scale that the comparison divided out.

#### Why twelve tests agreed with the wrong constant

Every one supplies its own intensity and writes the divide into its own expected value. The old
remarks on `IntensityScale` stated the reason this could not work — "a test that supplies its own
intensity has no opinion about what units a map uses" — and the constant was then chosen on exactly
such a test, from a viewer-log observation of luminances of 140 to 1535.

They are corrected by scaling their INPUTS into the lump's real units, which leaves every expected
value unchanged: those tests are about falloff, cones, ranking and culling, and none of those claims
ever depended on the scale. `WorldLightScaleConformanceTests` now pins the scale itself against vrad's
arithmetic, including that a lamp overhead must outweigh the bounce.

#### Still open, deliberately not folded in

**vrad multiplies a spotlight's falloff by `dot2` as well as by the cone fringe** (`lightmap.cpp:1934`),
and `LocalLights.Cone` returns only the fringe. Worth about 1.75x at the angle measured. Filed as B122
so this change could be measured alone.

---

### B95, as originally written — local lights are applied and contribute almost nothing

**The heading and the paragraph below it were true when written and have been stale since**
`LocalLights.AddTo` was wired into `MainForm.LightAt`. Local lights are implemented, tested twelve
ways, and running. Read as "not implemented", this sent a later session off to build a feature that
already existed — the same waste as B120's duplicate, from the opposite direction. **A risk entry that
is not revised when its subject changes is worse than no entry**, because it is believed.

What is actually wrong is quantitative, and the original text is kept below for the history.

#### Measured 2026-08-20 — the direct term is a rounding error

Reporting the bounce and direct terms apart, across `koth_harvest_final`:

```
bounce 0.0723, with direct 0.0732      bounce 0.2561, with direct 0.2562
bounce 0.1875, with direct 0.1928      bounce 0.1677, with direct 0.1679
```

Under three per cent at best, usually under one, from 136 world lights.

**The lights are not the problem, which had to be checked because it was the likelier story.** A
near-zero direct term is CORRECT on a map whose lights are mostly surface lights, since those carry
no falloff and are rightly excluded as non-runtime. Harvest is the opposite:

```
Spotlight: 126, 126 with a falloff        Surface: 8, 0 with a falloff
SkyLight: 1, 0 with a falloff             SkyAmbient: 1, 0 with a falloff
```

**126 of 136 are eligible**, all pure inverse-square. And at the exact spot the symptom was seen —
the spy of `z1800` at tick 47601 — two of them are close and inside their cones:

```
Spotlight 127 units away: cone dot 0.570 against stop 0.707/0.259, gives 1.8687 INSIDE the cone
Spotlight 135 units away: cone dot 0.471 against stop 0.574/0.423, gives 0.8626 INSIDE the cone
```

Contributions of 1.87 and 0.86 against an ambient cube of 0.11. So the selection works, the cone
maths works, the falloff works, and something after them divides the answer away.

#### The suspect is one constant, and the argument for it cuts both ways

`LocalLights.IntensityScale` is `1f / 255f`, justified in its own remarks as reconciling a cube that
is "0–1 (`sample[i] / 255f`)" with a world light that is 0–255. **The cube is not divided by 255.**
`BspAmbientLight.Colour` is `mantissa * 2^exponent` and nothing else, and the comment beside it records
that a 255 was once there and made every cube "255 times too dark … which drew every player model as
a black silhouette".

So the stated reason for the constant describes a normalisation that was deliberately removed from the
other side of the sum. That is a strong argument that it should be 1.

**And a strong argument that it should not**, from the same remarks: without it, luminances of 140,
311, 903 and 1535 were measured against cubes of 0.1 to 0.4. `ColorRGBExp32` with the negative
exponents these samples carry lands naturally in 0–1 regardless of there being no explicit divide,
while vrad builds a world light as `pow(r/255, 2.2) * 255`, which is plainly 0–255. Two scales, and
the constant is doing real work.

**Both cannot be right and neither is settled by reading.** Removing it makes a lamp overhead dominate
a dark cube, which is what a lamp does; keeping it makes the sum well-behaved, which is what the
earlier session measured.

#### The experiment, run 2026-08-20 — the cube is right, and the constant is not the whole story

Comparing our model lighting against our own lightmap, whole-map distributions on
`koth_harvest_final`, in the space the shader receives each:

```
lightmap luxels:      588,480 values, median 0.2144, 90th 0.944, max 2.0
ambient cube samples:  13,193 values, median 0.2358, 90th 0.354, max 0.596
```

**The medians agree within a tenth**, so models are not systematically darker than the world and the
ambient cube's decode is correct. `AmbientCubeScaleConformanceTests` now pins that, two-sided, and was
verified by sabotage: multiplying the cube by 255 reddens it at 280x.

**What the distributions do say is where the gap is.** The lightmap's 90th percentile is 0.94 against
the cube's 0.354. A surface near a lamp gets nearly three times what the cube ever gives, because a
leaf's ambient sample averages a volume that includes shadow while a luxel sits on the lit surface.
**Closing that gap for models is exactly what the direct term exists to do, and it is contributing
0.007.**

A quantified target follows. At the spot measured, the nearest spotlight is 127 units away and vrad
baked the floor beneath it at roughly 0.9 in shader space; evaluating the same light at runtime gives
`39215 / 127²` = 2.43 in vrad's units, which the 1/255 scale turns into 0.0095. **About a hundredfold
short of what the compiler put in the lightmap for the same lamp at the same place.** Whether the
error is in the scale, in the assumption that stored intensity is pre-multiplied by the falloff at one
hundred units, or in the spotlight's cone normalisation is the open question — but it is now a
question with a number attached and a reference to check against.

#### An instrument that accused a correct decoder

The first version of the comparison reported **231.8x** and blamed the byte scale, and it was one edit
away from "fixing" `BspAmbientLight.Colour`, which is correct. It compared the lightmap's STORED
linear value, 0 to 510, against the cube's USED value. The texture is sampled as `byte / 255` and
doubled by the shader, so what reaches the arithmetic is `linear / 255` — the space the cube is
already in.

Two different spaces, one confident number, and a conclusion that inverted once the units were made to
match. Worth recording beside the finding because the wrong version was more persuasive than the right
one: 231.8 is so close to 255 that it read as proof.

#### The original plan for that experiment

**Compare our model lighting against our own lightmap at the same point.** The brushes in that room are
lit by these same lamps, decoded by us, and look correct on screen — so the lightmap is a known-good
reference for how bright that place should be. If the floor's lightmap luminance next to the spy is
several times the model's ambient cube, models are too dark by that factor and the constant is wrong.
If they agree, the constant is right and the darkness is elsewhere — the cube's per-leaf coarseness,
or the shader's handling of it.

That comparison needs nothing from the engine, uses two decoders this project already has, and gives a
number rather than an opinion. It is the next step, and the reason no fix was attempted here: a
constant changed to make one picture look better, with no measurement able to say which value is
right, is how the wrong one got in.

`LocalLightContributionProbe` reports the light-side half of it and is the instrument to extend.

#### The original entry, kept because it dated the work

`istudiorender.h` describes a model's lighting as an ambient cube "and lights that aren't in
locallight[]", beside `m_nLocalLightCount` and `m_LocalLightDescs[4]`. We apply the cube and the sun
and nothing else, so no prop receives direct light from a point or spot light.

This is a genuine divergence and the owner has confirmed it still wants doing — it is simply not what
made one capture point dark. Filed separately so the two are not conflated again.

`dworldlight_t`'s falloff terms, radius, and spotlight penumbra cosines are now read, with the
offsets checked against Valve's declaration. The falloff is stated inline in `bspfile.h`:
`1 / (constant_attn + linear_attn * dist + quadratic_attn * dist²)`.

**Measured input for whoever implements it:** cp_process_f12 carries 477 world lights — **290
spotlights**, 108 surface, 77 point, 1 sky, 1 sky ambient. Spotlights dominate almost 4:1, so cones
(`stopdot`, `stopdot2`, `exponent`) are the main case rather than an extra. Every point light on the
map is pure inverse-square: constant 0, linear 0, quadratic 1.

**The cost, measured 2026-08-20 on z1800 in a lamplit interior (B120, filed as new and folded back
here).** Every model in one frame, sampled at its own position:

```
lbtf_medal_participant_demo 0.0934    c_spy_arms       0.1111
c_proto_backpack            0.0946    c_knife          0.1112
homefront_blindfold         0.1037    spr17_upgrade    0.1114
fob_e_sniperrifle           0.1050    ghost_aspect     0.1191
v_watch_leather_spy         0.1086    c_engineer_arms  0.1192
```

**Twenty unrelated models between 0.09 and 0.12**, in a room with three ceiling lamps directly
overhead, while the walls and floor around them read as correctly lit. That contrast is the whole of
this defect made visible: the brushes carry the lamps in their lightmaps and the models cannot receive
them, so the split is not subtle and it is not confined to dark corners.

The owner noticed it as a spy's gloves looking flat black. The gloves are a red herring — a spy's
gloves are genuinely black — but they are where a scene-wide tenth becomes obvious first, because a
dark albedo times a tenth is indistinguishable from nothing.

**A before-figure to check an implementation against:** these numbers should rise for the models
under those lamps and stay put for anything genuinely in shade. Since this viewer renders
deterministically — two identical launches produce byte-identical captures — a frame hash plus this
table is a usable check that the change did something and did it where expected.

### B96 — no visibility culling, so a roof hides the map from above — OPEN, owner-diagnosed

**Not a lighting defect, and it was nearly chased as one.** The large black regions in the viewer's
top-down screenshots are a roof, drawn because this project has no visibility culling. TF2's own
spectator free camera does not show it — the owner supplied a reference capture of the same map from
above with the roof absent.

Worth stating plainly because it was on its way to being investigated as unlit geometry: the geometry
is drawn correctly, from a viewpoint the engine would never have drawn it from.

The engine culls per frame against the view frustum and the PVS, from wherever the camera is. This
project culls at BUILD time and only by normal — which the `MapWorld` comments already describe as
the deliberate deviation (D35 and the B68 work), on the reasoning that a build-time cull is only
equivalent for a camera that never moves. A free camera above the map is exactly the case that
breaks.

Related and already recorded: build-time shortcuts tuned for the top-down view broke once a free
camera existed.

### B94 RESOLVED — interpolation was not causal, so entities slid toward updates that had not arrived

**Confirmed by the owner watching a full cycle**, after four wrong theories. The shutter now sits
still while shut, rises when triggered, and stops at the sill.

A client draws `cl_interp` behind the present and can only blend between history entries it has
already received. This reader holds the whole demo, so tick 100 could see a keyframe at tick 610 and
slide toward it for the entire hold. Delta compression creates the gap in the first place — a
stationary entity sends nothing — so a door's two nearest keyframes routinely straddle a long
stationary stretch. That smear is the drift, and the same smear running backwards is the sink.

Two rules, both the engine's: sample `InterpolationDelayTicks` behind the tick asked for (0.1 s at
66.67 ticks is 6.67, rounded to 7), and never use a keyframe later than the tick asked for.

Measured per-entity Z range on cp_process, before and after: `*132` 564.7..735.8 becomes
584.0..728.0 — exactly rest to open, no excursion; `*139` 582.7..738.0 becomes 583.2..729.3. Before
any of the work in this entry, doors left the map downward and kept going.

**Never door-specific.** Every entity that holds still and then moves was being smeared: lifts, the
payload cart, a player standing then strafing.

**Ten tests changed, and they were wrong rather than stale.** They spaced updates a hundred ticks
apart and sampled the midpoint — a shape no client renders, since it holds such a gap for
ninety-three ticks and interpolates over the last seven. Their intent is unchanged and now measured
at four-tick spacing, which is what a demo carries.

**The four dead theories, recorded because each cost a cycle:** hermite overshoot (a constant-speed
close does not overshoot); a keyframe below rest (none exists); a keyframe defaulting to the origin
(none — and the test that looked for one required X, Y AND Z near zero, so it would have missed a
Z-only default); and a fixed sixteen-tick cap on the gap, which is not a rule the engine has. It has
no cap; it simply cannot see the future.

Evidence class: read from published source (`interpolatedvar.h`), measured on the corpus, confirmed
on screen by the owner.

### B97 — the free camera moves on key auto-repeat, so it steps instead of flying — OPEN

**Owner's observation, and it invalidates an instrument.** Camera movement is driven by Windows key
auto-repeat — "single clicks that repeat, like typing in notepad" — rather than by polling key state
each frame. So it stalls for the OS repeat delay, then advances in discrete jumps at the repeat rate.

Worth filing for its own sake, and worth knowing before using the viewer to judge anything about
timing: asked whether entity motion felt late after the interpolation delay landed, the owner could
not tell, because the camera already lags far more than 0.1 s. An instrument that lags cannot measure
latency.

The fix is to hold a pressed-key set from the key down and up messages and integrate movement per
frame against the frame time, which is also what makes diagonal movement and acceleration possible.

### D37 addendum — whether the recorded camera should share the interpolation delay is OPEN

Entities are now drawn 0.1 s behind the tick asked for, because that is where a client draws. The
demo's own recorded view origin is not: it is taken at the tick. Whether the two should agree has not
been settled, and the honest answer needs a side-by-side against the game rather than reasoning —
which B97 currently prevents.

### B97 RESOLVED — the free camera moved on key auto-repeat, not on the frame

**Owner's description: "single clicks that repeat, like typing in notepad".** Movement happened once
per `WM_KEYDOWN`, so Windows' auto-repeat decided how the camera flew — nothing for the repeat delay,
then fixed jumps at the repeat rate, and never two directions at once because auto-repeat reports only
the last key held. The code knew: a comment beside it read *"Held keys arrive here as auto-repeat,
which is coarse; smooth movement wants the frame tick and is worth doing once the view has earned its
keep."*

Now a held-key set feeds `FreeFlight.Movement`, which integrates against the frame time. Speed is
600 units a second (Shift ×4) instead of 32 units a press; diagonals work and are normalised, so a
diagonal is not faster than a straight line; and distance no longer depends on the machine's keyboard
settings or the frame rate.

**It shipped broken once, and the reason is worth keeping.** The key-up handler went into the form's
`WndProc`, which never sees it: key messages go to the FOCUSED window and the viewport panel takes
focus — the same reason the Escape handling sits where it does, in a comment a few hundred lines
above the new code. `ProcessCmdKey` works for key down only because WinForms walks it up the parent
chain; there is no equivalent for key up. So every key stayed held for ever, and pressing the opposite
direction cancelled to a standstill rather than reversing. A thread-wide `IMessageFilter` sees
releases before dispatch, so focus stops mattering.

**None of `FreeFlight`'s eleven tests could have caught that**, and they all passed against the broken
camera: the defect was in what FILLS the held set, not in what the movement computes. Same shape as
the wiring no-ops recorded elsewhere here.

**Valve settles the design question, in the opposite direction from polling.**
`public/tier0/protected_things.h` redirects `GetAsyncKeyState`, `GetKeyState` and `ReadConsoleInput`
to `__USE_VCR_MODE` names that will not link — banned outright, because VCR mode records and replays
input deterministically and polled global state is not reproducible. So the engine reads keys from the
message queue, which is what this now does. Raw input is the legitimate step beyond that, and Source
uses it for the MOUSE (`m_rawinput`), where it bypasses pointer acceleration.

### B98 — flying the camera re-projects the whole map every frame — OPEN, caused by B97

**Owner's observation: flight is smooth while the demo is paused and jittery while it plays.** That
split is the diagnosis. The view matrix upload lives INSIDE `ProjectMap`, so the only way to tell the
device the camera moved is to re-project every map segment and every surface triangle into screen
space for the top-down overlay — which the free view does not even draw.

Moving the camera once per keystroke made that affordable. Flying it every frame makes it the frame
budget, and during playback it competes with the per-frame scene rebuild, so frame times spike and
vary. Paused, it is the only work and stays even.

**The fix is to separate the two**, and the geometry already allows it: the world's vertices are in
map coordinates and only the view changes (D35), so a camera move is sixty-four bytes rather than a
projection. Extract the `SetCamera` call from `ProjectMap` and have flight call that alone.

One thing to check before taking it: `ReprojectScene` is gated on the same `_worldIsStale` flag and
scene points are stored in SCREEN space. Whether they are drawn in the free view decides whether
flight may skip that too, or must keep it and skip only the map projection. Not yet established, and
guessing it wrong freezes the overlay instead of the map.

### B98 RESOLVED — the view matrix upload was buried inside the map projection

**Confirmed smoother by the owner while playing.** Flight set `_worldIsStale`, and the only thing
that consumed that flag also re-projected every map segment and every surface triangle into screen
space for the top-down overlay — because `SetCamera` was called from inside `ProjectMap`. The free
view does not draw that overlay.

Affordable at one camera move per keystroke; the frame budget once the camera flew every frame
(B97). The tell was the owner's: smooth while paused, jittery while playing, because playback's
per-frame scene rebuild was competing for the same milliseconds.

`UploadCamera` now sends the matrix alone. Each reason it is safe in the free view was checked rather
than assumed: the world's vertices are in map coordinates and only the view changes (D35); the 3D
models are world-space and placed by their own matrices; and the screen-space scene points are a
map-view fallback drawn only for players with NO model, so they are empty in any modern demo and are
projected through the top-down camera anyway. The map view still rebuilds in full, because there
everything is projected to screen space.

The frame log now reports the LONGEST frame each second beside the rate, because a mean hides jitter
by construction — the average barely moved while the worst frame grew.

### B99 — playback costs twenty milliseconds a frame on the CPU, and rendering costs three — OPEN

**Owner's target: a thousand frames a second, which TF2 itself reaches.** Measured on cp_process at
a 300 fps cap with vertical sync off: about **48 frames a second** standing still with the demo
playing, longest frame 21–27 ms; **22–37** while flying, longest 46–100 ms.

**The first version of this entry blamed culling, and the measurement it was missing killed that.**
Every sample had been taken while PLAYING. Paused, the same viewpoint on the same map reports:

```
[render] 300 frames a second, longest 3.4 ms, paused
[render]  48 frames a second, longest  25 ms, playing
```

**Drawing the entire uncalled map costs 3.4 milliseconds.** It reaches the 300 cap with room to
spare, on roughly 1.4 million triangles — 11,186 world faces and 1,222,475 prop triangles. The owner's
read was right: a modern card does not struggle with that, and culling is an optimisation on top
rather than the reason for 48 frames a second.

**Playback adds about twenty milliseconds of CPU per frame**, and that is the whole gap. `ShowMoment`
rebuilds the scene on every frame: poses for every track, bone matrices for every skinned model, and
the lighting for each one.

**Prime suspect, and it was added on 2026-08-17 in this same session.** `LocalLights.AddTo` runs per
model per frame and scans all 477 of the map's world lights to rank the strongest four, then
evaluates the falloff again for each of six cube faces. At roughly 95 models that is about 45,000
light evaluations a frame, each with a square root, for a result that cannot change unless the model
moves. The ambient reconstruction beneath it also went from picking one sample to averaging sixteen.

Neither is wrong — both are what the engine computes — but the engine computes them for a moving
entity once, not for every entity on every frame.

**Measured, 2026-08-17, per second of wall time while playing:**

```
46.6 frames a second; sampling 15.5 ms, posing 895.8 ms (lighting 318.8 ms) of the second
```

Sampling the timeline — interpolating every track at the moment being drawn — is **16 ms a second**
and is free. **Posing owns about 900 ms of every second**, which at 46 frames is ~19 ms a frame and
is the entire gap. Of that, **lighting is ~320 ms**, so roughly a third of the total cost, leaving
~580 ms for bone matrices and transforms.

Both halves recompute results that mostly cannot have changed. A health pack that has not moved has
the same ambient cube and the same four nearest lights it had last frame, and most entities in a demo
are stationary at any moment.

**Step one is done, 2026-08-17, and it was worth five times the frame rate.** Measured on the same
demo from the same viewpoint, playing:

| per second, playing | before | after |
|---|---|---|
| frame rate | 46.6 | **250–278** |
| longest frame | 25 ms | **5–7 ms** |
| posing | 896 ms | **600 ms** |
| — of which lighting | 319 ms | **~95 ms** |

And while flying, which was the worst case: **22–37 frames a second becomes 110–170**, with the
longest frame falling from 46–100 ms to 14–17.

**Lighting did not fall to zero, and should not have.** Players and projectiles move every frame and
must be re-lit every frame; what disappeared was the recomputation for stationary props, which are
most of a map. `sampling` rose from 16 to about 90 ms a second for the same reason it looks worse and
is not — it is the same per-frame work spread over five times as many frames, 0.35 ms a frame before
and after.

**The order to fix, cheapest and most certain first:**

1. ~~**Cache lighting per entity, invalidated on movement.**~~ **Done.** Keyed on the entity and on the
   illumination point compared as BITS: the question is whether the model is at the identical point,
   not near it, and a tolerance would let a slow drift accumulate without ever refreshing. A held pose
   interpolates to a bit-identical `ScenePose`, so an unmoved entity produces an identical point.
   The sun is cached with it — it traces a ray through the BSP for sky visibility, and was being
   asked twice per model.
2. **Rank the local lights once per map, not per model per frame.** `LocalLights.AddTo` scans all 477
   of cp_process's world lights for every model on every frame to pick four. A spatial index, or
   simply caching the choice with the position, removes almost all of it.
3. **Then the remaining ~600 ms of posing, which is now the bulk of it.** Bone matrices for a skinned
   model are genuine per-frame work when it is animating, and are not when it is not — the same
   staleness argument as the lighting. **Measure before assuming, though:** two theories about the
   sinking door died to measurement earlier the same day, and "it must be the bones" is exactly that
   shape of guess.
4. **Culling last**, for B96's roof and the worst viewpoints, because rendering is already 3.4 ms.

**Both hot paths were added on 2026-08-17 in this session** — local lights, and the ambient
reconstruction that went from picking one sample to averaging sixteen. Neither is wrong and both are
what the engine computes; the engine simply does not recompute them for every entity on every frame.
A frame rate measured while playing was measuring the wrong thing for the whole of this entry's
first draft, and the fix it pointed at — culling — was the one thing the numbers do not support.

### B100 — every player plays one of two animations, chosen by speed alone — OPEN

**Owner's observation, and it outranks the remaining performance work:** the legs move, but "most of
the models are not doing anything but their running animation, and the animation being blanket
applied when each player class has a different one".

That is exactly what the code does. `PlayerAnimation.For` is two states resolved by name:

```csharp
bool moving = speed > MovingMinimumSpeed;
int wanted = moving ? model.Find("run_PRIMARY") : model.Find("Stand_PRIMARY");
```

No class difference, no crouch, no jump or fall, no aiming, no weapon, no death, no taunt, and the
primary-weapon variant assumed for everyone because `m_hActiveWeapon` is not decoded.

**A demo does not carry the answer, which is why this is a state machine and not a decode.** The
server never networks a player's sequence; the client computes it. So parity means reproducing what
the client computes, and all of it is published:

| Source | What it holds |
|---|---|
| `game/shared/tf/tf_playeranimstate.cpp` (1,551 lines) | TF2's own state: activity selection, per-class translation, aim layers |
| `game/shared/base_playeranimstate.cpp` | the base state machine it derives from |

`CTFPlayerAnimState::TranslateActivity` is where the per-class part lives, and its shape shows how
specific the real rules are — a Spy's `ACT_MP_STAND_MELEE` and a Demoman's `ACT_MP_STAND_SECONDARY`
are special-cased in the same branch as the ordinary `ACT_MP_STAND_PRIMARY`.

**Order of work, since the whole thing is large:**

1. **Activity from movement state first** — idle, walk, run, airwalk, crouch, jump start/float/land.
   That is the difference between "everyone runs on the spot" and legs that match what the player is
   doing, and it needs nothing but the position and flags already decoded.
2. **Per-class translation next**, which is `TranslateActivity` and the activity-to-sequence lookup
   against each model's own table.
3. **Weapon-dependent variants last**, because they need `m_hActiveWeapon` decoded first — a separate
   piece of work this project has not done.

**Do not optimise bone matrices before this.** Posing owns about 600 ms a second and bones are most
of it, but making the wrong animation cheaper is the wrong order.

### D37 addendum — the lighting is not verified, only unglitchy

Worth stating plainly because a five-times frame rate is easy to mistake for a correctness result.
The owner's assessment of the lighting is "I really don't know if it is right, I'm assuming it is,
it's not glitchy" — and the cache added under B99 preserves whatever the lighting computes rather
than validating it.

What is actually established: the local-light evaluation is transcribed from `mathlib/lightdesc.cpp`
and the ambient reconstruction from `utils/vrad/leaf_ambient_lighting.cpp`, both with their constants
checked against the SDK. What is known to DIVERGE: brush entities take an ambient cube where the
engine lightmaps them. What is untested: whether the result matches the game on screen, which needs a
side-by-side against a reference capture rather than a screenshot of ours alone.

### B101 — a moving player plays the BACKWARD run, and three pose-parameter divergences — RESOLVED

**Owner's observation:** "as long as they are moving they are running backwards according to the
animation, but if they sit still, they do properly stand".

The standing half is the useful part of that: activity selection works. `PlayerActivityState` picks
stand and run correctly and the sequence resolves per class — demo 94, medic 60, soldier 150,
scout 175 on cp_process. So this is not the activity, it is which cell of the run's blend grid the
pose parameters select.

**Three divergences are confirmed from `CMultiPlayerAnimState::ComputePoseParam_MoveYaw`, and none of
them explains the reversal**, so there is a fourth thing and it is not yet identified. Recorded
separately because each is independently a defect:

1. **The box push-out is missing.** Valve divides both components by `MAX(|x|,|y|)` — commented
   "push edges out to -1 to 1 box" — so a diagonal becomes a full-magnitude corner. Without it a
   player running at 45° reads 0.707 on each axis and the corner animations are never reached.
2. **The speed scaling is missing.** `if (flMaxSpeed > flSpeed) { x *= flSpeed/flMaxSpeed; ... }`,
   which is why a player easing along animates slower than one at full sprint. Ours are always full
   magnitude.
3. **The snap is applied unconditionally and should be conditional.** `SnapYawTo` runs only
   `if ( mp_slammoveyaw.GetBool() )`, and that cvar is declared
   `ConVar mp_slammoveyaw( "mp_slammoveyaw", "0", FCVAR_REPLICATED | FCVAR_DEVELOPMENTONLY, ... )` —
   default **off**, and development-only. This project's comment on the snap says it "is not a
   rounding convenience", which is true of what it does and wrong about whether TF2 uses it.

**What to measure next, and it has to be a measurement rather than another reading.** For a player
whose heading matches their body yaw — running dead forward — log `move_x`, `move_y`, the normalised
parameter, the blend cell chosen, and the authored movement of that cell from
`mstudioseqdesc_t.movementindex`. The authored movement is the ground truth: it says which way the
animation actually travels, so comparing it against the direction the player is moving settles
whether the inversion is in the parameter, in the normalisation, or in the grid's cell order.

Guessing was tried and produced three plausible candidates that all turned out to be real but
irrelevant, which is the signal to stop reading and start measuring.

### B102 — dead players were drawn, and a respawn animated as a 17-second jump — RESOLVED

**Owner's observation, which is what identified it:** "none of my RJs were 17 seconds, a 17 second
rocket jump isnt even really possible on a real non jump map lol". A movement recording made
deliberately to exercise jumps and crouches had a 17-second block of `AIR` in it, which had been
read as a rocket jump. It was a respawn.

**The engine does not animate death at all, and that is a fact about Valve's code rather than a gap
in ours.** `CMultiPlayerAnimState::HandleDying` exists and sets `ACT_DIESIMPLE`, but `m_bDying` can
only be set by `PLAYERANIMEVENT_DIE` — and that event is raised nowhere in the entire `game/` tree.
Its handler is `Assert( 0 ); // Should be here - not supporting this yet!`. The empty search was run
with `PLAYERANIMEVENT_JUMP` as a control, which returns real raise sites in `tf_player.cpp`, so the
zero is a fact about the code and not about the grep.

What happens instead is at the end of `CreateRagdollEntity`, `tf_player.cpp:15637`:

```cpp
// Turn off the player.
AddSolidFlags( FSOLID_NOT_SOLID );
AddEffects( EF_NODRAW | EF_NOSHADOW );
```

The corpse is a separate `CTFRagdoll` entity with physics. Turn ragdolls off in the game and the
player simply vanishes, after a single frame of the model in its reference pose — hands at the
sides, no sequence playing. That is the owner's description of TF2 and it is exactly what the code
above produces: the model is drawn for one frame with no activity, then not drawn at all.

**Three separate defects here, and only the first was visible.**

1. `DemoTimeline` gated players on `IsVisible`, which is about the PVS, rather than on `IsDrawn`,
   which also tests `EF_NODRAW`. Dead players kept being drawn. Harmless-looking until B100 began
   choosing an activity from `m_fFlags`: a corpse has `FL_ONGROUND` clear, so it was given
   `ACT_MP_JUMP_FLOAT` and fell through the air for the whole respawn.
2. The call site passed `alive: true` with a comment claiming "a dead player is drawn by its
   ragdoll rather than by an activity". Nothing here draws ragdolls and dead players **were**
   reaching that call, so the comment was false in both directions. A comment asserting the
   precondition that makes a hardcoded argument safe is worth no more than a check.
3. The marker pass would have inherited the bug in a cheaper primitive. Its rule is "a player with
   no model gets a dot", so removing the dead from the model pass alone turns every corpse into a
   marker gliding around the map behind whoever it is spectating.

**Measured, because "dead" and "not drawn" are not the same set.** On
`movement-test-stv-cp_process`, 535 dead player-ticks were drawn before the fix. Following
`EF_NODRAW` alone removed 322 of them and left 213 — and those 213 are a real engine behaviour
rather than a decode fault. `StateThinkDYING` puts the effect back:

```cpp
if ( !m_bAbortFreezeCam && m_hRagdoll &&
     (m_lifeState == LIFE_DYING || m_lifeState == LIFE_DEAD) && ... )
    RemoveEffects( EF_NODRAW | EF_NOSHADOW );	// still draw player body
```

**That exception is gated on `m_hRagdoll`.** The body is re-shown only once a corpse exists to
justify it, so with no ragdolls built (B58) the condition is false for every death this project can
render and the engine's own answer for our situation is that the effect stays on. So `Drawn` is
`IsDrawn && alive` today, and becomes `IsDrawn` alone when B58 lands. The `&& alive` is a
placeholder for a ragdoll, not a second opinion about death.

**The stand-in is gone, at the owner's instruction.** A dead player's entity follows whoever they
are spectating, so the timeline used to hold the last living position and yaw and report a body
roughly where it fell. That approximated a corpse the engine never draws, and it reported a
coordinate the demo does not contain. Dead players now report their real origin — which is the
spectated position, and is the truth — and are simply not drawn.

`PlayerActivity.Die` is retained because `HandleDying` is genuinely in Valve's code and this
reimplements that function, but it is unreachable in TF2 for the reason above and no viewer path
can select it.

**B101's answer, and none of the three divergences above was it.** The cause was found by measuring
each hop in turn rather than by reading further.

**The parameter was never wrong.** The POV half of a purpose-recorded pair carries the recorder's own
`CUserCmd`, so "was this player running forward" is answered by the input rather than reconstructed
from the output. Sampling the middle of every unbroken run of at least 60 ticks of `forwardmove 450`
with `sidemove 0` and `IN_FORWARD` held gives, at seven of nine samples, exactly:

```
tick  218 move_x 1.000 move_y -0.000
tick  640 move_x 1.000 move_y -0.000
tick  878 move_x 1.000 move_y -0.000
tick 1187 move_x 1.000 move_y  0.000
tick 1399 move_x 1.000 move_y  0.000
tick 4375 move_x 1.000 move_y  0.000
tick 4872 move_x 1.000 move_y  0.000
```

The two exceptions, ticks 5541 and 5681, are `-0.707, -0.707` and both fall inside a rocket jump,
where `forwardmove` and the direction of travel legitimately disagree because the player is airborne.

`Studio_LocalPoseParameter` was checked too and our port matches it, including the `groupsize > 2`
test that looked like an off-by-one and is Valve's own.

**The fault was the pose parameter LIST.** `scout.mdl` declares two parameters — `body_pitch` and
`body_yaw` — and nothing else. `move_x` and `move_y` live only in `scout_animations.mdl`, the model
it includes. A sequence's `paramindex` is local to the group that owns the sequence, so the run's
request for index 5 was served against a two-entry list, fell out of bounds, and returned cell zero
with a setting of zero on both axes. That is the grid corner at `move_x = −1, move_y = −1`: the
backward-left run, played by every moving player in every direction, forever.

Nothing could report it. Falling off the end of a list is a legitimate answer for a model that
genuinely has no such parameter, and cell zero is a real cell.

**The engine merges the lists**, in `CVirtualModel::AppendPoseParameters`
(`studio_virtualmodel.cpp:445`), and keeps a per-group map read back by
`CStudioHdr::GetSharedPoseParameter`. Three details are followed: matching is by name and
case-insensitive; a duplicate WIDENS the shared range across all four endpoints, which matters
because `body_pitch` is −45..45 in the base model and −45..90 in the animations; and the shared list
is in group order so the base model keeps its own indices.

**The translation is implemented but is not yet observable on any player model**, and that was
established by sabotage rather than assumed: replacing `masterPose[local]` with `local` leaves the
whole suite green, because a player model's parameters are a prefix of its animation model's and the
map comes out as the identity. It is kept because it is what the engine does and because a model
whose animations reorder a shared name would need it — Valve's own comment is that returning the
untranslated index "is just some random unrelated index". The merged list is what the corpus can
currently falsify.

The three divergences recorded above are still real and still unfixed; they change magnitudes and
diagonals, not direction. They are now the whole of what is left in B101's original list.

### B103 — two properties were looked for in the wrong send table, and both were silent — RESOLVED

**Found by asking why B100's crouching never worked**, then by writing the conformance test that
would have caught it. A qualified key is `Table.Property`, and a property name that is real in the
WRONG table matches nothing at all — while looking entirely correct.

**`m_fFlags` was looked for in `DT_LocalPlayerExclusive`.** It is declared in `DT_BasePlayer`
(`player.cpp:8183`), with no exclusivity and `SPROP_CHANGES_OFTEN`:

```cpp
IMPLEMENT_SERVERCLASS_ST( CBasePlayer, DT_BasePlayer )
    ...
    SendPropInt ( SENDINFO(m_fFlags), 0, SPROP_UNSIGNED|SPROP_CHANGES_OFTEN ),
```

So `Flags` answered null for **every player in every demo**, the activity state machine took its
"nothing said, assume on the ground" branch forever, and nobody has ever crouched or jumped in the
viewer — the owner's "everyone is still just running all the time". A trace of a POV demo carries
119 `DT_BasePlayer.m_fFlags` and not one occurrence of the name being searched for.

The comment beside the constant cited `player.cpp:8183` — the correct line — while stating the wrong
table and adding an invented consequence: "for the recorder alone in a POV one". A citation attached
to a guess reads exactly like a citation attached to a measurement. `LifeState()` in the same file
already read from `DT_BasePlayer` with a comment saying why, so two accessors on one entity
disagreed about where a player's own state lives. Both now share a constant.

**`m_flCycle` was looked for in `DT_BaseAnimating`.** It is in a sub-table
(`baseanimating.cpp:223`), and Valve's comment above it explains who receives it:

```cpp
// Sendtable for fields we don't want to send to clientside animating entities
BEGIN_SEND_TABLE_NOBASE( CBaseAnimating, DT_ServerAnimationData )
    SendPropFloat (SENDINFO(m_flCycle), ANIMATION_CYCLE_BITS, ...)
END_SEND_TABLE()
```

A door or a moving platform sends its cycle; a player never does, because `CTFPlayer` calls
`UseClientSideAnimation()` (`tf_player.cpp:949`) and the client advances it. Measured: 97
`DT_ServerAnimationData.m_flCycle`, zero `DT_BaseAnimating.m_flCycle`.

**Why the existing conformance test could not catch either.** `SendPropConformanceTests` checks that
each name appears in SOME send table anywhere in the SDK, which is deliberate — this project decodes
generically, so a name is legitimate if any class sends it. But it uses the table only in its error
message. `SendTableConformanceTests` now parses each `IMPLEMENT_SERVERCLASS_ST` /
`BEGIN_SEND_TABLE` block to its `END_SEND_TABLE()` and checks the PAIR. It found the `m_flCycle`
mismatch immediately, which is the whole argument for it: one of the two defects was found by
reasoning and the other by the instrument.

**The instrument needed a control and failed it first.** The initial scan found zero send tables, for
two independent reasons at once — `SourceSdk.Files` defaults to the top folder only and `src/game`
has no `.cpp` there, and it returns absolute paths while `SourceSdk.Text` takes one relative to the
checkout. Both produce the same empty result, and without the control asserting that the scan finds
`DT_BasePlayer.m_fFlags` the whole test would have reported a clean sweep of nothing.

Two fixtures asserted the old table and were changed rather than worked around: they were this
project pinning its own mistake.

**Three of B101's four divergences are now fixed, and a FOURTH was found while fixing them — the
sign was inverted.** The engine computes

```cpp
float flYaw = flAngle - m_PoseParameterData.m_flEstimateYaw;   // eye − travel
flYaw = AngleNormalize( -flYaw );                              // → travel − eye
```

and this project computed `eye − travel`. It is zero for a player running dead forward, which is
exactly the case the POV recording measured, so the measurement that settled the backward run could
not see it. What it breaks is left against right: a player strafing to their left played the
strafe-right animation and vice versa. Found only by reading the function again, term by term, while
implementing the other three — which is the argument for quoting a routine into a test rather than
paraphrasing it.

The snap is gone. `SnapYawTo` is real engine code but is called only under
`if ( mp_slammoveyaw.GetBool() )`, and that cvar is `"0"` and `FCVAR_DEVELOPMENTONLY`, so no shipped
client takes the branch. This project applied it unconditionally, with a comment arguing it stopped
the legs wavering as the differenced heading jitters — plausible, and not what TF2 does. A player
running 30° off their facing was animated as though at 45°.

The box push-out is in, guarded where Valve guards it.

**The speed scaling is still not implemented**, and it is the only piece left:

```cpp
float flMaxSpeed = GetBasePlayer()->GetSequenceGroundSpeed( GetBasePlayer()->GetSequence() );
if ( flMaxSpeed > flSpeed ) { vecCurrentMoveYaw.x *= flSpeed / flMaxSpeed; ... }
```

The path is now traced and nothing about it is unknown: `GetSequenceGroundSpeed` is
`GetSequenceMoveDist / SequenceDuration`; the distance is `Studio_SeqMovement` blending up to four
animations by the pose parameters; each one is `Studio_AnimPosition` walking `mstudiomovement_t`
records with `d = v0*f + 0.5*(v1-v0)*f²`. The records sit at `nummovements`/`movementindex`, offsets
20 and 24 of `mstudioanimdesc_t`, stride 44 — and this project already reads `fps` and `numframes`
from that struct.

What makes it a separate change rather than a fourth line here is **where it has to live**. The
scaling needs the authored ground speed of the sequence the player is playing, which is model data,
and `DemoTimeline` decodes a demo and has never opened a model. So the final scale has to move into
the viewer, after the sequence is chosen. Note also that the engine sets `move_x`/`move_y`, reads
the ground speed with those in place, and only then rescales and sets them again — the order is in
the source and is not incidental.

Until then a player easing along animates at a full-magnitude blend rather than being drawn back
towards the middle of the grid.

### B101's speed scaling — RESOLVED, and the ground speed matches TF2's class speeds exactly

The last piece of `ComputePoseParam_MoveYaw` is now implemented:

```cpp
float flMaxSpeed = GetBasePlayer()->GetSequenceGroundSpeed( GetBasePlayer()->GetSequence() );
if ( flMaxSpeed > flSpeed ) { vecCurrentMoveYaw.x *= flSpeed / flMaxSpeed; ... }
```

`StudioMotion` reads the `mstudiomovement_t` blocks an animation carries — `nummovements` and
`movementindex` at offsets 20 and 24 of `mstudioanimdesc_t`, stride 44 — and ports
`Studio_AnimPosition`, whose integral is `d = v0*f + 0.5*(v1-v0)*f²`. `Studio_SeqMovement` sums the
weighted VECTORS and takes the length of the sum, which is not the weighted mean of the lengths: two
animations travelling opposite ways at equal weight cancel to a standstill, and averaging would
report full pace.

**The duration term was already here and the division cancels.** `Studio_CPS` is
`Σ weight·fps/(numframes-1)`, `Studio_Duration` is its reciprocal, and `GetSequenceGroundSpeed` is
distance ÷ duration — so the whole thing is distance × cps, and `StudioAnimation.CyclesPerSecond`
was already exactly that per-animation term.

**The result validates against a constant from somewhere else entirely.** Run over each class's
forward run it gives 400, 240 and 230 for scout, soldier and heavy — precisely the `speed_max`
values the game loads from its own class scripts (`tf_classdata.cpp:152`). Nothing in the code was
given those numbers; they fall out of the movement records and the frame rate. An arithmetic slip
anywhere in the chain would land somewhere else.

**One term is untestable against any shipped model, and sabotage established that rather than
intuition.** Changing Valve's `0.5` coefficient leaves every corpus assertion green, because TF2's
run loops are authored at constant velocity — `v0` equals `v1`, so the acceleration term is
identically zero however it is scaled. Scaling `v0 * f` instead reddens four of them. A fixture
supplies the missing condition: an animation accelerating from rest to 200 units a second over one
second, where the integral gives 100, reading the end velocity flat would give 200 and dropping the
term would give 0. That fixture is the only test that fails when the coefficient is wrong.

**The scaling lives in the viewer, not in `DemoTimeline`**, because it needs the authored speed of
the sequence the player is playing and the scene layer has never opened a model. The two-pass shape
is Valve's: set the parameters, read the ground speed WITH THOSE IN PLACE — they choose which cells
are blended, so they choose whose authored speed is being asked about — then rescale and set again.
`flMaxSpeed > flSpeed` is a strict comparison, so a player moving faster than their animation was
authored for is left alone rather than scaled past the edge of the grid.

B101 is now closed in full.

### B104 — a solution-wide `dotnet test` once reported a TRUNCATED total — OPEN

Observed 2026-08-17 during the active-weapon work. One invocation of the merge gate reported

```
Passed!  - Failed: 0, Passed: 50, Skipped: 0, Total: 50, Duration: 1 s - Tf2DemoSalvage.Viewer3D.Tests.dll
```

against a suite of **350**, which takes about **80 seconds**. Nothing in the output said anything was
wrong: no failure, no warning, a green line. The immediately following run of the same command
reported 350, and running that project alone reported 350.

**This is the exact hazard the standing rule names — "Passed!" is not the result, the COUNT is.** Had
the missing 300 contained a regression from that change, the gate would have reported success.

Not diagnosed, and recorded rather than explained away. The shape suggests a race between the
parallel build writing the test assembly and the runner discovering it — in the truncated run
Viewer3D executed unusually early in the ordering and finished in a second, which is about what
discovery alone would cost. It did not reproduce on demand, so it is a one-in-many event rather than
a deterministic fault.

What to do about it until it is understood: **compare each assembly's total against its known size
every time**, rather than reading the pass/fail word. Current sizes at this commit:

| Assembly | Total |
|---|---|
| Audio.Tests | 16 |
| Core.Tests | 1034 |
| Cli.Tests | 63 |
| Content.Tests | 429 |
| Corpus.Tests | 138 (gcor-only; more with lcor) |
| Viewer3D.Tests | 350 |
| Viewer3D.UiTests | 8 |

A total that drops without the suite shrinking is a failed run wearing a pass.

### B105 — the weapon's activity suffix is computed but not yet wired to the renderer — OPEN

The chain from a demo to a weapon's animation role is complete and tested, and the last hop into the
viewer is not done. Recorded so the half-built state is visible rather than looking finished.

**What works.** `m_hActiveWeapon` gives the weapon entity, whose server class the timeline resolves.
`WeaponScriptName` turns that class into the entity class the script is named for — a rule plus ten
enumerated exceptions, checked against every `LINK_ENTITY_TO_CLASS` pair the SDK declares, 96 of
them. `WeaponRoles` then finds `scripts/<name>.ctx`, decrypts it with the key Valve publishes in
`tf_shareddefs.cpp:1616`, and reads `WeaponType` — the same key `tf_weapon_parse.cpp:134` reads.
Measured against the installed game: a medigun and a pistol are secondaries, a bonesaw and a bat are
melee, a rocket launcher and a minigun are primaries.

**What is missing, and it is one step.** Nothing calls `WeaponRoles.Suffix` yet.
`PlayerActivityState.NameOf` already takes the slot as a parameter and already defaults it to
`PRIMARY`, so the wiring is: carry the suffix on `ScenePose` beside `Flags`, build a `WeaponRoles`
once per demo from the weapon classes it mentions, and pass it through `PlayerAnimation.For`. Until
that happens every player still animates as though holding a primary — the suffix is computed and
discarded, which is the same shape as the three no-ops recorded in `CLAUDE.md`.

**Two known gaps in the role itself**, both real and both silent:

- **Per-class weapon translation.** `pszWpnEntTranslationList` (`tf_shareddefs.cpp:1628`) rewrites a
  base weapon entity into a per-class one, and the role can differ between them:
  `tf_weapon_shotgun` becomes `_soldier`, `_hwg` or `_pyro`, all secondaries, but `_primary` for the
  engineer, whose shotgun is his primary. One server class, several scripts. The holder's class is
  on the wire, so this is implementable; without it a soldier's shotgun reads primary.
- **The econ `anim_slot` override.** `GetActivityWeaponRole` prefers
  `CEconItemView::GetAnimationSlot` over the script when an item defines one, from `items_game.txt`
  keyed by `m_iItemDefinitionIndex` — which IS networked and does appear in the corpus. Reading it
  needs the econ schema, whose entries resolve through prefabs.

A weapon whose script cannot be found falls back to `PRIMARY`, which is the engine's own default —
`ActivityList` gives `TF_WPN_TYPE_PRIMARY` the same body as `default:`. That makes a miss
indistinguishable from a correct primary, which is why the name mapping is enumerated against the
SDK rather than trusted to a rule.

### B105 — RESOLVED for the wiring; the per-class translation remains

The suffix now reaches the renderer. `ScenePose` carries `Slot` beside `Flags`, the viewer builds a
`WeaponRoles` from the weapon classes a recording mentions, and `PlayerAnimation.For` passes it to
`PlayerActivityState.NameOf`. Measured on a real match, autoplayed so players actually exist:

```
weapon roles: CTFBat=MELEE, CTFBonesaw=MELEE, CTFCompoundBow=ITEM2, CTFCrossbow=PRIMARY,
CTFMinigun=PRIMARY, CTFPipebombLauncher=SECONDARY, CTFPistol=SECONDARY, CTFScatterGun=PRIMARY,
CTFWeaponBuilder=BUILDING, CTFWeaponPDA_Engineer_Build=PDA, CWeaponMedigun=SECONDARY
```

The Huntsman as `ITEM2` and the Crusader's Crossbow as a medic's `PRIMARY` are both the game's own
answers, and neither was written down anywhere in this project.

**The first attempt was a no-op and the tests could not see it.** The roles were built beside the
timeline, which is where the weapon classes become known — and the archives are opened AFTER that,
so `_archives` was null every time, every suffix came back null, and the lookup fell back to the
primary forms. The viewer drew exactly what it had drawn before. Every unit test passed throughout,
because they call `WeaponRoles` directly rather than through the viewer.

It was found by a line missing from the log. That is the fourth no-op of this shape recorded in this
project, and the only instrument that has ever caught one is output from a real run.

Two tests now guard the wiring rather than the components: one asserts a medic resolves a DIFFERENT
sequence holding a secondary than holding a primary — which fails if the suffix is computed and
discarded — and one asserts the medigun is what produces `SECONDARY`. Split because a broken script
read and a broken lookup are different defects. Verified by sabotage: making `For` ignore its slot
reddens the first and leaves the second green.

**Still open, and now measured rather than predicted.** `CTFShotgun=PRIMARY` appears in that log.
`pszWpnEntTranslationList` (`tf_shareddefs.cpp:1628`) translates a base weapon entity per class, and
the shotgun is the case where the role differs: `_soldier`, `_hwg` and `_pyro` are secondaries while
the engineer's `_primary` is a primary. So a soldier's shotgun currently animates as a primary. The
holder's class is on the wire, so this is implementable.

The econ `anim_slot` override from `items_game.txt` is also still unread.

### B106 — a scout is reported holding an engineer's shotgun — OPEN

Seen while verifying the per-class weapon translation on
`demostf-cp_process_f12-2026-08-08-2207`. The viewer logs each weapon with the class holding it:

```
CTFShotgun/1=PRIMARY          CTFShotgun/2=PRIMARY        CTFShotgun/9=PRIMARY
CTFShotgun_Revenge/1=PRIMARY  CTFShotgun_Revenge/9=PRIMARY
CTFPistol_Scout/1=SECONDARY   CWeaponMedigun/5=SECONDARY
```

Class 1 is Scout and 2 is Sniper. **Neither can equip `tf_weapon_shotgun`**, and
`CTFShotgun_Revenge` is the Frontier Justice, which is engineer-only — so `CTFShotgun_Revenge/1` is
a scout holding a weapon no scout can carry.

Some pairs in the same line are clearly right: `CWeaponMedigun/5` is a medic with a medigun and
`CTFPistol_Scout/1` is a scout with the scout's pistol. So the pairing is not uniformly wrong, which
rules out the simplest explanations.

**Two candidates, and neither is confirmed.** Either `m_iPlayerClass` is being read for the wrong
player — it comes from the resource entity's arrays keyed by entity index, `m_iPlayerClass.%03d`, so
an off-by-one in the slot would attribute a neighbour's class — or `m_hActiveWeapon` resolves to an
entity that is not that player's weapon. The handle goes through the same `Slot` decode as every
other, invalid-tested before masking, so the second is the less likely of the two.

**What this does and does not affect.** The per-class translation itself is measured against the
game's own scripts and is right: a soldier's shotgun reads SECONDARY and an engineer's PRIMARY,
verified by sabotage. What is in doubt is the CLASS handed to it, which decides which of those two
answers a given player gets. A wrong class quietly picks the wrong script and the result is a
plausible suffix, so this is the silent kind again.

Worth measuring first: whether the same demo's class-to-model assignment agrees with the
class-to-weapon one, since the models are drawn from the same `m_iPlayerClass` and look correct on
screen. If the models are right and the weapons are wrong, the fault is in the weapon handle rather
than in the class.

### B106 — RESOLVED: it is nine player-ticks in 929,371, and the SET presentation hid that

Counted rather than listed, on the same demo:

```
CTFShotgun_Revenge  class 9 (engineer)  x2118     class 1 (scout)  x6
CTFShotgun          class 9             x215      class 1 x2   class 2 x1
CWeaponMedigun      class 5 (medic)     x157233
total player-ticks with both: 929371
```

The impossible pairs are **nine ticks in nine hundred thousand**. That is not a misattribution; it is
the two sources not being sampled atomically. `m_iPlayerClass` comes from the resource entity's
arrays and the weapon from the player entity, and a class change lands on one before the other — so
for a tick or two a player reads as their old class holding their new weapon. The engine sees the
same skew; it simply never draws a conclusion from it.

Both hypotheses raised when this was filed were wrong, and checking them was still worth it. Entity
slot reuse is handled — `Delete` removes the entity and an `Enter` with a different serial replaces
it — so a recycled slot cannot carry a stale class name. And the weapon handle is decoded through
the same `Slot` as every other, invalid-tested before masking.

**The real defect was in how the finding was presented.** The viewer logs the pairs as a SET, which
gives one tick and a hundred and fifty thousand exactly the same weight, and that is what made a
rounding error look like a decode fault. A set answers "did this ever happen"; the question worth
asking was "how often". Same family as *measure the output, not the capability* — the instrument
reported a true thing that meant nothing like what it appeared to mean.

No code change follows. The per-class translation reads the class it is given, that class is right
for 999,991 ticks in a million, and a suffix wrong for one tick is not visible at sixty frames a
second.

### B107 — a jump plays its push-off before its float — RESOLVED

Every airborne player played `ACT_MP_JUMP_FLOAT` for the whole jump. TF2 splits it:

```cpp
if ( gpGlobals->curtime - m_flJumpStartTime > 0.5 )
    idealActivity = ACT_MP_JUMP_FLOAT;
else
    idealActivity = ACT_MP_JUMP_START;
```

Both are real animations in every class model, so the launch was simply never drawn.

**The clock is derived, and that is the interesting part.** The engine sets `m_flJumpStartTime` when
the jump EVENT arrives, and a demo carries no such event — the same reason the whole activity state
machine exists here. So `DemoTimeline` watches `FL_ONGROUND` clear and records the tick, converting
to seconds with the recording's own interval. Null while the interval is still unknown, because a
zero interval would make every jump read as its own first instant for ever.

**`ACT_MP_JUMP_LAND` is deliberately not implemented, and that is a fact about the engine.** It is
started with `RestartGesture( GESTURE_SLOT_JUMP, ACT_MP_JUMP_LAND )` — a layered gesture played over
whatever the body is doing, not a body activity. Returning it from the activity lookup would replace
the run a player lands into. Gestures are a separate mechanism this project does not have.

**Also read and deliberately skipped: `m_bDontDoNewJump`.** `HandleJumping` checks it before
choosing the phases and falls back to the single old `ACT_MP_JUMP`; it comes from a class script,
every shipped class has it false, and the comment beside it reads "Remove me once all classes are
doing the new jump". Reproducing it would be reproducing a finished migration.

Three assertions guard it, at three levels: the threshold is strict on both sides (half a second is
still the push-off), the corpus test requires readings on both sides of it plus a minimum of exactly
zero — a clock started from the demo's beginning would report hundreds of seconds — and a viewer
test requires the two phases to resolve to DIFFERENT sequences on a real medic model, which fails if
the clock is computed and discarded.

Still open from `HandleJumping`: **airwalk**, which supersedes the jump when `vecVelocity.z > 300`
and the player is not ducking, and `ACT_MP_FALLING_STOMP` beneath it. Both need vertical velocity,
which is derivable from the track, and airwalk additionally needs `m_bDontDoAirwalk` from the class
script — which this project already decrypts and reads for the player model.

### B108 — a rocket-jumping player tucks instead of air-walking — RESOLVED

`CTFPlayerAnimState::HandleJumping` checks the air-walk BEFORE the jump and it supersedes it:

```cpp
bool bValidAirWalkClass = ( pData && pData->m_bDontDoAirwalk == false );
if ( bValidAirWalkClass && ( vecVelocity.z > 300.0f || m_bInAirWalk ) && !bInDuck )
```

So a fast-rising player runs in the air rather than tucking, and every rocket jump in this viewer
was drawn with the wrong animation.

**Only the medic opts out**, measured from the shipped class scripts rather than guessed —
`DontDoAirwalk` (`tf_classdata.cpp:187`) is set for class 5 and no other. The plausible guesses were
the heavy or the soldier and both are wrong, which is the whole argument for reading the data.

**The threshold separates an ordinary jump from a blast jump by design.** A TF2 jump leaves the
ground at 268 units a second and the test is strictly above 300, so plain jumping never air-walks.
The corpus test asserts both halves on one recording — the rocket jump air-walks and the ordinary
jumps do not — because either alone is consistent with a latch that never fires or never clears.

**The latch is the engine's, not a convenience.** `vecVelocity.z > 300.0f || m_bInAirWalk` means the
air-walk continues once started, so a rocket jump does not flicker back to the jump animation as the
rise slows. `DemoTimeline` reproduces it by latching on the first fast-rising tick and clearing when
the ground flag returns.

**Vertical speed is differenced from position, which is what the client does too.** The animation
state reads `GetOuterAbsVelocity`, and on the client that is `EstimateAbsVelocity` — an estimate
from position history rather than a networked velocity. So the derivation here is the engine's own
method rather than a substitute for one.

**The two halves are resolved in different layers and meet on the pose.** `DemoTimeline` knows the
rise and cannot open a class script; the viewer can. Neither could answer alone.

Not implemented from the same branch: **`ACT_MP_FALLING_STOMP`**, which replaces the air-walk when
`m_flFallVelocity > PLAYER_MAX_SAFE_FALL_SPEED` and `CanFallStomp()` — the Mantreads. It needs a
fall-velocity accumulator and an item check, and it is one animation for one item.

### B109 — nobody aimed: body_pitch was never set — RESOLVED for pitch, OPEN for yaw

`ComputePoseParam_AimPitch` is one line (`multiplayer_animstate.cpp:1689`):

```cpp
float flAimPitch = m_flEyePitch;
GetBasePlayer()->SetPoseParameter( pStudioHdr, m_PoseParameterData.m_iAimPitch, -flAimPitch );
```

and `m_iAimPitch` is `LookupPoseParameter( pStudioHdr, "body_pitch" )` (`:1421`). That parameter sat
at zero for every player in every demo, so nobody ever looked anywhere but level — most visible on a
sniper, and on anyone tracking a player above them.

**The negation lives at the binding rather than in the stored value**, so what the scene carries
still matches what the wire said. A stored value that is already negated reads as a bug every time
someone compares it against a trace.

**Kept apart from the pose's own `Pitch`, which stays zero for a player.** That field rotates the
whole model; `tf_player.cpp:2689` feeds pitch to the animation state to aim the torso, not to tip
the body, and assigning it there lays a looking-up player on their back.

**A test for that separation was written twice and removed both times**, and the gap is real. Players
are not props in the timeline — `PropsAt` returns entity props and a player becomes one only in the
viewer — so there is no artefact at this level carrying both a player and a model rotation. The
first attempt asserted that only brush entities carry a pitch and was falsified by
`comp_win_banner_scaled.mdl` at 14.9 degrees, a prop the mapper genuinely tilted; the second found no
player models at all. Covering it needs the viewer's pose construction extracted from `MainForm`.

**Still open: `body_yaw`.** `ComputePoseParam_AimYaw` sets it from the eye yaw minus the FEET yaw,
and the feet are a state machine of their own — `m_flGoalFeetYaw`/`m_flCurrentFeetYaw`, which match
the eyes while moving and turn in place with limits while standing. This project currently uses the
eye yaw as the body yaw outright, which is right while moving and wrong while turning on the spot.
That is B61.

### B61 — the feet-yaw state machine — RESOLVED

**A player's body is drawn at their FEET yaw, and the torso twists to make up the difference.**
`ComputePoseParam_AimYaw` (`multiplayer_animstate.cpp:1702`) ends with

```cpp
m_angRender[YAW] = m_flCurrentFeetYaw;
float flAimYaw = m_flEyeYaw - m_flCurrentFeetYaw;
GetBasePlayer()->SetPoseParameter( pStudioHdr, m_PoseParameterData.m_iAimYaw, -flAimYaw );
```

This project used the eye yaw as the body yaw outright, which is right whenever a player is moving —
"The feet match the eye direction when moving" — and wrong whenever they turn on the spot, where the
feet should stay planted and the waist should twist.

`FeetYaw` is that machine, and it is stateful because the engine's is: the feet lag and catch up
over several ticks. Four numbers, all from the source rather than chosen — 45 degrees of twist
before the feet step, 720 degrees a second, a 60-degree fade, and a movement threshold of one unit a
second on the THREE-dimensional velocity rather than the horizontal speed the activity choice uses.

**Two quirks are reproduced deliberately and both would be lost by tidying.**

`ConvergeYawAngles` takes the magnitude BEFORE normalising the angle and the sign after:

```cpp
float flDeltaYaw = flGoalYaw - flCurrentYaw;
float flDeltaYawAbs = fabs( flDeltaYaw );
flDeltaYaw = AngleNormalize( flDeltaYaw );
```

Turning from 170 to −170 is twenty degrees the short way, but the raw difference is 340 — so the
fade saturates at full rate rather than easing through, while the direction still comes from the
normalised −20 and turns the short way. Normalising first eases instead, and the test pins the
resulting −179.1.

The feet also step round in whole 45-degree jumps rather than tracking the eyes, under a comment
where Valve marks the branch unfinished in place. Reproduced as written.

**The approach is asymptotic, which caught a wrong prediction of mine.** The fade scales the rate by
`delta / 60`, so each tick covers about a fifth of what remains and the gap decays geometrically
until the one per cent floor turns it linear. A test asserting the feet reach the eyes within twenty
ticks failed at 88.11 degrees — correct behaviour, wrong expectation. It takes about forty.

**One coupling had to be preserved rather than discovered later.** `ComputePoseParam_MoveYaw` reads
the EYE yaw, so the movement blend must not start reading the feet now that the drawn yaw is the
feet. `ScenePose.EyeYaw` carries it, and the move parameters take `EyeYaw ?? Yaw` — null for
anything that is not a player, where the entity's own rotation is the only yaw there is. Without
that, B101's fix would have regressed silently the moment this landed.

### B104 — the guard against a truncated run existed and was too loose to fire — RESOLVED

The truncated total recorded above was never diagnosed, and looking for the cause turned up
something worse: **the check that should have caught it was already in the repository and would have
passed it.**

`build/assert-test-count.sh` reads the `.trx` counters and fails below a floor, exactly for this. The
floors had not been raised as the suite grew:

| Assembly | Real count | Floor |
|---|---|---|
| Viewer | 352 | **34** |
| Core | 1034 | 744 |
| Corpus | 138 | 99 |

The run that reported 50 of Viewer's 350 tests would have satisfied a floor of 34 without complaint.
A floor is only a guard while it is close to the number it guards, and these had drifted an order of
magnitude. Now 340, 1000 and 130.

**The local gate was worse than CI, because it had no check at all.** Every "green at full count" in
this project's history was a person reading six console lines and comparing them against remembered
numbers. `build/gate.sh` replaces that: one project at a time, `.trx` per project, floor asserted for
each. Running whole projects sequentially also removes the assembly-level concurrency that is the
leading suspect for the truncation itself — a solution-wide `dotnet test` runs test assemblies in
parallel, and a single run writes one `.trx` per project all under the same name, so the counts
cannot be told apart afterwards.

**A second way two runs differ, found while checking the first.** `--filter` changes which tests
EXIST rather than which ones run: NUnit's adapter includes `[Explicit]` tests when no filter is given
and drops them as soon as any filter is present. Content.Tests reports 441 unfiltered and 436 with
`--filter 'FullyQualifiedName!~UiTests'`, the five being diagnostic probes. That filter is the one
`CLAUDE.md` documents for the merge gate, so every `[Explicit]` test in this repository has been
silently absent from it.

The original truncation is still not explained, and the honest position is that it now cannot hide:
the floors would fail it and the per-project run removes its most likely cause.

### B110 — nobody swam: waistDeep was hardcoded false — RESOLVED

`PlayerAnimation.For` passed `waistDeep: false` to the activity state machine, so the swimming
branch of `CalcMainActivity` was unreachable in every recording ever opened. A stub rather than a
decision, and the kind the owner is right to dislike: the state machine handled swimming correctly
and nothing could reach it.

`m_nWaterLevel` is on the wire — `SendPropInt( SENDINFO( m_nWaterLevel ), 2, SPROP_UNSIGNED )` at
`tf_player.cpp:792`, two bits for four levels, which Valve documents in a comment at
`player.cpp:1961`: 0 not in water, 1 feet, 2 waist, 3 eyes.

**Sent on `DT_TFPlayer` rather than a local-player table**, deliberately, with a note saying why:
"This will create a race condition will the local player, but the data will be the same so.....".
`DT_BasePlayer` carries its own copy for the local player alone, so reading that one would have
worked for a POV recorder and nobody else — the same shape as B103, and avoided this time by
checking the enclosing table before writing the constant. `SendTableConformanceTests` confirms the
pair.

The threshold is `>= WL_Waist`, tested identically by `HandleJumping` (which cancels a jump the
moment the water reaches the waist) and `HandleSwimming`. Feet-deep water is not swimming, which the
test pins: a player wading a shallow puddle keeps running.

### B111 — there is no per-class playback rate, and this project said there was — RESOLVED (retraction)

`PlayerAnimation.cs` carried a note that `m_flMaxGroundSpeed` drives the main sequence's playback
RATE, so a heavy's run should cycle slower than a scout's, and that this was unimplemented. **That was
read from the wrong class.** `CBasePlayerAnimState::ComputePlaybackRate` does scale the rate by max
ground speed — and TF2 does not inherit from it. `CTFPlayerAnimState` derives from
`CMultiPlayerAnimState`, which is standalone (`multiplayer_animstate.h:168`), and the only
`SetPlaybackRate` in that class is `SetPlaybackRate( 1.0f )` for the local player
(`multiplayer_animstate.cpp:1366`). Its `m_flMaxGroundSpeed` is maintained by `UpdateInterpolators`
and returned by `GetMaxGroundSpeed()`, which nothing in the TF2 hierarchy reads for the main sequence.

What TF2 does with speed is the pose-parameter scaling in `ComputePoseParam_MoveYaw` —
`x *= flSpeed / flMaxSpeed` against the sequence's authored ground speed — and that IS implemented
(B101). The rate is left at the authored value because that is what the engine does. Implementing
the "missing" scaling would have been a divergence dressed as a fix.

Retracted rather than quietly deleted, because a wrong claim recorded without the reasoning that
killed it is the kind that gets confidently repeated.

### B112 — the gesture layer: jump-land, attacks, reloads, flinches — OPEN

`ComputeSequences` runs `ComputeMainSequence` and then `ComputeGestureSequence`. This project has
the first and none of the second, so no player fires, reloads, lands or flinches — the body plays
its main activity and nothing else.

Traced end to end in the SDK, and it is a subsystem rather than a gap:

- **Trigger:** `CTEPlayerAnimEvent`, a temp entity (`tf_player.cpp:340`) sent through
  `svc_TempEntities` as `DT_TEPlayerAnimEvent` — `m_hPlayer`, `m_iEvent`
  (`Q_log2( PLAYERANIMEVENT_COUNT ) + 1` bits, unsigned), `m_nData` (`ANIMATION_SEQUENCE_BITS`).
  `EntityDecoder.DecodeTempEntities` already decodes the message; the event class is not yet
  interpreted.
- **Dispatch:** `DoAnimationEvent` maps `PLAYERANIMEVENT_ATTACK_PRIMARY`, `_RELOAD`, `_JUMP` and the
  rest onto seven slots — `GESTURE_SLOT_ATTACK_AND_RELOAD`, `_GRENADE`, `_JUMP`, `_SWIM`, `_FLINCH`,
  `_VCD`, `_CUSTOM`.
- **Playback:** `RestartGesture` translates the activity through `TranslateActivity` (so it takes the
  weapon suffix, as the main sequence does), and `AddToGestureSlot` puts it on an anim layer with its
  own cycle, auto-killed at the end. A gesture already playing in the slot is run to `m_flCycle =
  1.0` first.
- **Blend:** the layer is composed over the main pose per bone. `PropModels` blends only within one
  sequence's grid today; a layer over a sequence is new.

The reload rate has weapon attributes folded in (`mult_reload_time`, `fast_reload`,
`multiplayer_animstate.cpp:198`), which needs the econ item — the same dependency the `anim_slot`
override has.

**Measured rather than assumed: every gesture checked is additive, not interpolated.** `jumpland_*`
and `a_flinch01` on both `scout_animations.mdl` and `soldier_animations.mdl` all carry flags `0x14`
— `STUDIO_POST | STUDIO_DELTA` — confirmed with a probe rather than guessed. That matters because
`SlerpBones` (`bone_setup.cpp:1373`) takes an entirely different branch for `STUDIO_DELTA`:

```cpp
if (seqdesc.flags & STUDIO_DELTA)
{
    // adds to the base pose per bone, not slerp blends toward the gesture's own pose
}
```

and the per-bone strength for either branch is not the layer weight alone — it is

```cpp
pS2[i] = s * seqdesc.weight( i );	// blend in based on this bone's weight
```

`seqdesc.weight(i)` comes from `mstudioseqdesc_t.weightlistindex` → `pBoneweight(i)`, one float per
bone, authored per sequence. So a landing gesture is not "play a second sequence and mix" — it is a
genuinely different pose-composition primitive (additive delta, per-bone weighted) layered on top of
the one this project has (interpolated, uniform weight, one sequence).

**This changes B112 from a wire-up into three subsystems, and it is being recorded as a checkpoint
rather than built on a guess:**

1. ~~A per-bone weight list reader~~ — **done.** `StudioGestureWeights.ForSequence` reads
   `weightlistindex`/`pBoneweight`. Measured rather than assumed: `jumpland_primary` and an ordinary
   run sequence share the identical shared default table at the same absolute address, while
   `r_handposes`/`r_armposes` in the same file carry genuinely restricted 0/1 patterns — proof the
   offset resolves correctly rather than a guess about gesture authoring.
2. ~~Additive delta composition~~ — **done.** `StudioPoseBlend.Layer` ports `SlerpBones`'s
   `STUDIO_DELTA` branch: `strength = layerWeight * seqdesc.weight(i)`, then `QuaternionMA`
   (base ⊗ scaled-delta) for `STUDIO_POST` or `QuaternionSM` (scaled-delta ⊗ base) otherwise, with
   `pos += delta * strength`. `QuaternionScale` is the sin/asin partial-rotation form from
   `mathlib_base.cpp:1757`, not a component multiply. A bone a delta track never mentions defaults
   to IDENTITY, not the rest pose — confirmed in the runtime's own decode at `bone_setup.cpp:599`,
   which is what makes an untouched bone a genuine no-op rather than a drag toward the bind pose.
   Predictions computed by hand from Valve's own quaternion formulas — the sign-flipped Z component
   between POST and non-POST is the discriminator — and verified by sabotage: swapping the multiply
   order reddens exactly the two order-dependent tests and no others.
3. Gesture lifecycle. Split in two along the axis that actually matters here — whether the piece is
   era-fragile:
   - **3a — cycle progression + auto-kill — done.** `GestureLayer` (Core.Scene) ports the
     `CLIENT_DLL` branch of `UpdateGestureLayer` (`multiplayer_animstate.cpp:1275`): `cycle =
     elapsed/duration`, and once that passes one strictly (`> 1.0f`) either the gesture is gone
     (`m_bAutoKill`) or it holds on its last frame (`m_flCycle = 1.0`). Closed form is exact, not an
     approximation: on the standard `AddToGestureSlot` path every rate factor is constant —
     `m_flPlaybackRate = 1.0`, `GetGesturePlaybackRate = 1.0`, `GetSequenceCycleRate = Studio_CPS =
     1/duration` (`bone_setup.cpp:5532`) — so the per-frame integration sums to the closed form, and
     the client's own `frametime`s (which the closed form avoids needing) are not recorded in the
     demo anyway. That same path fixes the layer weight at `1.0` with zero blend in/out, which is why
     `GestureLayer` carries no weight or fade — the per-bone shaping is the weight list from slice 1,
     not a layer envelope. The auto-kill discriminator (`null` vs held `1.0`) and the strict `> 1.0f`
     boundary were both verified by sabotage: each reddens exactly its own test.
   - **3b — the trigger and the slot map — the mapping is now built; the wiring is not.**
     `CTEPlayerAnimEvent` already decodes off `svc_TempEntities` generically (`m_hPlayer`,
     `m_iEvent`, `m_nData` are in the decoded property set). The event→slot+activity mapping —
     `DoAnimationEvent` read as a pure function — is done: `PlayerGestureEvent.Map` (Core.Scene)
     ports the full `CTFPlayerAnimState::DoAnimationEvent` over the base, total across all 41
     events, with `PlayerAnimEvent`/`GestureSlot` enums, a `GestureContext` for the duck/swim/
     airwalk/loser/minigun/sniper variant bits, and a `GestureTrigger` result. Events that drive the
     MAIN sequence or clear state (jump, swim, die, spawn, snap-yaw, the custom-sequence pair) return
     null rather than a gesture — those are `PlayerActivityState`'s job — as do the events dead in
     the SDK (grenade draw/throw have no handler; `CustomGestureSequence` and `DoubleJumpCrouch` are
     commented out, and `z1800`'s 19 `DoubleJumpCrouch` events correctly draw nothing). The
     precedence subtlety is tested with a duck+swim input (reload picks duck first, attacks let swim
     override) and both it and the pre-fire auto-kill rule were sabotage-verified.

     **What remains of 3b is the wiring**, in two pieces: (i) decode the `CTEPlayerAnimEvent` stream
     into `(player, event, nData)` carrying the persistent-instance state forward — an absent
     `m_iEvent` means "same event as the last one", not zero; and (ii) feed each trigger a
     `GestureLayer` (slice 3a) started at the event's tick and composite it (slice 2) in the viewer,
     tracking one live gesture per slot per player. The remaining era-sensitive corner is narrow:
     the two dynamic events (`CustomGesture`, `VoiceCommandGesture`) carry an activity ORDINAL in
     `m_nData`, and the activity list itself grows across eras — so resolving that number to a name
     needs the era's `ActivityList`, unlike the fixed-activity events which are already era-clean.

     The rest of this note (the enum history) records why the fixed mapping is safe across eras. **The era-fragility this note used to claim was a real mechanism that did not happen
     here, and checking three SDK generations is what settled it** (`docs/findings/25-gesture-layer.md`):
     `PlayerAnimEvent_t` is strictly append-only — ordinals 0–29 are identical from the Orange Box
     era (`hl2sdk/orangebox`) through 2013 and the current `hl2sdk/tf2`, with `DOUBLEJUMP_CROUCH`(30)
     onward only ever appended. So one mapping table decodes every protocol; the only era effect is
     range, which the narrower field self-enforces. Every observed corpus value maps under that
     single enum. Two implementation notes for whoever builds it: a temp entity sends only what
     differs from the previous instance, so an absent `m_iEvent` means "same event as the last one"
     and the decode must carry that state forward (not default to zero — the sentinel trap); and
     `m_nData` is the activity/sequence for the handful of events that use it (voice-command and
     custom gestures), absent otherwise.

3b remains on the owner's direction. 3a was the era-clean half and is landed and tested; 3b is now
de-risked — it can be built from one SDK mapping rather than needing per-era measurement, and the
2007/2008 client decompile (owner-offered) would confirm the launch-era row rather than being needed
to discover it.

### B113 — `WriteAngle` wrote every negative angle as zero — FIXED

`svc_FixAngle` carries three 16-bit angles, and the encoder was

```csharp
writer.Write((uint)MathF.Round(degrees * (65536f / 360f)) & 0xFFFF, 16);
```

**`(uint)` applied to a negative float does not wrap — .NET saturates it to zero.** So a pitch of
−30° (a player looking up, entirely ordinary) was written as `0`, and the message reproduced a
player looking dead level. The mask that looks like it handles the wrap never sees a negative value
to wrap, because the conversion has already clamped it.

Valve's own encoder gets this right by going through a SIGNED integer, where the mask does the
two's-complement wrap — `bf_write::WriteBitAngle`, `tier1/bitbuf.cpp:551`:

```cpp
d = (int)( (fAngle / 360.0) * shift );
d &= mask;
```

The fix is one cast, `(uint)(int)`, which reproduces that: −90° now encodes as 49152 and decodes as
270°, the same direction by a different representative.

**Why the corpus round trip could never have found it, and this is the interesting half.** That
suite re-encodes messages decoded from real demos and reports 100 % of payload bits reproduced —
which was true and remains true. `ReadAngle` is `raw * 360f / 65536f`, so a demo-sourced angle is
**always in 0..360 and never negative**; the value handed back to the writer therefore never enters
the broken branch. The defect is unreachable from any recording, by construction. It is reachable
from a caller that builds a message in code — a synthetic test, or the text-to-demo compiler that
is Phase 1's last item, which would have written flattened view angles into every demo it produced.

Found by `NetMessageWriterTests`, written to give the writer coverage that does not depend on the
corpus (see `docs/MEASUREMENT-PLAN.md`). It is the first finding from that work and it is exactly
the shape the plan predicted: real data carries ordinary values, and ordinary values agree with a
broken encoder. Verified by sabotage — restoring the single `(uint)` cast reddens both negative-angle
tests and nothing else.

### B114 — a libopus abort that was the fuzz harness, not the decoder — CLOSED, NOT A DEFECT

The voice fuzz target's first full run took the test host down:

```
Fatal (internal) error in D:\a\libopus\libopus\opus-src\src\opus_decoder.c, line 865:
assertion failed: ret==packet_frame_size
```

That is `abort()` rather than a return code, so it cannot be caught, and the obvious reading was
the alarming one: a malformed voice frame — bytes chosen by whoever supplies the `.dem` — kills the
process. It was written up that way, and a packet-validation guard was added to
`OpusVoiceDecoder.Decode` as the fix.

**That reading was wrong, and the thing that settled it was isolating the variable rather than
accepting the first coherent story.** Two facts did not fit: the fuzz property tests passed 12/12
on their own, and only the FULL audio suite crashed. The difference is NUnit's parallel fixtures —
and `VoiceFuzzTarget` held one decoder per codec in a plain `static` field, shared across every
thread. Opus, CELT and Speex are all stateful and none is thread-safe, so two fixtures decoding
concurrently corrupt one decoder's state, and the assertion is libopus noticing.

Measured both ways, which is the only reason this is settled rather than argued:

| Harness | Guard | Result |
|---|---|---|
| shared `static` decoders | absent | **abort** |
| `[ThreadStatic]` decoders | absent | 28 tests pass |
| `[ThreadStatic]` decoders | present | 28 tests pass |

So the abort is fully explained by the harness, and **nothing here shows `opus_decode` mishandling
malformed input.** The fix is `[ThreadStatic]` on the target's decoders.

The guard was kept, with its comment corrected to say what it is. It is precaution rather than a
repair: inspecting a packet before decoding is what libopus's inspection entry points are for, and
the oversize check is a genuine buffer-safety property regardless. It is explicitly not evidence of
a decoder bug.

**Worth keeping because the failure mode is instructive.** A native abort with a real library's
assertion text in it is extremely convincing, and it pointed at the component under test rather
than at the instrument pointing at it — the same shape as
`docs/memory/instrument-bugs-outnumber-decoder-bugs.md`, which records five measurements that were
wrong before any reader was. The tell was there immediately: the crash needed the whole suite and
the targeted run was clean. A defect in the decoder would not care which other tests were running.

### B115 — nine material flags compared against the string "1" — FIXED

`VmtMaterial` read nine boolean parameters as `Value(key) is "1"`:

```
$alphatest  $translucent  $vertexalpha  $additive  $nocull
$halflambert  $mod2x  $ssbump  $selfillum
```

**The engine does not do that, and the SDK says so in a way that needs no decompiler.** These are
declared `SHADER_PARAM_TYPE_INTEGER` — `SHADER_PARAM( SSBUMP, SHADER_PARAM_TYPE_INTEGER, "0", ... )`
— and the flag-valued ones become `MATERIAL_VAR_*` bits set from an integer read. Nothing in the
material system compares a parameter against the characters `'1'`.

So `"$translucent" "2"` draws translucent in TF2 and drew **opaque** here, and `"$nocull" " 1"` with
a leading space was ignored entirely.

**Why it survived: it agrees with the engine on every material Valve ships.** Valve's own VMTs write
`1`, so the corpus and the game install both look correct. But a custom map's materials go through
the same reader, and "Valve always writes 1" is a fact about Valve rather than about the input this
code is handed — the same shape as every other place this project has assumed its inputs are
well-formed because the ones in front of it were.

Fixed with a `Flag` helper that parses the leading integer and treats non-zero as true, which is
`atoi`-shaped like the engine's own read. Pinned by
`ShaderParameterDefaultConformanceTests.ABooleanParameterIsAnIntegerAndAnyNonZeroIsTrue`, with zero
and a non-numeric value as controls so it cannot pass by calling everything true. Verified by
sabotage: restoring the string comparison on `$additive` alone reddens it.

**Found by auditing which implemented shader parameters had SEMANTIC conformance rather than merely
being claimed in `MaterialCensus`.** `SdkCoverageTests` counts what is missing; only a test that
compares behaviour against the engine catches what is present and wrong. Eight of the twenty-one
implemented parameters had no such test, which is what the audit was for.

### B116 — every static prop drew skin family 0 — FIXED

`StaticPropLump_t.m_Skin` says which skin family a placed model draws with. `BspStaticProps` read
origin, angles and prop type and stopped, so the field was never decoded and every static prop in
every map drew its FIRST family whatever the map asked for.

**Measured before and after, because "the field parses" is not the claim worth making.** A decode of
a member no map ever sets would be a decode of zeroes and would look identical to not reading it:

```
cp_process_final: 267 of 1631 placements name a skin family other than 0
```

So 267 props per map were drawing the wrong variant. That is not an error and never produced a
warning — a model showing its red variant where the map asked for blue reads as the map's own art.

**The offset is 32, and the reason is padding rather than arithmetic.** `m_PropType`, `m_FirstLeaf`
and `m_LeafCount` are three `unsigned short` ending at 30, then `m_Solid` takes one byte, and the
next member is an `int` the compiler aligns to four — so byte 31 is padding.
`StaticPropConformanceTests` derives this independently from the declaration, so the constant is
checked rather than asserted.

**Applied per placement rather than per model, which is the whole difficulty.** A model is loaded
once and placed many times with different families at different placements, so the swap cannot be
baked at load. Vertices were already being copied per placement, so remapping the material index on
the way past costs a dictionary lookup and no extra geometry. The swap tables themselves already
existed — `StudioSkins` has read them since the player team-colour work — so this was the wire
between two finished halves, which is what made it the cheapest fix on the board.

**Found by the conformance audit, and the specification was written before the code.**
`UnimplementedRenderingConformanceTests.AStaticPropCarriesItsOwnSkinIndex` skipped with the citation
and an `Assert.Fail` beneath it, so finishing the work could not leave a test quietly passing on
nothing — it forced the placeholder to be replaced with a real assertion. The suite's skip count
went 7 to 6.

### B117 — every alpha-tested edge was cut at half — FIXED

The world shader clipped at a constant:

```hlsl
if (bump.w > 0.5f) { clip(albedo.a - 0.5f); }
```

`bump.w` carried a 1-or-0 flag saying "this surface is alpha tested", and the cutoff was hardcoded.
`$alphatestreference` was not implemented at all.

**What the engine does** (`BaseVSShader.cpp:925`): alpha testing is enabled from
`MATERIAL_VAR_ALPHATEST`, and the reference is overridden **only when the material states one above
zero** —

```cpp
s_pShaderShadow->EnableAlphaTest( IS_FLAG_SET(MATERIAL_VAR_ALPHATEST) );
if( alphaTestReferenceVar != -1 && params[alphaTestReferenceVar]->GetFloatValue() > 0.0f )
    s_pShaderShadow->AlphaFunc( SHADER_ALPHAFUNC_GEQUAL, params[alphaTestReferenceVar]->GetFloatValue() );
```

So a material asking for 0.9 keeps only its most opaque texels, and ours kept everything above half
— **every alpha-tested edge was too thick**, on exactly the surfaces that make a map read as a map:
foliage, grates, chain-link, ladders. The kind of defect that looks like bad art rather than a bug.

**Fixed by carrying the CUTOFF in the float that used to carry a flag**, which needed no new
plumbing: zero keeps its old meaning of "not alpha tested" and any other value is the threshold. That
maps exactly onto Valve's "only override when above zero" rule, so a material naming no reference
still gets the previous behaviour and nothing regresses.

Two details worth keeping:

- **An absent reference is zero, and zero is not a cutoff.** The declaration's default is empty or
  `"0.0"` depending on the shader, and the guard is `> 0`. Reading a missing key as "clip at zero"
  keeps every texel and turns a grate into a solid sheet — the inverse of the bug being fixed.
- **The declared default is spelled differently by different shaders and means the same thing.**
  `lightmappedgeneric_dx9` and `vertexlitgeneric_dx9` write `"0.0"`; `depthwrite` writes `""`. The
  conformance test asserts the MEANING for that reason — its first draft pinned the empty string and
  failed against correct code.

The one number still interpolated is the DEFAULT of 0.5: Valve leaves the shader API's own reference
alone when the material names none, and the shader API is closed. Everything else here is read from
published source. Recorded on the constant itself.

Specified before it was built, in `UnimplementedRenderingConformanceTests`; the rendering suite's
skip count went 6 to 5.

---

### B118 — `docs/DECISIONS.md` numbered nine decisions twice — FIXED

**D20 through D28 each named two different decisions**, and both were cited in live comments.

| Number | kept — the `###` series | renumbered — the `##` series |
|---|---|---|
| D20 | the protocol boundary list comes from Valve | **D34** one renderer, two camera modes, Direct3D 11 |
| D21 | the era boundaries stay open | **D35** geometry is world space; the camera owns the view |
| D22 | the trace reaches the command line | **D36** surf and jump runs set the accuracy bar |
| D23 | corpus work is cached per process | **D37** models are lit the way the engine lights them |
| D24 | a faster suite recalibrated the mutation tool | **D38** the suite runs on synthetic demos |
| D25 | the test project splits pure/stateful | **D39** test names are `{Subject}_{Scenario}_{Expected}` |
| D26 | CI mutation and fuzzing schedules | **D40** no scripted edits to source files |
| D27 | entity baselines | **D41** the measurement check names this project |
| D28 | user messages are named, not decoded | **D42** the viewmodel lookup answers the main hand |

**The `###` series kept its numbers** because it is contiguous D1–D33 and carries the older
citations. The later series moved to D34–D42 in file order.

**A later session restarted numbering at D20 without reading the file first**, and the two series
interleave rather than sit apart — `## D20` is at line 1246, between `### D31` and `### D32`. The
heading level is the only thing that tells them apart, which is invisible in a citation.

**This is confirmed to be ambiguous in practice, not just in principle.** Both series are cited from
source comments, sometimes for the same number:

```
D20 can be trusted to cover: it lists what the *engine* branches on     -> ### D20
D20's choice of thin Direct3D bindings over an                          -> ## D20
```

**Why it matters more here than a duplicate number usually would:** the decisions log exists so the
reasoning behind a choice survives next to the choice. The owner's standing instruction is explicit
that this has to be defensible months later, when the conversation is gone. A citation that resolves
to two different decisions defeats exactly that, and it degrades quietly — nothing fails, the reader
just reads the wrong entry.

#### The fix, applied 2026-08-20

**Every citation in the repository was classified by reading what it says**, which is why this was
not a substitution: the same token meant different things in adjacent files. **Seventeen moved** —
eleven in code, tests and memory across seven files, six in this document — and the rest already
pointed at the surviving series.

- **All eleven `D21` citations meant the camera** — `MapWorld`, `TopDownCamera`, `MainForm`,
  `CameraMatrixTests` and four passages in this file — so they became D35.
- **All seven `D25` citations meant the mutation split**, not the test-name convention, so none of
  them moved. The number they wanted was already correct.
- **`D20` split down the middle.** `OldProtocolTests`, `SPEC.md` and two entries here mean the
  protocol boundary list; `NativeOpus.cs` means the Direct3D binding choice and became D34. Both
  readings were live in the repository at once, which is the concrete proof this was ambiguous in
  practice and not only in principle.

**Two judgement calls, recorded because they are the parts a later reader could reasonably dispute:**

1. **`### D23 addendum — whether the recorded camera should share the interpolation delay`** was
   renumbered to D37, the lighting decision, on the strength of its own sentence "that is where a
   client draws" — the same match-the-engine principle D37 states. It is not a lighting question, so
   this is inference rather than a citation. The alternative reading is that it was never attached to
   any decision.
2. **One citation was simply wrong and was corrected, not renumbered.** B22 pointed at "`DECISIONS.md`
   D24 and the baseline research" for entity baselines; entity baselines are D27, and neither D24
   was ever about them. Found only because the collision forced every citation to be read.

**Guard, now in place.** `build/assert-decision-numbers.sh`, run first by `build/gate.sh`. It fails
on a number used twice and on a gap in the sequence, and — the part that matters — **it fails when
its own pattern matches nothing**, since a check that silently matches zero headings is the same
class of defect it exists to catch. Verified by sabotage in all three directions before being
trusted: a duplicated D28 names both lines, a D99 reports the gap, and a changed heading style
reports the stale pattern.

`docs/DECISIONS.md` also gained an index and an explicit "the next number is D43", because the
original mistake was reasonable: the file is in write order, not number order, and D32 and D33 sit
between D34 and D35. Scrolling to the end to find the highest number gives the wrong answer.

---

### B119 — the spy's knife is drawn at the camera, not in the hand — FIXED

**Confirmed by looking**, 2026-08-20, `z1800` entity 11 tick 47601 in first person. The arms and the
watch are both correct; the right hand is empty, fingers curled around nothing.

**It is not missing geometry, which is what the log suggests at first reading.** `c_knife` is packed,
uploaded as the 78th model, and the poser reports `asked for 3, produced 3; skipped 0 no-batches`. It
produces an instance every frame and is listed in the viewmodel pass beside the arms and the watch.

The tell is the line before it draws:

```
animating c_knife.mdl: sequence 34 cycle 0 -> baked frame 0 of 1 blend 0 yaw -117.54 at (-232,-1896,72)
```

It is posed **at the camera origin with the camera's yaw** — its own rest pose, not the arms'. A
knife is 14 units long (`extents ... z from -3.8 to 10.5`) against a viewmodel near plane of 1, so at
the eye it straddles the near plane and clips away entirely.

**The likely mechanism, not yet proven.** `EntityModels` merges a child onto its wearer and
deliberately keeps the child's own matrix for any bone the parent does not have:

> Bones the parent does not have keep the child's own … an item with a part the player has no bone
> for keeps the shape the artist gave it rather than collapsing to the origin.

That fallback is right for a cosmetic and wrong here. If `c_knife`'s bone names do not match
`c_spy_arms`, every bone takes the fallback, the model keeps its rest skeleton, and `transform`
becomes the wearer's — which for a viewmodel is the camera. **The same shape as B82**, where a
spellbook whose only bone is `mvm` matched nothing and sat at the player's feet.

No `remap group` line appears for the knife, where one appears for models that do merge. That is
consistent with the above and does not prove it: the merge may be running and matching nothing.

**Two ways this differs from what was working.** The sniper rifle on the same code path reported
`bone-merged … root permuted at (26.4,-9.6,-8.7)` and appeared correctly, so either the spy's arms
differ from the sniper's or the knife differs from the rifle. And the knife is on the BAKED path
(`1 baked frames`) while `c_spy_arms` is `posed on the GPU` — worth checking whether merging is
applied on one path and not the other, since that would explain a per-model split with no per-model
cause.

**First step is an instrument, not a fix.** `Merge` must report how many of the child's bones matched
the parent, because zero-matched and all-matched currently produce the same silence — and zero is the
hypothesis. Everything after that depends on the number.

**Found only by looking**, and the log actively misled: reading it alone gave "packed, uploaded,
merged, instanced, drawn", and the flat-colour capture — where a blade against a near-white wall
would be unmissable — showed nothing in the hand. `docs/memory/output-level-assertion-or-it-is-not-done.md`
again, one level further out: even an output-level count can agree while the picture is wrong.

#### Resolution, 2026-08-20 — two causes, and the instrument was already there

**The instrument the section above asks for existed the whole time.** `EntityModels.Merge` already
logged `N of M bones matched`, with the matched and missing names. The reason no such line appeared
for the knife is that `Merge` never reached it — the first guard returns early when the model has no
skeleton, and says nothing.

**Cause one: the knife was baked, so it had no skeleton to merge.** `WornModelPaths()` decides
`mustSkin`, and it only walks `timeline.Props`. The first-person weapon is not a prop track — the
client creates it (`econ_entity.cpp:1153`) so no demo carries it, and `AddViewmodel` builds it ad hoc.
It therefore never entered the worn set, was loaded unskinned, and baking pre-transforms the vertices
by one pose and discards the bone indices. `Merge` then returns the model's own matrices while the
caller has already set the transform to the WEARER's — the camera. A fourteen-unit knife against a
near plane of one clips away entirely.

**This is the same defect as the cosmetics-at-ankle-height one that `mustSkin` was created for**, and
the remarks on `WornModelPaths` describe it exactly. The knife simply took a route into the renderer
that bypasses the set the flag is built from. Fixed by adding `HeldWeaponModels(timeline)` to that
set, which already existed and already fed the LOAD set.

After it: `skinning c_knife.mdl: 5,808 corners against a budget of 371,712, so it is posed on the GPU`
and `bone merge c_knife.mdl onto c_spy_arms.mdl: 5 of 6 bones matched; matched weapon_bone,
vm_weapon_bone, vm_weapon_bone_1..3; missing c_weapon_stattrack`. The only miss is a StatTrak counter
bone, which correctly keeps its own matrix.

**Cause two: the viewer was overriding the recorded animation.** With the knife visible it sat at the
bottom of the frame, which the owner caught: "the knifes there its jkust super low". `AddViewmodel`
substituted the model's `VM_IDLE` for the demo's sequence whenever they differed — on this spy,
replacing the recorded 34 with 3 on every frame, posing the arms for a weapon they were not holding.

The owner's rule, and it is the correct one: **"we shouldnt be forcing any sequence only stuff from
the demo or how valve does it"**. The engine agrees — `C_BaseViewModel` plays `m_nSequence` as it
arrives and nothing in the viewmodel path picks an idle. Substitution removed from both hands; the
knife moved into the grip.

**The log was reporting the recorded sequence while drawing the substituted one**, so `seq 34`
appeared in every line while 3 was on screen. It now prints all three — recorded, what `VM_IDLE`
would have been, and what is actually played. That line would have shown this a fortnight ago.

**One worry raised and then killed by measurement.** A recorded sequence indexes the weapon's own
table, while ours is merged from two models and 98 entries deep, so the two could disagree silently
and produce a plausible wrong animation. They agree: `demo says 34, VM_IDLE would be 3, playing 34`
poses a correct spy knife. Worth re-checking on a model whose merge has a different shape.

**The regression test, and the seam it needed.** The rule lived in a private method on a `Form`, so
asserting it meant opening a window, so it was never asserted — that missing seam is what let this
ship. It is now `WornModels.From(props, heldWeapons)`, a pure function, with
`WornModelsTests` covering it: the held weapon is worn (the regression), an attached studio track is
worn, an unattached track is NOT (the control that stops "return everything" passing), a brush entity
is not, and the set is case-insensitive and rejects empty paths.

**Verified by manipulation rather than by being green**, since it was written after the fix. Deleting
the `heldWeapons` loop — B119 exactly — reddened precisely two tests: the regression itself and the
case-insensitivity one, which also feeds only weapons. The other five stayed green, so the failure is
specific to the weapons path and not a blanket break. Restored with the inverse edit.

One deliberate widening: `Tf2DemoSalvage.Core` now grants `InternalsVisibleTo` to
`Tf2DemoSalvage.Viewer3D.Tests`. `ScenePropTrack.AttachedTo` is written only by the timeline and so
has an internal setter, which meant the viewer's own suite could not construct a worn track at all.

**What made the fix checkable was determinism.** Two identical launches produce byte-identical
captures (`352EBD85…` twice), so a frame hash is a valid regression instrument for this viewer: after
the merge fix `B2192859…`, after the sequence fix `08C14B3E…`. Each change was proved to have done
something before the picture was even looked at.

---

### B120 — every model in the scene is lit at about a tenth — DUPLICATE OF B95

**Filed as new and it is not: this is B95, "local lights are still not applied", measured from the
other end.** B95 says no prop receives direct light from a point or spot light, because the renderer
applies the ambient cube and the sun and nothing else. The room in this capture has three ceiling
lamps. The world's brushes carry them in baked lightmaps and look correctly lit; the models cannot
receive them at all, so they sit at ambient-only — which is the ~0.1 below.

Kept rather than deleted because the numbers are new and belong to B95: they turn "no prop receives
direct light" from a statement about the code into a measured consequence, and they give whoever
implements it a before-figure to check against.

**Recording the mistake too.** The risks log was searched for this symptom and not for its cause —
`LocalLights.cs` cites B95 in a comment three lines long, and reading that first would have skipped
the entire filing. Same shape as B118's duplicate decision numbers, one document over: a register
only works if it is read before it is written to.

Noticed by the owner as "the gloves look dark too but im not sure if thats lighting ot what", on the
same capture. The gloves are a red herring — a spy's gloves are genuinely black — but the instinct
was right and the scope is much larger than the viewmodel.

Every model in that frame, sampled at its own position:

```
lbtf_medal_participant_demo  0.0934      c_spy_arms      0.1111
c_proto_backpack             0.0946      c_knife         0.1112
homefront_blindfold          0.1037      spr17_upgrade   0.1114
fob_e_sniperrifle            0.1050      ghost_aspect    0.1191
v_watch_leather_spy          0.1086      c_engineer_arms 0.1192
```

**A range of 0.09 to 0.12 across twenty unrelated models**, while the world brushes in the same shot
— walls, floor, a lit doorway — read as correctly lit. Models and world are lit by different paths
here, and only one of them looks right.

**Not yet established: whether 0.1 is wrong.** The scene is a dim wooden interior and an ambient
sample of 0.11 may be honest; the map's brightness comes from lightmaps carrying direct light, which
the ambient cube would not. The measurement says models agree with each other, not that they agree
with the engine. What would settle it is the same view in TF2, or the ambient cube for that leaf
computed independently.

Related: the same run warns `*27 is lit by nothing at (-416,-1862,130); its leaf carries no ambient
light, so it draws black`, so at least some leaves in this map genuinely carry none.

Also in that run and unrelated to either: nine `.vhv` prop-lighting files fail their checksum against
the model they belong to (`sp_224`, `sp_287`, `sp_290`, `sp_294`, `sp_306`, `sp_463`, `sp_465`,
`sp_474`, and one more), so those props fall back to unlit.

---

### B121 — the SDK sweep handed out its cache, and callers mutated it — FIXED

**Found as flake and it was not flake.** `SendProps_Moveparent_IsARealSendPropUnderAnAlias` failed
inside a full gate run, passed in isolation, and passed on a re-run. This project's rule is that flake
is a defect in the code or in the synchronisation, never noise, so it was chased rather than retried.

The failure named its own cause. `ShouldContain("moveparent")` failed while the message printed
`"moveparent"` among the actual values — a collection being mutated while it was read.

**`SourceSdk.Names` cached its sweep and returned the cached `HashSet` itself.**
`SendPropConformanceTests.SentProperties` then calls `UnionWith` on that result to fold in the
aliased `SENDINFO_NAME` names. Two separate faults follow:

- **A race.** NUnit runs these in parallel, so one test's `UnionWith` overlapped another's `Contains`
  on an unsynchronised `HashSet`. Intermittent by nature, which is why it survived so long.
- **Cache poisoning, which is worse.** The additions were written into the entry cached for the FIRST
  pattern, so any later caller sweeping that pattern received names it never asked for. `Names`' own
  remarks say this exact thing must not happen — "two callers sweeping the same directory for
  different things must not share an answer. That would be a wrong result rather than a slow one" —
  and the mutation defeated it by a route the keying could not guard.

The consequence of the second is the quiet kind: a conformance suite's denominator silently widens,
so it stops reporting properties it should report. Nothing fails; the suite just goes blind in a
direction nobody chose.

**Fixed by returning a copy.** Affordable by the cache's own measurements, which record its benefit as
unmeasurable — 553 ms against 532–648 ms — so correctness here costs nothing worth counting.

**`SourceSdkCacheTests` pins both halves.** The race cannot be reproduced on demand; the pollution can,
and it is the more dangerous half, so that is what is asserted: mutating a result must not reach the
next call, and two calls must not be the same instance. Verified by sabotage — returning the cached
set directly reddens both.

**The general shape, worth carrying:** a cache that hands out a mutable reference is not a cache, it
is shared state. Every consumer of one is trusted not to write to it, and one of six was not.

---

### B122 — a spotlight's cosine term is missing from the falloff — FIXED

**Fixed 2026-08-20**, `Cone` now returns `dot2 * fringe`. Measured on the same frame:

```
(415,1891,57)    direct 1.5385  ->  0.9944
(-632,-1988,64)  direct 0.2924  ->  0.1908
(632,2031,64)    direct 0.2678  ->  0.1305
```

About a third off, which is a cosine averaging roughly 0.65 over those points. The lamp still
outweighs the bounce where it is overhead, so this trims rather than undoes B95.

**`SpotlightFalloffConformanceTests` pins it, and needed an OFF-AXIS condition to do so.** The
existing `ASpotlightInsideItsInnerCone_IsAtFullStrength` sits exactly on the axis, where `dot2` is one
and multiplying by it changes nothing — which is how the wrong behaviour stayed green. Two controls
guard the fix in both directions: the on-axis spotlight, unchanged; and an off-axis POINT light, which
must keep its full falloff because `emit_point` has no cosine term. Without the second, a fix applying
the cosine to every light kind would have passed.

#### As originally filed

vrad computes a spotlight's falloff as the inverse of the attenuation polynomial **multiplied by the
cosine between the light's direction and the direction to the lit point**, and only then applies the
penumbra fringe (`lightmap.cpp:1929`-1942):

```cpp
out.m_flFalloff = ReciprocalSIMD( out.m_flFalloff );
out.m_flFalloff = MulSIMD( out.m_flFalloff, dot2 );      // <- this
// outside the inner cone
mult = ... ( dot2 - stopdot2 ) / ( stopdot - stopdot2 ) ...
```

`LocalLights.Cone` returns the fringe alone and never multiplies by `dot2`, so a light is at full
strength anywhere inside its inner cone rather than falling off toward the cone's edge.

**Worth about 1.75x at the angle measured** — the spotlight 127 units above the spy of `z1800` has a
cone dot of 0.57 — so it is a real error and a small one next to the 255 that B95 turned out to be.
Held back deliberately so that fix could be measured on its own.

**Note it is the same `dot2` twice, for two purposes**, which is what makes it easy to read as already
handled: once as the cone mask and fringe, and once as a plain cosine on the falloff. Our code
computes `dot2` and uses it for the first only.

Point lights are unaffected — `emit_point` has no such term (`lightmap.cpp:1885`-1895).

---

### B123 — a static prop whose baked lighting is refused gets no lighting at all — FIXED

**Fixed, and it does NOT explain the capture point that prompted it.** That correction is the first
thing here because the investigation was started by a symptom this turned out not to cause.

`cp_badlands` reports `ASKED FOR 1232 placements across 145 models; HAVE baked lighting for 1186`, so
**46 placements take this path and the capture point is not one of them** — its baked lighting is
present and valid. The dark disc is B55: `cap_point_base`'s materials are `VertexLitGeneric` with
`$envmap env_cubemap`, `LUMP_CUBEMAPS` is not read, and B83 predicted this outcome in as many words —
"the disc's base texture is a dark or mid grey metal, and essentially all of its apparent brightness
in game is the reflection".

**The count was available before the capture was taken**, and taking it first would have shown that
this fix could not move that pixel. Measuring what a change can reach is cheaper than looking at what
it did.

The fix itself stands on its own evidence and is kept.

**Noticed by the owner in a capture:** "we have dark CPs on POV demos still while i thought we fixed
that on STV demos". Badlands mid at tick 2500 draws its capture point as a dark disc.

**It is not B83 coming back.** That was `cap_point_base.mdl`'s skin, then the ambient cube's
nearest-sample, and both are fixed. This is a different path.

**Static props are lit only by their baked `.vhv` vertex colours.** `MainForm.LightAt` — the sampler
that carries the ambient cube, the sun and, since B95, the local lights — is consumed by
`_models.Instances` and nothing else, which is the ENTITY path: props the demo describes, and the
viewmodel. Map props never reach it.

So two things follow, and the second is the visible one:

- **The B95 fix does not reach static props.** A lamp overhead brightens a player and leaves the
  crate beside them alone.
- **A refused `.vhv` leaves a prop with no lighting whatsoever.** `PropModels.Lighting` returns
  `Refused` and the prop draws with WHITE vertex colours — full-brightness albedo, flat. On a
  light-coloured model that reads as washed out; on dark metal like a capture point base it reads as
  a dark disc, which is exactly the report.

**Refusals are common, not exceptional: 44 on `cp_badlands`.** The checksum guard is right — vertex
lighting baked against a different build of the model would light the wrong parts of it — and the
offsets were checked against Valve's own structures while investigating this: `studiohdr_t.checksum`
at 8, `HardwareVerts::FileHeader_t.m_nChecksum` at 4. Both correct, so the mismatches are genuine.
TF2 has updated models since these maps were compiled.

**What the engine does instead — confirmed in the SDK, which is what the fix follows.**
`DrawModelInfo_t` (`istudiorender.h:207`) carries both sources at once:

```cpp
ColorMeshInfo_t *m_pColorMeshes;
bool            m_bStaticLighting;
Vector          m_vecAmbientCube[6];
LightDesc_t     m_LocalLightDescs[4];
```

`m_bStaticLighting` selects between them, so a model with no colour meshes is lit exactly as a
dynamic one is. `c_physicsprop.cpp:85` shows a single model switching modes — it asks for
`STUDIO_STATIC_LIGHTING` only while asleep and is cube-lit while awake — so "nothing baked" has never
meant "unlit" in Source.

`PropModels.Load` now takes the light sampler and uses it when nothing was baked, evaluated with each
vertex's own NORMAL so the prop is lit rather than tinted flat. Sampled once per placement, matching
the engine, which gives a whole model one cube. A prop with valid baked lighting keeps it: that is
higher quality than a cube, which is why the compiler wrote it.

Possible only because of the load order — `_leaves` and `_ambient` are read at `MainForm:1051` and
`MapAssets.Load` runs at 1067 — so the sampler exists before any prop is built.

**Do not start from the checksum.** The temptation is to make the guard more permissive so the baked
data is used anyway; that would light props with colours belonging to a different mesh, which is a
convincing wrong answer rather than a dark one.

Related: B95 for the direct term this path never receives, and B83 for the two earlier and unrelated
causes of a dark capture point.

## B124 — a model's `env_cubemap` was discarded, so no prop ever reflected — CLOSED 2026-08-20

**Closed by D44.** Measured on cp_process_final: **29 of 413 materials ask for the literal
`env_cubemap`** and every one of them resolved to null, including all three capture-point materials.
43 placements are baked into the map and all 43 now decode.

**This was the other half of B55, and B55 closed without it.** That entry says, correctly, that "the
expensive-looking half did not exist" — vbsp assigns each brush face's cubemap at compile time, so
the world path resolves a name and never searches. What it did not say is that the assignment
*cannot* reach a model: `Cubemap_CreateTexInfo` works on texinfo and a model has none, so a prop's
material arrives still carrying the literal. The reader's own comment said so —

> a static prop's material still asking for the literal `env_cubemap` (which the engine binds at
> runtime by proximity and this does not do yet)

— and it was written as a parenthesis inside a docstring about something else, next to code that
returned null for exactly that case. **The knowledge was in the file; the risk was not in this
document.** Same shape as B83 below, and as `docs/memory/measure-the-output-not-the-capability.md`.

**Two defects, not one, and the second was invisible.** `DrawModel` bound four texture slots of five
and never bound the cube at all, so the world pass's last cubemap stayed in the slot. That was inert
only because no model material ever resolved one — the moment `env_cubemap` started resolving, it
would have drawn one prop reflecting another prop's cube. Found by writing the offscreen test, not
by reading.

**A third, latent and worse, surfaced on the way.** `DrawModel` bound no shaders, input layout,
topology or samplers either, relying on `Draw` having run first in the same frame. `Draw` returns
early when the map has no geometry, so **a frame with no map loaded would issue a model draw with no
vertex shader bound** — which does not fail, it removes the device, and reports later as
`"The GPU device instance has been suspended"` from whatever reads back next. Both paths now call
`BindPipeline`.

**Verified by manipulation, twice, one change at a time**: forcing the search to ignore the model's
position reddens the placement test, and never binding the cube reddens both model tests while
leaving the world and matte controls green.

Instruments: `ReflectionRender_AModelsReflection_FollowsItsNormal` and
`ReflectionRender_AModelAtTwoPlacements_TakesTheNearerCubemap` draw through the real pipeline
offscreen and read a pixel; `CubemapLoading_TheMapsOwnPlacements_AreDecodedAndPlaced` checks the
placements against the lump; `CubemapLoading_AModelMaterial_CarriesALocalReflectionAndNoCubemap`
names the capture point.

**Not verified: whether it looks right.** A pixel that changes with the placement says the right cube
is bound, not that the picture is correct. That is a question for someone looking at the screen.

## B125 — every reflection was attenuated by a Fresnel term Valve turns off — CLOSED 2026-08-20

**Found by the owner looking at the screen after B124 was declared fixed: "its still not fixed".**
The capture point was still dark, and every assertion about it was still green.

**The defect.** This renderer applied raw Schlick to the reflection:

```hlsl
float fresnel = pow(saturate(1.0f - dot(surfaceNormal, eyeDirection)), 5.0f);
specular *= fresnel;
```

Valve computes the same fifth power and then **discards most of it**
(`lightmappedgeneric_ps2_3_x.h:532`):

```cpp
fresnel = fresnel * g_OneMinusFresnelReflection + g_FresnelReflection;
```

The pair is packed from one material parameter as `[ 0, 0, 1-R(0), R(0) ]`
(`lightmappedgeneric_dx9_helper.cpp:728`), so the term is `schlick * (1 - R) + R` for
R = `$fresnelreflection` — **which defaults to 1**, collapsing it to a constant. The parameter's own
description says which end is which: `"1.0 == mirror, 0.0 == water"`. The fast path hardcodes the
same defaults as `static const HALF g_FresnelReflection = 1.0f`.

**And `VertexLitGeneric` has no Fresnel term at all.** Its whole envmap block
(`vertexlit_and_unlit_generic_ps2x.fxc:456`) is cube, mask, tint, contrast, saturation, then
`result = diffuseComponent + specularLighting`. No eye dot product anywhere in it. So a model
reflects at full strength whatever its VMT says. (`$envmapfresnel` exists on
`vertexlitgeneric_dx9.cpp:61` and belongs to the **Skin** shader, which `VertexLitGeneric` routes to
under `$phong 1`; its default is 0, the opposite convention, same answer.)

**Why every instrument missed it, and this is the part worth keeping.** `ReflectionRenderTests`
measures that a reflection *changes with the normal*, which is exactly what raw Schlick does — most
strongly of all. The defect made the test's own signal LARGER. Two more tests measured which cube
was bound, which was correct throughout. Nothing in the suite asked how much reflection a surface
keeps head-on, because that quantity was never predicted from the source.

Measured, before and after, offscreen through the real pipeline:

| Surface | Before | After |
|---|---|---|
| brush material 95, head-on | (69, 68, 69) | (156, 149, 147) |
| model material 391, head-on | (13, 3, 1) | (66, 47, 34) |

A flat capture-point disc seen from standing height is the worst case for this: the normal points at
the eye, `1 - dot` is near zero, and its fifth power is a few percent. **Almost black**, which is
what B83 has said since it was opened.

**A second parity error found while reading the same block.** The mask was applied LAST, after
contrast had squared the value. Both of Valve's shaders apply it first — `specularLighting *=
specularFactor` before the tint — and squaring is not linear, so the order is part of the
specification. Fixed with it.

Covered by `Envmap_TheFresnelTerm_IsOffUnlessTheMaterialAsksForIt` and
`Envmap_AModelsReflection_HasNoFresnelTermAtAll`, both written before the fix and both quoting the
source. The first also asserts the remap arithmetic at R = 0, 0.5 and 1.

**The lesson is the project's own rule, applied to a shader**: read the source before measuring your
own data. Three green measurements said the cube was bound, sampled and chosen by position — all
true, all irrelevant to how much of it survived to the screen.
