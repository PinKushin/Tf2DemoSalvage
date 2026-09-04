namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The viewmodel projection correction — <c>FormatViewModelAttachment</c>.
/// </summary>
/// <remarks>
/// **A viewmodel renders through its own field of view**, so a point taken from its bones does not
/// sit where the world thinks it does. `SetupBones_AttachmentHelper` corrects every attachment it
/// resolves (<c>c_baseanimating.cpp:2081</c>) through a virtual whose base body is empty — nothing
/// for a world model, this for a viewmodel.
///
/// **The axis along the view is never touched**, only the two across it, which is what makes the
/// correction a squash rather than a move.
/// </remarks>
public sealed class ViewmodelAttachmentCorrectionTests
{
    /// <summary>How close two floats must be to count as equal here.</summary>
    private const double Tolerance = 1e-3;

    /// <summary>TF2's own defaults: `fov_desired` 75 against `viewmodel_fov_demo` 54.</summary>
    private const float WorldFov = 75f;
    private const float ViewmodelFov = 54f;

    /// <summary>A view at the origin looking down +X, with Valve's right-handed basis.</summary>
    private static readonly (float X, float Y, float Z) Eye = (0f, 0f, 0f);
    private static readonly (float X, float Y, float Z) Right = (0f, -1f, 0f);
    private static readonly (float X, float Y, float Z) Up = (0f, 0f, 1f);
    private static readonly (float X, float Y, float Z) Forward = (1f, 0f, 0f);

    [Test]
    public void Correct_APointOnTheViewAxis_DoesNotMove()
    {
        // Only x and y are scaled, so a point straight ahead has nothing to scale.
        (float x, float y, float z) = Correct((10f, 0f, 0f));

        x.ShouldBe(10f, Tolerance);
        y.ShouldBe(0f, Tolerance);
        z.ShouldBe(0f, Tolerance);
    }

    [Test]
    public void Correct_APointAcrossTheView_MovesOutwardWhenTheWorldFovIsWider()
    {
        // tan(75/2) / tan(54/2) is about 1.507, so a point one unit to the side of a forward axis
        // ten units long lands 1.507 units out. The world lens is wider than the viewmodel's, so
        // the same screen position corresponds to a point further off-axis in world terms.
        (float _, float y, float _) = Correct((10f, -1f, 0f));

        float expected = (float)(System.Math.Tan(75d * System.Math.PI / 360d) /
            System.Math.Tan(54d * System.Math.PI / 360d));

        (-y).ShouldBe(expected, Tolerance, "the offset scales by tan(world/2) / tan(viewmodel/2)");
    }

    /// <remarks>
    /// **The control that makes the case above about the two FIELDS OF VIEW rather than about the
    /// arithmetic.** With both equal the factor is one and nothing moves at all, so an
    /// implementation that scaled by some other quantity would fail here while passing there.
    /// </remarks>
    [Test]
    public void Correct_WhenBothFieldsOfViewAgree_ChangesNothing()
    {
        (float x, float y, float z) = ViewmodelAttachment.Correct(
            (10f, -1f, 2f), Eye, Right, Up, Forward, WorldFov, WorldFov);

        x.ShouldBe(10f, Tolerance);
        y.ShouldBe(-1f, Tolerance);
        z.ShouldBe(2f, Tolerance);
    }

    /// <remarks>
    /// **Valve's <c>viewx ? ( worldx / viewx ) : 0.0f</c>, and the comment naming the case**:
    /// *"NOTE: viewx was coming in as 0 when folks set their viewmodel_fov to 0 and show their
    /// weapon."* A factor of zero collapses the attachment onto the view axis rather than leaving
    /// it where it was — reproduced because `viewmodel_fov 0` is a thing people type.
    /// </remarks>
    [Test]
    public void Correct_WithAZeroViewmodelFieldOfView_CollapsesOntoTheViewAxis()
    {
        (float x, float y, float z) = ViewmodelAttachment.Correct(
            (10f, -3f, 4f), Eye, Right, Up, Forward, WorldFov, viewmodelFieldOfView: 0f);

        x.ShouldBe(10f, Tolerance, "the along-view component survives");
        y.ShouldBe(0f, Tolerance, "and the two across it are zeroed, not left alone");
        z.ShouldBe(0f, Tolerance);
    }

    [Test]
    public void Correct_Inverted_UndoesTheCorrection()
    {
        // `UncorrectViewModelAttachment` is the same function with bInverse — so applying one then
        // the other must return the point it started at.
        (float X, float Y, float Z) start = (10f, -1f, 2f);

        (float X, float Y, float Z) forward = Correct(start);

        (float x, float y, float z) = ViewmodelAttachment.Correct(
            forward, Eye, Right, Up, Forward, WorldFov, ViewmodelFov, inverse: true);

        x.ShouldBe(start.X, Tolerance);
        y.ShouldBe(start.Y, Tolerance);
        z.ShouldBe(start.Z, Tolerance);
    }

    [Test]
    public void Correct_FromAnEyeAwayFromTheOrigin_MeasuresFromThatEye()
    {
        // The offset is taken against the VIEW's origin, not the world's — a correction written
        // against (0,0,0) would pass every test above, since every one of them puts the eye there.
        (float X, float Y, float Z) eye = (100f, 50f, 20f);

        (float x, float y, float z) = ViewmodelAttachment.Correct(
            (110f, 50f, 20f), eye, Right, Up, Forward, WorldFov, ViewmodelFov);

        x.ShouldBe(110f, Tolerance);
        y.ShouldBe(50f, Tolerance);
        z.ShouldBe(20f, Tolerance);
    }

    private static (float X, float Y, float Z) Correct((float X, float Y, float Z) point) =>
        ViewmodelAttachment.Correct(point, Eye, Right, Up, Forward, WorldFov, ViewmodelFov);
}
