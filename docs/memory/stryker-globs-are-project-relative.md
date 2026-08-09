---
name: stryker-globs-are-project-relative
description: Stryker's mutate and ignore-changes-in globs resolve against each project directory, not the solution root, and a non-matching glob fails silently.
metadata:
  type: project
---

Stryker.NET resolves its path globs relative to **each project's own directory**, not the
solution root and not the directory `dotnet stryker` was invoked from. Owner-stated 2026-08-09.
The official configuration docs do not say this — they show `'**/*Services.cs'` and
`MyFolder/MyService.cs{10..100}` without ever naming the reference point.

So in this repo, a `mutate` entry must be written relative to `managed/Tf2DemoSalvage.Core/`:

```jsonc
"mutate": ["Schema/**/*.cs"]              // correct
"mutate": ["managed/Tf2DemoSalvage.Core/Schema/**/*.cs"]   // matches nothing
```

**Why it matters more than an ordinary config mistake:** a glob that matches nothing does not
error. Stryker runs, reports a score, and the number looks fine. This is the same failure shape
as `actions/upload-artifact` finding no files — a green result that means the step did nothing.
A mutation score is only evidence if the mutants were actually placed where you think.

**Open question, not yet checked empirically.** `tests/Tf2DemoSalvage.Core.Tests/stryker-config.json`
sets `since.ignore-changes-in` to `["**/*.md", "**/docs/**", "**/tools/**"]`. If those resolve
per-project, `docs/` and `tools/` are outside the mutated project entirely and can never match,
which would mean documentation-only commits do not get ignored and `--since:main` re-mutates
everything. That costs an hour rather than correctness, and it would look exactly like a normal
run — so confirm it at the next daily mutation run by making a docs-only commit and checking
whether Stryker reports zero changed files.

There is no `mutate` key in that config today, which is why this has not bitten yet.

See [[stryker-targetframework-must-be-in-csproj]] for the other Stryker setting that fails
without naming itself, and [[mutation-score-is-not-the-goal]].
