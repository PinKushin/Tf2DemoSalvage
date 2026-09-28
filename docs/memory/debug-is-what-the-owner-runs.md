---
name: debug-is-what-the-owner-runs
description: The owner runs the Debug viewer more often than not; performance claims must hold there, and Debug is optimized (D176).
metadata:
  type: feedback
---

The owner mostly runs the **Debug** build, not Release — measuring only Release and reporting its fps
answers a question he isn't asking.

**Why:** *"debug needs to be fast too ya know, its what i see more often than not"* (2026-09-15).
Unoptimized JIT made Debug 3-5 fps on f12 where Release ran 150+, since corpse physics is Vector3
arithmetic left as calls stepping per tick — a slow frame owes more steps next frame.

**How to apply:** `Directory.Build.props` sets `<Optimize>true</Optimize>` for every configuration
(D176) — don't override in a project. Measure with the configuration he runs; say which build a
frame rate came from. Profile with `dotnet-trace collect --profile dotnet-sampled-thread-time
--format Speedscope -- <exe> <args>` under `run-exclusive.ps1`. Related:
[[the-f12-demo-is-the-parity-reference]], [[the-fat-column-is-the-subtracted-one]].
