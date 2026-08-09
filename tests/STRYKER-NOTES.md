# Stryker notes

JSON has no comments and **Stryker rejects unknown keys outright** — a `_comment` field in
`stryker-config.json` is not ignored, it aborts the run with a list of allowed keys. That is why
this is a separate file. (Found the hard way: a comment key was added to the core config,
validated as JSON, and committed without running Stryker.)

## Path globs are project-relative

Every path glob in these configs resolves against **the mutated project's own directory** —
`managed/Tf2DemoSalvage.Core` or `managed/Tf2DemoSalvage.Cli` — not the solution root and not
the directory `dotnet stryker` was invoked from.

A glob written from the repo root matches nothing, and Stryker does not warn: it runs and reports
a score for whatever it did find. Same failure shape as `actions/upload-artifact` finding no
files — a clean-looking result for a step that did nothing.

```jsonc
"mutate": ["Schema/**/*.cs"]                              // correct
"mutate": ["managed/Tf2DemoSalvage.Core/Schema/**/*.cs"]  // matches nothing, reports success
```

Neither config sets `mutate` today, so this has not bitten yet.

**Unverified, and worth checking on the next run:** the core config's
`since.ignore-changes-in` lists `**/docs/**` and `**/tools/**`. Those are outside the mutated
project, so if the globs are project-relative they may never match — meaning a documentation-only
commit would not be ignored and `--since:main` would re-mutate everything. That costs runtime
rather than correctness, and looks identical to a normal run. Confirm by making a docs-only
commit and checking whether Stryker reports zero changed files.

## Expect survivors in `Program.cs`

The CLI's `Program.cs` is argument dispatch and file I/O. The behaviour worth pinning was
deliberately extracted into `CommandLine` and `ProgressBar` so it could be tested without running
the tool, so mutants that survive in `Program.cs` are usually mutants of the plumbing that
remains. Read the survivors rather than the score — see `docs/DECISIONS.md` D15.

## `ignore-methods` on the CLI, and why it is set that way

`stryker-config.json` for the CLI sets `"ignore-methods": ["WriteUsage", "WriteLine"]`.

**`WriteUsage` is there for documentation and does nothing** — `ignore-methods` matches method
*calls*, not declarations, so naming the method whose body you want skipped has no effect. It is
kept only so the intent is visible next to the entry that does work.

**`WriteLine` is the one that works, and it is deliberately broad.** Without it, 28 of the CLI's
mutants were help text: `writer.WriteLine("  -t, --trace  ...")` mutated to `""` or deleted.
Killing those means asserting every line of usage prose, which is a change-detector test — it
fails whenever the help is reworded and detects no defect. Score went 73.9% to 97.5% on that
entry alone.

The cost is honest and worth stating: it also removes real `WriteLine` mutants from the
denominator, such as deleting the `wrote N commands` line. That one is still *asserted* by
`ProgramTests`, it simply no longer counts. The trade is 28 worthless assertions against a
handful of real mutants that tests already cover.

## Known equivalent mutant

`ProgressBar._last = string.Empty` survives mutation to any other string. The field is only ever
compared against a rendered bar, and no rendered bar equals a Stryker sentinel, so no test can
distinguish the two. Equivalent by construction — do not write a test for it.

An earlier survivor, `bar.Finish()` in `Program.Run`, was removed rather than tested: the bar is
now scoped in a `using` so disposal provides the same ordering, and the unobservable statement is
gone.


## `test-runner` must be `mtp`, and getting it wrong looks like bad tests

xUnit v3 test projects are self-executing and run on Microsoft.Testing.Platform. Stryker's
default runner is VsTest, and against a v3 project it **runs to completion, reports
`Errors: 0`, and scores almost everything as survived**:

```
VsTest (default)   Killed: 1    Survived: 78   score  1.27 %
mtp                Killed: 76   Survived:  1   score 98.73 %
```

Same code, same tests, same Stryker 4.16.0. The 1.27% is not a measurement — the tests never ran
against the mutants, so every one of them "survived" by default.

**The reason this is worth a section rather than a config line:** the failure presents as a
quality problem, not a tooling problem. A 1.27% score with zero errors reads as "the test suite
is worthless" and invites someone to go write assertions that already exist. Nothing in the
output says the runner failed to invoke anything.

The same shape as the other two traps recorded here — a non-matching `mutate` glob, and a
comment key that aborts the run. Stryker fails quietly in more than one way, so **treat any
sudden score collapse as a tooling question before a test-quality one.**
