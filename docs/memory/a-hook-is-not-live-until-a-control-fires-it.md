---
name: a-hook-is-not-live-until-a-control-fires-it
description: Project hooks load from the session's own project folder (${CLAUDE_PROJECT_DIR}) - under T3 that is the t3-* worktree; keep it at main and prove a hook with a control through the harness, not by piping a payload to the script.
metadata:
  type: feedback
---

A project hook is live only when the harness calls it. Testing the script with a piped payload proves the script, not the registration.

2026-10-13: `tf2-thin-view.ps1` was registered 2026-10-12 and `CLAUDE.md` said the rule was "held at the edit". It never fired for eight slices. A control Write of `Tf2DemoSalvage.Scene` into the view folder went through. Cause: hooks and `.claude/settings.json` load from the base checkout (the repository root the worktrees hang off), which sat on a detached HEAD from 2026-10-05 with no such hook. Moving the base checkout to current main made the hook fire at once, mid-session, for a running subagent too.

**Why:** the owner: "just the decision and memory failed hard" - D214 and its memory note did not hold the thin view; only the tests did, and the hook was believed live when it was not.

**Corrected 2026-11-19:** the hooks run from `${CLAUDE_PROJECT_DIR}` (`settings.json` names it), which is the folder the
SESSION was opened in. In 2026-10 that was the base checkout; under T3 it is the thread's `t3-*` worktree, and the
dispatcher's `run-hooks.log` is written beside whichever copy ran - that is how to tell. The T3 worktree sat 619
commits behind main with stale hook copies, and its stale `docs/measurements` files made the commit gate refuse every
commit from the session once the gate was fast enough to finish (before, it timed out and passed).

**How to apply:**
- After adding or changing a project hook, bring the session's project folder to main (fast-forward the `t3-*`
  worktree, or `git -C <base> checkout --detach main` for a base-checkout session), then fire one control the hook
  must refuse - an `echo` of the command is enough for a Bash hook. Denied = live.
- Keep that folder at main after every push of main.
- A hook `ask` DOES prompt in bypass-permissions mode (owner got the prompts). A denied prompt ends a running subagent's turn.
- An `ask` on every edit gets approved blind. Ask only when something is removed, and name the removed lines.

Related: [[a-partial-thin-view-is-worse-than-none]], [[a-check-is-a-test-or-it-is-not-in]].
