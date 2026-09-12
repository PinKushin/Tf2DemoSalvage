---
name: ci-is-the-machine-without-tf2
description: CI is the only environment that exercises the no-TF2-install path; local UI tests are structurally blind to it.
metadata:
  type: project
---

**The developer's machine has TF2. The CI runner does not.** That is not an inconvenience to work
around — it makes CI **the only place the no-install path runs at all**, and this project's whole
premise is watching demos on machines that may not have the game.

**Measured 2026-08-26, the expensive way.** A B211 fix meant to improve the no-install experience
broke it: with no TF2, `ReadMapNamed` reported the missing install and **returned**, so the map was
never downloaded and no world was ever built. Every one of the 20 UI tests failed on CI with
`worlds 0, textures 0`. The same suite passed 20/20 locally, three times, because this machine has
the game and never enters that branch.

**The mistake in one line: "no TF2 install" is not "nothing can be done about the map."** The
downloader writes into the viewer's OWN maps folder, which the locator searches, and a map there
draws without the game — models and stock textures are what go missing, not the world. The
requirement was to *mention* the missing install, and mentioning it is all it should do.

**Why this is worth an entry rather than just a fix:** the direction is counter-intuitive. The usual
rule is [[read-the-trx-total-not-the-console]]'s — *"a test that passes locally and fails in CI is usually a
test asserting on the developer's machine"*, and the answer there is to gate the test
(`ViewerSession.RequireTheGame()`). Here it is the **production code** that assumed the developer's
machine, and the answer is the opposite: the CI failure is the correct signal and must not be gated
away.

**How to apply:**

- Touching anything that reads `MapProvider.GameFolder`, `GameContent`, the archives, or the map
  search? The local UI suite cannot tell you whether it works. Read the CI run.
- A change intended to *improve* the missing-install experience is exactly the change local tests
  cannot verify at all.
- Never add a `RequireTheGame()` gate to make a no-install failure go away. That is deleting the only
  instrument for the case the program exists to serve.

Related: [[the-game-folder-is-the-users-to-provide]], [[output-level-assertion-or-it-is-not-done]],
[[read-the-trx-total-not-the-console]].

## `read-ci-before-pushing-onto-it`, and "no TF2" is rarely the real condition (B402, 2026-09-12)

**A test had been failing on every CI run for four runs and nobody looked**, including me, who then
pushed a fix onto the red and reported it green off the local gate. The gate is an instrument that
runs on a machine WITH the game; it cannot fail for this class at all. **Read the annotations before
pushing, not after** — `gh run view <id> --log-failed`, and
`gh api repos/{owner}/{repo}/check-runs/{job}/annotations --jq 'length'` for the count.

**The CI viewer log is downloadable and it is the actual debugger here:**
`gh run download <run-id> --name viewer-logs`. That artifact named the cause when the test could
only say "the viewer did not exit cleanly".

**Then the trap that cost a wrong fix: "CI has no TF2" is a description of the RUNNER, not of the
condition under test.** The crash needed *a map fetch in flight when the window closes*, and a fetch
happens whenever the map is not installed — TF2 or no TF2. This machine has TF2 and dozens of maps
it does not have, so `koth_pro_viaduct_rc4` reproduces the fetch locally in one command. I had
written "the local machine takes no fetch path" into a risk entry as a fact, and on that basis wrote
a fix from a story rather than from evidence, and called it fixed. The next run failed identically.

**"Gate green" is not a reason to push, because the gate does not measure coverage.** Same day,
second instance of the same mistake: B395's new branches in `Core` shipped with no `Core` test, and
Core branch coverage fell 85.2% → 84.7% against a floor of 85. The gate passed every count floor
and cannot see coverage at all; only CI does. **Before pushing code that adds branches to a project
with a coverage floor, ask which instrument would notice — and if the answer is "only CI", that is
a reason to look, not a reason to assume.** The floor was right and the answer was tests, never a
lower floor.

**How to apply: before concluding a failure is CI-only, name the CONDITION and ask whether this
machine can produce it.** Usually it can, with a different input. And when a crash has no name —
`0xC000041D` with empty stderr is an exception inside a native callback — the move is to add the
handler that writes one down (`Application.ThreadException`, `AppDomain.UnhandledException`, and
`TaskScheduler.UnobservedTaskException` for a fire-and-forget `Task`, which reaches neither of the
others), not to guess at the cause. See [[instrument-bugs-outnumber-decoder-bugs]] and
[[logs-are-the-debugger]].
