using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// The client's physics environment on the ported driver — one <see cref="IvpSimulation"/> for the map and every corpse, in IVP's
/// own axes and metres (B369, D172, D173).
/// </summary>
/// <remarks>
/// **Everything Source hands in crosses once, here and in <see cref="IvpRagdoll"/>** — `CPhysicsEnvironment` and `CPhysicsObject`
/// convert on the way in and back (<see cref="IvpTransform"/>); nothing inside the driver sees an inch.
///
/// **What the client gives its environment** (`game/client/physics.cpp:163-187`): gravity and the step, nothing else, so the limits
/// are the constructor's own <see cref="IvpPerformanceSettings.Defaults"/> taken at the constructor's step, <c>1/66</c>
/// (`FUN_1800114f0`); the rest delay is the constructor's <c>env+0xc8 = 0.3f</c>. *The rest check countdown's starting value is not
/// read*; fifteen, the value it is reset to, is used.
/// </remarks>
public sealed class IvpRagdollWorld
{
    /// <summary>The constructor's own step, <c>env+0x108 = 1/66</c>, at which the default limits are taken.</summary>
    private const double ConstructorStep = 1d / 66d;

    /// <summary><c>env+0xc8 = 0.3f</c>, written by <c>FUN_180080d90</c>.</summary>
    private const float RestDelay = 0.3f;

    private readonly IvpRandom _random = new();
    private readonly Dictionary<IvpCollisionObject, (IvpRagdoll Ragdoll, int Element)> _owners = [];
    private readonly Dictionary<IvpCollisionObject, int> _contents = [];

    /// <summary>Makes the environment.</summary>
    /// <param name="step">The simulation timestep — <c>SetSimulationTimestep</c>, the demo's tick interval.</param>
    /// <param name="gravity">Source gravity, in inches per second squared — <c>(0, 0, −sv_gravity)</c>.</param>
    /// <param name="surfaces">The game's surfaces, the environment's material manager.</param>
    /// <exception cref="ArgumentNullException"><paramref name="surfaces"/> is null.</exception>
    public IvpRagdollWorld(float step, Vector3 gravity, VphysicsSurfaceProps surfaces)
    {
        Surfaces = surfaces ?? throw new ArgumentNullException(nameof(surfaces));

        (float X, float Y, float Z) ivpGravity = IvpTransform.Position(gravity.X, gravity.Y, gravity.Z);

        IvpImpactEnvironment environment = new()
        {
            Step = step,
            InverseStep = 1d / step,
            Limits = IvpAnomalyLimits.FromPerformanceSettings(IvpPerformanceSettings.Defaults, ConstructorStep),
            Anomalies = new VphysicsAnomalyManager(null),
            Materials = surfaces,
            GravityLength = MathF.Sqrt((ivpGravity.X * ivpGravity.X) + (ivpGravity.Y * ivpGravity.Y) + (ivpGravity.Z * ivpGravity.Z)),
            RestDelay = RestDelay,
            RestCheckCountdown = 15,
        };

        Simulation = new IvpSimulation(environment, ivpGravity, _random.Next)
        {
            ShouldCollide = ShouldCollide,
        };

        Simulation.Start();
    }

    /// <summary>The driver.</summary>
    public IvpSimulation Simulation { get; }

    /// <summary>The game's surfaces.</summary>
    public VphysicsSurfaceProps Surfaces { get; }

    /// <summary>Adds a static solid of the map's collide — <c>CreatePolyObjectStatic</c>.</summary>
    /// <param name="surface">The solid's compact surface.</param>
    /// <param name="origin">Where the brush model stands, in Source units.</param>
    /// <param name="material">The solid's material.</param>
    /// <returns>Its object.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public IvpCollisionObject AddStatic(PhysicsLedgeTree surface, Vector3 origin, IIvpMaterial material) =>
        AddStatic(surface, origin, material, IvpWorldCollision.ContentsSolid);

