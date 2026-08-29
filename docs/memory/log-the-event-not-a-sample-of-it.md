---
name: log-the-event-not-a-sample-of-it
description: Four ways a diagnostic log went blind in one session — sampling a brief event, testing a proxy, counting instead of identifying, and comparing two different moments.
metadata:
  type: project
---

**A log added to catch a defect can be blind to that defect, and it looks exactly like evidence of
absence.** Four ways this happened in one evening, 2026-08-28, hunting a viewmodel that vanished for
a few frames at a time (B222). Every one produced a confident wrong conclusion first.

**1. A sampled log cannot see an event shorter than the sample.** The viewmodel pass was reported
once a second, and the weapon vanished for ~60 ms. The log read `drawing 2` on both sides of every
gap, so the gap never happened as far as it was concerned. The owner: *"why tf are you using a
second timeout jesus fning christ that is stupid"* — and he was right; the caveat had already been
written down and then reasoned straight past.

**The fix is not a shorter interval, it is a TRANSITION log.** Fire when the value changes, and a
two-frame flicker writes a line while a stable state writes nothing. That removes the blind spot and
the spam problem at once, and it is the right shape for any state whose *changes* are the signal.
Cap it per subject so a pathological flip cannot fill the disk.

**2. A degeneracy test that checks the wrong degeneracies.** Bones were tested for all-zero and
non-finite. A matrix that is finite and non-zero with a **zero-length basis row** collapses its
vertices just as thoroughly, and the first version reported `0 degenerate` on a confirmed
reproduction. The failure mode was named in that method's own doc comment and not implemented.

**3. A COUNT is not an IDENTITY.** The pass instrument reported `2 drawn` throughout, and was
correct: two props were drawn. The second had silently become a *different weapon*. `MomentScene`
warns about this in as many words eight lines from the code being instrumented — *"the count says
two and cannot say two of WHAT"* — and the instrument was written anyway. **Log what a thing IS, not
how many there are.**

**4. Comparing two measurements from different moments.** A weapon's bone centre at 17:37:37 was
compared against the arms' centre logged at 17:37:34 and the 4,400-unit difference reported as a
misplaced viewmodel. The player had run across the map in between. Measured against the arms **in
the same frame** it was correct, and the owner confirmed by looking. A difference between two
timestamps is not a difference between two things.

**5. A threshold chosen without asking what size of effect must survive it.** A weapon's distance
from the hands was bucketed as "over 100 units = AWAY". A viewmodel lives within tens of units of the
eye, so a weapon fifty units out — completely off screen — reported `with the hands`. The fix was
5-unit buckets, and the fault was choosing the resolution before asking what had to be visible
through it.

**6. A global report budget where the subject is per-model.** A "what did this model submit"
line was capped at 200 reports total, in a method that runs for every one of 250 props a frame. Two
animating props — `cappoint_hologram` and `demo_scotchbonnet` — spent the entire budget within
seconds, and the viewmodel, the only subject it was built for, never reported once. A whole
reproduction was wasted. **A shared budget is a resolution choice too**, and the noisiest subject
spends it.

**Why:** each of these is the "wrong instrument" failure from the testing section applied to a LOG
rather than to a test, and a log has no red state to warn you — silence reads as "the thing did not
happen". So the question to ask of any diagnostic before believing it is the same one:
**is there a state where broken and working produce different output, and can this line see it?**

**How to apply:** prefer transition logs to sampled ones. Name the subject, never just count it.
Test the property that makes the symptom, not one adjacent to it. And when a proxy is unavoidable —
bone-translation span standing in for "does the model have size" — say so where it is read, because
the next person to look will otherwise treat the number as the finding.

Related: [[instrument-bugs-outnumber-decoder-bugs]], [[measure-the-output-not-the-capability]],
[[a-threshold-instrument-cannot-see-a-sum]], [[logs-are-the-debugger]].
