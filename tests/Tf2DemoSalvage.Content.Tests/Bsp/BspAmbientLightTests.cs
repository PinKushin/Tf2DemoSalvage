using System;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// The ambient light a map bakes for the things that move through it.
/// </summary>
/// <remarks>
/// **A model has no lightmap, which is why this exists.** A lightmap belongs to a surface; a health
/// pack, a rocket and a player do not have one, so <c>vrad</c> also samples the light arriving
/// inside each leaf and stores a cube per sample. The client lights anything that moves from the
/// leaf it stands in — <c>LightingState_t</c> carries it as <c>m_vecAmbientCube[6]</c>.
///
/// Without it every entity model draws at full brightness, which is what made a medkit a pale
/// square instead of a teal case (B51).
/// </remarks>
public sealed class BspAmbientLightTests
{
    [Test]
    public void ASurfaceFacingAnAxis_TakesThatFacesColourAlone()
    {
        // The squared normal sums to one for a unit normal, so a surface facing exactly along an
        // axis takes one face of the cube and nothing else. That is the property that makes the
        // whole scheme a smooth blend rather than six flat buckets.
        AmbientCube cube = Cube();

        cube.Light(1f, 0f, 0f).ShouldBe((1f, 0f, 0f));
        cube.Light(0f, 1f, 0f).ShouldBe((0f, 0f, 1f));
        cube.Light(0f, 0f, 1f).ShouldBe((0.5f, 0.5f, 0.5f));
    }

    [Test]
    public void ANegativeNormal_TakesTheOppositeFace()
    {
        // **The half of the cube a wrong sign test would lose.** Facing down is not facing up, and
        // a model lit from the wrong side looks like a lighting bug rather than an indexing one -
        // the cube is indexed as cAmbientCube[isNegative.x], [isNegative.y+2], [isNegative.z+4].
        AmbientCube cube = Cube();

        cube.Light(-1f, 0f, 0f).ShouldBe((0f, 1f, 0f));
        cube.Light(0f, -1f, 0f).ShouldBe((1f, 1f, 0f));
        cube.Light(0f, 0f, -1f).ShouldBe((0.25f, 0.25f, 0.25f));
    }

    [Test]
    public void ADiagonalNormal_BlendsByTheSquares()
    {
        // Valve's own arithmetic: nSquared weights the faces. At 45 degrees between +X and +Z each
        // squared component is 0.5, so the result is half of each face.
        //
        // A test that only checked the axes would pass against an implementation that used the
        // normal rather than its square - which is the difference between a smooth blend and one
        // that is wrong everywhere except the six axes.
        AmbientCube cube = Cube();

        float diagonal = MathF.Sqrt(0.5f);

        (float red, float green, float blue) = cube.Light(diagonal, 0f, diagonal);

        red.ShouldBe(0.75f, 1e-5f);
        green.ShouldBe(0.25f, 1e-5f);
        blue.ShouldBe(0.25f, 1e-5f);
    }

    [Test]
    public void AnEmptyCube_LightsNothing()
    {
        // A leaf with no samples is solid or outside the map. Black is the honest answer; the
        // alternative is inventing light for a place the compiler never lit.
        default(AmbientCube).Light(0f, 0f, 1f).ShouldBe((0f, 0f, 0f));
    }

    /// <summary>A cube with a different colour on each face, so a swap is visible.</summary>
    private static AmbientCube Cube() =>
        new(
            PositiveX: (1f, 0f, 0f),
            NegativeX: (0f, 1f, 0f),
            PositiveY: (0f, 0f, 1f),
            NegativeY: (1f, 1f, 0f),
            PositiveZ: (0.5f, 0.5f, 0.5f),
            NegativeZ: (0.25f, 0.25f, 0.25f));
}
