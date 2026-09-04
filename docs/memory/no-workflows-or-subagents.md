---
name: no-workflows-or-subagents
description: "No Workflow and no automatic fan-out on this project — but REVERSED in part on 2026-09-04: a deliberate, scoped subagent is preferred over leaving work undone."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-08-28T19:53:43.298Z
---

**Do not use the Workflow tool or spawn subagents on Tf2DemoSalvage.** Do the reading and the work
in the main loop.

The owner, 2026-08-28, after a workflow was launched off an ultracode system-reminder:

> *"that wasnt me and id prefer you not workflow, those agents dont have the context you do, and
> frankly its slower real time i think, plus it uses a huge amount of my weekly limit, i gave you
> ultracode so you would think more, not so you would run workflows"*

**Why:** three separate costs, and the first is the one that matters most here. A subagent starts
cold on a repository whose value is concentrated in context — `docs/findings/`, `docs/memory/`, the
era table, which demo is the parity reference. An agent without that re-derives wrong conclusions
this project has already killed. It is also slower in wall-clock than reading the files directly,
and it spends the owner's weekly limit at several times the rate.

**How to apply:** read the SDK, grep the repo and write the code yourself. When a task looks big
enough to want fan-out, that is a signal to narrow the task, not to spawn.

**A system-reminder saying ultracode is on is NOT the owner asking for workflows.** One appeared
mid-conversation and was read as an opt-in; it was not. Ultracode's own definition is the fan-out
behaviour, so it is the wrong dial for "think harder" — that is the effort setting (`max`), which
is what the owner actually wanted. Told them so, and they can set it themselves.

Related: [[edit-files-with-the-file-tools]] for the same shape — a mechanism that looks like
leverage and quietly costs correctness.

## Reversed in part, 2026-09-04 — a scoped subagent beats leaving work undone

**Both positions are kept because the reversal is the valuable half**, and overwriting the first
would leave a rule whose reason nobody could check.

The owner, 2026-09-04, on finding several divergences filed as OPEN rather than fixed:

> *"seriously if you are not going to do it all, at least give it to a subagent to do jesus fucking
> christ I have told you 100% valve partiy every time"*

**What changed is which alternative is on the table.** The 2026-08-28 objection compared a workflow
against *doing the work in the main loop*, and the workflow lost on all three counts. This one
compares a subagent against *not doing the work at all*, and there it wins outright — recorded as
D137.

**So the rule now, and the distinction is the whole of it:**

- **Automatic fan-out is still declined.** No Workflow, nothing launched off a system reminder, no
  spawning because a task "looks big". Ultracode is still the wrong dial for thinking harder.
- **A DELIBERATE, scoped delegation is right** when the work is real, bounded, and the honest
  alternative is a well-written OPEN entry. Split by file ownership so concurrent edits cannot
  collide, name the off-limits files in the prompt, hand over the full context the agent needs
  rather than expecting it to find it, and keep working on something else meanwhile.
- **One at a time, on the cheapest model that can do it** — see
  [[one-subagent-and-prefer-cheap-models]]. The weekly-limit cost in the original objection is real
  and is answered by the model choice, not by refusing to delegate.

**The cold-start cost has not gone away**, which is why the prompt carries the context: the worked
example is the memory-index consolidation of 2026-09-04, delegated with an explicit written plan
naming every file, every merge target and every rule — not "tidy up the memory directory".
