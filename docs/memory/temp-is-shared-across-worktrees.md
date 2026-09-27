---
name: temp-is-shared-across-worktrees
description: $TEMP is one folder for every worktree and subagent — a fixed log name gets overwritten mid-run by a parallel agent.
metadata:
  node_type: memory
  type: feedback
  originSessionId: 124d1a9c-39d8-407f-871a-adb7c8b92a98
  modified: 2026-09-27T12:52:11.142Z
---

`$TEMP` is the same directory for this session, every subagent worktree and every sibling session. A gate log
written to `$TEMP/gate1.log` was overwritten by a subagent's own test run halfway through, and the tail showed
9 Audio failures that belonged to the other worktree (its `Results File:` line named the other worktree's path).

**Why:** a report read from a shared, fixed-name file can describe someone else's run.

**How to apply:** when subagents run in parallel, give logs a name unique to the run (worktree or task id in
the name), and trust the gate's own exit code and this worktree's `TestResults/*.trx` counters over a log tail.
Related: [[read-the-trx-total-not-the-console]], [[instrument-bugs-outnumber-decoder-bugs]].
