using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.ProjectedShadows;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// Brush entities as render-to-texture shadow receivers — `CClientLeafSystem::ProjectShadow` (`clientleafsystem.cpp:963-1053`),
/// `CClientShadowMgr::AddShadowToReceiver` (`clientshadowmgr.cpp:3439-3525`) and `AddShadowToBrushModel` (`engine.dll`
/// `FUN_180121f70`) (finding 107).
/// </summary>
/// <remarks>
/// **The fixture** is <see cref="ShadowReceiversConformanceTests"/>' player, origin (100, 200, 300), 20 × 20 × 72, cast
/// straight down, with its entity carried as the shadow's source; the same one-node tree on x = 0, front leaf 0 and back
/// leaf 1; and a door, entity 7, whose model is one floor face 100 units square, its own z = 0 at the door's z = 250.
/// </remarks>
public sealed class ShadowBrushReceiversConformanceTests
{
    private static readonly ShadowEntity PlayerEntity = new(
        1, null, ShadowCast.Dynamic, new Vector3(100f, 200f, 300f), (0f, 0f, 0f), new Vector3(-10f, -10f, 0f), new Vector3(10f, 10f, 72f));

    private static readonly CastShadow Player = new(
        1,
        [1],
        ShadowProjection.ForRenderToTexture(
            PlayerEntity.RenderOrigin, PlayerEntity.RenderAngles, PlayerEntity.Mins, PlayerEntity.Maxs, new Vector3(0f, 0f, -1f), 50f),
        new ShadowCell(128, 256, 64, 64),
        PlayerEntity);

    private static readonly Vector3[] DoorFace = [new(-50f, -50f, 0f), new(50f, -50f, 0f), new(50f, 50f, 0f), new(-50f, 50f, 0f)];

    /// <summary>
    /// The one-node tree, with the node's and both leaves' boxes ±4096 — `EnumerateLeavesInBox` tests a box against
    /// them (`dnode_t` mins and maxs at 12 and 18, `dleaf_t`'s at 8 and 14), where the swept walk does not.
    /// </summary>
    private static BspLeafTree Wall()
    {
        byte[] plane = new byte[20];
        BinaryPrimitives.WriteSingleLittleEndian(plane.AsSpan(0), 1f);

        byte[] node = new byte[32];
        BinaryPrimitives.WriteInt32LittleEndian(node.AsSpan(4), -0 - 1);
        BinaryPrimitives.WriteInt32LittleEndian(node.AsSpan(8), -1 - 1);
        Bound(node, 12);

        byte[] leaves = new byte[64];
        Bound(leaves, 8);
        Bound(leaves, 32 + 8);

        return BspLeafTree.FromLumps(node, plane, leaves);

        static void Bound(byte[] lump, int at)
        {
            for (int axis = 0; axis < 3; axis++)
            {
                BinaryPrimitives.WriteInt16LittleEndian(lump.AsSpan(at + (2 * axis)), -4096);
                BinaryPrimitives.WriteInt16LittleEndian(lump.AsSpan(at + 6 + (2 * axis)), 4096);
            }
        }
    }

    private static DecalFace Face(int index, Vector3[] corners) => new(
        index, corners, Vector3.UnitZ, corners[0].Z, default, default, (0, 0), (0, 0), false, false, false, default);

    private static ShadowBrush Door(
        Vector3 origin,
        (float, float, float) angles = default,
        Vector3? mins = null,
        Vector3? maxs = null,
        int entity = 7) =>
        new(new ShadowEntity(entity, null, ShadowCast.None, origin, angles, mins ?? new Vector3(-50f, -50f, -8f), maxs ?? new Vector3(50f, 50f, 0f)), 0, 1);

    /// <summary>The door's face on its own, in no world leaf: brush model faces are not in the world's leaves.</summary>
    private static List<ShadowVertex> OnDoor(ShadowBrush door, Vector3[]? face = null)
    {
        ShadowReceivers receivers = new(Wall(), new DecalWorld([], [[], []], [Face(0, face ?? DoorFace)]), [SurfaceShadowBits.None]);
        List<ShadowVertex> drawn = [];

        receivers.Begin([door]);
        receivers.Project(Player);
        receivers.BuildBrushes(drawn);

        return drawn;
    }

    /// <summary>The same face as a world floor in leaf 0, through the world's path.</summary>
    private static List<ShadowVertex> OnWorld(Vector3[] corners)
    {
        List<ShadowVertex> drawn = [];
        new ShadowReceivers(Wall(), new DecalWorld([], [[0], []], [Face(0, corners)]), [SurfaceShadowBits.DynamicShadows]).Project(Player, drawn);
        return drawn;
    }

