---
name: a-default-is-not-a-constant
description: Reading a ConVar's default out of the SDK and writing it in as a literal is a correct number and a wrong conclusion; check for the cvar and its clamp.
metadata:
  type: feedback
---

**A number found in Valve's source is often a DEFAULT, not a constant.** `viewmodel_fov` was read
from the SDK, its clamp of 54–70 was quoted in a comment, and then 54 was written into the code as a
fixed value. The owner corrected it: it is a setting the player changes, and TF2 reads a separate
`viewmodel_fov_demo` during playback — which is this project's only case.

**Why it matters here specifically:** the whole project decodes off whatever schema a file provides
rather than hardcoding an era's layout ([[fallbacks-do-not-make-guesses-safe]]). Baking a client
setting into the renderer is the same mistake at a different layer, and it fails silently — the
picture looks right, it just is not what the recording would have shown.

**How to apply:** when a number comes out of the SDK, grep for it as a `ConVar` before using it. If
it is one, the questions are what the demo records about it (usually nothing), whether a
playback-specific variant exists, and what the clamp is. Then decide deliberately: follow the
default, expose it, or read it from a config. Write down which, because "54" in the source tells a
later reader nothing about whether it was chosen.

Related: [[a-running-client-caches-its-config]] is the same subject from the testing side, and
[[read-the-sdk-for-the-whole-mechanism]] is the general form — finding the declaration is the easy
half.
