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
    /// **Every object's core stands at its surface's mass centre** (`FUN_180073df0`, `IvpObjectTemplate::ConstructCore`): the surface
    /// manager's slot 1 gives the centre, the object is set back from it, and the radius is the surface's own about it. A static solid
    /// is built the same way as a corpse's part. *It stood at the object's origin instead*, its radius widened by the distance to the
    /// centre — so a displacement, whose object is at the world's origin, was a sphere around the map's middle, and every body on f12
    /// was paired with all 922 of them (B369).
    /// </remarks>
    [Test]
    public void AddStatic_ASolidWhoseMassCentreIsAwayFromItsOrigin_StandsItsCoreThere()
    {
        IvpRagdollWorld world = World();
        byte[] bytes = IvpTestSurface.Bytes((new Vector3(10f, 0f, 0f), new Vector3(0.5f)));
        System.BitConverter.TryWriteBytes(System.MemoryExtensions.AsSpan(bytes, 0x00), 10f);
        PhysicsLedgeTree surface = PhysicsHull.Tree(bytes).ShouldNotBeNull();
        surface.MassCenter.X.ShouldBe(10f, "the control: the header's mass centre was read");

        IvpCollisionObject made = world.AddStatic(surface, Vector3.Zero, world.Surfaces.ObjectMaterial("default")!);

        IvpRigidBody core = made.Core.ShouldNotBeNull();
        core.Position.X.ShouldBe(10d, 1e-6d);
        core.ObjectOffset.ShouldBe((-10f, 0f, 0f));
        core.Radius.ShouldBe(surface.Radius, "the surface's own radius, not widened by the distance to its centre");
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
    /// **A corpse's own rules apply to its first pairs too.** `RagdollCreate` hands every object its game data when it is made, and
    /// `RagdollActivate` enables collisions only once the joints and rules are set (`ragdoll_shared.cpp:193`, `:375-382`). Two joined
    /// elements driven into each other — a pair the default rules disable — must never make an impact. *The control*: the same two
    /// with the joint's block renamed, so the rules let them collide, make one.
    /// </remarks>
    [Test]
    public void Create_TwoJoinedElementsOverlapping_NeverCollide()
    {
        IvpRagdollWorld world = World();
        byte[] surface = IvpTestSurface.Bytes((Vector3.Zero, new Vector3(0.1f)));
        byte[] file =
        [
            .. System.BitConverter.GetBytes(16),
            .. System.BitConverter.GetBytes(0x59485056),
            .. System.BitConverter.GetBytes(2),
            .. System.BitConverter.GetBytes(0),
            .. System.BitConverter.GetBytes(surface.Length), .. surface,
            .. System.BitConverter.GetBytes(surface.Length), .. surface,
            .. System.Text.Encoding.ASCII.GetBytes("""
                solid {
                  "index" "0"
                  "name" "bip_root"
                  "mass" "10.0"
                  "surfaceprop" "default"
                }
                solid {
                  "index" "1"
                  "name" "bip_child"
                  "parent" "bip_root"
                  "mass" "2.0"
                  "surfaceprop" "default"
                }
                ragdollconstraint {
                  "parent" "0"
                  "child" "1"
                  "xmin" "-30" "xmax" "30" "ymin" "-30" "ymax" "30" "zmin" "-30" "zmax" "30"
                }
                """),
        ];

        RagdollBody body = RagdollBody.Build(PhysicsModel.Read(file), RagdollSkeletons.Straight()).ShouldNotBeNull();
        body.Elements[1].Surface.ShouldNotBeNull("the control: the elements carry surfaces, so they are collided at all");

        // A child a foot above its root, both 8 inches across, driven down through it.
        IvpRagdoll ragdoll = IvpRagdoll.Create(
            world, body, [(Vector3.Zero, Quaternion.Identity), (new Vector3(0f, 0f, 12f), Quaternion.Identity)]);
        ragdoll.Kill(new Vector3(0f, 0f, -20000f), forceBone: 1);
        world.SimulateFrames(0.3d);

        world.Simulation.Environment.Impacts.ShouldBe(0);
    }

    /// <remarks>
    /// **And filing makes no pair of them either**: two joined elements created on top of each other are in each other's range the
    /// moment the second is filed, and the rules must already be asked about both. *The port filed each element before registering
    /// its owner, so that pair was made — and on `cp_process_f12` a scout's two forbidden forearm bones held a contact.*
    /// </remarks>
    [Test]
    public void Create_TwoJoinedElementsOnTopOfEachOther_FileNoPairBetweenThem()
    {
        IvpRagdollWorld world = World();
        RagdollBody body = RagdollBody.Build(PhysicsModel.Read(JoinedPhy()), RagdollSkeletons.Straight()).ShouldNotBeNull();

        IvpRagdoll ragdoll = IvpRagdoll.Create(world, body, [(Vector3.Zero, Quaternion.Identity), (Vector3.Zero, Quaternion.Identity)]);

        ragdoll.Bodies[1].Objects[0].Node.ShouldNotBeNull().Watchers.ShouldBeEmpty();
    }

    private static byte[] JoinedPhy()
    {
        byte[] surface = IvpTestSurface.Bytes((Vector3.Zero, new Vector3(0.1f)));

        return
        [
            .. System.BitConverter.GetBytes(16),
            .. System.BitConverter.GetBytes(0x59485056),
            .. System.BitConverter.GetBytes(2),
            .. System.BitConverter.GetBytes(0),
            .. System.BitConverter.GetBytes(surface.Length), .. surface,
            .. System.BitConverter.GetBytes(surface.Length), .. surface,
            .. System.Text.Encoding.ASCII.GetBytes("""
                solid {
                  "index" "0"
                  "name" "bip_root"
                  "mass" "10.0"
                  "surfaceprop" "default"
                }
                solid {
                  "index" "1"
                  "name" "bip_child"
                  "parent" "bip_root"
                  "mass" "2.0"
                  "surfaceprop" "default"
                }
                ragdollconstraint {
                  "parent" "0"
                  "child" "1"
                  "xmin" "-30" "xmax" "30" "ymin" "-30" "ymax" "30" "zmin" "-30" "zmax" "30"
                }
                """),
        ];
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

        world.Own(first.Core.ShouldNotBeNull(), one, 0);
        world.Own(second.Core.ShouldNotBeNull(), two, 0);

        world.Simulation.ShouldCollide!(first, second).ShouldBeFalse();
    }

    /// <remarks>
    /// **Two gibs never collide** (B409): TF sets every gib to `COLLISION_GROUP_DEBRIS` (`BreakModelCreateSingle`,
    /// `physpropclientside.cpp`, `#ifdef TF_CLIENT_DLL`), and `CGameRules::ShouldCollide` returns false for debris against
    /// anything but `COLLISION_GROUP_NONE` and `PUSHAWAY` (`gamerules.cpp:692-717`).
    /// </remarks>
    [Test]
    public void ShouldCollide_TwoGibs_DoNotCollide()
    {
        IvpRagdollWorld world = World();
        (IvpCollisionObject first, IvpCollisionObject second) = TwoSolids(world);

        world.Own(first.Core.ShouldNotBeNull(), Gib(world), 0);
        world.Own(second.Core.ShouldNotBeNull(), Gib(world), 0);

        world.Simulation.ShouldCollide!(first, second).ShouldBeFalse();
    }

    /// <remarks>
    /// **A gib and a corpse collide** (B409): the corpse's group is never set on the client — `InitAsClientRagdoll` does not call
    /// `SetCollisionGroup` — so it is `COLLISION_GROUP_NONE`, which debris collides with; and `cl_ragdoll_collide` asks only when
    /// BOTH objects are part of a ragdoll (`physics.cpp:241`).
    /// </remarks>
    [Test]
    public void ShouldCollide_AGibAndACorpse_Collide()
    {
        IvpRagdollWorld world = World();
        (IvpCollisionObject first, IvpCollisionObject second) = TwoSolids(world);

        world.Own(first.Core.ShouldNotBeNull(), Gib(world), 0);
        world.Own(second.Core.ShouldNotBeNull(), Ragdoll(world), 0);

        world.Simulation.ShouldCollide!(first, second).ShouldBeTrue();
    }

    /// <remarks>
    /// **`CPhysicsObject::AddVelocity` (`18001a6c0`, vtable slot 52), read from the binary**: the linear part into `core+0x120` as
    /// `(x, −z, y) × 0.0254`, the angular part into `core+0x110` as `(x, −z, y) × 0.017453292` — DEGREES a second, in the same
    /// axes. A gib's spin is `AngularImpulse( RandomFloat( 0, 120 ), RandomFloat( 0, 120 ), 0 )` in those units.
    /// </remarks>
    [Test]
    public void AddVelocity_AThrowAndASpinInDegrees_AreStagedInIvpMetresAndRadians()
    {
        IvpRagdollWorld world = World();
        IvpRagdoll gib = Gib(world);
        IvpRigidBody core = gib.Bodies[0];
        (float X, float Y, float Z) velocityBefore = core.PendingVelocity;
        (float X, float Y, float Z) spinBefore = core.PendingAngularVelocity;

        gib.AddVelocity(new Vector3(100f, 0f, 50f), new Vector3(90f, 30f, 60f));

        core.PendingVelocity.X.ShouldBe(velocityBefore.X + (100f * 0.0254f), 1e-6f);
        core.PendingVelocity.Y.ShouldBe(velocityBefore.Y - (50f * 0.0254f), 1e-6f);
        core.PendingVelocity.Z.ShouldBe(velocityBefore.Z, 1e-6f);
        core.PendingAngularVelocity.X.ShouldBe(spinBefore.X + (90f * 0.017453292f), 1e-6f);
        core.PendingAngularVelocity.Y.ShouldBe(spinBefore.Y - (60f * 0.017453292f), 1e-6f);
        core.PendingAngularVelocity.Z.ShouldBe(spinBefore.Z + (30f * 0.017453292f), 1e-6f);
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

        world.Own(part.Core.ShouldNotBeNull(), Ragdoll(world), 0);

        world.Simulation.ShouldCollide!(part, clip).ShouldBeFalse();
        world.Simulation.ShouldCollide(part, brush).ShouldBeTrue();
    }

    /// <remarks>
    /// **An element built with a drag coefficient is under the environment's drag, with its bases computed** — the object builder's
    /// `EnableDrag` for a non-zero parameter (`FUN_18001b340`). The `.phy` names no drag, so the parameter is the engine's `1`; a
    /// 0.2-metre box of mass 1 has every basis lane above zero.
    /// </remarks>
    [Test]
    public void Create_AnElementWithTheDefaultDrag_IsFiledUnderTheDragWithItsBases()
    {
        IvpRagdollWorld world = World();
        RagdollBody body = RagdollBody.Build(PhysicsModel.Read(JoinedPhy()), RagdollSkeletons.Straight()).ShouldNotBeNull();

        IvpRagdoll ragdoll = IvpRagdoll.Create(world, body, [(Vector3.Zero, Quaternion.Identity), (new Vector3(0f, 0f, 50f), Quaternion.Identity)]);

        IvpRigidBody core = ragdoll.Bodies[0];
        core.Controllers.ShouldContain(world.Simulation.Drag);
        core.DragCoefficient.ShouldBe(1f);
        core.DragBasis.X.ShouldBeGreaterThan(0f);
        core.AngularDragBasis.Z.ShouldBeGreaterThan(0f);
    }

    /// <remarks>
    /// **`CPhysicsEnvironment::Simulate` (`FUN_180015310`), read from the disassembly 2026-09-16**: the constructor leaves the
    /// fixed-step byte `+0xcf` set, and a delta equal to the step (`UCOMISS` against `(float)env+0x108`) simulates to
    /// `env+0x198 + (double)((float)step · 1.9999895f)` (`FUN_180082780`, `DAT_1800ec288`). The first PSI is queued at zero and the
    /// rebase base is zero, so the first frame runs two PSIs — the second at `(float)step` — and the clock ends just short of a
    /// third.
    /// </remarks>
    [Test]
    public void Simulate_TheStepFromTheStart_RunsTwoPsisOnTheFixedPath()
    {
        IvpRagdollWorld world = World();
        float step = 1f / 66f;

        world.Simulate(step);

        world.Simulation.Environment.RebaseBase.ShouldBe((double)step);
        world.Simulation.Now.ShouldBe((double)(step * 1.9999895f));
    }

    /// <remarks>**Each later frame runs one PSI**: the target moves from the last PSI's own time, not from the clock.</remarks>
    [Test]
    public void Simulate_TheStepAgain_RunsOnePsiFromTheLastOne()
    {
        IvpRagdollWorld world = World();
        float step = 1f / 66f;
        world.Simulate(step);

        world.Simulate(step);

        // The target is read before the frame runs: from the first frame's last PSI, at the step.
        world.Simulation.Environment.RebaseBase.ShouldBe((double)step + step);
        world.Simulation.Now.ShouldBe((double)step + (double)(step * 1.9999895f));
    }

    /// <remarks>
    /// **Any other delta clears the byte and simulates to the clock plus the delta** (`FUN_180082540`), and the byte stays clear,
    /// so the step itself then takes the same path.
    /// </remarks>
    [Test]
    public void Simulate_AnotherDelta_AdvancesTheClockByItFromThenOn()
    {
        IvpRagdollWorld world = World();
        float step = 1f / 66f;

        world.Simulate(0.02f);
        world.Simulate(step);

        world.Simulation.Now.ShouldBe((double)0.02f + step);
    }

    /// <remarks>
    /// **A delta over one second, or not over `1e-4`, simulates nothing** (`COMISS`/`JA` against `1f`, `COMISD`/`JBE` against
    /// `DAT_1800ec278`), and **one over a tenth is cut to `0.1f`** (`COMISD` against `DAT_1800ec280`, `DAT_1800ea968` stored).
    /// </remarks>
    [TestCase(1.5f, 0d)]
    [TestCase(0.00005f, 0d)]
    [TestCase(0.5f, (double)0.1f)]
    public void Simulate_ADeltaOutsideTheClamp_IsSkippedOrCut(float delta, double now)
    {
        IvpRagdollWorld world = World();

        world.Simulate(delta);

        world.Simulation.Now.ShouldBe(now);
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

    /// <summary>A gib — one prop body from a single-solid <c>.phy</c> — made as <c>BreakModelCreateSingle</c> makes one.</summary>
    private static IvpRagdoll Gib(IvpRagdollWorld world) =>
        IvpRagdoll.CreateProp(
            world,
            RagdollBody.BuildProp(RagdollBodyConformanceTests.Prop("gib_reference"))!,
            [(Vector3.Zero, Quaternion.Identity)]);
}
