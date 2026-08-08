---
name: tf2demosalvage-build-gates
description: Build gates in Tf2DemoSalvage are strict enough to reject TDD stubs and global using System
metadata: 
  node_type: memory
  type: project
  originSessionId: 9b3a8b35-1dc8-47b0-a320-73b01288f10c
  modified: 2026-08-07T21:00:10.350Z
---

Two consequences of Tf2DemoSalvage's build configuration that bite before any real code
is written (established 2026-08-07 when the solution was scaffolded):

1. **TDD placeholder types do not compile.** With `TreatWarningsAsErrors` plus
   `AnalysisMode=All` plus SonarAnalyzer, a stub whose members all throw
   `NotImplementedException` fails on CA1065 (exception from a property getter) and S2325
   (member does not use instance state). So the red step for a brand-new type is a compile
   failure, not a failing assertion — write the tests first, then implement directly, and
   do not waste a cycle trying to stage a stub.

2. **`global using System;` is banned in test projects.** The SDK emits its own
   `using System;` into the generated `AssemblyInfo.cs`, which collides with the global
   and fails as CS8933 under warnings-as-errors. `GlobalUsings.cs` carries Shouldly and
   Xunit only; `System` is declared per-file.

**Why:** Both look like configuration mistakes when hit cold, and the natural "fix" for
either (loosening the analyzer settings, or deleting the per-file usings) undoes a gate
the project deliberately wants. The reasons are recorded in comments at both sites.

**How to apply:** Don't relax `TreatWarningsAsErrors` or `AnalysisMode` to make a stub
compile. Don't add `System` to `GlobalUsings.cs`. Related:
[[stryker-targetframework-must-be-in-csproj]].
