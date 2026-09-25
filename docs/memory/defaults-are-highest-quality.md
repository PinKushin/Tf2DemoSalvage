---
name: defaults-are-highest-quality
description: "Graphics defaults are TF2's highest-quality settings; performance modes are downgrades from them, not the baseline."
metadata:
  node_type: memory
  type: feedback
  originSessionId: 124d1a9c-39d8-407f-871a-adb7c8b92a98
  modified: 2026-09-25T20:01:12.945Z
---

Default every graphics setting to TF2's highest-quality option (e.g. the dx90/high particle files, full texture size, max detail). Lower-quality performance settings are opt-in downgrades from that baseline.

**Why:** the owner, 2026-09-25: "our defaults should be as high as possible, like gfx quality wise, i figured thats basically how the games built anyway, you build the high quality version, and downgrade it for the low quality super performance mode."

**How to apply:** when a TF2 cvar or asset choice has quality tiers, parity means matching the high tier by default and reading the engine's high-setting behaviour. Check the viewer's current defaults against the high tier when touching a quality-dependent feature. A golden screenshot from TF2 must be taken at max settings to compare against.
