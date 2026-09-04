---
name: measure-a-new-feature-on-a-second-demo
description: A feature measured only on the demo it was built against reads as finished — run the probe on one era specimen too, because a wire name that differs by era makes a complete feature half of one.
metadata:
  type: feedback
---

**Before calling a decode feature done, run its probe on a demo from another era.** One extra
command, and it is the difference between "159 of 159" and finding that the same feature scores zero
on everything older.

Measured (B319). A corpse's orientation is reached through the player it was, since `DT_TFRagdoll`
sends no angles. Built and measured against a 2026 SourceTV demo: **159 of 159**. Run on `z1800`,
which is a committed era specimen: **0 of 407**. The two demos name the field differently, and the
values are not even the same kind:

```
DT_TFRagdoll.m_hPlayer        24587, 174093, 311301, …   packed ehandles, need Resolve
DT_TFRagdoll.m_iPlayerIndex   2, 3, 4, 5, 6, …           entity indices, used as they stand
```

**`m_iPlayerIndex` is not in the published SDK at all** — not even kept as a `RECVINFO_NAME` alias.
Reading the SDK could not have found it, and no amount of care about the modern name would have
helped. Only a demo carries it. That is the whole premise of decoding off the embedded schema
([[the-demo-dates-its-own-fields]], [[wire-names-are-strings]]).

**The tell that a feature is under-measured is a perfect score on one file.** 159 of 159 reads as
proof and is really a statement about one recording. Two demos of different eras cost one more
command, and this project keeps era specimens precisely so that command exists.

**And prefer a committed era specimen over another modern demo.** The gcor corpus is one file per
era for this reason; picking a second 2026 match would have scored 159 of 159 again and taught
nothing.

Related: [[era-axis-is-measured]], [[check-backwards-compat-on-old-demos]],
[[an-empty-search-needs-a-control]], [[record-both-points-of-view]].