    /// <remarks>
    /// An unturned door at (100, 200, 250) puts its face where a world floor from (50, 150) to (150, 250) at z = 250
    /// would be, and the decal is that floor's, corner for corner: placed, cut, mapped into the cell and faded the same.
    /// </remarks>
    [Test]
    public void BuildBrushes_AnUnturnedDoorUnderThePlayer_CutsTheWorldFloorsDecal()
    {
        List<ShadowVertex> expected = OnWorld([new(50f, 150f, 250f), new(150f, 150f, 250f), new(150f, 250f, 250f), new(50f, 250f, 250f)]);

        expected.ShouldNotBeEmpty("the control: the same floor in the world takes the shadow");
        OnDoor(Door(new Vector3(100f, 200f, 250f))).ShouldBe(expected);
    }

    /// <remarks>
    /// Turned 90° in yaw, a face from model (0, −50) to (100, 50) lands from world (50, 200) to (150, 300): model x runs
    /// along world +y and model y along world −x (`AngleMatrix`), and the decal is the floor's there, to rounding.
    /// </remarks>
    [Test]
    public void BuildBrushes_ATurnedDoor_IsCutWhereItsFaceIsPlaced()
    {
        Vector3[] face = [new(0f, -50f, 0f), new(100f, -50f, 0f), new(100f, 50f, 0f), new(0f, 50f, 0f)];
        List<ShadowVertex> expected = OnWorld([new(150f, 200f, 250f), new(150f, 300f, 250f), new(50f, 300f, 250f), new(50f, 200f, 250f)]);
        List<ShadowVertex> drawn = OnDoor(Door(new Vector3(100f, 200f, 250f), (0f, 90f, 0f), new Vector3(0f, -50f, -8f), new Vector3(100f, 50f, 0f)), face);

        expected.ShouldNotBeEmpty();
        drawn.Count.ShouldBe(expected.Count);

        for (int i = 0; i < drawn.Count; i++)
        {
            Vector3.Distance(drawn[i].Position, expected[i].Position).ShouldBeLessThan(1e-3f, $"corner {i}");
            Vector2.Distance(drawn[i].TexCoord, expected[i].TexCoord).ShouldBeLessThan(1e-5f, $"corner {i}");
            drawn[i].Fade.ShouldBe(expected[i].Fade);
        }
    }

    /// <remarks>
    /// Rolled 180°, the door's own +Z points down: `AddShadowToBrushModel` turns the shadow's direction into the model's
    /// frame, where it now runs along +Z, so a face whose model normal is +Z faces away and is refused — and the same
    /// face wound for −Z, which now faces up, takes it.
    /// </remarks>
    [Test]
    public void Project_ARolledDoor_TestsItsFacesInItsOwnFrame()
    {
        ShadowBrush rolled = Door(new Vector3(100f, 200f, 250f), (0f, 0f, 180f), new Vector3(-50f, -50f, 0f), new Vector3(50f, 50f, 8f));
        ShadowReceivers receivers = new(
            Wall(),
            new DecalWorld([], [[], []], [Face(0, DoorFace), Face(1, DoorFace) with { PlaneNormal = -Vector3.UnitZ }]),
            [SurfaceShadowBits.None, SurfaceShadowBits.None]);
        List<ShadowVertex> up = [];
        List<ShadowVertex> down = [];

        receivers.Begin([rolled with { FaceCount = 1 }]);
        receivers.Project(Player);
        receivers.BuildBrushes(up);

        receivers.Begin([rolled with { FirstFace = 1, FaceCount = 1 }]);
        receivers.Project(Player);
        receivers.BuildBrushes(down);

        up.ShouldBeEmpty();
        down.ShouldNotBeEmpty();
    }

    /// <remarks>
    /// The leaf system offers a shadow only to renderables in its leaves. A door at (0, 200, 250) bounded from x −30 to
    /// −1 lies wholly behind x = 0, in leaf 1, which the sweep misses: it gets nothing, though its face sits under the
    /// player and `CullReceiver` would keep it — its sphere (radius ≈ 200.6) reaches the shadow's box and its separating
    /// plane, +X, is edge-on to the cast. Bounds reaching across into leaf 0, it receives.
    /// </remarks>
    [Test]
    public void Project_ADoorInALeafTheSweepMisses_ReceivesNothing()
    {
        Vector3[] face = [new(50f, -50f, 0f), new(150f, -50f, 0f), new(150f, 50f, 0f), new(50f, 50f, 0f)];
        Vector3 at = new(0f, 200f, 250f);
        ShadowBrush behind = Door(at, mins: new Vector3(-30f, -200f, -8f), maxs: new Vector3(-1f, 200f, 0f));

        ShadowReceiverCull.Culls(Player.Projection, behind.Entity, PlayerEntity).ShouldBeFalse("the control: only the leaf refuses it");
        OnDoor(behind, face).ShouldBeEmpty();
        OnDoor(Door(at, mins: new Vector3(-30f, -200f, -8f), maxs: new Vector3(150f, 200f, 0f)), face).ShouldNotBeEmpty();
    }

