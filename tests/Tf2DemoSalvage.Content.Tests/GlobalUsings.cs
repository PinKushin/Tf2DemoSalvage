// Shared namespaces for every test file in this project, declared once (global standards:
// explicit usings, centralized in GlobalUsings.cs rather than MSBuild <Using> items).
//
// Note: System is deliberately NOT global here. The SDK-generated AssemblyInfo.cs emits its own
// `using System;`, which collides with a global using of the same namespace and fails the build
// under Zero Warnings as CS8933. Test files declare `using System;` explicitly instead.
global using Shouldly;
global using NUnit.Framework;

// **SdkReference, because this project reads what the game and the SDK ship more than it does
// anything else.** Eighty-three of its files name `GameInstall`, `SourceSdk`, `CStruct` or `Skip`;
// declaring it here is the same call `Rendering.Tests` made for `Render` and `Scene` below its own
// note, and it is what makes the shared install gate cheaper to reach for than a hand-written copy.
// That mattered: a shared locator nothing forces you to use is a suggestion, and the count of files
// carrying their own copy grew from seventy-three to ninety-four while it sat there (D109).
//
// **Worth knowing if you go measuring**: a file using `GameInstall` therefore carries no `using`
// line naming it, so grepping imports to find which tests read the install reports far too few.
global using Tf2DemoSalvage.SdkReference;
