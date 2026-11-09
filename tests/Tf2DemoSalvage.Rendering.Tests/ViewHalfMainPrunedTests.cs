namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// The main view's pruned list (B711, D251): its translucent pass walks pruned places, and each reaches the world list by
/// <c>m_pActualLeafIndex</c> (`viewrender.cpp:4307`, `:3574`).
/// </summary>
public sealed class ViewHalfMainPrunedTests
{
    [Test]
    public void MainActual_WithAPrunedList_IsThePlacesIndexInTheWorldList()
    {
        ViewHalf half = new() { MainPruned = ([7, 3], [1, 4]) };

        half.MainActual(0).ShouldBe(1);
        half.MainActual(1).ShouldBe(4);
    }

    [Test]
    public void MainActual_PastThePrunedList_IsThePlaceAsItIs()
    {
        ViewHalf half = new() { MainPruned = ([7, 3], [1, 4]) };

        half.MainActual(5).ShouldBe(5, "a stale entity place reaches no other leaf's runs");
    }

    [Test]
    public void MainActual_UnprunedAndAfterTheWaterPlanIsCleared_IsThePlaceItself()
    {
        ViewHalf half = new() { MainPruned = ([7, 3], [1, 4]) };

        half.ClearWater();

        half.MainActual(1).ShouldBe(1);
    }
}
