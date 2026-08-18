# Measurement plan — making mutation and fuzzing cover the whole codebase

Working plan, opened 2026-08-18. **The goal in one line: replace data-dependent tests with
synthetic ones, so every part of this codebase can be mutation-tested on the box instead of only
the half that happens to run without a 20 MB demo or an 8.2 GB game install.**

Status lives in the session task list; this file holds the reasoning, the measurements, and the
decisions, so a later session does not re-derive them.

## Where it stands (measured 2026-08-18)

`mutation-box` runs exactly one mode for this repo: `core`, nightly at 09:00. Everything else is
unmeasured. Mapped by the PokemonBattleJournal agent in `MEASUREMENT-BOX-LOG.md` (2026-08-18 15:17):

| Production project | Prod LOC | Tests | Mutation mode | Runs? |
|---|---:|---:|---|---|
| Core | 20,205 | 1,073 | `core` | nightly 09:00 |
| **Content** | **12,283** | **454** | none | **never** |
| Viewer3D | 16,869 | 353 | none | never |
| Cli | 682 | 63 | `cli` exists | never measured |
| Audio | 948 | 16 | none | never |

And within `core` itself, **1,166 mutants are NoCoverage across 40 files** — about 70 % of them
(818) in the write/encode/trace half: `*Assembly`, `*Writer`, `*Encoder`, `DemoTraceWriter`,
`DemoTextDumper`. The corpus tests *parse* demos; they never exercise the paths that *produce*
output, so a whole direction of the codebase is untested by demos on principle.

## The one root cause, and the one fix

Every gap above has the same shape: **the tests that would cover it depend on data the box does not
have.**

- Corpus tests need 20 MB demos (Git LFS, and slow — the instrumented suite takes 22+ minutes for a
  single coverage pass, so mutating it is hours).
- Content tests need the TF2 install — measured: the `_dir.vpk` indices are only 7 MB but they point
  into **8.2 GB** of `tf2_misc_*` / `tf2_textures_*` archive parts.
- Audio tests need native voice codecs; `celt.dll` and `speex.dll` are MSVC-built Windows DLLs
  (`tools/native-audio/build.ps1`), so two of the three decoders cannot even load on Linux ARM.

So the fix is one thing done in several places: **synthetic tests over small fixtures, built by the
project's own encoders wherever an encoder exists.** This is not a workaround for the box — it is
the better harness anyway, for reasons the repo already records:

- A corpus test can only exercise the paths its ten demos happen to take
  (`docs/memory/tests-before-codecs.md`), so it is a poor mutation harness at any runtime.
- Real data hides bugs; small inputs expose them
  (`docs/memory/real-data-hides-bugs-small-inputs-expose.md`).
- Hand-written fixtures cause more bugs than the decoders do, so **prefer round-trip properties
  where an encoder exists** (`docs/memory/fixtures-are-the-weak-point.md`,
  `differential-beats-fixtures.md`). Write bits with `BitWriter`, read them back, compare — no
  fragile hand-built object graphs.

**Nothing is mutation-tested locally.** Owner, 2026-08-18: local runs take hours and the machine is
in use at most times of day. Everything measured runs on the box; this machine builds and runs
ordinary tests only.

## Decisions taken (owner, 2026-08-18)

| Question | Decision | Rejected, and why |
|---|---|---|
| Content (12.3 k LOC, 454 tests, needs 8.2 GB of VPKs) | **Synthetic tests, box-native.** Fast unit tests over small bundled fixtures for the pure parsers; game-gated tests stay as local integration tests. | Copying 8.2 GB to the box (16 % of a 50 GB boot volume, and slow per mutant); mutating locally (hours, machine in use). |
| Audio (16 tests; celt/speex are Windows-only) | **Cross-build `celt.so` and `speex.so` for linux-arm64**, so all three decoders mutate on the box. Plus more tests — 16 for three decoders is thin regardless. | Opus-only on the box (leaves two decoders unmeasured); mutating locally. |
| Corpus tests | **Make them synthetic too.** | Leaving them as the only coverage of the paths they touch. |

