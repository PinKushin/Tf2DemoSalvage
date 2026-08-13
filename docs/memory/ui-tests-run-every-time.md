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