    /// <summary>Adds a static solid made of some contents — <c>CreatePolyObjectStatic</c> then <c>SetContents</c>.</summary>
    /// <param name="surface">The solid's compact surface.</param>
    /// <param name="origin">Where the brush model stands, in Source units.</param>
    /// <param name="material">The solid's material.</param>
    /// <param name="contents">Its <c>CONTENTS_*</c> mask — <c>physics_shared.cpp:648</c>; a new object's is <c>CONTENTS_SOLID</c>.</param>
    /// <returns>Its object.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public IvpCollisionObject AddStatic(PhysicsLedgeTree surface, Vector3 origin, IIvpMaterial material, int contents) =>
        AddStatic(surface, origin, Quaternion.Identity, material, contents);

    /// <summary>Adds every solid static prop — <c>CStaticPropMgr::CreateVPhysicsRepresentations</c>.</summary>
    /// <param name="props">The map's static props, in lump order.</param>
    /// <param name="collide">A model's first solid and that solid's surface property, or null when it has no collide.</param>
    /// <returns>The objects made, in the order they were made.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **Read from `engine.dll`** (GhidraMCP): `FUN_180203280` walks the props from the LAST down, and `FUN_180203060` makes one
    /// `CreatePolyObjectStatic(vcollide->solids[0], GetSurfaceIndex(first "solid" block's surfaceprop), origin, angles)` for a
    /// `SOLID_VPHYSICS` (6) prop — the first solid only, however many the model has. A `SOLID_VPHYSICS` prop whose model has no
    /// collide was cleared to `SOLID_NONE` at load (`FUN_180206590`). *`SOLID_BBOX` (2) — `BBoxToCollide` of the model's bounds — is
    /// not carried yet*; any other type is refused with "bogus solid type". The angles go through <c>AngleQuaternion</c>
    /// (`mathlib_base.cpp:2063`); vphysics' own conversion goes by way of a matrix, which differs in rounding only.
    /// </remarks>
    public IReadOnlyList<IvpCollisionObject> AddStaticProps(
        IReadOnlyList<BspStaticProp> props, Func<string, IvpStaticPropCollide?> collide)
    {
        ArgumentNullException.ThrowIfNull(props);
        ArgumentNullException.ThrowIfNull(collide);

        List<IvpCollisionObject> made = [];

        for (int index = props.Count - 1; index >= 0; index--)
        {
            BspStaticProp prop = props[index];

            if (prop.Solid != SolidVphysics || collide(prop.Model) is not { } found ||
                Surfaces.ObjectMaterial(found.SurfaceProp) is not { } material)
            {
                continue;
            }

            made.Add(AddStatic(
                found.Surface,
                new Vector3(prop.X, prop.Y, prop.Z),
                AngleQuaternion(prop.Pitch, prop.Yaw, prop.Roll),
                material,
                IvpWorldCollision.ContentsSolid));
        }

        return made;
    }

