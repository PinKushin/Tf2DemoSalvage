---
name: interp-is-the-watchers-setting
description: cl_interp/cl_interp_ratio/cl_updaterate come from the viewer config (D190); a hardcoded 0.1 s fired effects 6 ticks late
metadata:
  type: project
  originSessionId: 124d1a9c-39d8-407f-871a-adb7c8b92a98
  modified: 2026-09-23T18:48:07.325Z
---

Interpolation is the WATCHER's setting, never a constant (D190). `ViewerSettings.Interp` is bounded by
the recording's `sv_client_min/max_interp_ratio` and `sv_min/maxupdaterate` (10/66, from engine.dll);
sets the temp-entity `FireTick` and every `ScenePropTrack.InterpolationDelayTicks`.

**Why:** owner runs 0/1/66 (competitive values) — TF2 fired his scattergun impact 1 tick after
arrival, we fired 7 ticks late using default values instead.

**How to apply:** when comparing timing against TF2, use the owner's interp, not TF2's defaults. See
[[never-assume-valve-is-broken]].
