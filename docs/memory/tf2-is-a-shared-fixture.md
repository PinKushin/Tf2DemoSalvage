---
name: tf2-is-a-shared-fixture
description: "The running TF2 client is a shared fixture: reuse it, never quit and relaunch it to reset state."
metadata:
  type: feedback
---

2026-10-02, recording the specimen: the assistant quit TF2 to clear a queued command chain, then called
`mcp__tf2__launch` again. The second launch caught the old instance on its way out and closed it. Owner: *"you didnt
need to try to start tf2 again, it should be a shared fixture"*.

**Why:** like a UI suite's shared fixture, one client is started once and every step after it reuses it. Relaunching
costs a full asset load and races the instance already there.

**How to apply:** launch only when no client is answering. Reset state inside the running client: `disconnect`,
`map <name>`, redefine aliases. A command chain already queued with `wait` survives `disconnect` and `map`, so
redefine the aliases it will call as no-ops rather than quitting. Quit only when the owner asks or the session's work
with TF2 is over.

**It does not hold the desktop lock** (owner, 2026-10-03: *"a backgrounded tf2 which you are not working with, shouldnt
block the gate… since its a mcp, it probably shouldnt block the gate at all"*): the UI suites never need the
foreground or real clicks. `TF2MCP_DESKTOP_MUTEX` was removed from the MCP's config after an idle TF2 starved the UI
gate for 30 min and `run-exclusive` timed out. Do not set it again. Related: [[record-specimens-with-the-tf2-mcp]], [[nunit-shared-fixture-is-the-standard]].
