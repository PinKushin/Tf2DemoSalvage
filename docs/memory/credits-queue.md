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
- **The cross-model review always goes through this queue.** Owner, 2026-10-18, after the lead (Sonnet) tried subagents for it and the hooks refused opus reviewers: *"that should have been done on the api"*. The reviewer is always a different model from the lead; with a Sonnet lead, Sonnet subagents are out. Since 2026-10-18 the hook also allows an opus `caveman:cavecrew-reviewer` (owner: *"we can change the hook to allow opus review"*), so a small diff can use that instead of credits. Write the prompt to `F:\credits-queue\prompts\<name>.md`, then the job JSON (`write: false`, workdir = the branch's worktree).
- **Budget, 2026-10-21 (owner):** *"we have $28 left on the API, so keep an eye out for that running out today, maybe tomorrow, and it will be out for the rest of the month."* About $2-4 a Fable review, so roughly 8-10 reviews. **Spend it only on the cross-model MERGE reviews** (the highest value per dollar); engine reads, Ghidra reads and implementation go local (tf2-engine-reader on sonnet, local implementers). Note the balance in the next report after each job. **When it runs out** (the runner stops on "Credit balance is too low"): the cross-model merge review is a local `caveman:cavecrew-reviewer` on opus (the hook allows it for a lead that is not opus; keep the prompt neutral, scope only), and the lead's own review still comes first.
Related: [[gate-once-per-batch]], [[review-for-recall]].
