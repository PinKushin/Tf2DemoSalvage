// Shared namespaces for every test file in this project, declared once (global standards:
// explicit usings, centralized in GlobalUsings.cs rather than MSBuild <Using> items).
//
// Note: System is deliberately NOT global here. The SDK-generated AssemblyInfo.cs emits its own
// `using System;`, which collides with a global using of the same namespace and fails the build
// under Zero Warnings as CS8933. Test files declare `using System;` explicitly instead.
global using NUnit.Framework;
global using Shouldly;
