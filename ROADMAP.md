# TF2 Demo Parser — Architecture & Roadmap

Status: locked for initial implementation (2026-08-07). See `docs/DECISIONS.md` in the repo for the ADR-style record of every choice below.

Goal: recover data from TF2 `.dem` files of any age — including demos Valve's own updates have broken — and eventually view them, in the spirit of the Quake community's demo tools (parse → readable text/data → 2D playback → full 3D playback), without depending on Valve's live client.

## 1. Why old demos break, and why that doesn't actually block us

A `.dem` file has three layers:

1. **Container envelope** — `HL2DEMO` header + a stream of commands (`dem_signon`, `dem_packet`, `dem_synctick`, `dem_consolecmd`, `dem_usercmd`, `dem_datatables`, `dem_stringtables`, `dem_stop`, and `dem_customdata` in newer protocol versions). This has been very stable across TF2's history — only a handful of "demo protocol" version bumps in 18 years.
2. **Network protocol** — the actual messages inside `dem_signon`/`dem_packet` chunks (`svc_ServerInfo`, `svc_SendTable`, `svc_PacketEntities`, `svc_GameEvent`, `svc_UserMessage`, `svc_StringTable`, etc). This is what changes almost every major update — message IDs shift, bit layouts change, new message types appear.
3. **Entity schema (SendTables/DataTables)** — the actual field layout for every networked entity (players, weapons, objects, etc). Critically, **this is embedded inside every demo file itself** (`dem_datatables`). A demo is self-describing: it doesn't need to agree with the *current* game's entity layout, because it carries the layout that was active when it was recorded.

The July 25, 2023 break (`RecvProp type doesn't match server type for DT_ObjectDispenser/healing_array`) happened because the live **client** validates incoming SendTables against its own compiled-in class definitions. A standalone parser that reads *only* what the demo provides never hits that check — it just needs a decoder that's schema-driven (reads whatever SendTables the demo carries) rather than hardcoded to one era's field layout, plus correct per-version handling of the lower-level bit-packing/message-ID quirks in layer 2.

That's the actual engineering problem: not "know every version of TF2," but "build one generic SendTable-driven decoder + a small table of documented quirks per demo/network protocol version range."

