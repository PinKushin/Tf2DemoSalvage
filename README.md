# Tf2DemoSalvage

A standalone parser (and, eventually, viewer) for Team Fortress 2 `.dem` files — built to work on demos from any era of TF2's 18-year history, including ones Valve's own client updates have broken.

Independent, clean-room project. Not affiliated with Valve. Ships no Valve-authored game
assets — maps are resolved from your own TF2 install or a source you configure, not bundled
(see `docs/DECISIONS.md` D9).

## Status — early, and honest about it

**Phase 1 partially built. Nothing user-facing exists yet: there is no CLI, no output, no viewer.**
What works is the bottom of the stack, tested hard.

| Layer | State |
|---|---|
| Bit reader, varint decoding | Done. Unit tested, mutation tested, fuzzed. |
| Demo header | Done. Parses all three corpus demos. |
| Command stream | Done. Walks all three demos; counts match their headers exactly. |
| Net messages inside packets (layer 2) | **Not started.** Message ids unmined. |
| Entity/SendTable decode (layer 3) | **Not started.** No public wire spec exists. |
| Text dump — the first actual deliverable | **Not started.** Next up. |
| 2D viewer (Phase 2), 3D viewer (Phase 3) | Not started. |

102 tests, zero build warnings, zero surviving mutants.

So: the container is solved and verified against real files, and the interesting half — what is
*inside* the packets — has not been attempted. Treat any claim beyond the table above as
aspiration, not capability.

### Documentation map

- `ROADMAP.md` — the phased plan.
- `docs/DECISIONS.md` — D1–D11, every architectural choice and why.
- `docs/SPEC.md` — the format spec, with every claim tagged by how it is known (CONFIRMED
  against real bytes / DOCUMENTED / UNDOCUMENTED / OPEN).
- `docs/RISKS.md` — anticipated blockers, ordered by when they bite.
- `docs/FORMAT_NOTES.md` — findings per corpus demo, including corrections to earlier claims.
- `docs/FUZZING.md`, `docs/RENDERING_NOTES.md` — D8 and Phase 2/3 groundwork.
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

TF2 demos are self-contained — the network entity schema (`SendTables`) that describes how to decode a demo's data is embedded in the file itself, so a demo doesn't actually need to match the *current* game client's schema to be readable. The client crashes on old demos because it validates against its own live schema; a standalone parser that only reads what the file provides sidesteps that entirely. See `ROADMAP.md` §1 for the full explanation, and `docs/FORMAT_NOTES.md` for a real example (`z1800.dem`, a 2020-era demo that fails in the live client but is intact).

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
