---
name: one-subagent-and-prefer-cheap-models
description: "Subagent model selection: opus for full implementation work (D196), sonnet for a bounded known task, haiku only for the sabotage verifier; one subagent at a time."
metadata:
  type: feedback
---

## D196: Opus for implementation work

Full implementation work goes to `model: "opus"` — porting a Valve class, a multi-file feature,
anything with judgment about engine behaviour. Owner: the subagent rule "was made with the idea that
subagents were only doing very very basic work... not doing full implementation work like this."

**Why:** sonnet first passes consistently under-ported ("narrowed", "deferred") things that turned
out fully portable at two-three review rounds and 500-700k tokens per agent. Measured in tokens per
GOOD outcome, the better model wins.

**How to apply:**
- `opus`: implementation work.
- `sonnet`: a bounded, known task — one method, one branch, one lookup with a stated answer shape.
- `haiku`: only the sabotage verifier (D177).
- Always name the model; review stays mandatory (D145) whichever ran.

## Early in the weekly limit: one subagent at a time, main loop by default

Depends on token budget — early in the week, do work directly and run at most one subagent for a
bounded sonnet task; late in the week with tokens to spare, several at once is fine. Check the budget
(`get_usage`), don't guess.

**Why:** each new agent pays input tokens rebuilding context the main loop already holds.

## D168: Haiku refused for code and analysis subagents

Haiku produced wrong answers on decompiler output and SDK analysis. Only pass `haiku` for a purely
mechanical, fully self-contained task (sabotage verifier, a remote-log number read).

## Confirmed 2026-09-28: the split works, keep it

Owner: *"yes the current split is fine … this workflow has actually seemed to be faster than what we had
before, it took us a little over a week to do physics, but the hud system … took us like 2 days"*.
Split: main loop reads engine (Ghidra/SDK), writes brief; opus subagent implements; main loop reviews,
runs gates 2/3, merges, fixes CI. **Engine read before brief, always** — B426 brief skipped it, lighting
origin left open.

**Subagents skip TDD even when the brief says TESTS FIRST** (B429, B436, 2026-09-29): they report "code first, sabotaged
after". Brief must require a separate red commit or a logged red run BEFORE the implementation edit.

## 2026-09-30: parallel subagents OK, rigour never traded for speed

Subagents ran 1.5-3 h each (B112 46 sabotage rounds, gate twice, 59-demo censuses, harness bugs). Owner approved:
two in parallel, each `isolation: "worktree"`, on unrelated areas; sabotage every branch but run only the affected test
project; gate once per branch. Owner: *"I don't want to lose the rigor though. Doing it right the first time, works
faster overall, and uses less tokens overall, then getting it wrong and having to fix it."*
**How to apply:** speed comes from parallelism and targeted runs, never from fewer sabotage rounds, skipped censuses
or skipped engine reads. Desktop phases (2/3) stay serial under the lock.

## 2026-09-30: Sonnet 5.5 does not change the split
Owner: sonnet's problem was *"not continuing its work, not really its generation"*, so a newer sonnet is not expected
to fix it. Keep opus for implementation. Usage: two opus agents plus main-loop decompiles hit 50% of the 5-hour limit
in an hour. **Standing rule (owner, 2026-09-30): one subagent at a time; two only if both are sonnet on small tasks** —
*"running more than one agent at a time, eats my limit hard"*. Supersedes the parallel note above.
**Temporary raise, 2026-10-01 until the Sunday weekly reset:** up to 3 at once — owner: *"you can use more than
one subagent for the rest of the week... still max of like 3"* (under 20% of the week used). Back to one after.

## 2026-10-14: plan upgraded — 3-4 at once normally; 4-5 until Sunday 2026-10-18 (supersedes one-at-a-time)
Owner: *"i just upgraded the plan so we are good for basically 4 subagents maybe 5"*, then: *"that isnt perm, its
probably like 3 or 4 perm, but right noww the weekly limit reset, and it resets again sunday so we have a lot of them to
burn"*. Binding limit is now the desktop,
not tokens: gates, UI suites, big probes and TF2 loads serialize under run-exclusive and the TF2/Ghidra leases
([[parallel-agents-lock-the-gate-too]]). Prefer agents whose long phase is reading/coding.

