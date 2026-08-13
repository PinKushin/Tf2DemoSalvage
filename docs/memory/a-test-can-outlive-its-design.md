---
name: a-test-can-outlive-its-design
description: A passing test can encode a design you deleted, then fail against the improvement that replaced it
metadata:
  type: project
---

**A test that asserts HOW something works will fail when you make it work better.** The
full-screen UI test demanded that entering full screen REBUILD the world, which was correct
while the camera projection was baked into every vertex. When the camera became a matrix,
`MainForm` began returning on `_device.HasWorld` before it ever reached the build — the world
is built once per map and a resize uploads sixty-four bytes.

The test then waited twenty seconds for a rebuild that correctly never comes, and failed. The
failure read as *"full screen is broken"* while the window in front of the tester was plainly
full screen. Two sessions treated it as a focus problem or an app problem before the owner said
outright: the world does not rebuild any more, the camera matrix means it only builds once.

**Why it is worth recording:** the test had passed for weeks, so nothing marked it as suspect,
and its failure pointed at the wrong subsystem. A test encoding an old design does not announce
itself — it fails at the moment you improve the thing it covers, which is the moment you are
least inclined to believe the test is at fault.

**How to apply:** when a long-passing test fails right after a change that was meant to *remove*
work, check whether the test asserts the removed work before debugging the application. Prefer
asserting the outcome (the camera was repointed; the textures were not re-uploaded) over the
mechanism (the world was rebuilt). Related: [[decode-must-be-total]],
[[measure-the-output-not-the-capability]].
