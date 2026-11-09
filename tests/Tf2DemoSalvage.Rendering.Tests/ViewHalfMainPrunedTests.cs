using System.Collections.Generic;

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
        Device3D.ReadsFrameBuffer(new ModelInstance("m.mdl", [], null, null, Cloak: new CloakBind(factor, 0f)), materialsNeed: false)
            .ShouldBe(reads);

    /// <remarks>
    /// **A sheen pass or a frame-buffer shader answers true with no per-frame test** (`vertexlitgeneric_dx9.cpp:286-301`):
    /// uncloaked, the model is still skipped in a water view.
    /// </remarks>
    /// <remarks>
    /// **The reflection fades a detail model from its mirrored origin** (`BuildDetailObjectRenderLists( origin )` after
    /// `AdjustView`; `EnumerateLeaf`, `detailobjectsystem.cpp:2739-2775`). `cl_detaildist` 1200, `cl_detailfade` 400: at 1000
    /// units the squared distance is 1,000,000, inside the band from 640,000 to 1,440,000, so 440,000 × 255 / 800,000 = 140.
    /// The control, 100 units away, is inside the fade start: 255.
    /// </remarks>
    [TestCase(0f, 140)]
    [TestCase(900f, 255)]
    public void DetailAlphaFrom_AnEyeBelowTheModel_IsTheFadeAtThatDistance(float eyeZ, int expected) =>
        Device3D.DetailAlphaFrom(
            new ModelInstance("m.mdl", [], null, null, Origin: (0f, 0f, 1000f)), (0f, 0f, eyeZ), DetailFade.For(1200f, 400f))
            .ShouldBe(expected);

    /// <remarks>
    /// **The materials asked are the ones the entity shows** (`IsUsingFBTexture( model, skin, body, … )`): a batch's skinref is
    /// swapped by the skin, and a body part's alternative not selected by the body is not shown.
    /// </remarks>
    [Test]
    public void AnyShownMaterial_SwappedBySkinAndGatedByBody_AsksTheShownMaterial()
    {
        // One part with two alternatives; the batch is alternative 1 of slot 3, whose material 3 the skin swaps for 7.
        WorldBatch[] batches = [new(MaterialIndex: 3, FirstVertex: 0, VertexCount: 3, BodyPart: 0, BodyModel: 1, MaterialSlot: 3)];
        (int Base, int Count)[] parts = [(1, 2)];
        Dictionary<int, int> skin = new() { [3] = 7 };

        WorldRenderer.AnyShownMaterial(batches, skin, parts, body: 1, material => material == 7).ShouldBeTrue("swapped and shown");
        WorldRenderer.AnyShownMaterial(batches, null, parts, body: 1, material => material == 7).ShouldBeFalse("its own material is 3");
        WorldRenderer.AnyShownMaterial(batches, skin, parts, body: 0, material => material == 7).ShouldBeFalse("body 0 hides it");
    }

    [Test]
    public void ReadsFrameBuffer_UncloakedWithAMaterialThatNeedsIt_IsTrue() =>
        Device3D.ReadsFrameBuffer(new ModelInstance("m.mdl", [], null, null), materialsNeed: true).ShouldBeTrue();

    [Test]
    public void MainActual_UnprunedAndAfterTheWaterPlanIsCleared_IsThePlaceItself()
    {
        ViewHalf half = new() { MainPruned = ([7, 3], [1, 4]) };

        half.ClearWater();

        half.MainActual(1).ShouldBe(1);
    }
}
