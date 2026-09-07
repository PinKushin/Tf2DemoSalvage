---
name: workflow-agents-inherit-the-session-model
description: A Workflow script's agent() calls inherit the main-loop model unless opts.model is set. Subagents run on haiku, enforced globally by ~/.claude/hooks/block-expensive-subagents.ps1 for both Agent and Workflow — and haiku is only acceptable because their output is reviewed.
metadata:
  type: feedback
---

**The owner, 2026-09-07, on a five-agent scouting workflow: *"omg you subagented to opus 5 models
fuck"*.** Stopped mid-run.

## The mechanism

**`agent()` inside a Workflow script inherits the MAIN-LOOP model unless `opts.model` is set** —
silently, and multiplied by however many agents the script fans out to. A workflow written the
obvious way spawns whatever the session is running.

```js
await agent(prompt, { model: 'haiku', label: 'read:x', phase: 'Read', schema: S })
```

## What was enforced, and what was not

**The gap was Workflow, not Agent — and I first reported that backwards.** A project hook
(`.claude/hooks/subagent-policy.ps1`) had matched `Agent` since D145 and worked. I claimed no hook
covered it because I checked only `~/.claude/settings.json` and never the project's own
`.claude/settings.json`. **A claim that a hook does not exist is a claim about where you looked**,
which is the same shape as [[an-empty-search-needs-a-control]].

That hook is gone now, replaced by a global one the owner wanted global:
`~/.claude/hooks/block-expensive-subagents.ps1`, matching `Agent|Workflow`. It denies any model but
`haiku`, including a Workflow script whose `agent()` calls do not name one literally. Its backup
lives at `.claude/hooks/global/` — see the README there for why that directory exists.

## Haiku, and the condition that makes it viable

This reverses 2026-09-06's *"i dont really trust haiku… sonnet 4.6 used less tokens than haiku it
seemed like, while giving me better code"*. The reversal is coherent once the earlier measure is
stated properly, and the owner stated it: *"4.6 is what i thought used less tokens or at leased used
them better because the code had less bugs"* — **tokens per good outcome, not tokens per call.**

What killed it is that the option is gone: *"since we can only get 5, thats going to probably use
more tokens, even when antho says it shouldnt lol"*.

**The condition is load-bearing rather than a caveat:** *"as long as you review the haiku specific
bugs should be cought and fixed"*. The rework argument only favours a better model when the rework
is unpaid for, and review is what pays for it. **A session that stops reviewing subagent output has
removed the reason haiku was acceptable.**

## And ask whether it needs an agent at all

The run that caused this was four read-only scouting agents over files in this repo and the SDK.
Reading those inline costs the main loop a few tool calls and no agent tokens. Fan out for genuine
parallelism over many items, or for independent verification — not for "read four files and tell me
what they say".

**Ultracode makes this louder rather than safer.** It says to reach for a workflow on every
substantive task and that token cost is not a constraint, which is a licence to orchestrate, not a
licence to ignore a standing decision about which model does the orchestrated work.

Related: [[one-subagent-and-prefer-cheap-models]].
