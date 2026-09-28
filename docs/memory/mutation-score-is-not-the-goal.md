---
name: mutation-score-is-not-the-goal
description: "Everything about running Stryker here — the baseline is a ratchet not a gate, the config fails silently in two ways, and a quarter of the code is invisible to it."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-09T03:54:23.707Z
---

## The number: a ratchet, not a gate, and not 80

**The `tf2-core` mutation baseline is 52.16%** (measured 2026-08-13, sha `0eb27f9`: 1,574 killed, 405
survived, 11 timeout, 1,049 no coverage). Don't quote 80 as a floor here — that came from general
guidance and was over-applied; no baseline existed before this measurement. (80 remains correct for
`Cli.Tests`, which covers all of what it mutates.)

**It's a ratchet in intent, not a gate** — the score dropping is expected as new code lands between
runs; what's non-optional is noticing and fixing it, not letting it drift. **The baseline itself is
provisional** — a large feature landing can drop it a long way, in which case it's RE-SET, not
defended. What's ratcheted is attention, not a figure.

**The 1,049 uncovered mutants are the interesting number, not the 52%** — no coverage means no test
reaches the code at all, different from a test failing to notice a change ([[most-of-a-decoder-is-untested]]).

## Two rules that matter more than the number

- **Don't trace dead ends** — an equivalent survivor is done the moment it's established.
- **Don't write tests for tests' sake** — a test written to kill a mutant rather than pin real
  behaviour is a change-detector, breaking on every refactor.

## Expect a lower score here than normal

- Much of the code is renderers, killable only by change-detector tests this project won't write.
- A large part is exercised only by real demos, which live in the un-mutatable corpus project (B34)
  (1,136 mutants show as NoCoverage for this reason alone).
- Safe mode removes whole methods when one mutant fails to compile (1,827 CompileError mutants
  concentrated in renderer files on one run).

**Read survivors by FILE, never the percentage** — 149 of 242 survivors sat in three files on one
run; the aggregate percentage hides that entirely (RISKS.md B35).

## The gate cannot see a quarter of the code

**444 mutants removed before testing** — safe mode drops mutations it can't compile around this
codebase's `ref struct BitReader` parameters, concentrated in the decode core. What actually covers
decode paths is the corpus differential in `tools/differential/`. See [[fixtures-are-the-weak-point]].

## Cadence: once a day at most, never per change, never on a disturbed tree

Three full runs in one evening cost 2.5 hours for nothing — use `dotnet stryker --since:main` (D13)
while iterating; full runs are daily or before a milestone. **The gate only means something on an
undisturbed tree** — editing files underneath Stryker mid-run produced wildly different scores
(98%→93%→83%→100%) on the SAME code; only the first and last runs (undisturbed) meant anything.

## A truncated run reports a plausible score

A run that stopped early (accounted 1,215 of 1,954 mutants) still printed "All mutants have been
tested" at 37.74% — internally consistent with the subset it held, reported as real and built on for
three conclusions. **Compare the accounted mutant total against a known-good run before believing a
score** — same rule as reading a runner's `Total:` instead of `Passed!`.

## `stryker-targetframework-must-be-in-csproj`

`TargetFramework` must be in each `.csproj`, never `Directory.Build.props`, or `dotnet stryker` aborts
with "Failed to analyze project builds" and no MSBuild error naming the cause. Verified across
Stryker 4.16.0 / net9.0 / net10.0 — not a .NET 10 gap. If a new project breaks analysis, check the TFM
first.

## `stryker-globs-are-project-relative`

Stryker resolves path globs relative to EACH PROJECT'S own directory, not the solution root — a glob
prefixed with the project path matches nothing, silently (no error, a score is still reported).
`--since` targets must be branch names or full SHAs, not `HEAD~3` (which fails after 15 minutes of
work, at report generation).

## Where runs land

`~/measurements/<stamp>-<sha>-tf2-core/` on mutation-box, scheduled daily. Neighbouring `stryker-core`
runs belong to another project — do not read their score as ours; prune only by `.owner` marker.

**After the D25 split**, `Core.Tests` and `Corpus.Tests` both mutate the same csproj, so corpus-only
code shows NoCoverage in the core run and vice versa — a raw floor set pre-split is not achievable by
writing tests; score the NoCoverage subset out separately.

See [[tests-before-codecs]] — writing tests after the code is what produces survivors.
