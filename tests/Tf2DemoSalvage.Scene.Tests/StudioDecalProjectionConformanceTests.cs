using System.Collections.Generic;
using System.Numerics;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// `CStudioRender::AddDecal` on a skinned model, read out of `studiorender.dll` (B415): whole triangles, UVs from the
/// decal frame, front faces only.
/// </summary>
public sealed class StudioDecalProjectionConformanceTests
{
    private static readonly float[] Identity = [1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f];

    [Test]
    public void Project_AShotAlongMinusXOntoAFaceAtTheOrigin_MapsYAndZToUAndV()
    {
        // fwd = −delta = +X, right = up × fwd = +Y, down = fwd × right = +Z; U = y / radius · 0.5 + 0.5.
        IReadOnlyList<StudioDecalCorner> corners = Project([Corner(0f, 2f, 0f), Corner(0f, 0f, 2f), Corner(0f, -2f, -2f)]);

        corners.Count.ShouldBe(3);
        corners[0].ShouldBe(new StudioDecalCorner(0, 0.75f, 0.5f));
        corners[1].ShouldBe(new StudioDecalCorner(1, 0.5f, 0.75f));
        corners[2].ShouldBe(new StudioDecalCorner(2, 0.25f, 0.25f));
    }

    [Test]
    public void Project_AFaceTurnedAwayFromTheShot_TakesNothing()
    {
        // Σ w · ( row2 · normal ) must be at least 0.1; a normal along −X gives −1.
        Project([Corner(0f, 2f, 0f, -1f), Corner(0f, 0f, 2f, -1f), Corner(0f, -2f, -2f, -1f)]).ShouldBeEmpty();
    }

    [Test]
    public void Project_ATriangleWhollyBesideTheDecal_TakesNothing()
    {
        // Every corner has U > 1, so the outcodes share bit 4.
        Project([Corner(0f, 10f, 0f), Corner(0f, 12f, 2f), Corner(0f, 11f, -2f)]).ShouldBeEmpty();
    }

    [Test]
    public void Project_ATriangleStraddlingTheDecalWithNoCornerInside_IsKeptWhole()
    {
        // No corner inside, but the triangle crosses the square: clipped, it still has corners, so the WHOLE triangle
        // is kept, with its corners' UVs beyond 0..1.
        IReadOnlyList<StudioDecalCorner> corners = Project([Corner(0f, -10f, -1f), Corner(0f, 10f, -1f), Corner(0f, 0f, 10f)]);

        corners.Count.ShouldBe(3);
        corners[0].U.ShouldBe(-0.75f);
    }

    [Test]
    public void Project_ABoneMovingTheModel_ProjectsThePosedVertex()
    {
        // The corner is at y = 0 in bind space, and its bone carries it to y = 2: poseToDecal = decal · poseToWorld.
        float[] moved = [1f, 0f, 0f, 0f, 0f, 1f, 0f, 2f, 0f, 0f, 1f, 0f];

        IReadOnlyList<StudioDecalCorner> corners = StudioDecalProjection.Project(
            [Corner(0f, 0f, 0f), Corner(0f, 0f, 2f), Corner(0f, -2f, 0f)],
            [moved],
            new Vector3(100f, 0f, 0f),
            new Vector3(-110f, 0f, 0f),
            Vector3.UnitZ,
            radius: 4f,
            noPokeThru: false);

        corners[0].U.ShouldBe(0.75f);
    }

    private static IReadOnlyList<StudioDecalCorner> Project(PropVertex[] vertices) =>
        StudioDecalProjection.Project(
            vertices, [Identity], new Vector3(100f, 0f, 0f), new Vector3(-110f, 0f, 0f), Vector3.UnitZ, radius: 4f, noPokeThru: false);

    private static PropVertex Corner(float x, float y, float z, float normalX = 1f) =>
        new(x, y, z, 0f, 0f, 0, NormalX: normalX, NormalY: 0f, NormalZ: 0f, Weights: (1f, 0f, 0f));
}
