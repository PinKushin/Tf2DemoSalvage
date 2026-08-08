---
name: stryker-targetframework-must-be-in-csproj
description: Stryker.NET project analysis fails if TargetFramework is set in Directory.Build.props instead of the .csproj
metadata: 
  node_type: memory
  type: project
  originSessionId: 9b3a8b35-1dc8-47b0-a320-73b01288f10c
  modified: 2026-08-07T21:00:02.458Z
---

In Tf2DemoSalvage, `TargetFramework` must be declared in each `.csproj`, never in
`Directory.Build.props`. If it lives in the props file, `dotnet stryker` aborts with
"Failed to analyze project builds. Stryker cannot continue." Verified 2026-08-07 with
Stryker 4.16.0 and .NET SDK 10.0.302; net9.0 fails the same way, so it is not a
.NET 10 support gap.

**Why:** Stryker's Buildalyzer step produces zero analyzer results and logs only
"No analyzer results to log" — no MSBuild error, nothing naming the TFM. The cause was
found by bisecting `Directory.Build.props` property by property. Everything else in that
file (`TreatWarningsAsErrors`, `AnalysisMode=All`, the `SonarAnalyzer.CSharp`
PackageReference) is fine to centralize; only the TFM breaks it.

**How to apply:** If a new project is added and `dotnet stryker` starts failing to
analyze, check whether the TFM drifted into the props file before investigating anything
else. Both `Directory.Build.props` and each `.csproj` carry a comment saying the TFM must
stay put — do not "tidy" it back into the props file. See [[tf2demosalvage-build-gates]].
