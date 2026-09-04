---
name: read-the-trx-total-not-the-console
description: How to read a test run honestly — the trx total, not the console; a floor that tracks the suite; a skip is invisible; a wrong invocation exits 0; never edit a running script; build servers outlive the build and accumulate; push when the gate is green but not for its own sake, and read the CI run rather than trusting the tick; a probe belongs outside the suite, not inside it as an [Explicit] test; and a UI suite's timing measures the application, not the harness.
metadata:
  type: project
---

**Three memories were merged into this one on 2026-08-27** — `a-floor-must-track-the-number-it-guards`,
`a-skip-is-not-a-pass-or-a-failure` and `a-wrong-invocation-exits-zero`. They already cross-referenced
each other in every direction, because they are one subject: **"Passed!" is not the result, and every
number beside it can lie in a different way.** **Six more memories were folded in on 2026-09-04** —
about running the gate honestly rather than just reading it honestly: not editing it mid-run, reaping
the daemons it leaves behind, pushing on the right cadence and actually reading the CI run, keeping a
probe out of the suite entirely, and reading a slow UI suite as a measurement of the app. Their names
are kept as headings below.

## The trx total, not the console

**`build/assert-test-count.sh` reads `total=` out of the `.trx`. The console prints a different,
smaller number. Bump floors from the trx.**

Measured on Content.Tests, 2026-08-22, same run:

| source | number |
|---|---|
| console `Total:` | 623 (610 passed + 13 skipped) |
| trx `<Counters total=>` | **638** |
| trx `executed=` | 610 |

The gap is `[Explicit]` tests, which are discovered and counted in the trx total but not run. The
floors in `build/gate.sh` are therefore all trx numbers, and this is why the file's comments can say
things like "613: SoundFormatProbe, `[Explicit]`" — an `[Explicit]` probe raises the floor even
though it never executes.

**Why it is worth knowing:** reading the console after adding 9 tests gave 623 against a floor of
628, which looks exactly like *tests were silently lost* — the precise failure the floors exist to
catch. Several minutes went into hunting a regression that was not there. The real count was 638,
which is 628 + 9 conformance + 1 probe, matching to the unit.

```bash
bash build/assert-test-count.sh "**/content.trx" <old-floor> content
```

It prints `content: 638 executed, 0 failed (floor 628)` — that first number is the new floor. Run it
rather than doing arithmetic on the console line.

---

## `a-floor-must-track-the-number-it-guards`

**A floor that has not been raised is not a guard.** `build/assert-test-count.sh` exists precisely to
catch a truncated run, and its floors had drifted an order of magnitude behind the suites:

| Assembly | Real count | Floor |
|---|---|---|
| Viewer | 352 | **34** |
| Core | 1034 | 744 |
| Corpus | 138 | 99 |

A solution-wide run that reported **50 of Viewer's 350 tests** as a pass (B104) satisfied a floor of
34 without complaint. The check was present, ran, printed a reassuring line, and meant nothing.
Floors must be raised as the suite grows — [[mutation-score-is-not-the-goal]] is the same discipline
applied to a different number.

**Run one project at a time.** A solution-wide `dotnet test` writes one `.trx` per project all under
the same file name, so no count check can tell them apart afterwards; and it runs test assemblies
concurrently, which is the leading suspect for the truncation itself. `build/gate.sh` does the
sequential, count-asserted run — use it rather than reading console lines.

**`--filter` changes which tests EXIST, not merely which of them run.** NUnit's adapter includes
`[Explicit]` tests when no filter is given and drops them as soon as any filter is present.
Content.Tests reported **441 unfiltered and 436** with `--filter 'FullyQualifiedName!~UiTests'` — the
five being diagnostic probes. That filter was the documented merge gate for months, so every
`[Explicit]` test in the repository was quietly absent from it.

Two invocations that look equivalent can therefore report different totals for two unrelated reasons.

---

## `a-skip-is-not-a-pass-or-a-failure`

**A test whose precondition breaks does not fail. It skips — and a skip is invisible.**

Found for real on 2026-08-22. `BspModelsTests` looked up TF2's install path, found nothing because
its hardcoded copy of that path was corrupt ([[edit-files-with-the-file-tools]]), and took its
`Assert.Ignore` branch. The map had gone unread for an unknown length of time. Nothing anywhere
reported it.

