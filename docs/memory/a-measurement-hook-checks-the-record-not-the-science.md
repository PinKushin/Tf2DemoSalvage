---
name: a-measurement-hook-checks-the-record-not-the-science
description: tf2-measurement-gate/-cited make a measurement leave a record and a fix: name its test; they cannot judge whether the test or control is any good. Escapes and what they cost.
metadata:
  type: feedback
---

Two project hooks (2026-10-21, owner: "nothing counts as a number until it was observed"; "test what you are deciding") hold the method in `docs/measurements/README.md`. What each enforces is that README's "Hooks" section; the reasoning is in each script's header.

**Before a measuring command** (`tf2demoview --measure|--shot`, `dotnet-trace collect`, a measuring Probe, `cl_showfps 1`/`net_graph 1`/`timedemo` in the TF2 console): commit a `docs/measurements/` file with Observation, Question, Method and Control filled first. Name it with `# measurement: <file>` when several are open. `# exploratory` (`// exploratory` in the console) skips the gate, and its numbers are then not quotable anywhere.

**Before a commit**: a filled Result needs a Verdict; sections filled at first commit may not change. **A `fix:`/`perf:` commit** needs tests/ staged, or `Tested by:` + `Observed:`, or `No-test: <reason>`; `perf:` also cites its measurements file.

**Why:** the lead quoted a pub figure as measured and a gap never paired. **How to apply:** do not treat a passing gate as evidence. It proves a test or a control was NAMED, not that it tests the decision. Review still reads it.

Instrument lesson from building them: `git show <40-hex>:<path>` dies with "Filename too long" under a deep worktree path, and with `2>$null` that reads as an empty file. That silently disabled the tamper check until its control failed. Use `git cat-file -p`.
