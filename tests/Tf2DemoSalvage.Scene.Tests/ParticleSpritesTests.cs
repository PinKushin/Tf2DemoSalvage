using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// Turning live particles into camera-facing quads (B373).
/// </summary>
[TestFixture]
public sealed class ParticleSpritesTests
{
    [Test]
    public void Build_AParticle_IsATriangleListFacingTheCamera()
    {
        // A quad two units across, built from a camera looking down -Z with x right and y up. Every
        // corner must lie in that plane, one radius from the centre on each axis.
        ParticleStore particles = new();

        particles.Add(new Vector3(10f, 20f, 30f), lives: 1f);

        List<DetailSpriteVertex> corners = [];

        ParticleSprites.Build(particles, Vector3.UnitX, Vector3.UnitY, corners);

        corners.Count.ShouldBe(ParticleSprites.CornersPerParticle);

        // The default radius is 1, so the quad spans 9..11 in x and 19..21 in y at a constant z.
        foreach (DetailSpriteVertex corner in corners)
        {
            corner.X.ShouldBeInRange(9f, 11f);
            corner.Y.ShouldBeInRange(19f, 21f);
            corner.Z.ShouldBe(30f);
        }
    }

    [Test]
    public void Build_TheCameraBasis_IsWhatDecidesTheQuadsPlane()
    {
        // **The assertion that says these are BILLBOARDS and not axis-aligned quads.** Turning the
        // basis must turn the quad; a builder ignoring the basis would pass the test above and fail
        // this one, which is why the two exist separately.
        ParticleStore particles = new();

        particles.Add(Vector3.Zero, lives: 1f);

        List<DetailSpriteVertex> sideways = [];
        List<DetailSpriteVertex> upright = [];

        ParticleSprites.Build(particles, Vector3.UnitX, Vector3.UnitY, upright);
        ParticleSprites.Build(particles, Vector3.UnitZ, Vector3.UnitY, sideways);

        // Facing one way the quad has no extent in z; facing the other it has no extent in x.
        foreach (DetailSpriteVertex corner in upright)
        {
            corner.Z.ShouldBe(0f);
        }

        foreach (DetailSpriteVertex corner in sideways)
        {
            corner.X.ShouldBe(0f);
        }
    }

    [Test]
    public void Build_AParticleFadedToNothing_CostsNoVertices()
    {
        // A fully transparent particle draws nothing, so six vertices for it are six wasted. The
        // control is the second particle, which is opaque and must still be built.
        ParticleStore particles = new();

        particles.Add(Vector3.Zero, lives: 1f);
        particles.Add(new Vector3(5f, 0f, 0f), lives: 1f);

        particles.Fade(0, 0f);

        List<DetailSpriteVertex> corners = [];

        ParticleSprites.Build(particles, Vector3.UnitX, Vector3.UnitY, corners);

        corners.Count.ShouldBe(ParticleSprites.CornersPerParticle);

        // And it is the SECOND particle that survived, not the first.
        corners[0].X.ShouldBeInRange(4f, 6f);
    }

    [Test]
    public void Build_TheAlpha_ReachesEveryCornerOfThatParticle()
    {
        // Alpha is per particle and the pass takes it per vertex, so it has to be written to all
        // six. Writing it to one corner produces a quad that fades across itself.
        ParticleStore particles = new();

        particles.Add(Vector3.Zero, lives: 1f);
        particles.Fade(0, 0.25f);

        List<DetailSpriteVertex> corners = [];

        ParticleSprites.Build(particles, Vector3.UnitX, Vector3.UnitY, corners);

        foreach (DetailSpriteVertex corner in corners)
        {
            corner.Alpha.ShouldBe(0.25f, 0.0001d);
        }
    }
}
