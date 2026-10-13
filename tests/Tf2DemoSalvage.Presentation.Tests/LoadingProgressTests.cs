namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>The loading display's words and bar [no engine counterpart: the overlay is this viewer's].</summary>
public sealed class LoadingProgressTests
{
    [Test]
    public void Label_WithAFraction_IsTheStageAnEllipsisAndAWholePercentage()
    {
        LoadingProgress.Label("Reading the map", 0.256).ShouldBe("Reading the map… 26%");
        LoadingProgress.Label("Reading the map", 0d).ShouldBe("Reading the map… 0%");
    }

    [Test]
    public void Label_WithNoFraction_IsTheStageAndAnEllipsis()
    {
        LoadingProgress.Label("Decoding the demo", null).ShouldBe("Decoding the demo…");
    }

    [Test]
    public void Position_OfAFraction_IsThatShareOfTheBarRounded()
    {
        LoadingProgress.Position(0.2566, 1000).ShouldBe(257);
        LoadingProgress.Position(1d, 1000).ShouldBe(1000);
    }

    [Test]
    public void Position_OutsideTheBarOrUnknown_IsHeldToItsEnds()
    {
        LoadingProgress.Position(1.5, 1000).ShouldBe(1000);
        LoadingProgress.Position(-0.5, 1000).ShouldBe(0);
        LoadingProgress.Position(null, 1000).ShouldBe(0);
    }
}
