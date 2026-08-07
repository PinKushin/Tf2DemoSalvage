# Fuzzing

> **Status: accepted as D8 (2026-08-07). Partially implemented.**
> The `BitReader` target and the deterministic mutation layer exist, and the
> coverage-guided path has been run locally under WSL (see "Running it locally"
> below). What does not exist is the *scheduled* workflow, because the repo has
> no CI yet. See `DECISIONS.md` D8 for the decision record — this document is
> the reasoning and the setup detail behind it.

## Why this project needs it more than most

Two things about this codebase make fuzzing unusually valuable, and they
compound.

**The parser is hand-written at the bit level.** `BitReader` is a `ref struct`
consuming a `ReadOnlySpan<byte>` least-significant-bit-first, and every bounds
check in it is one somebody wrote. Compare that to a JSON parser, where
`Utf8JsonReader` has already absorbed a decade of adversarial input on your
behalf. Here there is no such layer: `ReadUInt32(int bitCount)`, the varint
reader, the string-table decode and the SendTable delta decode are all original
code standing directly between a file and the rest of the program.

C# makes this a liveness problem rather than a memory-safety one, which is
better but not harmless. Hostile or merely corrupt input can still produce an
`IndexOutOfRangeException` where an `EndOfStreamException` was intended, a
length field that drives an unbounded allocation, or a decode loop that does not
terminate.

**The corpus is one demo, and D5 says that probably will not change.** That is
the sharper reason. D5 concluded that community outreach for pre-2010 specimens
is low-probability at scale, and that the schema-driven design is the hedge. A
fuzzer is the other hedge: it cannot manufacture a 2009 demo, but it can
manufacture *hundreds of thousands of malformed ones* from the demo you already
have, and those are exactly the inputs a sparse corpus never supplies.

Be precise about what that does and does not buy:

| | Real demos | Fuzzing |
|---|---|---|
| Does the parser decode *correctly*? | **Yes** | No |
| Does the parser survive input it did not expect? | Barely — one file | **Yes** |

A fuzzed input proves the parser did not fall over. It proves nothing about
whether the bits were interpreted right. **Fuzzing does not reduce the need for
more demos**; it covers a different axis that more demos would cover only
slowly.

## Where it sits next to D6

D6 already mandates Stryker on every C# project. The three tools answer
different questions and none substitutes for another:

| | Question |
|---|---|
| Unit tests | Does it do the right thing on input we thought of? |
| Mutation testing | Would the tests notice if the code were wrong? |
| **Fuzzing** | **What happens on input nobody thought of?** |

## What to fuzz, in order

1. **`BitReader`** — first, and it is nearly free. It takes a
   `ReadOnlySpan<byte>` and libFuzzer hands a target a `ReadOnlySpan<byte>`, so
   the harness is about five lines with no plumbing at all. Drive
   `ReadUInt32` with varying bit counts until the buffer is exhausted.
2. **The varint reader** — same shape, and length-prefix decoders are where
   unbounded allocations come from.
3. **String-table decode** and **SendTable delta decode** — larger surface,
   more state, and the place a malformed schema could send the decoder
   somewhere strange. Worth doing once the primitives are clean.
4. **Whole-file parse**, seeded with `z1800.dem`. Last, because a crash here is
   harder to localise than one in a primitive.

## The property to assert

One rule, and it is the same shape at every level:

> A parse either succeeds, or fails with an exception this project has
> documented as meaning "that input was not valid". Never anything else, and
> never by hanging.

`EndOfStreamException` and `ArgumentException` are already used that way in
`BitReader`, and `VarInt` adds `InvalidDataException` for an encoding that is
structurally impossible — a varint asking for more bytes than its type can hold.
An `IndexOutOfRangeException`, a `NullReferenceException`, an
`OutOfMemoryException` or a non-terminating loop are all defects, because a
caller cannot reasonably defend against them when the input came from a file
someone downloaded.

The bound on varint length is *part of the property*, not an implementation
detail. An unbounded decoder handed `FF FF FF FF …` does not crash — it reads on,
which is worse, because a hang looks like slowness rather than a bug.

## Two layers, because they cost different amounts

**Every push — cheap and deterministic.** Mutate `z1800.dem` and hand-built
fixtures mechanically: truncate at intervals, flip single bits, inject
structural bytes, and assert the property above. Seed the RNG so a failure names
a reproducible case rather than being a one-off nobody can re-run. This runs in
the normal suite in milliseconds and catches the obvious regressions.

**Weekly — coverage-guided.** SharpFuzz instruments the assembly and libFuzzer
explores. This is where inputs nobody would think to write actually come from.

## Setup notes worth having in advance

These were learned setting the same thing up in `TcgDex.CSharpSdk`, where the
weekly run does ~1.82M executions in 180 seconds across seven modes
(`docs/measuring.md` there has the current figures). They cost an afternoon to
find:

- **The toolchain is Linux-first.** On Windows this means WSL, and `clang` is
  the only step needing root — everything else is per-user.
- **Run `apt-get update` before installing anything.** A stale index reports a
  candidate version that then cannot be fetched. It reads as a broken mirror and
  is not one.
