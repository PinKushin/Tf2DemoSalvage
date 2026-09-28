---
name: mutation-box-gets-maps-never-demos
description: The mutation box may be given real maps with their VPKs, never real demos; prefer synthetic fixtures first
metadata:
  type: project
---

Owner: *"we can give the mut runs a real map if we need to, we have the space, but what we cant do is
give it real demos, they take too long to mut test. The map parsing is bound, while demos are not."*
Then: *"giving it a real map, means giving it all the vpks... but i might have been underestimating
the work needed."*

**Why:** a mutation run executes the suite once per mutant. Map parsing has fixed cost; demo decode
grows with length. A real map also needs the game's VPKs.

**Never the Source SDK (D184):** the program reads game data at runtime, so those tests are worth
mutating; the SDK is only a reference, and its conformance tests aren't mutation-tested.

**How to apply:** when box-side code shows NoCoverage because a test needs the install, write a
synthetic fixture first ([[mutation-score-is-not-the-goal]], D38). Provisioning maps with VPKs is
allowed but real work; never demos.
