using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Turning HUD rectangles in screen pixels into clip-space triangles.
/// </summary>
/// <remarks>
/// **Measured without a device on purpose**, exactly as <c>TopDownCamera</c> is. Where a thing
/// appears on screen is arithmetic, and arithmetic that can only be checked by looking at a GPU is
/// arithmetic nobody checks — so the conversion is a static method the tests can call and the shader
/// is a pass-through.
///
/// **What is NOT tested here is the picture**, and that is deliberate rather than a gap: whether the
/// meter is legible, whether the outline reads at 10 pixels, whether it sits somewhere sensible over
/// a map. Those are questions for a person looking at the screen
/// (`docs/memory/a-picture-is-assertable.md` for the ones that can be pinned, the owner's eyes for
/// the rest).
/// </remarks>
public sealed class HudRendererTests
{
    /// <summary>Floats per vertex: two of position, two of texture, four of colour.</summary>
    private const int Stride = 8;

    private static readonly HudQuad WholeViewport =
        new(0, 0, 200, 100, 0, 0, 255, 255, 255, 255);

    private static float[] Build(
        HudQuad quad, int viewportWidth = 200, int viewportHeight = 100, int atlas = 256) =>
        HudRenderer.BuildVertices([quad], viewportWidth, viewportHeight, atlas, atlas);

    [Test]
    public void BuildVertices_OneQuad_IsTwoTriangles()
    {
        Build(WholeViewport).Length.ShouldBe(6 * Stride);
    }

    /// <summary>
    /// A quad covering the whole viewport reaches every corner of clip space.
    /// </summary>
    /// <remarks>
    /// The condition is chosen so a wrong scale cannot pass: the target is exactly the corners, so
    /// halving, doubling or forgetting the -1 all land somewhere else. A quad in the middle of the
    /// screen would be satisfied by several wrong transforms.
    /// </remarks>
    [Test]
    public void BuildVertices_AQuadCoveringTheViewport_ReachesEveryCornerOfClipSpace()
    {
        float[] data = Build(WholeViewport);

        List<(float X, float Y)> corners = [];

        for (int at = 0; at < data.Length; at += Stride)
        {
            corners.Add((data[at], data[at + 1]));
        }

        corners.ShouldContain((-1f, 1f), "top left");
        corners.ShouldContain((1f, 1f), "top right");
        corners.ShouldContain((1f, -1f), "bottom right");
        corners.ShouldContain((-1f, -1f), "bottom left");
    }

    /// <summary>
    /// Screen Y grows downward and clip Y grows upward.
    /// </summary>
    /// <remarks>
    /// **The one transform error that looks plausible on screen**: a HUD drawn upside down in the
    /// vertical axis still appears, just in the wrong corner, and a meter in the wrong corner reads
    /// as a placement choice rather than as a bug. So the flip is asserted directly rather than left
    /// to the corner test, which a flipped implementation also satisfies — it produces the same four
    /// corners in a different order.
    /// </remarks>
    [Test]
    public void BuildVertices_AQuadNearTheTopOfTheScreen_IsNearTheTopOfClipSpace()
    {
        // Ten pixels down a hundred-pixel viewport: a fifth of the way from +1 toward -1.
        float[] data = Build(new HudQuad(0, 10, 20, 10, 0, 0, 255, 255, 255, 255));

        float highest = float.MinValue;

        for (int at = 0; at < data.Length; at += Stride)
        {
            highest = Math.Max(highest, data[at + 1]);
        }

        highest.ShouldBe(0.8f, 0.0001f, "1 - (10 / 100) * 2");
    }

    /// <summary>
    /// The texture coordinates come from the quad's place in the atlas.
    /// </summary>
    /// <remarks>
    /// A 256-pixel atlas is chosen so the expected values are exact in binary and need no tolerance,
    /// and the source rectangle is deliberately not at the origin — a reader that ignored SourceX
    /// and SourceY would pass against a quad at (0,0).
    /// </remarks>
    [Test]
    public void BuildVertices_AQuadFromTheMiddleOfTheAtlas_TakesItsTextureCoordinatesFromThere()
    {
        float[] data = Build(new HudQuad(0, 0, 32, 16, 64, 128, 255, 255, 255, 255));

        List<(float U, float V)> texture = [];

        for (int at = 0; at < data.Length; at += Stride)
        {
            texture.Add((data[at + 2], data[at + 3]));
        }

        texture.ShouldContain((64f / 256f, 128f / 256f), "top left of the source rectangle");
        texture.ShouldContain((96f / 256f, 144f / 256f), "bottom right, 32 and 16 further on");
    }

    /// <summary>
    /// Colour is carried per vertex, normalised.
    /// </summary>
    /// <remarks>
    /// The shader multiplies the texture's RGB by this, which is the whole outline mechanism (D84):
    /// a black texel stays black at any tint because <c>0 × c = 0</c>. So the tint reaching the
    /// vertex correctly is what makes an outlined font possible, and a channel swapped here would
    /// show as a meter that turns blue when it should turn red.
    /// </remarks>
    [Test]
    public void BuildVertices_AColouredQuad_CarriesThatColourOnEveryVertex()
    {
        // GetFPSColor's yellow: red and green full, blue none.
        float[] data = Build(new HudQuad(0, 0, 10, 10, 0, 0, 255, 255, 0, 128));

        for (int at = 0; at < data.Length; at += Stride)
        {
            data[at + 4].ShouldBe(1f, 0.0001f, "red");
            data[at + 5].ShouldBe(1f, 0.0001f, "green");
            data[at + 6].ShouldBe(0f, 0.0001f, "blue");
            data[at + 7].ShouldBe(128f / 255f, 0.0001f, "alpha");
        }
    }

    [Test]
    public void BuildVertices_SeveralQuads_AppendsThemInOrder()
    {
        HudQuad first = new(0, 0, 10, 10, 0, 0, 255, 0, 0, 255);
        HudQuad second = new(100, 0, 10, 10, 0, 0, 0, 255, 0, 255);

        float[] data = HudRenderer.BuildVertices([first, second], 200, 100, 256, 256);

        data.Length.ShouldBe(2 * 6 * Stride);

        // The first quad's vertices are red and the second's are green, so a builder that
        // overwrote rather than appended is caught by reading the second block.
        data[4].ShouldBe(1f, 0.0001f);
        data[6 * Stride + 5].ShouldBe(1f, 0.0001f);
        data[6 * Stride + 4].ShouldBe(0f, 0.0001f);
    }

    [Test]
    public void BuildVertices_NoQuads_IsEmpty()
    {
        HudRenderer.BuildVertices([], 200, 100, 256, 256).ShouldBeEmpty();
    }
}