    /// <summary>Adds the displacements' virtual terrain — <c>PhysCreateVirtualTerrain</c>.</summary>
    /// <param name="displacements">Each displacement's collision tree and <c>SURF_NOPHYSICS_COLL</c> flag, by displacement index; null for an index no face names.</param>
    /// <param name="hulls">Each displacement's <c>LUMP_PHYSDISP</c> blob, by index; empty or null entries for none.</param>
    /// <returns>The objects made, in index order.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **Read from published source and `engine.dll`**: the engine's loader makes a mesh per displacement unless flagged
    /// (`FUN_18016f6d0`), and the game makes each a static object at the origin with surface `default`, in index order
    /// (`physics_shared.cpp:563-582`), when the world's collide carries a `virtualterrain` block — the caller's test. A displacement with
    /// no hull blob has no root ledge, so nothing collides with it, as the engine's does. *Contents are left <c>CONTENTS_SOLID</c>*:
    /// nothing calls <c>SetContents</c> on terrain.
    /// </remarks>
    public IReadOnlyList<IvpCollisionObject> AddVirtualTerrain(
        IReadOnlyList<(DisplacementCollisionTree Tree, bool NoPhysics)?> displacements, IReadOnlyList<byte[]?> hulls)
    {
        ArgumentNullException.ThrowIfNull(displacements);
        ArgumentNullException.ThrowIfNull(hulls);

        List<IvpCollisionObject> made = [];

        if (Surfaces.ObjectMaterial("default") is not { } material)
        {
            return made;
        }

        for (int index = 0; index < displacements.Count; index++)
        {
            if (displacements[index] is not (DisplacementCollisionTree tree, false))
            {
                continue;
            }

            byte[] hull = index < hulls.Count ? hulls[index] ?? [] : [];
            IvpVirtualMeshSurfaceManager mesh = new(PhysicsVirtualMesh.Build(tree.Vertices, tree.Triangles, hull), tree);

            IvpRigidBody core = new()
            {
                Immovable = true,
                Orientation = (0d, 0d, 0d, 1d),
                WorkingOrientation = (0d, 0d, 0d, 1d),
                CoreMatrix = IvpMatrix.FromRotation((0d, 0d, 0d, 1d), (0d, 0d, 0d)),
                InverseMass = 0f,
                InverseInertia = (0f, 0f, 0f),
            };

            IvpCollisionObject collisionObject = Simulation.Collide(core, mesh, material);
            _contents[collisionObject] = IvpWorldCollision.ContentsSolid;
            made.Add(collisionObject);
        }

        return made;
    }

    /// <summary><c>SOLID_VPHYSICS</c>, <c>public/const.h</c>.</summary>
    private const int SolidVphysics = 6;

    /// <summary><c>AngleQuaternion( const QAngle &amp;, Quaternion &amp; )</c>, <c>mathlib_base.cpp:2063-2096</c>.</summary>
    private static Quaternion AngleQuaternion(float pitch, float yaw, float roll)
    {
        const float HalfRadians = MathF.PI / 180f * 0.5f;

        float sy = MathF.Sin(yaw * HalfRadians), cy = MathF.Cos(yaw * HalfRadians);
        float sp = MathF.Sin(pitch * HalfRadians), cp = MathF.Cos(pitch * HalfRadians);
        float sr = MathF.Sin(roll * HalfRadians), cr = MathF.Cos(roll * HalfRadians);

        float srXcp = sr * cp, crXsp = cr * sp, crXcp = cr * cp, srXsp = sr * sp;

        return new Quaternion(
            (srXcp * cy) - (crXsp * sy),
            (crXsp * cy) + (srXcp * sy),
            (crXcp * sy) - (srXsp * cy),
            (crXcp * cy) + (srXsp * sy));
    }

    private IvpCollisionObject AddStatic(PhysicsLedgeTree surface, Vector3 origin, Quaternion angles, IIvpMaterial material, int contents)
    {
        ArgumentNullException.ThrowIfNull(surface);

        (float x, float y, float z) = IvpTransform.Position(origin.X, origin.Y, origin.Z);
        (double X, double Y, double Z, double W) rotation = IvpRagdoll.Rotation(angles);

        IvpRigidBody core = new()
        {
            Immovable = true,
            Position = (x, y, z),
            Orientation = rotation,
            WorkingOrientation = rotation,
            CoreMatrix = IvpMatrix.FromRotation(rotation, (x, y, z)),
            InverseMass = 0f,
            InverseInertia = (0f, 0f, 0f),
        };

        IvpCollisionObject made = Simulation.Collide(core, surface, material);

        _contents[made] = contents;

        return made;
    }

