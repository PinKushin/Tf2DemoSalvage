---
name: ui-tests-run-every-time
description: The UI suite runs on every change, not just UI ones, and any UI addition gets a UI test.
metadata:
  type: feedback
---

**Run the UI suite every time**, alongside unit and integration, not only when the change looks
UI-shaped. And **anything added to the UI gets a UI test added with it.**

Owner's instruction, given after a session where the UI suite was run twice in several hours of
viewer work.

**Why:** the viewer is the product here, and the defects that reached the owner this session were
all in it — a borderless mode that behaved as exclusive, full screen at one frame a second, props
that drew black. None were caught by the unit suites, which stayed green throughout. The existing
full-screen UI test also opened no demo, so it had no map and could not distinguish the fast build
from the slow one; that was fixed by giving it a demo, which is the same lesson in miniature.

**How to apply:** `run-exclusive.ps1 dotnet test <UI project>` — it takes the desktop, so it holds
the machine-wide lock, and the owner should be told before it starts. When adding a control, a
mode, a menu item, or a rendering path, add the UI assertion in the same change rather than
promising it later. Where the property genuinely cannot be observed from the automation tree — z
order against another application, for instance — say so in the commit instead of substituting a
check on the flag, which only restates the diff. Related: [[tests-before-codecs]],
[[nunit-shared-fixture-is-the-standard]].

## The phase-scoped exception has expired, 2026-08-26

There used to be a companion entry saying the UI suite was optional *while the UI was small*. It is
not small any more — twenty tests, and they have earned their keep: the F11 collision that silently
broke full screen for days (B165), three wiring regressions that shipped at 620/620 green (B193), and
a per-second diagnostic that a log-LEVEL change silenced while its unit tests stayed green. The
exception is closed and this entry is the whole rule.

**The worked example it carried is worth keeping**, because it is the reason a UI test can be worse
than none. `Click_TheCycleTargetButton_ReachesTheSpectatorCode` counted the log line
`"following entity N"` to prove a click reached `CycleTarget`. That line is written only when the
target search SUCCEEDS; the other branch writes `"nobody else to follow at this tick"`, and **both
prove the wiring**. So the test asserted "the click reached the handler" by requiring "and it found
somebody" — a fact about the demo and the tick, not about the code. Once B171 required a target to be
alive and drawn, the solo POV era specimen the UI session opens legitimately produced no target, and
the test went red against a viewer the owner was watching work correctly. His verdict: *"that seems
like a stupid test for a pov demo or a demo with a single player, it doesnt actually check
anything"*.

Fixed by counting the `[spectate]` area instead of either message — which also sharpened the negative
control, since in the free camera `CycleTarget` returns before logging anything, so the count proves
the handler never RAN rather than merely that it found nobody.
