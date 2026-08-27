global using NUnit.Framework;
global using Shouldly;

// **Render and Scene, because that is what these tests are of** — and declaring them here is what
// `Viewer3D.Tests` has been doing since D59 split those assemblies out. Its own `GlobalUsings.cs`
// says why, and names this move as the one that was always coming:
//
//   "The split on 2026-08-22 (D59) moved their subjects into their own assembly without moving the
//    tests, so this keeps every existing file compiling while the suite follows its subjects across
//    in its own change."
//
// This is that change (B184).
//
// **Worth knowing if you go measuring**: because these are global, a file testing `Render` or
// `Scene` carries no `using` line naming either. Grepping imports to work out what a test exercises
// therefore reports zero for both and is wrong — the dependency is real and declared here.
global using Tf2DemoSalvage.Render;
global using Tf2DemoSalvage.Scene;
