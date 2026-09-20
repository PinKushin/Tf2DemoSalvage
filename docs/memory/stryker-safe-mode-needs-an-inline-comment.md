---
name: stryker-safe-mode-needs-an-inline-comment
description: Stryker's Safe Mode drops every mutation in a method when one mutant fails to compile; only an inline comment prevents it, never a config setting, and the comment's form depends on whether the statement is one line.
metadata:
  type: project
---

When a Stryker.NET mutant fails to COMPILE, Stryker enters "Safe Mode" and discards **every** mutation
in that whole method, counting them all as compile errors. The score is then computed over what
survived the cull, so it describes a subset of the code and reads as a measurement of all of it.

**Why:** measured on `Tf2DemoSalvage.Audio`, the smallest project carrying every shape. Fifteen
triggers were hiding 410 compile-error mutants; clearing them took tested mutants from 681 to 786 and
the score from 62.37% **down** to 59.62%. The score falling is the tell that the blind spot was
flattering, not that the fix broke something.

**How to apply:** three shapes cause it in this codebase, and all three are idioms used everywhere, so
expect them in any new project too.

- `if (expr is not { } name)` — a mutant that empties the guard body leaves `name` (CS0165) or a
  struct `name`'s fields (CS0170) unassigned at the use below.
- `try { x = …; } catch { throw …; }` — a mutant that empties the catch removes the `throw` that
  definite-assignment analysis relies on (CS0165).
- `string.Create(CultureInfo.InvariantCulture, $"…")` — the String mutator rewrites the literal as
  `(IsActive(n) ? $"" : $"…")`, which cannot bind to a `ref DefaultInterpolatedStringHandler` (CS1620).

**Config cannot fix any of it, and trying wasted two runs.** `ignore-mutations` and `ignore-methods`
both applied — their own counters proved it — and the Safe Mode list came back byte-identical. Those
filters run after injection and compilation, so the mutant has already broken the build. An earlier
`"ignore-mutations": ["String"]` sat in a config for a day suppressing ordinary string mutants and
nothing it was added for.

**Only an inline comment prevents injection**, and the form matters:

- `// Stryker disable once` covers the next STATEMENT — enough for a one-line `if` guard.
- A multi-line statement, such as a `throw` spanning five lines, needs
  `// Stryker disable all` … `// Stryker restore all`. A comment placed INSIDE an argument list is
  ignored outright; that was tried and the error survived.

Keep the disabled range tight: every mutant inside it is lost, which is the cost of the fix.

**Fix the `string.Create` family exhaustively, never from the log.** The log names only the FIRST
trigger in a method, because Safe Mode removes the rest before they can be reported — so a file fixed
from the log alone surfaces its next one on the following run. Search for the idiom instead.

See `docs/RISKS.md` B410, and [[instrument-bugs-outnumber-decoder-bugs]] for the general rule that an
instrument is proved with a control before it is believed — here the control was the filter's own
"Removed by mutation type filter" counter, which is what showed the setting had applied and still
changed nothing.
