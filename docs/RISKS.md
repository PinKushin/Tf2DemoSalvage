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

## B3. Layer 2 message IDs are unmined — **OPEN**

We do not yet know the message-ID bit width at network protocol 24, nor the full
ID→type mapping. Everything above depends on it. No public authoritative source
found; `tf-demo-parser` is the reference.

**Mitigation:** this is the next research task, and it is bounded — one table.
Narrowed 2026-08-07: `protocol.h` and `netmessages.h` are **not** in source-sdk-2013, so
the SDK cannot help here. Prior art is the only route.

## B4. SendTable flattening is where silent wrongness lives — **CONCEPTUAL**

VDC documents `SPROP_CHANGES_OFTEN` as reordering the property list and
`SPROP_EXCLUDE` as removing inherited properties. Neither is cosmetic: the
flattened, sorted property list is what entity deltas index into. Get the ordering
wrong and the decoder reads real values into the wrong fields — it will not crash,
it will just be wrong.

**Mitigation:** this is precisely what the cross-parser differential test exists
for. Build it before entity decode, not after.

Substantially de-risked 2026-08-07 by reading `dt_common.h` from the SDK, which is
authoritative where VDC is prose: 17 `SPROP_` flags rather than the 8 VDC documents,
plus exact bit widths (`SPROP_NUMFLAGBITS` 17, `MAX_DATATABLE_PROPS` 4096,
`DT_MAX_STRING_BITS` 9, `MAX_ARRAY_ELEMENTS` 2048) and the `DPT_` type ids. The three
`SPROP_COORD_MP*` variants are absent from VDC entirely and are almost certainly what TF2
uses for player positions — a decoder built from VDC alone would decode every position
wrongly and never crash. See `SPEC.md`.

## B5. No public wire spec for entity decode — **UNDOCUMENTED**

Established during the spec consolidation: VDC describes entity networking
conceptually but publishes no bit layout for `svc_PacketEntities`, no delta-index
encoding, no property ordering rules. More reading will not produce one.

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

## B8. Cross-parser oracle needs a Rust toolchain — **decision pending**

`parse_demo` outputs a JSON *summary* (header, players, scoreboard), not per-tick
data. Useful as a first oracle; insufficient for entity-level diffing, which would
need a small harness over their `DemoTicker` API.

D2 bans Rust *from this codebase*. A test-only oracle is arguably outside that, but
it is the owner's call. Not needed until there is output worth diffing.

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

## B9. Memory pressure at corpus scale — **anticipated, not yet measured**

A 75 MB demo yields ~120,000 commands. `DemoCommandReader` yields
`ReadOnlyMemory` windows rather than copies specifically to avoid this, but entity
decode will materialise per-tick state and that is where allocation will actually
show up. D2's rule applies: profile before reaching for anything exotic.

## B10. Git LFS bandwidth on a public repo — **known tradeoff**

Per D11: LFS bandwidth is billed to the repository owner beyond 1 GB/month, while
ordinary clones are free. A popular public repo would want demos in release assets
instead. Volume problem, not a correctness one.
