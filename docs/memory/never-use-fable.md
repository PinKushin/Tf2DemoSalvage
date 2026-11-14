---
name: never-use-fable
description: "Fable is a reviewer (queue first, local reviewer subagent too); never implementer, lead or workflow, except one owner-allowed implementer A/B trial (2026-11-13) behind an expiring lock."
metadata:
  node_type: memory
  type: user
  originSessionId: 124d1a9c-39d8-407f-871a-adb7c8b92a98
  modified: 2026-11-14T05:57:25.637Z
---

**The rule now (corrected 2026-11-06):** Fable is a reviewer. **The credits queue first** ([[credits-queue]], model
`claude-fable-5-1`) while it has money; **Fable is ALSO allowed locally** as a reviewer subagent (`model: fable` on
`caveman:cavecrew-reviewer`). Never as implementer, lead, advisor, or inside workflows.

Owner, 2026-11-06, asked who the reviews had been going to (Sonnet, all session): *"fable can be used locally, i never meant
for it to never be used locally, i onyl wanted the Q to be used first ... the Q ... theres only 15 bucks or so on it, so its
only good for like 3 more reviews ... start the Q and run a fable review over the bigger changes, and start using fable
locally for review too, because i never said it wasnt allowed, i only said to use the Q first"*. The earlier "never pass model
fable to Agent" reading below was wrong: it was an ordering (queue first), not a ban.

**How to apply:** a large or structural merge review → a Fable job on the queue while money lasts (note the balance after
each); otherwise, and for reviews in general, a local Fable `caveman:cavecrew-reviewer`. Small diffs may still use Sonnet or
Opus when the hook allows. Never Fable for implementation, except the trial below.

**2026-11-13, one implementer trial allowed.** Owner: *"i actually have a weekly and fable reset too, so next week, after
sunday, we can run hard and even use some fable for implementation maybe, but it really depends on the token cost of a fable
implementor"*, then *"btw you can actually run that fable test we need now before the new week it will be fine"*. Run as a
same-task A/B (Fable and Opus implementers, one brief, separate worktrees), comparing tokens, review findings and rework.
Implementation stays Opus by default until the owner decides on the result.

**Trial result (2026-11-14, one task, so an anecdote, not a rate).** The brief was B724's `SND_SPEAKER`/-2 leftovers.

| | Opus | Fable |
|---|---|---|
| Implementation | 283k tokens, 141 tools, 41 min | 403k tokens, 195 tools, 47 min |
| Scope | the 2 rules asked, plus 2 found (dry-music sum, weapon +2 dB) | the 2 rules asked, plus 3 found (Opus's two and the looping-speaker refusal); also covered the soundscape |
| Review, real bugs | 1 (missed the soundscape) | 1 (refusal placed before the pick) |
| Engine claims | not checked | checked by its reviewer in Ghidra, all correct |
| Rework round | not run | 578k tokens for 8 items, plus a `main` merge |

- **Total:** Fable 981k; Opus 283k plus an unrun rework round. On tokens per merged result, Fable was about 2-3× Opus.
- **What Fable's rework added:** it found the weapon boost's real gate (soundlevel < 256), corrected `IsDry`'s provenance, measured 352k sounds (none carries `SND_SPEAKER`), and dropped the unreachable -2 branch under D180.
- **Merged:** Fable's branch, `6bf0c24ad`. The Opus branch `fix/b724-speaker-flag-opus` is kept unmerged as the record.

History, kept because the rule changed:

- **2026-10-09, the original ban.** Owner: "fable is far too token heavy".
- **2026-10-14, trial as merge reviewer** — 145k tokens, found a real invariant break; owner: *"yep keep it"*.
- **2026-10-18 / 2026-11-01, via the queue** — the owner wanted the queue used for it; this was read as "never locally", which
  the 2026-11-06 correction above overrules.
