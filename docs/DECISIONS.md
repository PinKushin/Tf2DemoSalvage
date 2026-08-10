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
- **Prefer publicly published demos. Self-recorded ones are allowed, owner's call.** A POV demo's `client_name` header field is the recording player's name, and the string tables carry player names and SteamIDs. Committing the owner's own POV demo would bind their handle to a public repository permanently, and in LFS history at that. Public league demos carry other players' names too, but those matches were published by the league itself, so committing them exposes nothing new.
  - Format coverage needs no self-recording: ETF2L publishes POV demos (`etf2l-12025-pov-2020-07-21.dem` is one), which covers the POV-only command paths.
  - Correctness ground truth still does, since nobody knows what happened inside a public demo. Owner's position (2026-08-07): their name and SteamID are already widely published, so committing a self-recorded demo is acceptable, recorded under an altered screen name to be marginally harder to search for. `tools/corpus/local/` is git-ignored and stays available for anything they would rather keep off the repo, but it is not mandatory.
  - One technical caveat on the alias: the `userinfo` string table carries the **SteamID** next to the display name, so changing the screen name does not break the link to the account. It reduces casual searchability, nothing more. Not a problem given the owner's stated position — just do not expect more from it than it delivers.

- **The one-byte-short `dem_stop` is not a salvage fixture.** It was recorded as one; two further demos showed every TF2 demo ends that way, so it is the normal terminator (`SPEC.md`).

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
