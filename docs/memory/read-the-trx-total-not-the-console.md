---
name: read-the-trx-total-not-the-console
description: "How to read a test run honestly — the trx total, not the console; a floor that tracks the suite; a skip is invisible; a wrong invocation exits 0; never edit a running script; build servers outlive the build and accumulate; push when the gate is green but not for its own sake, and read the CI run rather than trusting the tick; a probe belongs outside the suite, not inside it as an [Explicit] test; and a UI suite's timing measures the application, not the harness."
metadata: 
  node_type: memory
  type: project
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-10T22:53:33.953Z
---

**"Passed!" is not the result, and every number beside it can lie a different way.**

## The trx total, not the console

`build/assert-test-count.sh` reads `total=` from the `.trx`; the console prints a smaller number.
**Floors are trx numbers.** Measured: console `Total: 623` vs. trx `total="638"` on the same run — the
gap is `[Explicit]` tests, counted in the trx but not run. Reading the console after adding tests once
looked exactly like a regression when there was none.

## `a-floor-must-track-the-number-it-guards`

**A floor that hasn't been raised is not a guard.** Floors drifted an order of magnitude behind real
suite sizes — a run that reported 50 of 350 Viewer tests satisfied a floor of 34 without complaint.
Run one project at a time (`build/gate.sh`): a solution-wide run writes one `.trx` per project under
the same name, indistinguishable afterward, and runs assemblies concurrently (a leading suspect for
truncation). `--filter` changes which tests EXIST (drops `[Explicit]` the moment any filter is
present) — two invocations that look equivalent can report different totals for unrelated reasons.

## `a-skip-is-not-a-pass-or-a-failure`

**A test whose precondition breaks doesn't fail — it skips, and a skip is invisible.** A corrupted
hardcoded path made a test silently `Assert.Ignore` — a map went unread for an unknown time, with
`Passed!` on the console and the trx total (which counts skips) satisfying the floor.

- A guard clause is a claim — make it checkable (one shared skip helper, not per-file duplicates).
- When a suite's skip count is non-zero, find out which and why.
- Suspect this whenever a test "has always passed" but you can't remember it producing output.

The skip is still correct behaviour (it's what keeps CI green without the game,
[[ci-is-the-machine-without-tf2]]) — what's wrong is a skip nobody accounted for.

## `a-wrong-invocation-exits-zero`

**A command invoked wrongly usually exits 0.** A wrapper script invoked by bare filename printed its
own usage banner and exited 0 for weeks; `dotnet test … | tail` reports the PIPE's exit code, so a
broken build read as green; `--filter` matching nothing exits 0 with no summary. **`run-exclusive.ps1`
does not propagate the inner command's exit code** — a failed UI phase still exits 0; only the
`Passed!`/`Failed!` line inside the output says so, and that line can itself be cut by a `| tail`.

**General shape:** whenever a command's OUTPUT is read rather than its exit code, absence of expected
output IS the failure signal. Assert on shape (a matching total, a required line), not status.

**Corollary for a windowed suite: a human at the keyboard is an input to it** — a UI test timing out
waiting for a frame may simply be the owner hitting a key. Re-run before investigating, say which
you're reporting.

## `never-edit-a-running-script`

**Do not edit a shell script while it's running.** `bash` reads by BYTE OFFSET, so a length-changing
edit shifts everything unread and resumes at the old offset in new bytes — silently skipping
assemblies (exit 0) or running a word fragment as a command (`en: command not found`). **The trigger
is updating a floor comment in the very script that's running** while its wait is used productively.

**Rule is mechanical: while a gate is in flight, the gate script is off-limits, floors included.**
Everything else (docs, source, tests) is fair game.

## `build-servers-outlive-the-build`

Every `dotnet build`/`test` leaves MSBuild node-reuse workers and the Roslyn compiler server running
by design — measured ~1.4GB resident immediately after one gate run finished. Accumulates across
sessions; the owner's periodic "needs a restart" symptom is consistent with this.

**How to apply:** `dotnet build-server shutdown` (the gate runs it via `trap ... EXIT`, covering
failed runs too). Run by hand after ad-hoc `dotnet test` calls. **Shut down rather than disable** node
reuse — it genuinely helps across a gate run's many projects. **Never `pkill -f`** — it matches the
shell running the build script itself.

