---
name: a-test-can-outlive-its-design
description: A passing test can encode a design you deleted, then fail against the improvement that replaced it
metadata:
  type: project
---

**A test that asserts HOW something works will fail when you make it work better.** The full-screen
UI test demanded that entering full screen REBUILD the world — correct while the camera projection
was baked into every vertex. Once the camera became a matrix, `MainForm` returned on `_device.HasWorld`
before reaching the build (the world builds once per map; a resize just uploads 64 bytes). The test
then waited 20s for a rebuild that correctly never comes, and failed — read as "full screen is
broken" while the window in front of the tester was plainly full screen. Two sessions blamed focus or
the app before the owner said: the world doesn't rebuild any more.

**Why worth recording:** the test had passed for weeks, so nothing marked it suspect, and its failure
pointed at the wrong subsystem — least likely moment to suspect the test.

**How to apply:** when a long-passing test fails right after a change meant to *remove* work, check
whether the test asserts the removed work before debugging the application. Prefer asserting the
outcome over the mechanism. Related: [[decode-must-be-total]], [[measure-the-output-not-the-capability]].
