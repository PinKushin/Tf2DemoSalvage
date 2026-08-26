---
name: set-the-opposite-state-first
description: A test whose precondition already equals its assertion cannot fail; set the opposite state first.
metadata:
  type: reference
---

**If the state you assert is already true before the call, the test holds against a method with an
empty body.** Two of these were found in one hour on 2026-08-26, in code written months apart, and
both were in tests whose own comments named the exact failure they could not detect.

**The older one, in `DemoSystemsTests`:**

```csharp
spectator.Eyes = null;          // precondition
moment.Viewmodels = null;
Systems(...).Open(timeline: null, ...);
spectator.Eyes.ShouldBeNull();  // assertion — identical to the precondition
moment.Viewmodels.ShouldBeNull();
```

Its comment says *"easy to write as an `if` that only assigns when there is something to assign"* —
which is precisely the defect it was blind to. Setting each source to a **stub** first, so `Open`
has something to clear, made it fail against exactly that `if`. Confirmed by writing the bad shape
and watching it go red.

**The newer one, in a test written the same hour**, asserted that two `Show` calls did not accumulate
into a shared buffer. Clearing is the *source's* job, and the stub cleared — so the count it asserted
was decided by the stub, not by the code under test. Its subject was the stub. Replaced with the
claim the design actually encodes: the same buffer instance reaches the source each time.

**Why:** null is the default of every reference field, and empty is the default of every collection,
so "assert it is null/empty afterwards" is the single easiest unfalsifiable test to write by
accident. It reads as rigour. It is the mirror of [[an-empty-search-needs-a-control]] — an absence
observed without establishing that presence was possible.

**How to apply:** before writing `ShouldBeNull`, `ShouldBeEmpty`, or `ShouldBe(0)`, ask what the
value is *immediately before* the call. If it is the same, the test is measuring nothing — put the
opposite there first. And when the assertion's value comes from a stub rather than from the subject,
the stub is what you are testing.

**This is route 3 of the four in the global standards** — "no control", one subject so *affected
everything* and *affected the target* are indistinguishable — and it generalises past deletion tests
to any assertion on a default value.

Related: [[instrument-bugs-outnumber-decoder-bugs]], [[a-test-can-outlive-its-design]].