    /// <remarks>
    /// The same entity, one frame bounded across both leaves and the next wholly behind x = 0: it is refiled when its
    /// bounds change, so the second frame's shadow misses it, as a fresh build does.
    /// </remarks>
    [Test]
    public void Project_ADoorWhoseBoundsChange_IsRefiled()
    {
        Vector3[] face = [new(50f, -50f, 0f), new(150f, -50f, 0f), new(150f, 50f, 0f), new(50f, 50f, 0f)];
        Vector3 at = new(0f, 200f, 250f);
        ShadowReceivers receivers = new(Wall(), new DecalWorld([], [[], []], [Face(0, face)]), [SurfaceShadowBits.None]);
        List<ShadowVertex> across = [];
        List<ShadowVertex> behind = [];

        receivers.Begin([Door(at, mins: new Vector3(-30f, -200f, -8f), maxs: new Vector3(150f, 200f, 0f))]);
        receivers.Project(Player);
        receivers.BuildBrushes(across);

        receivers.Begin([Door(at, mins: new Vector3(-30f, -200f, -8f), maxs: new Vector3(-1f, 200f, 0f))]);
        receivers.Project(Player);
        receivers.BuildBrushes(behind);

        across.ShouldNotBeEmpty();
        behind.ShouldBeEmpty();
    }

    /// <remarks>
    /// A door's tool face — `nodraw`, a trigger, a hint — is not drawn by the brush draw, so it takes no shadow: the face
    /// carries <see cref="SurfaceShadowBits.NoDraw"/>, `SURFDRAW_NODRAW`, which `AddShadowToBrushModel` tests first.
    /// </remarks>
    [Test]
    public void Project_ADoorsNodrawFace_TakesNoShadow()
    {
        ShadowReceivers receivers = new(
            Wall(),
            new DecalWorld([], [[], []], [Face(0, DoorFace)]),
            [ShadowReceivers.BitsOf(false, SurfaceProperties.NoDraw, SurfaceShadowBits.None)]);
        List<ShadowVertex> drawn = [];

        receivers.Begin([Door(new Vector3(100f, 200f, 250f))]);
        receivers.Project(Player);
        receivers.BuildBrushes(drawn);

        drawn.ShouldBeEmpty();
        OnDoor(Door(new Vector3(100f, 200f, 250f))).ShouldNotBeEmpty("the control: the same face drawn takes it");
    }

    /// <remarks>Every texinfo flag the brush draw drops (<see cref="BspFace.NotDrawnSurfaces"/>) is no-draw; others are not.</remarks>
    [Test]
    public void BitsOf_TheFlagsTheBrushDrawDrops_AreNoDraw()
    {
        foreach (SurfaceProperties dropped in new[]
        {
            SurfaceProperties.NoDraw, SurfaceProperties.Sky, SurfaceProperties.Sky2D, SurfaceProperties.Hint,
            SurfaceProperties.Skip, SurfaceProperties.Trigger,
        })
        {
            ShadowReceivers.BitsOf(false, dropped, SurfaceShadowBits.None).ShouldBe(SurfaceShadowBits.NoDraw, dropped.ToString());
        }

        ShadowReceivers.BitsOf(false, SurfaceProperties.NoLight, SurfaceShadowBits.None).ShouldBe(SurfaceShadowBits.None);
    }

    /// <remarks>
    /// Bounds that sit above the player — a slab at z 376 to 384 — find a separating plane on +Z (47 against 40) and lie
    /// against the cast, so `CullReceiver` drops the door though its face is under the player; bounds that reach down to
    /// the face find no plane and keep it.
    /// </remarks>
    [Test]
    public void Project_ADoorWhoseBoundsSitAboveTheCaster_IsCulled()
    {
        Vector3[] face = [new(-50f, -50f, -130f), new(50f, -50f, -130f), new(50f, 50f, -130f), new(-50f, 50f, -130f)];
        Vector3 at = new(100f, 200f, 380f);

        OnDoor(Door(at, mins: new Vector3(-50f, -50f, -4f), maxs: new Vector3(50f, 50f, 4f)), face).ShouldBeEmpty();
        OnDoor(Door(at, mins: new Vector3(-50f, -50f, -134f), maxs: new Vector3(50f, 50f, 4f)), face).ShouldNotBeEmpty();
    }

