using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>The engine's decal system on the world, read out of <c>engine.dll</c> (B415).</summary>
/// <remarks>
/// **One node and one wall, so every answer is exact.** The world is a node whose plane is x = 0 carrying one face on
/// it, a 512-unit square in the y-z plane facing +x, with 1:1 texture rows (s = y, t = −z) and nothing else. A bullet
/// hole is 64 by 64 at `$decalScale` 0.16: 10.24 units across.
/// </remarks>
public sealed class WorldDecalsConformanceTests
{
    private static readonly DecalMaterial Hole = new("decals/concrete/shot1_subrect", 64, 64, 0.16f);

    /// <remarks>
    /// `R_DecalComputeBasis`, no axis: a wall (|n.z| ≤ 0.7071) takes t = (0 0 −1), then s = n × t and t = s × n; a
    /// floor takes s = (1 0 0), then t = s × n and s = n × t.
    /// </remarks>
    [Test]
    public void Basis_OnAWallAndOnAFloor_IsTheEnginesAxes()
    {
        (Vector3 s, Vector3 t) = WorldDecals.Basis(Vector3.UnitX);

        s.Y.ShouldBe(1f, 1e-5f);
        t.Z.ShouldBe(-1f, 1e-5f);

        (Vector3 fs, Vector3 ft) = WorldDecals.Basis(Vector3.UnitZ);

        fs.X.ShouldBe(1f, 1e-5f);
        ft.Y.ShouldBe(-1f, 1e-5f);
    }

    /// <remarks>
    /// A decal in the middle of the wall is the whole square: four corners 10.24 apart, centred on the shot, pushed
    /// 0.1 off the face, with coordinates spanning 0..1.
    /// </remarks>
    [Test]
    public void Shoot_TheMiddleOfAWall_PlacesTheWholeSquarePushedOffIt()
    {
        WorldDecals decals = new(Wall());

        decals.Shoot(Hole, new Vector3(0f, 100f, 100f));

        PlacedDecal placed = Placed(decals).ShouldHaveSingleItem();
        IReadOnlyList<DecalVertex> polygon = placed.Polygon;

        polygon.Count.ShouldBe(4);
        polygon.Min(v => v.Position.Y).ShouldBe(100f - 5.12f, 0.01f);
        polygon.Max(v => v.Position.Y).ShouldBe(100f + 5.12f, 0.01f);
        polygon.All(v => MathF.Abs(v.Position.X - 0.1f) < 1e-4f).ShouldBeTrue("0.1 along the plane's normal");
        polygon.Min(v => v.U).ShouldBe(0f, 1e-4f);
        polygon.Max(v => v.U).ShouldBe(1f, 1e-4f);
    }

    /// <remarks>A decal hanging over the face's edge keeps only the part on the face: half of it, cut at y = 0.</remarks>
    [Test]
    public void Shoot_AtTheEdgeOfAFace_KeepsOnlyThePartOnTheFace()
    {
        WorldDecals decals = new(Wall());

        decals.Shoot(Hole, new Vector3(0f, 0f, 100f));

        IReadOnlyList<DecalVertex> polygon = Placed(decals).ShouldHaveSingleItem().Polygon;

        polygon.Min(v => v.Position.Y).ShouldBe(0f, 0.01f);
        polygon.Max(v => v.Position.Y).ShouldBe(5.12f, 0.01f);
        polygon.Min(v => v.U).ShouldBe(0.5f, 1e-3f);
    }

    /// <remarks>The node pass tries a node's faces only within ±4 units of its plane, strictly.</remarks>
    [Test]
    public void Shoot_FiveUnitsOffTheWall_PlacesNothing()
    {
        WorldDecals decals = new(Wall());

        decals.Shoot(Hole, new Vector3(5f, 100f, 100f));

        decals.Count.ShouldBe(0);
    }

    /// <remarks>
    /// **The same spot twice is one decal**: the new one covers the old one entirely, and an overlap of 0.9 or more
    /// removes the older decal whatever `r_decal_overlap_count` says.
    /// </remarks>
    [Test]
    public void Shoot_TheSameSpotTwice_ReplacesTheOlderDecal()
    {
        WorldDecals decals = new(Wall());

        decals.Shoot(Hole, new Vector3(0f, 100f, 100f));
        decals.Shoot(Hole, new Vector3(0f, 100f, 100f));

        decals.Count.ShouldBe(1);
        Placed(decals)[0].Slot.ShouldBe(0, "the overlap is removed BEFORE a slot is sought, so the new decal reuses it");
    }

