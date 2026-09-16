using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>The client's environment on the ported driver: the map it is given and the pairs it refuses (B369, D172).</summary>
/// <remarks>
/// Read from `game/shared/physics_shared.cpp:588-667` (`PhysCreateWorld_Shared`) and `game/client/physics.cpp:198-258`
/// (`CCollisionEvent::ShouldCollide`). Synthetic conformance (D38).
/// </remarks>
public sealed class IvpRagdollWorldTests
{
    /// <remarks>
    /// **The world is solid 0 and every `staticsolid` block but index 0** — a solid the text does not name (a fluid's, here) is
    /// not created as a solid at all.
    /// </remarks>
    [Test]
    public void AddMap_TheWorldModel_CreatesSolidZeroAndTheStaticSolidsItNames()
    {
        IvpRagdollWorld world = World();
        MapPhysicsModel model = Model(0, "staticsolid {\n\"index\" \"0\"\n\"contents\" \"1\"\n}\nstaticsolid {\n\"index\" \"2\"\n\"contents\" \"1\"\n}\n", 3);

        IReadOnlyList<IvpCollisionObject> objects = world.AddMap([model], new Dictionary<int, Vector3>());

        objects.Count.ShouldBe(2, "solid 0, then staticsolid 2; solid 1 is named by nothing");
    }

    /// <remarks>
    /// **The world's `materialtable` becomes the world material table** — slot to surface index by name (`FUN_18002eeb0`), so a
    /// triangle of material 3 on world solid 0 is `metal`.
    /// </remarks>
    [Test]
    public void AddMap_TheWorldsMaterialTable_MapsTriangleSlotsToSurfaces()
    {
        IvpRagdollWorld world = World();

        IReadOnlyList<IvpCollisionObject> objects = world.AddMap(
            [Model(0, "materialtable {\n\"default\" \"1\"\n\"metal\" \"3\"\n}\n", 1)], new Dictionary<int, Vector3>());

        world.Surfaces.MaterialAt(objects[0], 3).ShouldBeSameAs(world.Surfaces.Surfaces[1]);
    }

    /// <remarks>**A brush entity's model stands at its entity's origin**, converted into IVP metres.</remarks>
    [Test]
    public void AddMap_ABrushModel_StandsAtItsEntityOrigin()
    {
        IvpRagdollWorld world = World();

        IReadOnlyList<IvpCollisionObject> objects = world.AddMap(
            [Model(3, string.Empty, 1)], new Dictionary<int, Vector3> { [3] = new Vector3(100f, 0f, 50f) });

        objects.Count.ShouldBe(1);
        IvpRigidBody core = objects[0].Core.ShouldNotBeNull();
        core.Position.X.ShouldBe(100d * IvpTransform.MetresPerInch, 1e-5d);
        core.Position.Z.ShouldBe(0d, 1e-5d);
        core.Position.Y.ShouldBe(-50d * IvpTransform.MetresPerInch, 1e-5d);
    }

    /// <remarks>
    /// **`CStaticPropMgr::CreateVPhysicsRepresentations` walks the props LAST to first** (`FUN_180203280`), and each
    /// `SOLID_VPHYSICS` prop is one object over its model's first solid (`FUN_180203060`); a prop of another solid type, or one
    /// whose model has no collide, makes none.
    /// </remarks>
    [Test]
    public void AddStaticProps_SolidVphysicsProps_AreMadeLastFirstFromTheirFirstSolid()
    {
        IvpRagdollWorld world = World();
        PhysicsLedgeTree near = Box();
        PhysicsLedgeTree far = IvpTestSurface.Boxes((Vector3.Zero, new Vector3(2f)));

        IReadOnlyList<IvpCollisionObject> objects = world.AddStaticProps(
            [
                new BspStaticProp("models/near.mdl", 0f, 0f, 0f, 0f, 0f, 0f, 1f, Solid: 6),
                new BspStaticProp("models/none.mdl", 0f, 0f, 100f, 0f, 0f, 0f, 1f, Solid: 0),
                new BspStaticProp("models/far.mdl", 0f, 0f, 200f, 0f, 0f, 0f, 1f, Solid: 6),
                new BspStaticProp("models/nophy.mdl", 0f, 0f, 300f, 0f, 0f, 0f, 1f, Solid: 6),
            ],
            model => model switch
            {
                "models/near.mdl" => new IvpStaticPropCollide(near, "metal"),
                "models/far.mdl" or "models/none.mdl" => new IvpStaticPropCollide(far, null),
                _ => null,
            });

        objects.Count.ShouldBe(2);
        objects[0].Core.ShouldNotBeNull().Position.Y.ShouldBe(-200d * IvpTransform.MetresPerInch, 1e-5d, "the last prop first");
        objects[1].Core.ShouldNotBeNull().Position.Y.ShouldBe(0d, 1e-5d);
    }

    /// <remarks>
    /// **The angles turn the object as `AngleQuaternion` does** (`mathlib_base.cpp:2063`), carried into IVP's axes: a yaw of 90°
    /// about Source Z is a turn about IVP −Y.
    /// </remarks>
    [Test]
    public void AddStaticProps_AYawedProp_IsTurnedAboutIvpMinusY()
    {
        IvpRagdollWorld world = World();

        IReadOnlyList<IvpCollisionObject> objects = world.AddStaticProps(
            [new BspStaticProp("models/near.mdl", 0f, 0f, 0f, 0f, 90f, 0f, 1f, Solid: 6)],
            _ => new IvpStaticPropCollide(Box(), null));

        (double X, double Y, double Z, double W) turn = objects[0].Core.ShouldNotBeNull().Orientation;
        turn.X.ShouldBe(0d, 1e-6d);
        turn.Y.ShouldBe(-System.Math.Sqrt(0.5d), 1e-6d);
        turn.Z.ShouldBe(0d, 1e-6d);
        turn.W.ShouldBe(System.Math.Sqrt(0.5d), 1e-6d);
    }