**Why it survives every instrument this repo has:**

- The console prints `Passed!` with no mention of it.
- `build/assert-test-count.sh` reads the trx `total`, which **counts skipped tests**, so the floor
  is satisfied.
- Coverage does not move enough to notice.
- The test is still there, still green, still named as if it measures something.

This is the shape of [[measure-the-output-not-the-capability]] one level up: the *fallback branch*
made a dead test look like a healthy one, exactly as a fallback in production makes a dead feature
look implemented.

- **A guard clause is a claim, so make it checkable.** The reason dozens of files could hide this is
  that each stated its own precondition. `GameInstall` plus `Skip` (D52, D109) is one copy, so
  everything that reads game data skips together — loudly and obviously — rather than one file
  skipping alone. See [[output-level-assertion-or-it-is-not-done]] for how long that took to finish.
- **When a suite's skip count is non-zero, find out which tests and why.** 13 skips in Content.Tests
  is normal only once each one has been accounted for. An unexplained skip is a finding.
- **Prefer a helper that returns null-for-absent over a caller-written `File.Exists`.** The check is
  precisely where the silence gets in, so it belongs in one place that is itself tested.
- Suspect this first whenever a test "has always passed" but you cannot remember it ever producing
  output. Check `Skipped:` in the run before assuming the code path is covered.

**The skip is still the right behaviour** — it is the only thing that keeps CI green on the machine
without the game ([[ci-is-the-machine-without-tf2]]). What is wrong is a skip nobody accounted for.

---

## `a-wrong-invocation-exits-zero`

**A command that was invoked wrongly usually exits 0.** Printing a usage banner is not an error to
the program that printed it, so "did it pass" and "did it run at all" collapse into the same answer.

Three measured on this project, all of which cost real time:

- **`pwsh run-exclusive.ps1 dotnet test …`** — the script lives at the PinKushin root, not in the
  repo, so `pwsh` cannot find the bare filename, prints its own usage banner and **exits 0**. It was
  in `CLAUDE.md` in that form for weeks. Correct: `pwsh -File "C:/Users/pinku/source/repos/PinKushin/run-exclusive.ps1" …`
- **`dotnet test … | tail`** — the pipeline's exit code is `tail`'s. A broken build came back exit 0
  with an empty grep and read as green. Redirect to a file, then check `$?`.
- **`dotnet test --filter` matching nothing** — exits 0 with no summary at all, so a renamed fixture
  silently tests nothing.

**The general shape: whenever a command's OUTPUT is being read rather than its exit code, the
absence of expected output is the failure signal, and nothing reports it.** So assert on the shape —
a total that matches a known floor, a line that must appear — rather than on the status.

This is the same family as `--no-build` and as a stale binary: the run succeeds at doing nothing.

---

## `never-edit-a-running-script`

**Do not edit a shell script while it is running.** `bash` does not load the file up front; it reads
and executes by BYTE OFFSET, so an edit that changes the length shifts everything the interpreter
has not reached yet. It resumes at the old offset in the new bytes.

Seen 2026-09-02, backgrounding `build/gate.sh` and editing a floor in it while it ran:

```
core: 1669 executed, 0 failed (floor 1668)
cli: 74 executed, 0 failed (floor 74)

[exited with code 0]
```

**Two of twelve assemblies, and exit code 0.** No error, no truncation warning, nothing that reads
as wrong — the same family as the crashed test host that reports `Passed!` with a short total, and
the `--filter` that matches nothing and exits clean. The gate's own count assertions cannot help:
the ten runs that would have made them fire never executed.

**The habit that causes it is backgrounding a long run and using the wait productively.** That is
usually right; the exception is any file the running command is READING — the script itself, and
anything it sources. Edit docs, edit source the next build will compile, but leave the script alone
until it exits.

**How to notice**: compare the number of assemblies reported against the number the gate runs. A run
that stops early looks exactly like a run that succeeded, and this project already knows that shape
from the entry above on reading the trx total — the rule there, count what came back, do not read the
last line, is the same rule.

### It happened again on 2026-09-04, with this memory already written

Ten of twelve that time — the edit was further down the file — and the tell was a new one worth
recognising:

