// Shared namespaces for every file in this project, declared once (global standards: explicit
// usings, centralized in GlobalUsings.cs rather than MSBuild <Using> items).
//
// Tf2DemoSalvage.Scene is global because this project is now the THIN half of the split made on
// 2026-08-22 (D59): eleven files, every one of which draws or hosts something the scene layer
// produced. Repeating the same using in all eleven would be the scattering this convention exists
// to avoid, and adding a twelfth file that does not need it is not a case worth optimising for.
//
// Note: System is deliberately NOT global here, for the same reason the test projects give — the
// SDK-generated AssemblyInfo.cs emits its own `using System;`, which collides with a global using
// of the same namespace and fails the build under Zero Warnings as CS8933.
global using Tf2DemoSalvage.Audio;
global using Tf2DemoSalvage.Presentation;
global using Tf2DemoSalvage.Render;
global using Tf2DemoSalvage.Scene;
