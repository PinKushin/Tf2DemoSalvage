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
rule is [[push-when-the-gate-is-green]]'s — *"a test that passes locally and fails in CI is usually a
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

Related: [[the-game-folder-is-the-users-to-provide]], [[three-test-levels-and-the-third-is-missing]],
[[push-when-the-gate-is-green]].
