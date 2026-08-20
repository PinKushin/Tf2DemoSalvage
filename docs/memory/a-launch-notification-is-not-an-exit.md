---
name: a-launch-notification-is-not-an-exit
description: A background-task "completed" for a run-exclusive launch reports the wrapper, not the app; and a log read while it is still being written looks exactly like a crash.
metadata:
  type: feedback
---

**Launching the viewer through `run-exclusive.ps1` as a background task reports "completed" while the
application is still running.** The notification describes the wrapper, not the app. Measured twice on
2026-08-20: the task reported completion, and `Get-Process -Name tf2demoview` showed the process alive
minutes later, once for eighteen minutes with the owner using it.

**The compounding half is worse.** Reading the viewer's log after that notification shows a file that
stops mid-load — because the app is still writing it. One such log was 860 lines when read and **79 MB**
by the time the session ended. I concluded "it exited during load, you saw nothing", and the owner had
in fact been looking at it and taking screenshots the whole time. The owner corrected it: "i dont think
the earlier run exited", "there was a app up on my pc".

**Why:** a truncated log and a crashed process produce identical evidence at one instant. The
difference is only visible over time, or by asking the operating system.

**How to apply:** to know whether a launched application is still running, ask for the process —
`Get-Process -Name tf2demoview` — never the task notification and never the log's last line. If the
log must be the instrument, read it twice and compare, because growth is the signal. The same shape as
[[a-count-cannot-see-past-a-pruner]]: a single reading of a moving quantity is not a measurement of it.

And when reporting to a person who is sitting in front of the machine, remember they can see the screen
and you cannot — [[instrument-bugs-outnumber-decoder-bugs]]. Saying "it exited, you saw nothing" to
somebody looking at the running window spends credibility that the actual findings then need.
