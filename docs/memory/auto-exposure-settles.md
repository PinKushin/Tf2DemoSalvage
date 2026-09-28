---
name: auto-exposure-settles
description: D192 — auto-exposure stays on (TF2 default); a comparison capture just waits for it to settle; HUDs come before post-processing
metadata:
  type: feedback
---

When TF2's HDR post-processing is built, auto-exposure is ON with no off switch for captures — a
capture waits a few seconds to settle, then two shots of the same camera agree.

**Why:** owner, 2026-09-25: *"the auto exposer settles after a few seconds so, no need to turn it
off, just make sure the scene has settled, which ive honestly never seen it not settle"*. Raising "a
still depends on where the camera was" as a reason to disable it was a non-problem. Same message:
*"id rather start huds before worryying about more lighting"*.

**How to apply:** for a golden shot, let the scene settle before capturing; don't propose disabling a
TF2 default for convenience. Post-processing (bloom, exposure) is queued behind the HUD (D192).