## The work, in order of value

1. **Synthetic tests for the write/encode half of Core.** Highest value and *needs no box changes at
   all* — the box already mutates `core` nightly, so these raise the existing score the next
   morning. The 40-file NoCoverage list is in `MEASUREMENT-BOX-LOG.md`; the top four have no
   dedicated `Core.Tests` file today: `MessageAssembly` (122 NoCov), `StringTableAssembly` (111),
   `EventAssembly` (103), `NetMessageWriter` (83). Round-trip is the technique: build valid bits,
   read to a message, write it back, assert the bits are identical.
   **Caveat on the list: it is a superset.** It contains every reachable target but also some
   genuinely dead or equivalent-by-construction paths. Triage is ours — PBJ cannot tell a dead
   branch from a demo-only one from the box.
2. **Content synthetic tests + a `content` mode.** New `stryker-config.json` and a `content` case in
   `build/run-measurements.sh` with `NEEDS_CORPUS=0`. Then ask for a slot.
3. **Cross-build celt/speex + an `audio` mode**, and more voice-decoder tests.
4. **More fuzz targets.** Today only four exist — `bitreader`, `varint`, `container`, `snappy`. The
   untrusted-input surface is much wider: entity decode, string tables, the net message reader, temp
   entities, and the three voice decoders, which parse untrusted network audio and are the strongest
   remaining candidates.
5. **Viewer3D last, and maybe never.** 16.8 k LOC but mostly rendering, which mutation-tests as
   poorly as UI does. Split the non-rendering logic out first, or skip it.

## Booking a slot — the protocol

`mutation-box` crontab as of 2026-08-18 (this is the truth; `crontab -l` outranks any document):

```
07:00, 19:00 daily  pbj stryker-core
08:15 Sunday        pbj stryker-scraper
09:00 daily         tf2 core          <- ours, the only one
11:00, 23:00 daily  tcgdex stryker
```

Free: **13:00 and mid-afternoon.** The PokemonBattleJournal agent owns all crontab edits on both
boxes — do not edit a crontab directly. Ask in `MEASUREMENT-BOX-LOG.md` (newest entry at top, read a
real clock for the timestamp) with: which box, which command, **measured runtime on the box**, and
how often. Measure before asking; the lock REFUSES rather than queues, so a job that overruns its
slot silently skips someone else's run.

## Traps already paid for — do not rediscover these

- **`--concurrency $(nproc)`.** Stryker defaults to half the logical processors, which is 1 on a
  3-core box (integer division). Measured elsewhere: 1 h 35 m to 22 m, 4.2×.
- **The corpus guard's stated reason is stale.** `run-measurements.sh` refuses `corpus` citing
  Stryker's 180 s MTP JSON-RPC limit. That wall is *gone* since the move to NUnit (VSTest has no
  such limit) — PBJ ran coverage capture for 22+ minutes with no RPC failure on 2026-08-18. The
  conclusion still holds for a different reason: the suite is 142 integration tests over real
  demos, so capture alone is 22 minutes and the full run is hours. **Update the message, keep the
  refusal.**
- **Prune run directories by an ownership marker, never a name glob.** `~/measurements/` is shared;
  a glob deletes a neighbour's 18-hour run with no error.
- **Never `rm` a lock file.** `flock` is on the open file description, so unlinking it gives *no*
  lock rather than a stuck one, and two projects then run concurrently with no error.
- **A runner that pulls itself takes effect one run late** — verify a runner change on the second
  invocation, or with `--no-pull`.
- **`-artifact_prefix` writes nothing through `libfuzzer-dotnet`**; crash preservation happens in
  the harness, from an exception *filter*, and the `selftest` target exists to prove that pipeline
  works. Gate any fuzz claim on it.
