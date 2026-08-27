---
name: the-viewer-suite-wants-the-gpu
description: Viewer3D.Tests creates real D3D devices, so an app in exclusive full screen can abort the whole run; the count floor is what catches it.
metadata:
  type: project
---

**`Tf2DemoSalvage.Viewer3D.Tests` creates real Direct3D devices**, unlike every other non-UI suite
here. It takes no desktop and needs no `run-exclusive.ps1`, but it does want a GPU nobody else has
taken exclusively.

**Seen once, 2026-08-20: `Test Run Aborted` at 192 of 512.** Another application was in exclusive full
screen at the time — a video player — which is a known way for device creation to fail. It did not
reproduce in four clean runs afterwards and nothing was captured from the crash, so this is a
plausible cause rather than an established one. Recorded because the alternative is a future session
chasing it as a decoder defect.

**What made it visible at all is the count floor.** The run printed a summary and stopped; only
comparing 192 against the project's known 512 said anything was wrong. That is the whole argument for
exact floors rather than comfortable ones — see [[read-the-trx-total-not-the-console]].

**How to apply:** before treating a viewer-suite abort as a code defect, ask what else was on the
screen, then re-run. A genuine crash reproduces; this did not. And do not confuse it with flake in the
ordinary sense — [[ui-tests-run-every-time]] and the standing rule that flake is a defect still hold
for anything that fails rather than aborts.

**One trap noticed while chasing it, and it is much bigger than first written:** the console logger
and the `.trx` disagree on the total, and **not by a constant**. First seen as one on this suite
(console 511, `<Counters total="512">`), and measured on 2026-08-21 as **eleven** on
`Content.Tests` — console `Total: 601`, trx `total="612"`. The gap grows with the number of skipped
and `[Explicit]` tests, which the two count differently.

`assert-test-count.sh` reads the trx, so **the floor is right and the console line is not**.

**How to apply:** never compare a console `Total:` against a gate floor. It reads as tests having
vanished, and it cost a real detour — 601 against a floor of 606 looked like eleven tests lost while
six had just been ADDED. Read the counters instead:

```bash
dotnet test tests/<project> --logger "trx;LogFileName=check.trx" && \
  find . -name check.trx | head -1 | xargs grep -oE '<Counters[^/]*/>'
```

Same family as [[logs-are-the-debugger]]: two instruments reporting "total" and meaning
different things, with nothing on either line saying which.
