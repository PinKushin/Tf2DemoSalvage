---
name: close-what-you-launched
description: Tidy up viewer launches you started yourself, and treat an exit nobody asked for as a possible silent crash. Written deliberately loose.
metadata:
  type: feedback
---

**Close a viewer you launched on your own initiative, once it has answered whatever you launched it
for.** Owner, 2026-08-21: *"if you boot if yourself shut it down when your done"*. Nothing breaks if
you leave it — it does hold the exclusive lock and lock the build DLLs, so the next build fails with
`MSB3027 … The file is locked by: "tf2demoview"`, which names a copy step rather than the cause.

**When the owner asked for the launch, leave it running**, and if it then disappears they closed it.
That is ordinary and needs no comment. The owner's framing: the distinction *"only matters because
you would get confused at me shutting it down myself"*.

**An exit neither of you asked for is worth looking at.** *"it is a signal if it closes on you, as a
crash, when you or I didnt tell it to. Some crashes dont actually full crash and they just exit a
program, those can be a pita to debug."* The tail of `viewer-*.log` in
`%LOCALAPPDATA%\Tf2DemoSalvage\` is the cheapest first look — a tidy ending versus one that stops
mid-sentence. Not common, on the owner's read, given the analyzers this project runs.

**On how much of this to write down, which the owner raised directly:**

> *"getting all the nuance of situations can be a pita to try to write down, you can basically never
> write down every single scenario a 'rule' will ever be put into, so the best thing to do IMO, is to
> basically always hedge, id rather understate things than overstate them most of the time"*

So this entry is deliberately loose, and that is the standing preference rather than a property of
this one note. **Prefer the understated version.** A rule written as an absolute gets applied
confidently to the case it was never meant for, and the confidence is the damage — the reader cannot
tell an inferred edge from a stated one. This entry has already been rewritten three times in an
hour, each time because it claimed more than the owner meant.

Related, and the opposite error: [[a-launch-notification-is-not-an-exit]] — a background task
reporting "completed" describes the wrapper, not the app. `Get-Process` before concluding either way.
