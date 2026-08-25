global using NUnit.Framework;
global using Shouldly;

// The scene layer itself, because every test here is of one of its types. Mirrors what
// Viewer3D.Tests declares, which is where these tests lived until their subjects' own project
// existed (B184).
global using Tf2DemoSalvage.Scene;
