---
name: ask-what-the-request-is-for
description: a request names a mechanism; the owner's goal behind it decides what to build — "splash screen on boot" meant "let me see boot time"
metadata:
  type: feedback
---

Asked for *"a splash screen that pops up immediately on boot"*, I built a demo-loading overlay, because the demo decode was the
wait I knew about. The owner: *"you didnt actually do what i asked at all … I asked for a splash screen for when the APPLICATION
IS LOADING"*, and then the real goal: *"I just want to see how long our boot time is, and be able to notice if it becomes crazy
long."* (D182, 2026-09-18.) The overlay was kept — he wanted it too — but it was not the request.

**Why:** I substituted the wait I had measured for the one he described, and never asked what the splash was FOR. The answer
changed the deliverable: a number he can watch (logged and in the status bar), with the splash as a side dish.

**How to apply:** when a request names a UI mechanism, restate which moment it covers in his words ("the app itself, before any
demo") before building; if the purpose is not stated, the cheap question is "what do you want to notice?". [[name-the-reading-you-picked]]
