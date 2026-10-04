---
name: water-lod-lives-in-a-material-proxy
description: The Water shader never learns the view chose cheap water; the WaterLOD proxy hands the view's distances to the material and the cheap pass covers. $forceexpensive defaults to 1 on PC.
metadata:
  type: project
---

**Asking "how does the shader know the frame is cheap?" sends you into the closed engine for nothing.** The
shader decides its passes from the material alone; the per-frame LOD arrives through the `WaterLOD` material proxy
(`WaterLODMaterialProxy.cpp:61`), which writes the view's `water_lod_control` distances (or the view ctor's 0/0.1)
into `$cheapwater*distance` every bind, and the cheap pass alpha-covers the expensive one.

**Second trap:** `DetermineWaterRenderInfo` reads `$forceexpensive` with `IsDefined()`, but `water.cpp`'s
`SHADER_INIT_PARAMS` has already set it to 1 on PC — read material defaults from the shader's init, not the VMT.

**How to apply:** when a decision reads a material var, check the shader's `SHADER_INIT_PARAMS` and the VMT's
`Proxies` block before concluding where a value comes from. See `docs/findings/71-the-water-views.md`.