    /// <summary>Adds the map's collide — <c>PhysCreateWorld_Shared</c> for the world, each brush entity's model at its origin.</summary>
    /// <param name="models">The brush models of <c>LUMP_PHYSCOLLIDE</c>.</param>
    /// <param name="origins">Each brush model's entity origin, in Source units, by model index.</param>
    /// <returns>The static objects made, in order.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **The world is solid 0, then every `solid`/`staticsolid` block but index 0** (`game/shared/physics_shared.cpp:588-667`),
    /// each with its block's contents and the surface `default`. A solid no such block names — a `fluid`'s — is not a solid.
    /// *Fluid controllers and virtual terrain are not carried.* **A brush entity's model** is every solid at the entity's origin,
    /// with the contents its text declares or `CONTENTS_SOLID` — the rule the drawn map's collision already follows, carried rather
    /// than read: *which client entities put a brush model into the client's environment is not read*.
    /// </remarks>
    public IReadOnlyList<IvpCollisionObject> AddMap(IReadOnlyList<MapPhysicsModel> models, IReadOnlyDictionary<int, Vector3> origins)
    {
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(origins);

        List<IvpCollisionObject> made = [];

        if (Surfaces.ObjectMaterial("default") is not { } material)
        {
            return made;
        }

        foreach (MapPhysicsModel model in models)
        {
            MapSurfaceTable table = MapSurfaceTable.Parse(model.Text);

            if (model.ModelIndex == 0)
            {
                // `materialtable` (`physics_shared.cpp:669-676`): `FUN_18002eeb0` writes `table[atoi(value)] = GetSurfaceIndex(key)`
                // for an unsigned index under 0x80 into a zeroed 128. *An empty block is not told apart from none*.
                if (table.Materials.Count > 0)
                {
                    int[] world = new int[128];

                    foreach ((int slot, string name) in table.Materials)
                    {
                        if ((uint)slot < 0x80)
                        {
                            world[slot] = Surfaces.GetSurfaceIndex(name);
                        }
                    }

                    Surfaces.SetWorldMaterialIndexTable(world);
                }

                AddSolid(made, model, 0, Vector3.Zero, material, IvpWorldCollision.ContentsSolid);

                foreach ((int index, int contents) in table.StaticSolids)
                {
                    if (index != 0)
                    {
                        AddSolid(made, model, index, Vector3.Zero, material, contents);
                    }
                }

                continue;
            }

            Vector3 origin = origins.TryGetValue(model.ModelIndex, out Vector3 placed) ? placed : Vector3.Zero;
            Dictionary<int, int> declared = [];

            foreach ((int index, int contents) in table.StaticSolids)
            {
                declared[index] = contents;
            }

            for (int solid = 0; solid < model.Surfaces.Count; solid++)
            {
                AddSolid(
                    made, model, solid, origin, material,
                    declared.TryGetValue(solid, out int contents) ? contents : IvpWorldCollision.ContentsSolid);
            }
        }

        return made;
    }

    private void AddSolid(List<IvpCollisionObject> made, MapPhysicsModel model, int solid, Vector3 origin, IIvpMaterial material, int contents)
    {
        if (solid >= 0 && solid < model.Surfaces.Count && model.Surfaces[solid] is { } surface)
        {
            made.Add(AddStatic(surface, origin, material, contents));
        }
    }

    /// <summary>Runs the environment forward by some seconds.</summary>
    /// <param name="seconds">How long.</param>
    public void Simulate(double seconds) => Simulation.Advance(Simulation.Now + seconds);

    /// <summary>Files an object as one ragdoll's element, for the rules.</summary>
    internal void Own(IvpCollisionObject collisionObject, IvpRagdoll ragdoll, int element) =>
        _owners[collisionObject] = (ragdoll, element);

