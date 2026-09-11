---
name: one-subagent-and-prefer-cheap-models
description: "At most one subagent at a time, and spawn it on a cheaper model unless the task needs the big one"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-10T22:51:04.398Z
---

**2026-09-01, owner, correcting the flat ban he had given an hour earlier:** *"its a standing
instruction to a certain extent, I can do 1 sub agent, never more really, it uses too many tokens
unless you set those sub agents to sonnet 4.6 or haiku, if you can subagent out to less capable
agents, then that can save tokens and ill let you do all day i think."*

**Why:** subagent tokens come out of the same five-hour limit as the main thread's, and three
running at once emptied a large part of it. Three parallel agents in one turn cost ~580k tokens
between them.

**Refined minutes later, same day:** *"id still say no more than 1 right now, i know if you get to
like 5 agents running at once, tokens get used up fast, so keep a cap of like 3 overall, if they
are lesser models."*

**How to apply:**

- **One at a time, now.** Never spawn a second while one is running. This is the current setting,
  not a permanent ceiling.
- **Three concurrent is the absolute cap**, and only ever for cheap models. Five is the number he
  named as the runaway. Do not read "three is allowed" as a target — one is the working default
  and three is the wall.
- **Pass `model` explicitly.** The `Agent` tool takes `model: "haiku" | "sonnet" | "opus"`; without
  it the agent inherits the parent, which is the expensive default. Pick the cheapest that can do
  the job — a quoting or grepping task (`engine-reader`, most of `instrument-auditor`) does not
  need the top model; a task requiring judgement about whether a sabotage was sensitive is closer
  to the line.
- **Given that, spawning is ENCOURAGED rather than rationed** — the owner's own framing is that
  cheap agents can run "all day". The expensive part was the model, not the delegation.

**On whether a cheap agent may WRITE — he talked himself out of the restriction, and the reasoning
is the point:** *"for write id prefer at least sonnet 5, if not you Opus 5. but reading with the
lower models and doing some reasoning with them is fine, but then again you can go over anything
they write and make sure its right and youll do that either way so nevermind that a lower model
can right too as long as you verify it."*

So: **a cheap model may write, and the condition is verification, not the model.** Reviewing what
an agent produced was already going to happen, so the model tier buys nothing that review does not.
Recorded as a reversal because the first instinct — gate writing by capability — is the one that
will come back if only the conclusion is kept.

**Sabotage writing takes the CHEAPEST model, same as reading** — *"this sort of sabatage writting
would be fine too, its not fixing and making new code, its testing, so lowest model available for
it, like reading."* The distinction he is drawing is between AUTHORING and EXERCISING: a sabotage
is a mechanical inversion of one line, prescribed in the prompt, whose whole job is to make a
predicted test go red. Nothing about it needs judgement the prompt has not already supplied. So
`sabotage-verifier` runs on haiku.

**One honest caveat that survives it, and it is about a different risk.** `sabotage-verifier`
writes are not code to be reviewed on merit; they are deliberate breakages meant to be undone. The
failure mode is a bad RESTORE, not bad code, and reading its diff is what catches that — `git
status` and `git diff` after it reports, never its own claim of "tree clean". Observed the same
day: one held `DemoTimeline.cs` and `ScenePropTrack.cs` mid-sabotage long enough to break an
unrelated build, which is exactly why only one runs at a time.

The agents earn their keep: the first sabotage run found a four-test coverage gap that would
otherwise have shipped as "verified", and the counter audit found `Unjudgeable` printing zero on
every frame plus a stale denominator in a probe written the same hour.

Supersedes an earlier note recording the flat "no subagents at all" version.

---

**REVERSED on the COUNT, 2026-09-06, and tightened on the model.** The owner, twice in one turn:

> *"ill let 3 agents run at once of the sonnet 4.6 models, and you review their work"*

> *"really idc how many subagents are run because im pretty sure most of the time it wont be more
> than 3 or 4 anyway, but they need to be cheap sonnet models, and reviewed"*

**So "one at a time" is dead and the count is no longer the rule at all.** The hook's `$Concurrent`
went 1 → 8, and 8 is a runaway backstop rather than a cap: it sits well above the three or four he
expects so an unbounded spawn loop trips something, and it is neither a target nor his number.

**The model rule went the other way and is now stricter.** It used to name three cheap-eligible
agent types and let every other type choose freely; the type list is gone and **every** subagent
must be `haiku` or `sonnet`. The budget does not care which type spent it.