```
corpus: 156 executed, 0 failed (floor 156)
build/gate.sh: line 1042: en: command not found

[exited with code 0]
```

**`en: command not found`.** The interpreter resumed at the old byte offset, which now landed in the
middle of a word, and ran the tail of it as a command. So the second symptom, after a short list of
assemblies, is a **nonsense command name that is a fragment of a real word** — `en` from `written`,
say. It is one line in a hundred and it is followed by exit 0.

**The trigger both times was the same and it is not carelessness in the moment.** A long gate is
backgrounded, the wait is used productively, and one of the productive things is updating a floor in
the very script that is running — which feels like documentation, because a floor comment IS
documentation. It is not: it is the executing file.

**So the rule is mechanical rather than a matter of care.** While a gate is in flight, `build/gate.sh`
is off limits, floors included. Write the new floor down somewhere else and apply it after the run
exits. Everything else — docs, source, tests — is fair game.

---

## `build-servers-outlive-the-build`

**Every `dotnet build` and `dotnet test` leaves daemons running, by design, and nothing reaps
them.** MSBuild's node reuse keeps worker nodes alive for the next build; the Roslyn compiler server
(`VBCSCompiler`) does the same. Both outlive the process that spawned them.

**Measured 2026-08-25, immediately after one green `build/gate.sh` run, with nothing else
building:**

| process | count | each | total |
|---|---|---|---|
| `dotnet` (MSBuild nodes) | 8 | ~110 MB | ~0.9 GB |
| `VBCSCompiler` | 1 | 502 MB, 547 s CPU | 0.5 GB |

**About 1.4 GB still resident with the gate long finished**, and it does not go away on its own.

**Why it matters more than one run suggests: it accumulates.** Several agents build in this
directory, sessions come and go, and each `dotnet test` adds to the pile. The owner's symptom —
needing a restart every few days — is consistent with this, and it was the reason the cleanup got
looked at at all.

**The honest cost to a RUN is small, and overstating it sends the fix the wrong way.** The viewer
stage measured 2m30s inside the gate against 2m18s standalone: twelve seconds. The reason to clean
up is the memory a machine keeps handing over, not the speed.

**How to apply:**

- `dotnet build-server shutdown` is the reaper. `build/gate.sh` runs it from a `trap ... EXIT`, so a
  gate that FAILS cleans up too — which is the run most likely to be followed by another.
- Run it by hand after a batch of ad-hoc `dotnet test` calls. They leave nodes just as the gate does.
- **Shut down rather than disable.** `MSBUILDDISABLENODEREUSE=1` stops them existing at all, but node
  reuse genuinely helps ACROSS the eleven projects a gate run walks. The defect is persistence, not
  reuse. Set it machine-wide only if the restarts continue after reaping is routine.
- **Never `pkill -f`** for this: it matches the shell running it, and a build script's own command
  line contains every pattern worth matching. That one has already cost an SSH session, exit 255,
  looking exactly like a network drop.
- A symptom worth recognising: `Get-Process dotnet` showing several ~110 MB processes with a start
  time matching a build that finished long ago. They are idle, not stuck.

---

## `push-when-the-gate-is-green`

**Push when the gate is green.** Owner, 2026-08-23: *"we need to push when the gate is green too"*.

**This overrides the global "push sparingly" default**, which says to hold local commits until a
logical unit is finished. Here the gate passing *is* the signal — a green gate means the work is in
a shareable state, so it goes up.

**How to apply:** after `bash build/gate.sh` reports every project at or above its floor, commit and
push. Run the UI suite first when the change could touch the window, since the gate deliberately
excludes it.

**Do not gate for the sake of it.** Same conversation, on running the gate after writing up a
finding: *"you realize if you just found an issue and havent done anything to fix it, you dont have
to run the gate before the commit, nothing has changed"*. The gate answers "did I break the code";
a documentation-only change has not. Batch the gate with the code it guards.

**Sub-branch pushes are crash insurance, and the owner asked for them explicitly** (2026-08-26):

> *"push too, i dont mind pushes on subbranches, expecially when they get over 1k lines, because
> losing this much work due to a crash would suck."*

