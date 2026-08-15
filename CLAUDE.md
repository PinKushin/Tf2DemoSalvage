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

**This section described a one-demo corpus and is now badly out of date in the good direction.** As of 2026-08-10 the committed corpus (**gcor**) is 10 demos / 20.3 MB spanning **five measured protocols** — 11, 14, 15, 16 and 24 — each recorded on a period client whose `version` output dates it exactly. Most eras carry a POV and a SourceTV recording of the same session, which is the pairing that has caught two writer-side findings. Metadata in `tools/corpus/manifest.json`, era table in `docs/TIMELINE.md`.

`z1800.dem` is still there and is still the founding specimen, but the guess about it in the original text was wrong twice over: it is **2020 or later, not ~2015** (protocol numbers date nothing — see `docs/memory/z1800-is-modern-not-2015.md`), and it decodes end to end rather than being a target.

Two corpora, and the distinction matters when you are told to add a demo:

- **gcor** — `tools/corpus/demos/`, committed, one specimen per era × point of view. It grows **only for a new generation**, because GitHub's free Git LFS tier is 1 GiB/month and every CI job pays for it. Era specimens are kept to 2–4 minutes deliberately (`manifest.json` notes).
- **lcor** — `tools/corpus/local/`, git-ignored, currently 14 demos / 774 MB. Modern matches, extra specimens, anything for volume. Tests pick it up automatically, so a local run is a superset of CI. **"Add these demos" means lcor unless the demo is a new protocol.**

**`TF2DEMOSALVAGE_GCOR_ONLY=1` runs against gcor alone, and it is what you want most of the time.**
The corpus suite over lcor takes about **30 minutes**; over gcor it takes **28 seconds**, because
lcor is 774 MB of modern matches against 20 MB of short era specimens. Use it for any run whose
purpose is "did I break something" — the merge gate, a quick check after an edit — and run the full
superset when the change touches decoding itself.

```bash
TF2DEMOSALVAGE_GCOR_ONLY=1 dotnet test Tf2DemoSalvage.slnx
```

**A test that needs a specific demo asks for it with `Corpus.Demo("name")`**, which skips with a
reason when the file is absent rather than throwing out of `First`. That distinction matters here:
the committed era specimens are the owner's own SOLO recordings, so they carry no other players and
no worn items at all — the 2013 badlands POV has 11 props and zero wearables. A cosmetics test
redirected onto one would pass while measuring nothing, which is worse than skipping.

Remaining gaps on the era axis: protocols **12–13** and **17–23**.

Do not assume a broad multi-era test corpus exists or will exist soon. TF2's pre-2013 competitive scene mostly used live Mumble casts rather than recorded demos, and there was no centralized archive before demos.tf, so older specimens are genuinely rare (`docs/DECISIONS.md` D5). Build defensively (schema-driven, not hardcoded) *because* of this, not despite it. If/when more demos surface (community outreach is a parallel, non-blocking effort), add them to `tools/corpus/manifest.json` and give each one a regression fixture in `tests/`.

## `docs/findings/` is a rolling account — keep it current as you go

`docs/findings/` is the **reverse-engineering history of TF2's demo system**: how each part of the
format was worked out, what was believed first, what turned out to be wrong, and which piece of
evidence settled it. It is written to be read end to end and quoted in a write-up.

**Update it in the same commit as the finding, not at the end of the project.** A finding written
up weeks later loses the thing that makes it worth reading — the wrong turn, the measurement that
killed it, the number that made it obvious.

What belongs there, in rough order of value:

- **Anything learned about Valve's own code and engine behaviour**, not just about the wire format.
  Vestigial fields, placeholder bytes, dead guards, clamps, how `bf_write` behaves under pressure.
  This is the part with the least prior art and the most interest.
- **Dates and history.** Valve publishes *what* changed between protocols and never *when*. The
  protocol-to-build-date table is original research and every new specimen extends it.
- **Wrong conclusions and what killed them.** Kept deliberately — a conclusion recorded without the
  reasoning that failed is the kind that gets confidently repeated.

Keep the division of labour clean, because duplicated prose goes stale:

| Document | Answers |
|---|---|
| `docs/SPEC.md` | what the format **is** |
| `docs/findings/` | **how we know**, and what we got wrong |
| `docs/RISKS.md` | what is still **open**, numbered |
| `docs/DECISIONS.md` | **why the project** is built this way |
| `docs/TIMELINE.md` | the **era axis** |

Mark each claim with its evidence class — read from published source, measured on the corpus,
arithmetic, differential, or interpolated. They are not equal, and the difference has repeatedly
decided arguments. Flag interpolations every time.

## AI memory is mirrored into this repo

`docs/memory/` holds the assistant's working memory, committed so it survives a machine
wipe or a move to another computer. **Write every memory change to both places** — the
assistant's own memory directory *and* `docs/memory/`. Updating only one silently diverges
the local copy or leaves the backup restoring something stale.

**No personal or identifying information goes in `docs/memory/`** — this repo is meant to be
public. Personal preferences belong in the assistant's global memory (`~/.claude/memory/`).
The test: would it help on a different project? Then it is global.

Read `docs/memory/MEMORY.md` at session start alongside this file. Several entries record
corrections to earlier wrong conclusions; those are deliberate, because a memory that keeps
only the conclusion is the kind that gets confidently repeated.

## Where to start

Phase 1 (see `ROADMAP.md` §3): `managed/Tf2DemoSalvage.Core`, pure C# — container parsing, then `dem_datatables`/`dem_stringtables`, then generic SendTable-driven entity delta decode, emitting a normalized event stream. Validate against `z1800.dem` end to end once the primitives are unit-tested individually. Output target: a Quake-style readable trace — the demo decompiled to text, message by message, in stream order — plus a summary dump and JSON Lines. **No SQLite**: removed 2026-08-10, see `docs/DECISIONS.md` D17. Do not create anything under `native/libtf2dem` for this phase.

Do not start Phase 2 or Phase 3 work before Phase 1 is solid and tested. Do not build toward Phase 4 (demo repair for live-client replay) at all unless explicitly asked — it's parked, see `docs/DECISIONS.md` D1.

## Reference material (external, not vendored)

- [demostf/parser](https://github.com/demostf/parser) / `tf-demo-parser` crate — mature Rust reference implementation (demos.tf's actual parser). Read for cross-checking behavior, do not port code directly (different language, and the point is to actually understand the format).
- [demboyz DemFormat.md](https://git.botox.bz/CSSZombieEscape/demboyz/src/commit/3858162c9c0fb0988e30f61de526ebfe85eb1e2f/docs/DemFormat.md) — container format writeup, as documented for the TF2 build active July 2015.
- Valve Developer Community wiki: Networking Entities, Networking Events & Messages.