    /// <summary>The game's rules for a pair — <c>CCollisionEvent::ShouldCollide</c>, <c>game/client/physics.cpp:200-258</c>.</summary>
    /// <remarks>
    /// **In the engine's order, for what this world holds**: one corpse's own parts answer its <c>collisionrules</c>; two corpses'
    /// parts never collide, <c>cl_ragdoll_collide</c> being <c>"0"</c>; a static solid collides with a corpse only if its contents
    /// meet the corpse's <c>MASK_SOLID</c> (a corpse's own objects are <c>CONTENTS_SOLID</c>, inside the world's mask). *The game
    /// rules' collision groups between a corpse and the world are not carried*: debris against the world collides.
    /// </remarks>
    private bool ShouldCollide(IvpCollisionObject first, IvpCollisionObject second)
    {
        bool firstPart = _owners.TryGetValue(first, out (IvpRagdoll Ragdoll, int Element) a);
        bool secondPart = _owners.TryGetValue(second, out (IvpRagdoll Ragdoll, int Element) b);

        if (firstPart && secondPart)
        {
            return ReferenceEquals(a.Ragdoll, b.Ragdoll) && a.Ragdoll.Body.ShouldCollide(a.Element, b.Element);
        }

        IvpCollisionObject other = firstPart ? second : first;
        int contents = _contents.TryGetValue(other, out int declared) ? declared : IvpWorldCollision.ContentsSolid;

        return (contents & IvpWorldCollision.MaskSolid) != 0;
    }
}

/// <summary>What a static prop's model gives <c>CreatePolyObjectStatic</c>: its first solid and that solid's surface property.</summary>
/// <param name="Surface">The first solid's compact surface.</param>
/// <param name="SurfaceProp">The first <c>solid</c> block's <c>surfaceprop</c>; null reads as unnamed, which falls to <c>default</c>.</param>
public sealed record IvpStaticPropCollide(PhysicsLedgeTree Surface, string? SurfaceProp);

/// <summary>A model's ragdoll on the ported driver, in IVP space — <c>RagdollCreate</c> through <c>CPhysicsObject</c> (B369, D172).</summary>
/// <remarks>
/// **Each element is made the way vphysics makes an object from the `.phy`'s own numbers**: the core at the surface's mass centre
/// (<c>FUN_180073df0</c>), its inertia from the surface's per-kilogram inertia and the solid's parameters (<see cref="IvpObjectTemplate"/>),
/// the object at minus the mass centre inside it, the collision object over the solid's own compact surface — all in the file's IVP
/// axes and metres, untouched. Only the starting pose, the killing force and the pose read back cross the boundary.
///
/// **Joints go through the engine's own remap** — each Source limit into its IVP slot, radians, axis 2 negated
/// (<see cref="RagdollJointLimits"/>) — and the attached frame is conjugated by the axis change, `P·A·Pᵀ`, so its columns are the
/// IVP slots' axes; the reference frame is the identity in either space.
/// </remarks>
public sealed class IvpRagdoll
{
    /// <summary><c>DAT_1800fcf98</c>: a mass centre nearer the origin than this, squared in metres, leaves the object at the core.</summary>
    private const float NegligibleSquared = 1e-16f;

    /// <summary><c>RAGDOLL_SLEEP_TOLERANCE</c>, <c>game/client/ragdoll.cpp:265</c>, in inches.</summary>
    private const double SleepTolerance = 1.0;

    /// <summary><c>ragdoll_sleepaftertime</c>'s default.</summary>
    private const float SleepAfterTime = 5f;

    private readonly IvpRagdollWorld _world;
    private readonly IvpRigidBody[] _bodies;
    private (double X, double Y, double Z) _origin;
    private float _since;

    private IvpRagdoll(IvpRagdollWorld world, RagdollBody body, IvpRigidBody[] bodies)
    {
        _world = world;
        Body = body;
        _bodies = bodies;
    }

    /// <summary>The ragdoll's bodies and joints, as the <c>.phy</c> declares them.</summary>
    public RagdollBody Body { get; }

    /// <summary>Whether the game has forced it to sleep.</summary>
    public bool Asleep { get; private set; }

