---
name: fuzzing-belongs-here
description: "Why this project is a stronger fuzzing candidate than most, what to target in order, and the setup traps — proposed, not implemented"
metadata: 
  node_type: memory
  type: project
  originSessionId: 9b3a8b35-1dc8-47b0-a320-73b01288f10c
  modified: 2026-08-07T22:34:40.561Z
---

**Status: proposed, nothing wired up.** Written 2026-08-07 from outside the
implementation effort, so no code was touched. Full write-up in
`docs/FUZZING.md`. It should become **D8** in `docs/DECISIONS.md` when someone
acts on it — deliberately left unnumbered to avoid colliding with a decision
being written at the same time.

## Why this project, specifically

**The parser is hand-written at the bit level.** `BitReader` is a `ref struct`
over `ReadOnlySpan<byte>`, least-significant-bit-first, and every bounds check is
original code. Nothing like `Utf8JsonReader` sits underneath having already
absorbed a decade of adversarial input. In C# that makes it a *liveness* problem
rather than memory safety: `IndexOutOfRangeException` where
`EndOfStreamException` was intended, a length field driving an unbounded
allocation, a decode loop that never terminates.

**It directly addresses D5, which is the project's biggest stated risk.** D5
concluded the corpus is one demo (`z1800.dem`) and that outreach for pre-2010
specimens is low-probability. A fuzzer cannot manufacture a 2009 demo, but it
manufactures hundreds of thousands of *malformed* ones from the file already in
hand — exactly the inputs a sparse corpus never supplies.

**Do not oversell it.** Fuzzing proves the parser did not fall over. It proves
nothing about whether bits were decoded *correctly*. It does not reduce the need
for real demos; it covers a different axis.

## Where it sits next to D6

D6 already mandates Stryker everywhere, so the appetite exists. Three questions,
no substitutions: unit tests ask "right thing on input we thought of", mutation
testing asks "would the tests notice if the code were wrong", fuzzing asks
"**what happens on input nobody thought of**".

## Target order

1. **`BitReader`** — nearly free. It takes a `ReadOnlySpan<byte>`, which is
   exactly what libFuzzer hands a target: ~5-line harness, no plumbing. Do it as
   soon as that class has unit tests; it needs nothing else settled.
2. Varint reader — length-prefix decoders are where unbounded allocation lives.
3. String-table and SendTable delta decode — more state, malformed schema.
4. Whole-file parse seeded with `z1800.dem` — last, hardest to localise.

**The property:** a parse either succeeds or throws an exception documented as
meaning "input was not valid" (`EndOfStreamException`, `ArgumentException`).
Never anything else, never by hanging.

## Setup traps — cost an afternoon in TcgDex.CSharpSdk

That repo's weekly run does ~1.82M executions in 180 s across seven modes (verified
2026-08-07 against its `docs/measuring.md`, which also has the working configuration to
copy). An earlier "4.4M in 300 s" figure was from the older single-mode harness — do not
reintroduce it.

- Toolchain is **Linux-first**; on Windows that means WSL. `clang` is the only
  step needing root. The owner has a working local WSL setup (Ubuntu 26.04,
  `~/.dotnet`, `sharpfuzz` installed, `~/libfuzzer-dotnet` built) — `dotnet` is
  **not** on the login PATH there.
- **`sharpfuzz` instrumentation is per build.** A fresh `dotnet publish` silently
  un-instruments the assembly and the fuzzer then runs at full speed finding
  nothing. Re-instrument after every publish. Most likely trap to hit.
- **Work out of `~`, never `/tmp`** — WSL discards `/tmp` when it idles out,
  taking the corpus with it.
- **`apt-get update` first.** A stale index names a candidate version it cannot
  fetch, which reads as a broken mirror and is not one.
- **Whenever a harness dispatches on part of its input, measure what the seeds
  dispatch to.** `BitReaderFuzzTarget` picks each field width from the buffer's
  own bytes; random input reaches all 32 widths, real seed data will cluster and
  silently stop exercising the rest. `SeededCorpus_ReachesEveryFieldWidth` exists
  to fail when `z1800.dem` is introduced as a seed.
- **Export `DOTNET_ROOT`** when .NET came from `dotnet-install.sh`, or
  `sharpfuzz` reports "Download the .NET runtime" — its apphost only looks for a
  system install.
- `sharpfuzz` **rewrites the assembly in place**; successful instrumentation is
  visible as file growth. No growth means the fuzzer runs happily and explores
  blind.
- **libFuzzer's `cov:` is near-meaningless here** — it counts edges in the tiny
  native shim. The .NET signal is `ft:`, and the real proof instrumentation is
  live is that the corpus *grows*.
- **A green fuzz run only means "no crash inside the budget."** A run that
  executed nothing looks identical — check execution count and corpus growth.
- **Cache the corpus, never commit it.** `actions/cache` + libFuzzer `-merge=1`
  keeps it cumulative and small; committing puts megabytes of binary churn in
  the repo for a file nobody reads.
- Build `libfuzzer-dotnet` from source, matching the supply-chain posture D6
  already takes for analyzers and dependencies.

See [[tf2demosalvage-build-gates]] and
[[mutation-score-is-not-the-goal]].
