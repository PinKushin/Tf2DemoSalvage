---
name: per-pass-gpu-needs-a-fixed-window-condition
description: "Per-pass GPU figures (Clear, Sky3D, World) depend on whether any part of the viewer window is visible; hold the window condition fixed across runs and classify each run by Clear GPU."
metadata:
  node_type: memory
  type: feedback
  modified: 2026-10-30T00:00:00.000Z
---

Any part of the viewer window visible on screen puts the run in state 2; nothing visible (minimized, off screen, fully
covered) is state 1, and Clear, Sky3D and World per-pass GPU differ between them while the frame mean does not.

**How to apply:** hold the window condition fixed across every run of a per-pass GPU comparison, and classify each run by
Clear GPU (above 1.0 is state 1, below 0.3 is state 2). Run all off screen with `TF2VIEW_WINDOW_POS=-3000,100`: the owner
minimizes or closes viewer windows that get in his way, so a visible window is the one that gets disturbed. Evidence and
numbers: `docs/RISKS.md` B597 and `docs/measurements/2026-10-30-gpu-state-selector.md`.
