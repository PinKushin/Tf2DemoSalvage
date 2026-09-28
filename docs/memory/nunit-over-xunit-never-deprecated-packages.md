---
name: nunit-over-xunit-never-deprecated-packages
description: NUnit is the default test framework for new .NET projects; a deprecated package version is a build warning and therefore a Zero Warnings violation.
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 9b3a8b35-1dc8-47b0-a320-73b01288f10c
  modified: 2026-09-09T03:54:26.524Z
---

**NUnit, not xUnit, for new .NET projects** — deciding reason is documentation quality, not features.
Tf2DemoSalvage is on xUnit, being upgraded to v3 rather than migrated (migrating ~4400 attributes is
its own decision).

**A deprecated package is a build warning, violating Zero Warnings** — xUnit v2 is deprecated and
NuGet says so; missed here, caught in a sibling repo the same week.

**Two mistakes cause this, both look like diligence:**
1. Scaffolding a new project by copying an existing `.csproj` propagates whatever was pinned last
   time.
2. Inferring the framework from surrounding code (`[Fact]` everywhere) is an observation about the
   past, not intent.

**How to apply:** check the current standard, not the sibling project, before adding a test project.
Check for deprecation before pinning any version. If a repo's existing framework differs from the
standard, say so explicitly instead of quietly matching it.

Related: [[tests-before-codecs]].
