---
name: never-stop-a-working-subagent
description: A subagent that has started working keeps running; a new concurrency limit applies to the NEXT launch only.
metadata:
  node_type: memory
  type: feedback
  originSessionId: 124d1a9c-39d8-407f-871a-adb7c8b92a98
  modified: 2026-10-04T14:37:11.011Z
---

2026-10-04: owner said "we are dropping to one subagent at a time from here out"; three running agents were stopped,
throwing away their in-flight work. Owner: *"never fucking stop a running subagent that already has its fucking context
and started its first turn... you just fucking burned over 500k tokens for nothing, THAT IS WHY I SAID 'FROM HERE ON
OUT'"*.

**How to apply:**
- A limit change ("from here on out") applies to future launches; let running agents finish.
- Stopping is acceptable only right after spawning, before real work (a few hundred tokens).
- If one was stopped by mistake, resume it with SendMessage (context is kept) rather than relaunching.

**But an agent that is only WAITING is not working** (owner, 2026-10-20, after the original frame-cost agent stayed flagged up for a day, the lead twice called it idle/finished, and the owner had to stop its background loops himself: *"the loops were not finished I STOPPED THEM when you should have after I mentioned the agent was still up"*). When the owner says an agent is still up: list its live processes (`dotnet`, `tf2demoview`, `pwsh`, `bash` loops, pmux sessions) and its worktree, stop the stale watcher/loop tasks with TaskStop, and only then say what its state is. Background A/B loops left running can also contaminate other agents' measurements. Never answer "it's idle" without having looked.

Related: [[one-subagent-and-prefer-cheap-models]].
