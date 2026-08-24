// Shared namespaces for this project, declared once (global standards: explicit usings,
// centralized in GlobalUsings.cs rather than MSBuild <Using> items).
//
// Note: System is deliberately NOT global. The SDK-generated AssemblyInfo.cs emits its own
// `using System;`, which collides with a global using of the same namespace and fails the build
// under Zero Warnings as CS8933. Files declare `using System;` explicitly instead.
global using Tf2DemoSalvage.Content.Assets;
