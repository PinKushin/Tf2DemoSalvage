---
name: one-subagent-and-prefer-cheap-models
description: "At most one subagent at a time, and spawn it on a cheaper model unless the task needs the big one"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-02T00:36:36.338Z
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

Supersedes [[no-more-subagents-this-session]], which recorded the flat version.