Prior art worth studying (not depending on, given license/language mismatch, but useful as a reference to cross-check against): [demostf/parser](https://github.com/demostf/parser) (powers demos.tf, handles the full multi-year corpus), `tf-demo-parser` (the crate behind it), and the format writeup in [demboyz's DemFormat.md](https://git.botox.bz/CSSZombieEscape/demboyz/src/commit/3858162c9c0fb0988e30f61de526ebfe85eb1e2f/docs/DemFormat.md).

## 2. Language architecture

Per your call: no Rust, no C++, no Python — and per further discussion, **no native C either, until/unless Phase 3 actually proves it necessary.** Modern C# (`unsafe`, `Span<T>`, `stackalloc`, `MemoryMarshal`, `System.Numerics` SIMD) is fast enough for bit-level demo decoding and bulk corpus processing. Pure C# for Phase 1 and 2.

- **`managed/Tf2Demo.Core` (C#)** — the actual decode engine lives here now, not in a separate native library: container parser, bit-reader/varint primitives, SendTable-driven entity delta decoder, string table decoder, plus the object model (ticks, entities, game events, chat) built on top of it.
- **`managed/Tf2Demo.Cli`, `Tf2Demo.Viewer2D`** — batch/parallel processing of a demo backlog, JSON/CSV/SQLite export, and the 2D viewer, all consuming `Tf2Demo.Core`.
- **`native/libtf2dem`** — kept as a placeholder folder only. Not started for Phase 1/2. Revisit only if Phase 3 profiling shows a specific piece (most likely a per-frame render-loop step, not demo decoding) genuinely needs native code after `unsafe` C# has actually been tried and measured — a last resort, not a default. If that trigger ever fires: default to C, with Zig as an open long-shot alternative (not C++) — it exports a plain C ABI natively, so it's the same P/Invoke story as C with real memory-safety improvements and none of C++'s naming-convention/template baggage, at the cost of its own build step outside the main `.sln`. See `docs/DECISIONS.md` D2 for the full reasoning.

**Native build, if it's ever needed: MSBuild/vcxproj**, not CMake — would live as a native project in the same Visual Studio solution as the C# projects. Trade-off already accepted for that hypothetical: Windows/MSVC-only, no cheap Linux ASan/UBSan fuzzing CI. This only matters if D2's "revisit if Phase 3 proves it necessary" ever actually triggers.

## 3. Phased roadmap

**Phase 0 — Corpus & spec-mining.** Collect reference demos spanning eras: pre-2013 (pre-SteamPipe), 2013–2015, ~2018 (64-bit update), 2020–2022, immediately before/after the July 2023 break, and current. This corpus is both ground truth and the regression suite. **This is the one input only you can supply** — do you have a personal stash of old demos, and/or should we plan to pull from community archives (comp league demo archives like RGL/ETF2L/ozfortress, teamfortress.tv, demos.tf's own public archive)?

**Phase 1 — Core parser (C#, `Tf2Demo.Core`), text/structured output.** Envelope + `dem_datatables`/`dem_stringtables` parsing, generic SendTable-driven entity decode, normalized event stream (entity spawn/update/delete, game events, chat, user messages, tick timing). Output: a Quake-style readable text dump, plus JSON Lines and/or a per-demo SQLite file (self-contained, queryable with plain SQL, pairs naturally with a C core). **This alone delivers the core "recover lost demos" goal**, independent of any viewer.

**Phase 2 — 2D top-down viewer (C#).** Player positions/orientation/deaths/objective state scrubbed over time on a top-down map projection. Use TF2's shipped overview/radar images where they exist, fall back to a wireframe top-down projected from BSP world brushes where they don't.

**Phase 3 — Full 3D native-quality viewer (long-term stretch).** Honest framing: matching the actual TF2 client's visual fidelity means writing a lightweight Source-engine renderer — BSP world geometry + lightmaps, MDL/VVD/VTX skeletal player & weapon models, VTF/VMT materials, animation from the demo's bone/pose data, particles, HUD. That's an engine-team-sized undertaking, not a side feature.

- **Phase 3.0 / v0.1 (locked scope):** everyone rendered as a sphere or capsule/pill — literally reusing the same primitive shapes TF2's own hitboxes already use internally — team-colored, positioned/oriented from the demo's entity data, over simplified flat-shaded world geometry (BSP brushes only, no lightmaps/materials). Tractable multi-month goal, not multi-year, and immediately useful for reviewing a match.
- **Phase 3.x (later, unscoped):** real player/weapon models, materials, animation, particles, HUD — fidelity work, only after v0.1 exists and proves the pipeline (entity → transform → render loop) end to end.

Rendering backend, when we get there: **Vortice.Windows** (actively maintained, modern successor to SharpDX, thin managed wrapper over real D3D11/12) fits your Windows/DirectX + C# preference directly — no need to drop into C for the renderer itself, only asset-format parsing if you want that shared with the C core for consistency.

**Phase 4 — Demo repair (replay compatibility with the live client): parked, essentially indefinitely.** Feasibility is genuinely uncertain — it would mean rewriting a demo's embedded SendTables/entity data to match whatever schema the *current* client expects, for every historical schema shape, which is a moving target Valve keeps changing. And if Phase 3 exists, the actual user need ("see what happened in this old match") is already met without fighting the client's validation at all. Keeping it noted here only so it isn't forgotten, not because it's expected to happen.

### On the Source SDK — options weighed

One important correction to flag: Source SDK 2013 does **not** actually contain the demo/netcode parser or the renderer (`engine.dll`, `materialsystem`, the actual .dem reader) — those stay proprietary and closed. What it *does* contain is the mod-side game code (client/server DLLs), `tier0`/`tier1` utility libs, mathlib, and the map/model compiler tools (`vbsp`, `vrad`, `studiomdl`) with their format headers (`bspfile.h`, `studio.h`). So it's only relevant to Phase 3 (asset parsing for the 3D viewer), never to Phase 1/2 demo parsing itself.

| | Clean-room C/C# (VDC docs + prior art as reference) | Source SDK 2013 headers/utils (C++) |
|---|---|---|
| **Wins** | Stays in your chosen stack end-to-end; no license entanglement — you own the whole codebase outright and can license/distribute however you want; forces you to actually understand the formats, which pays off when something inevitably doesn't match docs | Authoritative, exact struct layouts and constants straight from Valve — removes a whole category of "reverse-engineering drift" bugs; battle-tested math/BSP utility code you don't have to rewrite; if you ever want to lean on `studiomdl`/`vbsp` themselves rather than just their headers, that code already exists |
| **Drawbacks** | Real risk of subtle field/offset bugs versus community docs that occasionally lag or disagree; more up-front effort per format | SDK license (Source 1 SDK License) is written around "non-commercial mods that require the base game to run" — using it in a standalone public tool is a legal gray area, not a clean fit; pulls C++ into the codebase; ties that component's build to the SDK's own build assumptions (older MSVC toolchain conventions, etc.) |

Given you're fine with C++ *if* it's genuinely needed: recommend staying clean-room C/C# for Phase 1/2 (SDK has nothing to offer there — it doesn't contain demo parsing at all), and treating SDK-vs-clean-room as a per-format decision *within* Phase 3, made only if a specific format (most likely MDL/VVD/VTX skeletal animation, which is the gnarliest one) proves too error-prone to reverse-engineer cleanly. If we do reach for it, the same ABI-boundary pattern as the C core applies: wrap the SDK-dependent piece behind a narrow C-callable interface so it's an isolated, swappable native component rather than something that spreads C++ through the rest of the codebase.

## 4. Repo scaffold (once we lock the plan)

```
tf2-demo-salvage/
  native/libtf2dem/      placeholder only — not used unless Phase 3 proves it necessary
  managed/
    Tf2Demo.Core/        the actual decode engine (Phase 1) + object model
    Tf2Demo.Cli/         batch parse, text/JSON/SQLite export
    Tf2Demo.Viewer2D/    phase 2
    Tf2Demo.Viewer3D/    phase 3 (v0.1 primitives, then fidelity work)
  tools/corpus/          manifest + demo files
  docs/                  per-era format notes, ADRs
  tests/                 golden-output regression tests, one per corpus demo
```

CI: build the C core + run the C# test suite against the full corpus on Windows, fail on any output regression. License: MIT (locked), with a clear note that it's an independent/clean-room project unaffiliated with Valve and ships no game assets.

## 5. Corpus status

Only confirmed specimen so far: `z1800.dem` — FACEIT SourceTV demo, `koth_harvest_final`, demo protocol 3 / network protocol 24 (matches the documented July 2015 TF2 build), ~14.4 min / 57,551 ticks at the standard 66.67 tick rate, header internally consistent, file structurally intact. It fails to play in the current client for the same class of reason as the July 2023 break (client-side SendTable validation against the live schema) — the file itself isn't damaged, it's a compatibility problem a standalone parser sidesteps by design.

Reality check on going further back: TF2's early competitive era (~2008–2010, ETF2L S2–S10-ish) ran mostly on Mumble for casting rather than recorded STV, and no centralized demo archive existed before demos.tf. Recovering anything from that era depends entirely on an individual having personally kept a local `.dem` for 15+ years — plausible but low-probability at any real scale. **Decision: corpus growth is opportunistic and non-blocking, not a gate.** Phase 1 builds and validates against `z1800.dem` now; a community ask (r/tf2, TF2 Discords, ETF2L/teamfortress.tv forums) runs in parallel as a cheap side effort, not a dependency.
