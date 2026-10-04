---
name: a-check-is-a-test-or-it-is-not-in
description: "Never put a check in teardown/setup to dodge ordering - a check is a counted test or it does not go in."
metadata:
  type: feedback
---

2026-10-04: proposed moving the playback gate into `ViewerSession`'s `[OneTimeTearDown]` so it would run last on the
shared viewer. Owner: *"teardown is a huge no, it either shows as a test or it doesnt go in, that is horrible context
for you to have to remember and use, it is completely against what teardowwn is suppose to be for"*.

**How to apply:**
- Setup/teardown only establish and release state; they never assert a behavior.
- A check that needs an order is a real test; solve the order another way or keep it separate.
- Playback in the UI suite uses z1800, the demo the shared viewer already has open.

Related: [[nunit-shared-fixture-is-the-standard]], [[a-pass-must-establish-its-own-state]].
