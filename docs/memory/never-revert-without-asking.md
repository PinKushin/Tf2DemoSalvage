---
name: never-revert-without-asking
description: Reverting exploratory work is fine; discarding changes that were asked for, work, and should already have been committed is not.
metadata:
  type: feedback
---

**The rule is about WHICH changes, not about reverting.** Owner, after an unprompted `git restore`
threw away an evening's work: *"if you havent changed much, then revert away, but when we have
changes that should already be their own commits, didnt break, and were directly asked for because
they are valve standards, you dont throw them away."*

A change meeting all three is not yours to discard: (1) should already be its own commit, (2) didn't
break anything, (3) was directly asked for.

**The real failure was earlier than the revert** — those changes should have been committed when
made (`CLAUDE.md` already says commit after any bounded chunk). "Should I revert this?" is usually
"this should have been a commit an hour ago."

**How to apply:**
- Commit work in progress as `wip:` with the failure stated in the message.
- A wrong-looking picture is a reason to ASK, not undo — say what looks wrong, let the owner decide.
- `git restore`/`git checkout --` discard unrecoverable, unreviewed work.

**Watch the direction:** both reverts here moved AWAY from Valve's values, which is a defect by
definition (D46) — a depth buffer format and an overlay clip direction were both our own wrong
choices, mistaken for a bad Valve value. When an experiment with a Valve value looks wrong, test
"what else of ours diverges and is distorting it" before concluding the value is wrong.

Related: [[a-filed-design-choice-may-not-be-one]], [[name-the-reading-you-picked]],
[[logs-are-the-debugger]].