- **`sharpfuzz` instrumentation is per build, and a fresh `dotnet publish`
  silently undoes it.** The fuzzer then runs at full speed finding nothing,
  which looks identical to a clean run. Instrument *after* every publish, and
  confirm by file size (see below). This is the trap most likely to bite.
- **Work out of `~`, not `/tmp`.** WSL discards `/tmp` when it shuts down on
  idle, which takes the corpus with it.
- **Export `DOTNET_ROOT`** if .NET came from `dotnet-install.sh` rather than a
  package. Otherwise `sharpfuzz` fails with "Download the .NET runtime",
  because its apphost looks for a system install and does not find `~/.dotnet`.
- **`sharpfuzz` rewrites the assembly in place**, so successful instrumentation
  is visible as file growth. If the DLL does not grow, the fuzzer will still run
  happily and find nothing, because it is exploring blind.
- **Read libFuzzer's `cov:` as almost meaningless here.** It counts edges in the
  tiny native shim. The .NET signal is `ft:` (features), and the real proof that
  instrumentation is live is that the corpus *grows*.
- **A green fuzz run only means "no crash inside the budget."** A run that
  executed nothing looks identical. Check the execution count and the corpus
  growth, or the job is decorative.
- **Cache the corpus, do not commit it.** It is the fuzzer's memory — an input
  is kept only because it reached code no earlier input reached — so starting
  cold each week means spending the budget rediscovering. `actions/cache` plus
  libFuzzer's `-merge=1` keeps it small and cumulative without putting a
  megabyte of binary churn in the repository.
- **Build `libfuzzer-dotnet` from source** rather than pulling a prebuilt
  binary, which matches the supply-chain posture the analyzer and dependency
  gates in D6 already take.

## The seeding trap, which this harness is exposed to

Borrowed from the same effort in `TcgDex.CSharpSdk`, where it cost real fuzzing
budget before anyone noticed:

> Whenever a harness dispatches on part of its input, check what the seeds
> actually dispatch *to*.

Our `BitReader` target does exactly that — it picks each field width from the
buffer's own bytes (`data[cursor] % 32 + 1`). With uniformly random input every
width appears. With **real** seed data it will not: real bytes are not uniformly
distributed, which is precisely what makes them good seeds, and they will
cluster on a narrow set of widths. Nothing about that is visible from the
outside — the run looks healthy and simply never exercises the other paths.

So when `z1800.dem` is added as a seed (target #4), *compute* the width
distribution rather than assuming it. `BitReaderFuzzPropertyTests` already
asserts the seeded corpus reaches every width from 1 to 32; that assertion is
there to fail loudly when real-world seeds are introduced, not because random
bytes were ever in doubt.

## Running it locally

Verified end to end on Ubuntu 26.04 under WSL2 on 2026-08-07, not merely
described. `dotnet` lives in `~/.dotnet` and is **not** on the login PATH, so
every step needs the export:

```bash
export DOTNET_ROOT=$HOME/.dotnet
export PATH=$DOTNET_ROOT:$HOME/.dotnet/tools:$PATH

cd /mnt/c/Users/pinku/source/repos/PinKushin/Tf2DemoSalvage
dotnet publish tests/Tf2DemoSalvage.Fuzz -c Release -o ~/tf2fuzz/publish

# Instrument the code under test - not the harness assembly.
# Re-run this after EVERY publish; publishing silently un-instruments.
sharpfuzz ~/tf2fuzz/publish/Tf2DemoSalvage.Core.dll

mkdir -p ~/tf2fuzz/corpus
~/libfuzzer-dotnet \
  --target_path=$DOTNET_ROOT/dotnet \
  --target_arg=$HOME/tf2fuzz/publish/Tf2DemoSalvage.Fuzz.dll \
  ~/tf2fuzz/corpus -max_total_time=60 -print_final_stats=1
```

Instrumentation is confirmed by file growth: `Tf2DemoSalvage.Core.dll` went
6,144 → 6,656 bytes. If that number does not change, everything below is
meaningless.

First run, 60 seconds, empty starting corpus:

| | |
|---|---|
| Executions | 805,921 at ~13,200/s |
| Features (`ft:`) | 110 |
| Corpus | 13 entries, largest 45 bytes |
| Crashes / timeouts | none |

**The interesting result is not the clean run — it is that `ft:` stopped moving
after roughly 15,000 executions.** The remaining ~790,000 found nothing new.
That is the correct outcome for a target this small: `BitReader` has very few
distinct paths, and the fuzzer exhausts them almost immediately. Two things
follow:

- A long run against `BitReader` alone is a waste of budget. Seconds, not
  minutes, is the honest setting for this target today.
- The case for a *scheduled* long-running job only materialises with targets #3
  and #4 (string-table and SendTable delta decode, then whole-file parse), where
  the state space is genuinely large. Wiring the workflow up now would produce a
  green badge that proves nothing, which is the exact failure mode the setup
  notes warn about.

## What this would cost

A fuzz target project, a workflow, and a deterministic mutation suite in
`tests/`. Roughly the size of one focused session. The `BitReader` target alone
is small enough to be worth doing the moment that class has its unit tests, and
it does not need the rest of the plan to be settled first.
