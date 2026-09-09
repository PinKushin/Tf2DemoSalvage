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
    /// <summary>
    /// A sheet shaped like <c>smokelit</c>'s: one sequence of two frames, each an exact half of the
    /// texture, so a wrong frame is a wrong number rather than a near miss.
    /// </summary>
    private static IReadOnlyList<SheetSequence> TwoFrames =>
    [
        new SheetSequence(
            Id: 0,
            Clamp: false,
            TotalTime: 2f,
            Frames:
            [
                new SheetFrame(1f, 0f, 0f, 0.5f, 1f),
                new SheetFrame(1f, 0.5f, 0f, 1f, 1f),
            ]),
    ];

    [Test]
    public void Build_AParticle_IsATriangleListFacingTheCamera()
    {
        // A quad two units across, built from a camera looking down -Z with x right and y up. Every
        // corner must lie in that plane, one radius from the centre on each axis.
        ParticleStore particles = new();

        particles.Add(new Vector3(10f, 20f, 30f), lives: 1f);

        List<DetailSpriteVertex> corners = [];

        Build(particles, Vector3.UnitX, Vector3.UnitY, corners);

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

        Build(particles, Vector3.UnitX, Vector3.UnitY, upright);
        Build(particles, Vector3.UnitZ, Vector3.UnitY, sideways);

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

        Build(particles, Vector3.UnitX, Vector3.UnitY, corners);

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

        Build(particles, Vector3.UnitX, Vector3.UnitY, corners);

        foreach (DetailSpriteVertex corner in corners)
        {
            corner.Alpha.ShouldBe(0.25f, 0.0001d);
        }
    }

    [Test]
    public void Build_NoSheet_TakesTheWholeTexture()
    {
        // **The control for every frame assertion below.** A texture that declares no sequences is
        // the ordinary case for a non-animating material, and it must still draw — with the 0..1
        // coordinates this builder emitted for everything before the sheet was read.
        ParticleStore particles = new();

        particles.Add(Vector3.Zero, lives: 1f);

        List<DetailSpriteVertex> corners = [];

        ParticleSprites.Build(
            particles, Vector3.UnitX, Vector3.UnitY, corners,
            sheet: [], rate: 3f, asFramesPerSecond: true, fitLifetime: false);

        Spread(corners, one => one.U).ShouldBe((0f, 1f));
        Spread(corners, one => one.V).ShouldBe((0f, 1f));

        // Nothing to animate toward, so the shader's lerp must be an identity.
        foreach (DetailSpriteVertex corner in corners)
        {
            corner.Blend.ShouldBe(0f);
        }
    }

    [Test]
    public void Build_AtBirth_TakesTheFirstFrameAndNotTheWholeSheet()
    {
        // A newborn particle is on frame 0, which is the LEFT HALF of this sheet. Emitting 0..1
        // would draw both halves on every particle — the exact divergence B373 names, and the one
        // this test exists to keep from coming back.
        ParticleStore particles = new();

        particles.Add(Vector3.Zero, lives: 1f);

        List<DetailSpriteVertex> corners = [];

        ParticleSprites.Build(
            particles, Vector3.UnitX, Vector3.UnitY, corners,
            TwoFrames, rate: 3f, asFramesPerSecond: true, fitLifetime: false);

        Spread(corners, one => one.U).ShouldBe((0f, 0.5f));
        Spread(corners, one => one.V).ShouldBe((0f, 1f));
    }

    [Test]
    public void Build_AtOneThirdOfASecondAtThreeFps_HasAdvancedOneFrame()
    {
        // **The frame clock is AGE times rate, and this is the value that proves it.** At three
        // frames a second a particle a third of a second old is exactly on frame 1 — the right
        // half. Reading the clock as a life fraction instead would still be on frame 0 here, since
        // the particle is a third of the way through a one-second life and this sheet has two
        // frames.
        ParticleStore particles = new();

        particles.Add(Vector3.Zero, lives: 1f);
        particles.Tick(1f / 3f);

        List<DetailSpriteVertex> corners = [];

        ParticleSprites.Build(
            particles, Vector3.UnitX, Vector3.UnitY, corners,
            TwoFrames, rate: 3f, asFramesPerSecond: true, fitLifetime: false);

        Spread(corners, one => one.U).ShouldBe((0.5f, 1f));

        // And the frame it is mixing toward wraps back to the first, because `Clamp` is false.
        Spread(corners, one => one.NextU).ShouldBe((0f, 0.5f));
    }

    [Test]
    public void Build_HalfwayBetweenTwoFrames_CarriesTheBlendAndBothFrames()
    {
        // `BLENDFRAMES` defaults to on (`spritecard.cpp:143`), so the engine crossfades rather than
        // cutting. Half a frame in — a sixth of a second at three fps — the blend must be a half and
        // the two coordinate sets must name DIFFERENT frames. A builder that picked one frame and
        // duplicated it would pass every assertion above and fail this one.
        ParticleStore particles = new();

        particles.Add(Vector3.Zero, lives: 1f);
        particles.Tick(1f / 6f);

        List<DetailSpriteVertex> corners = [];

        ParticleSprites.Build(
            particles, Vector3.UnitX, Vector3.UnitY, corners,
            TwoFrames, rate: 3f, asFramesPerSecond: true, fitLifetime: false);

        foreach (DetailSpriteVertex corner in corners)
        {
            corner.Blend.ShouldBe(0.5f, 0.0001d);
        }

        Spread(corners, one => one.U).ShouldBe((0f, 0.5f));
        Spread(corners, one => one.NextU).ShouldBe((0.5f, 1f));
    }

    [Test]
    public void Build_AParticlesOwnSequence_IsWhatSelectsItsFrames()
    {
        // **Two particles of one system on two sequences, which is why `SEQUENCE_NUMBER` is per
        // particle.** The second sequence here is the first reversed, so at birth the two particles
        // must land on opposite halves of the texture. A renderer that read the sequence from the
        // system rather than the particle would put both on the same one.
        IReadOnlyList<SheetSequence> pair =
        [
            TwoFrames[0],
            new SheetSequence(1, false, 2f, [TwoFrames[0].Frames[1], TwoFrames[0].Frames[0]]),
        ];

        ParticleStore particles = new();

        particles.Add(Vector3.Zero, lives: 1f);
        particles.Add(new Vector3(100f, 0f, 0f), lives: 1f);

        particles.PlaySequence(1, 1);

        List<DetailSpriteVertex> corners = [];

        ParticleSprites.Build(
            particles, Vector3.UnitX, Vector3.UnitY, corners,
            pair, rate: 3f, asFramesPerSecond: true, fitLifetime: false);

        corners.Count.ShouldBe(2 * ParticleSprites.CornersPerParticle);

        Spread(corners.GetRange(0, 6), one => one.U).ShouldBe((0f, 0.5f));
        Spread(corners.GetRange(6, 6), one => one.U).ShouldBe((0.5f, 1f));
    }

    /// <summary>The smallest and largest of one coordinate across a quad's corners.</summary>
    /// <remarks>
    /// **The bounds and not one corner's value**, because which corner is which is the winding's
    /// business: a test that asserted on `corners[0].U` would fail when the triangles are reordered
    /// for a reason that has nothing to do with the sheet.
    /// </remarks>
    private static (float Least, float Most) Spread(
        IReadOnlyList<DetailSpriteVertex> corners, System.Func<DetailSpriteVertex, float> of)
    {
        float least = float.MaxValue;
        float most = float.MinValue;

        foreach (DetailSpriteVertex corner in corners)
        {
            least = System.MathF.Min(least, of(corner));
            most = System.MathF.Max(most, of(corner));
        }

        return (least, most);
    }

    /// <summary>The builder with no sheet, for the tests that are about geometry.</summary>
    private static void Build(
        ParticleStore particles,
        Vector3 right,
        Vector3 up,
        ICollection<DetailSpriteVertex> into) =>
        ParticleSprites.Build(
            particles, right, up, into,
            sheet: [], rate: 1f, asFramesPerSecond: true, fitLifetime: false);
}
