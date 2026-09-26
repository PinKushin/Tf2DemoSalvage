---
name: auto-exposure-settles
description: D192 — auto-exposure stays on (TF2 default); a comparison capture just waits for it to settle; HUDs come before post-processing
metadata:
  type: feedback
---

When TF2's HDR post-processing is built, auto-exposure is ON with no off switch for captures. A capture — TF2's or ours —
waits a few seconds for the exposure to settle, and then two shots of the same camera agree.

**Why:** the owner, 2026-09-25: *"the auto exposer settles after a few seconds so, no need to turn it off, just make sure
the scene has settled, which ive honestly never seen it not settle"*. Raising "a still depends on where the camera was"
as a reason to disable it was a non-problem. Same message: *"id rather start huds before worryying about more lighting"*.

**How to apply:** for a golden shot, let the scene sit before capturing; do not propose disabling a TF2 default to make
comparison easier. Post-processing (bloom, exposure) is queued behind the HUD (D192).
