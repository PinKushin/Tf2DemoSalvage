namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>How much of a map's height is hidden, and what to say about it.</summary>
/// <remarks>
/// **This was arithmetic and two status strings inside `MainForm.ProcessCmdKey`** (B188, D90).
/// </remarks>
public sealed class HeightCutTests
{
    [Test]
    public void Deeper_FromNothing_CutsOneStep()
    {
        HeightCut.None.Deeper().Fraction.ShouldBe(HeightCut.Step, 0.0001);
    }

    [Test]
    public void Deeper_RepeatedlyPastTheLimit_StopsAtTheDeepest()
    {
        // **Never 1.0, and that is the point of the limit rather than a rounding.** A cut of the
        // whole map leaves nothing drawn, which is indistinguishable from a map that failed to load.
        HeightCut cut = HeightCut.None;

        for (int press = 0; press < 200; press++)
        {
            cut = cut.Deeper();
        }

        cut.Fraction.ShouldBe(HeightCut.Deepest);
        cut.Fraction.ShouldBeLessThan(1f, "a fully cut map is an empty screen");
    }

    [Test]
    public void Shallower_FromNothing_StaysAtNothing()
    {
        // The lower clamp. Without it the fraction goes negative and the renderer is asked to keep
        // geometry below the map, which is not a state anything else handles.
        HeightCut.None.Shallower().Fraction.ShouldBe(0f);
    }

    [Test]
    public void Shallower_AfterCutting_UndoesOneStep()
    {
        // **The control for the clamp above.** Without a case that actually moves, "stays at zero"
        // would be satisfied by a `Shallower` that did nothing at all.
        HeightCut.None.Deeper().Deeper().Shallower().Fraction.ShouldBe(HeightCut.Step, 0.0001);
    }

    [Test]
    public void Describe_WhenNothingIsCut_SaysTheWholeMapIsShown()
    {
        HeightCut.None.Describe().ShouldBe("Showing the whole map.");
    }

    [Test]
    public void Describe_WhenCut_NamesWhatIsLeftAndHowToUndoIt()
    {
        // **The remaining fraction rather than the cut one**, because that is what the user can
        // see. Telling someone "5% cut" while 95% is on screen makes them look for the 5%.
        string described = HeightCut.None.Deeper().Describe();

        described.ShouldContain("98");
        described.ShouldContain("Page Up", Case.Insensitive);
    }
}
