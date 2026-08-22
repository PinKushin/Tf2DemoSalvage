global using NUnit.Framework;
global using Shouldly;

// Tf2DemoSalvage.Scene, because most of this suite tests the scene layer rather than the renderer:
// of its 570 tests, 506 never touch Direct3D. The split on 2026-08-22 (D59) moved their subjects
// into their own assembly without moving the tests, so this keeps every existing file compiling
// while the suite follows its subjects across in its own change.
global using Tf2DemoSalvage.Scene;
