using Tf2DemoSalvage.Viewer3D.UiTests;

// Not in the `UiTests` namespace: its `ViewerSession` fixture launches a viewer for every test beneath it.
namespace Tf2DemoSalvage.Viewer3D.GoldenTests;

/// <summary>Who holds the foreground, named in a refused click's or key's message (B230). No viewer is launched.</summary>
public sealed class ForegroundHolderTests
{
    [Test]
    public void ForegroundHolder_Now_NamesTheProcessOwningTheForegroundWindow()
    {
        // The control is the second route the guard already trusts: the process id `HasFocus` compares against.
        string holder = ViewerApplication.ForegroundHolder();

        holder.ShouldStartWith($"process {ViewerApplication.ForegroundProcessId()} (");
        holder.ShouldContain(", window 0x");
    }
}
