// **Strictly serial, and this is not a performance choice.** UI tests drive a single desktop:
// two running at once send synthesized input at screen coordinates into whichever window happens
// to be in front, so a parallel run does not fail, it clicks into someone else's application.
//
// This is the UI half of the repo's two-tier rule
// (docs/memory/nunit-shared-fixture-is-the-standard.md). The fixture also keeps ONE launched
// application across its tests, which is the other half: relaunching the viewer per test would
// pay the startup cost every time and is what InstancePerTestCase would force.
[assembly: NonParallelizable]
[assembly: LevelOfParallelism(1)]

// **The window says which test is driving it.** Thirty-one tests share one viewer, so somebody
// watching a defect appear on screen cannot tell which test produced it. Asked for by the owner
// while watching this suite; see TestNameInTitleAttribute.
[assembly: Tf2DemoSalvage.Viewer3D.UiTests.TestNameInTitle]
