# Tf2DemoSalvage

A standalone parser (and, eventually, viewer) for Team Fortress 2 `.dem` files — built to work on demos from any era of TF2's 18-year history, including ones Valve's own client updates have broken.

Independent, clean-room project. Not affiliated with Valve. Ships no Valve-authored game
assets — maps are resolved from your own TF2 install or a source you configure, not bundled
(see `docs/DECISIONS.md` D9).

## Status

**Phase 1 is substantially complete.** Every demo in the corpus decodes end to end across five
network protocols — 11, 14, 15, 16 and 24, spanning October 2007 to 2013 and the modern game —
and every message body those demos contain is decoded rather than stepped over.

| Layer | State |
|---|---|
| Bit reader, varint decoding | Done. Unit tested, mutation tested, fuzzed. |
| Demo header, command stream | Done. Re-encodes byte-for-byte on every demo held here. |
| Net messages (layer 2) | Done for every type the corpus contains, across all five protocols. |
| Entity schema (layer 3) | Done. `dem_datatables` parses and flattens; entity deltas, instance baselines and cross-tick state all decode. |
| Sounds, temp entities, user messages | Done. These were the last bodies consumed without being read. |
| Text dump, Quake-style trace, JSON Lines, CLI | Done. |
| 2D viewer (Phase 2), 3D viewer (Phase 3) | Not started. |

### How much of the codec is actually deciphered

The honest measure is not "decodes without stopping" — a reader that steps over a body it does
not understand stays perfectly aligned and looks identical to one that reads it. So a corpus test
counts payload bits in two buckets: **modelled**, meaning every field became a value, and
**opaque**, meaning consumed at a known length and discarded.

```
tf2-2007-build3258-stv-cp_granary.dem     192 of  1,966,872 payload bits opaque (0.01%)
tf2-2009-build3862-pov-cp_badlands.dem    879 of    685,560 payload bits opaque (0.13%)
tf2-2013-build1729296-stv-cp_foundry.dem   88 of  2,264,584 payload bits opaque (0.00%)
z1800.dem                                 357 of 14,932,648 payload bits opaque (0.00%)
```

0.00–0.19% per demo. What remains is `svc_EntityMessage`, whose body is laid out by the receiving
entity's class and has no generic reading, and voice payloads, which are a codec. The test reports
and does not gate — a gate would be set to today's number and then defended.

### Whether the decode is lossless

A different question from the one above, and the sharper one. A field can be read and thrown away
without anything noticing: the reader stays aligned, the trace looks complete, the length check
passes. So the parser re-encodes what it decoded and compares against the demo's own bytes.

- **Every message**, on every demo held here, comes back bit for bit — 87,733 of them across five
  protocols. That is a gate, not a report.
- **Entity snapshots**, rebuilt from decoded property values rather than replayed, are exact on
  13,942 of 13,973. Every demo recorded before 2013 is at 100%; the 31 exceptions are modern and
  are documented in `docs/RISKS.md` B25 rather than smoothed over.
- **Sound bodies** are exact on all 11,989, which matters because `svc_Sounds` is the one decoder
  with no second implementation anywhere to check it against.

Building that found four things nothing else could: a temp entity count of zero meaning one
reliable effect, three messages discarding their bodies, `svc_VoiceInit` overwriting a quality with
a sample rate, and `svc_BspDecal` decoding a position and then dropping it.

**What is still not proven:** the *text* output cannot be compiled back into a `.dem`. The pieces
now exist — a bit writer, a message writer, an entity encoder — but the text parser that would
drive them does not. That is the Quake demo tools standard and the remaining Phase 1 goal.

### Corpus

Ten demos are committed (Git LFS): matched POV and SourceTV pairs recorded on period TF2 clients
from archive.org — 2007 launch, 2008, 2009, 2011, 2013 — plus modern demos. A larger local corpus
is used for testing and deliberately not committed; `docs/DECISIONS.md` D31 has the split and the
reason.

Old demos turned out to be genuinely scarce, so the route that worked was acquiring old *clients*
and recording new demos on them, which gives dated specimens instead of undated ones.
`docs/TIMELINE.md` is the era table and `docs/RECORDING_CHECKLIST.md` is what to do while
recording, so two eras differ only by era.

### Testing

773 tests, zero build warnings.

