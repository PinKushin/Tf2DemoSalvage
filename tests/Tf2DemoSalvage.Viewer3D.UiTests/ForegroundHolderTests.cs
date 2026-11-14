using System;

using Tf2DemoSalvage.Viewer3D.UiTests;

// Not in the `UiTests` namespace: its `ViewerSession` fixture launches a viewer for every test beneath it.
namespace Tf2DemoSalvage.Viewer3D.GoldenTests;

/// <summary>Who holds the foreground, named in a refused click's or key's message (B230). No viewer is launched.</summary>
public sealed class ForegroundHolderTests
{
    [Test]
    public void DescribeHolder_AWindow_NamesItsProcessHandleAndTitle()
    {
        ViewerApplication.DescribeHolder(new IntPtr(0x1A2B), 4242, "steam", "Friends List")
            .ShouldBe("process 4242 (steam), window 0x1A2B \"Friends List\"");
    }

    [Test]
    public void DescribeHolder_NoForegroundWindow_SaysSoRatherThanNamingTheIdleProcess()
    {
        ViewerApplication.DescribeHolder(IntPtr.Zero, 0, string.Empty, string.Empty).ShouldBe("no foreground window");
    }
}