    /// <remarks>"Don't add a shadow cast by an object to itself" (`:3445-3450`): the receiver is the shadow's own source.</remarks>
    [Test]
    public void Project_TheCastersOwnEntity_ReceivesNothing() =>
        OnDoor(Door(new Vector3(100f, 200f, 250f), entity: 1)).ShouldBeEmpty();

    /// <remarks>A new frame forgets the last one's links.</remarks>
    [Test]
    public void BuildBrushes_AfterANewFrame_IsEmpty()
    {
        ShadowReceivers receivers = new(Wall(), new DecalWorld([], [[], []], [Face(0, DoorFace)]), [SurfaceShadowBits.None]);
        List<ShadowVertex> drawn = [];

        receivers.Begin([Door(new Vector3(100f, 200f, 250f))]);
        receivers.Project(Player);
        receivers.Begin([Door(new Vector3(100f, 200f, 250f))]);
        receivers.BuildBrushes(drawn);

        drawn.ShouldBeEmpty();
    }

    /// <remarks>
    /// `FUN_180121f70`'s test: facing is <c>dot ≤ 0</c> — so a face edge-on to the shadow takes it, where the world's
    /// −0.01 would not — or no-cull; translucent, alpha-tested and no-shadows refuse; the dynamic-shadows bit is not read.
    /// </remarks>
    [Test]
    public void TakesOnBrushModel_TheEnginesConditions_AreFacingOrNoCullAndNoneExcluded()
    {
        Vector3 down = -Vector3.UnitZ;

        ShadowReceiver.TakesOnBrushModel(down, Vector3.UnitZ, SurfaceShadowBits.None).ShouldBeTrue();
        ShadowReceiver.TakesOnBrushModel(down, Vector3.UnitX, SurfaceShadowBits.None).ShouldBeTrue();
        ShadowReceiver.TakesOnBrushModel(down, -Vector3.UnitZ, SurfaceShadowBits.None).ShouldBeFalse();
        ShadowReceiver.TakesOnBrushModel(down, -Vector3.UnitZ, SurfaceShadowBits.NoCull).ShouldBeTrue();
        ShadowReceiver.TakesOnBrushModel(down, Vector3.UnitZ, SurfaceShadowBits.Translucent).ShouldBeFalse();
        ShadowReceiver.TakesOnBrushModel(down, Vector3.UnitZ, SurfaceShadowBits.AlphaTest).ShouldBeFalse();
        ShadowReceiver.TakesOnBrushModel(down, Vector3.UnitZ, SurfaceShadowBits.NoShadows).ShouldBeFalse();
        ShadowReceiver.TakesOnBrushModel(down, Vector3.UnitZ, SurfaceShadowBits.NoDraw | SurfaceShadowBits.NoCull).ShouldBeFalse();
    }

    /// <remarks>
    /// The drawn `*N` entities, at their pose, bounded by their model; the world (`*0`), a studio model and a model the map
    /// does not have are not brush receivers, nor one rendered at alpha 0 in a mode above normal — while alpha 0 in the
    /// normal mode still is (`ShouldReceiveProjectedTextures`' flashlight branch).
    /// </remarks>
    [Test]
    public void Brushes_TheDrawnSubmodels_AreTheirModelsWhereTheyStand()
    {
        ScenePose pose = new() { X = 1f, Y = 2f, Z = 3f, Pitch = 4f, Yaw = 5f, Roll = 6f };
        BspModel[] models = [default, new((-1f, -2f, -3f), (4f, 5f, 6f), (0f, 0f, 0f), 12, 30, 4)];
        List<ShadowBrush> into = [new(default, 9, 9)];

        ShadowEntities.Brushes(
            [
                new SceneProp(7, "*1", SceneModelKind.Brush, pose),
                new SceneProp(8, "*1", SceneModelKind.Brush, pose with { RenderMode = 1, RenderAlpha = 0 }),
                new SceneProp(9, "*1", SceneModelKind.Brush, pose with { RenderAlpha = 0 }),
                new SceneProp(10, "*0", SceneModelKind.Brush, pose),
                new SceneProp(11, "*2", SceneModelKind.Brush, pose),
                new SceneProp(12, "models/crate.mdl", SceneModelKind.Studio, pose),
            ],
            models,
            into);

        into.Count.ShouldBe(2);
        into[0].ShouldBe(new ShadowBrush(
            new ShadowEntity(7, null, ShadowCast.None, new Vector3(1f, 2f, 3f), (4f, 5f, 6f), new Vector3(-1f, -2f, -3f), new Vector3(4f, 5f, 6f), "*1"),
            30,
            4));
        into[1].Entity.EntityIndex.ShouldBe(9);
    }
}