Layered deliberately, because each layer answers a different question: unit tests (right answer on
input we thought of), CsCheck properties (right across the whole input space), Stryker mutation
testing (would the tests notice if the code were wrong), SharpFuzz (does it survive input nobody
would write), corpus tests (does it work on bytes TF2 actually produced), and cross-parser
differential tests (does an independent implementation agree — the only check that can catch a
self-consistent misunderstanding). See `docs/DECISIONS.md` D6, D8 and D12, and
`docs/DIFFERENTIAL.md`.

Three bugs found in one session were unreachable from every demo held here — a temp entity count
of zero, a class-id width, a string table width — because the corpus contains no input where the
right and wrong answers differ. A corpus is evidence about the demos in it, and silence from it is
not agreement.

```
tf2demosalvage <demo.dem>              # readable summary and command listing
tf2demosalvage <demo.dem> -s           # header, counts, players and events only
tf2demosalvage <demo.dem> -t           # decompile to text, message by message
tf2demosalvage <demo.dem> -t -e        # ... with entity snapshots expanded
tf2demosalvage <demo.dem> -j           # one JSON object per line
tf2demosalvage <demo.dem> -o out.txt   # write to a file
```

Entities are off by default because expanding them turns a 39 MB demo into gigabytes of text;
`--entity-limit <n>` is the practical way to look at them.

### Documentation map

- `ROADMAP.md` — the phased plan.
- `docs/DECISIONS.md` — D1-D31, every architectural choice and why.
- `docs/SPEC.md` — the format spec, with every claim tagged by how it is known (CONFIRMED
  against real bytes / DOCUMENTED / UNDOCUMENTED / OPEN).
- `docs/RISKS.md` — anticipated blockers, ordered by when they bite.
- `docs/FORMAT_NOTES.md` — findings per corpus demo, including corrections to earlier claims.
- `docs/TIMELINE.md` — the era axis: which protocol each build shipped, measured rather than
  assumed, and what changes at each boundary.
- `docs/RECORDING_CHECKLIST.md` — what to do while recording on a period client, so two eras
  differ only by era.
- `docs/FUZZING.md`, `docs/RENDERING_NOTES.md` — D8 and Phase 2/3 groundwork.
- `docs/DIFFERENTIAL.md` — comparing output against an independent parser, and how to set the
  optional oracle up.
- `docs/memory/` — the AI assistant's working memory, committed so it survives a machine
  wipe. Includes the findings that cost the most to establish, and the corrections to
  earlier wrong ones.
- `CLAUDE.md` — handoff brief.

### Prior art (referenced, not copied)

Both are permissively licensed, checked 2026-08-07:

- [tf-demo-parser](https://codeberg.org/demostf/parser) (Rust, MIT OR Apache-2.0) — powers
  demos.tf. The reference for TF2 specifically.
- [UntitledParser](https://github.com/UncraftedName/UntitledParser) (C#, MIT) — Source demo
  parser used by the Portal/HL2 speedrunning community. Same language as this project.

The licences permit copying. This project's own rule against porting is an engineering
preference — the point is to understand the format — not a legal constraint.

## Why

TF2 demos are self-contained — the network entity schema (`SendTables`) that describes how to decode a demo's data is embedded in the file itself, so a demo doesn't actually need to match the *current* game client's schema to be readable. The client crashes on old demos because it validates against its own live schema; a standalone parser that only reads what the file provides sidesteps that entirely. See `ROADMAP.md` §1 for the full explanation, and `docs/FORMAT_NOTES.md` for a real example (`z1800.dem`, a modern demo that fails in the live client but is intact).

## Plan at a glance

1. **Parse** — decode any-era `.dem` into structured/readable output (the actual "restore lost demos" deliverable).
2. **2D viewer** — scrub a match on a top-down map.
3. **3D viewer** — starts at primitive geometry (spheres/capsules, same shapes TF2's hitboxes already use), fidelity work comes later and isn't scoped yet.

Pure C# (`managed/`) — decode engine, CLI, and both viewers all live there. `native/libtf2dem` is a placeholder only; native code is a last resort, revisited only if Phase 3 profiling proves C# genuinely isn't enough for something. Full reasoning in `docs/DECISIONS.md`.

## Repo layout

```
native/libtf2dem/     placeholder — not used unless Phase 3 proves it necessary
managed/               decode engine (Tf2DemoSalvage.Core), CLI, viewers — all C#
tools/corpus/          reference demos (Git LFS) + manifest
docs/                  decisions, spec, risks, format notes
tests/                 unit, corpus regression, and fuzz-property tests
```
