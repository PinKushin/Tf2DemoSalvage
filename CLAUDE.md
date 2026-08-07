# CLAUDE.md — implementation handoff

This project was planned in a Cowork conversation before any code existed. Everything below is context an implementer needs that isn't obvious from an empty repo. Read `ROADMAP.md` and `docs/DECISIONS.md` in full before writing code — this file is a pointer/summary, those are the source of truth.

## What this project actually is

A standalone TF2 `.dem` parser that works across TF2's full history, including demos the live game client can no longer play due to Valve's own schema changes. The insight that makes this tractable: `.dem` files embed their own entity schema (`SendTables`, via the `dem_datatables` command), so a parser that decodes generically off whatever schema each file provides — rather than hardcoding one era's field layout — doesn't need to "know" every TF2 version, just the container/bit-packing quirks, which change far less often. Full explanation in `ROADMAP.md` §1.

## Non-negotiable constraints (owner-stated, don't relitigate without asking)

- **No Rust.** Explicitly rejected, don't suggest it.
- **No C++ by default.** C for the perf/correctness-critical decode core, C# for everything else. C++ is only acceptable if deliberately reached for to wrap Source SDK code for one specific hard-to-reverse-engineer Phase 3 asset format (see `docs/DECISIONS.md` D4), and even then it must be isolated behind a C ABI shim, not spread through the codebase.
- **No Python** for the core — too slow for bulk corpus processing at the scale this is meant to eventually handle.
- **Default to C# for everything, including work that feels performance-sensitive.** Use `unsafe`/`Span<T>`/`stackalloc`/`MemoryMarshal` before reaching for C. Only drop into `libtf2dem` when C# has actually proven inadequate for that specific piece (profiled, not assumed) — the C surface should stay limited to the varint/bit-level decode primitives that genuinely need it, not expand by default just because it's "the perf layer."
- **No native code for Phase 1/2, full stop.** The decode engine lives in `managed/Tf2DemoSalvage.Core`, pure C#. `native/libtf2dem` is a placeholder folder, not a starting point — don't build in it unless Phase 3 profiling has actually shown a specific piece needs it. If that ever happens: default to C (MSBuild/vcxproj, same VS solution as everything else); Zig is an open long-shot alternative (not C++) since it exports a plain C ABI natively — same P/Invoke story as C, better memory safety, no C++-style naming/template chaos — but it needs its own build step (`build.zig`) outside the main `.sln`. Decide C-vs-Zig only when this trigger actually fires, not preemptively.
- **TDD, SOLID, DRY are standing requirements**, not just this project's style — see `docs/DECISIONS.md` D6 for how they map onto this codebase's actual seams (decode-vs-interpret separation, per-version-quirk strategy objects instead of branchy conditionals, one schema/quirk table as single source of truth). Write the byte-level unit tests for `libtf2dem`'s primitives (varint reader, bit reader, SendTable delta decode, string table decode) *before* the implementation, using small hand-built fixtures — don't rely on end-to-end corpus tests alone to catch primitive-level bugs, the corpus is too sparse for that to be safe (see next section).
- **Run Stryker.NET on every C# test project** as part of normal development, not bolted on at the end. It mutates the code and checks whether the test suite actually kills the mutants — proves the tests do something, unlike coverage percentage alone. A surviving mutant is a real finding: either add the missing assertion, or the mutated code path genuinely doesn't matter and can be deleted. Doesn't apply to `libtf2dem` (Stryker is .NET/JS-only) — for the C core, equivalent rigor comes from adversarial hand-built byte fixtures per primitive, and every malformed-input bug found becomes a permanent regression fixture.
- **Wire up SonarLint + Roslyn analyzers (`Microsoft.CodeAnalysis.NetAnalyzers`, `SonarAnalyzer.CSharp`) from the first C# project**, with `.editorconfig` set to `warning`/`error` for correctness-related rules so violations surface at build time, not in a later cleanup pass.

## Corpus reality

There is currently **one** confirmed reference demo: `tools/corpus/demos/z1800.dem` (metadata in `tools/corpus/manifest.json`, full notes in `docs/FORMAT_NOTES.md`). It's a ~2015-era FACEIT SourceTV demo, demo protocol 3 / network protocol 24, structurally intact, fails in the live client for schema-validation reasons unrelated to file integrity — a good first Phase 1 target.

Do not assume a broad multi-era test corpus exists or will exist soon. TF2's pre-2013 competitive scene mostly used live Mumble casts rather than recorded demos, and there was no centralized archive before demos.tf, so older specimens are genuinely rare (`docs/DECISIONS.md` D5). Build defensively (schema-driven, not hardcoded) *because* of this, not despite it. If/when more demos surface (community outreach is a parallel, non-blocking effort), add them to `tools/corpus/manifest.json` and give each one a regression fixture in `tests/`.

## Where to start

Phase 1 (see `ROADMAP.md` §3): `managed/Tf2DemoSalvage.Core`, pure C# — container parsing, then `dem_datatables`/`dem_stringtables`, then generic SendTable-driven entity delta decode, emitting a normalized event stream. Validate against `z1800.dem` end to end once the primitives are unit-tested individually. Output target: a Quake-style readable text dump plus JSON Lines / per-demo SQLite. Do not create anything under `native/libtf2dem` for this phase.

Do not start Phase 2 or Phase 3 work before Phase 1 is solid and tested. Do not build toward Phase 4 (demo repair for live-client replay) at all unless explicitly asked — it's parked, see `docs/DECISIONS.md` D1.

## Reference material (external, not vendored)

- [demostf/parser](https://github.com/demostf/parser) / `tf-demo-parser` crate — mature Rust reference implementation (demos.tf's actual parser). Read for cross-checking behavior, do not port code directly (different language, and the point is to actually understand the format).
- [demboyz DemFormat.md](https://git.botox.bz/CSSZombieEscape/demboyz/src/commit/3858162c9c0fb0988e30f61de526ebfe85eb1e2f/docs/DemFormat.md) — container format writeup, as documented for the TF2 build active July 2015.
- Valve Developer Community wiki: Networking Entities, Networking Events & Messages.