    /// <summary>Builds a ragdoll in a world.</summary>
    /// <param name="world">The environment.</param>
    /// <param name="ragdoll">The bodies and joints.</param>
    /// <param name="start">Each element's starting position and orientation, in Source space.</param>
    /// <returns>The ragdoll.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">The starting state does not match the element count.</exception>
    public static IvpRagdoll Create(IvpRagdollWorld world, RagdollBody ragdoll, IReadOnlyList<(Vector3 Position, Quaternion Orientation)> start)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(ragdoll);
        ArgumentNullException.ThrowIfNull(start);

        if (start.Count != ragdoll.Elements.Count)
        {
            throw new ArgumentException("A ragdoll starts with one state per element.", nameof(start));
        }

        IvpRigidBody[] bodies = new IvpRigidBody[ragdoll.Elements.Count];
        IvpRagdoll made = new(world, ragdoll, bodies);

        for (int index = 0; index < bodies.Length; index++)
        {
            RagdollElement element = ragdoll.Elements[index];
            (Vector3 position, Quaternion orientation) = start[index];

            IvpObjectTemplate template = IvpObjectTemplate.FromParameters(
                element.Mass, element.Inertia, element.Damping, element.RotationDamping, element.RotationInertiaLimit);
            (float mass, (float X, float Y, float Z) inertia) = template.CoreInertia(
                (element.IvpHullInertia.X, element.IvpHullInertia.Y, element.IvpHullInertia.Z));

            (float X, float Y, float Z) offset = element.IvpMassCenter.LengthSquared() < NegligibleSquared
                ? (0f, 0f, 0f)
                : (-element.IvpMassCenter.X, -element.IvpMassCenter.Y, -element.IvpMassCenter.Z);

            (double X, double Y, double Z, double W) rotation = Rotation(orientation);
            (float bx, float by, float bz) = IvpTransform.Position(position.X, position.Y, position.Z);
            (double X, double Y, double Z) turned = IvpMatrix.FromRotation(rotation, (0d, 0d, 0d)).Rotate((offset.X, offset.Y, offset.Z));
            (double X, double Y, double Z) core = (bx - turned.X, by - turned.Y, bz - turned.Z);

            IvpRigidBody body = new()
            {
                Position = core,
                ObjectOffset = offset,
                Orientation = rotation,
                WorkingOrientation = rotation,
                CoreMatrix = IvpMatrix.FromRotation(rotation, core),
                Inertia = inertia,
                InverseInertia = (1f / inertia.X, 1f / inertia.Y, 1f / inertia.Z),
                Mass = mass,
                InverseMass = 1f / mass,
                Ledges = element.Ledges,
                Damping = element.Damping,
                RotationDamping = element.RotationDamping,
            };

            bodies[index] = body;
            world.Simulation.Add(body);

            if (element.Surface is { } surface && world.Surfaces.ObjectMaterial(element.SurfaceProp) is { } material)
            {
                world.Own(world.Simulation.Collide(body, surface, material), made, index);
            }
        }

        IvpConstraintGroup group = new();

        foreach (RagdollConstraint constraint in ragdoll.Constraints)
        {
            if (constraint.Parent < 0 || constraint.Parent >= bodies.Length ||
                constraint.Child < 0 || constraint.Child >= bodies.Length || constraint.Child == constraint.Parent)
            {
                continue;
            }

            IvpRigidBody child = bodies[constraint.Child];
            IvpRigidBody parent = bodies[constraint.Parent];
            RagdollElement childElement = ragdoll.Elements[constraint.Child];

            (float X, float Y, float Z) childAnchor = child.ObjectOffset;
            (float ox, float oy, float oz) = IvpTransform.Position(
                childElement.OriginParentSpace.X, childElement.OriginParentSpace.Y, childElement.OriginParentSpace.Z);
            (float X, float Y, float Z) parentAnchor = (ox + parent.ObjectOffset.X, oy + parent.ObjectOffset.Y, oz + parent.ObjectOffset.Z);

            group.Joints.Add(new IvpRagdollJoint
            {
                BodyA = child,
                BodyB = parent,
                Constraint = Joint(constraint, Conjugated(childElement.AxesParentSpace), child, parent, childAnchor, parentAnchor),
                AnchorA = childAnchor,
                AnchorB = parentAnchor,
            });
        }

