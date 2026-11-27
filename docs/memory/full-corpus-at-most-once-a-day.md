---
name: full-corpus-at-most-once-a-day
description: "The full lcor superset gate runs at most once a day; a failure in it is rerun as that one test, never as the whole corpus; recheck with --filter, never a rerun."
metadata:
  type: feedback
---

2026-10-03: an agent re-ran the whole corpus (GCOR_ONLY=0) to recheck one failing lcor test, lost the output and started
again. Owner: *"if a 5+ hour run failed forget it"*, *"full corpus runs are like a once a day thing at most, they take
too long"*.

**Why:** the superset takes hours. Rerunning it for one test spends the day on a question one filtered run answers.

**How to apply:**
- Full superset at most once a day, for a decode change. Never per commit, never per agent.
- A superset failure is rechecked with `--filter` on that test (GCOR_ONLY=0) plus gate phase 1 (gcor). Not the corpus again.
- Agents never run the full corpus unless told.
- Run any long job with output to a log file, so the result survives the session closing.

**2026-11-17, broken anyway, so now a hook.** The lead ran `dotnet test tests/Tf2DemoSalvage.Corpus.Tests --filter ...`
with no `TF2DEMOSALVAGE_GCOR_ONLY=1` to "check" B544, a sampling change. A filter narrows the classes, not the demos, so
it walked all of lcor and held the Corpus.Tests binaries for 70 minutes, blocking both gates. Owner: *"yea that would
have taken hours if not a whole day or more"*. `.claude/hooks/tf2-corpus-run-is-gcor.ps1` now denies a corpus
`dotnet test` without `GCOR_ONLY=1` and asks before any `GCOR_ONLY=0`.

**Same day, the sibling rule:** a three-suite UI soak piped `dotnet test …UiTests` into `grep -A1 | head` with no copy,
and B558's rare failure lost its window dump. `.claude/hooks/tf2-ui-run-keeps-its-log.ps1` denies a piped UI run that
does not also write its whole output to a file.

**2026-11-27, the lead put it in a brief.** B880 fix-9's brief told the implementer to "run the full-corpus superset";
it launched `GCOR_ONLY=0` inside a pmux `send-keys` string and the hook's ask reached the owner. Owner: *"ok yea im not
doing this the actual full lcor takes days it shouldnt be trying to do a full run"*, then *"Do you not understand how
big the fucking lcor is now? its 100s of gb"*, then *"check the actual size i might be under or over extimating"*.
Measured 2026-11-27: `tools/corpus/local` → `F:\Tf2DemoSalvage-lcor`, 35.5 GB / 855 `.dem` (18.3 GB) recursive, but
`DemoCorpus.Files` reads only the top level: 146 demos, 2.1 GB; `incoming\` holds 709 `.dem` (16.2 GB, 33.3 GB total);
`D:\tf2-demo-archive` 380 `.dem` (19.1 GB). Whole demo pool ≈ 37 GB of `.dem`, not hundreds. A full pass still takes
days (every corpus test decodes each demo), so (CLAUDE.md's old "~50 min" row was stale): **no brief ever asks for a superset** — a decode change is measured on a chosen sample
(z1800, gullywash_f9, tf2-2026-pub-pov-clean, one or two modern matches, both points of view) through a probe or
`Corpus.Demo("name")`, and the gate stays gcor.

Related: [[gate-once-per-merge-not-per-commit]], [[lcor-is-not-in-a-worktree]].
