---
name: a-hook-is-not-live-until-a-control-fires-it
description: Project hooks load from the BASE checkout, not the session worktree; prove a new hook with a control edit through the harness, not by piping a payload to the script.
metadata:
  type: feedback
---

A project hook is live only when the harness calls it. Testing the script with a piped payload proves the script, not the registration.

2026-10-13: `tf2-thin-view.ps1` was registered 2026-10-12 and `CLAUDE.md` said the rule was "held at the edit". It never fired for eight slices. A control Write of `Tf2DemoSalvage.Scene` into the view folder went through. Cause: hooks and `.claude/settings.json` load from the base checkout (the repository root the worktrees hang off), which sat on a detached HEAD from 2026-10-05 with no such hook. Moving the base checkout to current main made the hook fire at once, mid-session, for a running subagent too.

**Why:** the owner: "just the decision and memory failed hard" - D214 and its memory note did not hold the thin view; only the tests did, and the hook was believed live when it was not.

**How to apply:**
- After adding or changing a project hook, update the base checkout (`git -C <base> checkout --detach main`), then make one control edit through the Edit/Write tool that the hook must refuse. Denied = live.
- Update the base checkout after every push of main; it is detached because the session worktree holds `main`.
- A hook `ask` DOES prompt in bypass-permissions mode (owner got the prompts). A denied prompt ends a running subagent's turn.
- An `ask` on every edit gets approved blind. Ask only when something is removed, and name the removed lines.

Related: [[a-partial-thin-view-is-worse-than-none]], [[a-check-is-a-test-or-it-is-not-in]].
