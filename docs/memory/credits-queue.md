---
name: credits-queue
description: "API-credit Claude runs go through a job folder the owner's queue runner works through: drop a JSON job in F:\\credits-queue\\pending; never launch claude -p yourself."
metadata:
  node_type: memory
  type: reference
  originSessionId: 124d1a9c-39d8-407f-871a-adb7c8b92a98
  modified: 2026-10-19T00:05:08.110Z
---

Owner, 2026-10-18: *"we can ssetup that folder thing, i like that lol"*, after being told that jobs a session adds would run on his key without him launching each.

- **Runner:** `C:\Users\pinku\.local\bin\claude-credits-queue.ps1`; the owner starts it: `pmux new-session -d -s credits -- pwsh -NoProfile -File "$HOME\.local\bin\claude-credits-queue.ps1"`. Billing needs an owner-launched process: never start it, or `claude -p`, from a session.
- **Job:** a JSON file in `F:\credits-queue\pending`: `prompt`, `model`, `workdir`, `out`, optional `adddir` [ ], `write` bool. Oldest first, one at a time; it moves to `running\`, then `done\` or `failed\` with a `.log`.
- **Guards:** models allowlisted; every path under `F:\` or `C:\Users\pinku\source\repos\`; `-Write` refused on main/master by `claude-credits.ps1`; stops on "Credit balance is too low".
- **Machine:** tools run locally. Review and engine-read jobs are cheap. A `-Write` job builds and runs tests on the CPU: unit tests only, never the UI suite or gate.sh.
- **Model:** a review is `claude-fable-5-1`; implementation is `claude-opus-5-5` (D196).
Related: [[gate-once-per-batch]], [[review-for-recall]].
