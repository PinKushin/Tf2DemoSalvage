---
name: ask-what-the-request-is-for
description: a request names a mechanism; the owner's goal behind it decides what to build — "splash screen on boot" meant "let me see boot time"
metadata:
  type: feedback
---

Asked for *"a splash screen that pops up immediately on boot"*, a demo-loading overlay was built —
the wait already known about. Owner: *"you didnt actually do what i asked at all... I asked for a
splash screen for when the APPLICATION IS LOADING"*, then the real goal: *"I just want to see how long
our boot time is, and be able to notice if it becomes crazy long."* (D182, 2026-09-18). The overlay
was kept (he wanted it too) but wasn't the request.

**Why:** the wait I'd measured was substituted for the one he described, without asking what the
splash was FOR. The real deliverable: a watchable number (logged + status bar), splash as a side dish.

**How to apply:** when a request names a UI mechanism, restate which moment it covers in his words
before building; if purpose is unstated, ask "what do you want to notice?".
[[name-the-reading-you-picked]]
