---
name: builds-leave-the-compiler-server-running
description: every dotnet build/test spawns VBCSCompiler (~0.5-1.5 GB) that outlives it; set UseSharedCompilation=false
metadata:
  type: feedback
---

Every `dotnet build`/`dotnet test` starts `VBCSCompiler` (Roslyn's compiler server), which stays resident after the
build: measured 1.4 GB and 5,600 CPU-seconds after a day of builds across worktrees. `MSBUILDDISABLENODEREUSE=1` does not
cover it, and `dotnet build-server shutdown` from SDK 11 did not stop one a build had just started (it had to be stopped
by PID).

**Why:** owner, 2026-11-25: "my pc is chugging, somthing is lkeft over", then "that compiler server thing is staying up".

**How to apply:** prefix every ad-hoc build and test with `UseSharedCompilation=false DOTNET_CLI_USE_MSBUILD_SERVER=0`
beside `MSBUILDDISABLENODEREUSE=1`. The MSBuild server (`MSBuild.exe /nodemode:8`) outlives a build and keeps the
environment it started with, so a later build's `UseSharedCompilation=false` never reaches it, and it starts
`/nodeReuse:true` worker nodes and `VBCSCompiler` anyway (measured 2026-11-25: eight nodes and a compiler server, ~1.3
GB, at 9:01). A reviewer delegate may build despite a "do not build" brief (not measured). After a gate, a review round or anything else
that may have started them, list `MSBuild`/`VBCSCompiler` by start time and stop this session's by PID. See
[[my-own-processes-are-mine]], [[delete-leftover-files-yourself]].
