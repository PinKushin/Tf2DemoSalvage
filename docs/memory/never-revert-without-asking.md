---
name: never-revert-without-asking
description: Reverting exploratory work is fine; discarding changes that were asked for, work, and should already have been committed is not.
metadata:
  type: feedback
---

**The rule is about which changes, not about reverting.** The owner's wording, after an unprompted
`git restore` threw away an evening's work:

> *"if you havent changed much, then revert away, but when we have changes that should already be
> their own commits, didnt break, and were directly asked for because they are valve standards, you
> dont throw them away"*

So: a scratch edit, a probe, something being tried out — revert it freely, no ceremony. But a change
that meets all three of these is not yours to discard:

1. it **should already be its own commit** — a bounded piece of work, finished enough to describe;
2. it **did not break anything** — the build is clean and the suite is green;
3. it was **directly asked for**, particularly where it brings this project in line with Valve.

**The real failure was earlier than the revert.** Those changes should have been committed when they
were made. `CLAUDE.md` already says to commit after any bounded chunk rather than batching — had that
happened, there would have been nothing to throw away and no decision to get wrong. **The fix for
"should I revert this?" is usually "this should have been a commit an hour ago".**

**How to apply:**

- **Commit work in progress as `wip:`** with the failure stated in the message. A commit saying "this
  is wrong and here is how" loses nothing and costs nothing.
- **A wrong-looking picture is a reason to ask, not to undo.** Say what looks wrong, say what would
  distinguish the causes, let the owner decide whether to press on or step back. Twice in one evening
  a screenshot I had read myself — once misread — triggered a revert nobody wanted.
- `git restore` and `git checkout --` discard work that was never reviewed and cannot be recovered.

**And watch the direction.** Both reverts moved *away* from Valve's values, and divergence from Valve
is a defect here by definition (D46). Tonight proved it twice: the depth buffer was `D32_FLOAT` where
the engine's is 24-bit fixed point, silently rescaling every depth constant in the renderer (D48);
and the overlay clipping ran the wrong way round until it was matched to what an overlay is (B134).
Both were our own choices, and both were the bug.

So when an experiment with a Valve value looks wrong, test **"what else of ours diverges and is
distorting it"** before concluding the value is wrong. The `-262144` decal bias was tried and
reverted on 2026-08-14 and again on 2026-08-21 — and on both occasions the depth format was still
wrong, so neither attempt ever tested it.

Related: [[a-filed-design-choice-may-not-be-one]], [[name-the-reading-you-picked]],
[[close-what-you-launched]].
