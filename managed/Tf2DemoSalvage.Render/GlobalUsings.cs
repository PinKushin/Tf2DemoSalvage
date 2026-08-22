// Shared namespaces for every file in this project, declared once (global standards: explicit
// usings, centralized in GlobalUsings.cs rather than MSBuild <Using> items).
//
// Tf2DemoSalvage.Scene is global because that is what this project draws: every file here consumes
// the geometry, materials and camera the scene layer produced. Seven files, all of them.
//
// Note: System is deliberately NOT global — the SDK-generated AssemblyInfo.cs emits its own
// `using System;`, which collides with a global using of the same namespace and fails the build
// under Zero Warnings as CS8933.
global using Tf2DemoSalvage.Scene;
