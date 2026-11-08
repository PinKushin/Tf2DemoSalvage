using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Scene.ProjectedShadows;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The world surfaces a render-to-texture shadow lands on, drawn — `CShadowMgr::ProjectShadow`'s leaf walk
/// (`FUN_1801264f0`) and `RenderShadows`' decal build (`FUN_1801238a0`, `FUN_180121970`) (finding 107).
/// </summary>
/// <remarks>
/// **The fixture** is the standing player of <see cref="ShadowReceiverConformanceTests"/> — origin (100, 200, 372), cast
/// straight down — in a 64 cell at (128, 256); a one-node tree on the plane x = 0, front leaf 0 and back leaf 1; and a
/// floor at z = 250 under the player, face 0, in leaf 0: 122 below the origin, past the falloff start of
/// √5984 / 2 + 36 ≈ 74.7, so it fades by (122 − 74.7) × 240 / 50 ≈ 227 of 255.
/// </remarks>
public sealed class ShadowReceiversConformanceTests
{
    private static readonly CastShadow Player = new(
        1,
        [1],
        ShadowProjection.ForRenderToTexture(
            new Vector3(100f, 200f, 300f), (0f, 0f, 0f), new Vector3(-10f, -10f, 0f), new Vector3(10f, 10f, 72f), new Vector3(0f, 0f, -1f), 50f),
        new ShadowCell(128, 256, 64, 64));

    private static readonly Vector3[] FloorCorners =
        [new(0f, 100f, 250f), new(200f, 100f, 250f), new(200f, 300f, 250f), new(0f, 300f, 250f)];

    private static BspLeafTree Wall()
    {
        byte[] plane = new byte[20];
        BinaryPrimitives.WriteSingleLittleEndian(plane.AsSpan(0), 1f);

        byte[] node = new byte[32];
        BinaryPrimitives.WriteInt32LittleEndian(node.AsSpan(4), -0 - 1);
        BinaryPrimitives.WriteInt32LittleEndian(node.AsSpan(8), -1 - 1);

        return BspLeafTree.FromLumps(node, plane, new byte[64]);
    }

    private static DecalFace Face(int index, Vector3[] corners, bool displacement = false) => new(
        index, corners, Vector3.UnitZ, corners[0].Z, default, default, (0, 0), (0, 0), false, displacement, false, default);

    private static List<ShadowVertex> Project(DecalWorld world, params SurfaceShadowBits[] bits)
    {
        List<ShadowVertex> drawn = [];
        new ShadowReceivers(Wall(), world, bits).Project(Player, drawn);
        return drawn;
    }

    /// <remarks>
    /// The floor's polygon, clipped to the shadow and lifted (<see cref="ShadowDecal.Clip"/>), as fan triangles; each
    /// corner's coordinate is the cell's origin plus its span times (u, v), and its alpha the fade byte of its depth.
    /// </remarks>
    [Test]
    public void Project_AFloorUnderThePlayer_EmitsItsClippedPolygonAsAFan()
    {
        IReadOnlyList<ShadowDecalVertex> polygon = ShadowDecal.Clip(FloorCorners, Vector3.UnitZ, Player.Projection)!;
        ShadowProjection p = Player.Projection;

        List<ShadowVertex> drawn = Project(new DecalWorld([], [[0], []], [Face(0, FloorCorners)]), SurfaceShadowBits.DynamicShadows);

        drawn.Count.ShouldBe(3 * (polygon.Count - 2));

        for (int i = 0; i + 2 < polygon.Count; i++)
        {
            ShadowDecalVertex[] corners = [polygon[0], polygon[i + 1], polygon[i + 2]];

            for (int corner = 0; corner < 3; corner++)
            {
                ShadowVertex vertex = drawn[(3 * i) + corner];
                ShadowDecalVertex expected = corners[corner];

                vertex.Position.ShouldBe(expected.Position);
                vertex.TexCoord.ShouldBe(new Vector2(
                    (128.5f / 1024f) + (63f / 1024f * expected.Texture.X), (256.5f / 1024f) + (63f / 1024f * expected.Texture.Y)));
                vertex.Fade.ShouldBe(ShadowDecal.Fade(expected.Texture.Z, p.FalloffStart, p.MaxHeight, 240f, 0) / 255f);
            }
        }

        drawn[0].Fade.ShouldBe(227f / 255f);
    }

    /// <remarks>A face whose bits the test refuses — here no dynamic-shadows bit — is not drawn on.</remarks>
    [Test]
    public void Project_AFaceTheShadowDoesNotTake_EmitsNothing() =>
        Project(new DecalWorld([], [[0], []], [Face(0, FloorCorners)]), SurfaceShadowBits.None).ShouldBeEmpty();

    /// <remarks>A face listed by two leaves the sweep reaches, or twice by one, is drawn once (the per-projection counter).</remarks>
    [Test]
    public void Project_AFaceListedTwice_IsEmittedOnce()
    {
        int once = Project(new DecalWorld([], [[0], []], [Face(0, FloorCorners)]), SurfaceShadowBits.DynamicShadows).Count;

        Project(new DecalWorld([], [[0, 0], [0]], [Face(0, FloorCorners)]), SurfaceShadowBits.DynamicShadows).Count.ShouldBe(once);
    }

