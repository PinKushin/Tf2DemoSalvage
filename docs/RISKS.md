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
