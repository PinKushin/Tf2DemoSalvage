# Decisions log (ADR-style)

Short record of every locked decision from planning, with the reasoning. If you're reconsidering any of these, read the reasoning first — most were argued through explicit wins/drawbacks, not just picked.

## D1. Scope: extraction first, 2D viewer, 3D viewer (primitives first), repair parked

Goal is "recover data from TF2 demos of any age" primarily, with a viewing story on top, in the spirit of Quake community demo tools (parse → text/data → 2D playback → 3D playback), not dependent on Valve's live client.

- Phase 1: parse to structured/readable data. This alone satisfies "restore lost demos."
- Phase 2: 2D top-down scrub viewer.
- Phase 3: 3D viewer, v0.1 = primitive geometry only (spheres/capsules for players, same shapes TF2's own hitboxes already use, over flat-shaded BSP brush geometry). Fidelity work (real models/materials/animation) is explicitly unscoped/later, not part of v0.1.
- Phase 4 ("repair a demo so it replays in the live TF2 client again"): parked, treated as basically never happening. Feasibility is genuinely uncertain (would mean rewriting entity data to match a constantly-moving current-client schema), and Phase 3 already meets the actual need ("see what happened") without fighting client-side validation at all.

## D2. Language: pure C# for Phase 1 and 2. Native C deferred entirely, revisit only if Phase 3 profiling demands it.

**Superseded from the original plan** (first draft proposed a C decode core + C# everything else — that C core was never built, don't resurrect it from an old conversation transcript or stale docs). Current call: there's no reason to believe Phase 1 (demo decode) or Phase 2 (2D viewer) need native code at all — modern C# (`unsafe`, `Span<T>`, `stackalloc`, `MemoryMarshal`, `System.Numerics` SIMD) is fast enough for bit-level binary decoding and bulk corpus processing. Write Phase 1/2 entirely in C#, in `managed/Tf2DemoSalvage.Core` (the decode engine itself, not just bindings over one) and `managed/Tf2DemoSalvage.Cli`/`Tf2DemoSalvage.Viewer2D`.

- `native/libtf2dem` stays in the repo as a placeholder only. Do not start implementation there for Phase 1/2. Revisit *only* if Phase 3 (3D viewer) profiling shows a specific piece genuinely needs native code (most likely candidate, if any: a hot per-frame render-loop transform step, not demo decoding) — and even then, treat it as a last resort after `unsafe` C# has actually been tried and measured, not assumed inadequate.
- **If native code is ever justified, default to C, but Zig is an open long-shot alternative — not C++.** Zig exports a plain C ABI as its native mode, not an accommodation bolted on — so from C#'s side (via P/Invoke) a Zig library looks exactly like a C library, zero interop cost difference. What it buys over raw C: bounds-checked slices, no implicit null, explicit error unions instead of silent undefined behavior — real safety improvements with none of C++'s naming-convention chaos or template/smart-pointer learning curve. Caveat: Zig isn't MSVC/vcxproj-native (own build system, `build.zig`), so it wouldn't slot into D3's "lives in the main .sln" build story as cleanly as C would — it'd produce a static/dynamic lib as a separate build step that the C# solution links against, same as any external native dependency. Still pre-1.0-flavored and smaller ecosystem than C, which is why it's "open to," not "preferred over," C — treat the actual choice between them as a decision to make *if and when* D2's native-code trigger ever fires, not now.
- Reference point for that future decision, if it ever comes: [PlummersSoftwareLLC/Primes](https://github.com/PlummersSoftwareLLC/Primes) ("Software Drag Racing") is a legitimately harder-to-game cross-language benchmark than most — open PR review, an explicit standard algorithm every submission must implement, and rules against exactly the "shell out to a faster language for the hot path" trick — and it already has both C and Zig entries. Worth checking as a sanity check alongside our own workload-specific benchmark, not as a substitute for one.
- Note on Phase 3 rendering specifically, since "will C# be fast enough for DirectX" is a reasonable thing to doubt: **Vortice.Windows is a thin wrapper directly over the real D3D11/12 COM interfaces**, not a heavy engine abstraction layer — the interop overhead itself should be close to native. Unity's reputation for uneven performance isn't evidence against this; Unity's overhead mostly comes from its own scripting/component architecture and GC churn patterns sitting *on top of* the native engine, not from C#-to-native interop being inherently slow. So "thin C# wrapper calling straight into D3D" and "Unity game" aren't the same kind of C# usage, and shouldn't be expected to have similar performance characteristics.
- Explicitly rejected: Rust (owner preference, do not suggest again), pure Python (too slow for bulk corpus processing), C++ as a default (owner doesn't want to deal with its quirks) — **except** where Source SDK reuse is deliberately chosen for a specific Phase 3 asset format (see D4), isolated behind a C ABI shim if it happens.
- Practical implication for D6 (TDD/mutation testing): since the decode engine is now C#, Stryker.NET mutation testing applies to it directly — no separate "C core has no mutation tool" carve-out needed for Phase 1/2. Byte-level adversarial fixtures are still the right approach for the decode primitives regardless of language, just written and tested in C# from the start.

## D3. Native build: MSBuild/vcxproj, not CMake

`libtf2dem` lives as a native project in the same Visual Studio solution as the C# projects. Tightest IDE/debugging integration across the native/managed boundary; matches how the rest of the owner's repos are organized.

Trade-off accepted knowingly: Windows/MSVC-only, no cheap Linux CI for ASan/UBSan fuzzing of the decode core. If fuzzing becomes a priority later, add a secondary CMake-based fuzz target rather than migrating the main build.

## D4. Source SDK: not used by default; evaluated per-format only in Phase 3

Source SDK 2013 does **not** contain the demo/net parser or renderer (`engine.dll` stays closed) — it only has mod-side game code, `tier0`/`tier1`/mathlib utilities, and compiler tool headers (`bspfile.h`, `studio.h`). So it's irrelevant to Phase 1/2 regardless.

For Phase 3 asset parsing (BSP/MDL/VTF), default is clean-room parsing from community-documented formats (Valve Developer Community wiki, cross-checked against prior art like SourceIO/Crowbar/HLLib) to avoid the SDK's license ambiguity (written around non-commercial mods requiring the base game — a standalone public tool is a gray-area fit) and to avoid pulling C++ into the codebase. Reconsider **only** if a specific format (most likely MDL/VVD/VTX skeletal animation) proves too error-prone to reverse-engineer cleanly — and if so, wrap it behind a C ABI shim like the core, don't let C++ leak into the rest of the codebase.

## D5. Corpus strategy: two different problems, not one

**Rewritten 2026-08-07.** The original version treated corpus scarcity as a single problem and rested on a factual error: it described `z1800.dem` as "~2015 era", inferred from its protocol pair. Reading the file's own string tables dates it to **mid-2020 or later** (`sum20_fire_fighter_style1`, `@20_handsome_devil`, `rglgg_medal`, Competitive Mode voice lines). Demo protocol 3 / network protocol 24 was current in July 2015 *and stayed current for years* — see `SPEC.md`. Protocol numbers tell you which decode quirks apply and nothing whatever about age.

That correction splits the problem in half, and the halves need opposite strategies.

### Modern demos (roughly 2016 onward): abundant, and we were treating them as scarce

They are freely available from demos.tf, and the owner can record new ones on demand. This is not a constraint at all, and three things follow that the old decision missed:

- **Self-recorded demos are the only source of correctness ground truth we have.** For a demo you recorded, you know what happened — map, classes, a kill at a known moment — so the parser's output can be checked against reality. Neither `z1800.dem` (nobody knows what is in it) nor fuzzing (D8 proves the parser does not fall over, explicitly not that it decodes correctly) can do this. It is the only axis that catches a decoder that runs cleanly and reports the wrong thing.
- **Two eras side by side is how version quirks get found.** One specimen cannot show which behaviours are version-gated. A second era can, by disagreeing.
- **`z1800.dem` is still a good first target**, but for a reason we had wrong. It is not rare — it is a *2020 demo broken by the July 2023 schema change*, which is precisely the failure this project exists to work around. That makes it representative of the common case, not of a historical edge case.

### Historical demos (pre-2016): genuinely scarce, and we currently have **zero**

The original reasoning here still holds. TF2's early competitive era (~2008–2010) ran mostly on live Mumble casts rather than recorded STV, no centralised archive existed before demos.tf, and sizzlingstats — which did keep demos — has been gone for years. Recovering anything from that far back depends on an individual having personally kept a file for 15+ years.

What changed is the count. We believed we had one mid-2010s specimen; we have none. **The parser currently has no historical coverage of any kind, and no test that would detect its absence.**

### Decision

- **Build against modern demos, and say plainly that this is what we have tested.** Do not let a green suite imply era coverage we do not possess.
- **Add modern specimens deliberately, not incidentally**: demos.tf for variety, self-recorded for ground truth. Each one gets an entry in `tools/corpus/manifest.json` and a regression fixture in `tests/`.
- **Date every acquired demo from its assets**, never its protocol numbers, and record the evidence in the manifest. This is cheap and would have caught the original error immediately.
- **Historical outreach stays parallel and non-blocking** (r/tf2, TF2 Discords, ETF2L/teamfortress.tv forums). Still worth doing, still not a dependency.
- **The schema-driven design (D1/D2) remains the hedge**, and the correction strengthens the case for it rather than weakening it: we now know we have no way to test the historical path, so the architecture has to be right by construction rather than by verification.
- **`z1800.dem`'s one-byte truncation is a free fixture** for the salvage case (`SPEC.md`), independent of era.

## D6. Engineering practice: TDD, SOLID, DRY — applied everywhere, not just talked about

Owner's standing global preference for all work, not specific to this project, but worth stating concretely here since it should shape how Claude Code actually implements Phase 1 onward:

- **TDD**: tests come first, especially for `Tf2DemoSalvage.Core`'s decode engine (per D2, this is where the decode primitives now live — pure C#, not a separate native core). The golden-corpus regression tests in `tests/` (D5) are the outer loop — but the decode primitives (varint reader, bit reader, SendTable delta decode, string table decode) each need their own unit tests written *before* the implementation, using small hand-built byte fixtures, not just relying on `z1800.dem` end-to-end. End-to-end-only testing means a bug in an untested primitive won't surface until it happens to matter for that one demo, which is exactly the failure mode this project can't afford given how sparse the corpus is (D5).
  - **Mutation testing with Stryker.NET on every C# project** (`Tf2DemoSalvage.Core` included — per D2 there's no separate native core to carve out for Phase 1/2) — coverage percentage alone doesn't prove the tests would actually catch a bug, Stryker mutates the code and confirms the suite kills the mutants. Run it as part of the normal test-writing loop, not as an afterthought bolted on later, and treat a surviving mutant as a real gap to close (either the test is missing an assertion, or the mutated line genuinely doesn't matter and can be simplified away). If Phase 3 ever produces actual native C code (D2, last resort only), that piece would need adversarial hand-built fixtures instead, since Stryker is .NET/JS-only.
- **Static analysis on all C# projects: SonarLint + the Roslyn analyzer stack** (`Microsoft.CodeAnalysis.NetAnalyzers` at minimum, plus SonarLint's own analyzer via the Sonar for VS/Rider extension or `SonarAnalyzer.CSharp` NuGet package for CI enforcement) wired in from the first C# project, not added later — `.editorconfig` should set analyzer severities to `warning` or `error` for anything correctness-related so violations surface at build time. Treat these the same way as the mutation-testing gate: a finding is either fixed or the suppression is justified in a comment, not silently ignored.
- **SOLID**, applied concretely to this codebase's actual seams:
  - *Single Responsibility*: keep "decode bytes for protocol version X" separate from "interpret decoded entities into game-level concepts" (kills, captures, chat) — the former lives in `libtf2dem`/`Tf2DemoSalvage.Core`, the latter shouldn't know about bit-packing at all.
  - *Open/Closed*: per-protocol-version quirks (D1's "small table of documented quirks per version range") should be added as new strategy implementations, not by branching deeper into existing decode functions. A new TF2 era showing up shouldn't require touching code that already works for other eras.
  - *Liskov / Interface Segregation*: any viewer (2D or 3D) should consume a narrow playback/query interface over parsed demo data, not the raw parser internals — so Phase 3 replacing/extending Phase 2's data access doesn't ripple into parsing code.
  - *Dependency Inversion*: the C ABI boundary between `libtf2dem` and `Tf2DemoSalvage.Core` already forces this — C# code depends on the stable ABI shape, not native internals. Keep that discipline going up the stack too (CLI/viewers depend on `Tf2DemoSalvage.Core`'s abstractions, not on each other).
- **DRY**: one schema/quirk table is the single source of truth for "how does version X differ," referenced by every consumer (CLI, both viewers) rather than each reimplementing its own notion of version handling. If the same "which protocol quirks apply" logic starts getting duplicated across projects, that's a signal that abstraction needs to move down into `Tf2DemoSalvage.Core` or `libtf2dem`.

## D7. License: MIT, public repo

Public so the wider TF2 demo-tooling community (demos.tf, tf-demo-parser maintainers, etc.) can contribute schema fixes for versions the owner doesn't personally have test demos for. MIT chosen as a simple permissive default; easy to revisit if a reason to change comes up.

## D8. Fuzzing: adopted, two layers, primitives first

Accepted 2026-08-07. Full reasoning and the toolchain setup traps live in `FUZZING.md`; this entry is the decision record, not a duplicate of it.

The case rests on two facts about this project compounding. The decode primitives are hand-written at the bit level — `BitReader` and everything after it are original code with no `Utf8JsonReader`-style layer underneath that has already absorbed a decade of adversarial input. And D5 says the corpus is one demo and will probably stay that way. A fuzzer can't manufacture a 2009 demo, but it manufactures hundreds of thousands of *malformed* ones from the file already in hand, which is exactly the input class a sparse corpus never supplies. Fuzzing is therefore the second hedge against D5, alongside the schema-driven decoder design.

- **The property, at every level:** a parse either succeeds, or throws an exception this project documents as meaning "that input was not valid" — currently `EndOfStreamException` (the buffer ended mid-field), `InvalidDataException` (the bytes are structurally impossible, e.g. a varint longer than its type allows), and `ArgumentException`. An `IndexOutOfRangeException`, `NullReferenceException`, `OutOfMemoryException`, or a non-terminating loop is a defect, because a caller can't defend against those when the bytes came from a file someone downloaded. The list grows as decoders are added; adding to it is a deliberate act, not something a new decoder does by accident.
- **Two layers, because they cost different amounts.** A deterministic, seeded mutation suite runs in the normal test suite on every build — milliseconds, reproducible failures, catches obvious regressions. Coverage-guided fuzzing (SharpFuzz + libFuzzer) runs on a slower cadence and is where inputs nobody would think to write actually come from.
- **Target order:** `BitReader`, then the varint reader, then string-table and SendTable delta decode, then whole-file parse seeded with `z1800.dem`. Primitives first because a crash in one is trivially localised; a crash in the whole-file parse is not.
- **Scope limit, stated so it isn't oversold:** fuzzing proves the parser didn't fall over. It proves nothing about whether the bits were decoded *correctly*. It does not reduce the need for real demos (D5) — it covers a different axis. Correctness still comes from unit tests and golden-corpus regression.
- **Relationship to D6:** unit tests ask "right answer on input we thought of", Stryker asks "would the tests notice if the code were wrong", fuzzing asks "what happens on input nobody thought of". Three different questions; none substitutes for another.

Deliberately deferred: the *scheduled* coverage-guided workflow. The coverage-guided path itself runs locally under WSL and has been verified (805,921 executions, no crashes — see `FUZZING.md`), but the repo has no CI and no remote yet, so the job lands when CI does. There is a second reason not to rush it: against `BitReader` alone the feature count saturates after ~15,000 executions, so a scheduled long run today would burn budget and produce a green badge that proves nothing. It earns its place once targets #3 and #4 exist and the state space is actually large.

## D9. Map assets: resolved at runtime, not bundled — and no blanket "ships no assets" promise

Decided 2026-08-07, replacing the earlier README/ROADMAP line "ships no TF2 game assets," which was an over-promise made without the owner's agreement. Phases 2 and 3 genuinely need map geometry, and a tool that refuses to touch maps is useless for the corpus it exists to serve.

**Default: resolve, don't bundle.** A map is located, in order:

1. The user's own TF2 install (`.../steamapps/common/Team Fortress 2/tf/maps`, and any custom/download directories). They own the game; Valve's maps are legitimately on their disk already.
2. A local cache directory owned by this tool.
3. A source the *user* configures — a league fastdl, TF2Maps, wherever they already get maps.

This is the better engineering answer regardless of licensing. The long tail of community comp maps is unbounded and changes every season; no bundle can cover it, so a resolver is strictly more capable than shipping a fixed set. Bundling only ever solves maps we anticipated.

**Rules that follow:**

- **Never bundle Valve-authored content.** Not a legal opinion, just the one clear-cut case, and the user already has those files.
- **Community maps may be bundled case by case**, when the author's terms actually permit redistribution — recorded in a manifest naming the map, author, license, and source. Not ruled out; just not done by default and never done silently. Worth being precise here: community maps are not Valve's, but that means the copyright is the *mapmaker's*, not that it is unowned. Permission to host a map for play (which is what fastdl relies on) is not automatically permission to redistribute it inside a separate tool. Usually the author is fine with it; occasionally not; checking per map is a real research cost that scales badly. The resolver avoids the question entirely.
- **MIT covers this project's code only.** Any asset that ever does get bundled keeps its own license, stated in that manifest.
- **Prefer the TF2 `maps` directory when there is one; fall back to a tool-owned cache.** An earlier draft of this decision said never write into the game install, on the grounds that it would collide with Steam's file validation. **That was wrong** — Steam verifies files it has manifests for, and a community map is in no manifest, so validation ignores it. Community maps have lived in `tf/maps` for the game's whole life and that is where users and servers already put them. Writing there is also what a TF2 player actually wants, because the game can then use the map too.
  - Fall back to the tool's own cache when there is no TF2 install (headless analysis), the path is not writable, or the user asks for it.
  - **Never overwrite an existing map file.** This is the real hazard, and it is not Steam's: a user may have a modified or differently-versioned map of the same name, and silently replacing it destroys something we cannot restore. If the target name exists and differs from what we fetched, write to the cache and say so rather than clobbering.
- **Treat every fetched map as untrusted input** (per the global security standard): no baked-in mirror URLs, sanitize any path derived from a demo's map name before it touches the filesystem (`Path.GetFullPath` plus a prefix check — a map name is attacker-controlled text inside a demo someone downloaded), and validate what comes back before parsing it.

**Not yet decided:** whether the fetch step ships at all in v1, or whether resolution is install-and-cache only, with fetching left to the user. Decide when Phase 2 actually needs a map we do not have.
