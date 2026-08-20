using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Which faces are culled, and why a viewmodel is the exception.
/// </summary>
/// <remarks>
/// **A viewmodel is drawn mirrored, so its winding is backwards and the cull has to flip with it.**
/// <c>C_BaseViewModel::InternalDrawModel</c> (<c>c_baseviewmodel.cpp:371</c>) is the whole rule:
///
/// <code>
/// if ( ShouldFlipViewModel() )
///     pRenderContext->CullMode( MATERIAL_CULLMODE_CW );
///
/// int ret = BaseClass::InternalDrawModel( flags );
///
/// pRenderContext->CullMode( MATERIAL_CULLMODE_CCW );
/// </code>
///
/// Note that it puts the mode BACK afterwards unconditionally, rather than restoring what it found.
/// That is worth copying: a leaked render state is what put a medkit over a medic here once, when
/// DrawTranslucent left a read-only depth state set and every model after it drew without depth
/// writes.
///
/// **Getting this wrong does not fail, it draws the weapon inside out** — you see the far faces of
/// the model and none of the near ones, which reads as a broken model rather than as a render
/// state. That is exactly why the project's own conformance note singled it out before any of this
/// was written.
///
/// <c>$nocull</c> still wins over both. A material that asked for no culling gets none whichever
/// way the model is wound, because the flag is a statement about the material rather than about
/// the geometry.
/// </remarks>
public sealed class ViewmodelCullConformanceTests
{
    [Test]
    public void CullFor_AnOrdinaryModel_CullsBackFaces()
    {
        // The default, and the engine's: MATERIAL_CULLMODE_CCW, front faces wound clockwise
        // (imaterialsystem.h:180).
        WorldRenderer.CullFor(mirrored: false, noCull: false).ShouldBe(ModelCull.Back);
    }

    [Test]
    public void CullFor_AMirroredModel_CullsFrontFacesInstead()
    {
        // **The flip.** Same geometry, opposite winding, so the opposite faces are the ones facing
        // away. A viewmodel drawn with the ordinary state shows its inside.
        WorldRenderer.CullFor(mirrored: true, noCull: false).ShouldBe(ModelCull.Front);
    }

    [Test]
    public void CullFor_AMaterialThatAsksForNoCulling_GetsNoneEitherWay()
    {
        // **$nocull outranks the flip, and that is not obvious.** The flag says the material's
        // faces are meant to be visible from behind — a chain-link fence, a flat blade — which is
        // true whichever way the model that carries it is wound. Culling the front faces of a
        // two-sided material would hide exactly what it asked to keep.
        WorldRenderer.CullFor(mirrored: false, noCull: true).ShouldBe(ModelCull.None);
        WorldRenderer.CullFor(mirrored: true, noCull: true).ShouldBe(ModelCull.None);
    }

    [Test]
    public void CullFor_TheThreeStates_AreAllDistinct()
    {
        // The property behind the three cases: a selector that collapsed two of them would satisfy
        // one test above and quietly break the other. Stated because this is a three-way choice
        // written as two booleans, which is the shape that usually loses a case.
        ModelCull[] all =
        [
            WorldRenderer.CullFor(false, false),
            WorldRenderer.CullFor(true, false),
            WorldRenderer.CullFor(false, true),
        ];

        all.ShouldBeUnique();
    }
}
