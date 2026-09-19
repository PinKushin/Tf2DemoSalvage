---
name: mutation-box-gets-maps-never-demos
description: The mutation box may be given real maps with their VPKs, never real demos; prefer synthetic fixtures first
metadata:
  type: project
---

The owner, 2026-09-19: *"we can give the mut runs a real map if we need to, we have the space, but what we cant do is give it real demos, they take too long to mut test. The map parsing is bound, while demos are not."* Then: *"giving it a real map, means giving it all the vpks, like materials textures and the rest too, which we still do have the space for, but i might have been underestimating the work needed to do it"*.

**Why:** a mutation run executes the suite once per mutant. Map parsing has a fixed cost, but demo decode grows with the demo's length. A real map is not only the `.bsp` file. It also needs the game's VPKs.

**How to apply:** when box-side code shows as NoCoverage because a test needs the install, write a synthetic fixture first ([[mutation-score-is-not-the-goal]], D38). Provisioning maps with their VPKs on the box is allowed but is real work. Never put demos there.
