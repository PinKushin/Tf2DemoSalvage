---
name: agents-ask-the-owner-client-questions
description: A subagent stuck on a TF2 client-behaviour question stops and asks; the owner answers in seconds. The one exception to no-interim-status.
metadata:
  node_type: memory
  type: feedback
  originSessionId: 124d1a9c-39d8-407f-871a-adb7c8b92a98
  modified: 2026-10-10T02:25:53.839Z
---

Every agent brief carries: "If stuck on how the TF2 client behaves (a console command, a cvar, a menu, what a
panel shows), stop and report the question instead of trying alternatives - the owner knows." Relay the question
to the owner, send the answer back to the SAME agent.

**Why:** 2026-10-09, B161: an agent tried several commands for a tick readout and gave up; the owner: "wont demoui
or demoui2 show the tick?" and "Id have probably cought that a long time ago". He cannot see agent reasoning.

**How to apply:** only for owner-knowable client facts, only when actually stuck. Not progress pings -
[[no-interim-status-replies]] still holds. Engine internals stay with the SDK/disassembly.
