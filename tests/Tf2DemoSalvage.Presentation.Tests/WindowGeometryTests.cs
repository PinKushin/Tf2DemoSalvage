namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>Reading a window size and position out of two strings.</summary>
/// <remarks>
/// **This was `MainForm.ApplyGeometryOverride`** (B208), which parsed `800x600` and `10,20` inline
/// and could only be exercised by launching a window with the environment set.
/// </remarks>
public sealed class WindowGeometryTests
{
    [Test]
    public void Size_WithAWidthAndHeight_ReadsBoth()
    {
        WindowGeometry.Size("1280x720").ShouldBe((1280, 720));
    }

    [Test]
    public void Size_WithSpacesAround_StillReads()
    {
        // A value pasted from a shell or a launcher carries whitespace, and rejecting it would be a
        // refusal nobody could see the reason for.
        WindowGeometry.Size(" 1280 x 720 ").ShouldBe((1280, 720));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("1280")]
    [TestCase("1280x720x60")]
    [TestCase("widexhigh")]
    [TestCase("0x720")]
    [TestCase("-100x720")]
    public void Size_WithAnythingUnusable_IsNull(string? text)
    {
        // **Null rather than a default, and rejecting zero and negatives is the point of the pair at
        // the end.** A window sized 0x720 is invisible, and one sized -100 is a crash in some
        // window managers — so "unusable" has to include values that parse perfectly well.
        WindowGeometry.Size(text).ShouldBeNull();
    }

    [Test]
    public void Position_WithXAndY_ReadsBoth()
    {
        WindowGeometry.Position("10,20").ShouldBe((10, 20));
    }

    [Test]
    public void Position_Negative_IsAccepted()
    {
        // **The asymmetry with `Size`, and it is deliberate.** A negative POSITION is ordinary — it
        // is how a window lands on a monitor left of or above the primary one. A negative SIZE is
        // not. Treating the two the same would either break multi-monitor placement or allow an
        // invisible window.
        WindowGeometry.Position("-1920,-200").ShouldBe((-1920, -200));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("10")]
    [TestCase("10,20,30")]
    [TestCase("left,top")]
    public void Position_WithAnythingUnusable_IsNull(string? text)
    {
        WindowGeometry.Position(text).ShouldBeNull();
    }
}
