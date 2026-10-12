---
name: stryker-safe-mode-needs-an-inline-comment
description: Stryker's Safe Mode drops every mutation in a method when one mutant fails to compile; only an inline comment prevents it, never a config setting, and the comment's form depends on whether the statement is one line.
metadata:
  type: project
---

When a Stryker mutant fails to COMPILE, Stryker discards EVERY mutation in that whole method as a
compile error, and the score is computed over what survived — describing a subset while reading as a
measurement of everything.

**Measured:** fifteen triggers hiding 410 compile-error mutants; clearing them took the score DOWN
from 62.37% to 59.62% — the falling score is the tell the blind spot was flattering.

**Three shapes cause it, all common idioms:**
- `if (expr is not { } name)` — a mutant emptying the guard leaves `name` unassigned (CS0165/CS0170).
- `try { x = …; } catch { throw …; }` — emptying the catch removes the `throw` definite-assignment
  relies on.
- `string.Create(CultureInfo.InvariantCulture, $"…")` — the mutator's rewrite can't bind to the
  interpolated-string handler (CS1620).

**Config cannot fix it** — `ignore-mutations`/`ignore-methods` both apply too late (after injection
and compilation has already broken).

**Only an inline comment prevents injection:**
- `// Stryker disable once` covers the next STATEMENT — fine for a one-line guard.
- A multi-line statement needs `// Stryker disable all` … `// Stryker restore all` — a comment inside
  an argument list is ignored outright.
- **Write `all` on the range form, not the bare form** — verified by A/B: bare form let the CS1620
  come straight back; `all` suppressed it. The no-argument form reaches only the outermost node.

- **`// Stryker disable once all : why` before a statement covers the WHOLE statement, multi-line too** — measured
  2026-10-12 on a three-line `return string.Create(…)`: 15 compile errors and a Safe Mode removal before, none
  after. One line a site; before the member for an expression-bodied one. The pair is only for several statements.
- **The compile phase reports every method whatever `--mutate` says** — so a two-minute run scoped to a few
  characters is the oracle: fix what it names, rerun, until it names nothing. `Viewer3D` went 75 removals to 0 in
  three runs (116 comments; compile errors 2,197 to 529 of 4,400; D214).
- **`--mutate "File.cs{a..b}"` spans CHARACTERS, not lines.** The local hook's "80" is 80 characters; cover a method
  with consecutive spans. A mutant wider than one span (a block removal) is filtered out.
- **A reason string containing "Safe Mode" pollutes `grep -c "Safe Mode"`** — count `Safe Mode!`.

Keep the disabled range tight — every mutant inside it is lost.

A fourth shape: `while (true)` where flipping to `false` skips the loop's only return/assignment.

**Fix the `string.Create` family exhaustively, never from the log** — the log names only the FIRST
trigger per method, since Safe Mode removes the rest before they're reported.

**Add the comment in the SAME change that writes the idiom** — searching a branch's diff for the
idiom pattern (`git diff main -U0 | grep -E "is not \{|out [A-Za-z?<>]+ [a-z]"`) found all fourteen new
triggers in one pass, where the box found only three, one per night.

**Recovered methods can make a run much LONGER** — recovering parser methods with mutated loop bounds
turned a 27-minute run into 6h14m; a timeout is still a detected mutant, but watch runtime and
re-book the slot from the measured number.

See `docs/RISKS.md` B410, [[instrument-bugs-outnumber-decoder-bugs]].
