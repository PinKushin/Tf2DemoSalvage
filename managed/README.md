# managed/ (C#)

Everything lives here — the decode engine included (see `../docs/DECISIONS.md` D2: no native C core, pure C# for Phase 1/2).

- `Tf2DemoSalvage.Core` — the actual demo decode engine (container parsing, SendTable-driven entity delta decode, string tables) plus the friendly object model (ticks, entities, game events, chat). This is the Phase 1 target.
- `Tf2DemoSalvage.Cli` — batch parsing; the Quake-style trace, the summary dump, and JSON Lines.
- `Tf2DemoSalvage.Viewer2D` — Phase 2: top-down scrub viewer.
- `Tf2DemoSalvage.Viewer3D` — Phase 3: primitive-geometry (v0.1) then fidelity 3D viewer. Do not start before Phase 1/2 are solid.

No projects/solution file created yet — that's part of the Phase 1 implementation task, not this scaffolding pass. Wire up Stryker.NET and SonarLint/Roslyn analyzers (`docs/DECISIONS.md` D6) from the very first project, not later.