**The one thing to watch is CI volume**, also owner-stated: *"the only think to watch for, when
pushing, is too many ci's running."* Measured against the workflows rather than guessed:

| workflow | triggers | cost of a sub-branch push |
|---|---|---|
| `test.yml` | `push: [main]`, `pull_request` | **none** |
| `codeql.yml` | `push: [main]`, `pull_request` | **none** |
| `fuzz.yml`, `mutation.yml` | schedule + dispatch | none |

**So a sub-branch push costs nothing at all** — nothing triggers on it. Confirmed 2026-08-26: one
push to `main` produced exactly two runs (Test and CodeQL) and a sub-branch push produced none. That
is what makes "push freely on sub-branches" free rather than merely tolerated.

**The trap is opening a PR.** `pull_request` has **no branch filter**, so a PR flips that branch
from zero cost to a full Test + CodeQL run on *every* push to it — and `test.yml`'s corpus job pulls
Git LFS blobs against a 1 GiB/month tier. Do not open a PR on work in progress unless CI feedback is
actually wanted.

**Pushes to `main` are the expensive ones**, so batch those. `concurrency: cancel-in-progress`
supersedes an in-flight run for the same ref, so rapid pushes do not stack — but each one restarts
the LFS pull, which is the budget that actually binds.

**A push to `main` is not finished until its run is read.** Measured 2026-08-26: two consecutive
merges to `main` left the Test job RED and it went unnoticed for over an hour, because pushing felt
like the end of the task. A green local gate says nothing about CI — that is the entire point of
having CI — and this project's standing rule already goes further than a status tick (*"CI
annotations count as build output"*). Not looking at all is worse than trusting the tick.

**Both failures were things only CI could see, which is why the local gate stayed green:**

- **Floor drift.** `build/gate.sh` and `.github/workflows/test.yml` carry the same numbers in two
  places, and lowering one without the other fails only in CI. It happened twice in one session
  (presentation and viewer). `CLAUDE.md` warns about this in those words, so the fix is mechanical:
  after changing any floor, diff the two lists and confirm they agree.
- **An environment-dependent test.** `WiringUiTests` asserted the viewer's log never says "no player
  appearance" — true on a machine with TF2 installed, false on a runner without it, where the
  appearance legitimately cannot build. **A test that passes locally and fails in CI is usually a
  test asserting on the developer's machine**, and the fix is the assembly's existing
  `ViewerSession.RequireTheGame()` gate rather than weakening the assertion.

**A run can be ABSENT, and absent looks exactly like "not looked at yet".** Measured 2026-08-26: a
merge pushed to `main` at 15:14 UTC produced **no workflow run at all**. Fifteen minutes later
`gh run list --branch main` still showed the previous commit's run at the top — green, and about a
different commit. Reading that list casually says "main is green".

Everything checked out: `origin/main` and `gh api repos/{repo}/commits/main` both held the merge,
all four workflows reported `state=active`, Actions permissions were `enabled`, and the repository
is public so minutes are unlimited. The repository's own event list had no `PushEvent` for
`refs/heads/main` at that time either.

**The cause was a GitHub Actions outage, and checking that should have been the FIRST move, not the
last.** The status page reported a major Actions outage beginning 15:11 UTC — upstream database
failure, inbound traffic throttled. The push was at 15:14. A `workflow_dispatch` sixteen minutes
later was accepted and then sat with **zero jobs** for forty minutes: accepted, never expanded.

```bash
curl -s https://www.githubstatus.com/api/v2/summary.json | jq '.components[] | select(.name=="Actions")'
```

**The order was backwards and it cost half a dozen tool calls.** Workflow states, Actions
permissions, YAML validity, repository visibility, billing — every one came back healthy, which is
what "the problem is not yours" looks like from the inside, one negative result at a time. **When an
external service behaves impossibly, ask the service before auditing your own configuration.** The
tell is a symptom no configuration could produce: a run created with zero jobs is not something a
repository setting can express.

**A run with `total_count: 0` jobs is the distinguishing observation.** A run waiting for a runner
has jobs in `queued`; a run with none was never expanded, which is the provider's side of the line.

**So verify by SHA, never by reading the top of the list:**

```bash
gh api "repos/{owner}/{repo}/actions/runs?head_sha=$(git rev-parse main)" --jq .total_count
```