    /// <remarks>
    /// **`PhysCreateVirtualTerrain` makes one static object per displacement, in index order** (`physics_shared.cpp:563-582`), except
    /// where the engine's loader made no mesh — a displacement flagged <c>SURF_NOPHYSICS_COLL</c> (`engine.dll` `FUN_18016f6d0`).
    /// </remarks>
    [Test]
    public void AddVirtualTerrain_ThreeDisplacementsOneWithoutPhysics_MakesTheOtherTwoInOrder()
    {
        IvpRagdollWorld world = World();
        DisplacementCollisionTree Flat(float x) => DisplacementCollisionTree.Build(
            [new Vector3(x, 0f, 0f), new Vector3(x, 16f, 0f), new Vector3(x + 16f, 16f, 0f), new Vector3(x + 16f, 0f, 0f)],
            2,
            new (Vector3, float)[25]);

        IReadOnlyList<IvpCollisionObject> objects = world.AddVirtualTerrain(
            [(Flat(0f), false), (Flat(100f), true), (Flat(200f), false)],
            [null, null, null]);

        objects.Count.ShouldBe(2);
        IvpVirtualMeshSurfaceManager second = objects[1].Surface.ShouldBeOfType<IvpVirtualMeshSurfaceManager>();
        second.Tree.Vertices[0].X.ShouldBe(200f);
    }

    /// <remarks>
    /// **`cl_ragdoll_collide` defaults to 0** (`physics.cpp:198`), so two corpses' parts pass through each other; a corpse's own
    /// parts answer its collision rules instead.
    /// </remarks>
    [Test]
    public void ShouldCollide_PartsOfTwoRagdolls_DoNotCollide()
    {
        IvpRagdollWorld world = World();
        (IvpCollisionObject first, IvpCollisionObject second) = TwoSolids(world);
        IvpRagdoll one = Ragdoll(world);
        IvpRagdoll two = Ragdoll(world);

        world.Own(first, one, 0);
        world.Own(second, two, 0);

        world.Simulation.ShouldCollide!(first, second).ShouldBeFalse();
    }

    /// <remarks>
    /// **A solid made of playerclip alone is not in a corpse's `MASK_SOLID`** (`physics.cpp:249`), where an ordinary brush is.
    /// </remarks>
    [Test]
    public void ShouldCollide_AStaticSolidsContents_AreTestedAgainstTheCorpsesMask()
    {
        IvpRagdollWorld world = World();
        IIvpMaterial material = world.Surfaces.ObjectMaterial("default")!;
        IvpCollisionObject clip = world.AddStatic(Box(), Vector3.Zero, material, contents: 0x10000);
        IvpCollisionObject brush = world.AddStatic(Box(), new Vector3(0f, 0f, 200f), material, contents: 0x1);
        IvpCollisionObject part = world.AddStatic(Box(), new Vector3(0f, 0f, 400f), material, contents: 0x1);

        world.Own(part, Ragdoll(world), 0);

        world.Simulation.ShouldCollide!(part, clip).ShouldBeFalse();
        world.Simulation.ShouldCollide(part, brush).ShouldBeTrue();
    }

    private static IvpRagdollWorld World() =>
        new(
            1f / 66f,
            new Vector3(0f, 0f, -800f),
            new VphysicsSurfaceProps(
            [
                new VphysicsSurface("default", new SurfacePhysicsParams(0.8f, 0.25f, 0f, 0f, 0f), hasSecondFriction: false),
                new VphysicsSurface("metal", new SurfacePhysicsParams(0.8f, 0.25f, 0f, 0f, 0f), hasSecondFriction: false),
            ]));

    private static PhysicsLedgeTree Box() => IvpTestSurface.Boxes((Vector3.Zero, new Vector3(0.5f)));

    private static (IvpCollisionObject, IvpCollisionObject) TwoSolids(IvpRagdollWorld world)
    {
        IIvpMaterial material = world.Surfaces.ObjectMaterial("default")!;

        return (
            world.AddStatic(Box(), Vector3.Zero, material, contents: 0x1),
            world.AddStatic(Box(), new Vector3(0f, 0f, 200f), material, contents: 0x1));
    }

    private static MapPhysicsModel Model(int index, string text, int solids)
    {
        List<PhysicsLedgeTree?> surfaces = [];

        for (int solid = 0; solid < solids; solid++)
        {
            surfaces.Add(Box());
        }

        return new MapPhysicsModel(index, solids, [], text, []) { Surfaces = surfaces };
    }

    private static IvpRagdoll Ragdoll(IvpRagdollWorld world)
    {
        ConstraintAxis axis = new(-30f, 30f, 0f);
        PhysicsModel physics = PhysicsModel.From(
            [
                new PhysicsSolid(0, "bip_root", "", "flesh", 10f, 1f, 0f, 0f, 100f, 0f),
                new PhysicsSolid(1, "bip_child", "bip_root", "flesh", 2f, 1f, 0f, 0f, 20f, 0f),
            ],
            [new RagdollConstraint(0, 1, axis, axis, axis)],
            2,
            checksum: 0,
            collisionRules: null,
            hulls: null,
            massProperties:
            [
                new PhysicsMassProperties(Vector3.Zero, Vector3.One),
                new PhysicsMassProperties(Vector3.Zero, Vector3.One),
            ]);

        return IvpRagdoll.Create(
            world,
            RagdollBody.Build(physics, RagdollSkeletons.Straight())!,
            [(Vector3.Zero, Quaternion.Identity), (new Vector3(3f, 4f, 0f), Quaternion.Identity)]);
    }
}