        if (group.Joints.Count > 0)
        {
            world.Simulation.Add(group);
        }

        return made;
    }

    /// <summary>The killing blow — <c>RagdollCreate</c>'s force, carried into IVP as vphysics' <c>ApplyForce*</c> carry it.</summary>
    /// <param name="force">The wire's <c>m_vecForce</c>, kg·in/s, Source axes.</param>
    /// <param name="forceBone">The wire's <c>m_nForceBone</c>, or negative for none.</param>
    /// <remarks>See <see cref="RagdollSimulation.Kill"/> for the game's arithmetic, which is unit-free.</remarks>
    public void Kill(Vector3 force, int forceBone)
    {
        if (forceBone < 0 || forceBone >= _bodies.Length)
        {
            return;
        }

        float total = 0f;

        foreach (IvpRigidBody body in _bodies)
        {
            total += body.InverseMass > 0f ? 1f / body.InverseMass : 0f;
        }

        total = MathF.Max(total, 1f);

        (float X, float Y, float Z) impulse = IvpTransform.Position(force.X, force.Y, force.Z);
        IvpPush.ApplyForceCenter(_bodies[forceBone], impulse);

        (double X, double Y, double Z) struck = _bodies[forceBone].ObjectOrigin();
        (float X, float Y, float Z) at = ((float)struck.X, (float)struck.Y, (float)struck.Z);

        for (int index = 0; index < _bodies.Length; index++)
        {
            if (index == forceBone)
            {
                continue;
            }

            float share = (_bodies[index].InverseMass > 0f ? 1f / _bodies[index].InverseMass : 0f) / total;

            IvpPush.ApplyForceOffset(_bodies[index], (impulse.X * share, impulse.Y * share, impulse.Z * share), at);
        }
    }

    /// <summary>Gives every body the corpse's inherited velocity — <c>AddVelocity</c>, in Source in/s.</summary>
    /// <param name="velocity">The wire's <c>m_vecRagdollVelocity</c>.</param>
    public void Inherit(Vector3 velocity)
    {
        (float X, float Y, float Z) ivp = IvpTransform.Position(velocity.X, velocity.Y, velocity.Z);

        foreach (IvpRigidBody body in _bodies)
        {
            IvpPush.AddVelocity(body, ivp, (0f, 0f, 0f));
        }
    }

    /// <summary>
    /// The game's settle check after a step — <c>CRagdoll::CheckSettleStationaryRagdoll</c> — putting the corpse to sleep once it has
    /// moved no more than an inch on any axis for five seconds.
    /// </summary>
    /// <param name="step">The seconds the world just ran.</param>
    /// <remarks>See <see cref="RagdollSimulation.Step"/> for the published arithmetic.</remarks>
    public void CheckSettle(float step)
    {
        if (Asleep || _bodies.Length == 0)
        {
            return;
        }

        _since += step;

        Vector3 root = State()[0].Position;
        (double X, double Y, double Z) origin = (root.X, root.Y, root.Z);
        (double X, double Y, double Z) moved = (origin.X - _origin.X, origin.Y - _origin.Y, origin.Z - _origin.Z);
        _origin = origin;

        if (Math.Abs(moved.X) > SleepTolerance || Math.Abs(moved.Y) > SleepTolerance || Math.Abs(moved.Z) > SleepTolerance)
        {
            _since = 0f;
            return;
        }

        if (_since < SleepAfterTime)
        {
            return;
        }

        Asleep = true;

        foreach (IvpRigidBody body in _bodies)
        {
            // `PhysForceClearVelocity` then `Sleep()`.
            body.Velocity = (0f, 0f, 0f);
            body.AngularVelocity = (0f, 0f, 0f);

            if (body.Unit is { } unit)
            {
                _world.Simulation.Sleep(unit);
            }
        }
    }

    /// <summary>Every element's position and orientation, back in Source space — vphysics' <c>GetPosition</c>.</summary>
    /// <returns>One entry per element, ready for <see cref="RagdollBody.Pose"/>.</returns>
    public (Vector3 Position, Quaternion Orientation)[] State()
    {
        (Vector3 Position, Quaternion Orientation)[] state = new (Vector3, Quaternion)[_bodies.Length];

        for (int index = 0; index < _bodies.Length; index++)
        {
            IvpRigidBody body = _bodies[index];
            (double X, double Y, double Z) origin = body.ObjectOrigin();
            (float x, float y, float z) = IvpTransform.SourcePosition((float)origin.X, (float)origin.Y, (float)origin.Z);
            (double qx, double qy, double qz, double qw) = body.Orientation;

            // The inverse of `Rotation`: an IVP axis `(x, y, z)` is the Source axis `(x, z, −y)`.
            state[index] = (new Vector3(x, y, z), new Quaternion((float)qx, (float)qz, (float)-qy, (float)qw));
        }

        return state;
    }

    /// <summary>A Source rotation in IVP's axes — <c>P·R·Pᵀ</c>, which for a quaternion turns its axis by <c>P</c>.</summary>
    internal static (double X, double Y, double Z, double W) Rotation(Quaternion source) =>
        (source.X, -source.Z, source.Y, source.W);

    /// <summary>A bone-to-bone frame in IVP's axes: <c>P·A·Pᵀ</c>, whose columns are <c>P·X</c>, <c>−P·Z</c> and <c>P·Y</c>.</summary>
    private static RagdollAxes Conjugated(RagdollAxes source) =>
        new(Turned(source.X), -Turned(source.Z), Turned(source.Y));

    private static Vector3 Turned(Vector3 v) => new(v.X, -v.Z, v.Y);

    /// <summary>A joint in IVP space: limits through the engine's remap, the twist axis chosen as <c>FUN_1800393d0</c> chooses it.</summary>
    private static IvpRagdollConstraint Joint(
        RagdollConstraint constraint,
        RagdollAxes attached,
        IvpRigidBody reference,
        IvpRigidBody attachedBody,
        (float X, float Y, float Z) referenceAnchor,
        (float X, float Y, float Z) attachedAnchor)
    {
        IvpAxisLimit[] limits = new IvpAxisLimit[3];
        (float Minimum, float Maximum)[] source = [
            (constraint.X.Minimum, constraint.X.Maximum),
            (constraint.Y.Minimum, constraint.Y.Maximum),
            (constraint.Z.Minimum, constraint.Z.Maximum),
        ];

        for (int axis = 0; axis < 3; axis++)
        {
            (int slot, IvpAxisLimit limit) = RagdollJointLimits.Convert(axis, source[axis].Minimum, source[axis].Maximum);
            limits[slot] = limit;
        }

        (float Minimum, float Maximum)[] ranges = [
            (limits[0].Minimum, limits[0].Maximum),
            (limits[1].Minimum, limits[1].Maximum),
            (limits[2].Minimum, limits[2].Maximum),
        ];

        int primary = RagdollSimulation.Turning(attached, reference, attachedBody, referenceAnchor, attachedAnchor);
        int wider = RagdollSimulation.Widest(ranges, primary, -1);
        int narrower = RagdollSimulation.Widest(ranges, primary, wider);

        return IvpRagdollConstraint.FromLimits(
            limits[primary],
            limits[narrower],
            limits[wider],
            RagdollSimulation.Frame(RagdollAxes.Identity, primary, narrower, wider),
            RagdollSimulation.Frame(attached, primary, narrower, wider));
    }
}
