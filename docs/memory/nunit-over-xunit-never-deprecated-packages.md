---
name: nunit-over-xunit-never-deprecated-packages
description: NUnit is the default test framework for new .NET projects; a deprecated package version is a build warning and therefore a Zero Warnings violation.
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 9b3a8b35-1dc8-47b0-a320-73b01288f10c
  modified: 2026-08-09T18:03:30.935Z
---

**NUnit, not xUnit, for new .NET projects.** Owner-stated 2026-08-09. The deciding reason is
documentation quality, not features. Tf2DemoSalvage is on xUnit and is being upgraded to v3
rather than migrated, because migrating ~4400 test attributes is its own decision.

**A deprecated package version is a build warning, so it violates Zero Warnings.** xUnit v2 is
deprecated and NuGet says so. This was missed here and caught in another repo the same week, when
a different assistant tried to pull xUnit into WinAppDriver work.

**Why:** two specific mistakes caused it, and both look like diligence at the time.

1. **Scaffolding a new test project by copying an existing `.csproj`.** That is how xUnit v2
   reached a second project in this repo — the new project built cleanly, so nothing objected.
   Copying propagates whatever was pinned last time, deprecations included.
2. **Inferring the framework from the code in front of you.** `[Fact]` everywhere reads as "this
   repo uses xUnit", which is an observation about the past, not about intent. The owner's
   standard was NUnit the whole time.

**How to apply:** before adding any test project, check the current standard rather than the
sibling project. Before pinning any package version, check it is not deprecated. If a repo's
existing framework differs from the standard, say so explicitly instead of quietly matching it —
the cost of staying silent is a full migration later, and the owner priced that at a day.

Related: [[tf2demosalvage-build-gates]] for how strict this repo's analyzers already are.