**One leak of three:** a KILLED gate (a stopped background task, a closed terminal) skips the trap
entirely — the shell dies outright. `build/reap-dotnet.ps1` finds orphans by PARENTAGE (never name),
runs at gate START as well as in its trap, verified with a control (an orphaned process reaped, a live
one untouched).

## `push-when-the-gate-is-green`

Owner: *"we need to push when the gate is green too"* — overrides the "push sparingly" default; a
green gate IS the shareability signal. **Do not gate for its own sake** — a documentation-only change
needs no gate. **Sub-branch pushes are crash insurance** and cost nothing (confirmed: no workflow
triggers on non-main pushes) — except opening a PR, which has no branch filter and flips a branch from
zero-cost to a full CI run on every push.

**A push to main is not finished until its run is READ.** Two merges left the Test job red,
unnoticed for an hour. **A run can be ABSENT** (a GitHub Actions outage produced zero-job runs,
looking identical to "not looked at yet") — verify by SHA (`gh api .../runs?head_sha=$(git rev-parse
main)`), not by reading the top of a run list, and use the FULL sha.

## `a-probe-is-a-script-not-a-test`

Owner: *"you can script a probe outside the test suite, having a bunch of probe tests just slows the
suite down."* A probe compiled into the test assembly is still discovered, compiled, and counted in
the trx total even as `[Explicit]`; asking it a question costs a full VSTest host launch, and `const`
parameters meant editing code just to change an input.

**How to apply:** a probe is a console program discovered by reflection — adding one is adding a file,
run via `dotnet run --project tools/.../Probe`. This doesn't replace a real synthetic test for
anything with a right answer (decode, arithmetic) — those stay in the suite. Don't bulk-port existing
probes without reading them; some carry findings that should become `docs/findings/` entries instead.

## `slow-ui-tests-measure-the-app`

**A UI suite that got slow is telling you the application got slow** — UIA queries are served by the
target's message loop, so a laggy app makes a five-second wait become fifty. Adding a second demo took
a suite from 12s to 4m43; per-test duration logging showed every test AFTER the demo-switch taking
20-50s — pointing straight at the application (frame rate collapsed from 300fps to 19fps, paused).
Read per-test durations before touching the tests; a slowdown is a measurement already paid for.

---

**A filtered gate command can leave you reading a STALE number** — grepping only failure lines from a
fresh run alongside a leftover file from a PREVIOUS run showed a floor-matching result that was
actually one run behind. Redirect to a file THIS invocation names, read only that file.

**A gate in flight owns the tree, and it still exits 0 on a mid-run edit** — a source edit landing
between two projects' test runs means each measured a DIFFERENT tree, and the run reports success
because nothing detects the split. **While a gate is in flight, do documentation/reading/planning,
never a source edit.** A probe run (which also builds) has the same hazard — two false "findings"
were actually build error text matched by the grep, because source was edited mid-scan.

**A commit nobody ran may be the editor's own button** — before auditing hooks or suspecting a peer
session for an unexplained commit, ask the owner (a UI button clicked in an editor was the actual
cause once).

**The viewer suite wants the GPU** — creates real Direct3D devices; a `Test Run Aborted` mid-suite
once coincided with another app in exclusive fullscreen, didn't reproduce after. The count FLOOR is
what made a truncated run visible at all.

**Console vs. trx gap is not a constant** — grows with the number of skipped/Explicit tests; on one CI
run it was eleven tests. Never compare a console `Total:` against a gate floor.

**A CI floor can't be checked against a run that uploads nothing** — both artifact-upload steps were
gated on failure only, so no GREEN run ever produced a trx to confirm floors against, and floors
drifted hundreds low for months. Fixed with `if: always()`.

**Raising one file's floor is not raising the other's** — `build/gate.sh` and the CI workflow hold the
same numbers in two files that merge independently and cleanly with no conflict. See
[[one-place-or-it-drifts]].

**Verify a floor by manipulation** — the floor PLUS ONE must fail, or the check measures nothing.

**A `find . -name X.trx` in a tree with multiple worktrees can silently read a stranger's file** — two
spun-off tasks each left their own trx under `.claude/worktrees/`, and `head -1` picked one at random.
Fixed: worktrees pruned from search, and multiple matches now REFUSE and name the files rather than
guessing. This exact hazard was written down as a known trap once already and re-occurred — see
[[filing-a-divergence-is-not-fixing-it]]; writing a hazard down doesn't mitigate it by itself.
