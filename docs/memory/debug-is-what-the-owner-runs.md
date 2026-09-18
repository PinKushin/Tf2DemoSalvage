---
name: debug-is-what-the-owner-runs
description: The owner runs the Debug viewer more often than not; performance claims must hold there, and Debug is optimized (D176).
metadata:
  type: feedback
---

The owner mostly looks at the **Debug** build of the viewer, not Release. Measuring only Release and
reporting fps from it answered a question the owner was not asking.

**Why:** *"debug needs to be fast too ya know, its what i see more often than not"* (2026-09-15). The
unoptimized JIT made Debug 3-5 fps on f12 where Release ran 150+, because corpse physics is Vector3
arithmetic left as calls and it steps by tick, so a slow frame owes more steps next frame.

**How to apply:** `Directory.Build.props` sets `<Optimize>true</Optimize>` for every configuration
(D176) — do not set it back in a project. Measure with the configuration the owner runs; when quoting a frame
rate, say which build it came from. For a profile, `dotnet-trace collect --profile
dotnet-sampled-thread-time --format Speedscope -- <exe> <args>` works under `run-exclusive.ps1`.
Related: [[the-f12-demo-is-the-parity-reference]], [[the-fat-column-is-the-subtracted-one]].
