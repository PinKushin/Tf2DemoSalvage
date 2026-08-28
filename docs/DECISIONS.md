# Decisions log (ADR-style)

Short record of every locked decision from planning, with the reasoning. If you're reconsidering any of these, read the reasoning first — most were argued through explicit wins/drawbacks, not just picked.

## Adding a decision

**The next number is D44.** Take it from the index below, never by scrolling to the end — entries are
in the order they were written and the file is not sorted, so the last heading is not the highest
number. D32 and D33 sit between D34 and D35.

**A number is permanent once cited.** Correcting a decision means an addendum under its existing
heading (`### D15 addendum`, `### D25 outcome`) or a new number that says what it reverses. Never
renumber a live entry.

This section is deliberately not itself a numbered decision: it has to be read by whoever is about to
add one, and that is the top of the file, not entry 43 of 43.

`build/assert-decision-numbers.sh` fails the gate on a number used twice, and `build/gate.sh` runs
it. It exists because D20–D28 each named two different decisions for a while (B118): a session
restarted the count at D20 without reading the file, the two series interleaved, and the heading
level was the only thing telling them apart — invisible in a citation. Both series were cited from
live source comments, and half the "D20" citations pointed each way.

## Index

| # | Decision |
|---|---|
| D1 | scope: extraction first, 2D viewer, 3D viewer, repair parked |
| D2 | language: pure C# for Phase 1 and 2, native C deferred |
| D3 | native build: MSBuild/vcxproj, not CMake |
| D4 | Source SDK: not used by default, per-format only in Phase 3 |
| D5 | corpus strategy: two different problems, not one |
| D6 | engineering practice: TDD, SOLID, DRY |
| D7 | licence: MIT, public repo |
| D8 | fuzzing: adopted, two layers, primitives first |
| D9 | map assets resolved at runtime, not bundled |
| D10 | rendering API: Direct3D 11, not DX9 (binding choice superseded by D34) |
| D11 | demo corpus storage: Git LFS |
| D12 | property-based testing with CsCheck |
| D13 | the mutation gate runs incrementally, fully before a merge |
| D14 | the corpus stores one demo per map |
| D15 | the mutation gate runs at most once a day (+ two addenda) |
| D16 | JSON Lines is the machine-readable output |
| D17 | SQLite export is removed, not deferred |
| D18 | the primary output is a Quake-style trace |
| D19 | old demos need old binaries, not old source (+ two addenda) |
| D20 | the protocol boundary list comes from Valve |
| D21 | the era boundaries stay open, a demo is cheaper than research (+ outcome) |
| D22 | the trace reaches the command line, the CLI gets its own tests |
| D23 | corpus work is cached per process |
| D24 | a faster suite recalibrated the mutation tool (+ correction) |
| D25 | the test project splits along the pure/stateful seam (+ outcome) |
| D26 | CI runs mutation and fuzzing on separate schedules |
| D27 | entity baselines, and what they are not wired to yet |
| D28 | user messages are named, not decoded |
| D29 | mutation testing moves to the shared Oracle box |
| D30 | date a candidate build before downloading it |
| D31 | two corpora: gcor per generation, lcor everything else |
| D32 | a downloaded BSP is hostile input |
| D33 | FlaUI for the UI tests |
| D34 | one renderer with two camera modes, Direct3D 11 via Silk.NET |
| D35 | geometry is world space; only the camera knows about the view |
| D36 | surf and jump runs are a named audience |
| D37 | models are lit the way the engine lights them |
| D38 | the suite runs on synthetic demos; the corpus keeps what only real bytes prove |
| D39 | test names are `{Subject}_{Scenario}_{Expected}` |
| D40 | no scripted edits to source files |
| D41 | this project's measurement check names this project |
| D42 | the viewmodel lookup answers the main hand |
| D43 | the viewmodel field of view defaults to 70, the top of the game's range |

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

> **SUPERSEDED IN PRACTICE, 2026-08-22.** Both halves of this entry are now wrong, and it is left
> standing with this note because a decision that was quietly deleted is one nobody can learn from.
>
> **The factual premise is false.** "Only mod-side game code" understates it badly: the SDK carries
> **1,318 files of TF2's own game code**, including the HUD, player conditions and the econ schema —
> see `docs/memory/tf2-game-code-is-in-the-sdk.md`, written after "TF2 is closed" had been asserted
> in three places and checked in none. `docs/memory/nothing-is-closed.md` is the general form: check
> the SDK, then the shipped data, then a decompiler, before ever writing "unavailable".
>
> **The conclusion is contradicted by daily practice.** This project reads the SDK constantly and
> deliberately. `tests/Tf2DemoSalvage.SdkReference` exists solely to read it; `CLAUDE.md` instructs
> to *"read and cite freely; quoting it in comments is the point"*; and `SdkCoverageTests` generates
> its denominator from the SDK — 489 shader parameters, 66 lumps, 54 studio structures. Every
> conformance suite in the repository rests on it.
>
> **The licence worry was also relitigated and settled differently.** The owner's position is that
> the legal question is grey and not a practical concern here, and that the hard rule about
> decompilers is about **repository size**, not licensing (`~/.claude/CLAUDE.md`). Reading published
> source was never decompilation in the first place.
>
> What survives unchanged: clean-room parsing of the asset formats, and the rule that if C++ is ever
> pulled in it stays behind a C ABI shim. Nothing in this project has needed that yet.

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
- **Prefer publicly published demos. Self-recorded ones are allowed, owner's call.** A POV demo's `client_name` header field is the recording player's name, and the string tables carry player names and SteamIDs. Committing the owner's own POV demo would bind their handle to a public repository permanently, and in LFS history at that. Public league demos carry other players' names too, but those matches were published by the league itself, so committing them exposes nothing new.
  - Format coverage needs no self-recording: ETF2L publishes POV demos (`etf2l-12025-pov-2020-07-21.dem` is one), which covers the POV-only command paths.
  - Correctness ground truth still does, since nobody knows what happened inside a public demo. Owner's position (2026-08-07): their name and SteamID are already widely published, so committing a self-recorded demo is acceptable, recorded under an altered screen name to be marginally harder to search for. `tools/corpus/local/` is git-ignored and stays available for anything they would rather keep off the repo, but it is not mandatory.
  - One technical caveat on the alias: the `userinfo` string table carries the **SteamID** next to the display name, so changing the screen name does not break the link to the account. It reduces casual searchability, nothing more. Not a problem given the owner's stated position — just do not expect more from it than it delivers.

- **The one-byte-short `dem_stop` is not a salvage fixture.** It was recorded as one; two further demos showed every TF2 demo ends that way, so it is the normal terminator (`SPEC.md`).

**Restated 2026-08-24, because D5 had framed this as a hedge against scarcity rather than as the
design.** The owner: *"the dating gap was never blocking, never was the corpus gap really, i was
going to make this with or without demo examples, and pray it worked for untested demos, because
there is plenty of information available to reverse the changes and account for them without
actually having to have a demo from every protocol, or client ever. we did most of our demo decode
work before we ever had a launch tf2 client, but it worked as soon as we passed a demo in because
the changes had all been documented online or by referencing earlier sdk's."*

That is the plan, stated before this project had a name, and it is confirmed by its own history:
the decode work was done and correct **before** a launch client was ever obtained, from published
changelogs and earlier SDK branches, and it worked the moment a real demo was finally passed in.
The corpus did not teach the parser anything it did not already know — it corroborated a design
already believed correct by construction.

So: **a demo is a specimen to corroborate the design, never an input the design depends on.** The
schema-driven read (D1/D2) is not insurance against not having one; it is the actual bet. Every
corpus and dating gap this project has filed — 12–13, 17–20/23, the un-dated recovered demos — is
notable and worth closing, but none of it has ever been blocking, and treating an open one as
blocked work is a category error the project has made before and should not make again.

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

## D10. Rendering API for Phase 3: Direct3D 11 via Vortice, not DX9

Decided 2026-08-07, ahead of the work, because the question ("TF2 is a DX9 game, shouldn't the viewer be DX9?") is reasonable enough that it will be asked again otherwise.

**The premise to reject first: TF2's rendering API and ours are independent.** We are not reimplementing Valve's renderer; we are reading Valve's *data formats*. BSP brushes, MDL meshes and VTF textures are bytes on disk with no API affinity — nothing about parsing them becomes easier by matching the API the game happened to ship with. "We know DX9 works" is evidence about TF2, not about this viewer.

**Why not DX9:**

- On current Windows it frequently runs through `d3d9on12` translation anyway, so the "closer to the metal" intuition is inverted — you land on D3D12 with a translation layer in between.
- Tooling is the real cost. RenderDoc and PIX are first-class on D3D11/12 and poor-to-absent on D3D9. The first time brush geometry renders inside-out, a frame debugger is the difference between an hour and a day.
- Shader model 3 ceiling, fixed-function-era baggage, single-threaded-era design.
- Vortice's maintained surface is D3D11/12; choosing DX9 means fighting the binding library as well as the API.

**Why D3D11 rather than D3D12:** D3D12's control — manual descriptor heaps, explicit fences, resource-state tracking — only pays off when CPU-bound on draw-call submission. Phase 3.0 renders roughly 3,110 brushes and a couple of dozen player capsules (measured from `koth_harvest_final.bsp`), which is nowhere near that regime. D3D12 would cost full complexity for no gain. D3D11 also runs on any GPU from roughly 2009 onward, which in practice is broader real-world coverage than DX9, since DX9-only hardware has aged out.

**The DX9-era detail that does matter, and is not an API question:** Source's formats encode 2004 conventions — DXT1/3/5 texture compression, VMT shader parameters, and a right-handed **Z-up** world space. Direct3D's convention is left-handed Y-up, so a coordinate transform is required no matter which API is chosen. Getting it wrong is the classic "everything renders sideways/mirrored" bug. This is parsing and math work, identical under DX9 or DX11, and it should not be confused with the API decision.

Revisit only if Phase 3.x fidelity work (real models, materials, lightmaps, particles) turns out to be draw-call bound — which would be a measured finding, not an assumption, per D2's standing rule about profiling before reaching for more machinery.

## D11. Demo corpus storage: Git LFS, with the bandwidth tradeoff stated

Decided 2026-08-07 when the corpus went from one 8.9 MB file to three totalling ~131 MB. `z1800.dem` was already committed as a plain blob, so `git lfs migrate import --include="*.dem" --everything` rewrote history — cheap at 6 commits with no remote and no collaborators, painful later. Tag `pre-lfs-backup` marks the pre-migration state; integrity was verified by SHA-256 against the manifest after checkout.

**The reasoning first offered for this was wrong and is recorded so it is not repeated.** The initial argument was git's "every version of every file, forever". Demos are write-once artefacts — a recorded demo is never edited — so there is only ever one version and that multiplication never happens. The owner caught this.

The arguments that actually hold:

- **GitHub refuses any single file over 100 MB in normal git.** This is the decisive one. Our SourceTV demo is 75.6 MB for a 30-minute match; a 40-minute STV demo clears 100 MB and then simply cannot be committed at all. LFS raises the per-file ceiling to 2 GB.
- **Clone cost falls on everyone.** Without LFS a contributor who only wants the C# code still downloads every demo byte. With LFS they get ~130-byte pointers and fetch blobs on demand.
- **The corpus is meant to grow** (D5), and compressed binaries do not delta-compress, so repository size is simply the sum of file sizes.

**The tradeoff pointing the other way, stated because it may reverse this decision later:** on a *public* repository, LFS bandwidth is billed to the repository owner — 1 GB/month on the free tier, paid beyond. Ordinary git clones cost the owner nothing. If this repository ever attracts real traffic, LFS becomes the more expensive option, and the demos should move to release assets or a fetch-on-demand model with checksums in `manifest.json`. That is a volume problem, not a correctness one, and can be decided when it happens.

**Practical consequence:** anyone cloning needs `git lfs install` first, or the `.dem` files arrive as pointer stubs. `git lfs checkout` restores them if that happens — as it did during this migration.

## D12. Property-based testing with CsCheck, alongside the existing gates

Adopted 2026-08-08. CsCheck 4.8.0, Apache-2.0, actively maintained (the NuGet registration index confusingly reports a `3.0.0-rc4` prerelease line as "latest" — 4.8.0 is the current stable).

**The gap it fills is not decoder bugs. It is fixture bugs.** By this point in the project the least reliable part of the test suite was the hand-written fixtures, not the code they tested. Real examples, all found the hard way: a byte-aligned message appended to another so the reader consumed a type field spanning the padding; forgetting that trailing zero padding decodes as `net_NOP`; an expected value computed wrongly by hand; and an assertion (`ShouldNotContain("#     1")`) that could never match anything and so passed regardless.

A round-trip property has no hand-computed expectation to get wrong: encode an arbitrary value, decode it, require what came out to equal what went in.

**Honest limits, measured rather than assumed.** A fault was injected into `VarInt` (6-bit groups instead of 7) to check the properties actually detect faults. They did, shrinking to a minimal case and printing a reproducible seed. But **the existing hand-written tests caught it too**, with 14 failures. For a fault that breaks every value, both approaches work.

The advantage is narrower than the sales pitch: faults that break only *some* values. Hand-written tests check chosen points — 0, 1, 127, 128, 300, `uint.MaxValue`. A bug at exactly 2^28, or in 64-bit values with bits set in both halves, sits between those points and is found by a generator rather than by taste. Shrinking and seed reproduction are real conveniences on top, not the reason.

**How it relates to what already exists**, since four testing mechanisms now sound redundant and are not:

| Mechanism | Question it answers |
|---|---|
| Unit tests | Right answer on input we thought of? |
| **CsCheck properties** | **Right answer across the whole input space?** |
| Stryker (D6) | Would the tests notice if the code were wrong? |
| SharpFuzz (D8) | Does it survive input nobody would write? |
| Corpus tests (D5) | Does it work on bytes TF2 actually produced? |

CsCheck largely supersedes D8's *deterministic* layer, which is a hand-rolled and worse version of the same idea — seeded random buffers with no shrinking, reporting only that one of two thousand cases failed. The coverage-guided layer is unaffected: libFuzzer explores toward new code paths, which property generators do not.

**Scope for now:** `BitReader` and `VarInt` only, as a proof. Extending it to the codecs is worth doing — a generated-schema round trip would have caught the 16-versus-17-bit flags error immediately — but each codec needs an encoder written from the format description, which is real test-only code to maintain. Decide per codec rather than as a sweep.

**One integration note:** SonarAnalyzer does not recognise `Gen.Sample(...)` as an assertion and raises S2699. Suppressed at class scope on property-test classes, with the reason inline, rather than project-wide — S2699 is worth keeping for ordinary tests.

## D13 — the mutation gate runs incrementally during work, fully before a merge

`dotnet stryker` with no arguments mutates everything: roughly 865 mutants, **26 minutes**. That
is the wrong cadence for iterating, and running it repeatedly is how a session ends up with
three contradictory scores in one afternoon.

**During work:**

```bash
dotnet stryker --since:main
```

Only mutates files that differ from `main`, including uncommitted changes. On a clean tree it
tests nothing and reports "unable to calculate a mutation score", which is the correct answer
rather than a failure. The floor is about **7 minutes** — Stryker always builds and runs the
full suite once before it can diff, so this never becomes instant.

**Before a merge:** the full run, once, with nothing else touching the repository.

Two things that cost time before they were understood:

- **The target must be a branch name or a full SHA.** `--since:HEAD~3` fails, and it fails
  *after* doing all the work — fifteen minutes in, at report generation. `stryker-config.json`
  sets `since.target` to `main` so the bare `--since` flag works; Stryker's default target is
  `master`, which does not exist in this repository.
- **Never run the gate against a tree that is still being edited.** Stryker builds and re-runs
  against the working tree, so concurrent edits corrupt coverage collection. Two runs on
  2026-08-08 reported 92.93% and 82.56% for this reason; the second claimed entire files were
  uncovered and finished in eight minutes instead of twenty-six. Both were noise, and one of
  them triggered a full pass of unnecessary work. The undisturbed runs that day were 98.12% and
  99.57%.

**The score is not a target to chase, and 80 is a floor.** `break` stays at 80. Not because the
number is sacred, but because it works in both directions: there is nothing to gain from
driving it higher, and a suite that cannot hold 80 is saying something real about the code
rather than about the threshold. Dropping below it is a smell to investigate, not a setting to
adjust.

Two things not to do, which matter more than the number:

- **Don't trace dead ends.** A survivor that turns out to be equivalent, or in code whose
  behaviour nothing depends on, is finished the moment that is established. Write the one-line
  reason and move on. Chasing it further is time spent proving something already known.
- **Don't write tests for tests' sake.** A test that exists to kill a mutant, rather than to
  pin behaviour someone depends on, is a change-detector — it fails on every future refactor
  and catches nothing. Rewriting production code so a mutant dies is the same mistake wearing a
  disguise; `IsSupported` was rewritten today and was borderline.

This is D6's "read the survivors, not the score", meant literally.


## D14 — the corpus stores one demo per map, not every demo

Nine modern demos.tf files arrived on 2026-08-08 and all parsed end to end. Three were kept and
five were recorded by hash only, in `manifest.json` under `verifiedButNotStored`.

**Why not all of them.** Git LFS on the free tier allows 1 GB of storage and 1 GB of bandwidth
per month. The stored corpus is now ~316 MB; keeping all eight would have reached ~660 MB, and
every fresh clone spends that against the same monthly allowance. Two clones would exceed it.

**Why that costs little.** Test value here is per-map and per-quirk, not per-file. A second
`cp_sunshine` demo from the same platform in the same week exercises the same entity mix, the
same message types and the same schema shape as the first. What earns storage is a new map, a
new era, a new point of view, or a new platform.

**What the recording preserves.** Hash, size and the decode result for each unstored file, so
the claim "these parse" stays auditable and any of them can be re-identified if it surfaces
again. The owner holds the files locally.

This does not relax D5. That gap is **pre-2020 demos**, and none of these are — they are all
modern. Nine modern files decoding proves generality across maps and platforms, and says
nothing about whether the parser handles a 2013 build, because no such file has ever been seen.


## D15 — the mutation gate runs at most once a day, never per change

Owner's call on 2026-08-08, after three consecutive full runs cost about two and a half hours
between them: **stop mutating every time.** Once a day is the cadence. The suite is already
large and still growing, and an hour of waiting between touching code does not work.

**The arithmetic.** A full run is now **43-48 minutes** against 505 tests and ~1,300 mutants,
up from 22 minutes earlier in the same project. It scales with tests times mutants, so every
feature makes it worse. Three runs in one evening produced 92.79% -> 97.34% -> 99.37%, and the
second and third told us progressively less for the same cost.

**What to run instead, during work:**

```bash
dotnet stryker --since:main
```

Per D13. It mutates only what changed, and its floor is about seven minutes because Stryker
always builds and runs the suite once before it can diff.

**When the full run is worth it:** once a day, or before a milestone — not before a merge, and
never repeatedly in one session to watch a number climb. That last one is the trap this decision
exists to prevent. `break` stays at 80 (D13); a full run confirming 99% is not more valuable
than the hour it costs.

### In CI it becomes a schedule, not a trigger

Direction, not yet settled: when this reaches CI the full gate is a **scheduled** job like the
fuzzer — weekly, or daily early in the morning — rather than anything attached to a push or a
pull request. Same reasoning as above, amplified: a 45-minute job on every push is worse in CI
than locally, because it blocks nothing useful and burns runner minutes on a number that moves
slowly.

Exact cadence to be decided when the workflow is written. What is decided is the shape:
scheduled, off the critical path, and reported rather than gating.

### A ceiling worth knowing about

**444 mutants are removed before testing** — Stryker's safe mode drops mutations it cannot
compile, and this codebase is full of `ref struct BitReader` parameters its instrumentation
cannot wrap. That is roughly a quarter of all mutants, concentrated in the decode core.

So the headline score covers **less of the parser than it appears to**. The number is honest
about what it measured and silent about what it could not reach. Treat a high score as evidence
about the tested subset, not about the decoder as a whole — the corpus differential in
`tools/differential/` is what actually covers the decode paths, and it runs in seconds.


## D16 — JSON Lines is the machine-readable output, and the scan is shared

Phase 1 names two outputs: a readable text dump and JSON Lines. Both exist now, and the pairing
is deliberate — the text dump is for a person deciding whether a demo is intact, the JSON Lines
file is for anything that wants to compute over it.

**One object per line, never pretty-printed.** That single rule is the reason to choose the
format: a consumer can `grep` for a player, pipe to `jq`, or stream a 120,000-event demo without
holding any of it in memory. A record split across lines breaks all three at once, so the writer
is configured so it cannot happen rather than merely avoiding it.

**Fields keep their types.** A boolean is `true`, not `"False"` — the latter is a trap in every
language whose truthiness rules differ from C#'s. Numbers are numbers, so a consumer comparing
`damageamount` against a threshold does not parse a string first.

**Event fields keep raw ids rather than resolved names.** The text dump resolves them because a
human reads it; this format is joined against the `player` records instead, which is what a
consumer would want anyway and avoids baking one interpretation into the data.

### The scan is shared, not duplicated

Both writers now use `DemoScan`, which walks the packet stream once. Decoding is the expensive
part of reading a demo, and the writers want different slices of the same messages — scanning
per writer would make cost scale with the number of output formats, which is the wrong thing to
scale with. It was already wrong once inside the text dump, when the player section added a
second pass.


## D17 — SQLite export is removed, not deferred

**Withdrawn entirely on 2026-08-10, at the owner's instruction: "remove sqlite output from the
roadmap completely its not needed idk where it even came from honestly."** It is gone from
`ROADMAP.md` and `managed/README.md`. Nothing was ever built, so there is no code to remove.

**Where it came from, since the owner reasonably did not recognise it:** the original `ROADMAP.md`
draft, written in the planning conversation before any code existed. It was the planner's
suggestion, not a requirement the owner stated, and it survived this long purely because it was
written down — which is the failure mode a roadmap invites. The and/or phrasing ("JSON Lines
and/or a per-demo SQLite file") was doing real work, and nobody chose.

The reasoning below is kept as the record of why it was never built. D18 supersedes the whole
question: the trace is the primary deliverable, and per that decision **the demo is its own best
archive** — a `.dem` is bit-packed, so any derived format is larger than the thing it came from.
Derived formats exist for *reading*, not for keeping.

`ROADMAP.md` §3 lists "JSON Lines **and/or** a per-demo SQLite file". The and/or was always
there; this records the choice.

**Not building it now, because it would buy nothing.** A full demo's JSON Lines output is about
10,000 lines — header, players, chat, events. That is small enough to `grep`, load whole, or
pipe to `jq`. SQLite would hold the same data in a heavier container, and add a dependency and a
schema to keep in step with the decoders for no capability gained.

**The trigger is entity state, not a checklist.** Entity positions per tick are roughly 14.8
million entity updates and 94 million property values per demo. As JSON Lines that is gigabytes,
and answering "where was this player at tick 40,000" means scanning all of it. With an index on
`(tick, entity)` it is immediate — which is precisely what the Phase 2 viewer needs to scrub a
timeline, and the first thing that genuinely cannot be served by a streamed text format.

So the condition is **"something needs random access by tick"**, and the natural moment is when
entity snapshots start being exported at all. Building it before then means maintaining a schema
for data nobody queries.

Worth stating plainly because the pull was real: the roadmap named SQLite, Phase 1 felt
incomplete without it, and "the doc says so" is not a reason to build something.


## D18 — the primary output is a Quake-style trace, not a summary

**Correction to D16 and to the scaffold's framing.** `ROADMAP.md` §3 said "a Quake-style
readable text dump, plus JSON Lines and/or a per-demo SQLite file", and the JSON and SQLite half
of that came from the pre-code planning conversation rather than from a stated need. Working
through it as a checklist produced a summary dump and a JSON Lines writer before anyone asked
what the output was actually for.

**What was actually wanted:** the output a Quake demo parser produces, with TF2 content.

### What that format is

`lmpc` — the Quake tool that decompiles a `.dem` to text *and compiles it back* — writes
block-structured source. Its decompiler emits keywords (`block`, `time`, `print`, `stufftext`,
`setangle`, `serverinfo`, `spawnbaseline`, `temp_entity`, …) with `{`, `}` and `;` from its
grammar. So a decompiled demo is a linear stream of blocks, each holding that frame's messages,
each message a keyword with fields.

`DemoTraceWriter` produces the same shape with Source names:

```
block dem_packet tick 14 {
    net_tick tick 12742 frametime 0.015000;
    svc_updatestringtable;
    svc_packetentities delta 1 updated 27 bits 1566;
}
```

### Why a trace beats a summary here

**Aggregates hide position, and position is the whole point when a demo is damaged.** "3,412
game events" says nothing about where a stream stopped making sense; a block ending in `stopped
after N bits` says exactly. So anything the reader cannot finish is reported **in place** rather
than omitted, and commands carrying no messages still get a block — a trace that skipped what it
could not read would describe a healthier file than the one on disk.

### On archiving, which is where JSON and SQLite really came from

**The demo is its own best archive.** A `.dem` is bit-packed; any text or JSON expansion of the
same content is larger, and complete entity state as JSON Lines would be gigabytes against a
39 MB demo. Nothing here should be built on the premise that a derived format preserves a demo
better than the demo does. What derived formats are for is *reading* — by a person, or by a
tool — not keeping.

The summary dump and the JSON Lines writer both stay: the summary answers "is this demo intact"
at a glance, and JSON Lines is a reasonable machine format for tools that want one. Neither is
the primary deliverable.


## D19 — old demos need old binaries, not old source

Researched 2026-08-09, after the idea came up of building TF2 from source to record a
period-correct demo and close D5's corpus gap.

**It does not work, and the reason is worth keeping** so nobody spends a weekend on it.

### The protocol history

| Era | Engine branch | Network protocol |
|---|---|---|
| 2007–2009 | Source 2007 | pre-15 |
| 2009–2011 | Source 2009 | 15–16 |
| Oct 2011–2013 | Source MP | 18–23 |
| 2013–present | Source 2013 MP, then the TF2 branch | **24** |

Every demo in the corpus is protocol 24. The untested range is 15–23 and earlier.

### Why source does not help

Valve released TF2's client and server code officially on **18 February 2025**, explicitly so
modders need not use leaked material. That release is **TF2-branch code** — building it produces
a modern client, which records protocol 24 demos. The same is true of Source SDK 2013.

The 2020 leak is no better: it is 2017–2018 code, also Source 2013 era, also protocol 24. So the
leaked dump does not solve this problem either, quite apart from being material this project has
no business sourcing.

**Valve has never released engine source for the 2007, 2009 or MP branches.** There is no build
path to those protocols, from official code or otherwise.

### What would actually work

An old **binary**: an archived TF2 client from the relevant era, or an old depot manifest via
Steam's `download_depot`. Recording a demo then needs only a client that runs, not code that
compiles.

### The consolation

The four protocol-conditional rules in the message layer (see `SPEC.md`) sit exactly on these
era boundaries: the replay flag at >15, the 16-byte map hash at >17, `svc_Prefetch`'s width at
>22, varint lengths at >23. **All four are implemented.** So the parser is built for those eras
and merely untested against them — which is a different and much better position than being
unprepared.


### D19 addendum — a concrete route to a 2009 client exists, and is parked

Two archive.org leads were checked on 2026-08-09.

**The retail DVD (`tf2-2009`) is the wrong shape.** It is a Russian retail disc: `Setup.exe`
plus `.sid`/`.sim`/`.sis` Steam Installation Data. Those packages are Steam-encrypted, and the
installer hands them to Steam as a local cache before Steam authenticates and **updates to
current**. The disc saves a download; it does not pin a version. Running it produces modern TF2.

**The Steam 2 depot archive is the right shape.** `steam2dats-part1` holds pre-SteamPipe depot
data dated 2004–2009, including:

| Depot | Contents | Size |
|---|---|---|
| 441 | Team Fortress 2 Content | 4.05 GB |
| 442 | Team Fortress 2 Materials | 628 MB |
| 443 | Team Fortress 2, further content | 197 MB |
| 217 | Multiplayer Orange Box Binaries — the engine | — |

That is a 2009 client in extractable form rather than encrypted retail packages.

**Parked, on cost.** Assembling it needs the depot chunks, the ~30 GB of Steam content blobs in
`steam2dats-meta`, and Steam 2-era extraction tooling this project has no familiarity with. It
is a preservation exercise in its own right, measured in days, with no guarantee of a client
that launches offline.

**Recorded because the position improved.** D19 previously said no build path existed to
protocols 15–23. That is still true of *source*, but a path to a period-correct *binary* now has
a name and a location. If closing the era axis ever becomes the priority, this is where to start
rather than from nothing.


### D19 second addendum — a prebuilt 2009 client exists, and closing the era axis is the priority

**Priority correction, owner-stated 2026-08-09:** closing the era axis *is* the point of the
project. Earlier notes here treated it as a background concern to be picked up if convenient,
and ranked parser work above it. That was wrong and is corrected: work that gets a
pre-2013 demo into the corpus outranks feature work on the parser.

**And it turns out to be cheap.** `archive.org/details/team-fortress-2-3862` is Team Fortress 2
build **3862, dated 4 June 2009** — "fully functional, uses the original .GCF contents" — as a
single 3.9 GB zip. Its directory was verified before downloading, by range-requesting the zip's
central directory and reading the file list:

```
team fortress 2/hl2.exe              launcher
team fortress 2/bin/engine.dll       the 2009 engine
team fortress 2/tf/bin/client.dll    game DLL, and therefore the 2009 SendTables
team fortress 2/tf/maps/             500 entries
```

A complete extracted install. **No depot extraction, no Steam 2 blobs, no retail decryption** —
the route recorded in the first addendum is superseded and should not be attempted.

**Why the game DLL is the part that matters.** `client.dll` and `server.dll` define the `DT_`
send tables, so they determine the entity schema and the protocol a recording announces. Old
binaries with modern content would still record an old-protocol demo; old binaries are the whole
requirement.

**Recording it:** launch `hl2.exe -game tf -insecure -novid -console`, then `map cp_dustbowl`
and `record`. Two likely obstacles, both ordinary: the archive carries no `steam_appid.txt`, so
one containing `440` may be needed beside `hl2.exe`; and a 2009 engine expects 2009 Steam API
interfaces, for which Steam in offline mode is the usual answer. Extract outside the Steam
library so nothing tries to update it.

**What it would settle.** A protocol 15 demo exercises all four protocol-conditional rules on
their old side simultaneously — every one currently implemented from reading the reference
parser and never executed against real data. See `SPEC.md` and the alignment tests in
`OldProtocolTests`, which prove internal consistency and nothing more.


### D20 — the protocol boundary list comes from Valve, and it is longer than this parser implements

Until 2026-08-09 this project treated its four protocol-conditional rules as the complete set,
inferred from reading `demostf/parser`. They are not the complete set, and the authoritative list
was available the whole time: **`common/proto_version.h`**, still shipped in the current TF2 SDK
(`alliedmodders/hl2sdk`, branch `tf2`) precisely because the live engine still reads old demos.

Every constant in it is a demo-backward-compatibility boundary, annotated with what changed:

| Constant | Annotation | Implemented here |
|---|---|---|
| `PROTOCOL_VERSION 24` | current | — |
| `PROTOCOL_VERSION_23` | `NET_MAX_PAYLOAD_BITS` went away | yes — varint table lengths |
| `PROTOCOL_VERSION_22` | sound index bits used to = 13 | yes — `svc_Prefetch` width |
| `PROTOCOL_VERSION_21` | before the special DSP shipped | no |
| `PROTOCOL_VERSION_20` | old-style dynamic model loading | no |
| `PROTOCOL_VERSION_19` | post-Halloween sound flag extra bit | no |
| `PROTOCOL_VERSION_18` | pre-Halloween sound flag extra bit | no |
| `PROTOCOL_VERSION_17` | MD5 in map version | yes — 16-byte hash vs 4-byte CRC |
| `PROTOCOL_VERSION_REPLAY 16` | replay shipped to public | yes — `svc_ServerInfo` replay flag |
| `PROTOCOL_VERSION_14` | create string tables compression flag | **yes, added on discovering this file** |
| `PROTOCOL_VERSION_12` | (unlabelled) | no |

**Read the convention before using the table.** Each constant names the last build *without* the
change, not the build that introduced it: `PROTOCOL_VERSION_17` is "MD5 in map version" and the
MD5 appears at 18. The four pre-existing rules independently confirm this reading, which is what
makes the entries for 14 and 12 usable rather than ambiguous.

**Why 14 was fixed immediately and the others were not.** String tables are load-bearing —
`svc_CreateStringTable` is not skippable, and reading a flag bit that was never sent shifts every
table and everything behind it. And it is on the era axis rather than hypothetical: **TF2 shipped
on the Orange Box engine in October 2007, which is pre-15**, so TF2's own 2007–2008 demos have no
flag there. The remaining unimplemented boundaries (21, 20, 19, 18) are all sound-related, in
messages this parser steps over rather than interprets, so they cost nothing until sounds are
decoded. 12 is unlabelled and needs its own investigation.

**Confirmed against a real client the same day.** The 2009 build recorded per D19 reports:

```
Protocol version 15
Exe version 1.0.5.9 (tf)
Exe build: 13:52:56 Jun  4 2009 (3862) (0)
```

Protocol 15 sits above the compression-flag boundary and below the other four, so a demo from it
exercises the replay flag, the CRC, the prefetch width and the fixed table lengths **all at once**
— every rule this project had written from reading someone else's parser and never executed.
It does not reach the 14 boundary; that one stays theoretical until a 2007–2008 demo turns up.

**Standing consequence:** when a protocol-conditional rule is needed, check `proto_version.h`
first. It is a short file, it is authoritative, and it enumerates the boundaries rather than
leaving them to be discovered one desynchronisation at a time.


### D21 — the era boundaries stay open, and a demo is cheaper than more research

After B17 and B18 both landed with an unverified boundary somewhere in protocols 16–23, a pass
was made at pinning them from changelogs, SDK branches and Valve documentation. Recording what
it produced, because the negative result is worth as much as the positive one.

**What worked: bracketing `DPT_VectorXY` across SDK branches.** `alliedmodders/hl2sdk` keeps one
branch per game, each frozen at that game's release, so the presence of a symbol dates it:

| Branch | Era | `DPT_VectorXY` |
|---|---|---|
| `episode1` | 2006 | no |
| `orangebox` | 2007–2009 | **no** |
| `l4d` | Nov 2008 | yes |
| `l4d2`, `swarm`, `portal2`, `csgo`, `sdk2013`, `tf2` | 2009+ | yes |

**This shows the change is engine-branch-driven, not TF2-version-driven.** `VectorXY` entered the
engine line with Left 4 Dead in late 2008, yet the June 2009 TF2 demo does not have it — because
TF2 was still on the Orange Box branch and did not inherit it until it was moved onto a newer
one. So the TF2 protocol boundary is whenever *TF2 changed engine branch*, which is not the same
question as when the feature was written, and is why searching for the feature's introduction
date cannot answer it.

**What did not work.** The Valve Developer Community sits behind a proof-of-work anti-bot
challenge that plain fetching cannot read, and the pages that would carry a protocol table
(`DEM (Source 1)`) are empty. TF2's patch notes do document protocol bumps — the 119th update
retrospective states "Added backward compatibility code to allow demos recorded with protocol 12
to continue to be playable under protocol version 13" — which proves the changelog route is
viable in principle, but no note covering the 15→24 span surfaced in this pass.

**Decision: leave both boundaries open and stop looking.** Justified by the failure mode rather
than by the difficulty. A wrong boundary on either rule wrecks the decode immediately and
visibly — 11,002 unreadable packets, a schema that dies mid-payload — so a protocol 16–23 demo
would announce the correct answer within seconds of being added, and until one exists neither
boundary can be wrong in a way that matters.

**Consequence for corpus priority.** The most valuable acquisition is no longer "anything older"
but specifically **one demo in protocols 16–23**, which would settle B17 and B18 together. A
2007–2008 launch-era client (protocol 14) remains valuable for a different reason: it is the only
thing that would exercise the string table compression rule from D20.

#### D21 outcome, 2026-08-10 — the demo was cheaper, and it settled both

A June 2011 client (build 4604) recording at **protocol 16** was obtained and settled B17 and B18
together, exactly as this entry predicted, within minutes of being decoded. Six-bit message type
and current `SendPropType` numbering, both at 16, so **both boundaries are 15→16** — the value the
code had guessed. The research pass that preceded this produced no answer in a day; the demo
produced two in seconds.

**The general lesson is the one this entry already stated, now with a measurement behind it: for a
rule whose failure is loud, a specimen beats research.** Not because research is useless — the SDK
branch bracketing above is still the reason we understand *why* the boundary is where it is — but
because a specimen answers the question the parser actually asks.

**One of this entry's own quotes turned out to be evidence.** It cites a TF2 patch note about
"backward compatibility code to allow demos recorded with protocol 12 to continue to be playable
under protocol version 13", noted at the time only as proof the changelog route was viable. The
launch client turned out to record at **protocol 11**, not the 14 assumed here, so protocols 12
and 13 sit in a five-month window between October 2007 and March 2008 — and that patch note is
independent confirmation that both existed and are consecutive. It was in hand before the demo
was, and was not recognised as an answer because it was being read for a different question.

**Corpus priority now:** protocols **12–13** (Oct 2007 → Mar 2008) and **17–23** (Jun 2011 → Mar
2013). Both windows are narrow enough that `bin/engine.dll` dating (D30) makes candidate triage
cheap.


### D22 — the trace reaches the command line, and the CLI gets its own tests

`DemoTraceWriter` had been the primary output since D18 and was unreachable from the tool: the
CLI only offered the summary dump. Fixed — `-t` traces, `-j` writes JSON Lines, `-e` and
`--entity-limit` control entity expansion.

**Argument parsing moved out of `Program` into `CommandLine`, and the CLI got a test project.**
It had none. That is the wrong surface to leave untested: parsing is where a command-line
program is quietly wrong most easily. An option that consumes the wrong number of arguments
shifts everything behind it, and a flag that silently loses to another produces the wrong output
with no complaint — neither is reachable from a test that only inspects a successful run's
output. Both are now pinned, and both were verified by sabotage rather than by having been
written down: removing the cursor advance after `-o` fails exactly one test, and removing the
`--entity-limit` implication fails exactly one other.

**`--entity-limit` implies `-e`.** Asking for a limit without asking for entities describes a
run that would do nothing with either, so reading it as a request for both is the interpretation
that cannot be a mistake.

**Progress redraws on change, not on a clock.** A 120,000-command demo reports progress hundreds
of times per visible percentage point. A time-based throttle would make the output depend on
machine speed — the same demo producing different bytes on different runs — which is precisely
what makes a test unfalsifiable. Two reports that render identically are indistinguishable to a
reader, so dropping the second costs nothing and keeps the behaviour deterministic and testable.
The bar draws only to standard error and only when it is not redirected, because standard output
may be where the trace is going.


### D15 addendum — the mutation run is now 1h29m, not 26 minutes

D15 recorded a full run at roughly 865 mutants and **26 minutes**. Measured again on 2026-08-09:
**1 hour 29 minutes**. The suite has grown by hundreds of tests and the corpus from three demos
to eight, and corpus tests dominate — every mutant re-runs them.

That changes the cadence advice rather than the decision. Once a day was already the rule; at
ninety minutes it is firmly a background or overnight job, and `--since:main` during work is no
longer a nicety. Whatever CI schedule this lands on should assume an hour and a half, not half
an hour, and it should not gate a push.

Worth noting for whoever tunes it: the cost is corpus tests, so `mutate` globs narrowed to the
file under change would cut it sharply — but see `tests/STRYKER-NOTES.md`, those globs are
project-relative and a wrong one reports a clean run rather than an error.


### D23 — corpus work is cached per process, and the measurement that found it

The core suite ran in 36 seconds and a full mutation run took 1h29m. Both were dominated by the
same thing: **tests re-deriving the same facts from the same eight demos, over and over.**

Three caches now live in `Corpus`, keyed by demo path: the parsed header, the parsed schema, the
`userinfo` roster, and the first 400 entity-snapshot headers. Suite time went **36s to 13s**, and
the whole solution now runs in under 19 seconds.

**The mutation-run saving is larger than the suite saving, and that is the actual point.** A
static cache lives for the life of a test host, and Stryker reuses a host across many mutants —
so the first mutant pays for the walk and the rest are free. The 36-to-13 figure understates it.

**How it was found, because the first attempt was wrong and cost an hour.** Timing each test
class separately said schema parsing dominated, so schemas were cached first — and the suite went
from 36s to 34s. That inference was wrong twice over: each per-class run carried about two
seconds of host startup, and xUnit runs classes in parallel, so a sum of per-class times says
nothing about wall clock. Per-test durations from the detailed logger showed the real shape in
one command: four tests in `CorpusPlayerTests` at 6–12 seconds each, all rebuilding the same
roster, and tests within a class run *sequentially* so they did not even overlap.

**Lesson worth keeping: measure the thing you are going to change, at the granularity you are
going to change it.** Per-class timing was the wrong instrument for a per-test problem.

**What is deliberately not cached.** Demo bytes — the corpus was 305 MB when this was written (it is now 20 MB, D31) and Stryker runs several
hosts at once, which would trade a time problem for a worse memory one. `EntityDecoder` — it is
stateful by design, since a delta update's class comes from the snapshot the entity entered on,
so a shared one would let one test's entities answer another's questions. And snapshot *bodies*,
which are `ReadOnlyMemory` views over the demo bytes and would pin the whole corpus by the back door;
only the scalar header fields are kept.

**A cap with teeth.** `FirstSnapshots` holds 400 per demo and *throws* if asked for more, rather
than quietly returning a short list. The tests that use it now assert the result is non-empty
per demo as well — B20 was precisely a helper that silently yielded nothing while every test
built on it kept passing, and a cache is a new place for that to happen.

### D24 — a faster suite recalibrated the mutation tool, and timeouts score as kills

Two findings from the 2026-08-09 mutation work, sharing one theme: **the number moved for a
reason that was not quality.**

#### Timeouts count toward the score

Verified by arithmetic rather than assumed, against the complete run of 2026-08-09 04:54:

```
Killed 662, Timeout 134, Survived 59, NoCoverage 76   -> Stryker reported 85.50%
(662 + 134) / (662 + 134 + 59 + 76) = 796 / 931 = 85.50%   correct
```

The formula is `(Killed + Timeout) / (Killed + Timeout + Survived + NoCoverage)`. A timeout sits
in the numerator beside a real kill, and a timeout is **never** evidence a test detected
anything — no assertion failed, something merely failed to finish.

| | score |
|---|---|
| headline | **85.50%** |
| floor, timeouts counted against | **71.11%** |

Three files whose perfect score is mostly unknowns:

| file | killed | timeout | headline | floor |
|---|---|---|---|---|
| `ChatMessage.cs` | 9 | 25 | **100.0%** | 26.5% |
| `NetMessageReader.cs` | 10 | 20 | **100.0%** | 33.3% |
| `StringTableCodec.cs` | 33 | 32 | **100.0%** | 50.8% |

#### Those timeouts are artifacts, not hangs

Classified by mutation kind. Genuine infinite loops would cluster in loop conditions; these do
not. `ChatMessage`'s land on guard clauses and ternaries — `if (body.Length <= HeaderBytes)`,
`if (end < 0)` — which cannot loop. Twelve of twenty-five are equality mutations on branch
conditions.

So the timeout status is **measurement noise**: each of those mutants is really a Killed or a
Survived that went unobserved. The true score is somewhere in 71-86% and is currently unknown.

#### The mechanism: our own optimisation moved a measurement

Stryker records a **per-test** baseline. During the baseline run the first test pays the cold
corpus walk and every later test is fast, so most tests carry a small recorded time. In a mutant
host — fresh, cold cache — whichever test runs first pays that walk, and if its baseline was
small it is recorded as a timeout.

Evidence: the timeout count doubled, 63 to 128, across exactly the corpus-caching commit (D23),
concentrated in the files corpus tests walk.

**The generalisation worth keeping:** the optimisation broke nothing. It recalibrated a tool that
derives thresholds from observed timing. Same family as the VsTest false-green recorded in
`tests/STRYKER-NOTES.md`.

#### Rules adopted

1. **Report two numbers** — headline and floor. When they bracket a wide range, the true figure
   is unknown whatever the headline says.
2. **Never tune `additional-timeout` to move a score.** Raise it once to learn what the timeouts
   resolve to, then lower it again. Measured cost of 5s to 30s: 25 extra seconds per hanging
   mutant, roughly 10-15 minutes of wall clock here.
3. **Judge a timeout change by what the timeouts became**, not by the count. A `Timeout ->
   Survived` is a real gap the short threshold had been scoring as a kill.
4. **Prefer fixing the cause to widening the window.** Warming the corpus cache outside any
   test's timer — a module initializer or assembly fixture — makes the cost independent of which
   test runs first, which is the actual defect.

#### Checked and cleared

Does caching blunt mutation detection, given only the first test executes a cached computation?
No. The mutation is baked *into* the cached value, so downstream assertions still fail on wrong
data, and `GetOrAdd` does not cache exceptions.

### D15 second addendum — cadence, and why this project is the slow one

Every other project the owner mutates finishes in 10-20 minutes. This one exceeds 90.

**The corpus is the entire difference.** Others mutate against small synthetic fixtures; this one
carried 305 MB of real demos when this was written, and its strongest tests are corpus tests, so every mutant touching
decode code re-walks eight files.

**A trade, not waste.** Those tests found B12, B17, B18 and B20. Removing them would match the
other projects' runtime and stop catching the defect class this project exists for.

- **During work:** `--since:main`.
- **Full run:** weekly or pre-milestone, overnight, never gating a push.
- **CLI project:** 52 seconds. Run freely.

### D25 — the test project splits along the pure/stateful seam

Owner decision, 2026-08-09. The seam already exists in the layout: eleven corpus-backed files
(`Corpus*Tests.cs` plus `Corpus.cs`), everything else synthetic. Two stragglers mix them —
`Differential/HeaderDifferentialTests.cs`, and the single corpus test in
`Text/DemoJsonLinesWriterTests.cs`.

| Project | Contents | Cadence |
|---|---|---|
| `Tf2DemoSalvage.Core.Tests` | synthetic fixtures only | daily, minutes |
| `Tf2DemoSalvage.Corpus.Tests` | the eleven corpus-backed files | weekly, overnight |

Shared helpers (`BitWriter`, `GameEventFixtures`) move to a `TestSupport` project referenced by
both and never mutated.

**The daily run must scope `mutate` to the pure files.** Otherwise every mutant reachable only
through corpus tests reports `NoCoverage`, which counts against the score, producing a
meaningless low number whose obvious "fix" is lowering the threshold.

Candidate pure set: `Primitives/*`, `SchemaFlattener`, `SendPropDecoder`, `SendTableParser`,
`PropertyValue`, `DemoHeader`. Stateful, therefore weekly: `NetDecodeState`, `NetMessageReader`,
`StringTableCodec`, `EntityDecoder`, `EntityTracker`, all of `Text/`.

**The guard, and the first version of it was wrong.** "Assert NoCoverage is near zero" cannot
detect a bad glob: an unmatched file generates no mutants at all, so it never enters any status
bucket and NoCoverage stays zero *because* the file was skipped. Caught by an outside reviewer.

The working guard **asserts the set of files in the report equals the expected pure set**.
Stryker lists only what it mutated, so a missing entry names the offending glob. A mutant count
also works, but cannot say which of six globs was wrong.

Failure modes are asymmetric, which is why a guard is needed: every glob wrong yields zero
mutants and a loud error; *one* glob wrong yields a real run and a plausible percentage covering
five-sixths of the intended set.

#### D24 correction — a module initializer is not a valid warmup hook

Rule 4 above suggested warming the corpus cache in "a module initializer or assembly fixture".
The module initializer half was tried on 2026-08-09 and **is wrong**:

```
[xUnit.net 00:01:15] Catastrophic failure:
System.InvalidOperationException: Test process did not respond within 60 seconds
```

A module initializer runs at **assembly load**, which is before the test host completes its
startup handshake with the runner. That handshake has a hard 60-second limit. Expensive work
there kills the host before a single test runs — and it runs again on every load, including
discovery.

A second thing the attempt measured, which is more useful than the failure: **eagerly warming
everything took over 60 seconds, against a 13-second suite.** Warming all four caches for all
eight demos does far more work than the tests ever ask for, because the lazy caches only compute
what something actually requests. That is evidence for attacking the walk's cost rather than its
placement.

Remaining options, in preference order:

1. **Make the walk cheaper.** `NetMessageReader` fully decodes every string table it passes,
   including `modelprecache` and `soundprecache` with thousands of entries, when a roster walk
   needs only `userinfo`. Each table carries a length prefix, so its body can be stepped over
   without being decoded. This must be **opt-in per caller** — a default that narrows what a walk
   sees is how B20, B21 and B22 all happened, and B22 shows `instancebaseline` is needed too.
2. **An xunit assembly fixture**, which at least runs after the handshake. Its cost is likely
   attributed to the first test, so it may not solve the per-test baseline problem at all —
   measure before adopting.
3. **Accept it and keep `additional-timeout` raised**, paying roughly 25 seconds per hanging
   mutant, and always reading the floor alongside the headline.

### D26 — CI runs mutation and fuzzing on separate schedules, and LFS bandwidth shapes both

Two workflows, deliberately separate so each can be run independently and so the dashboard names
which one failed: `.github/workflows/mutation.yml` and `.github/workflows/fuzz.yml`.

#### The binding constraint is Git LFS bandwidth, not Actions minutes

The repository is public, so Actions minutes are free and unlimited. **Git LFS is not.** The free
tier allows 1 GiB of bandwidth per month for the account, and the corpus was 305 MB — so an
uncached checkout exhausts the quota in **three runs**, whatever the repo's visibility.

Both workflows are built around that:

- `actions/checkout` runs with `lfs: false`, and the objects are fetched separately behind an
  `actions/cache` keyed on the LFS object ids. The corpus changes rarely, so after the first run
  this costs nothing.
- The **fuzz workflow never fetches the corpus at all.** Fuzzing builds Core and the harness and
  reads no demos, so it spends none of the quota and can run nightly.
- The mutation workflow runs its two jobs in sequence rather than in parallel — CLI first, which
  takes about a minute and populates the shared cache, then Core. Run in parallel they would both
  miss the cache and pull the whole corpus each.

#### Cadence follows measured cost, not preference

| Workflow | Schedule | Why |
|---|---|---|
| Mutation | weekly, Sunday 06:00 UTC | 1.5-2 hours locally, slower on a hosted runner (D15) |
| Fuzz | nightly, 05:00 UTC, 120s per target | short by design, see below |

**The fuzz job is deliberately short**, and that follows `docs/FUZZING.md` rather than caution:
the current targets saturate after roughly 15,000 executions — `ft:` stops moving and the
remaining ~790,000 executions discover nothing. A long nightly run against `BitReader` or
`VarInt` would burn hours to learn nothing. It is a regression guard today, and `MAX_TOTAL_TIME`
should rise when targets #3 and #4 land (string-table and SendTable delta decode, then whole-file
parse), which have enough distinct paths to justify it.

#### Both workflows assert that their inputs actually arrived

Three checks exist because the corresponding failures are all silent:

1. **The corpus really downloaded.** An LFS pointer stub is ~130 bytes; if the demos do not
   arrive, every corpus test degrades to a no-op and passes. The workflow asserts the smallest
   `.dem` exceeds 4 KB. This is RISKS B20 as a CI step.
2. **Instrumentation really happened.** `sharpfuzz` rewriting `Tf2DemoSalvage.Core.dll` must grow
   the file. An un-instrumented fuzz run explores nothing and looks exactly like a clean one.
3. **Artifacts really uploaded.** `if-no-files-found: error`, not the default warning — an upload
   that finds nothing is precisely how a workflow keeps a green tick after it has stopped
   producing anything.

#### Still to verify on the first real run

Per the standing rule that a green tick is not a clean run, read the **annotations** of the first
run of each workflow, not just the status:

```bash
gh api repos/{owner}/{repo}/check-runs/{job-id}/annotations --jq 'length'
```

Specifically unverified until then: whether a hosted 4-core runner finishes the core mutation
inside the 300-minute timeout, and whether `libfuzzer-dotnet` builds cleanly against the runner's
clang.

### D27 — entity baselines, and what they are not wired to yet

`instancebaseline` is now read, stored and applied. An entering entity is seeded from its class
baseline before the update's own properties, so defaults that never reach the wire are known.

**Measured effect, over the same 300 snapshots per demo:**

| demo | properties known without | with baselines |
|---|---|---|
| `z1800` | 3,369 | **49,452** |
| `demostf-cp_process_f12` | 4,624 | 46,050 |
| `tf2-2009-build3862` | 531 | 6,566 |

Roughly **ninety percent of entity state was missing**, which is what the gap recorded on
`EntityTracker` since it was written actually amounted to.

#### Three rules, each verified by sabotage rather than asserted

1. **Seed on entry only, never on a delta.** Re-applying a baseline on every update resurrects
   defaults over values the match already changed — a player's health springing back to full
   because a later tick touched only their position. Inverting the condition fails exactly the
   two baseline tests.
2. **The class id is the entry's *text*, not its index.** This is the reverse of `userinfo`,
   where B22 established the entity index *is* the entry index. For `instancebaseline` the two
   differ on essentially every entry — index 0 carries class 353, index 1 carries class 318.
   Reusing B22's rule files every baseline under the wrong class, and a baseline on the wrong
   class still decodes: real values into the wrong fields, silently.
3. **Drop the decoded copy when the raw bits are rewritten.** Baselines change mid-match through
   `svc_UpdateStringTable` — 101 times in one corpus demo — so a memo kept against a class id
   would serve a stale parse for the rest of the file. Removing the invalidation fails exactly
   the rewrite test, which reads before *and* after precisely so a never-invalidated cache
   cannot pass.

#### Stored raw, decoded on demand

A demo carries a baseline for every class while a match instantiates only some, and one in the
corpus runs to 7,669 bytes. The reference implementation makes the same choice for the same
reason. No new codec was needed: a baseline is encoded exactly like an entity delta, so the
existing property loop reads it.

#### What this is not wired to, and why that is deliberate

**Nothing in production consumes it yet.** The trace prints *deltas* — what changed each tick,
in stream order — which is what a trace is for, and baselines do not belong there. `EntityTracker`
plus baselines is Phase 2 infrastructure: the 2D viewer is exactly a query for every player's
position at an arbitrary tick, and that is the consumer this was built for.

Inventing a consumer now to make the wiring look finished would add output nobody asked for and
inflate the trace tenfold. The honest state is: the capability exists, is tested against every
corpus demo, and waits for Phase 2.

### D25 outcome — the split landed, and it settled the glob question by measurement

Done 2026-08-10. Cleaner than planned: **no shared `TestSupport` project was needed**, because no
corpus test uses `BitWriter` or `GameEventFixtures`. The seam really was already there. Only two
files mixed the two kinds, exactly as predicted, and `ReferenceParser` came along with the
differential test.

| project | tests | wall clock |
|---|---|---|
| `Tf2DemoSalvage.Core.Tests` (synthetic) | 558 | **274 ms** |
| `Tf2DemoSalvage.Corpus.Tests` | 62 | 12 s |
| `Tf2DemoSalvage.Cli.Tests` | 51 | 7 s |

#### Mutation on the fast project: 1h29m to 3m55s

```
Killed 808   Survived 51   Timeout 8   NoCoverage 100   ->  84.38 %
```

**The measurement got better, not just faster.** Timeouts fell from 134 to 8, which confirms D24's
diagnosis outright: they were cold-corpus-walk artifacts, and the synthetic project never walks a
demo. The honest floor is now `808 / 967 = 83.6%`, so the band between headline and floor
collapsed from **71–86%** to **83.6–84.4%**. That is the difference between a number worth acting
on and a number worth arguing about.

#### Correction: no `mutate` globs, contrary to the plan

D25 said the daily run must scope `mutate` to the pure files, or corpus-only code would report
`NoCoverage` and produce a meaningless low score. Measured, that fear was overstated: 100
NoCoverage mutants out of 967, and the score still clears the threshold.

And nearly all of it is the output writers, not decode logic:

```
47  DemoJsonLinesWriter.cs      24  DemoTraceWriter.cs      15  DemoTextDumper.cs
 7  NetDecodeState.cs            3  RosterBuilder.cs         3  DemoScan.cs
```

Those are covered by the weekly corpus run, which is where they belong.

**So no globs are configured.** The reasoning is asymmetric risk: a wrong glob fails *silently*,
measuring a fraction of the intended set while reporting a plausible percentage (see
`tests/STRYKER-NOTES.md`). Six globs is six chances to write one repo-relative out of habit, and
the entire prize is recovering ten percent of a score that already passes. Not worth it.

The file-set guard designed for those globs is therefore not needed either. Recorded rather than
deleted, because the reasoning behind it — that a NoCoverage check cannot detect a missing file —
stays true if globs are ever added.

#### Cadence, now that the numbers are known

| run | cost | cadence |
|---|---|---|
| Core.Tests mutation | 4 min | every change, freely |
| Cli.Tests mutation | 1 min | every change, freely |
| Corpus.Tests mutation | hours | weekly, CI only |

### D28 — user messages are named, not decoded, and the name table is generated

`svc_UserMessage` used to vanish into an anonymous `SkippedMessage` — 106 of them in a single
2009 demo. They now report their type and its registered name.

**Named rather than decoded, deliberately.** Each of the 79 types has its own body layout defined
by the game DLL, so decoding them all is 79 separate formats. Naming the type is most of the
readability for a fraction of the work, and it turns one anonymous count into 79 individually
addressable items — any of which can be decoded later when something actually needs it.

**The table is generated from `game/shared/tf/tf_usermessages.cpp` in the TF2 SDK, not recalled.**
A user message carries no name on the wire, only an id, which is that file's registration order —
so a wrong table renames every message in a trace while failing nothing.

#### Two independent cross-checks, because the table could not be diffed across eras

The 2009 SDK ships no TF2 game code, so the old registration order cannot be read from source.
Both checks are behavioural instead:

1. **`SayText2` lands at index 4**, matching a constant proven against real chat in real demos
   long before this table existed.
2. **Point-of-view demos carry `Damage`, `Rumble` and `VoiceSubtitle`; SourceTV demos carry
   none of them.** Those go to the local player, so the split is exactly what the game would
   produce — and a misaligned table would not reproduce it.

**And the second check holds across eras**, which is what settles the era question: the 2009 POV
demo shows `Damage` and `Rumble`, the 2020 POV demo shows them, and neither SourceTV demo does.
The 2009 demo also uses **only ids 0–28**, with no MvM messages — those start at 55 and MvM
shipped in 2012. So the low range has been append-only, and the table applies to both eras.

That is evidence rather than proof. If a message was ever *inserted* rather than appended, ids
after the insertion shift — the same trap as the property-type renumbering in RISKS B18. Ids past
the end of the table are reported by number with no name, so an unknown one is visible rather
than mislabelled.

#### One oddity, recorded rather than explained

Every modern corpus demo shows a dozen or so `MVMResetPlayerStats` (id 57) in ordinary
competitive matches, and the 2009 demo shows `Geiger` and `Train` — both HL2 leftovers TF2 has
no obvious use for. Either those ids mean something else in the builds that recorded these
demos, or the game really does send them. Not resolved, and not blocking: the ids are reported
alongside the names, so a reader can see the number that was actually on the wire.

### D29 — mutation testing moves to the shared Oracle box, and the corpus job is why

GitHub-hosted runners cannot finish this repo's slow mutation job. Measured 2026-08-10: the
pre-split combined project took **1h29m locally** and was **still running at 4h36m** on a hosted
4-core runner before its 300-minute timeout killed it with no report. **A hosted runner is
roughly 3× slower than the owner's machine on this workload.**

That is survivable for the fast projects and not for the corpus one, which is the whole reason
the measurement boxes exist.

#### The split across machines

| Workload | Where | Cadence | Cost |
|---|---|---|---|
| `Core.Tests` (synthetic) | GitHub Actions | daily | ~4 min local, ~12 min hosted |
| `Cli.Tests` | GitHub Actions | daily | ~1 min local |
| `Corpus.Tests` | **`mutation-box`** | weekly | 1h25m local, est. 3–5h on the box |
| Fuzzing | GitHub Actions | nightly, 120s/target | trivial |

#### What was provisioned, and the one thing that was missing

`~/tf2demosalvage` on `mutation-box`, with `build/run-measurements.sh` modelled on
PokemonBattleJournal's runner — the boxes are shared **by workload, not by project**, so two
repos run Stryker on one machine and have to agree about how.

**`git-lfs` was not installed.** It is now. Without it a clone yields ~130-byte pointer stubs
instead of demos, and every corpus test degrades to a passing no-op — RISKS B20 with a different
cause. The runner therefore asserts the smallest `.dem` exceeds 4096 bytes before it starts, and
that check is not decoration: it is the only thing standing between a broken clone and a clean
green run that measured nothing.

#### The lock guards the box, not the project

`/tmp/measurement-box.lock` is shared with every other repo on that machine. Giving this repo its
own lock file would let two projects mutate simultaneously, and **Stryker reads a build failure
caused by a concurrent job as a surviving mutant, not as an error** — so two locks would quietly
corrupt both projects' results rather than colliding loudly. It was originally named for PBJ and
has since been renamed for the box, which is the correct name for something guarding a machine.

#### Scheduling belongs to whoever owns the box

The crontab is not edited from this repo, by owner's instruction. What is offered instead is the
runner plus the constraints it has to satisfy:

- must not overlap PBJ's 07:00 and 19:00 daily, or 08:15 Sunday — the flock **refuses rather than
  queues**, so a collision silently skips a run;
- needs a window of up to five hours, against PBJ's 38 minutes;
- must not finish inside 02:00, the DST transition hour;
- weekly is sufficient — daily would be waste, since the fast projects already run on Actions.

A 3–5 hour weekly run also *helps* Oracle's idle-reclamation threshold rather than threatening
it, adding roughly four hours a week on top of PBJ's 8.9.

### D30 — date a candidate build before downloading it, and prefer ZIP items for that reason

Closing the era axis means finding period clients, and the candidates on archive.org are 3–5 GB
each with titles that often say only a year. Downloading one to find out it is redundant costs an
hour; **the build date is four bytes of information sitting inside a 4 MB file.**

**`bin/engine.dll` carries the build stamp as a plain string**, so a build can be dated without
launching it, without Steam, and without unpacking anything:

```
Exe build: 18:14:51 Oct  9 2007 (%i)          <- 2007 launch, PatchVersion 1.0.0.5
Exe build: 20:17:35 Mar 19 2008 (3420)        <- protocol 14
Exe build: 17:24:29 Mar 25 2013 (5252) (215)  <- protocol 24
```

The `%i` is a format specifier, not a value — the build number is substituted at runtime, so the
date is what the binary yields statically. Note the 2013 stamp takes **two** trailing numbers where
the older two take one; two samples is not a rule, but it is a candidate fingerprint.

**Archive.org will serve one file from inside a ZIP, and will not from inside a 7z.** Both formats
get a browsable listing at `/download/<item>/<archive>/` (which redirects to `view_archive.php`),
and that listing is enough to confirm the layout. But only ZIP supports fetching a member:

```bash
# ZIP: 4 MB, dates the build in seconds
curl -sSL -o e.dll "https://archive.org/download/<item>/<archive>.zip/<path>%2Fbin%2Fengine.dll"

# 7z: returns HTTP 200 with a ZERO-byte body
```

That is not a bug to work around — a solid 7z cannot be partially decompressed, so there is nothing
to serve without expanding the whole archive. **Check for a `200` with `size_download=0`**, because
the status code alone says success.

**Consequence for the hunt:** prefer a ZIP candidate when one exists, and treat a 7z candidate as a
full download. It is also worth reading the item's file listing first for a second reason — an item
whose `.7z` sits inside a subdirectory needs that path in the URL, and a wrong filename returns a
146-byte HTML 404 that `curl -o` will happily write over the target name.

**Verified 2026-08-10** by dating the 2007 launch client (`Oct 9 2007`, PatchVersion 1.0.0.5) from
a 3 GB ZIP for 4 MB, and by getting a zero-byte body from the equivalent 7z request.

### D31 — two corpora: gcor is one specimen per generation, lcor is everything else

The committed corpus went from 10 demos and 308 MB to 6 and 16.5 MB in one change, then back to
10 and 20.3 MB as five eras were added. Those are not the same ten demos, and the difference is
the decision.

**gcor — `tools/corpus/demos/`, committed.** One specimen per **era × point of view**. Ten files,
20.3 MB, protocols 11, 14, 15, 16, 24.

**lcor — `tools/corpus/local/`, git-ignored.** Everything else: modern matches, duplicate
specimens, anything held for volume. Fourteen files, 774 MB. `Corpus.Files()` includes it
automatically, so a **local run is a superset of CI** — a local pass cannot hide a CI failure,
only the reverse, which is the useful direction.

**What forced the split was a bill, not taste.** GitHub's free Git LFS tier is 1 GiB of bandwidth
per month and every CI job that fetches the corpus spends it. At 308 MB that was three runs. Six
of those ten demos were protocol-24 SourceTV recordings differing only in map and date — 257 MB to
say the same thing six times. At 20.3 MB it is fifty runs.

**The rule for growth: gcor grows for a new GENERATION, never for volume.** Another modern demo
tests the modern path again; a protocol 12 demo tests four rules nothing else can reach. When told
to "add demos", the default is lcor.

**Era specimens are kept to 2–4 minutes deliberately, and that is what keeps gcor small — not the
age of the client.** Measured across gcor: 0.18–0.23 MB/min for SourceTV on a listen server,
0.34–0.92 for POV (which carries `dem_usercmd` once per tick where SourceTV carries none), 0.59
for a 24-player match. The same recordings at 30 minutes would make gcor 90 MB. Every
protocol-conditional rule fires during signon and the first snapshots, so length buys an era
specimen nothing.

**Consequence for the mutation box:** the corpus job's input is now effectively stable. Filling
both remaining protocol gaps adds a few MB, not tens — provided the recordings stay short, which
is a condition rather than a promise.

## D34 — one renderer with two camera modes, on Direct3D 11 via Silk.NET

Decided 2026-08-11, when the viewer stopped being hypothetical.

**One project, not two.** The intended progression is a top-down labelled overview first, then a
free camera over real map geometry. Those differ by a projection matrix and a camera controller —
orthographic against perspective — not by a codebase. A separate 2D viewer would be thrown away at
exactly the point it started being interesting, so the empty `managed/Tf2DemoSalvage.Viewer2D`
placeholder was deleted rather than filled in.

The staging falls out of what is already decoded, which is convenient:

| stage | needs | status |
|---|---|---|
| top-down, labelled players | entity origins, `democmdinfo_t` camera track | **decoded already** |
| map geometry | BSP + VPK reading | Phase 3, not started |
| free camera, voice playback | the above, plus the codec work | in progress |

The useful stage is reachable before the expensive one, and the expensive one is additive.

**Direct3D 11, and the usual reason for it is wrong.** "TF2 is a Direct3D game" does not constrain
a tool that reads TF2's *files* rather than using its renderer. BSP geometry is vertices and faces;
VTF textures are DXT-compressed and upload unconverted as BC1/BC3 under Direct3D or as S3TC under
OpenGL. There is no compatibility argument in either direction, and it is written down here
explicitly so nobody re-derives the wrong one later.

The reasons that do hold:

- **This project is Windows-only regardless**, so OpenGL's portability buys nothing.
- **PIX and the Windows graphics tooling** are better than the OpenGL equivalents.
- **Silk.NET's Direct3D bindings are a thin layer over the COM vtables.** Every buffer map, every
  `UpdateSubresource`, every copy is visible and controllable. An abstraction such as Veldrid or
  MonoGame hides precisely the things worth reaching for when something turns out to be slow —
  which is the same argument as using `unsafe` at the codec interop boundary rather than a
  marshalling layer. Keep the layer thin where the cost lives.

The cost is real and was accepted knowingly: raw COM in C# means `ComPtr<T>`, manual device and
swap-chain setup, and HLSL through `D3DCompile` — a few hundred lines before anything is on screen,
against roughly eighty for OpenGL. It is front-loaded, so it lands before there is much to lose.

**`AllowUnsafeBlocks` and one scoped analyzer suppression.** `S6640` ("avoid using this unsafe code
block") is disabled by an `.editorconfig` in the renderer directory alone. There is no safe
formulation of this layer to prefer — the alternative to unsafe is a marshalling copy per frame —
and scoping the suppression to the directory means the analyzer still objects if `unsafe` ever
appears in `Core`, where it would be a decision to argue on its own merits.

### D32 — a downloaded BSP is hostile input, and the rules are written before the parser

Maps come from fastdl, the same HTTP mirror a game server hands a joining client. That is the
right source — it is where the game itself gets them — but it means **the bytes are supplied by
whoever runs the server**, not by Valve, and a map that arrives this way has passed through no
review.

This is not hypothetical. The Source engine has had map-driven remote-code-execution research
published against it, and a BSP is a fat target by construction:

- **A header of 64 lump entries, each a file offset and a length**, pointing anywhere in the file.
- **An embedded ZIP** — the pakfile lump — so a BSP reader inherits a zip parser's entire attack
  surface, including compression ratio bombs and `../` in entry names.
- **A tree structure** in the node and leaf lumps, which a naive reader walks recursively.
- **Indices between lumps everywhere**: texinfo into texdata, faces into edges, edges into
  vertices. Every one is a number from the file used to subscript an array.

**The rules, which apply when the reader is written and not after:**

1. **Validate every lump against the file length before reading it.** `offset >= headerSize`,
   `length >= 0`, and `offset + length <= fileLength` computed in `long`. This is the same check
   `WireBounds` already performs for the demo format, and for the same reason.
2. **Derive counts from lump length, never from a count inside the data**, where the format allows
   the choice. A length is at least cross-checkable against the file; a declared count is not.
3. **Allocate from what is present, not what is declared.** The two DoS defects found in this
   project on 2026-08-12 were both allocate-before-validate — `Lzss` sizing a buffer from a
   declared length that agreed with a second declared length, and `CopyBits` taking 250 MB from a
   declared bit count before the first read could fail.
4. **Bound-check every cross-lump index at use.** An index read from the file is untrusted even
   when the lump it came from validated.
5. **Traverse the BSP tree iteratively, or cap the depth.** Unbounded recursion raises
   `StackOverflowException`, which .NET cannot catch — it kills the process outright, so it is a
   denial of service that no `try` can soften.
6. **Cap bzip2 expansion.** fastdl serves `maps/<name>.bsp.bz2`, and a decompression bomb is the
   cheapest attack on the list.
7. **Never write into the user's game install.** Their `tf/maps` is read-only to us; downloads land
   in this application's own maps directory. A parser bug then cannot corrupt their game, and a
   malicious map cannot be planted where the game itself will load it.
8. **Sanitise any path taken from a pakfile entry** with `Path.GetFullPath` plus a prefix check
   before it touches the file system — the standard zip-slip guard.

**And the mechanism, because rules in a document decay.** The BSP reader gets a SharpFuzz target
alongside `container` and `snappy` the day it can parse a header. That harness already exists, it
already found a real bug in `Snappy` within sixty seconds of first running, and a format this
shape is exactly what it is good at.

### D33 — FlaUI for the UI tests, not WinAppDriver and not WindowsDriverCore

The owner is writing WindowsDriverCore, a WinAppDriver-compatible server on raw `IUIAutomation`.
It is not used here, and the reason is not readiness — it is that this project does not want what
it provides.

**A driver server exists to serve clients that speak WebDriver or Appium.** That protocol is what
lets an existing Python or Java suite drive a Windows application unchanged. This project is
Windows-only, its tests are C#, and nothing here speaks WebDriver — so the protocol layer would be
cost with no corresponding benefit.

**FlaUI is in-process, and that makes it the floor.** Every call a driver server handles is
marshalled over HTTP to another process and back; a library calling the same COM interfaces
directly cannot be beaten on latency by something doing strictly more work. For a suite that
launches a real application and then makes hundreds of element queries against it, that difference
compounds into minutes.

**Reversible cheaply, and only on one side.** Nothing in the viewer knows how it is being driven —
the application exposes automation ids and accessible names, which is what *any* UIA-based tool
consumes. Changing driver later is a migration of the test suite, and some tests would need
rewriting, but it touches no application code.

Practical consequence for anyone reading this later: **`ViewerApplication` is the only file that
references FlaUI types.** Tests speak to it, not to the library, so the blast radius of that
migration is one file plus whatever assertions genuinely depend on driver behaviour.

## D35 — geometry is world space; the camera is the only thing that knows about the view

**Decided 2026-08-13**, while wiring entity models into the viewer, and it changes what "add
models" means.

The renderer's vertex shader was already camera-agnostic:

```hlsl
output.pos = mul(float4(input.pos, 1.0f), viewProjection);
```

Two things around it were not, and both were reasonable when the only camera was an overhead one:

- **The vertex's third component is a precomputed `Depth`** — world height, inverted — rather than
  world Z.
- **The camera matrix ignores Z**, with the row `0, 0, 1, 0` and the note "Z passes through
  untouched". Depth is computed before the matrix ever runs.

That is a top-down projection baked into the geometry. It works, and it cost nothing while nothing
else existed, but **models must not be built on it**: a model whose vertices are already flattened
for one camera has to be rebuilt for the next one, and the point of the top-down view is that it is
one camera among several. The owner's framing, which decided this: the top-down view *is* a free
camera at its core, and everything built for animation and props should work unchanged for a free
camera and for a point-of-view camera.

**So: vertices carry world X, Y, Z. The camera matrix does the projecting.** Overhead becomes an
orthographic matrix that maps world height into the depth range; a free camera and a first-person
camera are different matrices over the same geometry. Nothing in a model, a prop, an animation or
an interpolated pose learns which camera is looking at it.

This is also what the engine does, and following it is the whole reason to look: a studio model in
Source is world-space geometry posed by bone matrices, and the view is a separate transform applied
after. Flattening geometry per camera is a thing this project invented, not a thing Source does.

**Consequence for the height cut.** The overhead view's "take the roof off" cutting plane currently
works on the precomputed depth. It becomes a world-height test instead, which is what it always
meant — and it is the mechanism B49's black lids need, so the two land together.

## D36 — surf and jump runs are a named audience, and they set the accuracy bar

**Stated by the owner, 2026-08-16.** Part of why this parser exists is so TF2's surf and jump
communities can properly document old runs — recordings the live client can no longer play, which is
the same problem this project was built for, arriving from a direction that had not been written
down.

**Not the same "surf" as `SURF_*`.** Those are texinfo bits in `bspflags.h` — sky, nodraw, hint,
bumplight — and have nothing to do with the game mode. The collision is named here because reading
one as the other builds the wrong thing confidently, and it happened in the session that produced
this entry.

**What it changes: which errors are tolerable.** A viewer can be approximate about a material and
still be useful to this audience. It cannot be approximate about a number. Specifically:

| Quantity | Why it is load-bearing |
|---|---|
| `dem_usercmd` view angles and `sidemove`/`forwardmove` | this IS the strafe, per tick, not a summary of one |
| tick attribution | a run's time is a tick count, so an off-by-one is a falsified record |
| `m_vecOrigin`, and a recording player's `m_vecVelocity` | derived speed from position deltas approximates velocity, it is not the same number |
| zone and timer events | usually plugin-driven on these servers, so they arrive as user messages or trigger entity state rather than as documented messages |

**The consequence for priorities**: a wrong material is a cosmetic defect here and a wrong tick,
angle or origin is a fabricated record. `UserCommandConformanceTests` exists because of this — it
extracts the field order from `WriteUsercmd` rather than transcribing it, since a transposed pair
still decodes and produces a complete command describing a run that never happened.

**What this does NOT commit to.** Emulating `gamemovement.cpp` would let positions be *reproduced*
from the recorded inputs, which is how a spliced demo would be detected and how a run could be
replayed with no client at all. That is a much larger job and is not needed to document a run —
decoding gives the run as recorded. The two compose if it is ever built: inputs from the demo,
positions from emulation, and any disagreement is a finding.

## D37 — models are lit the way the engine lights them, ambient cube plus local lights

**Owner's decision, 2026-08-17, stated when brush entities first drew and came out flat-lit:** the
lighting should be done as Valve does it. This closes a question the B71 amendment had left open,
and it settles a class of future ones — the target is the engine's model, not something that looks
close.

**What the engine's model is**, from `public/istudiorender.h`, which describes the whole of it in
three fields:

```cpp
Vector m_vecAmbientCube[6];		// ambient, and lights that aren't in locallight[]
int    m_nLocalLightCount;
LightDesc_t m_LocalLightDescs[4];
```

So a model takes the ambient cube of its leaf **and up to four local lights**, with everything beyond
those four folded back into the cube. Not one or the other.

Two consequences that are already visible:

- **Brush entities are lightmapped by the engine and are not lightmapped here.** A door drawn through
  the entity path takes an ambient cube, so it draws flat against lightmapped walls. The fix is
  lightmap coordinates in the entity vertex format, or the world shader with a per-instance
  transform, and it is a real divergence rather than a detail.
- **No prop receives direct light from a point or spot light** (B95). The cube is the bounce term;
  the direct term is the world lights, and only the sun was applied.

**Why this is a decision and not just a bug list.** The alternative — tuning the ambient term until
screenshots look right — was live for a while, and B83 shows where that leads: five falsified
appearance hypotheses across a month, every one proposed from a picture. Naming the engine as the
target means a disagreement is settled by reading Valve's code and measuring, not by argument about
how a screenshot looks.

**Where the answers live, since this project twice recorded that they were unavailable:** the falloff
is stated inline in `public/bspfile.h`, the ambient reconstruction is `Mod_LeafAmbientColorAtPos` in
`utils/vrad/leaf_ambient_lighting.cpp`, and `utils/` generally holds the compilers that WRITE the
data the engine reads. See the `nothing-is-closed` memory.

## D38 — the test suite runs on synthetic demos; the corpus keeps only what real bytes alone can prove

**Owner's direction, 2026-08-19**, given in several steps over one session. Recorded together
because the reasoning only makes sense as a sequence, and because two of the steps reversed an
earlier position of mine.

**The starting instruction** was operational: *"all the corpus tests should be changed to
synthetics, thats the only way we are going to be able to run them on any box, or github"*, and
*"using the real corpus is not and should not be needed for anything but the cli tests"*. The
corpus needs 305 MB of Git LFS, which means it cannot run on the measurement boxes and costs
bandwidth on every CI job.

Enabled by something the project already had and I had lost track of — the owner had to point it
out: *"you should be able to synthetically make a demo from scratch if you cant find a demo already
with something, thats the great thing about being able to compile back to dem format, we can create
demos to test things."* Followed by *"make sure to make a note of that, because its something we
worked on early, and while i hadnt forgotten, you seem to have."* Hence
`docs/memory/author-the-specimen-the-corpus-lacks.md`.

**Where the synthetic replacements live: `Core.Tests`, not `Corpus.Tests`.** The owner's instinct
was the opposite — *"the replacements you were making could go into the corpus project, instead of
anywhere else"* — and accepted the counter-argument, which is worth keeping because it is not
obvious: Stryker runs per project, the `corpus` mutation route is permanently closed on the
measurement box, so a synthetic test placed there is one **nothing ever mutates**.

**What killed most of the remaining corpus tests was the owner's question**, not the audit:
*"why do we need to verify a demo has anything? that is not a test we should really have to be
making, unless we are checking to make sure we decode that and or encode it properly."*

That collapsed a whole category. Those tests assert that a real recording contains a crouch, a
death, a mid-game join, and justify it as a control — *"if the recording contained no death the
assertion below measures nothing."* But that guard exists only because **the corpus is an
uncontrolled fixture**. A synthetic test constructs the death; there is nothing to guard. And the
other thing such a test appears to prove — that this occurs in real gameplay — is a claim about
TF2, not about this parser, and a test does not establish it. Reading the SDK does, which is what
the conformance suites are for.

**The final scope**, owner's words: *"if something really cant be converted than that is fine, but
the corpus suite should be basically nothing at this point, it was serving more like a bad
conformance test then the good ones we have, and it was slow."*

So what stays is only what real engine bytes alone can prove:

- byte-exact round trips of real demos, which is the project's flagship criterion
- the differential against an independent parser
- voice codec payloads, which cannot be synthesised as valid compressed audio at all
- totality — the engine wrote these bytes and reads them back, so anything failing to decode is
  our defect
- facts about specific real files, such as the launch-build SourceTV schema truncating at 64 KiB

Everything else converts. Where a corpus test asserted a plausibility RANGE — "inside the world
bounds", "more than ten distinct positions" — the synthetic version asserts the value, because a
written demo knows the answer and found data does not.

## D39 — test names are `{Subject}_{Scenario}_{Expected}`, converted repo-wide

**Owner's decision, 2026-08-19, and a reversal within the same session.** First position:
*"just dont change it, its no big deal"* and *"im not wasting time and money fixing this."* Then,
after I said the prose names cost me a file-open to learn what a failing test even touched:
*"if its a problem for you too then we will convert the names, this is going to suck balls and i
hate you, but we have to do it, start using the standard industry convention."*

**The deciding reason is the owner's and it is not aesthetic:** *"i will ignore it, even though it
actually makes me hand debugging and figuring out what failed in a test harder."* A failure reading
`TheTraceNamesEveryKindItWalksPast` names the CLAIM and not the SUBJECT, so a red run begins by
opening the file. That cost is paid on every failure, by both of us.

880 methods across five projects were converted. The convention itself is in `CLAUDE.md`; what
belongs here is why it was worth doing rather than what it is.

**No decision ever produced the old convention.** Checked against this file, `CLAUDE.md` and every
memory entry: it drifted. One early file used prose names, each later file matched its neighbours
because matching surrounding style is the default instruction, and nobody compared practice against
the written standard until it reached 2,132 tests. The owner's summary: *"the guilty party is many
many previous sessions models."* Writing the convention down is therefore the actual fix; the
renames are cleanup.

Declined at the same time, and worth recording so it is not re-proposed: splitting `Core.Tests` by
area into `Core.Decode.Tests` and similar, and renaming `Corpus.Tests` to match what it tests.
Owner: *"just dont change it, its no big deal, we are moving out of corpus.tests anyway which it
the weird one."*

## D40 — no scripted edits to source files, restated after a live near-miss

The global standards already ban editing source with `sed`/Python. It happened anyway on
2026-08-19, and the failure is worth recording because it is the exact one the rule predicts.

A `sed` rewriting 33 call sites of `DemoTimeline.Build` to `TimelineCache.For` also rewrote the
cache's own body into `() => TimelineCache.For(key)` — which compiles, and recurses until the stack
dies. It was caught only because the file was re-read afterwards.

Owner: *"thats why your not supposed to script like that."*

The distinction that makes the rule workable rather than absolute: **choosing** a change is
judgement and must be done by reading; **applying** an already-chosen identifier rename across many
files can be scripted, but only with a per-substitution assertion that the old token existed, that
the new one did not already exist, and that the counts match afterwards. That is what
`build/`-adjacent rename tooling did for the 880 test renames, and it caught four crefs that a
free-hand edit would have left dangling. It did not save the call-site rewrite above, because that
edit had no such guard.

## D41 — this project's measurement check names this project, on every line

`build/check-measurements.ps1` reports Tf2DemoSalvage's own runs on the shared boxes, and says
"Tf2DemoSalvage" in its header, in each box heading, and in its summary line.

Owner, 2026-08-19: *"make sure it tells me its for tf2, pbj's doesnt and it confused me at first."*

**The confusion is structural, not cosmetic.** PokemonBattleJournal's
`build/check-measurement-boxes.ps1` reports the single newest run directory in `~/measurements/`
— whichever project owns it — and prints its mutation score under a header that names no project.
Three projects share mutation-box. So on any given morning that line is a coin toss, and on
2026-08-19 it would have shown `20260819T230001Z-76e28b6-stryker-core` (PBJ's) while four
Tf2DemoSalvage runs sat directly beneath it, invisible.

The two checks are complementary rather than duplicated, and that is why a second one was written
instead of the first being changed:

| | Answers |
|---|---|
| PBJ's `check-measurement-boxes.ps1` | is the box alive, is anything running, is the disk full |
| this repo's `check-measurements.ps1` | did **our** five slots run, and what did they come back with |

**Runs are selected by the `.owner` marker, never by a name glob**, which is the rule the shared
box has already taught twice: `~/measurements/` holds `<stamp>-<sha>-<mode>` directories, so the
obvious own-glob `*-fuzz` also matches a neighbour's `*-tcgdex-fuzz`. Fuzz targets are taken from
the run's own `fuzz-<target>.log` filenames for the same reason.

Two things the first version got wrong, both found by running it:

- **A fuzz run has no mutation score**, so reporting "ran and scored" for it was a green line
  about a measurement that had not been read at all. It now reports targets and outstanding crash
  inputs, and a run with no score line at all is reported as a failure rather than as a blank.
- **It counted crash inputs that were already regression fixtures.** The Snappy artifact from
  2026-08-15 had been fixed and committed on the 16th and still sat in `~/findings-snappy`, so the
  check asked for work already finished — which is how a real finding gets scrolled past. Triaged
  artifacts now move to `findings-<target>/triaged/` on the box, with a README naming the fixture
  that replaced them, and the count is `-maxdepth 1`. Moved rather than deleted: the bytes are the
  fixture, and a reimaged box loses anything not committed.

**The daily schedule is session-only and this is a real limitation.** `CronCreate` jobs live in
one Claude session and auto-expire after seven days, so the durable artefact is the script; the
schedule around it has to be re-made, or wired into a Windows scheduled task, to outlive a session.

## D42 — the viewmodel lookup answers the main hand, and the off hand is left undrawn on purpose

A player carries two viewmodels: `MAX_VIEWMODELS` is 2, slot 0 is the weapon in hand and slot 1 is
the off hand — `CTFPlayer::GetOffHandViewModel` is `return GetViewModel( 1 )`. TF2 puts the spy's
Invis Watch in slot 1, and **the watch is the only thing that uses it.**

The SDK reads as though there were two. `tf_weaponbase_grenade.cpp:74` also calls
`SetViewModelIndex( 1 )` and was cited here as a second case — but TF2's throwable grenades were cut
before release. The class is still linked and nothing shipped names it: the only
`tf_weapon_grenade*` item class in `items_game.txt` is `tf_weapon_grenadelauncher`, the demoman's
PRIMARY. The owner caught the claim ("this isnt tf1, tf2 only has the spy watch for offhand"), and
the correction is kept because it is the recurring shape of error here — living SDK code that
nothing shipped exercises, read as evidence about the game.

The first implementation of `DemoTimeline.ViewmodelAt` kept
whichever viewmodel it saw last and therefore showed a spy watch in a soldier's hands on the one
corpus demo that carries both.

`ViewmodelAt` now filters on `m_nViewModelIndex` and answers with the main hand only.

**The off hand is not an alternative to the main hand — both are on screen at once.** The owner,
asked directly:

> main viewmodel doesnt get hidden when a spy goes invis, the watch just comes up and everything
> goes transparent

and:

> yep the watch is the left hand, the weapon in in the right, unless you use left handed
> viewmodels, then its the opposite

So answering with the main hand alone is knowingly one weapon short of what a cloaking spy saw.
That is the decision: **one weapon short beats the wrong weapon**, and drawing both is its own
piece of work with its own test. Recorded rather than fixed in passing so the gap is a known
absence instead of a surprise the next time somebody watches a spy demo.

The handedness remark does not change the lookup at all, and that is worth stating because it looks
like it should. `cl_flipviewmodels` is a setting on the machine playing the demo, not something the
recording carries; the demo names the entity and the model, and which hand it appears in is decided
at draw time by `C_BaseViewModel::InternalDrawModel` switching to `MATERIAL_CULLMODE_CW` when the
model is mirrored.

### D42 outcome, 2026-08-20 — the off hand is drawn, and the gap was a missing property

The known absence above is closed. `MainForm.AddViewmodel` now draws the off hand beside the weapon,
under its own entity index because all three models are on screen together.

**What made it a piece of work rather than a second lookup was a property this file said did not
exist.** The comment on `EntityState.ViewModelTable` read "a viewmodel inherits no `DT_BaseEntity` —
no origin, no angles, no `m_fEffects`". The first two are true. The third is not:
`baseviewmodel_shared.cpp:565` declares `m_fEffects` on `DT_BaseViewModel` itself, ten bits
unsigned. NOBASE stops a table INHERITING a property; it does not stop it declaring one.

That mattered because **a slot-1 entity is not a watch in a hand.** Every player carries both
viewmodels for their whole life. Drawing every slot-1 entity would have put a watch in eighteen
players' hands for a whole match, and the engine's answer is exactly the property we had written off:
`CTFWeaponInvis::SetWeaponVisible` resolves `pOwner->GetViewModel( m_nViewModelIndex )` and calls
`vm->AddEffects( EF_NODRAW )` on it.

Nothing failed while the claim was wrong. `IsDrawn` looked in `DT_BaseEntity`, a viewmodel answered
null, and null reads as "no flags set" and therefore as "draw it". Third time in this repository that
a right property name in the wrong table has been silent — see
`docs/memory/a-property-name-needs-its-declaring-table.md`.

**Two ways a viewmodel leaves the screen, and both are now handled in one place.** `EF_NODRAW`, and a
model index of zero, which is what an unused off hand sends — all 22 of z1800's do. Both are recorded
onto the sample rather than filtered at record time, because a viewmodel that is emptied or hidden
must not leave its last drawable sample standing: the lookup keeps the newest match, so a skipped
update means a watch that was put away stays in frame for the rest of the demo.

**Measured on z1800 afterwards: 190 of 9,165 sampled player-ticks, three distinct models, every one a
spy watch** — the stock Invis Watch, the Enthusiast's Timepiece and the Quäckenbirdt. The corpus
agreeing with the SDK that only `CTFWeaponInvis` claims slot 1, and confirming the off hand needs no
weapon merged onto it: each path is a complete model, not arms.

**Nothing here needed a decompiler, and it was proposed that it might.** The concern was reasonable
— the defect appears only on older demos, and the SDK is the 2013 tree — but a demo carries the
schema that describes it, so "did the 2009 build send this property" is a question the 2009 file
answers itself. `ViewmodelConformanceTests` asserts the property's presence and its 1-bit unsigned
width against each demo's own schema, back to the 2007 build.

## D43 — the viewmodel field of view defaults to 70, the top of the game's range

**Owner's decision, 2026-08-20**, made while trying to check whether the hands and arms were drawn
correctly: "the 55 doesnt let me see the hands or arms to check those". Then, on being told 75 would
be outside what TF2 permits: "sorry 70 if thats parity."

TF2 declares the convar with a default of 54 and hard bounds of 54 and 70:

```cpp
ConVar v_viewmodel_fov( "viewmodel_fov", "54", FCVAR_ARCHIVE, ..., true, 54, true, 70, NULL );
```

This viewer keeps the bounds and changes the default to 70.

**The distinction that makes this acceptable is between the game's DEFAULT and the game's
BEHAVIOUR.** 70 is a value any TF2 player can set, so every frame drawn at it is a frame the engine
could draw — nothing is invented and no geometry appears that a player could not see. A config asking
for 90 still gets 70, exactly as in game, because the bounds are unchanged. What is being departed
from is only the number a player sees before touching the setting.

**And the reason is what this program is for.** It exists to show what a demo contains, and at 54 the
arms sit mostly outside the frame — which is precisely the thing that could not be checked while the
viewmodel work was going on. A default that hides the subject is the wrong default for a tool whose
job is inspection, even when it is the game's own.

The exchange is worth keeping for its shape: the owner asked for 75, was told that exceeds the
engine's clamp, and immediately chose parity over convenience. **The bound is the engine's and the
default is ours** — set `viewmodel_fov 54` in the settings file for the player's-eye value.

`ViewerSettingsTests` covers the clamp at both ends, which is the part that must not drift; the
default is stated in `ViewerSettings.ViewmodelFieldOfView` and written into the generated config with
the reason beside it.

## D44 — a model's `env_cubemap` is resolved by Valve's nearest-cubemap rule, and the interpolation is flagged

**Owner's direction, 2026-08-20**, on how to close the gap that left every reflective prop matte:
"do it however valve does".

**The question this answers.** A material's `$envmap` can name a concrete texture or the literal
`env_cubemap`. vbsp rewrites the literal at compile time for every brush face it binds, so brushwork
arrives naming a real texture and needs no search — that is what closed B55. A model's material
cannot be rewritten, because `Cubemap_CreateTexInfo` works on texinfo and a model has none, so it
arrives still saying `env_cubemap`. Something has to decide which cubemap that means.

**The two shaders disagree, and that disagreement is the specification.** `LightmappedGeneric`
refuses the literal outright:

```cpp
if( stricmp( params[info.m_nEnvmap]->GetStringValue(), "env_cubemap" ) == 0 )
{
    Warning( "env_cubemap used on world geometry without rebuilding map. . ignoring: %s\n", ... );
    params[info.m_nEnvmap]->SetUndefined();
}
```

`VertexLitGeneric` carries no such rejection anywhere in the file and calls
`LoadCubeMap( info.m_nEnvmap, ... )` on whatever the material says. So on a model the literal is not
a compile leftover to be discarded — **it is the request**, and it resolves against whatever the
engine has bound with `BindLocalCubemap` (`imaterialsystem.h:1200`).

**What is implemented.** `Cubemap_FindClosestCubemap` (`vbsp/cubemap.cpp:835`), reduced to the half a
model can use. Valve's function runs two passes: nearest placement lying in front of the surface,
tested `DotProduct( vecDelta, pPlane->normal ) >= 0`; and if none is in front, nearest overall. The
first pass needs one brush side's plane — the function returns -1 immediately when handed no side —
and a model has no such plane. So the second pass is the whole of the applicable rule, and it is what
`BspCubemaps.Closest` does, ties going to the earlier placement as Valve's strict `<` gives.

**The evidence classes are NOT equal and this is the part that must not be smoothed over.**

- That the rule is nearest-by-distance: **read from published source**.
- That the engine chooses a model's local cubemap by this same rule at runtime: **interpolated**.
  The routine that does it is inside the closed engine; the published client binds a local cubemap
  only in `basemodelpanel.cpp` and only a fixed default. Nothing published states the runtime rule,
  and settling it would need a decompile.

The interpolation is recorded here, in `BspCubemaps.Closest`'s remarks and in
`EnvmapConformanceTests`, because a rule whose basis is forgotten gets defended as if it were
measured. If the engine turns out to select per leaf rather than per point, the two differ only near
a leaf boundary — but that is a prediction, not a finding.

**What it fixed.** B83, open since the capture point was first noticed drawing almost black, and
whose own entry said: "If `$envmap` appears there, B83 is B55 on a prop and the two close together."

**It already appeared there, and had for some time.** B83 records all three materials as
`VertexLitGeneric` with `$envmap env_cubemap` — that measurement was taken and written down, and
what was missing afterwards was not evidence but code. The diagnosis sat complete in the risks
document while the renderer went on discarding the key, which is the failure this project keeps
meeting from the other side: a correct measurement that nothing acts on looks exactly like an open
question.

So the contribution here is an assertion rather than a discovery. `CubemapLoadingTests` now names
`cap_point_base`, `cap_point_base_red` and `cap_point_base_blue` and fails if they stop asking, so
the fact lives where it can break rather than only where it can be read.

## D45 — a conformance gap marker must be able to fail when its gap closes

**Owner's direction, 2026-08-21**, on being shown the list of still-skipping conformance tests:
"yea they were suppose to auto start working or you were suppose to keep them updated so they follow
what we have integrated".

Both halves of that were the design and only the second was ever implemented, as a discipline. It
lapsed.

**What was measured.** Five markers were claiming features that demonstrably worked:

| Marker | Reality |
|---|---|
| `WorldConformanceTests.Cubemaps_AreNotRead` | `BspCubemaps` complete; 43 placements decode |
| `SourceConformanceTests.EnvironmentMaps_AreNotImplemented` | reflections draw, brush and model |
| `SourceConformanceTests.AttachmentPoints_AreNotImplemented` | `AttachmentPlacement.Matrix` in use |
| `ModelConformanceTests.Attachments_AreNotRead` | same |
| `EffectConformanceTests.ViewModels_AreNotDrawn` | arms, weapon and the off-hand watch all draw |

The last one had predicted its own obsolescence in its comment — "invisible until a first-person
camera exists" — and went on skipping through the entire session that built the camera.

**The cost is not tidiness.** `docs/CONFORMANCE.md` is quoted from these markers and is what this
project reads to decide what to build next. A false entry there meant `BspCubemaps` was written a
second time by someone who believed the map, over a complete and better implementation, deleting ten
tests with it (see `docs/memory/write-can-destroy-what-you-did-not-read.md`).

**The decision: a marker must carry evidence that can turn against it.** `ConformanceGapAuditTests`
holds one row per checkable marker — the marker's name, and a probe for whether the gap is still
open. It **fails** rather than skips, because a marker that has outlived its gap is not a gap, it is
a wrong entry.

Two kinds of probe, and the second is much the stronger:

- **A parameter** is checked against `MaterialCensus.ImplementedParameters`. That list is maintained
  for reasons of its own — leaving a parameter out means the asset log goes on reporting it missing
  on every map load — so it does not depend on anyone remembering this file. `$envmap` had been in
  it for a day while a test said otherwise.
- **A feature** is checked by loading a real map and asking whether it produced anything. That
  measures the output rather than a list somebody keeps, which is the rule in
  `docs/memory/measure-the-output-not-the-capability.md`.

**Policed in both directions, and this is the half that is easy to omit.** A row naming a marker that
has been deleted checks nothing while looking exactly like coverage. The audit's own first version
had that defect — it asked "does the feature work" and so failed for ever once the markers were
removed — and its second version caught two dead rows on the first run.

**What it cannot do.** A marker with no cheap probe — jiggle bones, ragdolls, particles — has no row,
and `TheAudit_CoversEveryMarkerThatCanBeChecked` pins the count so a new marker has to be classified
rather than silently unpoliced. That is a smaller claim than "every marker is policed", and it is
made deliberately: the alternative is a probe that measures the wrong quantity, which the audit did
once already and which accused a correct entry (`MaterialProxies_AreNotEvaluated`, whose real gap was
narrower than the probe understood — renamed, not removed).

## D46 — where this project's code diverges from Valve's, this project's code changes

### Why, in the owner's words — added 2026-08-21

> *"part of the reason i harp on valve standards too is im pretty confident valve hires some of the
> best programmers in the world. Its an extremely hard place to be hired, and based on valves work
> outside tf2, when they are not too rushed, they are really really good at optimizing and writing
> robust maintainable software. Tf2 is only semi unoptimized because of everything that was added on
> after the fact, that valve never went back to fix."*

**The operational consequence, and it is sharper than "prefer their way":** when Valve's code does
something that looks wrong, the first hypothesis is that something of ours is distorting it — not
that they got it wrong. TF2's rough edges are accretion, and accretion looks different from a bad
decision: it is a feature bolted beside an old one, not a constant that makes no sense.

**Two demonstrations in one evening, both mine:**

- Valve's decal bias `-262144` was declared wrong for this project twice, on 2026-08-14 and again on
  2026-08-21. Both times the depth buffer was `D32_FLOAT` where the engine's is 24-bit fixed point,
  so D3D was scaling the constant by a data-dependent factor instead of the fixed `1/2^24` it is
  calibrated against (D48). The number was never tested; our format was the fault.
- The same constant then appeared to do nothing at all, which read as further evidence against it.
  A `SetDecalBias` method was disposing the rasteriser state at map load and replacing it, so every
  experiment measured a value neither Valve nor anyone else had chosen (B135). Ours again.

So the rule below is not deference for its own sake. **A divergence is a variable, and an
uncontrolled variable makes every measurement downstream of it meaningless** — which is why matching
Valve first is cheaper than reasoning about why their value misbehaves.

### The failure mode this guards against, by analogy

> *"think about it like this, if you were to just randomly come across quakes fast inverse square
> root function, you would immediately notice it isnt a perfect approximation and probably call it a
> bug, try to fix it, but that would be wrong and bad to do, because then quake will start rendering
> at a snails pace, im sure theres a bunch of that in valves code."*

**An apparent defect in expert code is usually a trade whose other side is invisible at the site.**
`0x5f3759df` looks like a magic number, the Newton step looks like it is missing iterations, and the
result is measurably wrong — every local signal says bug. The thing it is traded against, a
reciprocal square root per vertex per frame, is nowhere in the function.

The practical rule that follows, and it is a rule about **evidence** rather than about respect:
**before changing anything of Valve's, name what it is trading against.** If that cannot be named,
the code is not understood well enough to change, and the honest move is to reproduce it exactly and
record the puzzlement — `docs/findings/` exists for precisely that.

Candidates already met here that look wrong and are not: `SHADER_POLYOFFSET_DECAL` as an enum where a
float would do, the decal bias being a raw buffer-unit constant rather than a world distance, an
overlay's face list including faces at 45 degrees to its own basis, and `m_nFaceCountAndRenderOrder`
packing two fields into one short. Each was read as a defect or an oddity at some point in this
project; each is deliberate.

**The asymmetry is what makes the rule cheap:** reproducing something correct costs nothing, and
"fixing" something correct costs a defect plus the time to find it again. Two of tonight's hours went
to exactly that.

### The qualification: the trade may have been against a platform that is gone

> *"some of the optimizations may be dx 9 only or earlier, and rely on bugs which existed then but
> dont exist now, but we will find those when they cause issues with the dx11 rendering"*
>
> *"i know there are some video game console optimizations like that"*

So "name the trade" has a second admissible answer: **the trade was against Direct3D 9, or against a
console, and the other side of it no longer exists.** That is not Valve being wrong — it is a correct
decision whose premise expired — and transcribing the mechanism faithfully then produces the wrong
picture. Reproduce the *intent* instead, and say in the code which premise lapsed.

**The tell:** a faithful transcription that misbehaves on DX11 while the reasoning behind it is
sound. The question then changes from "what is this trading against" to "what did Direct3D 9 do here
that Direct3D 11 does not".

> **Reversal, 2026-08-21 — the worked example of this qualification was wrong, and it was mine.**
>
> This paragraph used to read: *"Already met: the decal bias. `m_DepthBias_Decal = -262144` is a
> D3D9-era value, and the two APIs do not agree on what a depth bias is — D3D9's `D3DRS_DEPTHBIAS`
> is a float added to depth, D3D11's is an integer scaled by a factor the buffer format decides. The
> number cannot mean the same thing in both, whatever the format."*
>
> **They do agree, and Valve says so in published source.** `public/togl/linuxwin/dxabstract.h:966`
> is Valve's own D3D9-to-OpenGL translation layer handling that exact render state:
>
> ```cpp
> case D3DRS_DEPTHBIAS:            // kGLDepthBias
> {
>     // the value in the dword is actually a float
>     float fvalue = *(float*)&Value;
>     gl.m_DepthBias.units = fvalue;
> ```
>
> `units` is the second argument of `glPolygonOffset(factor, units)`, which OpenGL scales by **r,
> the smallest resolvable depth difference** — 1/2²⁴ on a 24-bit fixed-point buffer. Direct3D 11
> defines its integer `DepthBias` with the same scale on a UNORM format. **One quantity, three
> APIs**, and Valve's constant transfers unchanged: −262144 · r = −0.015625 of the depth range under
> any of them.
>
> The constant is now set to Valve's value, and `DecalRenderStateConformanceTests` parses it out of
> `materialsystem_config.h` and asserts ours equals it.
>
> **What made this expensive is that it was the *illustration* of a real rule.** The qualification
> above is sound — a trade can expire — and attaching a false example to a true rule makes the
> example inherit the rule's authority. It sat here as the canonical case of "the premise lapsed"
> and was quoted twice more, in `WorldRenderer` and in `docs/HANDOFF.md`, in both places as settled.
>
> **And it was reachable the whole time.** `togl` is in `source-sdk-2013`, in the same checkout every
> other citation here comes from. Nobody looked, because the claim sounded like an API fact rather
> than like something Valve would have written down — which is the same shape as
> [[nothing-is-closed]] and the "TF2's game code is closed" correction in `docs/CONFORMANCE.md`.
> **The tell is a confident claim about someone else's system with no citation attached.**

**A genuine instance of the qualification is still wanted**, and the decal bias is no longer it. The
rule stands on its own reasoning; what it lacks now is a case that actually demonstrates it.

**Console paths are simply out of scope, and need no weighing:** *"for all intents we can ignore tf2
on console, its not even current"*. The console versions were the 2007 Orange Box release and never
received the later updates, so `#if defined( _X360 )` and `_PS3` blocks describe a product that
stopped moving around 2009 and hardware this project will never run on. Read the PC branch and ignore
the guarded one.

Two were read while hunting B135: `CSimpleWorldView::Draw` calls `PushVertexShaderGPRAllocation( 32 )`
under `_X360` to split the Xbox 360's unified shader registers between vertex and pixel work, and
`DecalModulate_dx9.cpp` picks its vertex-texture path under `#ifndef _X360`. The risk with these is
not transcribing one by accident — they announce themselves — it is quoting one as *evidence* of
"what Valve does" when the PC branch beside it says otherwise.

### And the point the console remark was actually making

> *"my note about consoles was actually referencing stuff done on like the nintendo, to get overlays
> and the like. you know how mario 3 got the nonscrolling part at the bottom of the screen"*

Not TF2's port — the older tradition of building an effect out of a hardware quirk. Super Mario Bros.
3's status bar is a raster trick: the NES scrolls a whole nametable, so a fixed strip beneath a moving
playfield is not on offer, and the game changes the scroll registers *mid-frame* on a scanline timed
by sprite-0 hit or the MMC3 IRQ. One screen drawn with two scrolls, out of a chip that has one.

**That is the sharpest statement of why this rule exists.** The trick is inexplicable from the code
alone — no comment says "status bar", there is only a register write at a suspiciously precise
moment, and the constraint that makes it correct is nowhere near it. Same shape as `0x5f3759df`, and
same shape as a decal bias expressed in raw depth-buffer units instead of world distance.

So the tell to watch for in the engine is **arbitrary and precise at the same time**: a magic
constant, an odd ordering, a value that only makes sense at one particular moment. That combination
is a trick, not a mistake, and the correct response is to find what it answers — or to reproduce it
and record the puzzlement.



**Owner's direction, 2026-08-21**, given while `$lightwarptexture` was being specified and it became
clear that implementing it faithfully would mean editing a half-Lambert path that had been correct
for a year: *"we do not hesitate to change our own code to properly match valves"*.

**The situation it settles.** Valve's `DiffuseTerm` squares the half-Lambert result **only when there
is no light warp** — `if ( !bDoLightingWarp ) fResult *= fResult;` — because a warp texture is
authored to carry that curve. This project squares it unconditionally, which was right while nothing
warped and becomes a double application the moment something does. The tempting move is to add the
warp beside the existing term and leave the square alone, since the existing term is tested, shipped
and looks correct.

The direction says no: **change ours.** The prior code is not a constraint on parity.

**Why it is worth writing down rather than treating as obvious.** Every argument for leaving existing
code alone is a good one in isolation — it is covered by tests, it produces a plausible picture, and
touching it risks a regression in something unrelated. Taken together those arguments freeze a
divergence in place and then defend it, and the defence gets stronger the longer it stands. This
project's entire premise is that the answer is knowable from Valve's own source, so a difference
between the two is a defect here by definition, not a design choice to be weighed.

**What it does not license.** Changing our code to match a *guess* about Valve's, or to match a
decompiled fragment with no citation. The rule is about deferring to published source when it is
read, not about churning toward whatever seems more engine-like. The evidence classes in
`docs/findings/` still apply, and an interpolation stays flagged as one — D44 is the worked example.

Corollaries that follow from it and have already come up:

- A test that encodes the old behaviour is **rewritten with the code**, not preserved as a
  compatibility constraint. Twelve light tests wrote the wrong scale into their own expectations and
  had to be corrected alongside `LocalLights` (B95).
- A constant that was tuned to look right, rather than read from source, is a candidate for deletion
  the moment the source is found — the Fresnel term in B125 was exactly that.

---

## D47 — when a component can be handed the wrong one of two equivalent-looking views, the right one becomes a required dependency

**Prompted by B132**, where `EntityStateTable.Apply` read `DecodedEntity.Properties` — what the
snapshot carried — for months, while the state-faithful view it wanted sat next door in
`EntityDecoder.EffectiveProperties` behind a doc comment that spelled the difference out explicitly.
Both are `IReadOnlyList<DecodedProperty>`; on almost every entity they hold the same values; and the
one entity class where they differ totally is one nothing had asked about.

**The choice made.** Not "pass the merged list at the call site" — one line, one place, done. That
leaves the wrong call reachable, and the wrong call is what happened. Instead: `IEntityBaselines`, a
one-method interface implemented by `EntityDecoder`, taken as a **required** constructor argument by
`EntityStateTable`. There is no parameterless constructor. A caller that has no schema writes
`EntityBaselines.None` and says so.

**Why the required form rather than an optional one with a sensible default.** An optional dependency
defaulting to "no baselines" is exactly the defect, spelled as a feature: `new EntityStateTable()`
would still compile, still run, and still lose every entity whose state equals its baseline. The
cost of requiring it was twelve mechanical edits in test files, all compile errors, all loud.

**The general shape, since this is the second instance in the repository.** A type that offers two
accessors over the same data — wire versus state, raw versus effective, declared versus resolved —
has created a decision that every caller must get right and that nothing checks. The distinction
belongs in the type system or in a required argument, not in prose. `EffectiveProperties`' comment
was excellent, accurate, and did not prevent the bug it described.

**What this does not license.** Turning every optional parameter into a required one. The test is
whether the two options produce *plausible* results that differ — an optional logger or an optional
cache does not, because leaving it out is visibly nothing. This applies where both answers look
right.

---

## D48 — the depth buffer matches the engine's format, for debugging rather than for speed

**Owner's direction, 2026-08-21**, after an attempt to transplant Valve's decal bias produced a map
of floating signage: *"that difference is going to change more than just this and make debugging a
pita"*.

**The situation it settles.** This renderer used `D32_FLOAT`; the engine uses 24-bit fixed point.
D3D11 applies a rasteriser's `DepthBias` as `DepthBias × r`, and `r` is decided by the format — the
fixed `1 / 2^24` for UNORM, and `2^(exponent(max depth in the primitive) − 23)` for FLOAT, which is
data-dependent and roughly double near a depth of 1. So **every depth constant in this project meant
something other than what it said**, and any constant read out of Valve's source meant something
different again.

That was already showing. `SetDecalBias` computes `2^24 / worldRange` and calls the result "about one
world unit" — the arithmetic for a 24-bit fixed-point buffer, applied to a float one. The number was
neither one unit nor any fixed distance, and the wall stripes had been tuned around it.

**Why parity beats the alternatives here.** The projection already matched: the near plane is the
engine's own `VIEW_NEARZ` of 7, the field of view its `CViewSetup` default of 75, and the viewmodel
pass mirrors Source's separate near plane of 1. The buffer format was the last structural difference,
and it is the one that silently rescales every depth comparison — so leaving it different meant a
translation step on every future depth question, paid forever, to save nothing measurable.

**What was weighed and rejected.** Performance: `D24_UNORM_S8_UINT` is a packed format some drivers
expand to 64 bits per pixel, so matching may cost bandwidth. Judged speculative and small against a
certain, recurring debugging cost — the owner's argument, and the right one.

**The trade, stated so it is not rediscovered as a defect.** This forecloses reversed-Z, which pairs
float precision with a projection's depth distribution and would beat both options in the far field.
Parity was chosen over it deliberately. The eight stencil bits are unused.

**What it does not license.** Copying Valve's depth constants now that the format agrees. Matching
the format removes a confound; it does not by itself import a solution.

> **Amended 2026-08-21.** This paragraph went on to say the arithmetic in B70 showed
> `m_DepthBias_Decal` *"cannot be applied as a plain rasteriser bias in the world pass even at
> Valve's own near and far planes"*. **With the format matched, it can be, and it now is.**
>
> `DecalState.ConstantBias` is Valve's −262144 and `DecalRenderStateConformanceTests` parses the
> number out of `materialsystem_config.h` to check it. What settled the units was `togl` — see the
> reversal recorded under D46 — and what settled the behaviour was rendering it:
> `OverlayOcclusionRenderTests` puts an occluder either side of the bias's reach and measures which
> surface wins each pixel.
>
> The caution was not baseless, it was just aimed at the wrong thing. The constant genuinely does
> misbehave under an ORTHOGRAPHIC projection, where depth is linear over a whole map's height and
> 1.6% of the range is about twenty-five world units. That is D49's problem, not the constant's, and
> D49 is removing the camera responsible.

---

## D49 — the orthographic camera goes; the overhead view becomes a placement of the free camera

**Owner's direction, 2026-08-21**, stated while the decal bias and the height cut were both being
diagnosed as camera-dependent: *"we will likely actually get rid of the ortho cam, basically make it
just the default placement for the free cam by matching what the ortho sees with the free cam."*

**And it is not a change of mind — it is where the design was always heading.** The owner's account,
given twice and in this order:

> *"thats what i meant to do in the first place, but you ai made the ortho cam first."*

> *"the ortho cam is probably mostly my fault, i didnt really know the design completely at first,
> and didnt ecpress that the first cam should be like valves cam, just said i wanted a top down map
> view."*

**The second is the accurate one and it corrects the first, so it is what this entry is built on.**
There was no override. The requirement was "a top down map view", an orthographic camera is a
perfectly ordinary way to read that, and it went in on **2026-08-12** (`af03199`, the third viewer
commit ever) without anybody being wrong.

**What it cost anyway, which is the part worth keeping.** Nine days later that reading had produced a
second projection, a decal constant retuned to suit it that was wrong the moment a real camera
existed, a height cut that is not a height, a reflection gap with no eye vector, and — twice — an
attempt to reconcile the two projections that had to be reverted.

**So the lesson is not about whose fault it was.** It is that an underspecified requirement gets
resolved into a design decision, and the resolution then reads as the requirement. `TopDownCamera`
appears in the history with reasons attached, because that is how a considered implementation is
written up; nothing recorded that "top down map view" admitted more than one reading, or that a
projection had been chosen where only a viewpoint was asked for. Every later decision therefore
treated it as ground rather than as a choice to revisit — including two attempts, on 2026-08-14 and
2026-08-21, to make Valve's decal bias work across both projections rather than asking why there
were two.

**The practice that would have caught it**, and it is cheap: when a request admits more than one
implementation and one of them is load-bearing, say which was picked and why *at the time*, in the
same commit. "Top down map view — implemented as an orthographic projection rather than a high
perspective camera, because X" would have left a thread to pull. A design decision recorded as a
design decision can be revisited; one recorded as a requirement cannot.

**Recorded now, before it is done, because it changes what is worth building today.** Several open
items exist only to reconcile two projections, and reconciling them is wasted work if one is leaving.

| item | if the ortho camera goes |
|---|---|
| **B135**, the decal depth bias | the two-rasteriser-state design is unnecessary — one projection has one answer. The `2^24 / worldRange` formula and `DefaultDecalBias` become dead code, and the fix is `LESS_EQUAL` against coplanar fragments, which was the right shape regardless |
| **B136**, the height cut | must be clipped on world Z. Today the shader clips `SV_POSITION.z` and the comment calls depth "height", which is true only looking straight down — with no such camera the shortcut is never valid rather than sometimes valid |
| **B126**, no reflections under ortho | moot. It exists because an orthographic projection has no eye position to mirror about, and this project's own convention would have had to be invented |

**Why it is the right direction, in the terms this project already uses.** The overhead view is a
placement, not a projection — what it is *for* is seeing the whole map at once, and a perspective
camera placed high and looking down does that. Keeping a second projection to express a camera
position has been paying for itself in exactly the coin recorded three times today: a quantity that
is derived under one projection and fundamental under none gets written as whichever is cheaper, and
its comment records the equivalence as a definition. See
`docs/memory/build-time-shortcuts-assume-the-camera.md`.

**Not started, and nothing has been removed yet.** The work is a free-camera placement that
reproduces the current framing — fitting the map's bounds in view from above — and only then the
removal of `TopDownCamera`. Order matters: deleting first would lose the reference the replacement
has to match.

**One thing to check when it is done, because it is the reason the ortho camera survived this long:**
whether the overhead view still reads well under perspective. A high perspective camera showing the
whole map has convergence an orthographic one does not, and that difference is a matter of taste
rather than correctness — so it is a question for the owner's eyes, not for a test.

**What it does not license.** Deleting the ortho paths pre-emptively, or leaving B136 unfixed on the
grounds that its camera is going. The height cut is wrong under the free camera *today*, which is the
camera the owner is using.

---

## D50 — the convention audit against TcgDex.CSharpSdk: adopt all four, refuse one

**Source: the TcgDex.CSharpSdk agent, at the owner's direction**, comparing the convention-bearing
files of both repositories — `.gitattributes`, `.editorconfig`, `Directory.Build.props`,
`Directory.Packages.props`, the workflows, `dotnet-tools.json`, `GlobalUsings`, `docs/memory`.
Written up in `PinKushin/TF2DEMOSALVAGE-LOG.md`, 2026-08-22 00:31.

**Every claim was checked against this repository before acting.** The log's own convention says an
outside observation is a claim until the project's agent has run it, and the standing instruction
here is not to take another agent's results at face value. All four held: no CodeQL workflow (only
`fuzz`, `mutation`, `test`), no `NuGetAudit`, no `Directory.Packages.props`, and no
`InvariantGlobalization`.

### Adopted

**CodeQL** (`.github/workflows/codeql.yml`). The audit's own argument for it is the right one and
worth restating: this repository is the *stronger* candidate of the two. Almost everything here
parses hostile binary from strangers — demos from ESEA, ETF2L and archive.org, BSP maps, VTF
textures, MDL models — and there is a fuzzing harness whose entire job is to feed the decoders
malformed bytes. A REST client has an attack surface; a parser IS one.

**What it adds over the analyzers already here**, which is the question worth answering before
adding a second tool: `AnalysisMode=All` plus SonarAnalyzer is a strict stack, but it is
per-compilation and largely syntactic. CodeQL builds a database and asks **interprocedural**
questions — whether a length read out of a file reaches an allocation or an index without passing a
bound, across method and assembly boundaries. That is the shape of every parser defect in
`RISKS.md`, and it is the shape a per-file analyzer cannot see.

Scoped to `Core` and `Content` explicitly rather than by autobuild, because the Viewer3D projects
are `net10.0-windows` and cannot build on ubuntu — an autobuild would fail or quietly analyze a
subset. Those two are also where the hostile input is actually read.

**`NuGetAudit`**, in `Directory.Build.props`, with `NuGetAuditMode=all` and `NuGetAuditLevel=low`.
The mode matters more than the switch: the SDK default has historically covered **direct**
dependencies only, so an advisory against something a package pulls in would not have been reported.
`low` because a severity threshold is a decision about which vulnerabilities to ignore, and a few
dozen packages is not a graph that needs triage. Paired with `TreatWarningsAsErrors`, which is what
turns a new advisory into a failed build rather than a line that scrolls past.

**Central Package Management** (`Directory.Packages.props`), with
`CentralPackageTransitivePinningEnabled`. Versions had lived in twelve `.csproj` files.

**What it fixes is drift that has not happened yet, and that is worth saying plainly rather than
overselling:** the inventory taken first found every package on exactly one version across all
twelve projects, so nothing was broken. What was missing is anything that would keep it that way —
seven test projects each pinned NUnit, Shouldly, the test SDK and the adapter separately.

The clearest case in the repo is Silk.NET: its four packages are generated together, and a
mismatched pair between DXGI and Direct3D11 surfaces as a marshalling fault at runtime rather than
as a restore error.

Transitive pinning is on because auditing transitively (`NuGetAuditMode=all`) while letting
transitive *versions* float would be half a decision. `TargetFramework` stays in each `.csproj` —
CPM centralises versions, and the Stryker/Buildalyzer problem is a different property.

**A CI code-coverage gate**, in the unit job, via `build/assert-coverage.sh`.

**Gated per ASSEMBLY, not per report, and that distinction is the whole test.** A Cobertura file's
root `line-rate` averages every assembly the run loaded, including ones the suite never touches.
Measured 2026-08-22:

| suite | file total | the assembly under test |
|---|---|---|
| `Core.Tests` | 88.8 line / 83.9 branch | **Core: 96.0 / 89.5** |
| `Cli.Tests` | 37.1 line / 33.5 branch | **Cli: 99.6 / 97.0** |

Cli's file number is dragged to 37% by Core sitting at 56% in its report. **A gate on the file total
would have been set to something below 37 and could then never fail** — the same instrument fault as
everything else in this repository, with the same tempting wrong fix of adjusting the threshold
instead of the measurement.

Floors are 90/85 and 95/92: a ratchet below the current numbers, deliberately a "don't regress off a
cliff" line rather than a target. The owner's framing is that implementations are being taken fast,
so this must not become a tax on writing code. The mutation score stays a fluctuating signal rather
than a gate (`docs/memory/mutation-score-is-a-ratchet.md`).

**Not extended to the Windows job, and that is a scoping decision rather than an omission.**
`Viewer3D.Tests` creates real Direct3D devices and skips what it cannot run, so its coverage in CI —
on a runner with no GPU — would differ from a local run and move with the runner image rather than
with the code. A floor that drifts for reasons unrelated to the commit is worse than none.

### Also found while adopting these

Two things the audit did not raise, noted here rather than acted on:

- **`Content.Tests` is not run by CI at all** — 612 tests, including every BSP, VTF and MDL reader
  test, gated only by the local `build/gate.sh`.
- **The CI count floors are far below the real counts** — Core 1000 against 1491, Viewer 340 against
  570. `docs/memory/a-floor-must-track-the-number-it-guards.md` is explicit that a floor is only a
  guard while it is close to the number it guards, and the workflow's own comment says so. Not
  changed here because CI's counts are not the local ones and I cannot run CI to measure them.

### Forbidden, and this is the entry that matters most

**`InvariantGlobalization` must never be set in this repository.** The SDK sets it; the audit
flagged it as the one thing not to copy, and that flag is correct.

It strips ICU and culture data, and `docs/memory/international-names-are-required.md` records the
defect it would resurrect: a player named `miałker` came through the demo header as `mia??ker` while
the `userinfo` table read the same name correctly — **both plausible, in the same dump, and only
visible because they disagreed**. This project's inputs are strangers' player names by definition.

Correct for a REST client with no culture-sensitive string work; wrong here. It is written into
`Directory.Build.props` as a comment beside the settings that *were* adopted, because the natural
way to act on a convention audit is to work down the list, and this is the entry that has to survive
somebody doing exactly that.

**The transferable point.** A convention audit is "adopt what fits", not "match the other repo" —
and the fit is decided by what the code does, not by which repo is stricter. The same document
recommended two things this repo should take from a REST-client SDK and one it must refuse, and
getting the third wrong would have cost more than the first two gained.

---

## D51 — game audio: Valve's mixing reproduced by us, `Silk.NET.XAudio` as a dumb sink

**Owner's direction, 2026-08-22**, on starting game audio: *"its kinda boring watching demos without
game audio"*, then, weighing the output API: *"silk i think, unless it makes more sens to match
valves audio which is probably WASAPI"* and *"valve has decent 3d audio, 1.6 is the gold standard
imo, source is worse but not bad"*.

### The question was the wrong one, and answering it properly reframed the work

"Match Valve's audio API" is a non-question, because **Valve does not use a 3D audio API**. The
engine mixes in software — computes per-channel gains, spatialises to stereo, sums into a paint
buffer — and hands finished PCM to the operating system. Which OS API receives that PCM has no
bearing on what anybody hears.

So the parity question is entirely about the **mixer**, and the output device is a sink.

### Why not OpenAL, which was the first suggestion

**OpenAL would do the spatialisation for us, and its model is not Valve's.** Distance attenuation,
panning and the doppler treatment would all be OpenAL's, in exactly the place the owner says Valve
is good. Worse, it would sound *plausible* — a distance falloff that is merely a different curve is
not something a screenshot or an assertion catches, and it would be very hard to unpick later.

Using OpenAL purely as a stereo sink avoids that and wastes its only advantage.

Two practical points settle the rest:

- **`Silk.NET.XAudio` is version 2.23.0**, the same version as the four Silk.NET packages this
  project already ships. No new vendor, and the version is already pinned centrally (D50).
- **XAudio2 is an OS component, so there is no native binary to build or ship.** OpenAL Soft needs
  one per architecture, and this project already has friction there — `tools/native-audio/build.ps1`
  builds celt and speex, and CI carries a whole step for it.

### The mixer is closed, and that is a decompiler job rather than a wall

`snd_dma.cpp` and `SND_Spatialize` are not in `source-sdk-2013`; the mixer lives in `engine.dll`.
That binary is already imported in the Ghidra project from the `$decal` work
(`docs/memory/where-the-game-and-clients-live.md`), so this is an afternoon's habit rather than an
expedition — the same conclusion `findings/18-decals.md` drew.

What **is** published gives the frame to hang the recovered rules on, in `public/soundflags.h`:

```c
SNDLVL_NORM = 75
#define SNDLVL_TO_ATTN( a ) ((a > 50) ? (20.0f / (float)(a - 50)) : 4.0)
#define ATTN_TO_SNDLVL( a ) (soundlevel_t)(int)((a) ? (50 + 20 / ((float)a)) : 0)
#define MAX_SNDLVL_BITS  9      // 0-255 regular, 256-511 goldsrc-compatible
#define MAX_ATTENUATION  3.98f  // attenuation * 64 in 8 bits
```

**Note the reserved range.** Soundlevels 256–511 are "reserved for sounds using goldsrc
compatibility attenuation" — Valve kept the 1.6 model addressable from Source, which is worth
knowing given the owner rates 1.6 as the better of the two.

The engine-side parameters are **not** in the SDK at all — `snd_refdb`, `snd_refdist`,
`snd_foliage_db_loss`, `snd_gain_max`, `snd_gain_min` return nothing from a full grep of the
checkout. Those cvars ARE the attenuation model, which is what makes them the decompiler's anchor:
find their registration, and the function that reads them is the curve.

### The layering this produces

| layer | source |
|---|---|
| sound events | already decoded — `DecodedSound` from `svc_Sounds` |
| name to file | `SoundName` (prefix characters, done), then soundscripts |
| WAV/MP3 to PCM | ours, no dependency |
| **spatialisation and mix** | **Valve's rules, recovered from `engine.dll`** |
| output | `Silk.NET.XAudio`, fed finished stereo |

### Assets are read from the user's install and never packed

Owner, same conversation: *"those wav files are in the tf2 folder right? we are not needing to pack
them? I dont want to include wav files in this program"*.

Correct, and it needs no change: sounds are read through `GameArchives.Read`, the same path models,
textures and maps already use, and D32 makes the user's install read-only to this project. Nothing
is copied into the repository and nothing is redistributed — decoded PCM exists in memory for as
long as it is playing.

That also disposes of the size objection on its own terms: a WAV's cost is a storage cost, and this
project never stores one.

## D52 — one place knows where the game is installed, because 73 places did

**Decided 2026-08-22, while writing the soundscript conformance suite.**

Tests that read TF2's shipped data — VMTs, BSPs, VPK archives, soundscripts — each carried their own
copy of the install path: an environment-variable check, a list of Steam library roots, and a file
whose presence proved the folder was really the game. Seventy-three files had one.

**A copy of that lookup was already corrupt, and it had disarmed a test without breaking a build.**
`BspModelsTests` held the path as

```
@"F:SteamLibrarysteamapps<0x0F>mmonTeam Fortress 2<TAB>f"
```

— `\common` and `\tf` run through escape interpretation, inside a verbatim string where C#
interprets nothing, so the mangling happened before the file was written. The test looked the path
up, found nothing, and took its `Assert.Ignore` branch. **A skip is not a failure**: it passes the
gate, it passes the count floor, and it prints nothing anyone reads. The map had gone unread for an
unknown length of time behind a green suite.

**So the decision is not "tidy the duplication".** It is that a hardcoded absolute path is an
unfalsifiable claim about a machine, and repeating it 73 times makes it unfalsifiable 73 times over.
One copy can be wrong; it cannot be wrong *invisibly*, because everything that reads game data fails
together and the failure is loud.

`GameInstall` lives in `tests/Tf2DemoSalvage.SdkReference` beside `SourceSdk`, which exists for
exactly the same reason at smaller scale — its own header says "One place that knows where the SDK
lives, because three had it." The two are now symmetrical: `Root`, `Available`, `Missing`, and
accessors that return null for an absent file so the caller skips rather than throws.

**Existence is checked inside the helper rather than left to callers**, because the corrupt path
proved the check is precisely where the silence gets in. `Vpk("tf2_misc")` exists separately from
`Find(...)` for the same reason: every caller spelling `_dir.vpk` itself is another chance for a
misspelling to skip instead of fail.

**What was NOT done, deliberately:** the other 72 files were left alone in this change. Converting
them is mechanical, touches a large number of test files, and belongs in its own commit where the
count floors can be read as evidence that nothing was lost. Only the corrupt one was migrated, plus
the files this change added.

**Related:** `docs/memory/edit-files-with-the-file-tools.md` (how the corruption got there),
`docs/memory/one-place-or-it-drifts.md`, `docs/memory/measure-the-output-not-the-capability.md` (a
fallback branch making a dead test look healthy).

## D53 — sound belongs to the audio project, including its own parsing

**Owner's direction, 2026-08-22.** On finding the soundscript reader, the WAV reader and the
attenuation constants spread across `Tf2DemoSalvage.Content` and `Tf2DemoSalvage.Core`:

> i kinda figured the audio would be in the audio project

and, on the principle behind it:

> i figured either all the audio should be in audio or audio should be renamed to voice if its
> solely dealing with voice audio lol, but i think the audio project makes more sense, we already
> seperate projects based on what they are doing, the parser is seperate from the 3dviewer which is
> seperate from the audio and even its own parsing logic

### The tension this resolves, and it was a real one

`SoundScript` reads a KeyValues file. So does `ItemSchema`, and so does `VmtMaterial`, and both live
in `Tf2DemoSalvage.Content` — which is organised as "the game's file formats". By that rule a
soundscript reader belongs in Content, and that is where it was first written.

**The owner's rule is different and it is the better one: projects are split by WHAT THEY DO, and
each owns its own parsing.** Under that rule the file's syntax is irrelevant to where the reader
lives. A soundscript is not interesting because it is KeyValues; it is interesting because it says
how loud a shotgun is. That belongs with everything else about sound.

The naming observation is the sharpest part of the argument: the project was called `Audio` while
containing only voice-chat codecs, so either the contents were wrong or the name was. Renaming it to
`Voice` was a live option and was rejected in favour of making the name true.

### What moved

| from | to |
|---|---|
| `Content/Assets/RiffWave.cs` | `Audio/RiffWave.cs` |
| `Content/Assets/SoundScript.cs` | `Audio/SoundScript.cs` |
| `Content/Assets/SoundScriptCatalog.cs` | `Audio/SoundScriptCatalog.cs` |
| `Core/Net/SoundAttenuation.cs` | `Audio/SoundAttenuation.cs` |

with their five test files. Counts: core −7, content −33, audio +40. **The arithmetic balancing
exactly is the evidence that nothing was lost**, which is the only check a move like this really
has.

### What did NOT move, and why

**`SoundName` stays in `Core/Net`.** It parses the `soundchars.h` prefixes off a precached name, and
`DemoTraceWriter` and `SoundNames` — both in Core — use it to write the text trace. Moving it would
mean `Core` referencing `Audio`, and Core references nothing on purpose. It is also genuinely
wire-level: it decodes a name out of the demo's string table, which is parsing the demo rather than
playing a sound.

### The cost, stated rather than hidden

`Audio` now references `Content` for one type, `KeyValuesReader`, and so drags in the BSP, VTF, VPK
and studio-model readers it will never call. No cycle — nothing references `Audio` — so this is
weight, not entanglement. If it becomes annoying, the fix is to extract `KeyValuesReader` into a
small shared primitive, not to move the soundscript reader back.

## D54 — the viewer follows MVP, not MVVM; recorded late, and the delay is the finding

**Owner's decision, made early in the project and NEVER WRITTEN DOWN until 2026-08-22.**

> it was something i specifically talked about because MVVM doesnt work well on winforms

> its also the obvious thing we should have been using even without me mentioning it because its
> [...] win forms and was made to use MVP

### The decision

The WinForms viewer follows **Model-View-Presenter**.

**The owner opened by naming MVVM** — but as the only pattern he knew, not as a position:

> we didnt decide on every architectural decision yet, are we using MVVM, i kinda want to use MVVM
> and winforms

Clarified by him afterwards, and it changes how this reads:

> the argument for mvvm from me was only that it was the only thing i knew, thats why i asked about
> other ones

So this was **a question, not a preference that was overturned**. Worth stating plainly, because the
first draft of this entry framed it as the owner being "argued out of" MVVM, which invents a
contested decision where there was an open one. It also means the reasoning below carries the entire
weight of the choice: nothing was traded away for it, and there is no dissenting position to
reconsider — only an argument that either holds or does not.

**The argument that won, recovered verbatim on 2026-08-22 from the planning conversation** — and it
is not the one this entry first claimed:

> The reason MVVM's binding felt shaky as a fit and MVP doesn't isn't really about familiarity —
> it's that **MVP's boundary can be made a compiler error**, not just a convention someone (or
> something) has to remember to follow. If `Presenter` classes live in a project that has no
> reference to `System.Windows.Forms.dll` at all, then a Presenter reaching for a `Button` or a
> `Control` doesn't compile.

Two supporting reasons, both recovered with it:

- **It composes with the TDD requirement.** A Presenter test needs a fake `IView` that records what
  was called and nothing else — no WinForms runtime, no STA thread, no window. On this project that
  is worth more than usual: the UI suite takes the desktop and needs `run-exclusive.ps1`, which is
  why `docs/memory/ui-suite-optional-until-ui-grows.md` exists.
- **MVP is old and boring, and that is a point in its favour**, because "a well-known, decades-old
  pattern name gives an AI a strong, consistent prior to pattern-match against; a bespoke in-house
  architecture has to be re-explained perfectly every time or it drifts inconsistently across
  files."

### This entry originally recorded the wrong reason, which is worth keeping

The first version of D54 said MVVM was rejected because "its binding model is WPF-shaped and
WinForms cannot express it — no `ICommand`, no `DataTemplate`". **That was a reconstruction and it
is not what happened.** It was inferred from the owner's later recollection, *"it was something i
specifically talked about because MVVM doesnt work well on winforms"* — which is a compressed memory
of a much more specific argument about **enforceability**, not about binding.

Both are now recorded because the gap between them is the point. `CLAUDE.md` says to write the
owner's reasoning in his words where he gave it *"rather than your reconstruction of it"*, and this
is what the failure looks like when it is subtle: the reconstruction was plausible, technically
defensible on its own terms, and would have been quoted as authoritative by anyone who found it. A
reader acting on the wrong version would have concluded that MVVM becomes acceptable wherever
binding is good enough — when the actual reason has nothing to do with binding and does not weaken
anywhere.

- **Model** — already correct: `DemoTimeline`, `MapScene`, and the `Core` / `Content` / `Audio`
  projects behind them.
- **View** — `MainForm`, reduced to controls, events and property setters. No decisions.
- **Presenter** — does not exist yet. This is the whole of the work.

### Why it is being recorded now instead of then

**It was not deleted. It was never written.** Checked rather than assumed, across 1,225 commits:

```
git log -S"MVP" --oneline --all          # no results
git log -S"Presenter" --oneline --all    # no results
git log -S"Model-View" --oneline --all   # no results
git log --diff-filter=D --name-only -- 'docs/*'   # no docs ever deleted
```

The owner's account is that this took **over an hour to decide** and was discussed specifically. None
of that reached the repository, so the reasoning — including the rejection of MVVM, which is the
valuable half — existed only in a conversation that is gone.

**And it was decided before the repository existed, which is what makes the loss structural rather
than incidental.** Owner:

> an hour of reasoning at the very start before the repo was even made is what makes it hurt the
> most

`CLAUDE.md` opens by stating that this project *"was planned in a Cowork conversation before any code
existed"*, and that `ROADMAP.md` and this file are where that planning survives. Carrying pre-repo
reasoning forward is the entire purpose of the initial scaffold commit — `7c83a6e`, *"Initial
scaffold: architecture, decisions, and corpus seed"* — and MVP did not make it in. The one document
whose job was to prevent exactly this is the document that dropped it.

**The root cause, established afterwards: the planning session was a CLOUD session, and nothing
crosses that boundary.** The owner's first assumption was that it had been local:

> i thought i did it local, but if it was on clowd then that was probably the real issue

Checked, and it holds. The four memories that session reported writing are in none of the nine local
project memory directories nor in `~/.claude/memory`, and its transcript is not in
`~/.claude/projects/` — the one apparent `MVVM` hit in a local transcript turned out to be base64
inside an encoded blob. A Cowork session's memory and transcript are both server-side, so **the only
thing that crosses to the machine is what somebody writes into a repo file before the session ends.**

That makes the handoff document the single point of failure, and this one had the right instructions
and still leaked: `CLAUDE.md` opens by saying the project was planned in a Cowork conversation and
that `ROADMAP.md` and this file are where that planning survives. The mechanism was understood. MVP
did not make it through.

**The lesson generalises past MVP: a decision made before the first commit has no code to leave a
trace in.** Every later decision is at least inferable from a diff — someone can look at the codebase
and reconstruct that a choice was made, even without the reason. A pre-repo decision leaves nothing
at all, so if the scaffold omits it, it is gone with no evidence that anything is missing. Those
decisions need recording *first* and checking *hardest*, and the check is to read the list back to
the person who made them.

**This is exactly the failure `CLAUDE.md` already describes for test naming**: a convention that
lives only in someone's head, or only in the surrounding code, drifts, and nobody can distinguish
drift from a decision. It cost the same thing here that it cost there — the code grew for two weeks
in a shape nobody had agreed to, and the divergence was invisible because there was nothing to
compare against.

### The state it is being recorded against

`MainForm.cs` is **4,436 lines, 95 methods and 103 fields**. It calls `DemoTimeline.Build` itself,
holds `_timeline`, and carries domain helpers such as `HeldWeaponModels(DemoTimeline)`. That is a
form doing a presenter's job, and it is the largest file in the viewer apart from the renderer.

### What this buys beyond correctness of pattern

The viewer's 8 UI tests take the desktop and need `run-exclusive.ps1`, which is why
`docs/memory/ui-suite-optional-until-ui-grows.md` exists at all. **A presenter is testable in the
ordinary suite** — no window, no desktop lock, no focus stealing — so the same logic that today can
only be checked by driving a real form becomes six ordinary unit tests. That is a direct answer to a
measured, recurring cost in this project rather than an abstract benefit.

### Sequencing

Owner, same conversation:

> once we finish this audio shit we will have to redo the 3dviewer/winforms project to properly
> follow MVP

So: finish the audio pipeline, then retrofit MVP. Not started here.

## D55 — the MVP contract: what the Form may not do, and where the Presenters live

**Recovered 2026-08-22 from the planning conversation, alongside D54.** These are the rules that
were agreed with MVP and never written down, so the viewer was built without them.

### The three roles

**View is a thin interface, not the Form.** The shape agreed at the time:

```csharp
public interface IDemoViewerView
{
    event Action PlayPauseClicked;
    event Action<int> ScrubberMoved;      // tick the user dragged to

    void SetCurrentTick(int tick);
    void SetPlaybackState(bool isPlaying);
    void SetEventList(IReadOnlyList<TimelineEvent> events);
}
```

`DemoViewerForm : Form, IDemoViewerView` implements it, and its job is *"purely mechanical
translation"* — a real button click raises `PlayPauseClicked`; `SetCurrentTick(500)` sets a label and
moves the scrubber. No knowledge of what a tick means.

**The tell, and it is a good one:**

> If a Form method needs an `if` statement about business state, that's the tell it's doing the
> Presenter's job.

**Presenter** holds the interface, not the concrete Form, plus the logic — "when the scrubber moves
to tick X, ask the parsed demo for the state at that tick, then push the result to the view". A
plain C# class with **zero UI framework dependency**.

**Model** is `Tf2DemoSalvage.Core`'s parsed demo and playback state. The Presenter mediates; Model
and View never know about each other.

### The assembly boundary is the mechanism, not the pattern name

This is the load-bearing rule and the reason MVP was chosen at all (D54): **Presenters live in a
project with no reference to `System.Windows.Forms`**, so a Presenter touching a `Button` fails to
compile. Only the Forms project references both the presentation layer and WinForms. The dependency
graph enforces the rule rather than a comment asking politely.

### The failure mode to watch, named in advance

> A single Presenter can grow into a "God Presenter" if one `IView` interface tries to cover too
> much — e.g., cramming playback controls, the event list, and render-surface state into one
> `IDemoViewerView`.

The fix is the D6 one: **several small view interfaces per cohesive concern** —
`IPlaybackControlsView`, `ITimelineEventListView`, `IRenderSurfaceView` — composed together in the
Form, rather than one interface that does everything.

**Current state, for honesty:** `MainForm.cs` is 4,436 lines, 95 methods and 103 fields, calls
`DemoTimeline.Build` itself and carries domain helpers. It is the God Presenter and the View at
once. The retrofit is queued behind the audio work at the owner's direction.

## D56 — enterprise patterns are refused; the decode path stays low level

**Recovered 2026-08-22 from the planning conversation.** The owner asked whether any enterprise
patterns would help, and the answer was a deliberate refusal rather than an omission — which is
exactly the kind of decision that leaves no trace in code and so cannot be inferred later.

> is there anything im not thinking of like maybe some enterprise patterns that will come in handy
> for this, or instructions to keep things as low level as possible for speed, because this app has
> to be super performant

### Refused, and why

Repository/Unit-of-Work, heavy DI containers, CQRS/MediatR pipelines, layered DTO + AutoMapper
stacks. They exist to let large teams work independently, decouple deployment, or abstract swappable
infrastructure. This is one developer plus an AI, one data source, one output format at a time —
so they are not merely unnecessary ceremony, they **fight the stated performance goal**: every
mapping layer, container resolution and repository abstraction is indirection and allocation
multiplied across thousands of demos in a batch.

### Kept, both already implied by earlier decisions rather than new ceremony

- **Strategy**, for per-protocol quirk handling (D1/D6's Open/Closed point) — one implementation per
  version range, selected once per file at parse start. This is the actual mechanism that makes "a
  new TF2 era does not require touching working code" true.
- **Builder**, optionally, for assembling the parsed result incrementally — *"only if the result
  object ends up complex enough to warrant it; don't add it preemptively."*

### The low-level rules, as agreed

| Rule | Reason given |
|---|---|
| **Span/Memory zero-copy parsing** | read over `ReadOnlySpan<byte>`, no intermediate `byte[]` per field. This is the specific capability that makes deferring native C (D2) justified rather than optimistic |
| **Memory-map the `.dem`** (`MemoryMappedFile`), not `File.ReadAllBytes` | avoids double-buffering a file that can be tens of megabytes for a long STV demo; lets the OS page cache work |
| **Structs, not classes, on the hot path** | tens of thousands of ticks × many entity deltas; class instances there are GC pressure that shows up in profiling |
| **No LINQ and no exceptions in the decode loop** | LINQ's iterator/delegate allocations and .NET's exception unwinding are both hot-loop traps. Malformed input is `TryParse`-shaped; exceptions are for outer-boundary failures ("this isn't an HL2DEMO file at all") |
| **Streaming/callback emission** | not "materialise every tick into one giant list first". Pairs with MVP's push model on the viewer side |
| **Parallelise across the corpus, never within one file** | a demo's command stream is inherently sequential — each tick's entity state depends on the prior tick's delta — so the throughput lever is a producer/consumer pipeline over many files, each decoded efficiently single-threaded |
| **BenchmarkDotNet from day one on the decode hot path** | D2 defers native C on the bet that C# is fast enough, *"and that bet needs actual measurement, not assumption"*. A benchmark showing a primitive cannot hit throughput in C# is the evidence D2 requires before reaching for C |

### What was actually done, measured against the above

- **Memory mapping**: adopted — 23 files use `MemoryMappedFile`. Never recorded until now.
- **Structs on the hot path**: adopted, extensively — the repository is full of record structs, to
  the point that `docs/memory/nullable-pattern-on-a-struct-is-dead-code.md` exists because of it.
- **Strategy for protocol quirks**: adopted.
- **Enterprise patterns**: correctly absent — no AutoMapper, MediatR or CQRS anywhere.
- **BenchmarkDotNet "from day one"**: **not honoured.** It was added on 2026-08-21, two weeks in and
  only when the MP3 decoder question forced it. D50 records adding it as though it were a new idea,
  because the decision requiring it had been lost.
- **No LINQ in the decode loop**: **unverified, and 97 files under `managed/` use LINQ.** That is not
  itself a violation — the rule is about the decode loop, not the codebase — but nothing has ever
  checked which of those are on the hot path. Filed as a risk rather than claimed either way.
- **Parallelise across the corpus**: not applicable yet; no batch mode exists.

## D57 — the era this project exists for, in the owner's words

**Recovered 2026-08-22 from the planning conversation.** Previously in this repository only
second-hand, as an assistant's paraphrase, and therefore never treated as scope.

> this is a niche within a niche not many people are going to use this im sure, but i wanna try its
> kinda important to me becasue i played tf2 from season 12 to the season right before esea ended
> tf2 maybe 2 seasons before

**Corrected 2026-08-22, immediately, because the first version of this entry got the scope wrong.**
It read the quote above as the *boundary* of what the project supports. It is not:

> the era scope is fully season 2, tf2 beta, till today, but the esea time period is the big thing
> im trying to get back the demos for

**Scope is the whole history — TF2 beta and season 2 through today. The ESEA period is the
PRIORITY within that, not the edge of it.** Those are different claims and conflating them would
have quietly licensed dropping support for anything outside a window, which is the opposite of the
project's founding sentence: *"we need to plan out how to parse tf2 demos of arbitrarily old age"*.

So the personal motivation and the technical scope are separate facts, and both are worth keeping:
the demos most wanted back are the ones from seasons this person played, and the parser is meant to
read everything from the beta onward regardless.

### Why it changes a priority rather than just adding colour

`docs/TIMELINE.md` treats protocols **17–23** as the remaining gap on the era axis — twenty-one
months between protocol 16 (15 June 2011) and protocol 24 (25 March 2013) — and has a whole section
on failing to find a specimen. It is currently framed as a completeness problem.

**The 17–23 gap sits inside the ESEA window, which is the priority band.** That moves it from "the
axis has a hole" to "the demos most wanted back may be exactly the ones we cannot decode" — a
different priority entirely, even though every era remains in scope.

Note the gap is a *decoding* priority, not a scope boundary: protocols 11 through 16 and 24 are
already measured and supported, so nothing outside the ESEA window is at risk from ranking 17–23
first.

**Stated as conditional on purpose, because the dates are not established.** ESEA season numbers
have not been mapped to calendar dates anywhere in this repository, and guessing them would be the
same error as dating a demo from its protocol number (`docs/memory/z1800-is-modern-not-2015.md`).
The mapping is discoverable rather than unknowable: `http://demos.igmdb.org/` carries **per-season
directories**, already cited in `docs/findings/01-container.md`, so a season maps to a date range by
reading what is in it.

**Do that before acting on this entry.** Until then the claim is "the target era plausibly overlaps
the gap", not "it does".

### Immediate application

Twenty demos from roughly 2011, from professional matches, are expected shortly. Protocol 16 is
dated 15 June 2011, so anything recorded after that date is a candidate for 17–23. **Read the
headers before anything else** — the protocol is in the demo header and needs no client — and apply
`tools/corpus/manifest.json`'s rule: a new protocol earns a place in gcor, everything else goes to
lcor, because GitHub's free LFS tier is 1 GiB/month and every CI job pays for it.

## D58 — the name is one fused word, because a dot must split two real things

**Recovered 2026-08-22 from the planning conversation.** The repository went `tf2-demo-parser` →
`tf2-demo-salvage` → `Tf2Demo.Salvage` (proposed) → **`Tf2DemoSalvage`**, and only the final name
survived into the repository. The rule that produced it did not.

### The rule, in the owner's words

> well this isnt an add on to another tool so I dont really like the dot, if the front wasnt tf2demo
> making it look like it was a plugin or addition for another app that would work, but I dont have a
> buisness name to put in front like newtonsoft, the base project everthing else is built on
> shouldn't have a dot thats not needed in it imo either, i dont really like that convention, because
> it cause some dots in your dot notation in code to not actually be real dot notation, there is no
> way to split newtonsoft from json and anything work so why not just call it NewtonsoftJson imo

**A dot is legitimate only when it separates two independently real things.** `Newtonsoft.Json`
earns one — a business and a library. `TcgDex.CSharpSdk` earns one — an API and which per-language
SDK this is. `Tf2Demo.Salvage` earns nothing, because there is no separate `Tf2Demo` product this
attaches to, so the dot would be decoration that *looks* like namespace structure.

The sharpest part is the last clause: a decorative dot **makes some dots in dot notation not real
dot notation**. `Newtonsoft.Json` cannot be split at its dot and have either half mean anything.

### What follows from it

- **The repo is `Tf2DemoSalvage`** — fused, PascalCase.
- **The namespaces follow**: `Tf2DemoSalvage.Core`, `.Cli`, `.Content`, `.Audio`, `.Viewer3D`. Those
  dots *are* the legitimate kind — product root, then a specific sub-project.
- **`Tf2`, not `TF2`**, which is .NET's convention for a three-character abbreviation.
- **PascalCase over kebab-case**, matching the owner's other repositories and .NET open source
  generally (`Newtonsoft.Json`, `AutoMapper`, `CommunityToolkit.Mvvm`), against the JS/Python/Rust
  lean toward kebab.

### Not a reason to rename anything else

The owner second-guessed `TcgDex.CSharpSdk` on the strength of this and was talked out of it:

> honestly that name for the sdk was probably bad because i used the tcgdex name like that with a
> dot, but i decided on that naming a long time ago to differentiate me from the already existing sdks

That dot is the legitimate kind — the language stands in for the product name, distinguishing this
SDK from siblings for the same API. The rule is *"a dot must split two real things"*, not *"dots are
bad"*, and applying it correctly leaves `TcgDex.CSharpSdk` alone.

### Inside the code: .NET conventions win, and the exception is narrow

**Added 2026-08-22.** This repository carries two standing instructions that can conflict — *match
Valve's conventions* and *keep to proper .NET naming* — and which one governs was never written
down. The owner's ruling:

> ive told you to match valve, ive also told you to keep to proper .net naming conventions wherever
> possible. The only time our naming may not be able to be .net convention, is when we have to name
> it to match the sdk or it wont be called

**The principle behind it, stated separately because it is broader than naming:**

> proper porting means using the conventions of the language you are porting to

That is the general rule and the naming ruling is one consequence of it. Transcribing a routine from
Valve's C++ into C# is a *port*, and a port that carries the source language's conventions across is
a bad port — Hungarian prefixes, `::`-flavoured static helpers, out-parameters where a tuple or a
record belongs. The behaviour is what must match; the spelling and the shape should look like the
language it now lives in. The owner has applied this consistently elsewhere, asking of NLayer —
itself a port of a Java decoder — whether "they at least do it properly and follow c# conventions".

**So .NET conventions govern identifiers, and the exception is not stylistic — it is when the name
is load-bearing.** "Or it won't be called" is the test: does anything resolve this name at runtime
or by string lookup? If yes, spell it the way the thing on the other side spells it. If no, it is a
C# identifier and gets PascalCase.

| Load-bearing — spell it Valve's way | Not load-bearing — spell it .NET's way |
|---|---|
| wire property names (`m_flCycle`, `m_iRawValue32`) — these are **string data**, matched against the demo's own schema | the C# property that holds a decoded value |
| shader and material parameters (`$detail`, `$basetexture`) | the type modelling a material |
| game event field names, string table names (`soundprecache`) | the reader that walks them |
| P/Invoke symbols, where the exported name must match | the managed wrapper method, which may rename via `EntryPoint` |

**Most apparent conflicts are not conflicts**, which is why this needed saying rather than
enforcing: `m_flCycle` and `$detail` live in *string literals* and lookup tables, and string
literals have no naming convention to violate. The genuine cases are narrow — an exported symbol, a
serialized member name — and everything else is an ordinary C# identifier that should look like one.

The failure this prevents runs in both directions. Naming a C# property `m_flCycle` because the wire
does imports Valve's convention into code that has no need of it; renaming the *string* to
`Cycle` because .NET prefers PascalCase breaks the lookup silently, and a silent lookup failure here
reads as missing data rather than as an error — see
`docs/memory/lookups-must-match-exactly.md`.

## D59 — the MVP work is a rewrite beside the old code, not a retrofit

**Owner's direction, 2026-08-22**, given while sequencing the work after audio:

> fix the audio, then start the mvp "retrofit" even though i hate the fact we even have to retrofit,
> so im tempted to tell you to "remove" the problem projects and just redo them, by which i mean
> build the replacement beside what we have so we can reference our current code, but remove it for
> being called so we can effectivly start from scratch

**Recorded as a stated lean rather than a locked decision**, because it was put as a temptation and
the audio work comes first. The final call is made when that finishes; this exists so the reasoning
is not lost in the meantime.

### Why a rewrite beats a retrofit here

A retrofit extracts presenters from `MainForm` incrementally. It never breaks the app and keeps the
suite green throughout — but the shape of the existing code biases every step, and the usual outcome
is MVP-flavoured code rather than MVP. D55's rule is an **assembly boundary**: presenters in a
project that cannot reference WinForms. That is not something a file arrives at by degrees; either
the project exists with the reference absent or it does not.

Building beside, with the old code present but no longer called, gets the boundary on day one and
keeps the previous implementation readable as reference.

### The risk, which is real and specific

`MainForm` contains a great deal of behaviour that was found empirically and never written down as a
requirement — the taskbar staying on top in full screen, focus that could not be asserted, a
transport bar drawn a quarter of the way up the viewport, an empty sidebar left docked. Several of
those exist in `docs/memory/` precisely because **they were caught by a person looking at the
screen, not by a test**. A rewrite drops that silently, and the 8 UI tests are nowhere near a safety
net for it.

**Two things make it survivable, and they should be treated as preconditions:**

1. **Scope the rewrite to the presentation layer only.** "The problem projects" is really
   `MainForm` and the logic it has absorbed — around 4,400 lines. `WorldRenderer`, `MapAssets`,
   `PropModels`, `EntityModels` and the rest are Model-side, carry 570 tests between them, and are
   not what MVP is about. Rewriting those would discard the most expensively-earned code in the
   repository for no architectural gain.
2. **Harvest before disconnecting.** Sweep `MainForm` for the empirically-found fixes and turn each
   into a test, a note, or a line in the new implementation *before* it stops being called.
   `docs/memory/` already names many of them and is the place to start.

### On leaving the old code in place

It is the right call for reference and the wrong call to leave permanently: this repository has
already been bitten by stale markers that outlived their subject (D45, and the five deleted gap
markers behind the viewer floor drop). The old form should be deleted in the commit that proves the
replacement covers it, not left indefinitely as a fossil nobody dares remove.

## D60 — the scene layer is its own project on plain `net10.0`, and the boundary is proved

**Owner's direction, 2026-08-22**, on being shown that only the presenters would gain a
compiler-enforced boundary under the first plan:

> we want as much on .net10 plain as we can, when we have to use -windows we cant run it on the
> linux boxes. I want the model compiler enforcement too this needs to be done right, not half
> assed

### The measurement that made this worth doing

`Viewer3D` was `net10.0-windows` because it hosts WinForms — and **that TFM cannot run on the ARM64
Linux measurement boxes**, so every file sharing the assembly was excluded from mutation testing and
fuzzing for a reason that had nothing to do with the file. Measured before moving anything:

| | |
|---|---|
| files in `Viewer3D` | 46 |
| files touching WinForms **or** Silk.NET | **11** |
| viewer tests that reference Direct3D at all | 64 of 570 |
| **tests locked to Windows by association alone** | **506** |

**Silk.NET's D3D11 bindings need no `-windows` TFM.** They are P/Invoke wrappers, so the render layer
is plain `net10.0` as well — only WinForms forces the Windows TFM.

### What was moved, and what the compiler found

35 files to `Tf2DemoSalvage.Scene`, `net10.0`. The pre-move check said no scene file referenced a
renderer or form type in code — five did, and **every one was a comment** ("see
WorldRenderer.DrawModel", the phrase "Program Files").

**That check was still wrong, and the compiler said so.** It grepped for type names matching FILE
names, and a C# file declares more than one type: `WorldVertex`, `WorldBatch` and `SunLight` sat at
the top of `WorldRenderer.cs` and are pure data — a vertex, a run of triangles, a light colour and
direction, carrying no Direct3D type at all. They belonged to the scene layer and now live in
`WorldGeometry.cs`. Worth recording as a method note: *grepping for file names does not enumerate
types*.

**A layering error surfaced that the TFM alone would never have caught.** `MessageQueue` and
`ForegroundProbe` P/Invoke `user32.dll`, and they compiled perfectly happily inside a plain
`net10.0` project — because **a plain TFM blocks WinForms *types*, not P/Invoke into Windows DLLs**.
Both are message-pump and foreground-window concerns; both went back to `Viewer3D` and back to
`internal`. Anything doing `DllImport`/`LibraryImport` on a Windows library is a Windows-layer file
regardless of what its project file says.

### The boundary is proved, not asserted

A temporary file using `System.Windows.Forms.Button` fails in `Scene` with CS0246, and so does one
using `Silk.NET.Direct3D11`. Both were compiled deliberately and removed. D54's argument was that
MVP's boundary *can be made a compiler error*; this is that claim tested rather than trusted.

### The cost, stated

35 types became `public` where they had been `internal`, because they are now a contract between
assemblies. That surfaced six analyzer complaints, split by what they actually are:

- **Fixed**: CA1002 (`List<string>` parameter → `ICollection<string>`), CA1062 (two missing null
  guards on newly-public methods) and CA2000 — the last by giving `MapDownloader` a `Create` factory
  so ownership of its `HttpClient` never crosses a call site, which is better API than the argument
  it replaced.
- **Suppressed as a false positive**: CA1027 on `TextureQuality`, whose members are 0, 1024, 512 and
  256 — *pixel dimensions*, powers of two because texture sizes are, not because they combine.
- **Suppressed as inapplicable**: CA1819 (arrays returned from properties) on per-vertex and
  per-lightmap draw-path data, which D56 explicitly requires stay low level.
- **Suppressed as a judgement call, and labelled as one**: CA1034 on `PropModels.SkinnedModel` and
  `PropModels.ModelFrames`. The rule's own alternative — make them not externally visible — is
  unavailable because the renderer consumes them, and un-nesting two large records with method
  bodies across ~62 call sites is real risk for a guideline aimed at published library APIs. Not
  dressed up as a false positive; revisit if `Scene` ever ships to anyone outside this solution.

**Verification: all 570 viewer tests still pass**, along with every other suite. For a move of this
size that is the only check that means anything.

## D61 — the renderer is its own project too, and `System.Drawing` had to go for it

**Continues D60.** With the scene layer extracted, `Viewer3D` still held six Direct3D files behind
the `net10.0-windows` framework. They are now `Tf2DemoSalvage.Render`, plain `net10.0`, leaving the
WinForms host as the only project on the Windows framework — five files.

| Project | Framework | Files |
|---|---|---|
| `Tf2DemoSalvage.Scene` | `net10.0` | 35 |
| `Tf2DemoSalvage.Render` | `net10.0` | 7 |
| `Tf2DemoSalvage.Viewer3D` | `net10.0-windows` | 5 |

**`System.Drawing` was the only thing standing in the way**, in exactly two places — a screenshot
writer in `Device3D` and a frame dump in `OffscreenTarget`. `System.Drawing.Common` is Windows-only
by design since .NET 7, so keeping it would have locked the render layer to Windows for the sake of
writing a PNG.

**So `PngWriter` was written**, and the requirement is genuinely small: one colour type, no
interlacing, no palette, filter 0. PNG's container is four chunks and a CRC, and the compression is
`ZLibStream` out of the framework. A package would have been larger than the code and carried
decoding, resizing and conversion that nothing here needs.

**`OffscreenTarget.SavePng` had no callers at all** — it was dead. It was reimplemented rather than
deleted because its own doc comment makes the case for the capability: *"A test that renders can
leave the picture behind, and it should."*

### The lenient decoder, which is the finding worth keeping

The encoder was verified differentially — encode with ours, decode with `System.Drawing`, compare
pixels — on the reasoning that a fixture cannot falsify your own reading of a specification.

**Then the sabotage passed.** Swapping `ZLibStream` for `DeflateStream` writes raw deflate with no
zlib header and no Adler-32 trailer, which PNG forbids, and **all eight round-trip tests still
passed**: Microsoft's decoder accepts it.

So the independent decoder is *more lenient than the specification*, which makes it the wrong
instrument for that particular claim — the same shape as
`docs/memory/a-faithful-fixture-can-be-blind.md`, and with the same remedy: measure the thing
directly instead of strengthening an assertion that was never sensitive. A byte-level check of the
IDAT payload against RFC 1950 §2.2 — low nibble 8, and the first two bytes a multiple of 31 — now
fails on that sabotage while the round-trips continue to cover the pixels.

**A differential test is only as strict as the other implementation.**

### The `.editorconfig` followed its code

The S6640 suppression (*"avoid using this unsafe code block"*) was scoped to `Viewer3D` and its
comment said "scoped to the Direct3D layer only". That layer is now its own project and **no file
left in `Viewer3D` uses `unsafe` at all**, so the file moved with the code it was written for rather
than staying with the folder name.

### A gap marker's control did its job

`Fog_NothingInThisRendererReadsTheDecodedFog_WhichIsB139` went red — **on its control, not its
claim**. The sweep looks for `SceneFog` in `typeof(WorldRenderer).Assembly`, which correctly followed
the renderer into its new project; the control asked whether the same sweep could find a scene type
the renderer really uses, and named `DemoTimeline` — which the *form* consumes, not the renderer.

The claim was still true, so without the control the suite would have gone on asserting an
unfalsifiable "not referenced" forever. `MapAssets` replaces it, since `WorldRenderer.UploadTextures`
takes one. This is D45's rule earning its keep: a gap marker must be able to fail.

## D62 — the first presenter: playback, lifted out of the form with its tests

**The MVP work D54 and D55 describe, starting.** `Tf2DemoSalvage.Presentation` exists, on plain
`net10.0`, referencing **Core alone** — not Render, not Scene, and above all not WinForms.

**The absence of that reference is the design.** D54 records why MVP was chosen over MVVM and it was
not familiarity: *"MVP's boundary can be made a compiler error, not just a convention someone (or
something) has to remember to follow."* A presenter here that reaches for a `Button` does not
compile, and nothing else is doing that work.

### Why playback went first

Four methods and three fields in `MainForm`, entangled with a WinForms control and a live
`Stopwatch`. **Every rule in it was written from a bug, and not one had a test**, because reaching
the logic needed a form, a message pump and the desktop lock. The behaviour is also genuinely subtle
— three separate stopwatch rules that look arbitrary until each one's reason is stated:

- **Starting play restarts the elapsed clock.** Real time passed while paused is not playback time;
  without this the first frame after resuming jumps the demo forward by however long the user spent
  reading the map.
- **Pausing resets it**, so that gap cannot accumulate while stopped.
- **Changing speed restarts it, but only while playing**, so the frame straddling the change is not
  counted at the new rate.

Plus the cap: a stall — loading a map, dragging the window by its title bar — is not elapsed
playback time, and handing the whole gap to the clock teleports the demo. 100 ms turns a hitch into
a brief slowdown.

### `IElapsedTime` is what made any of it testable

The presenter takes where time comes from. In production that is a `Stopwatch`; in a test it is a
number the test sets. **Without it every rule above could only be observed by sleeping**, which this
project bans outright — and which would have turned deterministic checks into probabilistic ones.

That one interface is the difference between sixteen tests running in 24 ms and none existing at
all.

### What the suite covers that nothing covered before

Sixteen tests, no window, no STA thread, no `run-exclusive.ps1`, and it runs on the Linux boxes.
Two were verified by sabotage: removing the stall cap reddens the clamp test, and stopping only at
`AtEnd` reddens the reverse-playback test — the latter being a case that forward-only testing cannot
reach, since reverse playback spinning against tick zero still claims to be playing.

The controls matter as much as the claims. *"Speed changed while paused does not restart"* exists so
that *"speed change restarts"* and *"everything restarts"* are distinguishable, and the moment test
asserts the FRACTION (0.5) as well as noting the whole tick is still 0 — because truncating there
makes the interpolation layer a no-op that passes all of its own tests.

### `EventArgs` types rather than `EventHandler<int>`

CA1003 rejects the latter, and the named types read better at the call site anyway: `e.Tick` says
what the number is where `(_, tick)` only reports what somebody named a parameter. Three tiny
records — `TickEventArgs`, `PlayingEventArgs`, `SpeedEventArgs` — plus `MomentEventArgs` for the
fractional position going the other way.

### Not yet wired

`MainForm` still owns its copy. The presenter is built beside it, per D59, and the form is switched
over only when the replacement covers it — with the old code deleted in the commit that proves it,
not left as a fossil.

## D63 — the playback presenter is wired, and writing its interface found a real bug

**D62 built the presenter beside the form; this connects it and deletes what it replaced**, which is
the order D59 requires — the old code goes in the commit that proves the replacement covers it,
rather than being left as a fossil nobody dares remove.

`TransportBar` now implements `IPlaybackView` directly. It *is* the view, so an adapter would have
been a second object doing nothing but renaming events.

| | before | after |
|---|---|---|
| `MainForm.cs` | 4,436 lines | **4,379** |
| playback logic in the form | 3 event handlers, `AdvancePlayback`, a `Stopwatch` | `_playback.Advance()` and one redraw handler |
| tests over that logic | **0** | **16** |

### Writing the interface found a defect that had always been there

`IPlaybackView` documents that setting `Playing` must **not** raise `PlayPauseToggled`, or the
presenter re-enters its own handler. Writing that rule down forced a look at the real control, and
**`TransportBar.Playing`'s setter raised the event** — so the contract was violated by the very
control the interface was written for.

That conflated two genuinely different things: *the user pressed the button*, and *somebody assigned
the property*. `SetDemoLength` assigns it, and the presenter assigns it when playback reaches an end.

**The control already knew this distinction and had simply never applied it here.** `ShowTick`'s own
summary says it moves the readout "without raising `Scrubbed`" — ticks had the rule, playing did
not. The fix is `TogglePlayingByUser()` for the button, leaving the setter to update state and
labels only.

**This is the argument for interfaces stated concretely.** The bug was invisible while the form and
the control were one tangle, because the form never assigned `Playing` from a path that could
re-enter. It became visible the moment the boundary had to be *written down* — which is the same
mechanism as D54's compiler-enforced boundary, one level up: the compiler enforces what you write,
and writing it is what makes you look.

### Verification

All seven suites green, and **the UI suite too — 12 tests under `run-exclusive.ps1`** — which is the
one that actually drives the transport control whose event contract changed. Running it was not
optional here: `docs/memory/ui-tests-run-every-time.md` says every change, and this change is
precisely the kind the ordinary suites cannot see.

### What is left

Playback is one concern of roughly seven still in the form: camera, demo library and playlist, map
loading, scene composition, render-loop hosting, settings and full screen, capture. The pattern is
now proven end to end on the smallest of them, which was the point of doing it first — extracting
six more on an unvalidated pattern is how a design flaw arrives six presenters late.

## D64 — MVP's payout, measured: three of four defects came from the boundary, not the tests

**Owner, 2026-08-22, confirming the decision after watching it land:**

> the bugs you found and extra things you have been able to test are one of the big upsides of MVP,
> its separation of concerns, which enables testability

Recorded because D54 was decided on an *argument* before the repository existed, and this is the
first time it can be scored against evidence. It is also a sharper claim than "separation enables
testing" — **most of the value arrived before any new test ran.**

| Surfaced by | Defect |
|---|---|
| **Writing `IPlaybackView`** | `TransportBar.Playing`'s setter raised its own change event, so a presenter assigning it re-entered its own handler (D63). |
| **Extracting `Scene`** | `WorldVertex`, `WorldBatch`, `SunLight` — pure data — declared inside the renderer; `MessageQueue` and `ForegroundProbe` P/Invoking `user32.dll` from a project meant to be portable (D60). |
| **Extracting `Render`** | A gap marker's control named a type the renderer never consumed, leaving its claim unfalsifiable (D61). |
| The new tests | 16 playback rules with no coverage at all (D62). |

**Why three of four came from the boundary rather than the suite, and it is not luck.** A test asks
whether code behaves correctly *inside the structure it has*. A boundary asks whether the structure
is right, so it reaches defects that are invariant under every test you could write against the old
shape.

The re-entrancy bug is the clean example: **it had no failing input.** The form never assigned
`Playing` from a path that could re-enter, so no test over the old code — however thorough — could
have gone red. It became reachable only when a presenter started assigning that property, and
visible only when the rule had to be written into an interface.

**The practical form of this**, and it is the part worth carrying into the six remaining concerns:
writing an interface is an *inspection*, not paperwork. The sentence you are forced to state — "this
setter must not raise that event" — is the moment somebody checks whether the real implementation
obeys it. Nobody had ever had to state it.

## D65 — splitting the key mapping from the flight geometry, and the bug that fell out

**The camera concern, started.** `FreeFlight.Movement` took an `IReadOnlySet<Keys>` and did two
unrelated jobs: decide that W means forward and Ctrl means down, and then work out where that puts
the camera given a pitch and a yaw.

- The first is a **view** concern. It is about a keyboard, and rebinding it changes nothing about
  the movement.
- The second is trigonometry, and it is the half worth testing.

Welded together, the geometry could only be exercised by constructing WinForms key sets — **which is
why a function of sines and cosines had no direct tests for weeks.** `FlightInput` is the seam:
`FreeFlight.Intent` maps keys to axes and stays in the viewer, `FreeFlightPath.Movement` does the
geometry and lives in the presentation layer.

### The defect this surfaced immediately, and it was reachable

The first run of the new tests went red on the cancellation case, and the cause was inherited rather
than introduced:

```csharp
if (length <= 0f)   // "Opposed keys cancelling exactly"
```

**Floating point almost never produces exactly zero.** Fly forward and up while looking straight
down and the two cancel — but `cos(90°)` is **−4.4e-8**, not 0. So the length came out at 4.4e-8,
passed the guard, and the normalisation `travel / length` scaled that residue up to the full travel
distance: **the camera jumped 300 units sideways instead of standing still.**

**Reachable, not theoretical.** The mouse drag clamps pitch to ±89, with a comment saying the basis
is degenerate looking along the world's up axis — so the author knew. But `ParseCamera`, which reads
`TF2DEMOSALVAGE_CAMERA` and exists specifically to reproduce an exact viewpoint copied out of the
game's own `ang` readout, **did not clamp at all**. Pitch 90 is an ordinary thing to copy.

Fixed at both ends, because either alone leaves a trap:

- `FreeFlightPath.Movement` guards with an epsilon of `1e-4` — far below any genuine input, since
  the axes are ±1 and the smallest real resultant is of order one, and far above the residue a
  cancellation leaves. **A function should not depend on a clamp somewhere else to protect its own
  division.**
- `ParseCamera` clamps pitch to the same ±89 as the drag, so it stops producing an angle the rest of
  the viewer treats as impossible.

### This is D64's pattern again, and the fifth instance

The bug had **no failing input** in the old shape: nothing constructed a key set at pitch 90, so no
test over `FreeFlight.Movement` could have gone red. It became reachable only once the geometry took
numbers instead of keys, and visible on the first run of the tests that became possible.

### The constants are forwarded, not copied

`FreeFlight.SpeedPerSecond` and `ShiftMultiplier` now forward to `FreeFlightPath`. Two copies of a
speed is two speeds waiting to disagree, and the disagreement would show up as a camera that flies at
one rate while its tests assert another.

### Verification

Ten new tests over the geometry — the axis convention at two yaws, the pitch sign, diagonal
normalisation, world-up regardless of pitch, and the cancellation case that found the bug. The
existing eleven `FreeFlightTests` in the viewer suite still pass unchanged, which is the check that
the key mapping was moved rather than altered.

## D66 — the free camera's own state, and the overhead placement that replaces the ortho camera

**Two pieces of the camera concern, both pure and both previously untestable.**

### `FreeLookState` — where the camera is and which way it faces

Three fields and two event handlers in `MainForm`. No viewport, no control, no logging, so nothing
but the company it kept ever made it untestable.

**Pitch is clamped and yaw is not, and the asymmetry is the engine's.** Looking exactly along the
world's up axis makes the basis degenerate — forward parallel to up, no right vector — so the engine
clamps a player to ±89 and this does too. Yaw wraps and every value is a legal heading; bounding it
would introduce a discontinuity the geometry does not have, and a camera that stops turning after
enough drags one way.

`PlaceAt` clamps as well, which is the other half of D65: `TF2DEMOSALVAGE_CAMERA` exists so a
viewpoint can be copied out of the game's own `ang` readout, 90 is an ordinary thing to copy, and the
original path applied it raw.

### `OverheadPlacement` — the ortho camera's replacement

**Owner, 2026-08-22**, on the pending removal:

> remember the ortho cam is going to be pulled out, so if you just want to pull that out now, and
> set the free cam to have a start position thats not under the map, thats fine

D49 committed to this: the overhead view becomes a *placement* of the free camera rather than a
second projection. So this computes an origin and a pair of angles — not a projection, not a mode.
The camera flies away from it normally afterwards, and there is nothing to switch between.

- **Above the play area's centre**, not the world origin; plenty of Source maps are built well away
  from it.
- **Pitch 89, not 90**, for the degeneracy reason above.
- **Framed on whichever axis is tighter.** A map is rarely square and a viewport never is, so fitting
  the depth alone leaves a wide map cropped — the classic zoom-to-fit mistake, which looks like the
  map being bigger than it is rather than like a framing bug.
- **Height is the greater of the framing distance and the tallest geometry plus clearance.** That is
  the owner's "not under the map" requirement made precise: framing alone can sit below a skybox
  brush on a wide flat map, and clearance alone crops a large one.

### Sequencing, which this changed

**The ortho removal has to come before the camera presenter, not after.** A presenter modelling
`Map`/`Free`/`FirstPerson` modes and owning `_zoom` and `_lookingAt` would be built for code that is
about to be deleted. The placement above is the piece that makes the removal possible, so it comes
first and the presenter is shaped by what survives.

Presentation floor 26 → 46.

## D67 — the free camera starts above the map, and why the skybox does not break that

**Owner, 2026-08-22:** *"set the free cam to have a start position thats not under the map"* — and,
on being shown the anchor: *"im not sure how well highest point is going to work because technically
the skybox can be placed anywhere outside teh main map"*.

### The bug that was there

The entry placement orbited `FreeFocus()`, which anchored to **`_heightRange.Lowest` plus an eye
height**. Its comment gives the reasoning, and the reasoning is sound as far as it goes: the middle
of a map's vertical range is nowhere anybody stands, because that range includes the skybox and the
basements.

**The correction overshot.** The *lowest* drawn geometry is a basement floor or the underside of a
displacement, so entering the free view started the camera below the map rather than above it.

### The skybox objection, and why it is already handled

It is a real hazard — a TF2 map carries its 3D skybox as ordinary world geometry at reduced scale,
placed at an arbitrary offset — but this project solved it before, twice over, and the height range
already inherits both fixes:

- **`MapOutline.MainBounds` is the largest connected cluster of geometry.** Measured across nine
  shipped maps, that cluster holds 91.1%–99.7% of all points, and the outliers are single digits.
- **`_heightRange` is computed against those bounds**, not the whole file:
  `MapWorldBuilder.HeightRange(_surfaceList, _map.MainBounds)`. So the skybox is excluded in Z as
  well as in X and Y.

**And `sky_camera` was tried as the marker and rejected**, which is worth repeating because it is the
obvious thing to reach for: the entity is placed to *view* the skybox room rather than to sit in it,
and on four of the nine maps it falls outside every cluster of geometry altogether.

### Valve has nothing to copy here

The nearest thing in the engine is `cl_leveloverview`, a developer cvar that renders a top-down view
so someone can make a radar image — and even that leans on per-map numbers a mapper writes by hand
into a `.txt`. No Valve game needs "frame this map from above" at runtime, so no engine algorithm
exists for it. The connected-cluster heuristic is this project's own, and it is measured rather than
assumed.

### What went with it

`FreeFocus` and `PlayerEyeHeight` are deleted. The constant was correct about Source — `VEC_VIEW` is
64 for a standing player — but nothing reads it now: the first-person camera takes its eye position
from the demo rather than from a constant.

**Not yet verified by eye.** The arithmetic is tested and the camera is provably above the highest
play-area geometry, but whether the framing *looks* right is a UI claim, and the owner has said
matching the old ortho view exactly is not the concern yet — being above the map rather than under
it is.

## D68 — actions are bound, not keys, and the defaults are TF2's

**Owner, 2026-08-22:** *"the key should be settable by the user, so dont hard code it, cam changes
should be on space like valves controls in real tf2, if i remember right"* — and, on the awkward
ones: *"we keep the same defaults then, but allow them to be changed just like tf2"*.

### The shape, which TF2 supplies

TF2 does not hardcode keys anywhere the player can see. Its spectator HUD carries

```
"TF_Spectator_SwitchCamModeKey"   "[%jump%]"
"TF_Spectator_SwitchCamMode"      "Switch Camera Mode"
"TF_Spectator_CycleTargetFwdKey"  "[%attack%]"
```

— the label names the **action** and the engine substitutes whatever the player bound. `ViewerAction`
plus `KeyBindings` is the same idea: the presenter deals in actions and never sees a key, the view
owns the mapping, and rebinding changes one table.

**This extends the seam D65 opened.** That split "which keys mean what" from "what the movement is";
this makes the first half data instead of code.

### The defaults, all read from the game rather than remembered

| Action | Key | Source |
|---|---|---|
| Switch camera mode | `Space` | `[%jump%]` in `tf_english.txt` |
| Cycle target forward / reverse | `MouseLeft` / `MouseRight` | `[%attack%]`, `[%attack2%]` |
| Fly up / down | `'` / `/` | `bind "'" "+moveup"` in `config_default.cfg` |

**The vertical keys are the interesting ones.** `in_main.cpp` builds the roaming camera's vertical
from `in_up`/`in_down` — that is `+moveup`/`+movedown`, **separate commands from `+jump`** — so
Valve has no collision between "go up" and "switch mode".

I proposed E and Q instead, on the grounds that `'` and `/` are Quake-era leftovers nowhere near
WASD. **Overruled, and the owner's reasoning is better:** a TF2 player's own config translates, and
rebinding is the escape hatch exactly as in the game. Picking friendlier keys would make this
viewer's controls a third thing to learn. *"them being all the way over there is why i never used
them"* — which is a criticism of Valve's default, not a reason for us to invent a fourth convention.

### The collision that bit, because it is instructive

`FlyUp` was on `Space` alongside `SwitchCameraMode` for one commit. `ProcessCmdKey` checks flight
keys first, so it swallowed the press, the camera mode never switched, and **three UI tests failed
by timing out on a key that did nothing** — with Windows dinging on every unhandled press.

The owner diagnosed it from the sound before any log said anything, which is another entry for
`docs/memory/` on UI defects living where automated instruments do not look.

Two things came out of it:

- **`Defaults_NoTwoActions_ShareAKey`**, which would have caught it. Sharing a key is legal and
  `ActionsFor` deliberately reports every match — but a *default* that shares one is a control the
  user cannot reach out of the box, and it fails by doing nothing.
- **UI tests now press what is bound**, asserting the binding first. A test pressing a literal key
  fails the wrong way when a binding moves: it times out rather than saying the binding changed.

### `KeyNames` is the other half of the boundary

`KeyBindings` cannot reference `System.Windows.Forms.Keys`, so a binding is a **name** and
`KeyNames` resolves it. That also means bindings survive in a settings file as text a person edits,
which is what `config.cfg` does.

Note `'` and `/` are `Keys.OemQuotes` and `Keys.OemQuestion` — named after scan codes rather than
the characters printed on them, so neither resolves by `Enum.TryParse` and both would otherwise
become `Keys.None`: a binding that reads correctly in a file and does nothing.

## D69 — a real TF2 config must work wholesale, and the earlier "small return" assessment is reversed

**Owner, 2026-08-22:**

> i want someone like myself to be able to just copy and paste there tf2 configs over wholesale, in
> .cfg or vpk form like comfig's configs

### This is a REVERSAL, and the original is the instructive part

`ROADMAP.md` already carried the requirement, filed like this:

> Reading the user's **actual TF2 cfg** for camera controls is wanted eventually, but the return is
> small: apart from sensitivity, little transfers — a personal cfg is mostly movement scripts, which
> a viewer has no use for. Copying TF2's default camera bindings gets almost all of the benefit.

**So it was not lost. It was recorded with a dismissive assessment, and the assessment governed the
work.** D68 built precisely "copy TF2's default bindings" earlier the same day and treated it as the
finished feature — because the roadmap said that was almost all of the benefit.

**That is a worse failure mode than the MVP one (D54).** A lost decision leaves a gap somebody may
notice. A decision recorded with a wrong judgement attached looks like due diligence, so nobody
re-opens it — the reasoning is right there, apparently considered.

### Why the assessment was wrong

**The value is not in which commands transfer; it is in the user not having to set anything up.**
Someone running mastercomfig has already made every one of these decisions once, and being asked to
make them again in a second, different settings file is exactly the friction the requirement exists
to remove.

"Mostly movement scripts" is also true of the *lines* and false of the *file*: the binds are what
matter here and they are all present. Counting lines measured the wrong thing.

### What follows, including one thing already built wrong

- **Our vocabulary must BE Source's.** Keys named `SPACE`, `CTRL`, `MOUSE1`, `'`, `/`; actions named
  `+forward`, `+jump`, `+moveup`, `+speed`. D68 used its own names (`"MouseLeft"`,
  `SwitchCameraMode`) which would need a translation layer — and a translation layer means the paste
  does not work, which is the requirement.
- **Ignoring is the primary feature.** A real config is hundreds of `mat_*`, `cl_*`, `alias` and
  `exec` lines this viewer does not implement. A parser that objected to unknown commands would
  reject every real file it was pointed at.
- **`unbindall` and later-wins ordering are honoured**, because `config_default.cfg` opens with the
  first and `exec` layering depends on the second.
- **VPK form is in scope.** mastercomfig ships as `.vpk` under `tf/custom/`, this project has already
  read one (`docs/findings/24-reference-capture.md`, against `mastercomfig-base.vpk`), and
  `VpkArchive` is the tool.

### The cost of the wrong reading, stated

A typo in a binding and a command this viewer does not implement are indistinguishable — both do
nothing. So the reader returns **every** bind it saw rather than only the ones that mapped, leaving a
caller able to report the difference rather than swallowing it.

## D70 — a config is a program, so the viewer runs one rather than reading one

**Owner, 2026-08-22, while D69's static reader was being finished:**

> yea since we are going to take valve cfgs, we have to allow scripting or it wont work. valve
> configs are little state machines themselves

**He is right, and D69's first implementation was wrong because of it.** That version resolved a
bind statically: follow `+mfwd` to its alias body, take the first command it recognised, record
`w -> FlyForward`. It reads a script as though it were a table.

### Why static resolution cannot work, stated precisely

`alias` is a **runtime** command, and null-cancelling movement scripts — which is what most
competitive configs are — use it to redefine *other* aliases as they run:

```
alias +mfwd   "-back;      +forward;   alias checkfwd   +forward"
alias -mfwd   "-forward;   checkback;  alias checkfwd   none"
```

`checkfwd` means `none` before W is pressed and `+forward` afterwards. **The same name has two
meanings and which one is current depends on what has been pressed.** A static reader must pick one,
and whichever it picks is wrong half the time. This is not an exotic case; it is the owner's own
`config.cfg`.

### What was built

`Tf2DemoSalvage.Presentation/ConfigConsole.cs` — an interpreter with a mutable alias table, a bind
table, and one `kbutton_t` per action. It is the single source of truth for the controls;
`KeyBindings` became a projection of it for the settings screen to display.

Everything in it is read from `src/game/client/in_main.cpp` and `kbutton.h`, both published in
`source-sdk-2013`, and pinned by nineteen conformance tests written **before** the implementation:

| Behaviour | Where it comes from |
|---|---|
| `+foo` presses, `-foo` releases | `IN_ForwardDown`/`IN_ForwardUp` both take `args[1]` |
| a button holds **two** keys | `int down[ 2 ]` in `kbutton_t`; `KeyUp` returns early while either is set |
| a third key on one button is dropped | `DevMsg( 1,"Three keys down for a button ..." ); return;` |
| a repeat is ignored | `if (k == b->down[0] \|\| k == b->down[1]) return; // repeating key` |
| a key-up with no key-down is ignored | `return; // key up without coresponding down (menu pass through)` — typo Valve's |
| partial-frame credit of 0.25/0.5/0.75/1 | `CInput::KeyState`'s four impulse cases |
| reading the state consumes it | `key->state &= 1;` as the last statement of `KeyState` |

### Two findings that only a failing test produced

**1. The key number does not survive into an alias body, and the whole pattern depends on it.** The
engine appends the key to the command a key is *bound* to (`+mfwd 32`), and Source aliases take no
parameters — so `+forward` inside that body runs with nothing. `KeyUp` treats an empty argument as a
reset:

```c
if ( !c || !c[0] )
{
    b->down[0] = b->down[1] = 0;
    b->state = 4;   // impulse up
    return;
}
```

So `-forward` issued from inside `+mback` releases forward **no matter which key holds it**. Had the
key propagated, that release would have been discarded as unmatched and null-cancelling would do
nothing. The first implementation propagated it, and exactly one conformance test caught it.

**2. The release line flips ONE character, not every `+`.** Two independent binding layers in the
SDK build it identically — `in_sixense_gesture_bindings.cpp` writes
`m_pDeactivateCommand[0] = '-'` and `in_steamcontroller.cpp` writes `cmdbuf[0] = '-'`, both after
testing only `[0]`. Consequences, both reproduced deliberately:

- a bind not starting with `+` runs **nothing** on release;
- `"+forward; +moveright"` releases as `"-forward; +moveright"`, so the second button sticks down
  for ever.

**The second is a real Source footgun and it is why competitive configs wrap compound binds in
aliases.** A viewer that quietly improved on it would behave differently from the game the config
was written for, which is worse than reproducing the flaw. **A conformance test here asserted the
opposite before the SDK was read** — it was written from an assumption about what the engine
"obviously" does, and the citation corrected it. That is the case for writing these first and
citing them: the wrong answer was plausible enough to have shipped.

### What this cost elsewhere

- **`FreeFlight.Movement`, `Intent`, `Axis` and `IsDown` were deleted.** Once `MainForm` drove the
  console, nothing called them — and **eleven tests went on passing against dead code**, which reads
  as coverage. Those tests were repointed at the live path rather than removed; the assertions were
  fine, only their subject was wrong. This is
  `docs/memory/output-level-assertion-or-it-is-not-done.md` arriving from the other direction.
- **Shift moved from `Control.ModifierKeys` into the console**, because `+speed` is a bound command
  like any other. Reading it separately would have been a second source of truth for one fact, and
  the failure would have been silent: a camera that simply never goes fast.
- **The sided-modifier special case moved to `KeyNames.NameOf`**, so `ShiftKey`/`LShiftKey`/
  `RShiftKey` collapse onto the one name a config binds. It used to live in `FreeFlight.IsDown`, and
  having it in two places is how one side gains a key the other does not know about.

### Deliberately not implemented

Cvars, `exec`, `wait`, `toggle`, `incrementvar`, and the several hundred game commands this viewer
has no concept of are skipped in silence. A real config is mostly commands we ignore, and objecting
to them would reject every file it was pointed at. `Bound` and `Applied` are reported as a pair so a
caller can distinguish "your config loaded and bound nothing" from "your config did not load", which
otherwise look identical.

### Measured

The owner's own `config.cfg` plus `autoexec.cfg`: 78 binds, **13 applied**, up from 5 under static
reading and 0 when either file was read alone. Pressing W flies forward; holding S overrides it;
releasing S resumes forward with W never touched. That last sequence is asserted against the real
files in `RealTf2ConfigTests`.

## D71 — a config's silence about a feature the game lacks is not a preference

**Found by the diagnostic written one hour earlier, on the owner's real install.** Loading his three
configs logged:

```
[config] 3 files, 9 of 95 binds applied
[config] no key reaches: ResetCamera, PlayPause, FlyFast
```

**Three viewer controls, disabled by loading a config.** The mechanism is plain once seen:
`resetcamera` and `playpause` are *this project's own* command names, invented in D68 because TF2
has no equivalent for either. A TF2 config therefore cannot bind them — it just uses `f` and `k` for
its own purposes, and the keys those actions lived on are taken away with nothing put back.

**`+speed` is the same case in practice.** TF2 has no sprint, so the command appears in essentially
no config, while `bind "SHIFT" "+duck"` is completely ordinary — his config has exactly that. So
fly-fast loses its key for most people who paste a config in.

### The rule, and the reasoning that was wrong first

**A key whose config binding does nothing in this viewer keeps whatever this viewer had on it.**

The first implementation did the opposite and had an argument for it: the player said Shift is duck,
and overriding them would be the viewer claiming to know better than the file it was told to obey.
That argument is right about `+duck` and wrong about the general case, because **a config cannot
express a preference about a feature the game does not have.** Reading its silence as one is
inventing intent.

**Nothing is lost by falling back, which is what makes this safe rather than a guess.** The fallback
applies only when the config's command for that key does nothing here — the key was inert either
way, so no conflict is possible.

**The fallback yields the moment the config gives the action a new home.** Binding `CTRL` to
`+speed` and `SHIFT` to `+duck` is a player *moving* fly-fast, not losing it, so Shift must stop
doing it — otherwise two keys answer to one action and the settings screen picks one arbitrarily.
A conformance test caught that as a wrong key rather than as a crash, which is why the rule is
"unless the config claimed this action elsewhere" rather than the simpler "defaults always win".

**An action the config genuinely takes over is still reported unbound**, by `ConfigConsole.Unbound`.
Binding `SHIFT` to `+forward` is a statement this viewer can act on, so fly-fast really does lose
its key and the log says so.

### Why no test could have predicted this

Every fixture in `SourceConfigTests` was written by the same person who wrote the parser, and none
of them binds `f` or `k`, because there is no reason to unless you are looking at a file written by
somebody who had never heard of this program. **The real config binds keys for reasons that have
nothing to do with this viewer**, and that is the whole content of the finding.

The instrument that caught it was a diagnostic added for a different purpose — reporting unreachable
controls — pointed at real data. `docs/memory/output-level-assertion-or-it-is-not-done.md` again,
and the assertion now lives in `Tf2ConfigFilesTests`.

**Measured after:** the same install goes from 9 of 95 binds applied to 12, and nothing unreachable.

## D72 — spectator target cycling, and the test shape that was missing

**B145: the feature existed as a binding and nothing else.** `CycleTargetForward` and
`CycleTargetReverse` were declared, bound to `MOUSE1`/`MOUSE2`, given the Source command names
`+attack`/`+attack2`, and asserted on by three tests. No production code read them, so clicking
cycled nothing.

**The tests were not wrong.** They checked that a binding table held what it should, and it did.
**Nothing about a binding table can tell you whether anything consults it** — and no unit test of a
search can either, because the search would have been fine. It was simply never called.

### What the SDK settled, and what it did not

`source-sdk-2013` ships TF2's own game code, so this needed no decompiler
(`docs/memory/tf2-game-code-is-in-the-sdk.md`). From `CTFPlayer::FindNextObserverTarget` and
`GetNextObserverSearchStartPoint`:

- the search starts one step past the current target (`startIndex += iDir`), so a cycle never
  returns the player already being watched;
- **both directions wrap**, written as two explicit arms — a one-armed wrap works for exactly as
  long as nobody cycles backwards;
- **null when nothing is found**, and the caller's use of it is the point:
  `if ( target ) SetObserverTarget( target );`. A failed cycle leaves the camera where it was, which
  matters because the first seconds of a competitive match really are SourceTV alone.

**And from `ClientModeShared::HandleSpectatorKeyInput`, the part that decided the design**: the
engine dispatches on `pszCurrentBinding`, the bound command *string* — `Q_strcmp( pszCurrentBinding,
"+attack" )` — not on a key code. So the viewer feeds mouse buttons into `ConfigConsole` under their
Source names and acts on the action that comes back. Someone who has moved attack to a thumb button
gets cycling on that thumb button, and no code in `MainForm` knows about it.

**Not copied, deliberately.** `IsValidObserverTarget` also admits buildings, observer points and a
coached student, and rejects `target == this`. Neither transfers: this viewer follows players, and
"this" is the recording client — in a POV demo, precisely who you want to watch. A rule copied
without its context is the kind that gets confidently repeated.

**Ordering is ours and is stated as such.** TF2 walks `m_hObservableEntities`, rebuilt per search and
holding more than players. This walks entity index ascending, matching `SpectatorTarget.Choose`, so
the first click does not jump somewhere unrelated to the default target.

### The stale mouse names

`KeyNames.Resolve` still special-cased `MOUSELEFT`/`MOUSERIGHT`/`MOUSEMIDDLE`, .NET's vocabulary,
which the D69 move to Source spellings had left behind. They were **dead as written and correct only
by accident**: the live names `MOUSE1`/`MOUSE2` fell through to the `Enum.TryParse` fallback and also
produced `Keys.None`. Two ways of being right for different reasons is how a rename goes unnoticed.

### The test shape, which is the transferable part

Three levels, and the middle one is the one this project keeps missing:

1. **Conformance**, from the SDK with citations, written before the code — eleven tests.
2. **Real data**, three tests walking z1800's actual player list: a full lap visits all 24 playing
   players and returns to the start, forward-then-back is a no-op from every position including both
   wrap points, and 222 cycles across the match never land on the SourceTV camera.
3. **The wiring**, one UI test that clicks the real button in the real window and asks whether the
   spectator code ran — plus its control, that clicking in the free camera does not cycle, since
   `spec_next` is gated on being in an observer mode.

**Only the third can fail if the wiring is removed, and it was verified that way**: deleting the
`CycleTarget` call reddened it and nothing else. A feature that reached no code is exactly what
happens when levels 1 and 2 are present and level 3 is not — which is the whole of B145 and of
`docs/memory/output-level-assertion-or-it-is-not-done.md`.

**A POV demo would have been the wrong specimen at level 2** and would have passed while measuring
nothing: the committed era POVs are the owner's solo recordings, so a cycle finds one target and
stops — indistinguishable from a broken search
(`docs/memory/pov-demos-are-pvs-limited.md`). The UI session opens one of those, which is why the
UI test asserts that the spectator code *ran* and leaves the claim about *which* player to z1800.

## D73 — demos decode off the UI thread, and a load returns a result rather than nothing

**Owner, 2026-08-23**, watching a UI run open a real match:

> the program is stalling for a few seconds on every load, windows even thinks the program is hung

and then:

> yes demos should be loading somewhere that isnt the ui thread, thats just best practice

`MainForm.LoadDemo` ran entirely in the click handler. Windows marks a window that has not pumped
messages for five seconds as "Not Responding", and `z1800.dem` — a 24-minute nine-versus-nine match
— spends 4.9 s in `DemoTimeline.Build` alone.

**Split into `Decode` and `Apply`, and the line between them is "does this touch the form".**
`Decode` is **static**, so it cannot reach a field even by accident — the same argument as the
project boundaries in D54, where a rule that could be a compile error should be one. `Apply` assigns
the fields, the transport, the clock and the map, and must stay on the UI thread because it ends in
Direct3D.

### Two corrections from the owner, both about naming and shape

**`BeginLoadDemo` became `LoadDemoAsync`:**

> if something is async naming conventions would suggest we make sure that is obvious in the naming,
> so begin load demo might be better off as loaddemoasync or something

`Begin*` is the legacy APM pairing (`BeginX`/`EndX`) and reads as that to anyone who knows it. .NET's
current convention is the `Async` suffix.

**And no `async void`:**

> we dont async void, we do pass back, at least just pass a sucess or fail message

> im pretty sure our analyzers would have kept you from doing async void without a suppression
> anyway, and there was no argument for doing one here other than lazyness really

**He is right on both, and the second is checkable: it would have been a build error.**
`SonarAnalyzer.CSharp` is referenced with `AnalysisMode=All` and `TreatWarningsAsErrors=true`, and
S3168 — *"async" methods should not return "void"* — is not suppressed in `.editorconfig`. The
justification offered for it ("event handlers are the sanctioned exception") is a real convention,
and here it was a real convention doing duty as an excuse: the handler had an obvious place to keep
the task.

So `LoadDemoAsync` returns `Task<DemoLoadResult>`, the handler stores it in `Loading`, and the
outcome is one of three — `Loaded`, `Superseded`, `Failed`. **Three rather than a bool** because a
load abandoned when the user picks a different demo did not fail (nothing is wrong, nothing to
report) but did not load either, and collapsing them would put "Could not open" in the status bar
every time somebody changed their mind.

### A deadlock, found by a hanging test

The first version awaited with `ConfigureAwait(true)` and let the synchronisation context return it
to the UI thread. That works under WinForms and **hangs under NUnit**, whose single-threaded test
context stops pumping while the test itself is awaiting — the run was killed at ten minutes.

Replaced with an explicit `OnUi` helper: `IsHandleCreated && InvokeRequired ? Invoke(work) : work()`.
A form with no handle has no thread affinity, which is why every existing test can drive `MainForm`
without a message loop, and it is why the fallback is simply to run the work in place.

### What this did NOT fix, measured afterwards

**The decode was the smaller half.** Timing the phases either side of it showed ~20 s of a ~21 s
demo switch is asset loading — `reading surfaces and textures` alone is 13–18 s — and all of it is
still on the UI thread. B146 carries the table. The first diagnosis picked the one phase that
happened to be wrapped in a timer already, which is
`docs/memory/measure-every-hop-before-blaming-one.md` almost word for word.

Two further defects fell out of chasing it, both filed rather than fixed: **B147**, the scrub bar
cannot be set through automation and therefore not by anyone without a mouse; and **B148**, switching
demos permanently costs 15x the frame time (300 fps to 19, paused, with posing and lighting at zero).

## D74 — everything Valve puts on the GPU goes on the GPU, and fidelity decides the rest

**Owner, 2026-08-23**, after finding every texture was being decompressed on the CPU:

> i told the AI that was doing the decompressing to unload everything it could on the gpu and it must
> have ignored me or not realized it had already put one on the CPU. thats fning source SDK and video
> game dev 101 though, you offload everything you can to the gpu

and then, when the rule was about to be applied as a blanket performance principle, he narrowed it:

> when i say everything that can be on the gpu should be on the gpu, i really mean everything valve
> puts on the gpu has to be there, and if they have something on the cpu we see why, and likely
> follow it, but if theres not a good reason to keep it on the cpu anymore and it can be moved off
> without changing too much when it comes to matching valve and wont cause bugs we cant use the sdk
> and decomp to fix, then we might move it.

**That second message is the rule, and the difference matters.** It is not "move work to the GPU
because the GPU is faster". It is:

1. **Whatever Valve does on the GPU, we do on the GPU.** Not negotiable — it is a fidelity
   requirement, and performance is a side effect.
2. **Whatever Valve does on the CPU, find out why, and expect to follow it.** Their reason is
   usually still the reason.
3. **Only then, and only if** moving it costs nothing in matching Valve and any resulting bug is one
   the SDK or a decompiler can settle, is it a candidate to move.

### Applied: B149, DXT textures

Valve's material system hands a VTF to the device in whatever format it was stored in, and the
shaders sample it there — `texCUBE( envmapSampler, reflect )` in `common_ps_fxc.h` for a cubemap,
ordinary 2D samplers for everything else. So DXT belongs on the GPU by rule 1, before any argument
about speed.

That it also happened to be **the whole load time** is the side effect:

| | before | after |
|---|---|---|
| VTF decode | 16.87 s CPU | **0.10 s** |
| loading props | 4.52 s | 2.38 s |
| loading entity models | 9.01 s | 2.29 s |
| reading surfaces and textures | 17.85 s | **8.20 s** |
| uploading textures | 0.92 s | 0.55 s |

**Cubemaps were nearly excluded and should not have been.** The plan was to leave them on the RGBA
path because combining six faces into one buffer is awkward with blocks. The owner refused:

> dont skip the cubemaps, cubemap bugs are some of the most common map bugs in tf2, there may not be
> a lot of them but they are heavy, and they break easily, so they need to be on the gpu like valve
> has them.

He was right on the principle and it turned out to be *easier* that way, not harder — a cube is six
array slices times its mips, supplied at creation, which replaces the copy-into-one-buffer step
rather than complicating it.

### The CPU expander stays, and CI is the reason

The tidy conclusion — delete the DXT decoder, since production no longer needs it — is wrong. Some
tests genuinely read texel values: a Phong ramp's shape, a normal map's channels, a material's
brightness against the map's stated reflectivity. The obvious replacement is to read the texture back
off the device, which would test what is actually drawn.

> just curious, but im making sure we are not creating a test that doesnt actually check anything in
> a round about way.

> no it has to be cpu, the ci has no gpu

**A verification that cannot run where the suite runs is not a verification**, so `TextureImage.ToRgba`
keeps the expander for callers that must read values, documented as never belonging in a load.

### What the tests can and cannot reach, stated rather than implied

- **Covered on CPU**: that the bytes handed over are the right bytes — offset, mip, face, level size,
  chain order.
- **Covered as arithmetic** (`BlockUploadDescriptionTests`): the DXGI format each DXT format maps to,
  and the row pitch in blocks. These are the failures that leave the bytes correct and only the
  *description* wrong — a DXT1 image called `BC3_UNORM`, or a pitch measured in pixels — which no
  byte-level assertion can see and which produce a skewed or wrongly-lit picture rather than an
  error.
- **Not covered by anything**: whether the hardware's decode of a correctly-described block matches
  Valve's. That is the S3TC specification and the driver, and the only instrument is looking at the
  screen.

### A number this corrected

`PropModels.BakeSeconds` read **11.15 s** before this change and **2.44 s** after. Baking was not
touched. The difference was CPU contention with 16.87 s of parallel texture decoding, so the earlier
figure said more about the decode than about baking. B150's trade should be judged against 2.44 s.

## D75 — debug draws carry Valve's names, and exist to be used during development

**Owner's direction, 2026-08-23**, given while a rendering bug was being hunted by rebuild-and-look:

> "we need to do all of those debug draws, because they are very important for testing and
> debugging"

and, on scope and order:

> "if we just start with textures thats fine, but implemnt anything that could be causing this, fix
> this, then we go into including the rest"

**The reason is measured, not stylistic.** During the B154 hunt four hypotheses were cleared by
building the viewer and looking at it — DXT upload, alpha-test classification, strip winding,
back-face culling — at roughly two minutes each plus the owner's time flying to the same spot. The
bug was then found in a single pair of screenshots of one view, drawn two ways. A debug view is not a
convenience here; it is the difference between a measurement and a guess.

**They take Valve's names and Valve's defaults**, for the same reason the config console does (D69):
the viewer's vocabulary is Source's, so `mat_wireframe` and `mat_specular` are what they are called,
and a pasted config that sets them works. Where the SDK declares a default, that is the default —
`r_3dsky` is `"1"`, `mat_specular` is on.

**One deliberate divergence, stated rather than absorbed:** Valve gates several of these behind
`FCVAR_CHEAT` and `sv_cheats` (`WireFrameMode()` in `game/client/view.h:68` returns 0 unless cheats
are on). There is no server here to protect and no opponent to gain an advantage over, so the gate is
ceremony and is not implemented. `r_3dsky` is not cheat-gated by Valve either, which is the case that
proves the distinction is real rather than convenient.

**Implementation note that is part of the decision:** `mat_wireframe` builds a wireframe twin of
every rasteriser state rather than one shared wireframe state. A single shared state would drop each
pass's culling and depth bias, and would therefore answer "what is in the vertex buffer" instead of
"what is being drawn". Those are different questions, and the difference is exactly what a
missing-geometry hunt turns on.

## D76 — the 3D skybox is drawn, and whether it is drawn is the user's setting

**Owner's direction, 2026-08-23**, twice and with the reasoning both times. First that it stops being
deleted:

> "we dont need to drop the skybox anymore either, the free cam can be positioned and set properly
> without having to drop it completely, we are going to need it later to make the maps look right in
> free cam and pov anyway"

then that it becomes a setting rather than a decision:

> "yes having the skybox seeable is weird but people like me who played without the skybox are use to
> it and expect it, tf2 allows you to run skybox on or off, so we will too, video makers need the
> skybox on"

**Valve agrees, and the SDK is explicit about which of the two is a cheat**
(`game/client/viewrender.cpp:113`): `r_3dsky` defaults to `"1"` with no flags, `r_skybox` defaults to
`"1"` with `FCVAR_CHEAT`. So the 3D skybox is an ordinary preference and the 2D one is not — which is
precisely the split the owner described from playing the game.

**What was actually removed** is the play-area cull that deleted the skybox as a side effect of
framing the overhead camera. It was never a skybox feature; it was a camera shortcut that happened to
hide the skybox, and it also discarded 133 real world faces. Removing it is the same correction as
the downward-normal cull and the decal bias — see B155.

**The intermediate state is deliberate and is not the feature.** Drawing the skybox room without the
`sky_camera` transform puts a miniature copy of the surroundings far outside the level at its literal
scale. That is what the file contains. Tracked as B152, and a visible wrong thing is a better base to
build the transform on than an invisible one.

## D77 — a defect in the demo is in scope, because recovery is the point

**Owner's direction, 2026-08-23.** The case that prompted it turned out NOT to be a demo glitch —
the stock rocket launcher draws centred where TF2 holds it to the right, which is ours — but the
principle was stated in general and stands on its own:

> "we need to fix it either way, thats a demo glitch i want to fix, because the whole point in this
> is to recover demos and make them look at least as good as they did originally."

**This retires a triage question that was about to become a habit.** The instinct on finding that a
demo carries a wrong sequence index, a missed weapon change or a truncated field is to establish
whose fault it is and close the entry if the answer is "the recording's". That instinct is right for
a renderer and wrong for this project: a viewer whose job is to play back demos the live client can
no longer play does not get to hand the defect back to the file. The file is the input, and the
input is damaged — that is the premise of the whole thing, not an exception to it.

**The distinction still matters, but it decides HOW rather than WHETHER.** A rendering fault is
fixed in the renderer and must not be papered over at the decode layer, where it would corrupt every
other consumer. A demo fault is fixed by reconstructing what the recording failed to say — the same
work as `docs/memory/the-client-builds-what-the-demo-omits.md`, where the first-person weapon is a
client-side entity no demo contains and the item index plus `items_game.txt` rebuilds it. So the
question "is it ours or the demo's" is still worth answering first; it just never answers "leave
it".

**The bar it sets is explicit and is higher than "does not crash":** at least as good as the demo
originally looked. Not our best guess at plausible, and not a blank where the data was bad — what a
player watching it in the live client at the time would have seen.

## D78 — PROJECT RULE: every control is customisable, in Valve's config language, scripting included

**The owner, 2026-08-23, stating it as a rule rather than as a fix:** *"yea thats a project rule all
controls need to be able to be customized so we use valves config style, including scripting, in out
own config, for settings that valve doesnt carry"*.

Said first as a correction to a hardcoded shortcut — *"remember all this needs to be able to be
customized, so it needs to be done liek valve does in our app config"* — and then restated as
standing policy when the narrower reading was filed. **It binds every control this viewer will ever
have, not the debug views that prompted it.** A new feature with a fixed key is a violation of this
rule, not a thing to be migrated later.

**"Including scripting" is the part most easily dropped, and it is not decoration.** Valve's config
language is a language: `alias`, `exec`, `+`/`-` commands, several commands separated by semicolons
in one bind. The owner's own `viewmodels.cfg` is exactly that —
`alias vm_off "viewmodel_fov 0; r_drawviewmodel 0"` — so a viewer that accepted only `bind KEY
command` would reject the file it is meant to accept. `docs/memory/a-config-is-a-program.md` already
records why: aliases redefine each other at runtime, so resolving them statically is wrong by
construction.

Said while a hardcoded `Keys.Control | Keys.L` was being written for the leaf-box view, which is the
shape being rejected. The debug views, the lighting modes, wireframe, reflections, surface colours,
full screen and capture are all fixed `ShortcutKeys` in `MainForm` today, reachable only from the
menu — while movement, camera and playback already come from a config file through D69's console
(`SwitchCameraMode SPACE`, `FlyForward w`, and the rest, 12 of 95 binds applied on the owner's own
`config.cfg`). Two mechanisms for the same question, and only one of them the user can change.

**What follows, and it is Valve's model rather than a keybinding dialogue:**

- Each view is a **cvar with Valve's own name** — `mat_leafvis`, `mat_drawflat`, `mat_luxels`,
  `mat_normalmaps`, `mat_bumpbasis`, `mat_fullbright`, `mat_wireframe`, `r_drawworld`,
  `r_drawentities`. They exist in the engine, so inventing names for them would be the translation
  layer D69 exists to avoid.
- Keys reach them with `bind`, in the same vocabulary as everything else, so a key is a binding and
  never a constant in a menu item.
- **The current F-keys become the defaults**, not the mechanism. F1–F4 debug, F5–F7 lighting, F8
  reflections, F9 surface colours, F10 wireframe, F11 full screen, F12 capture — and `Ctrl+L` for
  the leaf box, which is only where it sits because F11 was already spoken for.
- It goes in **our** config beside TF2's, not in the stock one, per the owner's earlier rule: the
  viewer has settings the game has no equivalent for, and they must not be written into a file the
  game owns.

**Why it matters beyond preference:** the collision this was said over — leaf box and full screen
both on F11 — is a class of bug that a constants-in-menus design keeps producing and a bind table
makes visible, because a bind table can be read, listed and checked for duplicates while
`ShortcutKeys = Keys.F11` in two places cannot.

Filed as work in `docs/RISKS.md` B165. Not done in the change that recorded it, deliberately: the
outstanding viewmodel defect is a bug and this is a feature, and mixing them would put a feature in
a fix's commit.

**As a rule it applies from now on, so the test for a new control is "can the user rebind it in a
config file, and does a script that does so work" — asked before the control ships, not after.**

## D79 — PROJECT RULE: every setting is a cvar, and it is Valve's name wherever one exists

**The owner, 2026-08-23:** *"the debug modes might exist in real tf2 btw, they are valves debug
modes, idk if they only exist in hammer though. so they need to be cvars too, make a note about the
cvars, and maybe a rule that all our systems should use cvars matching valves if possible, or ones
just styled like valves if they dont have an equivalent."*

The companion to D78. That one says every control is bindable; this one says every **setting** is a
cvar, and names it:

1. **Where Valve has one, use Valve's name exactly** — `mat_fullbright`, `mat_wireframe`,
   `mat_leafvis`, `r_drawviewmodel`, `viewmodel_fov_demo`. Not a similar name, not a wrapper.
2. **Where Valve has none, invent one in Valve's style** — lower case, subsystem prefix, underscores
   (`r_`, `mat_`, `cl_`, `snd_`), a value rather than a flag word. It lives in our own config beside
   TF2's, per the owner's earlier rule that the game's files must not carry settings the game has no
   equivalent for.
3. **Never invent a name for something Valve already named.** That is the translation layer D69
   exists to prevent, and it breaks the requirement that a real config works by being pasted in.

**The uncertainty in the direction was worth resolving, and it resolved in favour of the rule.** The
owner did not know whether these were retail cvars or Hammer-only. Measured by scanning the shipped
binaries for each string:

| cvar | ships in |
|---|---|
| `mat_drawflat`, `mat_normalmaps` | `materialsystem.dll` |
| `mat_leafvis`, `mat_luxels`, `mat_bumpbasis` | `engine.dll` |
| `mat_fullbright` | `engine.dll`, `materialsystem.dll`, `client.dll` |

**All of them ship in retail Team Fortress 2.** So they are not a Hammer facility this project is
imitating — they are cvars a player can type today, and the viewer offering the same name is parity
rather than homage. `docs/memory/binaries-answer-what-the-sdk-cannot.md` is the technique: the SDK
publishes some of these and the shipped binary settles which are actually present.

**Why it matters beyond consistency:** a name is an interface. Somebody who knows TF2 already knows
this viewer's settings, a real config keeps working, and a launch option keeps working — the owner's
earlier observation that "a lot of valves launch options are real config options". A viewer with its
own vocabulary would have to document and teach all of it, and would still fail the one requirement
that a paste of a real config works.

## D80 — Audio output is Silk.NET.OpenAL, used as a dumb stereo sink

Chosen by the owner from three options while wiring B168. Silk.NET.OpenAL over NAudio and over raw
WASAPI, on the grounds that **it is the same generated package family the renderer already uses** —
`Silk.NET.Direct3D11`, `Silk.NET.DXGI` and `Silk.NET.Maths` are all pinned together at 2.23.0 for a
reason this repo already documents, and the marshalling conventions are ones it already knows. It is
also the only one of the three that is not Windows-only.

`Silk.NET.OpenAL.Soft.Native` comes with it because `openal32.dll` is not present on Windows by
default, unlike `d3d11.dll`: the binding is P/Invoke over a library that has to be shipped.

**OpenAL's own 3D audio is switched OFF, and that is the substantive half of this decision.**

This project already implements Valve's spatialisation — `SoundGain` and `SoundAttenuation`, written
against the SDK and against measured engine constants (`snd_refdist` 36, `snd_refdb` 60, per
`docs/memory/a-convar-default-sits-beside-its-name.md`). OpenAL has a distance model of its own and
applies it per source by default. Enabling both would either double-attenuate or, worse, quietly
substitute OpenAL's curve for Valve's.

Neither failure looks like an error. Sound would come out, it would fall off with distance, and it
would be wrong in a way nobody could hear as a defect — which is precisely the class of silent
divergence D79 was written to prevent, and precisely how three no-ops shipped here with a green
suite in one session.

So the sink is deliberately stupid: it receives finished stereo samples and plays them.
`AL_SOURCE_RELATIVE` with the distance model set to none means OpenAL never computes a gain of its
own, and **the only spatialisation in the program stays the one that can be compared against
Valve's**.

**The consequence worth stating for later:** anything OpenAL offers for free — Doppler, cones,
reverb, HRTF — is off limits on the same reasoning unless Valve's own behaviour is implemented first
and the library is merely being asked to reproduce it. A feature that sounds good and is not what
the demo recorded is not a feature of this project.

## D81 — The corpus lives on archive.org and is fetched, not committed

**The owner, 2026-08-24:** *"they are backed up on archive, which is where im going to put all the
corpus files and we will start pulling from there when we want to test against a demo"*.

This replaces Git LFS as the corpus's home, and it dissolves the constraint that has shaped the
corpus rules from the beginning. **gcor grows "only for a new generation" because GitHub's free LFS
tier is 1 GiB/month and every CI job pays it** — that is the entire reason era specimens are trimmed
to 2–4 minutes and the reason protocols 21 and 22 are currently local-only while filling a measured
gap. Fetching removes the bill, so the question stops being "can we afford to commit this" and
becomes "which demos does a run need".

**Two things this must not become, both of which fail quietly.**

- **A test that skips when a fetch fails.** A skip is not a pass
  (`docs/memory/a-skip-is-not-a-pass-or-a-failure.md`) and it is invisible in a summary line, so a
  corpus that cannot be reached would report a green suite that measured nothing. Whatever the
  fetch layer is, an unavailable demo has to be a failure or a loudly-reported absence — never a
  silent one.
- **A demo identified only by URL.** A fetched file that differs from the one a result was recorded
  against turns every historical measurement into a claim about something else. Each entry needs a
  checksum in `manifest.json`, verified after fetch. That is also what makes archive.org's own
  durability guarantees usable rather than trusted.

A local cache follows from both: the measurement boxes should not re-download on every run, and
`TF2DEMOSALVAGE_GCOR_ONLY` exists precisely because run time is the thing being managed. The natural
shape is the cache being what tests read, and the fetch being what fills it.

Related and still to decide: whether anything stays committed at all. A handful of tiny specimens in
the repository means `git clone && dotnet test` works with no network, which has real value for a
contributor and for CI. That is a smaller question than it was, and worth answering deliberately
rather than by attrition.

## D82 — Ambience follows the transport, even if TF2 does not, because TF2 is wrong here

**The owner, 2026-08-24**, on being told that pausing leaves the ambient loops running and that the
engine might well do the same:

> *"i will still want to fix it even if tf2 does the same thing, that is a bug with tf2 imo, and it
> would open up video makers a bit, allowing them to include audio they wouldnt before because of
> the obvious desync. like if tf2 does it itself im pretty sure they sv_cheats 1 and turn anbience
> off for videos, its not like they dont put music over it anyway, sooo"*

**This is the SECOND deliberate departure from "match Valve", not the first** — the owner named the
other one immediately, and the correction is worth keeping because the two are instructive against
each other. Prop and animation baking (B150) poses small props once at load instead of skinning them
per frame, and B150 describes itself as *"Deliberate, owner-approved, and the one place this project
knowingly departs from how the engine does it"* — a sentence that was true when written and is not
any more.

Every audio decision before this one was settled by finding what the engine does and doing that: the
gain curve out of `engine.dll`, `AddLoopingSound`'s slot reuse, the soundscape index order,
`SND_STOP`, the suppression of positioned loops the map cannot place. B173 and B142 are both closed
on that principle.

**The two departures are not the same kind of thing, and the difference is the lesson.** Baking
departs for PERFORMANCE, and B150's finding is that what it buys was never measured — no benchmark
exists, no decision entry was written at the time, and the owner's own recollection of having tested
it is one he doubts in writing. It slid in. This one departs for CAPABILITY, with a stated
user-facing reason, recorded as it was made. A performance departure needs a number or it is a
guess; a capability departure needs an argument, and has one here.

The principle has a limit, and this is where it falls: **fidelity is the default because the engine
is usually right, not because matching it is the goal.** Where the engine's behaviour is a defect,
reproducing it reproduces the defect. A viewer whose ambience runs against a stopped clock is not
faithful to anything a viewer is for.

**The argument is about what the tool is FOR, and the evidence is the workaround.** If frag video
makers already turn ambience off with `sv_cheats 1` and lay music over the result, then TF2's
behaviour has not been tolerated — it has been routed around, at the cost of everything the map
sounds like. Fixing it does not merely tidy a transport edge; it makes an entire category of audio
usable that is currently discarded before anyone hears it. That is a capability, not a polish item.

**What this does NOT license, in the owner's own words, given immediately after the decision:**

> *"i will depart from valve in small ways if we have very good reason to, we just cant make massive
> changes, and shouldnt, because valves code is really good already, all our changes will do is slow
> it down and make it worse"*

So the licence is bounded on two axes at once, and both bounds are load-bearing:

- **SIZE.** Small departures, not massive ones. Not because large changes are forbidden in
  principle, but because a large one stops being a departure and becomes a reimplementation — at
  which point the thing being matched is gone and there is nothing left to check our behaviour
  against.
- **JUSTIFICATION.** "Very good reason", which the argument above supplies and a preference does
  not.

**And the reason for the bound is an assessment of the code, not deference.** Valve's is good; ours
will usually be slower and worse. That is a claim about relative quality, and this project has
evidence for it in both directions — every audio bug closed today was ours and not theirs, and the
one thing found in their code (the alternating-crossfade comment at `c_soundscape.cpp:1136`) is a
bug they had already noticed and fixed.

The practical test, then: the reasoning here was specific and each part had to hold — the behaviour
is a defect on its own terms rather than a taste, the divergence is observable and explicable to a
user, and there is a concrete thing it enables that is otherwise impossible. Absent all three, D80's
rule stands and the engine's behaviour wins, including where it looks like a bug.

**Consequence for B176:** measuring what the engine does when paused stops being a gate on the work
and becomes a footnote. It is still worth knowing — it belongs in `docs/findings/` either way, and
"Valve does the same" is precisely the kind of thing that gets confidently misremembered — but the
implementation proceeds regardless of the answer.

**Measured the same day, and the answer was the one that made this decision necessary.** The owner
paused a demo in TF2: *"nope tf2 plays the ambience and lets all the active sounds play out"*. The
engine does exactly what this viewer does, so B176 was never a defect in our reproduction — it is a
faithful reproduction of a defect.

Worth noting what that costs, because it is the price of every departure taken under this decision:
**there is now no reference implementation for the fixed behaviour.** Everywhere else, a
disagreement about audio is settled by reading the SDK or the binary. Here nothing can adjudicate,
so the design is ours and so is any bug in it — which is a further reason the licence is bounded to
small departures with very good reason.

### A departure is sequenced AFTER parity, never before it

**The owner, on being told the engine behaves identically:** *"that means the fix comes after
parity though, not before"*.

This is the operational half of the decision and it is what stops the licence corroding. A
departure has to be built on a baseline that already matches, for a reason that is about
measurement rather than tidiness: **once the viewer differs deliberately, every remaining difference
becomes ambiguous.** A wrong sound afterwards could be a decode fault, a wiring fault, or the
departure working as designed, and there is no longer an engine to compare against to tell them
apart — the one instrument that settled every audio question this session.

The order is therefore: reach parity, confirm it, and only then diverge from a known-matching
baseline, so the delta is exactly the change and nothing else. The same reasoning as
`docs/memory/two-recordings-of-one-value.md` — a comparison is only worth what the control is worth.

Applied to B176: it is filed and reasoned but does not get built while other audio parity work is
outstanding.

**The rule does not cover every departure, and the owner drew the line immediately:**

> *"for things like this that rule works, for baking it wouldnt have, we needed to have baking setup
> to guide later stuff, at least by my understanding of what would have been the best decision"*

So the sequencing depends on what KIND of departure it is, and there are two:

- **A behavioural departure** — B176 — changes what the viewer does at one moment. Nothing is built
  on top of it, so deferring it costs nothing and sequencing it after parity buys the unambiguous
  control described above.
- **A structural departure** — baking (B150) — decides how everything downstream is written.
  Prop drawing, bone merging, the skinning path and the frame budget were all built against it.
  Deferring that until parity was complete would have meant reaching parity on an architecture
  chosen to be thrown away, and then rewriting all of it. The departure has to come first precisely
  BECAUSE later work is guided by it.

**The test is whether anything is built on top of it.** If a departure is a leaf, it waits for
parity. If it is load-bearing, it goes in early and the parity work is done against it — and then
the cost is that the engine is no longer a clean control for that subsystem, which is exactly the
price B150 is still paying: baking caused the bone-merge defect, and what it bought has never been
measured.

Recorded with the owner's hedge intact — *"at least by my understanding of what would have been the
best decision"* — because it is a judgement about a decision taken earlier and partly from memory,
not a claim of fact.

## D83 — Two logging systems, and why the viewer's was hand-rolled was never written down

**The owner, 2026-08-24, on seeing `Microsoft.Extensions.Logging` referenced only by the CLI:**
*"logging should basically be everywhere too, are we not logging the viewer and the rest of the
projects?"* and then *"why did viewer custom roll a log class?"*

Measured rather than assumed:

| project | logging |
|---|---|
| `Core`, `Content`, `Audio`, `Presentation` | none |
| `Scene` (6 files), `Render` (2), `Viewer3D` (2) | `ViewerLog`, a hand-rolled static |
| `Cli` (2 files) | `ILogger` |

**The library silence is deliberate and stays.** Those four return structured refusals instead of
logging — `SoundSampleReader` reports why a sound would not decode, `BspVisibility` throws naming
the cluster and the reason, `SoundScriptCatalog` hands back what it resolved. A library that logs
picks a sink on its consumer's behalf, which is the concretion D6's dependency-inversion rule warns
about. It is also why this session's audio defects were diagnosable at all: the viewer could report
what the decoder told it.

**Why `ViewerLog` is hand-rolled is NOT recorded, and this entry does not invent a reason.** Its own
remarks justify the LOGGING — three defects that were invisible rather than hard, fallbacks that
must announce themselves, a file rather than a console because a WinForms app has none, and never
failing the caller over a log line. None of that explains build-versus-buy.

The strongest reconstruction, offered as reconstruction: **`Microsoft.Extensions.Logging` ships no
file provider.** Console, Debug, EventSource and EventLog are the built-ins, so "just use `ILogger`"
would have meant taking Serilog or NLog for a sink the viewer's whole purpose depends on. Against
roughly a hundred lines of writer for one desktop application, hand-rolling is defensible. But
nobody wrote that down at the time and it may not be the reason.

**What is genuinely open**, and not decided here:

- **`ViewerLog` is a static.** That is the concretion the DI rule argues against, and it has a
  concrete cost: library code cannot log even where logging would be right, because reaching for a
  static in `Core` would drag `Scene` in behind it.
- **The CLI and the viewer diverge**, so a future shared component has no obvious way to report.

Neither is a defect today. Recorded so the next person asking "why two?" finds the measurement and
the honest gap rather than reconstructing it again — which is what just happened.

### Resolved the same day: full DI conversion, and the assistant's recommendation was overruled

**The assistant proposed keeping `ViewerLog`'s static surface as a thin facade over an
`ILoggerFactory`** — one file sink underneath, 193 call sites untouched, the divergence fixed in the
plumbing without a mass edit. It was argued from size: 193 edits across three projects with no
behaviour change, and D82's own reasoning that large changes usually make things slower and worse.

**The owner rejected that:**

> *"we do the full di conversion because its the correct thing to do, its a massive rewrite but weve
> done them before"*

Recorded because the disagreement is the useful part. The facade would have fixed the SYMPTOM — two
logging systems — while preserving the thing that caused it: a static that library code cannot use
and that no consumer can substitute. D6's dependency-inversion rule is not satisfied by hiding a
static behind a nicer implementation.

The size argument was also weaker than it looked. D82 bounds departures from VALVE, not refactors of
this project's own plumbing; and "we've done them before" is evidence, not optimism — this codebase
has absorbed several conversions of comparable reach.

**What was decided:**

1. A file `ILoggerProvider` — the piece .NET genuinely lacks — carrying `ViewerLog`'s proven
   guarantees: never throw to the caller, mirror to `Debug`, thread-safe append, same format.
2. Every logging class takes an injected `ILogger`; `MainForm`, which writes to eight areas, takes an
   `ILoggerFactory`.
3. **The log AREA becomes the logger CATEGORY**, which is what categories are for and preserves the
   existing file format exactly.
4. The CLI uses the same provider, so both halves of the project log one way.
5. `ViewerLog` goes.

Staged project by project — Render (15 calls), Scene (75), Viewer3D (103) — gating between, so the
tree is never far from green.

### Done, 2026-08-24. What it cost beyond renaming, and what nearly went wrong

`ViewerLog` is deleted. Gate green at 3,231 across eight projects.

**The parts a rename would not have covered:**

- **A class writing to more than one area takes a FACTORY, not a logger** — `WorldRenderer`,
  `EntityModelSet`, `MapWorldBuilder`, and `MainForm` with eight. Folding categories together would
  quietly reclassify lines people grep for.
- **Static methods have no field to inject into**, so the logger is threaded as a parameter:
  `WritePng`, `UploadCube`, `Decode`, `Superseded`, `Walk`, `PackLighting`, `LoadPlacedCubemaps`,
  `Resolve`, `Register`, `LoadOne`, `LoadFrames`.
- **`MainForm` keeps a `(params string[])` overload** delegating to the factory one, because
  `params` cannot follow an optional parameter and thirty tests construct it directly.
- **`GameArchives` and `DecodeLog` needed no change at all.** Both already took delegates rather
  than reaching for a static — better inverted than they looked. `LoggerAdapters.LogTo` bridges
  them.

**The analyzers caught a bug the conversion introduced.** S6668 flagged nine calls where
`ViewerLog.Warn(area, what, failure)` had become `LogWarning(what, failure)` — `ILogger` takes the
exception FIRST, so the exception was being passed as a template argument and its type and message
would have vanished from every caught-and-handled failure in the viewer.

**Reading the output caught one the tests could not, and it is the lesson worth keeping.** After the
conversion the viewer logged 13 `assets` lines and zero warnings, against 215 and 16 before.
`MainForm` was calling `MapAssets.Load`, `MapWorldBuilder.Build` and `EntityModelSet` without
passing its factory, so each fell back to the null logger.

Nothing failed. The suite was green throughout, because **the null-object default that makes the
tests convenient is also what makes a missed wiring silent** — the same shape as every other defect
in `docs/memory/output-level-assertion-or-it-is-not-done.md`. It was found by launching the viewer
and comparing category counts against a pre-conversion log: `assets` 215, `map` 58, `config` 16,
`demo` 8 all match exactly.

**The CLI needed no work.** It already used `ILoggerFactory` with a console provider, so the
divergence is resolved by both halves speaking `ILogger` with different providers — which is what
the abstraction is for, and what the facade would not have achieved.

## D84 — The HUD is drawn in Direct3D as VGUI draws it, and rasterised with GDI for pixel parity

**The owner, on seeing a WinForms overlay being built for the frame rate meter:** *"how does valve
display it? i dont mind this stuff being dx, since its stuff tf2 has too, i dont expect it to be in
winforms"*. And immediately after, on scope: *"yea we are going to implement huds, so we should
setup this backbone now"*.

So this is a backbone decision rather than a detail of B174. The frame rate meter is the first
consumer; the scoreboard (B175) is the second.

### What Valve does

`CFPSPanel` is a `vgui::Panel` whose `Paint` calls
`g_pMatSystemSurface->DrawColoredText( m_hFont, x, y, r, g, b, a, ... )` — the material system's 2D
surface, composited into the same frame as the world. Not an OS window. The font is the scheme's
`DefaultFixedOutline`, and finding that took one wrong turn worth recording: it is in neither
`tf/resource/ClientScheme.res` nor `hl2/resource/ClientScheme.res` but in
`platform/Resource/SourceScheme.res`, which is why every Source game's meter looks alike.

```
"DefaultFixedOutline" { "1" { "name" "Lucida Console"  "tall" "10"  "weight" "0"  "outline" "1" } }
```

### What was nearly built instead, and why it was wrong

An owned borderless `Form` hosting a `Label`, following the transport bar's `OverlayWindow`. That
pattern exists for a real reason — a child HWND over a `FLIP_DISCARD` swap chain does not composite
reliably — and the reason it was reached for is a real constraint too: text drawn inside Direct3D is
invisible to UIA and therefore to any UI test.

It was still wrong. The viewer is imitating a game HUD, and a HUD that is an operating-system window
cannot be captured in a screenshot of the viewport, cannot be positioned in viewport coordinates,
and would not survive into any later video capture. The UIA objection is real and the answer is that
HUD content gets asserted by picture rather than by tree — `docs/memory/a-picture-is-assertable.md`.

### REVERSAL — the rasteriser, from SkiaSharp to GDI

**The assistant recommended SkiaSharp and was overruled.** Both positions, because the losing one
was argued at length and is the kind that gets repeated:

- **Assistant:** one rasteriser on both platforms, because the point of the Linux box is testing, and
  a test result only transfers if the box runs the same program. Valve's own answer is platform-split
  — GDI on Windows, FreeType on Linux — so copying Valve would mean the Linux box exercised a
  rasteriser the Windows build never runs. Also argued that pixel parity was unreachable anyway.
- **Owner:** *"we will go for windows pixel parity, and just know we cant really test it on linux,
  which is fine because the viewer cant go opn linux either, but it means another project that cant
  if its a new project, which idk if this should be or if its a part of the viewer.3d project anyway
  so going skia actually doesnt get us anything anyway"*.

**What kills the assistant's argument is the consumer, and it is not a close call.** `Render` is
Direct3D 11 and the host is WinForms — this code cannot run on Linux under any rasteriser. So Skia's
cross-platform property was buying protection against a divergence that cannot occur, at the cost of
the one thing that was actually achievable. The Linux box was never going to run the HUD; it was only
ever going to run a rasteriser in isolation, which measures nothing anyone cares about.

Generalised, because it is a trap worth naming: **a portability argument is about the CONSUMER, not
about the library.** Choosing a cross-platform dependency for a component that only a Windows-only
consumer can call buys nothing and costs the platform-specific capability.

### What pixel parity actually costs, stated so it is not discovered later

Reachable on Windows, and not free:

1. **`vguimatsurface` is closed.** It is not in `source-sdk-2013` — only `fontabc.h` and
   `BitmapFontFile.h` are. The DLL ships, so it is decompilable under the usual rule (output outside
   every git tree), but it is a decompile rather than a read.
2. **`outline` is not a font feature.** It is Valve's own post-process over the GDI glyph bitmap, as
   are `blur`, `dropshadow` and `scanlines`. Matching them means recovering that code.
3. **TF2 is not pixel-identical to itself across platforms**, since its Linux build uses FreeType.
   "Pixel-identical to TF2" is therefore a per-platform target by construction.

Parity with the Windows build is the target. The decompile is not done, so anything below is an
approximation until it is, and the approximation must be labelled as one rather than claimed.

### Layering, which the reversal constrains

**Scheme parsing stays in `Tf2DemoSalvage.Content`.** Not tidiness: Content's suites run on the
Linux measurement boxes for mutation testing, so putting `System.Drawing` in Content would break
those runs — and it would break them as a build failure that Stryker scores as a surviving mutant
rather than reports as an error.

**The rasteriser gets its OWN project, `Tf2DemoSalvage.Fonts`, on `net10.0-windows`.** It went
through two wrong homes first and both are worth recording, because each was wrong for a different
reason.

*Wrong home one, `Render`.* The assistant wrote "Render is already Windows-only through Direct3D, so
it costs nothing there" — plausible, and false. `Render` targets plain `net10.0` deliberately
(D60/D61), and
`PngWriter` exists *only* because `System.Drawing.Common` was the last thing keeping it on
`net10.0-windows`. Its own project file states the rule: *"Anything doing DllImport on user32 or
kernel32 belongs in Viewer3D however portable its project file looks."* Putting GDI back in `Render`
would have quietly undone a decision taken two days earlier and thrown away a hand-written PNG
encoder's whole reason for existing.

Worth keeping as an example of the shape: **"it's already platform-specific" is a claim about a
project that has to be CHECKED, not inferred from what it happens to call.** Direct3D 11 is
Windows-only at runtime; the project referencing it is not Windows-only at compile time, and the
difference is the entire point of the split.

*Wrong home two, `Viewer3D`.* Corrected by the owner: *"it might not becuase of MVP, it might need
its own project"*. `Viewer3D` is the **View**. A glyph rasteriser is not view logic — it is
infrastructure that happens to need a Windows API, the same category as `Render` and `Logging`, both
of which were split out for exactly this reason. Folding it into the shell would put a testable
service inside a WinForms application project and blur the one boundary this architecture is built
on.

**And the enforcement is the point, not the tidiness.** The owner: *"we want the compile time safety
from MVP"*. A layering rule written in a document is a rule someone breaks by accident; a layering
rule expressed as a project reference is a rule the compiler refuses. This is the argument `Render`
already makes about itself — *"nothing in this project can quietly reach for a Form"* — applied
once more. With `Fonts` separate, no amount of carelessness can put GDI in the decode path, the
render path or the presenter, because none of them reference it.

**Rasterisation sits behind an interface, on the owner's direction:** *"we want to be able to test
this so a interface is probably not a bad idea, its SOLID if nothing else"*. The assistant had
talked itself out of one a minute earlier as speculative generality — one implementation, extract
when a second appears — and that reasoning was wrong here, because the second consumer is a test.

It also decides the split cleanly, and the split is better than any of the three suggestions:

| lives in | framework | what |
|---|---|---|
| `Content` | `net10.0` | `SchemeFont`, the scheme reader, `IGlyphRasteriser`, glyph metrics, atlas packing, text layout |
| `Fonts` | `net10.0-windows` | `GdiGlyphRasteriser` and nothing else — the only project in the tree allowed to know what a font file is |
| `Render` | `net10.0` | the atlas texture and the HUD draw call, in terms of pixels and quads |
| `Viewer3D` | `net10.0-windows` | composition: builds the atlas, hands it to the renderer |

So the ARITHMETIC of a HUD — where each glyph lands, how wide a string is, how the atlas packs — is
deterministic, Linux-testable and needs no font installed, while only the glyph bitmaps themselves
are Windows-bound. That is the part worth testing anyway: a wrong advance width is a bug, and a
one-pixel difference in antialiasing is not.

The abstraction lives in `Content` and the implementation in `Fonts`, which is the direction
dependency inversion asks for: the low, stable, portable project owns the interface, and the
high, volatile, platform-bound one implements it. Nothing references `Fonts` except the composition
root.

## D85 — User content is IMPORTED into our folder, never read live out of TF2

**This corrects how D69 was built, not what D69 said.** The owner, on finding the viewer reading
`tf/cfg` directly at startup:

> *"btw when i talked about using a players config, i wasnt talking about using it directly from the
> tf2 cfg folder, because movie makers probably play with a different config than they make videos
> with, i want to enable us to import the players config, and match everything we need from it
> basically. i figure the player can either copy paste their tf2 config into our config folder or we
> have a ui import path or option to auto import from the tf2 folder"*

and then: *"yea i think i should have made that more obvious from the get go because we kinda
implemented to config too strongly"*.

**The requirement was always "a real config works wholesale". The implementation read that as "read
the real config, in place", which is a different and worse thing.** A person who plays with a
competitive config and records with a movie config has two, and the viewer silently picking whichever
one `tf/cfg` holds is not a feature. Worse, live reading makes the game's folder an input the viewer
depends on: reinstall TF2, or verify files, and the viewer's controls change under it.

So: **TF2's folder is an import SOURCE, and our own folder is the truth.** Three routes, all landing
in the same place:

1. copy a file in by hand,
2. import from the TF2 install on request,
3. import from a zip.

**And the same rule covers HUDs**, on the owner's direction: *"the hud is another one of those places
where we should allow the user to add it manually to our files, or import from there tf2, or import
from a zip"*. That is exactly how TF2's own HUD community works — a custom HUD ships as a zip that
gets dropped into `tf/custom/`, so "import from a zip" is not an extra convenience, it is the native
distribution format.

### The general rule, and where the line falls

The owner generalised it past configs and HUDs:

> *"yea bascially everything we inport from tf2 which can be user changed should be handeled like
> that, i know the models and stuff cant be, because they are vpks and change when valve updates,
> but configs, huds, basically everything in the custom folder now lol"*

**So the boundary is `tf/custom/`, and it is a good boundary because Valve drew it.** That folder
exists precisely to hold what a player added; everything else in `tf/` is what Valve shipped.

| kind | example | how we read it | why |
|---|---|---|---|
| **user content** | configs, HUDs, anything under `tf/custom/` | **imported** into our folder | it is the player's, it varies per purpose, and it must not change under us when they reinstall |
| **game content** | models, materials, sounds in the VPKs | **read live** | Valve rewrites it on every update, so a copy is a copy that goes stale — and it is gigabytes |

The asymmetry is not inconsistency. A stale config is *wrong* — the viewer would silently use
controls the player has abandoned. A stale model is *wrong in the other direction* — a demo from
today wants today's assets, and a snapshot taken at import would drift from the game the demo was
recorded against.

**Nothing in the reading layer has to change for any of this, which is worth stating because it is
the test of whether the layering was right.** `SchemeFonts.Find` takes a `ReadOnlySpan<byte>` and
`ConfigConsole.Load` takes strings; neither knows what a folder is. The work is entirely in
acquisition — where bytes come from — and none of it reaches the parsers.

**Not built yet.** What exists today is the live read this entry says is wrong. Filed rather than
fixed in passing, because it is a UI feature (an import path, a picker, a zip reader) rather than a
one-line change, and the frame rate meter was the task in hand.


## D86 — PROJECT RULE: a departure from Valve must be DECLARED where it is made

**The owner, on discovering that the model upload had never matched the engine:**

> *"when that commit was made it was not explained that it was a departure or i would have never
> approved it, it might have been something you said was closed off, but its obviously not"*

D82 already says departures are bounded by size and justification. It assumed they would be
**visible**. This one was not, and an undeclared departure cannot be bounded, argued with, or
rejected — it is simply absorbed as though it were the design.

### The case that produced the rule

`a54e61e` (2026-08-13) introduced one packed vertex buffer holding every entity model, rebuilt
whenever the set grew. Its commit message reasons entirely from first principles:

> *"Two buffers rather than one because the lifetimes differ: the map's geometry is rebuilt when the
> world is, while this grows only when the demo shows a model it has not shown before."*

**Valve is not mentioned anywhere in it.** Not as a comparison, not as a rejected option, not as
something unknowable. So there was nothing for the owner to approve or refuse — the choice never
appeared as a choice.

And the engine's answer was two greps away in *published* headers.
`public/materialsystem/imaterialsystem.h` declares `CreateStaticMesh` / `DestroyStaticMesh`: one
mesh per model, created once, destroyed once. `CBaseEntity::PrecacheModel` behind
`IsPrecacheAllowed()` says when. Nothing here was closed; nobody checked. That is the failure
`docs/memory/nothing-is-closed.md` exists for, and the reason
`docs/memory/conformance-test-before-implementation.md` asks for the parity question to be answered
*before* the code that would bias the answer.

**What it cost:** roughly 200 ms of frozen application every time a model came into view, 25 times
in 1 minute 43 seconds of one match, for eleven days — invisible to every counter, because the cost
sat outside both the posing and drawing timers.

### The rule

1. **Before implementing anything the engine also does, find out how the engine does it.** The menu
   in `CLAUDE.md` is a menu: go to the source that holds the answer. For rendering structure that is
   the material system headers, which are published even though the implementation is not.
2. **If the design differs, say so IN THE COMMIT MESSAGE**, in the form: what the engine does, what
   we are doing, and why. Not in a code comment alone — a commit message is what gets reviewed.
3. **"I could not find out what Valve does" is a claim that must be shown**, not assumed. It is
   almost always false; when it is true the binaries are decompilable.
4. **A comment asserting our arrangement is cheap is not evidence and must not be written as
   though it were.** The one here — *"rare and bounded … known within a few seconds of playback"* —
   was measured at 25 rebuilds over 1m43s. Confident, specific and unchecked is the worst
   combination, because it reads as though somebody verified it and stops the question being asked
   again.

**The assistant's first proposal for the FIX repeated the original mistake** and is kept here as the
worked example: it suggested keeping the packed buffer and appending to it, which addresses the cost
while preserving the undeclared departure that caused it. Overruled — *"so we switch to valves,
which is what we should have been using in the first place, becasue valves imp is blazingly fast"*.

### The bone-merge ordering departure was DELETED on 2026-08-24, not amended

A subsection here declared the depth sort as a departure from Valve's recursion. B181 said to delete
it rather than amend it when the code changed, and the code has changed (D88): `EntityModelSet` now
drives `AnimatingEntity`, and `_ordered`, `_worn`, `_wanted`, `_wearerBones`, `_parents` and
`Depth()` are gone.

**What is worth keeping is why the declaration was wrong, and it was wrong twice.** It claimed the
outputs were identical — they were not, a chain three deep mixed two spaces (B180) — and it named
`DrawModel`'s recursion as the mechanism it was trading against, when the ordering is actually solved
by `CBoneMergeCache::MergeMatchingBones` calling `SetupBones` on its parent directly. There was no
ordering code on Valve's side to trade against. It was forty lines against zero.

That is recorded in `docs/findings/35-the-bone-pipeline-audit.md`, which is where the reasoning
belongs now that the code it governed no longer exists. **A departure declared under D86 is only as
good as the reading behind it**, and this one cited the second mechanism it found rather than the one
that mattered.

## D87 — PROJECT RULE: load at load time, not on sight

**The owner, after three stalls in one session all turned out to be the same shape:**

> *"yea i think most things in this project should probably not lazy load, i dont think video games
> use lazy loading too often, because they need execution speed over size"*

Right, and the engine says so in its own API. `CBaseEntity::PrecacheModel` is guarded by
`IsPrecacheAllowed()` and carries the comment *"Warn on out of order precache"* — Source does not
merely prefer loading at level load, it **complains when you do it later**. Sounds, models and
materials all go through a precache pass before play begins.

### Why a game does this and a general-purpose program does not

Lazy loading trades a bounded, one-off cost at start-up for an unbounded cost at an unpredictable
moment. That is a good trade when the deadline is a human's patience and a bad one when the deadline
is 6.9 ms. **A frame has a deadline; RAM does not.**

It is also the worse trade in the other direction here: a demo viewer is going to show what the demo
contains, so almost nothing loaded eagerly is wasted. The set is known before the first frame,
because the recording already happened.

### What this cost, measured on 2026-08-24

Every stall found in one session was the same mistake wearing different clothes:

| what was lazy | cost | fixed by |
|---|---|---|
| the packed vertex buffer, rebuilt on every addition | 193-231 ms, 25 times in 1m43s | per-model static meshes (D86) |
| model geometry PACKED when a prop first appears | 385 ms in one frame | precache from the timeline |
| slices and rebased batches, rebuilt per call | 40-53 ms | cached per path |

And the owner's report was the same each time — *"everything freezes for a half a second to maybe a
second"* — while the frame rate never dropped, because a stall is not a slow frame rate.

### The rule

1. **Anything the demo will need, load before playback starts.** The timeline knows every model
   before the first frame; the string tables know every sound and material. There is no reason to
   discover them by being surprised.
2. **Do it on the worker that already reads the map**, behind the `_readingMap` barrier, so it costs
   nothing on the UI thread and shows up as load time rather than as a hitch.
3. **Asynchronous loading is NOT the fix and must not be mistaken for it.** Loading a model off-
   thread when a prop appears still leaves the first appearance wrong — either it hitches waiting or
   it pops in late. Precaching removes the event; async only moves it.
4. **A cache keyed on something that changes every frame is not a cache.** The lighting cache keys
   on exact position, so static props hit it and players — who move every tick — never do.

### Still lazy, and known

- **Sound decode.** `Sample()` reads from the VPK and decodes on first play. Instrumented on
  suspicion during this session and it recorded nothing, so it is not a current stall — but it is
  the same shape, and the demo carries a `soundprecache` table naming every sound. Filed.
- **The map itself**, deliberately: 33.5 seconds, already off the UI thread, and a viewer that
  waited for every map on disk would never start.

## D88 — The bone pipeline matches Valve's ARCHITECTURE, threading included, with no departures

**Owner direction, 2026-08-24**, after the audit in `docs/findings/35-the-bone-pipeline-audit.md`
found the pose loop had diverged structurally rather than just cosmetically:

> *"all of the architecture needs to match valves"*

and, when offered a menu of options with the maximal one marked:

> *"give me trhe options you do not choose and i can almost guarantee im going for the option that is
> 100% parity"*

So the target is not "behaves the same". It is the engine's object model: a per-entity animating
object owning its own bone array, a bone accessor with readable/writable masks, per-bone flags read
from the `.mdl`, one idempotent `SetupBones` as the single entry point, and the merge pulling its
parent through it rather than any ordering pass of ours.

### The one departure that was proposed, and overruled

The assistant proposed keeping the entry point and lock seam but **not** building Valve's threaded
bone setup, on the grounds that it is an optimisation rather than semantics, and declaring that
under D86. The owner rejected it outright and gave the reason:

> *"go for the threading too, full parity thats an optimization valve did for vary good reason, the
> bones are heavy, they need speed."*

That is right, and the measurement here already agreed with it before it was said: posing is about
**420 ms of every second** in this viewer (B99), and the heavy entities are exactly the ones Valve's
pre-pass targets. Proposing to skip it was a case of classifying something as "merely an
optimisation" without checking the number that was already in the risk register.

**Recorded because the reasoning generalises:** an optimisation in Valve's code is not decoration. It
was written against a shipping frame budget by people measuring it, and the default assumption should
be that it earns its place. A departure from one needs the same evidence as a departure from a
behaviour.

### What Valve's threading actually is, since it is not what the name suggests

Read from `c_baseanimating.cpp:2749-2793` and `cdll_client_int.cpp:2210`. **It is a speculative
prefetch of last frame's expensive roots, not a parallel-for over the draw loop:**

1. During the draw, `SetupBones` **enrols** an entity that is expensive and independent —
   `nBoneCount >= 16 && !GetMoveParent() && m_iMostRecentBoneSetupRequest != g_iPreviousBoneCounter`
   (`:2897`) — into `g_PreviousBoneSetups`. Nothing threaded happens then.
2. At the **start of the next frame**, after `SimulateEntities()` and `PhysicsSimulate()` and before
   rendering, `ThreadedBoneSetup()` runs `ParallelProcess` over that list (`:2787`), posing each with
   `boneMask = -1` — which `SetupBones` resolves to `m_iPrevBoneMask`, "whatever was asked for last
   frame" (`:2827`). Then `g_iPreviousBoneCounter++` and the list clears.
3. When the draw pass later asks for those bones, the readable-bones early-out at `:2911` returns
   them immediately.

Three things make it safe, and all three must be carried over:

- **Only roots are enrolled** (`!GetMoveParent()`), so no parent/child chain is being walked
  concurrently. The recursion and the parallelism never overlap.
- **Per-entity lock, and inside threaded mode it BAILS rather than blocks** — `TryLock` then
  `return false` (`:2845`). A contested entity is simply left for the draw pass.
- **The model cache is held for the whole parallel region**, `PreThreadedBoneSetup` /
  `PostThreadedBoneSetup` wrapping `mdlcache->BeginLock()` / `EndLock()`.

`InitBoneSetupThreadPool` and `ShutdownBoneSetupThreadPool` are empty in this SDK — `ParallelProcess`
uses the engine's global pool, so the .NET thread pool is the faithful equivalent and no pool of our
own is needed.

### The consequence for the viewer's frame

Valve's phase order is **simulate → threaded bone setup → render**, and ours poses inside the render
loop. Adopting this means the viewer grows that phase split. That is a real structural change and it
is the reason this is a decision rather than an implementation note.

**And set the expectation correctly: the win is not "posing goes parallel".** It is that posing for
heavy roots moves off the critical path, and it only pays when the same entities are expensive frame
to frame. In a demo viewer they are — the players persist — which is why this should pay here and
why it is worth measuring against the 420 ms rather than assuming.

## D89 — Valve parity is the FIRST principle, and performance never buys a departure from it

**2026-08-25.** The owner, mid-optimisation, when I was about to trade structure for speed:

> "this isnt messing up the matching valve rule is it? we gained a lot of performance my matching
> valve, but weve seemed to lose part of them"

and then, plainly:

> "ok well dont change things that are valve parity, keep valve parity as first principal"

### What this settles

D82 bounds departures and D86 requires them to be declared where they are made. Neither says what
happens when a departure would be **faster**. This does: it does not happen. Parity is not one
consideration weighed against performance — it is the constraint the performance work happens
inside.

### The reason it is not merely a preference, in the owner's own observation

**Matching Valve is where the performance came from.** Every measured win on this viewer has been a
move *toward* the engine, not away from it:

| Change | Result | Valve's own shape |
|---|---|---|
| one static mesh per model (B163) | 193–231 ms × 25 → gone | `CreateStaticMesh` / `DestroyStaticMesh` |
| precache models at load (B163) | 385–425 ms in-frame → 515 ms once | `IsPrecacheAllowed()` |
| precache sounds at load | 27–91 ms every few seconds → 2,261 ms once | `Assert( "PrecacheSound: too late" )` |
| reuse the skinning buffer | ~20,000 arrays a frame → none | `m_CachedBoneData.SetSize` when the count differs |

An assistant proposal to keep the packed vertex buffer and append to it was overruled during B163
with "so we switch to valves, which is what we should have been using in the first place, becasue
valves imp is blazingly fast" — the same argument, and it was right then too.

**The owner states it as a general law, and the table above is four instances of it:**

> "every place we diverge we have issues, bugs or prformance"

That is worth taking literally when hunting a defect, not only when writing new code. **A bug of
unknown cause is a reason to go looking for a divergence**, because the divergence is where the bugs
have actually been. Found this way on 2026-08-25, after the four above: `StudioAnimation.Pose`
returns a fresh `IReadOnlyList<StudioBonePose>` per call while Valve's `CalcPose` writes into
caller-supplied `Vector pos[]` / `Quaternion q[]` (`bone_setup.cpp:2346`) and `SlerpBones` blends in
place. Nothing declared it, and posing owns ~540 ms of every second.

**So the burden of proof runs the other way from where an optimiser expects it.** A candidate
optimisation that departs from Valve is not a trade to be evaluated; it is first evidence that the
engine's arrangement has not been understood yet. The question to ask is "what does Valve do here,
and why is it fast" — not "how much parity is this worth".

### How to apply it

- Before proposing a performance change, find the engine's arrangement for the same problem. If ours
  already matches it, the cost is somewhere else and the change is the wrong one.
- If the engine's arrangement IS the cost — profiled, not assumed — that is a departure, and D86
  applies: declare it where it is made, with the measurement that forced it.
- **A change that moves toward Valve needs no such justification**, even when its immediate purpose
  is speed. The skinning buffer and both precaches are all parity restorations that happen to be
  faster, which is the ordinary case rather than a lucky one.

### The cycle itself is the defect: decide the home and the parity BEFORE writing

The owner, 2026-08-25, after three days of it:

> "every time we implement a couple of new things, we have to go back and fix all the archetectural
> and parity issues, the going back over and over is the annoying part."

**That is a process defect, not a tidiness preference, and the ratio names it.** The initial MVP
switch took a day. Undoing the drift that grew back took another. Bringing the same code to Valve's
conventions took a third. Three days of rework against "a couple of new things" is the cost of
deciding those two questions *after* the code exists rather than before.

**The project already has this rule for BEHAVIOUR and lacks it for STRUCTURE.** A conformance test
comes first, with its citation, before any code exists to bias the answer — that is written down and
it works. Nothing said the same about where code lives or what the engine's arrangement is, so both
were settled by proximity: new code went where its neighbours were, and matched what was already
there.

So, before writing a new piece, answer two questions and put the answers in the commit:

1. **What is the engine's arrangement for this job?** One grep. If Valve models it as a system, a
   presenter or a per-frame pass, ours takes that shape and preferably that name — `SoundscapeSystem`
   and `UpdateClientSideAnimations` are both named for their originals so the parity is checkable
   rather than rediscoverable.
2. **Which project does it belong in, and can it be tested there?** If the answer is "the viewer
   because that is where the thing that calls it lives", that is the drift starting. `AddViewmodel`
   was written into the form for exactly that reason and cost three viewmodel bugs their testability.

**Both questions are cheap before and expensive after.** A divergence written into a NEW type reads
as deliberate; a misplaced type takes its tests with it, and those tests are what stop the next
regression. The going-back is not caused by the refactors — it is caused by the two minutes not
spent when the code was written.

### A refactor is when the parity check is cheapest

The owner, 2026-08-25, while `ShowMoment` was being extracted:

> "the refactors are perfect times to double check stuff like that"

**Because the code is being moved anyway.** Reading the engine's arrangement for the same job costs
one grep at the moment you are already deciding what the new shape should be, and the alternative is
worse than merely skipping it: a divergence written into a NEW type is harder to see afterwards than
one left in an old method, because the new structure looks deliberate.

It also runs the other way. **An extraction is a chance to find a divergence nobody was looking
for** — the boundary you are drawing has an equivalent in the engine, and comparing them asks
"why is ours shaped differently" at the only moment when changing the answer is free.

Found this way, in the first minutes of extracting `ShowMoment`: Valve's equivalent is
`IClientLeafSystem::BuildRenderablesList( const SetupRenderInfo_t &info )`
(`clientleafsystem.h:169`), and `SetupRenderInfo_t` (`:75`) carries the output list, the render
origin, the render forward and the render frame. The builder is **told** where the camera is; it does
not read it from ambient state. `ShowMoment(double tick)` takes a tick and reads the rest off the
form, which is exactly the coupling that makes it untestable without a window (B188).

## D90 — `Viewer3D` is a view AND a presenter, and the split is the port

**2026-08-25.** The owner, on being asked to keep thinning it:

> "is the VIewer3d suppose to be a view or a presenter, because if its a view, thats a very fat
> view, but i think rendering is more presenting than view, so its a kinda fat presenter, but not
> quite a god presenter, but its probably time to split it up where we can for the compile time
> safety"

**The diagnosis is right and names the real fault.** `Viewer3D` is not a view that got large; it is a
view and a presenter sharing one `net10.0-windows` project. That TFM is what makes the fatness
matter, because it puts presenter work on the wrong side of the only boundary the compiler enforces.

### Rendering splits three ways, not two

| role | what | needs Windows |
|---|---|---|
| **View** | window, menus, layout, key handling, message pump, full screen, the surface D3D draws into | yes |
| **Presenter** | `ShowMoment`, `ReadMap`, `ProjectMap`, the camera, `ReportWeapons`, capture options, the ledgers | **no** |
| **Renderer** | turning a scene into draw calls | no — `Tf2DemoSalvage.Render` is already `net10.0` |

So "rendering is more presenting than view" is right, and it separates further than that: deciding
WHAT to draw is presenter, issuing the draw calls is `Render`, and the window it lands in is view.

### The acceptance test is a Linux port, and it is a real one

> "we should be easily able to replace the view with something that runs on linux… and not have to
> touch anything outside the winforms view and d3d"

**That is the correct test for this work and it should be treated as the definition of done.** It is
also close: only two projects carry a `-windows` TFM.

| Windows-pinned | everything else |
|---|---|
| `Tf2DemoSalvage.Viewer3D` (WinForms) | Core, Content, Scene, Animation, Audio, Presentation, **Render**, Logging, Cli |
| `Tf2DemoSalvage.Fonts` (GDI rasteriser) | |

A port therefore needs a frontend (Qt, or Dear ImGui — the owner's "really lite UI library"), a
backend other than D3D11 through the same Silk.NET family, and FreeType in place of GDI. **What it
must not need is a reimplementation of the viewer**, and every line of presenter logic still sitting
in `Viewer3D` is exactly that: work a second frontend would have to write again.

That is the argument for the split, and it is a better one than tidiness — it is the difference
between "write a new window" and "write a new viewer".

### Why a PARTIAL thin view is worse than none, which is the real argument

The owner, 2026-08-25:

> "a true view has zero domain knowledge, nothing a presenter or model would do. it also is one of
> those things that are worse when its not followed since im using AI, because it invites later AI
> to not follow the convention and we get a fat view again. we also get far better compile time
> protection by moving it all out."

All three hold, and the middle one is the reason this cannot be left at ninety percent.

**Zero domain knowledge is the definition, not an ideal.** A passive view renders what it is told and
forwards input; it cannot answer a question about the domain. Being one line does not make a
delegator view code — it makes it a concise violation.

**A convention that lives only in surrounding code drifts, and this repository has proved it twice.**
The strongest signal for "how do I write this" is "what do the neighbours do", which is true of any
contributor and especially of one that pattern-matches on context:

- **MVP itself.** D54 chose it, D62 built the first presenter, and B188 records the outcome —
  *"Nothing else followed… everything written since has gone into the form because that is where its
  neighbours are."*
- **Test naming.** `CLAUDE.md`: *"one early file set the style, every later file matched its
  neighbours, and nobody compared practice against the standard."* 2,132 tests drifted to the exact
  opposite of the written rule.

So a mostly-thin view does not read as "nearly done". It reads as **"logic in the view is acceptable
here"**, and the next change extends the precedent instead of the rule. That is why the bar is
literal: everything that is not view comes out.

**And the enforcement is the TFM, not the file.** `net10.0` cannot reference WinForms — that is the
compiler refusing, which is what D54 meant by a boundary that is a compile error. Moving logic to
another FILE inside `Viewer3D` buys nothing: the project is still `net10.0-windows` and the next
person can reach for a `Control` freely. **"Move it out" therefore means out of the PROJECT.** Given
the drift above, only the kind of rule a compiler enforces survives contact with the next session.

### How it proceeds

Presenter pieces move to a `net10.0` project as they are extracted, so the compiler refuses a
WinForms reference from any of them. `Tf2DemoSalvage.Presentation` already holds `PlaybackPresenter`
and `SoundPresenter`; the viewer's own presenter work joins it or gets a sibling when its
dependencies (Scene, Render) make that cleaner. What stays behind is what genuinely cannot compile
without a window.

## D91 — Settings are config-settable, and `custom/` holds configs and MORE THAN ONE hud

**2026-08-25**, recorded while the world field of view was being taken out of three hardcoded
constants. Two separate things, and the second is a deliberate step BEYOND TF2 rather than toward it.

### Every setting a player can change in the game is settable here

The owner, on being asked whether 90 or 75 was the right default:

> "its an easy change to change the default so i doesnt matter, but that is exactly why i want our
> settings to be settable from a config file, it makes changing them and changing defaults free."

**That is the argument, and it is better than the number.** A value compiled in has to be argued
about; a value in a config is tried. The field of view had been compiled into three separate places
— `FreeCamera.FieldOfView`, `OverheadPlacement.For`'s default and the call site — so nobody could
try anything, and the discussion about 75 versus 90 could only be settled by a rebuild.

This restates `docs/findings/13-settings-parity.md` with its reason attached: the miss it exists to
catch is not a wrong number, it is a **choice taken away**.

### `custom/`, and huds are chosen rather than installed — AFTER parity

**Scheduled after parity, and recorded now because it constrains decisions being made now.** The
owner: *"its a after parity goal, but it might effect some earlier design decisions, so it needs to
be kept in mind"*. So nothing below is work in flight; it is a shape the current settings and asset
code must not make impossible.

> "our program is suppose to/going to be able to import a users config, or allow a user to paste
> their config into our folder structure somewhere, likely a custom folder like modern tf2, along
> with a hud or huds, im going to go for being able to choose huds on demand, meaning more than one
> hud can be in custom and you can choose which one to use, which is something tf2 doesnt do, the
> user will be allowed to just import huds too, so they dont have to find our folders to put it in
> the right place."

Three commitments, and the middle one is the departure:

1. **A `custom/` folder, laid out as modern TF2 lays it out**, so a config or a hud that works in the
   game works here by being put in the same place. Parity, and it costs nothing.
2. **Several huds may sit in `custom/` at once and one is CHOSEN at runtime.** TF2 cannot do this —
   a hud is installed by being the one present. **This is deliberate and must not be "corrected"
   toward parity under D89.** D89 governs how the ENGINE's behaviour is reproduced; it does not
   forbid the viewer offering something the game does not, and a demo viewer is a tool for looking
   at recordings rather than a client that has to behave like one.
3. **Importing, so nobody has to find the folder.** The layout is parity for people who already know
   it; the importer is for everyone else.

**What this means for settings work now**, which is why it is written down today rather than when
the feature is built: the config reader must keep ignoring what it does not implement (D69), keep
using Valve's own cvar names, and never assume one config file — a `custom/` tree is several, and a
hud carries its own.


## D92 — Presenters compose DOWNWARD, and a new project must forbid something

Two questions the owner asked while the `MainForm` refactor was in progress, 2026-08-25:

> "should maps maybe become their own project… i know this solution is already super project heavy,
> but the different projects is what give compile time protection for cross talk, likt are presenters
> suppose to talk to each other? or no? i know models dont."

Both answers were checked against the code before being given, and one of them changed as a result.

### Presenters: downward yes, sideways no

- **A presenter MAY own sub-presenters and presentation state types.** That is how a composite view
  gets a composite presenter, and it is the direction that stays testable: the owner can be
  constructed with fakes for what it owns.
- **Peer presenters MUST NOT reach into each other.** If two need the same thing it goes DOWN into
  the model layer, where both can reach it. Sideways coupling means neither presenter can be tested
  or replaced alone, which is the whole point of the layer.
- **Models never talk up**, which was already true and is not in question.

**Measured before writing it down, and the repo is clean:** every reference between types in
`Tf2DemoSalvage.Presentation` is a presenter owning something smaller — `FpsOverlay` owns `FpsMeter`,
`SoundPresenter` owns `SoundSchedule`, `FreeCameraController` owns `OverheadPlacement`,
`ConfigConsole` owns `FlightInput` and `SourceConfig`. Not one is peer-to-peer.

### A new project must FORBID something

**The test is not "this cluster is big". It is: is there a dependency direction I want the compiler
to reject?**

Maps were the case in point, and the answer is no — because the arrow already points the way a split
would allow. Measured inside `Scene`:

| entity-side type | depends on map-side type |
|---|---|
| `MomentScene` | `LevelLighting` |
| `EntityModels` | `BrushModels` |
| `PropModels` | `MaterialTable` |

Models are lit by the level, brush entities ARE map geometry, and props resolve map materials. A
`Maps` project would be referenced by the rest of `Scene` on its first day, so the ceremony and build
time would buy an edge that already exists and is correct.

**Contrast with the splits that did pay**, which is what makes this a rule rather than a preference:

| split | what it forbids | real? |
|---|---|---|
| `net10.0` vs `net10.0-windows` | a presenter touching WinForms | **yes** — the compiler refuses (D54, D90) |
| `Scene` cannot see `Presentation` | a model reaching up to a presenter | **yes** |
| `Maps` vs entities | nothing — entities already depend on maps | no |

**What WOULD trigger a maps project later:** a second consumer that needs map reading without the
entity layer — a standalone BSP tool, say. That is a real trigger. "This cluster is large" is not,
and the answer to a large cluster is usually one type instead of ten fields. Which is exactly what
the map state turned out to be: six of `MainForm`'s ten map fields were `MapLevel` unpacked into the
form, so the fix was to keep the record rather than to build a project around it.

## D93 — Decode everything Valve writes; consume it when there is a reason

The owner, 2026-08-25, on finding that six BSP lumps went unread:

> "the reason i dont like dropping anything valve does is because i dont want to need it later and
> require a uge change"

**This corrects a conclusion I had just filed.** I had checked whether anything CONSUMES the vertex
normal lumps today, found nothing did, and closed B194 as "nothing to fix". That answered the wrong
question. The right one is what it costs to add when something does.

### Two costs, and conflating them is the mistake

| | cost | when |
|---|---|---|
| **Reading** a lump | one reader, one field on `MapLevel` — local and self-contained | **now** |
| **Consuming** it | a new vertex-layout element, a changed shader signature, a world-buffer re-upload | when there is a reason |

Only the second ripples. Deferring the first buys nothing and is exactly the "huge change later" the
owner is describing — the archaeology of working out which lump held what, months after the context
is gone.

**So: decode is total, rendering is not.** `docs/memory/decode-must-be-total.md` already said the
first half — "anything that does not decode to 100%, with no errors, is wrong" — and I had been
reading it as though a lump with no consumer were exempt. It is not. What we DRAW is a separate
question from what we READ, and only the drawing waits for a reason.

### The evidence that settled the specific case

Our world faces take their PLANE's normal. That looked equivalent to the lump, and `vbsp` even
writes it that way:

```cpp
// Add this face plane's normal.
// Note: this doesn't do an exhaustive vertex normal match because the vrad does it.
g_vertnormals[g_numvertnormals] = dplanes[f->planenum].normal;
```

— `src/utils/vbsp/normals.cpp:38`. But the comment is the point: **vrad replaces them.** In a
compiled map the lump holds true smoothed normals wherever a smoothing group applies, and the plane
normal only where none does. The two are equal on flat unsmoothed brushwork and nowhere else, so
"we can always derive it from the plane" was wrong.

### What this does NOT license

Reading a lump is not implementing a feature, and a reader with no consumer must not pretend
otherwise: no vertex-layout change, no shader work, no "while I am here" rendering. The consumer
arrives with the feature that needs it, and B194 records what that feature would be.

Collision and water lumps (`BrushSides`, `LeafWaterData`, `AreaPortals`) are the honest edge of
this rule: a demo viewer neither collides nor swims. They are still worth reading when something
plausibly wants them — showing trigger volumes in a demo-analysis tool is not far-fetched — but they
are the ones to defer if any are deferred, and deferring them is a decision to state rather than a
default.

### The conformance instrument already knew, which is the owner's point about ordering

> "yep thats why i say conformance tests first too"

`SdkCoverageTests` extracts every `LUMP_` the engine declares and diffs it against what this project
reads, then writes `docs/SDK-COVERAGE.md`. That file said, in the repository, committed:

```
## BSP lumps
**27 of 66** declared by the engine are handled here.
Not handled: ... LUMP_VERTNORMALINDICES, LUMP_VERTNORMALS, ...
```

**I found the same gap by grepping `Mod_Load*` out of `engine.dll`.** The binary scan was a fine
technique and it was the second time the answer had been derived — the conformance test wrote the
denominator from the SDK first, exactly as the rule intends, and nobody read the output.

**So the rule earns its keep at the point of ASKING, not only at the point of writing.** A
conformance test's whole value is that it holds the denominator before our data can bias it; consult
it before reaching for a decompiler, a binary, or a fresh count.

**And it exposes the instrument's one weak joint.** The denominator is extracted and cannot go
stale; the numerator — `ImplementedLumps()` — is a hand-maintained list. Adding a reader without
adding its name there fails nothing and quietly understates coverage. So a new lump reader is two
edits, and the second is not optional.


## D94 — Core stays the demo decoder, and a shared constant needs a shared REASON

Two things were settled in one exchange on 2026-08-25, and the second reversed what the assistant
was about to do.

### Core does not grow

The owner, when asked where a threshold shared by four projects should live:

> "i mean if that needs to go into a shared project then do it, thats the more proper implementation
> isnt it? or a global variable?"

> "id prefer core to stay mostly centered on the pure demo decode, theres probably stuff there that
> should not be already though, but we shouldnt add to it"

**So: Core is the demo decoder, and things that are not decoding do not move into it — even when it
is the only project everything already references.** The convenient answer and the correct one point
in opposite directions here, which is exactly how Core would have accumulated a viewer's perception
threshold.

**When something genuinely is shared, `Tf2DemoSalvage.Logging` is the diagnostics home** and it
satisfies D92's test for reusing or adding a project: it has no `ProjectReference` of its own, so
depending on it can create no cycle and can pull no decoder, scene or renderer type in behind it.
It already holds `LoggerTiming`, which is the same concern.

**The owner's aside — "theres probably stuff there that should not be already" — is filed rather
than acted on.** `Core/Scene/DemoTimeline.cs` holds `ScenePlayer` and `SceneProp`, which are
arguably scene types in the decoder. Not moved: that is a large change with its own audit, and
nothing is broken by it today.

### A constant is shared only when the REASON is shared

The prompt for the above was three declarations of `StallSeconds = 0.03` — in `SoundCache`,
`MomentScene` and `MainForm` — which read as an obvious DRY violation.

**It was not one, and unifying them would have reverted two recorded decisions.** Both of the
first two say in their own remarks why they are separate:

> "Its own threshold rather than the frame loop's, even though the numbers agree. A constant carries
> no scope: this one is applied to one decode blocking the thread that draws, and the viewer's frame
> threshold is applied to a whole frame. Sharing the symbol would tie two independent judgements
> together, and changing either would silently move the other."

**Three symbols that agree on a number are three judgements, not one fact repeated.** The test for
merging is not "are the values equal" but "is the REASON the same" — and here it is demonstrably
not: one decode, one rebuild step, one whole frame.

**There was a real defect underneath, and it is the mirror image of the one being guarded against.**
`MainForm.ReportSlowMoment` compared a whole moment — the rebuild plus the sampling plus the marker
pass — against `MomentScene.StallSeconds`, a constant whose own documentation says it is "applied to
one step of a scene rebuild". Borrowing a symbol whose stated meaning is narrower than the use is
how the two judgements get tied together, which is the thing the separation exists to prevent.
`StallReport` now declares its own, for the whole-step measurements it makes.

**The general rule, and it is cheap to apply:** before merging two constants that happen to be
equal, read what each is applied TO. Before borrowing one, read whether its documentation describes
your use. Both questions are answered by the declaration site, and neither was asked.


## D95 — The viewer is another instance of TF2: always 3D, always the engine's camera

Stated by the owner on 2026-08-25, while a stale comment in `PointRenderer` was being used to
justify a clip-space drawing path:

> "the whole idea is we never have to change the renderer from 3d or do anything special with the
> cameras other then set that default wide overhead view to free cam, so we are always in engine and
> the user is able to act as if the viewer is always just another instance of tf2 for the most part"

**This extends D49 from a camera decision into a renderer one.** D49 removed `CameraMode.Map`
because a top-down view is a *placement* of a perspective camera rather than a mode of its own.
D95 says the same thing about everything downstream: there is no second projection, no second
drawing path, and no primitive that exists only for a flat view.

### What it settles

- **Nothing is drawn in clip space because "the overhead view has no depth".** That reasoning is
  D49's world, and D49 deleted it. `PointRenderer` still carried it in a comment — "a flat overhead
  view has one axis that does not participate, so the caller sorts by height and the later triangle
  wins" — which was true when written and had quietly become a justification for markers floating
  over a 3D scene.
- **Debug primitives take WORLD coordinates and choose depth per call**, which is what the engine
  does: `DebugDrawLine( vecAbsStart, vecAbsEnd, r, g, b, bool test, duration )` and the
  `bool noDepthTest` field on the overlay record (`game/server/ndebugoverlay.h:24`, `:28`). A leaf
  box describes geometry and should be occluded by it; a player marker is an annotation about
  somewhere you cannot see and should not be.
- **The CPU does not project.** Transforming on the CPU to feed a clip-space shader is a second
  implementation of the camera, and this project has already had it wrong once — the leaf box was
  indexed as a column-vector transform and collapsed a room-sized box into, in the owner's words,
  "a dot that gets kinda triangular".

### What it defers, deliberately

> "the seperate povs displaying at once and the rest of the security cam like stuff i want to add
> comes after we get the 3d right and full parity"

**Several points of view on screen at once, and the security-camera features around them, are wanted
— and they are AFTER parity.** They are recorded here so nobody builds toward them early: a second
simultaneous view is exactly the kind of feature that tempts a second render path, which is the
thing this entry forbids until the first one matches the engine.

**It is also the reason parity is worth the effort rather than an end in itself.** A viewer that is
"another instance of TF2" can add a second camera by adding a second camera. One that has drifted
has to reconcile two renderers first.


## D96 — `playdemo` becomes one of OUR commands — ROADMAP, not now

Proposed by the owner on 2026-08-25:

> "can our cli tool be made to turn playdemo into a shell command so you can actually start viewing a
> demo from command line without any other shell?"

**This is about THIS tool's own command surface**, and the assistant first misread it as launching
TF2 from PowerShell — recorded because a decisions entry that misattributes an idea is worse than no
entry. The owner's clarification:

> "i meant turning playdemo into our own shell command not starting tf2 from powershell but it
> doesnt matter its a roadmap item not something for now"

**It fits what already exists rather than adding a vocabulary.** The viewer speaks Source's console
language (D69, D70): a real `.cfg` works wholesale, `+command value` is honoured on the command line,
and unknown commands are ignored on purpose. `playdemo` is the missing verb in that set — the one
command a person watching demos actually types in TF2 — and adding it to `ConfigConsole` would mean a
real config, alias or `exec` could drive this viewer the way it drives the game. That is the D69
requirement carried one step further: not just "a paste works", but "the thing you would type works".

**Roadmap, explicitly. Not to be started as part of the thin-view refactor.**

### The assistant's misreading, kept because the capability is separately worth having

Source also takes console commands as LAUNCH options, so `tf_win64.exe -game tf +playdemo <name>`
starts the real game on a demo, and **`-condebug` writes the console to `tf/console.log`** — which
turns the shipping engine into an instrument this project can read.

### Why that matters here specifically

This repository's hardest questions are all of the form "what does the engine actually do", and the
answers have come from reading `source-sdk-2013`, shipped data files, and occasionally a decompiler.
None of those can answer a question about the CLOSED renderer's behaviour at runtime. A scripted
`playdemo` plus a parsed `console.log` can:

- **Does the engine object to this map?** The question that cost an evening (B198, B200). The SDK
  snapshot predates TF2's map hash, so there is no citation for it — but the running game will say.
- **Differential rendering.** Launch both, capture both, compare. The project already prefers
  differential evidence to fixtures, because a fixture cannot falsify our own reading of a spec.
- **Era clients.** `docs/TIMELINE.md`'s protocol windows are estimated from changelogs; the period
  clients on `F:` can be driven the same way to turn estimates into measurements.

### What it must not become

**Not a dependency of the viewer, and not part of the gate.** It needs a TF2 install, a real desktop
and Steam running — the three things the measurement boxes do not have and the gate must not require.
It is a diagnostic verb, skipped when the install is absent, exactly as the SDK-backed suites skip.

**And it takes the machine-wide lock.** Launching the game is a UI workload: it steals the
foreground, so it belongs behind `run-exclusive.ps1` like every other desktop-taking run.

---

## D97 — Playback speed is continuous from 0.01 to 8, mirrored for reverse

**Three ways our transport differs from Valve's, and the owner sorted them on 2026-08-26.** The
assistant had raised the comparison as one open question; the answer was that it is three, with
different standing:

> "yea our frame scruber going backwards and us parsing the whole demo first instead of streaming it
> are known changes, the difference in scrubber resolution was not though."

and, clarifying which quantity "resolution" meant:

> "yes the 'resolution' was referring to the time scale."

**So two departures were already deliberate and one was an accident.** That distinction is the whole
value of this entry: a divergence nobody chose looks exactly like one somebody did, and only the
owner can tell them apart.

### The deliberate two, restated so they are not re-litigated

- **Reverse playback.** The engine streams a demo forward and each snapshot is a delta on the last,
  so it has nothing to step back into. This viewer decodes the whole demo to absolute positions
  first, so reverse costs what forward costs. This meets the owner's standing test for an acceptable
  departure — *know exactly why Valve does it, and exactly why we do not have to* — and is the same
  shape as `custom/` HUDs under D91: a deliberate step beyond the game, not a failure to match it.
- **Whole-demo decode instead of streaming.** The premise of the project (`ROADMAP.md` §1) and the
  thing that makes reverse, instant seeking and cross-era decoding possible at all.

### The accident, and what it was

**Valve's timescale is continuous; ours was eleven fixed steps.** Measured from
`game/client/replay/vgui/replayperformanceeditor.cpp`:

| | Valve | ours (before) |
|---|---|---|
| slowest | `TIMESCALE_MIN 0.01f` (`:78`) | 0.25 |
| fastest | `TIMESCALE_MAX 3.0f` (`:79`) | 8 |
| shape | slider, `SLIDER_RANGE_MAX 10000.0f` (`:83`) | 11 steps `[-4 … 8]` |

The slider is integer-valued over `[0, 10000]` and mapped linearly onto the range (`:567`, `:669`,
`:724`) — **10,001 reachable speeds**, a step of about 0.0003.

**A second divergence fell out of checking the first, and nobody had named it either: our slow floor
was 0.25 against Valve's 0.01.** An entire band 25× finer than anything we offered was unreachable,
and that band is precisely what frame-exact review needs — which
`docs/memory/surf-and-jump-are-an-audience.md` records as a real audience for this tool.

### The decision

**Continuous, from 0.01 to 8, mirrored into the negatives for reverse.** Valve's floor is taken
because it costs nothing and serves a real use; Valve's range becomes a strict subset of ours, with
every extension being one of the departures above.

**Where it lives is part of the decision.** The speed ladder, the clamping and the spoken
description were all inside `TransportBar`, a WinForms control in `Tf2DemoSalvage.Viewer3D` — so
building a slider there naively would have *added* a range, a clamp and a position mapping to a
view, which is the opposite of D90. The owner caught this immediately:

> "this fix touched the 3d viewer doesnt it?"

So the quantity is a `TimeScale` value in `Presentation` and `TransportBar` keeps only the widget.
**`PlaybackClock.TimeScale` was already a `double`** — the model was continuous all along and the
view was quantising it, which is why this needed no change below the presentation layer.

**The parity comparison was written before the code**, per `docs/CONFORMANCE.md` and the plan in
`docs/HANDOFF.md`: `TimeScaleConformanceTests` cites Valve's three constants and asserts our range
contains Valve's and our resolution is at least as fine, over Valve's own span rather than ours — a
coarser slider must not be able to pass by being wider.

**Corrected 2026-08-26, after decompiling: `demo_timescale` is a ConCommand, not a ConVar.** This
entry's commit message and the conformance test both described it as "a float convar whose help
string reads *Sets demo replay speed*". The help string is right; the kind was read off the help
string alone and never checked. Its registration is five pushes with a `.text` pointer in the
callback slot — see `docs/findings/37-the-engines-demo-vocabulary.md` — so it has no default and no
flags, because a command has neither.

**The decision above is unaffected, and saying why matters more than the correction.** The parity
reference for the numbers is the *slider* in `replayperformanceeditor.cpp`, read from published
source; the console command was context. What the error cost was a false claim sitting in a
decisions document — which is exactly the failure the owner named when he asked that no research be
left unused: the convar/command distinction was available in the same twenty bytes as the help
string, and taking only the string is what let the wrong claim through.

---

## D98 — The orthographic camera goes entirely (closes B205)

**B205 asked whether the overhead camera should survive now that it is reachable only as a
first-person fallback. The owner, 2026-08-26:**

> "i dont think the ortho cam should survive at all"

**This finishes what D49 started.** D49 deleted `CameraMode.Map` on the reading that an overhead
*view* is a **placement** of the ordinary camera rather than a projection of its own. The projection
survived anyway, reachable through exactly one path — first person with no eye available — which is
a mode nobody chose and nobody could name.

**It also settles what "overhead" still means here**, restating the owner's earlier position: *"the
overhead view is gone unless it is specifically talking about the free cams default poistion"*. So
`OverheadPlacement` **stays** — it computes an origin and angles for the free camera — and
`TopDownCamera`, the orthographic projection, goes.

**A correction to the assistant's account of the mouse wheel, in the owner's words:**

> "the mouse wheel was working in free cam before, i used it all the time"

That is right and the earlier summary read as saying otherwise. In the free camera the wheel calls
`FreeCameraController.Dolly` and always has; only the *ortho zoom* branch was first-person-only. The
distinction matters for this deletion: **the wheel keeps working, it simply stops having a second
meaning.**

**Most of the entanglement turned out to be vestigial, which is why this is smaller than it looks.**
`TopDownCamera` is threaded through `LoadedMap.BuildWorld` into `MapWorld.Build`, which **never reads
it** — a leftover of the top-down culling that
`docs/memory/build-time-shortcuts-assume-the-camera.md` records as having broken the free camera.
The build path needs no camera at all.

**What the first-person fallback becomes is part of this decision**: when there is no eye to look
through, the viewer falls back to the **free camera**, not to an overhead placement of it. A demo
that loses its subject drops the viewer into the view it can always offer.

**The flat player markers go with it, and that is the owner's call rather than a consequence**
(2026-08-26):

> "get rid of the flat markers for now, we will reimplement them into the flycam and make it on
> option later"

**They were the orthographic camera's last real consumer.** `MapOverview.Players` and
`MapOverview.Positions` turn `ScenePlayer`s into `ScenePoint(X, Y, R, G, B)` — flat, two-dimensional,
positioned by projecting through `MapCamera()`. With the world drawn in perspective through the eye
or the free camera and the markers placed by a top-down orthographic projection, **the two do not
agree about where anything is**; the markers were correct only for a view that no longer exists.

**"For now" is load-bearing and this is not a deletion on the merits.** The feature returns as a free
camera option — billboards or a 3D overlay that shares the actual view matrix — which is a different
implementation of the same idea rather than a revival of this one. Recorded so nobody later reads the
empty space as "markers were tried and rejected".

**One older note is worth carrying forward**, because it states the rule any reimplementation has to
obey and it was learned the hard way: **a player drawn as a model must not also get a flat marker on
top of it, and a player without a model must still get one or they vanish.** Asked in two places the
answers drifted, and they did — the markers went on being drawn over the models the moment those
started working, which hid whether the models were there at all.

---

## D99 — `fps_max`'s "cannot be set while connected" is not adopted

**Valve's frame limiter carries a restriction we are not taking.** `fps_max`'s help string in
`bin/engine.dll` reads *"Frame rate limiter, cannot be set while connected to a server"*; ours is
settable at any time. B209 raised it; the owner ruled on it, 2026-08-26:

> "the ' cannot be set while connected' is kinda irrelecent for us, because while we are emulating
> server side stuff to render right, we are not a server"

**The exemption is structural, not a preference.** The restriction is about a client's relationship
with a server, and a demo viewer has none — which is the owner's standing test for an acceptable
departure: know exactly why Valve does it, and exactly why we do not have to.

**A caveat he raised himself, recorded because it is a prediction rather than a finding:**

> "although that means we might run into some bugs if we switch frame rate while the demo is
> playing"

Nothing has been measured against that. If a mid-playback frame-rate change ever misbehaves, this
entry is where the suspicion was first written down.

**And his reading of why the restriction exists at all, kept as HYPOTHESIS and not as fact:**

> "i think that was valves way of stopping fps bugs like those in HL1 that allow speedrunners to do
> some crazy stuff, idk why it would have been transfered from gldsrc to src because source doesnt
> have the same framerate bugs as hl1 and gldsrc did, but i can understand why they would keep you
> from chaning fps in say cs 1.6"

That is unverified — no Valve source or changelog has been read for it — and it is worth keeping
precisely because it is a plausible story: a plausible story recorded as a conclusion is the kind
that gets repeated. Flagged so the next reader knows which it is.

---

## D100 — `engine_no_focus_sleep`, under Valve's own name

**The viewer renders at full frame rate while alt-tabbed; the engine does not.** The other half of
B209. The owner, 2026-08-26:

> "oh and the no focus we should probably implement, but i think the background fps is a cvar, in
> real tf2, if its not then we should make it one so it can be users choice."

**It is a real ConVar, and the decompile settles it exactly.** The owner searched first and found
nothing:

> "yea it doesnt look like theres a cvar for it in tf2, at least not that i could find on
> google/bing, the sleep per frame isnt even mentioned, so i think most people and the searc, diesnt
> know tf2 has a sleep time when in the background because it all talks about lowing graphics setting
> to get better fps int he background"

then: *"check the decomp, see what it really is"*. The registration decodes from twenty bytes of x86
in `bin/engine.dll` at `0x100436AA`:

```asm
push 0x80              ; flags
push 0x102eb2f8        ; default -> the string "50"
push 0x1032e4b8        ; name    -> "engine_no_focus_sleep"
mov  ecx, 0x1066f840   ; the ConVar object
call ConVar::ConVar
```

which is

```cpp
ConVar engine_no_focus_sleep( "engine_no_focus_sleep", "50", FCVAR_ARCHIVE );
```

`FCVAR_ARCHIVE` is `(1<<7)` — *"set to cause it to be saved to vars.rc"*
(`public/tier1/iconvar.h:48`).

**So it is not hidden and not development-only: it is archived, which means Valve treats it as a
user setting and persists it to the config.** Three arguments were pushed, not four, so it has **no
help string** — and that is the entire reason it is undocumented and the searching turned up
graphics advice instead. An undocumented convar and an internal one look identical from outside; the
flag distinguishes them and only the binary carries the flag.

**Default: 50 milliseconds of sleep per frame while the engine lacks focus.**

**Evidence class: measured**, all of it — the name, the default, the flag, and the argument count.
Nothing here is interpolated. An earlier draft of this entry hedged it as "present in the binary,
exposure inferred"; that hedge was correct at the time and is now superseded by the measurement.

**A note on why adjacency could not have answered this**, since it is a reusable trap: the default
`"50"` lives in a pool of short numeric strings beside `dsp_speaker` and `"40"`, nowhere near the
name. Short literals are string-pooled and shared across every convar that wants them, so reading
the bytes around a cvar's NAME finds its neighbours' help text and never its own default.

**What we build:** the same convar, same name, same units, defaulting to 50. D69 makes our
vocabulary Source's, so a real config that sets it works — which is the outcome the owner asked for
either way (*"if its not then we should make it one so it can be users choice"*), reached here
without having to invent anything.

**One precision on the mechanism, because it changes what gets built.** It is a **sleep duration per
frame while unfocused**, not a background frame-rate cap. The name says sleep, and the engine's
neighbouring convar is `mat_powersavingsmode` rather than a second `fps_max`. A "background fps"
number would be a different control with a different feel, so this implements the one Valve has.

**A stale paragraph stood here until 2026-08-26 and said the opposite of the entry above it:**
*"Its default was NOT read. The string table gives the name and no readable default, and reading one
out would need a disassembler."* True when written, and superseded four paragraphs earlier by the
disassembly that reads `push 0x102eb2f8 ; default -> the string "50"`. Left in place, it had this
entry simultaneously measuring the default and declaring it unmeasurable.

**Kept rather than deleted, because it is the third instance of one shape found in a single day** —
see `docs/memory/an-impossibility-claim-expires.md`. `ViewerSettings` said `fps_max`'s default could
not be recovered while finding 37 had recovered it; B209 stood "OPEN, needs the owner" while this
entry had already answered it; and this paragraph outlived its own evidence. In every case the
positive claim advanced and the negative one was never re-read, because nothing depends on a
negative claim and so nothing drags it back into view.

**Where it goes: `FramePacer`**, which already owns the budget and the sleep-or-spin decision. The
window contributes the one thing it knows — whether it currently has focus.

**Built 2026-08-26** as `FramePacer.NoFocusSleep(hasFocus, milliseconds)` with
`ViewerSettings.NoFocusSleep` defaulting to 50, exactly as specified above. One detail this entry did
not anticipate: the sleep goes in `OnIdle` **before** the frame-due check rather than inside the
existing wait, because that wait only runs when a frame is *not* due — an unfocused window would
otherwise have rendered at full rate whenever one was, which is every frame at any reachable limit.

## D101 — No hardcoded controls, ever; every key goes through the config

**The owner, 2026-08-26, in three messages while a speed slider was being wired up:**

> *"no hard coded controls ever"*

> *"and everything gets to be customized so runs through the config"*

> *"do not hard code home, no new hard codes"*

**This is a standing rule rather than a fix for one key.** It was said while looking at the height
cut's `Keys.Home`, but it is stated generally and applies to everything a person can press.

### What prompted it

A UI test pressed `Home` on a focused speed slider and nothing happened. `MainForm.ProcessCmdKey`
compared `keyData` against three literals — `Keys.Home`, `Keys.PageUp`, `Keys.PageDown` — and
returned true, and `ProcessCmdKey` runs **before any control sees a key**. So the height cut was
reaching over every control on the form: `Home` in the search box moved the map instead of the caret,
and the playlist, the scrub bar and the new slider all lost their standard navigation (B212).

Chasing that turned up a worse one with no unusual configuration at all: **`Space` is the default
bind for switch-camera-mode**, so typing `cp process` into the search box toggled first person
instead of inserting a space. In the free camera the flight keys took `w`, `a`, `s` and `d` as well.

### The rule

**Every key a user can press is resolved through the config**, the same way `ConfigConsole` already
resolves a real TF2 `.cfg` (D69). A literal `Keys.X` comparison in a handler, or a `ShortcutKeys` set
on a menu item, is a control nobody can rebind — and D69's whole premise is that a person's own
config works wholesale.

**It follows from D69 rather than adding to it**, which is why it was stated so briefly: if a pasted
config must work, the actions it can bind cannot be a subset chosen by whoever wrote each handler.

### What is NOT yet done, and the ordering is the owner's

> *"yea the migration and refactor for the config, will come after we refactor the views to actually
> be pure views"*

So the debt is filed and not paid yet. Today two actions route through `KeyBindings`
(`SwitchCameraMode`, `ResetCamera`) and the flight keys go through `ConfigConsole`; **every menu
shortcut is still a hardcoded `ShortcutKeys`** — F1 to F12, Ctrl+L, Ctrl+T. That is B214.

**What the rule forbids in the meantime is ADDING to the pile.** A speed slider was about to get
`Home` mapped to 1× — the owner's own suggestion, and a good one, since *"'Home means minimum' is
literally 1x when it comes to video playback, its the default too"* — and it was not built, because
building it would have meant one more literal to migrate. The slider uses the `TrackBar`'s own
platform behaviour instead, which is not ours to un-hardcode.

**Removals count as progress and were taken immediately.** The height cut's three keys are gone with
the feature (B213), and the guard added in their place names no key at all.

### Done 2026-08-26 (B214), and it needed one thing Source does not have

Every key in the viewer now resolves through `KeyBindings`. **`Escape` is the single exception and is
named as one in the code**: it is the way out of full screen, so a config that rebound it away would
leave a user with no menu, no title bar and no key that closes either. TF2 treats `ESCAPE` the same
way, as the escape hatch rather than as a control.

**Making the keys rebindable was the easy half. Choosing defaults was the hard one**, because
`tf/cfg/config_default.cfg` binds 64 keys — **every letter except `o`** — so any single-key default
elsewhere is taken away the moment a real config loads, and the action is silently gone. Six free
single keys, fifteen actions.

**So `CTRL+<key>` was added to the key vocabulary as a deliberate SUPERSET of Source's.** `bind`
takes one key and has no modifier syntax, which cuts both ways: no real config can name a
combination, so nothing pasted is misparsed *and* the entire combination space is unclaimable. That
is the only reason there is room for the viewer's own actions at all. Same shape as `custom/` HUDs
under D91 — a step beyond the game, taken knowingly, in the game's own style.

## D102 — The free camera imitates the roaming SPECTATOR, not the demo camera

**Source has two free cameras and they share nothing but a purpose.** Which one this viewer copies
had never been decided, so its speed was neither — 600 units a second, reasoned from a keyboard-repeat
defect (B97) rather than from the engine.

The parity audit of 2026-08-26 offered `CalcDemoViewOverride` as the reference, on the grounds that it
is literally the engine's demo-playback camera. The owner picked the other one:

> *"the correct speeds to use are spectator speeds, im pretty sure, idk what the demo cam speed is
> even actually for becasue ive never seen a tf2 server which has spectators off really"*

**This reverses an answer they gave twenty minutes earlier** — "Valve exactly, 320 u/s", which is
`cl_demoviewoverride` at scale 1. Recorded as a reversal because the second answer is better for a
reason the first question never surfaced: **`cl_demoviewoverride` ships `"0"`**, so it is a feature
almost nobody has switched on, while spectating is the thing every demo viewer is imitating. The
assistant had presented one of two candidates as though it were the only one.

### What follows from it

`FullObserverMove` (`gamemovement.cpp:2144`) delegates to `FullNoClipMove( sv_specspeed,
sv_specaccelerate )` because `sv_specnoclip` ships `1`, so the clipped body below that branch is dead
at shipped settings and `FullNoClipMove( 3, 5 )` is the reference.

| | value | from |
|---|---:|---|
| normal flight | **960 u/s** | `sv_maxspeed 320 × sv_specspeed 3` — the clamp at `:2260` |
| `+speed` held | **675 u/s** | `cl_forwardspeed 450 × 1.5`, which never reaches the clamp |

**`+speed` is Source's WALK key**, and this viewer had it quadrupling the speed. Under D69 that means
a pasted config did the opposite of what its author meant. `ViewerAction.FlyFast` is `FlyWalk` now.

**And it is not a halving.** `maxspeed` is computed from the unhalved factor on the line above
`factor /= 2.0f`, so the normal case is clamped and the walking case is not: 70.3%, not 50%.

### The rule this sets, beyond the numbers

**When the engine has two mechanisms for a job, which one we copy is a decision to record, not an
implementation detail to pick.** The audit found the divergence correctly and then nearly closed it
against the wrong reference — with a citation, which would have made it look settled. Ask which
mechanism, then cite it. Same family as
`docs/memory/name-the-reading-you-picked.md`.

---

## D103 — HDR lighting is planned work, not a bug fix; LDR stays until it lands

**Owner-set, 2026-08-27**, on being shown that TF2 ships HDR on by default: *"i think most comp
players run ldr so i didnt know that tf2 shipped with it on be default, we will need to implement
that, but its roadmap so write it down"*.

### What was found

Every compiled map carries **both** lighting compiles, and this project reads the LDR one with no
branch — `BspAmbientLight.Read` takes `LUMP_LEAF_AMBIENT_LIGHTING` (56) and its index (52), and
`BspLightmaps` takes `LUMP_LIGHTING` (8). The HDR pair (53, 55, 51) is never touched. Measured on
three maps spanning the era axis, both compiles are present at effectively the same size, and
`cp_granary`'s are byte-identical (`LightingLumps_AcrossTheEraAxis_AreReported`).

**The LDR reader and the missing tone map are consistent with each other, which is why neither is a
defect.** `FinalOutput` (`common_ps_fxc.h:345`) scales by `LINEAR_LIGHT_SCALE` before `SRGBOutput`,
but `viewrender.cpp:2214` sets that scale only under `HDR_TYPE_INTEGER`; under `HDR_TYPE_NONE`
Source does no tone mapping and overbright light clips. **That is exactly what this renderer does**,
so as an LDR renderer it is already at parity, and bolting a tone mapper onto the LDR path would
match neither engine mode.

### The decision

**Implementing HDR is a roadmap item, and it is a fork rather than a patch.** It means reading the
HDR lumps, and then the tone mapping that HDR obliges — including autoexposure, which is what makes
walking from shade into sun settle rather than stay blown out. Half of it is worse than neither.

**LDR is not a wrong choice in the meantime, and the owner's reason is the interesting part**: the
audience this viewer is for largely runs LDR anyway. So the current state is what a competitive
player's own client looks like, and the gap is against Valve's *default* rather than against what
these demos were watched in.

**This was not filed as a B-number**, because it is not a defect. It went to the roadmap, and the
correction that put it there is worth keeping: it arrived while investigating B170 and was briefly
treated as a candidate fix for it. It is not one — see B170, where only the weapon washes out while
the arms beside it are lit by the same cube, the same sun and the same tone-map-less output.

---

## D104 — Inventory Valve's convars deliberately, instead of adding one each time we miss it

**Owner-set, 2026-08-27**, immediately after `mat_phong` had to be built before B170 could be
tested at all: *"we are going to go through all valves settings, see which ones we need to import,
and fill out our cvar list completely so we can stop forgetting to make shit bindable the right
way"*.

### What keeps happening

**Every convar so far has arrived because something broke without it**, one at a time:

| convar | what forced it |
|---|---|
| `r_drawviewmodel`, `viewmodel_fov_demo` | B166 — the owner's own config turns viewmodels off and this viewer drew them anyway |
| `mat_leafvis`, `mat_showlowresimage` | B210 — a debug view was unreachable and its shortcut toggled a different one |
| `engine_no_focus_sleep` | D100 — the viewer rendered at full rate while alt-tabbed |
| `mat_phong` | B170 — the experiment that would settle it had no instrument |

That is a pattern, not four coincidences. **The gap is only ever found by hitting it**, and the cost
lands in the middle of investigating something else: `mat_phong` had to be designed, tested, wired
and merged before the actual question could be asked.

### The decision

**Enumerate the whole surface once, from `tf/cvarlist.log`** — 3,668 shipped convars with defaults,
flags and help text, which this project already treats as a source (`docs/findings/40`). Classify
each: implemented, deliberately out of scope, or a gap worth filling. The output is a checked list
rather than prose, so a later reader can tell "we decided against this" from "nobody looked".

**"Bindable the right way" is the second half and is not automatic.** D101 is that every control
goes through the config; D69 is that a real config must work wholesale. A convar can be implemented
and still be wrong on both counts — reachable only from our own menu, or under a name we invented.
`mat_phong` nearly shipped as a menu item alone, and the owner's correction is what put it in the
config: *"if its a valve setting then yes it goes in the config and can be imported from a
config"*.

**Sequenced after B170.** The owner: *"after this mat_phong test, if its not the issue"*. The
inventory is a sweep, and starting one while a live bug is mid-diagnosis is how the diagnosis gets
dropped.

---

## D105 — Conformance tests get their own project, and should have already

**Owner-set, 2026-08-27**: *"conformance test sshould have gotten their own project earlier"*.

Said while `PhongCvarConformanceTests` was being added to `Tf2DemoSalvage.Rendering.Tests`, which is
the fourth conformance suite to land in a project named for something else.

**This is a repeat of a mistake already made once.** B184 moved the render and scene suites into
their own assemblies after they had spent weeks in `Viewer3D.Tests`, and the reason recorded there
was that tests drift from their subjects when they do not live beside them. Conformance tests have
the same problem from the other direction: their subject is not a class of ours at all — it is
**Valve's behaviour** — so no project named after one of our assemblies is ever the right home.

**`docs/CONFORMANCE.md` already selects them by name**, with `--filter
'FullyQualifiedName~Conformance'`, and `CLAUDE.md` requires any class whose name contains
`Conformance` to keep it. That convention exists precisely because the suites are scattered; a
project boundary would carry the same meaning structurally and stop depending on a naming rule
nobody can enforce at compile time.

**Not done in the same change as `mat_phong`**, deliberately — the move is mechanical, touches
several assemblies and every count floor, and folding it into a bug investigation is how both get
harder to review. Recorded here so the sequencing is a decision rather than an omission.

---

## D106 — Nothing is hardcoded that Valve does not hardcode

**Owner-set, 2026-08-27**, while auditing the convar surface under D104:

> *"the vast majority of the cvars are going to be constants that never change, but i still dont want
> anything hardcoded that valve does not hard code, because doing so makes the rendering engine we
> are using less agnostic, and while i dont plan to include any other source engine games, there is a
> small possibility i change my mind and decide to run any demo from gldsrc to source 2, so i want to
> be able to do that pretty easily if we decide to."*

### The rule

**If Valve expresses a value as a `ConVar` — a name, a default, and the ability to change it — this
project expresses it the same way.** A `const float` is only correct where the engine itself has a
constant.

**The test is what Valve wrote, not whether the number ever moves.** Most of these never change in
practice; that is not the point and the owner says so explicitly. A value that never changes is still
a *named, defaulted* value in the engine, and copying only the number throws away the two parts that
make it portable.

### Why: engine-agnosticism, and the era axis is already the live case

The motive is not that users will retune movement speeds. It is that **a hardcoded number bakes one
engine build's answer into the renderer**.

**And that is not hypothetical here.** The owner, correcting a first draft of this entry that framed
it as future-proofing: *"well we already are handeling demos across engine versions, but not across
games"*. This project reads protocols 11 through 24, from 2007 builds to 2026 ones — a nineteen-year
span of one engine, with the corpus to prove it. A default that moved between TF2 builds is already
wrong for half the corpus, and a `const float` cannot even express the question.

**Cross-GAME is the hypothetical part**, and the owner says so plainly: GoldSrc through Source 2 is
a possibility rather than a plan. So the rule earns its keep on the era axis this project already
serves, and the cross-game case is the extension it happens to make cheap.

That ordering matters. A rule justified only by a maybe gets relaxed the first time it is
inconvenient; this one is justified by the corpus sitting in `tools/corpus/`.

### What this is NOT

**Not a demand that every value be settable from a config.** D104 establishes three homes for a
value — the watcher's config, the demo, or Valve's default as a fallback — and this decision is about
the FORM rather than the source. A default read from a named declaration is still a default; it
simply stays a *default* rather than becoming a constant.

**And no category is exempt, including the ones a demo can never supply.** The owner, on a first
draft of `CVAR-COVERAGE.md` that let client-side convars off: *"baked default is never the right
answer i dont think, at least not if its not a baked default valve has"*. That page had said a baked
default was correct for the seven client-only convars — `snd_gain`, `cl_showpos` and the rest — on
the reasoning that a demo cannot carry them. That reasoning settles where the value COMES FROM and
says nothing about how it is written, which is what this decision governs. Valve declares all seven
as ConVars, so all seven are named defaults here too.

**The one real exemption is a value Valve itself hardcodes.** `MAX_EDICTS`, a struct's field order,
the overbright factor of two — the engine wrote those as constants, so a constant is the faithful
copy. The test is always "what did Valve write", never "does this number ever change".

**Not retroactive panic.** Twenty are baked today, measured under D104 and listed in
`docs/CVAR-COVERAGE.md`. They are wrong by this rule and none of them is urgent, because every corpus
demo measured ran the engine's own values.

### The measurement that motivated it

Every era demo's `net_setconvar` was read, and **none of the twenty appears in any of them**:

```
2007 stv   6   sv_skyname, mp_timelimit, think_limit, sv_turbophysics, mp_winlimit, tv_transmitall
2011 stv   5   sv_skyname, think_limit, sv_turbophysics, tf_gamemode_cp, tv_transmitall
2013 stv   9   ... plus mp_maxrounds, steamworks_sessionid_server
z1800     30   mp_allowspectators, mp_tournament, mp_tournament_post_match_period, ...
f12 pov    6   func_break_max_pieces, sv_skyname, think_limit, sv_turbophysics, tf_gamemode_cp, ...
```

So the baked defaults are right for every demo this project has — **by luck of the servers running
Valve's values, not by design**. A server that raised `sv_maxspeed` would send it, this viewer would
receive it, decode it, and ignore it.

---

## D107 — LINQ is a test tool; the program proper does without it

**Owner-set, 2026-08-27**, on a `Join` over a `Select` added to the trace writer:

> *"linq can be slow if its in a hot path, i dont like link in the program proper so performance stays
> high, its really only a test thing in this project."*

### The rule

**Never on a hot path. Off one, allowed when what LINQ buys outweighs the cost — and it is always a
cost.** Tests may use it freely.

The first draft of this entry made it an outright ban and argued that a second standard "splits every
reader into is-this-hot arguments nobody wins". The owner corrected that immediately: *"if its not on
a hot path and the better things linq does overrides the downsides, i am open to having it in the
program, but it is a performance hit."* Recorded because the overstatement was mine and a rule
stricter than its author intended gets cited later as if it were.

So the test is a judgement the author has to actually make, not a keyword ban:

1. **Is this a hot path?** Then no, regardless of what the query buys.
2. **Otherwise, does the query genuinely read better or fail less often?** If yes, take it knowing it
   costs allocations. If it is a wash, the loop wins by default.

The cost is the enumerator and the closure, not taste: `Select` allocates, `Join` allocates, and a
lambda capturing a local allocates again. In a method called once at load that is invisible; in one
called per message of a demo, per entity of a snapshot, or per frame, it is the whole cost.

### What counts as a hot path here

Per-message decode and text, per-entity instancing and posing, per-batch drawing. Those are where
every performance finding of the last month landed — B181's ordering pass, B189's uncounted worn
lighting, B191's per-frame log formatting — and a query there is paid tens of thousands of times a
second.

**Load time counts too.** The owner: *"we just have to make sure none of its in a hot path, or slowing
loads down."* A map load is once per demo and still the thing a person waits on, so "not per frame"
is not the same as "free". Asset resolution and config parsing are where the second half of the rule
applies, and even there the question is whether the query earns its cost rather than whether anyone
will notice.

**And LINQ is already sparing here**, which changes what the debt below is worth. The owner:
*"linq is uses very sparingly already, so we just have to make sure none of its in a hot path"*. The
job is a check, not a migration.

### The debt this creates, measured rather than waved at

**45 of 356 files in `managed/` declare `using System.Linq;` — 13%.**

| project | files with LINQ | of |
|---|---:|---:|
| `Scene` | 15 | 58 |
| `Core` | 10 | 102 |
| `Content` | 7 | 70 |
| `Presentation` | 4 | 38 |
| `Audio` | 3 | 27 |
| `Render` | 2 | 16 |
| `Viewer3D` | 2 | 15 |
| `Animation`, `Cli` | 1 each | 8 each |

**This decision does not order a sweep, and under the corrected rule most of the 45 may be fine.**
The number worth having is not "how many files use LINQ" but "how many use it on a hot path", and
that is unmeasured. `Scene` and `Core` are where to look first, since they hold the per-entity and
per-message paths; a query in a map loader or a config reader is exactly the case the owner is open
to.

**What was forbidden outright is the one that prompted this.** Rendering `net_SetConVar` was a
`string.Join` over a `Select`, in a method that runs once per message of a demo — a hot path by any
reading, and the first test of the rule failed it.

---

## D108 — The budget is one millisecond a frame, and the engine we are copying meets it

**Owner-set, 2026-08-27**, as the thing to understand above every other performance rule:

> *"what must be understood above all is that at 1000fps we have to do everything in around 1
> millisecond and the real tf2 can do it."*

### The standard

**One frame is one millisecond, and that covers everything** — sampling the timeline, deciding what
is drawn, packing, posing, lighting, the viewmodel pass, the HUD, the sound. Not one of those gets a
millisecond; all of them share one.

**And the comparison is not aspirational.** TF2 renders the same map, the same players, the same
weapons at that rate on the same machine. Whatever this project cannot do inside the budget, the
engine it is copying does — so a shortfall is never evidence that the target is unreasonable. It is
evidence that something here is doing more work than Valve's equivalent, and the interesting question
is always which.

### What follows from it

**A cost is measured against a millisecond, not against a stopwatch's noise floor.** "It only takes
three milliseconds" is not a small number; it is three frames. Every stall this project has found
reads differently in that light: B189's deterministic 130 ms was a hundred and thirty frames, and
B191's per-frame log formatting was paying for strings nobody read.

**"Fast enough" is not a finding.** The number to report is the share of a millisecond, and the
comparison is what Valve spends on the same work — which is usually readable, because
`source-sdk-2013` is right there and this project already reads it for everything else.

**It also bounds D107 and every convenience like it.** A `Select` is not banned because allocation is
distasteful; it is banned on a hot path because the path has microseconds to spend and an allocation
is not one of the things worth spending them on. The same test applies to anything else that looks
harmless per call: at a thousand calls a frame, per-call costs are the frame.

### Where it does not apply, and saying so keeps it credible

**Load time is not this budget** — it has its own, which is a person's patience rather than a frame,
and D107's note on loads covers it. **Tests are not this budget either**; a test's cost is developer
seconds and its clarity is the product.

The rule is about the frame, and the frame is one millisecond.

---

## D109 — One place decides that a missing prerequisite is a skip, not a failure

**Owner-set, 2026-08-27.** Asked whether pulling the conformance tests into their own project (D105)
was worth it, the owner put the question the useful way round:

> *"does having the the tests seperate really gain us anything, or am i stupid for wanting them
> seperate?"*

and, once the answer came back that most of the value was in one small piece rather than in the
fifty-file move:

> *"ok write the helper into the handoff, that will be done first."*

So this is the part of D105 that was actually load-bearing, done on its own and ahead of it.

### What it fixes, measured

**CI is the machine without Team Fortress 2, and it is the only place the no-install path ever
runs.** A suite that *asserts* rather than *skips* when the game is absent therefore reports a fact
about the runner as a defect in the code. That happened twice on 2026-08-27, on two separate pushes,
on `LightingLumps_AcrossTheEraAxis_AreReported`.

It was not carelessness, it was arithmetic: **ninety-four test files carried their own copy of the
install gate** — their own list of Steam library roots, their own existence check, their own reason
string, each written from memory. `GameInstall` had already been extracted for exactly this reason
and its own remarks record the count at **seventy-three**; it has since grown, because a shared
locator that nothing forces you to use is a suggestion.

**Three of those copies were independent locators rather than call sites**, and they did not agree:

| copy | `TF2_FOLDER` accepted when | skips? |
|---|---|---|
| `SdkReference.GameInstall` | it contains `tf2_textures_dir.vpk` | no — returns null |
| `Rendering.Tests.Tf2Install` | the folder merely exists | no — returns null |
| `UiTests.ViewerSession.RequireTheGame` | the folder merely exists | yes, inline |

The two lax ones would accept a stale or mistyped `TF2_FOLDER` and run against the wrong install
while every other suite skipped — the silent-skip trap `GameInstall`'s own remarks were written
about, from the other direction.

### The shape

**`Skip` holds the decision; the locators keep holding the facts.** `GameInstall` and `SourceSdk`
still return null when their subject is absent, because a survey that walks several maps has to
carry on past the ones it does not have rather than abandon the ones it does. `Skip.Because` and
`Skip.Unless` turn a null into an `Assert.Ignore` — the *only* thing they do.

`GameInstall.Require()`, `GameInstall.RequireFile(path)` and `SourceSdk.Require()` are the forms a
test reaches for. `RequireFile` keeps two reasons apart on purpose: "the game is not installed" and
"the game is installed and this map is not" are different facts about the machine and call for
different actions.

**A skip still counts toward the suite total**, so `build/gate.sh`'s exact floors keep working
unchanged — a test that skips has not gone missing.

### Two choices worth defending

**It lives in `Tf2DemoSalvage.SdkReference`, not in a new test-support project.** The handoff said
"a test-support project", written before checking; that project already exists under another name
and already holds `GameInstall`. Splitting a locator from its own skip helper across two assemblies
would mean two references to do one thing. The name is now half a misnomer and that is the cost
being accepted.

**NUnit enters that project, for `Skip` alone.** It was deliberately assertion-free — "the caller
decides" — and that contract is unchanged: the accessors still return null. What changed is that one
of the decisions a caller can make is now named once instead of spelt out per file. Not a new
dependency for anyone: every project referencing it is a test project.

### The sweep, and what it turned up

**All ninety-four are done**, in a second commit and by hand — a find-and-replace across ninety-four
files is exactly the edit `docs/memory/replace-all-is-a-claim-about-every-site.md` was written about,
and the shapes were not uniform: sixteen were locator properties, twenty-one were `const` folder
paths, and the rest were local variables, map paths and candidate lists.

**Forty of them were not duplication at all — they were pinned to one machine.** A plain
`"F:/SteamLibrary/steamapps/common/Team Fortress 2/tf"` with no `TF2_FOLDER` override and no
fallback: correct on the owner's computer, and on any other it fails the `File.Exists` beside it and
takes the `Assert.Ignore` branch. **A test that has stopped measuring anything reads as a skip**,
which is the exact silent failure `GameInstall`'s own remarks record from the corrupt-path incident.
That was the sweep's real return, and it was invisible until the locators were counted.

Three smaller things came out with it: two copies recognised an install by `Directory.Exists` alone
(a Steam library keeps the folder for an uninstalled game), one test held an absolute path under one
user's home directory for a repository file, and `MapCache.RequirePath` lost its unreachable throw.

**The control was the count.** Content.Tests measured 682 passed / 13 skipped / 695 total before the
sweep and exactly that after it; the whole gate is unchanged at twelve assemblies. Finding the
install by a different route is supposed to change nothing on a machine that has it, and a moved
number would have meant a test had quietly stopped running.

What is left is unrelated to this: the tests that build a *synthetic* Steam layout under a temp root
(`MapLocatorTests`, `MapProviderTests`) keep their paths, because that layout is their subject.

## D110 — Opaque models draw biggest first, by the world box, per instance

**Owner-set, 2026-08-27**, and set twice — once to ask for the engine's behaviour and once to stop a
shortcut being taken inside it.

The first, on finding that this project sorted its world by material and its models not at all:

> *"why dont we sort like valve does?"*

and, on being offered culling instead:

> *"i want valve parity here before we worry about culling"*

The second is the one worth keeping. Having read `DrawOpaqueRenderables`, I proposed most of it and
then quietly proposed less:

> *"I realize computing a true world-space bbox per instance per frame is heavy, so the simplest
> faithful approach is to compute each model's model-space bounding box once at pack time and use
> its largest axis as `fDimension`, since Valve's models are mostly unscaled…"*

> *"what does valve do, do not simplify valve unless i give you permission and you explain why"*

— and then, on the SDK sources for the parts I had not read:

> *"then read them, and implement them"*

**What Valve does.** `CRendering3dView::DrawOpaqueRenderables` draws brush models first, then walks
four size buckets from largest to smallest under its own comment, *"Draw static props + opaque
entities from the biggest bucket to the smallest"*. The thresholds are `DetectBucketedRenderGroup`'s
and carry Valve's names for the sizes: `200.f // tree size`, `80.f // player size`,
`30.f // crate size`. The measure is `CalcRenderableWorldSpaceAABB` → `TransformAABB` — the box the
model occupies **after** its transform — and the box itself is `C_BaseAnimating::GetRenderBounds`,
which takes the authored clipping box or the movement hull and unions in the playing sequence's own
box. It is occlusion bought with a sort: a large object drawn early fills the depth buffer, so what
is behind it fails the depth test before its pixels are shaded.

**Why the shortcut was wrong, and it is not the reason I gave for it.** I justified it with "models
are mostly unscaled", which is true and irrelevant: rotation alone changes the world extent.
Working the arithmetic afterwards also corrected a claim I had *already committed* — a comment
saying a long prop turned forty-five degrees "buckets larger". It buckets **smaller**. A 100 × 10
box at forty-five degrees has world spans of 77.8 on both X and Y, down from 100; a **cube** at the
same angle grows by √2. The direction depends on the shape, so no single sentence about rotation is
true, and a packed-once extent is wrong in both directions rather than merely conservative.

**On tolerances**, asked while I was pinning the scout's hull to a tenth of a unit:

> *"are we dropping the .027? that can be a problem, stacking tolerances and shit"*

Right, and it exposed three of my own tests that could not fail. `StudioBoundsTests` now asserts all
six of the hull's floats exactly and carries no tolerance at all — a four-byte misread lands on a
neighbouring float of similar magnitude, which is precisely what slack forgives.

**Where the sort lives, and where it does not.** At the opaque draw loop, not at the scene build:
the translucent pass reads the same list and must stay back-to-front by distance, because blending
is order-dependent. Brush models are already drawn by the world path ahead of every model, so
Valve's brush-first step needs nothing arranged. The inner static-props-after-entities split has no
counterpart here — a map's props arrive as ordinary model instances — so the bucket order is the
whole of it.

**The sort had no test at its call site, and that was measured rather than assumed.** With
`OpaqueBuckets.InDrawOrder` deleted from `Device3D`, **all 566 rendering tests stayed green**: the
sort changes a frame rate, not a picture, so neither a unit test nor a screenshot can see it. The
worse failure is quieter still — an unset `ModelInstance.Bounds` is a zero box, so every model
buckets as the smallest and the sort returns its input unchanged. `Device3D` now writes one census
line per device (`opaque draw order: 49 models, buckets 1/6/0/42`) and a UI test asserts the spread
covers more than one bucket, which is the observation that distinguishes the two.
