---
name: a-d3d9-reference-can-carry-a-driver-halo
description: "TF2's D3D9 screenshots here carry a screen-space dark halo around geometry that no cvar names and -vulkan removes; compare world brightness against a -vulkan capture (B669)."
metadata:
  type: feedback
---

D3D9 TF2 on this machine: dark halo around depth edges, about 10 screen pixels wide at any distance, survives `mat_fullbright 2`, no cvar or shader names it. `-vulkan` removes it; concrete, pipes and close-up world stay to the digit. *Interpolated:* driver ambient occlusion; the setting was not read.

Cost: a wall behind pipes read 126.5 where the engine draws 153.5, ours looked 10% bright, and a session went into decals, fog and LOD.

**How to apply:** `mcp__tf2__launch` with `extraArguments: -vulkan` for a brightness reference. Tell: the ratio of ours over TF2 embosses every edge. Compare with static props on and off: a real light is equal away from the prop, this differs near it. Related: [[a-picture-is-assertable]], [[instrument-bugs-outnumber-decoder-bugs]].
