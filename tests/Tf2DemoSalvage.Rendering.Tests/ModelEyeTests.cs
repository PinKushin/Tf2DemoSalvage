using System.Numerics;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// The eye in a brush entity's own space, where its faces' planes are, for the <c>mat_wireframe 3</c> back-face test
/// (`engine.dll` `FUN_1800dcb20` tests the planes in the model's space; B803 review 2).
/// </summary>
public sealed class ModelEyeTests
{
    /// <summary>A quarter turn about z, x to y and y to −x, then a move of (40, 30, 0): row vectors, as the shader reads it.</summary>
    private static readonly float[] Placed = [0f, 1f, 0f, 0f, -1f, 0f, 0f, 0f, 0f, 0f, 1f, 0f, 40f, 30f, 0f, 1f];

    /// <remarks>
    /// **The placement's inverse, move then turn** (Fable, Sonnet): the eye (0, −300, 0) less the move is (−40, −330, 0),
    /// and turned back a quarter (x from y, y from −x) is (−330, 40, 0). The world eye returned as it is, or the matrix
    /// read transposed ((300, 0, 0): the turn's transpose with no move), both read differently.
    /// </remarks>
    [Test]
    public void ModelEye_UnderATurnedAndMovedPlacement_IsTheEyeMovedByItsInverse()
    {
        Vector3 eye = WorldRenderer.ModelEye(Placed, (0f, -300f, 0f)).ShouldNotBeNull();

        (eye - new Vector3(-330f, 40f, 0f)).Length().ShouldBeLessThan(1e-3f, $"was {eye}");
    }

    /// <remarks>
    /// **No eye, no answer** (Grok): before any camera there is no eye, and a placement with no inverse has no model space
    /// to move one into. Both answer null, which draws every face, rather than the origin or the world eye, which would
    /// leave out faces by a test against the wrong point.
    /// </remarks>
    [Test]
    public void ModelEye_WithNoEyeOrASingularPlacement_IsUnknown()
    {
        (WorldRenderer.ModelEye(Placed, null), WorldRenderer.ModelEye(new float[16], (0f, -300f, 0f))).ShouldBe((null, null));
    }
}