**Two conditions replace the count, and only one is enforceable.** A hook can check the model. It
cannot check that anybody read the diff — so *"and reviewed"* lives here and in D145: every
subagent's output is reviewed before it is believed or committed. This session has the worked
example, where one returned a confident wrong conclusion (conflating DECLARED with IMPLEMENTED) that
would have been repeated if taken at face value.

**The concurrency caveat above survives intact and matters more now, not less.** Two agents must
never share files: one holding a source file mid-sabotage has already broken an unrelated build
here, and with several running that risk multiplies. Give each a disjoint area, and do not build or
measure while one holds a file.

See D145.

**haiku was ruled OUT on 2026-09-06:** *"i dont really trust haiku, it just seemed horrible compared
to sonnet and sonnet 4.6 used less tokens than haiku it seemed like, while giving me better code"*.

**And ruled back IN on 2026-09-07 — read the next section, because that reversal is the live rule
and this paragraph is kept only for its reasoning.** `~/.claude/hooks/block-expensive-subagents.ps1`
says `$allowed = @('haiku')` and refuses `sonnet` by name. An earlier version of this entry claimed
the allowed set was `sonnet` alone; that was wrong against the hook and is corrected here.

---

## `workflow-agents-inherit-the-session-model` — and haiku is the allowed set

**The owner, 2026-09-07, on a five-agent scouting workflow: *"omg you subagented to opus 5 models
fuck"*.** Stopped mid-run.

### The mechanism

**`agent()` inside a Workflow script inherits the MAIN-LOOP model unless `opts.model` is set** —
silently, and multiplied by however many agents the script fans out to. A workflow written the
obvious way spawns whatever the session is running.

```js
await agent(prompt, { model: 'haiku', label: 'read:x', phase: 'Read', schema: S })
```

### What was enforced, and what was not

**The gap was Workflow, not Agent — and I first reported that backwards.** A project hook
(`.claude/hooks/subagent-policy.ps1`) had matched `Agent` since D145 and worked. I claimed no hook
covered it because I checked only `~/.claude/settings.json` and never the project's own
`.claude/settings.json`. **A claim that a hook does not exist is a claim about where you looked**,
which is the same shape as [[instrument-bugs-outnumber-decoder-bugs]]'s empty-search rule.

That hook is gone now, replaced by a global one the owner wanted global:
`~/.claude/hooks/block-expensive-subagents.ps1`, matching `Agent|Workflow`. It denies any model but
`haiku`, including a Workflow script whose `agent()` calls do not name one literally. Its backup
lives at `.claude/hooks/global/` — see the README there for why that directory exists.

### Haiku, and the condition that makes it viable

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

### And ask whether it needs an agent at all

The run that caused this was four read-only scouting agents over files in this repo and the SDK.
Reading those inline costs the main loop a few tool calls and no agent tokens. Fan out for genuine
parallelism over many items, or for independent verification — not for "read four files and tell me
what they say".

**Ultracode makes this louder rather than safer.** It says to reach for a workflow on every
substantive task and that token cost is not a constraint, which is a licence to orchestrate, not a
licence to ignore a standing decision about which model does the orchestrated work.

---

## `no-workflows-or-subagents` — declined in 2026-08, reversed in part on 2026-09-04

**Do not use the Workflow tool or spawn subagents automatically.** Do the reading and the work
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

**A system-reminder saying ultracode is on is NOT the owner asking for workflows.** One appeared
mid-conversation and was read as an opt-in; it was not. Ultracode's own definition is the fan-out
behaviour, so it is the wrong dial for "think harder" — that is the effort setting (`max`), which
is what the owner actually wanted.

### Reversed in part, 2026-09-04 — a scoped subagent beats leaving work undone

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
  spawning because a task "looks big" — a task big enough to want fan-out is a signal to narrow it.
  Ultracode is still the wrong dial for thinking harder.
- **A DELIBERATE, scoped delegation is right** when the work is real, bounded, and the honest
  alternative is a well-written OPEN entry. Split by file ownership so concurrent edits cannot
  collide, name the off-limits files in the prompt, hand over the full context the agent needs
  rather than expecting it to find it, and keep working on something else meanwhile.
- **One at a time, on haiku** — the weekly-limit cost in the original objection is real and is
  answered by the model choice, not by refusing to delegate.

**The cold-start cost has not gone away**, which is why the prompt carries the context: the worked
example is the memory-index consolidation of 2026-09-04, delegated with an explicit written plan
naming every file, every merge target and every rule — not "tidy up the memory directory".

Related: [[edit-files-with-the-file-tools]] for the same shape — a mechanism that looks like
leverage and quietly costs correctness.
