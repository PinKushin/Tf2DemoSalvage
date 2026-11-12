---
name: implement-inline-on-opus-or-fable
description: The lead implements inline when it runs on Opus or Fable; no implementer subagents. Reviews stay cross-model subagents. Replaces D196's routing of implementation to a subagent.
metadata:
  type: feedback
---

Implement inline when the lead is an Opus or Fable model. Do not spawn `implementer` agents. Review agents stay, on a model that differs from the lead's.

**Why:** owner, 2026-11-04: *"5.5 is MORE efficient than 5, i literally saw less single agent usage with 5.5, but started using 2 agents at a time. So its all the cache writes from the sub-model imps, because they stupidly dont share a cache, yep go back to implementing inline as long as you are opus or fable model"*. Transcripts agree on the cache writes: 50-60M a week in W33-W36, 452M in W42, 223M in W44, mostly implementers. Reviewer agents were about 4% of W44.

**Refined 2026-11-12 (D256):** "run subagents if it speeds you up" means PARALLEL work only. Owner, after a session where
the lead handed every follow-up to implementers and sat idle waiting on gates: *"you are stupid sometimes, the subagents
were not suppose to be for work you could do because you are free, they are for things that can be done in parallel,
sometimes"*, then *"leave them running, obviously, but if theres something you can do then do it"*. Rule: the lead
implements the next item itself whenever it is free (a gate running is free time); an implementer is only for a SECOND
item beside the one the lead is doing, and only when the owner has invited parallelism. Running agents are left running.

**How to apply:** a lead on Sonnet or below still delegates implementation to Opus (D196). Bounded doc and mechanical work may still go to cheap or free models. Decision record: D245. Spend rule: [[one-subagent-and-prefer-cheap-models]] is superseded on this point.
