# libtf2dem (placeholder — not currently used)

Originally planned as a native C decode core; superseded (see `../../docs/DECISIONS.md` D2). Decision now: pure C# for Phase 1/2, no native code by default. `Tf2DemoSalvage.Core` (`../../managed/Tf2DemoSalvage.Core`) is the actual decode engine.

This folder stays as a placeholder in case Phase 3 (3D viewer) profiling ever shows a specific piece — most likely a per-frame render-loop step, not demo decoding — genuinely needs native code after `unsafe` C# has been tried and measured. Do not start implementation here otherwise.

If that trigger ever fires: default to **C** (MSBuild/vcxproj, same solution as everything else). **Zig** is an open long-shot alternative, not C++ — it exports a plain C ABI natively (same P/Invoke story as C), with real memory-safety improvements (bounds-checked slices, no implicit null, explicit error unions) and none of C++'s naming-convention/template/smart-pointer baggage. Trade-off: needs its own build step (`build.zig`) outside the main `.sln`, rather than living in it directly like a C vcxproj would. Decide between the two only when the trigger actually fires — see `docs/DECISIONS.md` D2.
