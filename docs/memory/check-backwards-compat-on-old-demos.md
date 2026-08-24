---
name: check-backwards-compat-on-old-demos
description: Reach for an era demo the moment old files are in play; the corpus spans 2007 onward and almost none of it is driven by the UI suite.
metadata:
  type: feedback
---

Owner, after the doubled-viewmodel bug: *"you know the demos have to be backwards compat to 07, we
should probably check the 07 demo after this... thats why we should looks towards backwards compat
immediately whenever we are using an old demo."*

**Why:** the bug was a modern assumption — that a first-person weapon is always hands plus a
separate gun — applied to a 2011 recording where it was one combined `v_` model. It survived because
nothing exercises the era specimens end to end. The owner named the gap precisely: *"we dont ui test
every demo we have, and i dont look at every one before we commit"*, so a rendering regression on an
old file is invisible to both the suite and the eye.

**How to apply:** when a change touches how something is drawn or resolved, ask what the oldest
supported demo does with it, and open one. The era axis is measured — protocols 11, 14, 15, 16 and
24, with matched POV/STV pairs — so the specimen exists. Related:
[[a-viewmodel-is-one-model-or-two]], [[era-axis-is-measured]], [[record-both-points-of-view]],
[[the-demo-dates-its-own-fields]].

**A constraint on verifying era behaviour:** the period clients have **no internet connection**, so
a modern item cannot be loaded in them to compare. Whether a modern-only symptom also occurs on an
era demo often cannot be checked in the original client at all, and the answer has to come from the
shipped data and the SDK instead.
