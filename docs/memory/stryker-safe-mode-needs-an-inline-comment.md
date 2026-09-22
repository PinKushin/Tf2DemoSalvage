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

**Write `all` on the range form — it is load-bearing, not decoration.** A commit once removed it across
twenty sites on the reasoning that *"'all' is not a valid mutator name in Stryker 4.16"*, without running
Stryker. An A/B on two sibling `throw`s in one method, in one run, says otherwise: with `all` the site
was suppressed and 4 mutants were Ignored; with the bare form the CS1620 came straight back and only 1
mutant was Ignored. The no-argument form reaches only the statement's outermost node, and the mutant
that matters is several levels down on an interpolated literal.

Keep the disabled range tight: every mutant inside it is lost, which is the cost of the fix.

A fourth shape turns up too: `while (true)` where the Boolean Literal mutator flips it to `false`, so a
loop holding the method's only `return` or only assignment never runs (CS0161 / CS0165).

**Fix the `string.Create` family exhaustively, never from the log.** The log names only the FIRST
trigger in a method, because Safe Mode removes the rest before they can be reported — so a file fixed
from the log alone surfaces its next one on the following run. Search for the idiom instead.

**Add the comment in the SAME change that writes the idiom.** On 2026-09-20 the session that fixed B410 wrote fourteen
new triggers in its own code the same day — `is not { } x` guards, `out` variables read after the expression that
declares them, and `||` joining two of them. The box found three a night later, one name per method; searching the
branch's diff for the idioms found all fourteen in one pass: `git diff main -U0 -- managed/ | grep -E "is not \{|out [A-Za-z?<>]+ [a-z]"`.

**Recovered methods can make a run much LONGER, not just its score lower.** Content went 27 min → 6 h 14 min the night
B410 landed: the recovered methods were parsers, a mutated loop bound in a parser never ends, and 864 mutants ran to a
~70 s timeout each. It held the box's lock and three other jobs were refused. A timeout is a detected mutant, so the
work is real — watch the runtime after unmasking, and re-book the slot from the measured number (see
`PinKushin/MEASUREMENT-BOX-LOG.md`).

See `docs/RISKS.md` B410, and [[instrument-bugs-outnumber-decoder-bugs]] for the general rule that an
instrument is proved with a control before it is believed — here the control was the filter's own
"Removed by mutation type filter" counter, which is what showed the setting had applied and still
changed nothing.
