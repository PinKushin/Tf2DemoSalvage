---
name: owner-input-does-not-affect-rcon-driven-runs
description: A specimen run driven over RCON is unaffected by the owner using keyboard/mouse; do not tell him to stay hands-off. Only the click that starts a period client is his.
metadata:
  node_type: memory
  type: feedback
  originSessionId: 124d1a9c-39d8-407f-871a-adb7c8b92a98
  modified: 2026-10-29T16:07:12.510Z
---

Owner, 2026-10-29: *"my being on the keypoad and mouse actually doersnt effect the run"*.

**Why:** the specimen chain is console commands sent over RCON (port 27025), not synthesized clicks or keystrokes, so
the client's focus and the owner's input do not change what is recorded. I had been telling him to stay hands-off.

Owner, same session: *"its all in engine, and doesnt actualy send any real keystrokes"* — the `autoexec` route (no RCON)
is in-engine too.

**How to apply:** for a specimen run (RCON or autoexec), say only what he must do (the one launcher click, then
nothing). Do not ask for hands-off. Only a run that drives the desktop with synthesized input (UI suites) needs the
desktop to himself: there, say so. Related: [[a-client-starts-from-its-shipped-launcher]],
[[tf2-is-a-shared-fixture]].
