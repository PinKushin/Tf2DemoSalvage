---
name: one-subagent-and-prefer-cheap-models
description: "Subagent model selection: opus for full implementation work (D196), sonnet for a bounded known task, haiku only for the sabotage verifier."
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

## 2026-10-04: corpus/specimen plumbing is sonnet or haiku
Owner, on an opus agent launched to add gcor specimens for new protocols: *"adding specimens was probably a sonnet or
haiku job, not a opus job"*. Picking files, manifest entries, era tests, doc updates = bounded-task (sonnet). Opus only
if a decode actually fails and needs diagnosis — spawn that separately when it happens.

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
