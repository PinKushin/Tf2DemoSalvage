---
name: ci-is-the-machine-without-tf2
description: CI is the only environment that exercises the no-TF2-install path; local UI tests are structurally blind to it.
metadata:
  type: project
---

**The dev machine has TF2. CI does not** — making CI the only place the no-install path runs at all,
which is this project's whole premise (watching demos on machines that may lack the game).

Measured 2026-08-26: a B211 fix meant to improve the no-install experience broke it — with no TF2,
`ReadMapNamed` reported the missing install and **returned**, so the map was never downloaded, no
world built. All 20 UI tests failed on CI (`worlds 0, textures 0`), passed 20/20 locally three times,
since this machine never enters that branch.

**The mistake:** "no TF2 install" ≠ "nothing can be done about the map" — the downloader writes into
the viewer's own maps folder, and a map there draws without the game (only models/stock textures go
missing). The requirement was to *mention* the missing install, nothing more.

**Counter-intuitive direction:** [[read-the-trx-total-not-the-console]]'s rule ("a test passing
locally and failing CI usually asserts on the developer's machine, so gate it") is inverted here —
the PRODUCTION code assumed the developer's machine, so the CI failure is the correct signal and must
NOT be gated away.

**CI also lacks the owner's saved settings** (`%LOCALAPPDATA%\Tf2DemoSalvage\settings.cfg`). 2026-10-04:
`PlaybackUiTests` red on CI every push from beta.12 to beta.17, "0 samples" — the frame-rate log borrowed the
on-screen meter's reading, null while `cl_showfps` is 0; this machine saves `cl_showfps 2`. Fix: test pins
`+cl_showfps 0`; log owns its meter. A UI test inherits nothing from the runner's profile — pin it on the command line.

**How to apply:** touching `MapProvider.GameFolder`/`GameContent`/archives/map search? Read the CI
run, the local suite can't tell you. Never add a `RequireTheGame()` gate to silence a no-install
failure — that deletes the only instrument for the case the program exists to serve.

Related: [[the-game-folder-is-the-users-to-provide]], [[output-level-assertion-or-it-is-not-done]],
[[read-the-trx-total-not-the-console]].

## `read-ci-before-pushing-onto-it`, and "no TF2" is rarely the real condition (B402, 2026-09-12)

A test had failed on CI for four runs, unnoticed; a fix got pushed onto the red and reported green off
the local gate — which runs on a machine WITH the game and structurally cannot see this class of
failure. **Read the annotations before pushing**: `gh run view <id> --log-failed`,
`gh api .../check-runs/{job}/annotations --jq 'length'`. The CI viewer log artifact
(`gh run download <run-id> --name viewer-logs`) named the actual cause.

**Trap that cost a wrong fix:** "CI has no TF2" describes the RUNNER, not the condition under test.
The crash needed *a map fetch in flight when the window closes* — happens whenever the map isn't
installed, TF2 or no TF2. A risk entry had wrongly stated "the local machine takes no fetch path" as
fact; the fix was written from that story, called fixed, failed identically next run.

**"Gate green" isn't a reason to push — it doesn't measure coverage.** Same day: B395's new branches
shipped with no test, Core branch coverage fell 85.2%→84.7% against an 85% floor; the gate (count floors only)
passed regardless. Before pushing code with new branches under a coverage floor, ask which instrument
would notice — if only CI, that's a reason to look, not assume.

**How to apply:** before concluding CI-only, name the CONDITION and ask whether this machine can
reproduce it with a different input — usually yes. A nameless crash (`0xC000041D`, empty stderr) needs
a handler that names it (`Application.ThreadException`, `AppDomain.UnhandledException`,
`TaskScheduler.UnobservedTaskException`), not a guess. See [[instrument-bugs-outnumber-decoder-bugs]],
[[logs-are-the-debugger]].
