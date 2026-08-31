---
name: viewer-screenshots-are-f5
description: The viewer's screenshot key is F5, TF2's own — it was F12 until B214; captures land beside viewer.log in %LOCALAPPDATA%\Tf2DemoSalvage.
metadata:
  type: reference
---

**F5 takes a screenshot in the viewer**, and captures land beside the log in
`%LOCALAPPDATA%\Tf2DemoSalvage` as `shot-<yyyyMMdd-HHmmss-fff>.png`.

**It was F12 and this memory said so, which was wrong for weeks.** B214 moved it to **F5 for Valve
parity** — F5 is TF2's own `screenshot` key, so a config that rebinds screenshots moves the viewer's
with it. F12 was a bad choice twice over: TF2 gives it to replay tips and Steam's overlay takes it as
well. The owner, correcting it: *"f5 is the shortcut for ss's and the menu item saying f12 is
actually wrong"* … *"we cahnged it for valve parity"*.

**Why:** the key is not a fact about this project, it is TF2's. That is the whole of D101 — every
control comes from the binding table, and the table follows the game.

**How to apply:** never name a key from memory. Ask `KeyBindings.KeyFor(ViewerAction.Screenshot)`,
or read the defaults in `ViewerAction.cs`. A key written down anywhere — a memory, a menu label, a
message to the owner — is a copy that will go stale the next time parity moves one, and B239 is what
that costs: the menu printed "F12" long after the key was F5, because a LABEL is not a registration
and nothing breaks when it lies.

See [[a-default-is-not-a-constant]] and [[no-hardcoded-controls-ever]].