    /// <remarks>A face only in a leaf the sweep does not reach — the back of x = 0, 83 units away — is not drawn on.</remarks>
    [Test]
    public void Project_AFaceInALeafTheSweepMisses_EmitsNothing() =>
        Project(new DecalWorld([], [[], [0]], [Face(0, FloorCorners)]), SurfaceShadowBits.DynamicShadows).ShouldBeEmpty();

    /// <remarks>
    /// A displacement's drawn triangle under the player, through its leaf: the triangle is lifted 0.1 along its own
    /// normal and clipped like a face, so every corner sits at z = 300.1.
    /// </remarks>
    [Test]
    public void Project_ADisplacementTriangleUnderThePlayer_IsLiftedAndClipped()
    {
        List<ShadowVertex> drawn = Project(Displaced(), SurfaceShadowBits.DynamicShadows);

        drawn.ShouldNotBeEmpty();
        drawn.ShouldAllBe(vertex => MathF.Abs(vertex.Position.Z - 300.1f) < 1e-3f);
    }

    /// <remarks>The displacement's base face says no shadows, or its box misses the shadow's sphere: nothing.</remarks>
    [TestCase(SurfaceShadowBits.DynamicShadows | SurfaceShadowBits.NoShadows, 300f)]
    [TestCase(SurfaceShadowBits.DynamicShadows, -1000f)]
    public void Project_ADisplacementTheShadowRefuses_EmitsNothing(SurfaceShadowBits bits, float boxZ) =>
        Project(Displaced(boxZ), bits).ShouldBeEmpty();

    private static DecalWorld Displaced(float boxZ = 300f)
    {
        Vector3[] square = [new(0f, 100f, 300f), new(0f, 300f, 300f), new(200f, 300f, 300f), new(200f, 100f, 300f)];
        DisplacementCollisionTree tree = DisplacementCollisionTree.Build(square, 2, new (Vector3, float)[25]);
        DisplacementCorner[] triangle =
            [new(new Vector3(0f, 100f, 300f), 0f, 0f), new(new Vector3(200f, 100f, 300f), 0f, 0f), new(new Vector3(100f, 300f, 300f), 0f, 0f)];

        return new DecalWorld([], [[0], []], [Face(0, square, displacement: true)])
        {
            Displacements = [new DecalDisplacement(0, new Vector3(0f, 100f, boxZ - 1f), new Vector3(200f, 300f, boxZ + 1f), triangle)],
            RayDisplacements = [new RayDisplacement(0, tree, false)],
            LeafDisplacements = [[0], []],
        };
    }

    /// <remarks>
    /// The face's bits as the engine assembles them: the material's no-cull, translucent and alpha-test; texinfo
    /// <c>SURF_NOSHADOWS</c> and <c>SURF_TRANS</c>; and the face's own dynamic-shadows bit.
    /// </remarks>
    [Test]
    public void BitsOf_EachSource_SetsItsBit()
    {
        ShadowReceivers.BitsOf(true, SurfaceProperties.None, SurfaceShadowBits.None).ShouldBe(SurfaceShadowBits.DynamicShadows);
        ShadowReceivers.BitsOf(false, SurfaceProperties.NoShadows, SurfaceShadowBits.None).ShouldBe(SurfaceShadowBits.NoShadows);
        ShadowReceivers.BitsOf(false, SurfaceProperties.Translucent, SurfaceShadowBits.None).ShouldBe(SurfaceShadowBits.Translucent);
        ShadowReceivers.BitsOf(false, SurfaceProperties.None, SurfaceShadowBits.NoCull | SurfaceShadowBits.AlphaTest)
            .ShouldBe(SurfaceShadowBits.NoCull | SurfaceShadowBits.AlphaTest);
    }

    /// <remarks>A material that blends — alpha, additive or modulate, or faded below one — is translucent; a cut-out one alpha-tested.</remarks>
    [Test]
    public void MaterialBits_EachKindOfMaterial_IsClassifiedAsTheEngineMarksIt()
    {
        MapTexture plain = new(1, 1, 1, 1, default, IsTransparent: false);

        ShadowReceivers.MaterialBits(null).ShouldBe(SurfaceShadowBits.None);
        ShadowReceivers.MaterialBits(plain).ShouldBe(SurfaceShadowBits.None);
        ShadowReceivers.MaterialBits(plain with { IsNoCull = true }).ShouldBe(SurfaceShadowBits.NoCull);
        ShadowReceivers.MaterialBits(plain with { IsTransparent = true }).ShouldBe(SurfaceShadowBits.AlphaTest);
        ShadowReceivers.MaterialBits(plain with { IsTranslucent = true }).ShouldBe(SurfaceShadowBits.Translucent);
        ShadowReceivers.MaterialBits(plain with { IsAdditive = true }).ShouldBe(SurfaceShadowBits.Translucent);
        ShadowReceivers.MaterialBits(plain with { IsModulate = true }).ShouldBe(SurfaceShadowBits.Translucent);
        ShadowReceivers.MaterialBits(plain with { Modulation = (1f, 1f, 1f, 0.5f) }).ShouldBe(SurfaceShadowBits.Translucent);
    }
}