    /// <remarks>
    /// **`r_decals` caps the dynamic decals and the oldest go round a ring**: with the pool at its minimum of 64, the
    /// 65th decal takes slot 0 and the first decal is gone.
    /// </remarks>
    [Test]
    public void Shoot_OnePastTheCap_EvictsTheOldest()
    {
        WorldDecals decals = new(Wall(), maximum: 64);

        for (int index = 0; index < 65; index++)
        {
            decals.Shoot(Hole, new Vector3(0f, 20f + ((index % 16) * 20f), 20f + ((index / 16) * 20f)));
        }

        decals.Count.ShouldBe(64);
        Placed(decals).Single(one => one.Slot == 0).Polygon.Min(v => v.Position.Z)
            .ShouldBeGreaterThan(90f, "slot 0 now holds the 65th decal, in the fifth row");
    }

    /// <remarks>
    /// A Subrect's coordinates are remapped into its page. *Interpolated:* that `GetMaterialOffset`/`Scale` are the
    /// window's position and size over the atlas; the material system is closed and that pair was not read.
    /// </remarks>
    [Test]
    public void Shoot_APagedMaterial_RemapsCoordinatesIntoItsPage()
    {
        DecalMaterial paged = Hole with { Paged = true, PageOffset = (0.5f, 0.25f), PageScale = (0.125f, 0.125f) };
        WorldDecals decals = new(Wall());

        decals.Shoot(paged, new Vector3(0f, 100f, 100f));

        IReadOnlyList<DecalVertex> polygon = Placed(decals).Single().Polygon;

        polygon.Min(v => v.U).ShouldBe(0.5f, 1e-4f);
        polygon.Max(v => v.U).ShouldBe(0.625f, 1e-4f);
    }

    /// <remarks>`CalcSurfaceExtents` truncates the minimum toward zero and takes the ceiling of the maximum.</remarks>
    [Test]
    public void ExtentsOf_NegativeAndFractionalCoordinates_TruncateAndCeil()
    {
        ((int s, int t), (int es, int et)) = DecalFace.ExtentsOf(
            [new Vector3(0f, -1.5f, 2.2f), new Vector3(0f, 10.2f, 7.5f)],
            new Vector4(0f, 1f, 0f, 0f),
            new Vector4(0f, 0f, 1f, 0f));

        s.ShouldBe(-1);
        es.ShouldBe(11 - -1);
        t.ShouldBe(2);
        et.ShouldBe(8 - 2);
    }

    /// <remarks>
    /// `R_DecalShoot` into a brush model walks `model->nodes + headnode`, in the model's own frame. Here node 0 is the world,
    /// with no faces, and node 1 is a door's model holding the wall: a shot down the world's tree finds nothing, and one
    /// down the door's lands on its face and rides the door's entity.
    /// </remarks>
    [Test]
    public void Shoot_IntoABrushModelsHeadNode_LandsOnItsFaceAndRidesTheEntity()
    {
        DecalWorld wall = Wall();
        DecalWorld world = new(
            [new DecalNode(-1, -2, Vector3.UnitZ, -10000f, 0, 0), wall.Nodes[0]],
            wall.LeafFaces,
            wall.Faces);

        WorldDecals decals = new(world);

        decals.Shoot(Hole, new Vector3(0f, 100f, 100f));
        Placed(decals).ShouldBeEmpty();

        decals.Shoot(Hole, new Vector3(0f, 100f, 100f), headNode: 1, entity: 42);
        Placed(decals).ShouldHaveSingleItem().Entity.ShouldBe(42);
    }

    private static List<PlacedDecal> Placed(WorldDecals decals)
    {
        List<PlacedDecal> placed = [];

        decals.Placed(placed);

        return placed;
    }

    /// <summary>One node on the plane x = 0, holding one 512-unit wall face; both children are empty leaves.</summary>
    internal static DecalWorld Wall()
    {
        Vector3[] corners =
        [
            new(0f, 0f, 0f), new(0f, 512f, 0f), new(0f, 512f, 512f), new(0f, 0f, 512f),
        ];

        Vector4 s = new(0f, 1f, 0f, 0f);
        Vector4 t = new(0f, 0f, -1f, 0f);
        ((int, int) mins, (int, int) extents) = DecalFace.ExtentsOf(corners, s, t);

        DecalFace face = new(0, corners, Vector3.UnitX, 0f, s, t, mins, extents, true, false, false, default);

        return new DecalWorld(
            [new DecalNode(-1, -2, Vector3.UnitX, 0f, 0, 1)],
            [Array.Empty<int>(), Array.Empty<int>()],
            [face]);
    }
}
