---
name: an-impossibility-claim-expires
description: "X cannot be known" left in code is never re-read when later work establishes X; two docs disagreed here for weeks.
metadata:
  type: project
---

`ViewerSettings.DefaultFrameRateLimit` said `fps_max`'s default *"could not be recovered from the
binary"*. `docs/findings/37-the-engines-demo-vocabulary.md` had already recovered it — **400**, with
flags — by reconstructing the pooled numeric block instead of reading string adjacency. Both were
written in this repository, weeks apart, and the two sat contradicting each other until a parity
audit on 2026-08-26 read them on the same day.

**Why this shape survives when a wrong positive claim does not.** A positive claim is load-bearing:
something calls it, a test pins it, changing the code forces a re-read. An impossibility claim is
inert. Nothing depends on it, so nothing drags it back into view — and the later finding that
disproves it has no reason to look for it, because it is off doing the thing the claim said could
not be done.

The reasoning is usually *correct*, which is what makes it stick. The string-pool argument here is
exactly right: the pooled layout really does put `engine_no_focus_sleep` beside `fps_max`'s help
text, and defaults really are single-character literals shared by hundreds of registrations. What
does not follow is "therefore unknowable" — that promotes a fact about **one instrument** into a
fact about the world.

**How to apply:** when a finding establishes something, grep for prior claims that it cannot be
established — `cannot`, `impossible`, `no way to`, `not recoverable` — and retire them in the same
commit. And when writing one, scope it to the instrument: *"not recoverable from the string pool"*,
never *"not recoverable"*. Related: [[nothing-is-closed]], [[shipped-data-is-a-source]],
[[the-denominator-is-already-written-down]], [[a-filed-design-choice-may-not-be-one]].
