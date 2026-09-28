---
name: temp-is-shared-across-worktrees
description: $TEMP is one folder for every worktree and subagent — a fixed log name gets overwritten mid-run by a parallel agent.
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 124d1a9c-39d8-407f-871a-adb7c8b92a98
  modified: 2026-09-27T12:52:11.142Z
---

`$TEMP` is the same directory for this session, every subagent worktree, and every sibling session. A
gate log written to a fixed name was overwritten by a parallel subagent's own test run halfway
through, showing failures that belonged to the other worktree.

**Why:** a report read from a shared, fixed-name file can describe someone else's run.

**How to apply:** when subagents run in parallel, name logs uniquely (worktree/task id in the name),
and trust the gate's own exit code and this worktree's `TestResults/*.trx` counters over a log tail.
Related: [[read-the-trx-total-not-the-console]], [[instrument-bugs-outnumber-decoder-bugs]].
