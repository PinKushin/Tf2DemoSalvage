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
