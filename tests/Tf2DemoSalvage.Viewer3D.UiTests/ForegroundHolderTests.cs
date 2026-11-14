using Tf2DemoSalvage.Viewer3D.UiTests;

// Not in the `UiTests` namespace: its `ViewerSession` fixture launches a viewer for every test beneath it.
namespace Tf2DemoSalvage.Viewer3D.GoldenTests;

/// <summary>The words a refused click or key uses for who holds the foreground (B230). No viewer is launched.</summary>
public sealed class ForegroundHolderTests
{
    [Test]
    public void Describe_AWindowAndItsProcess_NamesThemAndTheTitle()
    {
        ForegroundWindow.Describe(new ForegroundWindow(0x1A2B, 4242), "steam", "Friends List")
            .ShouldBe("process 4242 (steam), window 0x1A2B \"Friends List\"");
    }

    [Test]
    public void Describe_AProcessThatCannotBeRead_SaysSoRatherThanNamingOne()
    {
        ForegroundWindow.Describe(new ForegroundWindow(0x1A2B, 0), name: null, string.Empty)
            .ShouldBe("process 0 (unreadable or gone), window 0x1A2B \"\"");
    }

    [Test]
    public void Describe_NoForegroundWindow_SaysSoRatherThanNamingTheIdleProcess()
    {
        ForegroundWindow.Describe(default, name: null, string.Empty).ShouldBe("no foreground window");
    }

    [Test]
    public void ProcessName_AReadingWithNoProcess_IsNullRatherThanTheIdleProcess()
    {
        // pid 0 is what a failed owner lookup leaves; asked as a pid, it would answer "Idle".
        new ForegroundWindow(0x1A2B, 0).ProcessName().ShouldBeNull();
    }
}
