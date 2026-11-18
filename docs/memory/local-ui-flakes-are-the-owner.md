---
name: local-ui-flakes-are-the-owner
description: "An intermittent local UI test failure is the owner at the computer (D262): rerun once, never soak, never file."
metadata:
  type: feedback
---

2026-11-17: the lead soaked the full UI suite three runs at a time on the shared desktop to chase B773/B632/B558. Owner:
*"why would you do that, we dont need a fuicking soak, the failures are me, deal with them"* (D262).

The rule, his words: *"if you have a single failure, and im home or you think i might be home, and it clears the very
next test, it was probably me"*.

**Why:** the UI suite drives the desktop the owner works on; the lock cannot stop his own hand. A failure that does not
reproduce idle is him, and a soak spends 40 minutes of his desktop to learn it.

**How to apply:**
- A local phase 2 failing one UI test the change cannot reach: rerun phase 2 once; green is the result. File nothing.
- Never loop or soak the UI suite to chase an intermittent failure.
- A long desktop run of any kind waits until the owner says the computer is free.
- CI failures are not covered: CI has no owner at its desktop.

Related: [[take-the-desktop-lock-dont-defer]], [[long-desktop-runs-go-while-the-owner-is-away]].