`0` means no run exists; re-trigger with `gh workflow run <name> --ref main` rather than waiting.
Every workflow that gates a merge should therefore carry `workflow_dispatch`, or there is no way to
ask for the run that did not happen.

**And use the FULL sha.** The first attempt at that query padded a short sha out to forty characters
by hand; it returned `0` for a commit that does not exist, which is the same answer as the real
problem and would have been believed. `$(git rev-parse main)` rather than anything typed.

**How to apply:** after `git push origin main`, run the count query above, then
`gh run list --branch main --limit 3`. If a run is still in flight, come back to it — do not report
the work as landed until the run is read.

---

## `a-probe-is-a-script-not-a-test`

**The owner, 2026-08-30:** *"btw, you can script a probe outside the test suite, having a bunch of
probe tests just slows the suite down and putting in a suite and running the whole damn thing takes
forever"*.

**Why:** two costs, and the second is the one that hides.

- **The suite pays for every probe it holds.** About sixty `*Probe` files sit across the test
  projects. `[Explicit]` keeps a probe out of the RUN and out of nothing else — it is still
  compiled, still discovered by the adapter on every `dotnet test`, and still counted in the
  `.trx` total that `build/assert-test-count.sh` gates on.
- **Asking a probe a question cost a test run.** `dotnet test --filter` builds an assembly
  referencing NUnit, the adapter and Shouldly, starts a VSTest host to execute one case, and buries
  the answer in `TestContext.Out`. Worse, the parameters were `const`, so the owner naming a second
  tick window meant editing a constant and paying it all again.

**How to apply:** a probe is a console program in `tools/Tf2DemoSalvage.Probe`, discovered by
reflection from `IProbe` — adding one is adding a file.

```bash
dotnet run --project tools/Tf2DemoSalvage.Probe
```

Its parameters are command-line arguments, not constants; that is most of the point. `DemoCorpus`
lives in the tool and `Tf2DemoSalvage.Corpus.Tests` consumes it, so the suite and the probes locate
demos through one implementation — see [[one-place-or-it-drifts]].

**This does not replace [[measure-the-output-not-the-capability]] or D38's rule that a measurement
is not a test.** D38 already said a harness worth keeping asserts nothing and is `[Explicit]`; what
it never said was where such a harness should live, so the answer defaulted to "in the suite" and
sixty accumulated there while each one followed the rule. Anything with a right answer — decode,
arithmetic, a rule read from the SDK — is still a synthetic test in `Core.Tests` or a layer's own
suite.

**Do not port sixty probes in one pass.** Several carry findings in their prose and several answer
questions that are now closed; those should be deleted with the finding promoted to
`docs/findings/`. A bulk move relocates prose without reading it, which is worse than leaving it.

D126.

---

## `slow-ui-tests-measure-the-app`

**A UI suite that got slow is telling you the application got slow.** UIA queries are served by the
target's message loop, so a viewer spending 57 ms a frame answers `Find` at 57 ms granularity, and a
five-second wait becomes a fifty-second one.

Measured 2026-08-23: adding a second demo to the UI session took the suite from 12 s to 4m43. Three
explanations were proposed and the first two were wrong —

1. the demo decode (it was 0.4–0.8 s, not the 20 s of asset loading beside it);
2. the log reader re-reading a 45 MB file on every poll (real, worth fixing, not the cause).

**What settled it was `--logger "console;verbosity=normal"`, which prints a duration per test.**
Every test running *before* the demo-switch test took under two seconds; every test *after* it took
23–58 s. That pattern names the cause on its own: nothing about those tests changed, so the thing
they share — the application — did. The render log then gave the number: **300 frames a second
before the switches, 19 after**, paused, with posing and lighting at zero. Filed as B148.

**Read the per-test durations before touching the tests.** A suite that slowed down is a measurement
you already paid for; treating it as test flakiness throws the finding away and usually adds a
timeout to hide it.

See [[nothing-is-closed]] — this is that rule with the hops being phases of a load — and
[[logs-are-the-debugger]], since the first wrong guess picked the one phase that already happened to
be wrapped in a timer.

---

Related: [[measure-the-output-not-the-capability]], [[instrument-bugs-outnumber-decoder-bugs]].
