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

    /// <remarks>
    /// A one-bone model takes the clipped path: a triangle covering the whole decal is cut down to the decal's square,
    /// and the square's four corners come back as a fan of two triangles, each at the point on the face it covers.
    /// </remarks>
    [Test]
    public void ProjectClipped_ATriangleCoveringTheDecal_IsCutToItsSquare()
    {
        IReadOnlyList<WorldVertex> corners = Clipped([Corner(0f, -100f, -100f), Corner(0f, 100f, -100f), Corner(0f, 0f, 100f)]);

        corners.Count.ShouldBe(6);

        foreach (WorldVertex corner in corners)
        {
            // U = y / 4 · 0.5 + 0.5 and V = z / 4 · 0.5 + 0.5, each exactly 0 or 1 at a square corner.
            corner.U.ShouldBe(System.MathF.Round(corner.U), 1e-5f);
            corner.V.ShouldBe(System.MathF.Round(corner.V), 1e-5f);
            System.MathF.Round(corner.U).ShouldBeInRange(0f, 1f);
            System.MathF.Round(corner.V).ShouldBeInRange(0f, 1f);
            corner.Y.ShouldBe((corner.U * 8f) - 4f, 1e-4f);
            corner.Depth.ShouldBe((corner.V * 8f) - 4f, 1e-4f);
            corner.NormalX.ShouldBe(1f, 1e-6f);
        }
    }

    [Test]
    public void ProjectClipped_ATriangleWhollyInsideTheDecal_IsKeptAsItIs()
    {
        IReadOnlyList<WorldVertex> corners = Clipped([Corner(0f, 2f, 0f), Corner(0f, 0f, 2f), Corner(0f, -2f, -2f)]);

        corners.Count.ShouldBe(3);
        (corners[2].Y, corners[2].U, corners[2].V).ShouldBe((-2f, 0.25f, 0.25f));
    }

    /// <remarks>`0x18000b690`: with `noPokeThru`, a corner is in depth only when |row2 · pos + t| &lt; radius.</remarks>
    [Test]
    public void ProjectClipped_AFaceBeyondTheDecalsDepth_TakesNothing() =>
        Clipped([Corner(-20f, 2f, 0f), Corner(-20f, 0f, 2f), Corner(-20f, -2f, -2f)]).ShouldBeEmpty();

    /// <summary>The better ray `AddStudioDecal` builds from a trace: from the hit point one unit into the face, bloated.</summary>
    private static IReadOnlyList<WorldVertex> Clipped(WorldVertex[] vertices) =>
        StudioDecalProjection.ProjectClipped(
            vertices, Vector3.Zero, new Vector3(-1.1f, 0f, 0f), Vector3.UnitZ, radius: 4f, noPokeThru: true);

    private static IReadOnlyList<StudioDecalCorner> Project(WorldVertex[] vertices) =>
        StudioDecalProjection.Project(
            vertices, [Identity], new Vector3(100f, 0f, 0f), new Vector3(-110f, 0f, 0f), Vector3.UnitZ, radius: 4f, noPokeThru: false);

    // A packed model vertex: model-space z rides in Depth.
    private static WorldVertex Corner(float x, float y, float z, float normalX = 1f) =>
        new(x, y, z, 0f, 0f, 0f, 0f, 0f, NormalX: normalX, NormalY: 0f, NormalZ: 0f, WeightA: 1f);
}