## 2026-10-18: new week, 2 agents at most (supersedes the 4-5 raise)
Owner: *"it is a new week so we run like 2 agents at most, the reviews are taking a bunch of tokens so this new sub didnt get me more concerrant agents, it got me better and more reviews though"*. Cap 2 implementers at once; reviews (Sonnet/Fable, API-credit Fable) keep their budget.

## 2026-10-18: new subsystem = fresh agent
Owner, after the D217 agent (~900k context) was handed unrelated frame-cost work: *"shouldnt those have been put on new agents for fresh context?"*. Same subsystem follow-ups stay with the agent that knows it; a new subsystem gets a fresh agent once a slot frees. **Why:** a big context is paid every turn and helps nothing outside its subsystem.

## 2026-10-04: corpus/specimen plumbing is sonnet or haiku
Owner, on an opus agent launched to add gcor specimens for new protocols: *"adding specimens was probably a sonnet or
haiku job, not a opus job"*. Picking files, manifest entries, era tests, doc updates = bounded-task (sonnet). Opus only
if a decode actually fails and needs diagnosis — spawn that separately when it happens.
**Repeated 2026-10-29, same mistake:** the gcor swap's retirement (renaming ~50 test references to the new specimens,
moving floors, manifest entries) went to an OPUS implementer for nearly an hour (447k tokens). Owner: *"the gcor demos
should have been another sonet or even haiku, iit was purely a mechaniical thing"*. **How to apply:** split a mixed task.
Rename/re-point sweeps, floors, manifest and doc edits = `bounded-task` (sonnet, haiku if fully self-contained); opus only
for the part with judgment (a synthetic fixture that must encode what the engine does, a failure that needs diagnosis),
launched as its own agent. Read this entry BEFORE choosing the model, not after.

## 2026-10-29: subagent effort is low, medium at most
Owner: *"use low effort or medium effort at the most of subagents too"*. Set `effort` to `low` (mechanical, bounded) or
`medium` (a judgment task) on EVERY Agent call; never `high`/`xhigh`/`max` for a subagent. The Agent tool's `effort`
parameter exists for exactly this and is only set when the owner asks, which he now has. Enforced since by
`~/.claude/hooks/block-expensive-subagents.ps1` (backup in `.claude/hooks/global/`).

## 2026-10-29: docs passes go to a free model first (broadened the same day to everything mechanical)
Owner: *"i wwant to use the free models everywhere we can so my paid models can do real work that matters"*. The scope
is now in `~/.claude/CLAUDE.md` "Free models" (docs, commit messages, mechanical sweeps, floors and manifest
bookkeeping, log summaries, an extra diff read; never a gate, a merge call, a decoder/parity judgment or a measured number).
Owner: *"docs can be done on a ffree model though"*. The release docs pass (README, RELEASE-NOTES, user guide, `--help`)
goes to the free OpenCode models in the global order (`deepseek-v4.1-flash-free`, then `glm-5.3-flash-free`, ...), not to
a Sonnet. Supersedes "never opus / sonnet bounded-task" in CLAUDE.md's public-beta paragraph for the writing itself.
**Still the lead's job:** read the output and check every figure against its measurement record before it lands (the
docs are public). A free model is never a review gate. **Fallback order (owner, 2026-10-29: *"if that one fails allback
to codexx and antoigravity first"*):** the free OpenCode models (the 2026-10-29 review bench used most of that allowance),
then Codex, then Antigravity, and only then a low-effort Sonnet `bounded-task`. Codex was found not good enough for
REVIEW (owner: *"codex doesnt seem to cut it"*); writing docs is a different job. These run through `delegate_task`
(`orchestrator_capabilities` lists the providers); the Agent-tool hook does not cover them.

## 2026-10-05: small bugs go to sonnet
Owner: *"The small bugs can probably use sonnet though, if they are actually small."* A bug whose fix is known and
local (one method, a constant, a wiring edit, a missing test) = bounded-task (sonnet). Opus stays for engine reads with
an unknown answer, disassembly, multi-file design. Follow-up rounds on an opus branch that are small fixes still go to
the same opus agent (context already paid).

## 2026-10-04: user-facing docs follow as a separate sonnet agent
Owner, on D210 (folder picker/cfg): *"the agent that fixes this will need to update docs or you will need to run doc
subagents for them, the second is probably the better idea"*. Implementer (opus) does code + tests + RISKS/findings
in-commit; afterwards a bounded-task (sonnet) updates README, RELEASE-NOTES requirements/known gaps, user guide,
--help prose. Still one subagent at a time — docs agent runs after the implementer finishes.
