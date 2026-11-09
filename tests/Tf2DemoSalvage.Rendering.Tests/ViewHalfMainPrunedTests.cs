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

    /// <remarks>
    /// **A cloak pass on screen reads the frame buffer** (`vertexlitgeneric_dx9.cpp:286-301`: `$cloakfactor` strictly inside
    /// (0, 1)), so a water view skips it (`viewrender.cpp:4614-4617`); fully cloaked or uncloaked it does not.
    /// </remarks>
    [TestCase(0.5f, true)]
    [TestCase(0f, false)]
    [TestCase(1f, false)]
    public void ReadsFrameBuffer_AtACloakFactor_IsTrueOnlyMidCloak(float factor, bool reads) =>
        Device3D.ReadsFrameBuffer(new ModelInstance("m.mdl", [], null, null, Cloak: new CloakBind(factor, 0f))).ShouldBe(reads);

    [Test]
    public void MainActual_UnprunedAndAfterTheWaterPlanIsCleared_IsThePlaceItself()
    {
        ViewHalf half = new() { MainPruned = ([7, 3], [1, 4]) };

        half.ClearWater();

        half.MainActual(1).ShouldBe(1);
    }
}
