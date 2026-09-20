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
    /// <summary><c>CONTENTS_SOLID</c>, what ordinary brushwork and every prop is made of — <c>public/bspflags.h:22</c>.</summary>
    public const int ContentsSolid = 0x1;

    /// <summary><c>MASK_SOLID</c> — what a RAGDOLL collides with, and the whole rule.</summary>
    /// <remarks>
    /// **A ragdoll uses `MASK_SOLID`**: `C_AI_BaseNPC::PhysicsSolidMaskForEntity` returns it for a ragdoll — *"This allows ragdolls
    /// to move through npcclip brushes"* (`game/client/c_ai_basenpc.cpp:53-62`) — and the base returns it outright
    /// (`game/shared/physics_main_shared.cpp:1107-1110`). So a corpse collides with
    /// `CONTENTS_SOLID | CONTENTS_MOVEABLE | CONTENTS_WINDOW | CONTENTS_MONSTER | CONTENTS_GRATE` (`public/bspflags.h:106`) and
    /// **not** with `CONTENTS_PLAYERCLIP`. That absence is what put three corpses under `koth_harvest_final`, whose solid 1 is
    /// playerclip alone over the whole middle of the map.
    /// </remarks>
    public const int MaskSolid = 0x1 | 0x4000 | 0x2 | 0x2000000 | 0x8;

    /// <summary>The constructor's own step, <c>env+0x108 = 1/66</c>, at which the default limits are taken.</summary>
    private const double ConstructorStep = 1d / 66d;

    /// <summary><c>env+0xc8 = 0.3f</c>, written by <c>FUN_180080d90</c>.</summary>
    private const float RestDelay = 0.3f;

    /// <summary><c>DAT_1800ec278</c>: <c>1e-4</c>, the delta a frame must exceed to simulate at all.</summary>
    private const double MinimumDelta = 1e-4d;

    /// <summary><c>DAT_1800ec280</c>: <c>0.1</c>, the delta past which a frame is cut.</summary>
    private const double MaximumDelta = 0.1d;

    /// <summary><c>DAT_1800ea968</c>: <c>0.1f</c>, what such a frame is cut to.</summary>
    private const float MaximumStep = 0.1f;

    /// <summary><c>DAT_1800ec288</c>: <c>0x3fffffac</c>, the steps the fixed-step path simulates past the last PSI.</summary>
    private const float FixedStepCount = 1.9999895f;

    private readonly IvpRandom _random = new();

    /// <summary>The fixed-step byte <c>+0xcf</c>, set by the constructor and cleared by the first frame of another length.</summary>
    private bool _fixedStep = true;
    private readonly Dictionary<IvpRigidBody, (IvpRagdoll Ragdoll, int Element)> _owners = [];
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
        AddStatic(surface, origin, material, ContentsSolid);

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

            // Stryker disable all : the condition spans two lines, so 'disable once' cannot reach
            // it (measured). A mutant that empties the guard body leaves 'found' or 'material'
            // unassigned below (CS0165), and Safe Mode then drops every mutation in this method —
            // B410.
            if (prop.Solid != SolidVphysics || collide(prop.Model) is not { } found ||
                Surfaces.ObjectMaterial(found.SurfaceProp) is not { } material)
            {
                continue;
            }

            // Stryker restore all

            made.Add(AddStatic(
                found.Surface,
                new Vector3(prop.X, prop.Y, prop.Z),
                AngleQuaternion(prop.Pitch, prop.Yaw, prop.Roll),
                material,
                ContentsSolid));
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

        // Stryker disable once : a mutant that empties the guard body leaves 'material'
        // unassigned (CS0165), and Safe Mode then drops every mutation in this method — B410.
        if (Surfaces.ObjectMaterial("default") is not { } material)
        {
            return made;
        }

        for (int index = 0; index < displacements.Count; index++)
        {
            // Stryker disable once : a mutant that empties the guard body leaves 'tree'
            // unassigned (CS0165), and Safe Mode then drops every mutation in this method — B410.
            if (displacements[index] is not (DisplacementCollisionTree tree, false))
            {
                continue;
            }

            byte[] hull = index < hulls.Count ? hulls[index] ?? [] : [];
            IvpVirtualMeshSurfaceManager mesh = new(PhysicsVirtualMesh.Build(tree.Vertices, tree.Triangles, hull), tree);
            ((double X, double Y, double Z) at, (float X, float Y, float Z) offset) =
                IvpRagdoll.AtMassCentre(mesh.MassCenter, (0d, 0d, 0d, 1d), (0d, 0d, 0d));

            IvpRigidBody core = new()
            {
                Immovable = true,
                Position = at,
                ObjectOffset = offset,
                Orientation = (0d, 0d, 0d, 1d),
                WorkingOrientation = (0d, 0d, 0d, 1d),
                CoreMatrix = IvpMatrix.FromRotation((0d, 0d, 0d, 1d), at),
                InverseMass = 0f,
                InverseInertia = (0f, 0f, 0f),
            };

            IvpCollisionObject collisionObject = Simulation.Collide(core, mesh, material);
            _contents[collisionObject] = ContentsSolid;
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
        ((double X, double Y, double Z) at, (float X, float Y, float Z) offset) =
            IvpRagdoll.AtMassCentre((surface.MassCenter.X, surface.MassCenter.Y, surface.MassCenter.Z), rotation, (x, y, z));

        IvpRigidBody core = new()
        {
            Immovable = true,
            Position = at,
            ObjectOffset = offset,
            Orientation = rotation,
            WorkingOrientation = rotation,
            CoreMatrix = IvpMatrix.FromRotation(rotation, at),
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

        // Stryker disable once : a mutant that empties the guard body leaves 'material'
        // unassigned (CS0165), and Safe Mode then drops every mutation in this method — B410.
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

                AddSolid(made, model, 0, Vector3.Zero, material, ContentsSolid);

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
                    declared.TryGetValue(solid, out int contents) ? contents : ContentsSolid);
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

    /// <summary>Runs the environment forward by one frame — <c>CPhysicsEnvironment::Simulate</c>, <c>FUN_180015310</c>.</summary>
    /// <param name="deltaTime">The frame's time, in seconds.</param>
    /// <remarks>
    /// **Read from the disassembly** (2026-09-16):
    /// <code>
    /// dt > 1f (COMISS/JA, a NaN goes on)  or  !((double)dt > 1e-4) (COMISD/JBE):  nothing is simulated
    /// (double)dt > 0.1 → dt = 0.1f
    /// +0xcf set and dt == (float)env+0x108 (UCOMISS/JNZ):  SimulateTo(env+0x198 + (double)((float)env+0x108 · 1.9999895f))
    /// else:  +0xcf = 0;  SimulateTo(env+0x188 + (double)dt)
    /// </code>
    /// The constructor leaves `+0xcf` set (`0x1000000` at `+0xcc`). *Not carried*: the delete list flushed either side
    /// (`FUN_1800128f0`), `FUN_180016090`, the stale-entry sweep of `+0xa8`, the friction-scrape reports after it and the
    /// `FUN_180026180`/slot `0x140` tail — none moves a body.
    /// </remarks>
    public void Simulate(float deltaTime)
    {
        if (deltaTime > 1f || !((double)deltaTime > MinimumDelta))
        {
            return;
        }

        if ((double)deltaTime > MaximumDelta)
        {
            deltaTime = MaximumStep;
        }

        IvpImpactEnvironment environment = Simulation.Environment;

#pragma warning disable S1244 // Valve's own exact comparison: UCOMISS against the step, the frame is the step or it is not.
        if (_fixedStep && deltaTime == (float)environment.Step)
#pragma warning restore S1244
        {
            Simulation.Advance(environment.RebaseBase + (double)((float)environment.Step * FixedStepCount));
            return;
        }

        _fixedStep = false;
        Simulation.Advance(Simulation.Now + (double)deltaTime);
    }

    /// <summary>Files a core as one ragdoll's element, for the rules — the object's game data and game index.</summary>
    internal void Own(IvpRigidBody core, IvpRagdoll ragdoll, int element) =>
        _owners[core] = (ragdoll, element);

    /// <summary>The game's rules for a pair — <c>CCollisionEvent::ShouldCollide</c>, <c>game/client/physics.cpp:200-258</c>.</summary>
    /// <remarks>
    /// **In the engine's order, for what this world holds**: one corpse's own parts answer its <c>collisionrules</c>; two corpses'
    /// parts never collide, <c>cl_ragdoll_collide</c> being <c>"0"</c>; a static solid collides with a corpse only if its contents
    /// meet the corpse's <c>MASK_SOLID</c> (a corpse's own objects are <c>CONTENTS_SOLID</c>, inside the world's mask). *The game
    /// rules' collision groups between a corpse and the world are not carried*: debris against the world collides.
    /// </remarks>
    private bool ShouldCollide(IvpCollisionObject first, IvpCollisionObject second)
    {
        (IvpRagdoll Ragdoll, int Element)? a =
            first.Core is { } firstCore && _owners.TryGetValue(firstCore, out (IvpRagdoll, int) aOwner) ? aOwner : null;
        (IvpRagdoll Ragdoll, int Element)? b =
            second.Core is { } secondCore && _owners.TryGetValue(secondCore, out (IvpRagdoll, int) bOwner) ? bOwner : null;
        bool firstPart = a is not null;

        if (a is { } mine && b is { } theirs)
        {
            if (ReferenceEquals(mine.Ragdoll, theirs.Ragdoll))
            {
                return mine.Ragdoll.Body.ShouldCollide(mine.Element, theirs.Element);
            }

            // **The game rules by collision group, then `cl_ragdoll_collide`** (B409): debris against debris never collides
            // (`gamerules.cpp:713-717`); a gib against a corpse — `COLLISION_GROUP_NONE` — does, and `cl_ragdoll_collide` (`"0"`)
            // stops only a pair whose BOTH objects are part of a ragdoll (`physics.cpp:241`).
            return mine.Ragdoll.IsDebris != theirs.Ragdoll.IsDebris;
        }

        IvpCollisionObject other = firstPart ? second : first;
        int contents = _contents.TryGetValue(other, out int declared) ? declared : ContentsSolid;

        return (contents & MaskSolid) != 0;
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

    /// <summary>The cores, one per element — for instruments.</summary>
    internal IReadOnlyList<IvpRigidBody> Bodies => _bodies;

    /// <summary>The world the corpse is in — for instruments.</summary>
    internal IvpRagdollWorld World => _world;

    /// <summary>This ragdoll's joints, wired into the world — for instruments; empty when the file names none.</summary>
    internal IvpConstraintGroup? Joints { get; private set; }

    /// <summary>Takes this ragdoll out of the world — <c>CRagdoll::ClearRagdoll</c>'s own order (B316, D172).</summary>
    /// <remarks>
    /// **Constraints first, then objects, and the order is not cosmetic.** `CPhysicsEnvironment::DestroyConstraint`
    /// (`0x180012fe0`) reaches each endpoint through pointers the constraint still holds and calls the object's vtable
    /// `+0xc0` — which is <c>Wake</c> (`docs/findings/51`, *Destroying a constraint wakes both bodies it joined*). Run the
    /// other way round, that notify would be reaching objects already torn down, and the limbs would never be woken to resume
    /// simulating on their own.
    ///
    /// Calling it twice is harmless: the joints are dropped on the first pass and the bodies are already gone from the
    /// simulation's units.
    /// </remarks>
    public void Destroy()
    {
        if (Joints is { } joints)
        {
            _world.Simulation.RemoveConstraints(joints);
            Joints = null;
        }

        foreach (IvpRigidBody body in _bodies)
        {
            _world.Simulation.Remove(body);
        }
    }

    /// <summary>Whether the game has forced it to sleep.</summary>
    public bool Asleep { get; private set; }

    /// <summary>Whether this is a gib — a client physics prop in <c>COLLISION_GROUP_DEBRIS</c> — rather than a corpse (B409).</summary>
    public bool IsDebris { get; private set; }

    /// <summary>Builds a gib — <c>BreakModelCreateSingle</c>'s physics prop (B409).</summary>
    /// <param name="world">The environment.</param>
    /// <param name="prop">The prop's one body, from <see cref="RagdollBody.BuildProp"/> — the prop's own parameters.</param>
    /// <param name="start">Its starting position and orientation, in Source space.</param>
    /// <returns>The gib.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">The starting state does not match the element count.</exception>
    /// <remarks>
    /// **`C_PhysPropClientside::Initialize` → `VPhysicsInitNormal` → `PhysModelCreate`**: one object from the model's first solid,
    /// with `PhysModelParseSolid`'s parameters over `g_PhysDefaultObjectParams` — `rotInertiaLimit` 0.05, where a ragdoll element's is
    /// 0.1 — and woken, `createAsleep` being false. **TF then sets it to `COLLISION_GROUP_DEBRIS`** (`BreakModelCreateSingle`,
    /// `#ifdef TF_CLIENT_DLL`), which <see cref="IvpRagdollWorld"/>'s rules read as <see cref="IsDebris"/>. *A prop has no
    /// `CRagdoll`*, so no forced settle: IVP's own rest check is what puts it to sleep.
    /// </remarks>
    public static IvpRagdoll CreateProp(IvpRagdollWorld world, RagdollBody prop, IReadOnlyList<(Vector3 Position, Quaternion Orientation)> start)
    {
        IvpRagdoll made = Create(world, prop, start);
        made.IsDebris = true;
        return made;
    }

    /// <summary>Adds a velocity and a spin to every body — vphysics' <c>CPhysicsObject::AddVelocity</c>, <c>18001a6c0</c>.</summary>
    /// <param name="velocity">Source inches a second.</param>
    /// <param name="angular">Degrees a second about the body's own axes — an <c>AngularImpulse</c>.</param>
    /// <remarks>
    /// **Read from the binary** (vtable slot 52): gated on moveable, then woken, then
    /// <code>
    /// core+0x120 += x·0.0254;  core+0x124 −= z·0.0254;  core+0x128 += y·0.0254
    /// core+0x110 += x·0.017453292;  core+0x114 −= z·0.017453292;  core+0x118 += y·0.017453292
    /// </code>
    /// then the speed limit (<see cref="IvpPush.Limit"/>) for an object with no shadow controller. *The wake is not repeated here*: a
    /// body is revived at the first PSI after it is added to the simulation, which is where every caller of this stands.
    /// </remarks>
    public void AddVelocity(Vector3 velocity, Vector3 angular)
    {
        (float X, float Y, float Z) linear = IvpTransform.Position(velocity.X, velocity.Y, velocity.Z);
        (float X, float Y, float Z) spin = (angular.X * DegreesToRadians, -(angular.Z * DegreesToRadians), angular.Y * DegreesToRadians);

        foreach (IvpRigidBody body in _bodies)
        {
            IvpPush.AddVelocity(body, linear, spin);
            IvpPush.Limit(body, _world.Simulation.Environment.Limits, _world.Simulation.Environment.InverseStep);
        }
    }

    /// <summary>The <c>0.017453292</c> vphysics' <c>AddVelocity</c> multiplies an angular impulse by.</summary>
    private const float DegreesToRadians = 0.017453292f;

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

            (double X, double Y, double Z, double W) rotation = Rotation(orientation);
            (float bx, float by, float bz) = IvpTransform.Position(position.X, position.Y, position.Z);
            ((double X, double Y, double Z) core, (float X, float Y, float Z) offset) =
                AtMassCentre((element.IvpMassCenter.X, element.IvpMassCenter.Y, element.IvpMassCenter.Z), rotation, (bx, by, bz));

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

            // The object builder `FUN_18001b340`: bases seeded zero (`FUN_18001c4c0`), computed for a movable object with a collide
            // (`FUN_18001d4e0`) with both coefficients the parameter, else both zero; then `EnableDrag` for a non-zero parameter.
            if (element.Surface is { } collide)
            {
                ((float X, float Y, float Z) min, (float X, float Y, float Z) max) = IvpDrag.CollideBox(collide);
                IvpDrag.ComputeBasis(body, min, max, (collide.DragAxisAreas.X, collide.DragAxisAreas.Y, collide.DragAxisAreas.Z));
                body.DragCoefficient = element.DragCoefficient;
                body.AngularDragCoefficient = element.DragCoefficient;
            }

            if (element.DragCoefficient != 0f)
            {
                world.Simulation.EnableDrag(body);
            }

            // The game data rides in with the object (`solid.params.pGameData`, `ragdoll_shared.cpp:193`), before any pair is asked about.
            world.Own(body, made, index);
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

        made.Joints = group;

        if (group.Joints.Count > 0)
        {
            world.Simulation.Add(group);
        }

        // `RagdollActivate` (`ragdoll_shared.cpp:375-382`): "now that the relationships are set, activate the collision system", each
        // element in index order. *Filing each element as it was made asked the rules about objects not yet owned, and made pairs they
        // forbid.*
        for (int index = 0; index < bodies.Length; index++)
        {
            RagdollElement element = ragdoll.Elements[index];

            if (element.Surface is { } surface && world.Surfaces.ObjectMaterial(element.SurfaceProp) is { } material)
            {
                world.Simulation.Collide(bodies[index], surface, material);
            }
        }

        return made;
    }

    /// <summary>The killing blow — <c>RagdollCreate</c>'s force, carried into IVP as vphysics' <c>ApplyForce*</c> carry it.</summary>
    /// <param name="force">The wire's <c>m_vecForce</c>, kg·in/s, Source axes.</param>
    /// <param name="forceBone">The wire's <c>m_nForceBone</c>, or negative for none.</param>
    /// <remarks>
    /// **`RagdollCreate`, `ragdoll_shared.cpp:405`**, whose arithmetic is unit-free, so it carries into IVP's axes unchanged:
    ///
    /// <code>
    /// totalMass = MAX( sum of masses, 1 );
    /// if ( forceBone >= 0 &amp;&amp; forceBone &lt; ragdoll.listCount ) {
    ///     ragdoll.list[forceBone].pObject-&gt;ApplyForceCenter( nudgeForce );
    ///     ragdoll.list[forceBone].pObject-&gt;GetPosition( &amp;forcePosition, NULL );
    /// }
    /// if ( forcePosition != vec3_origin ) {
    ///     for ( i … ) if ( forceBone != i ) {
    ///         float scale = ragdoll.list[i].pObject-&gt;GetMass() / totalMass;
    ///         ragdoll.list[i].pObject-&gt;ApplyForceOffset( scale * nudgeForce, forcePosition );
    ///     }
    /// }
    /// </code>
    ///
    /// - **The struck bone takes the WHOLE force through its centre**, so it gains speed and no spin of its own.
    /// - **Every other body takes a share by MASS, at the struck bone's position** — an offset push, so the rest of the body swings
    ///   about the hit. That is the difference between a corpse that tumbles away from a rocket and one that slides.
    /// - **`forcePosition` gates the second loop**, and with no force bone it stays at the origin. A corpse whose killer sent neither
    ///   is pushed not at all, which is correct: `m_vecForce` is zeroed for a death animation (`c_tf_player.cpp:847`).
    /// - **`GetPosition` reports the OBJECT's origin** — the bone — not its core (B403).
    ///
    /// **The scale divides by the TOTAL mass, not by the body's own**, so the shares sum to less than one force — Valve's own comment
    /// beside it reads *"UNDONE: Test scaling the force by total mass on all bones"*, so this is deliberate and unfinished in the
    /// engine too.
    ///
    /// **The magnitudes are Valve's own and they are large.** `CalcDamageForceVector` sizes the impulse at `damage * 75 * 4`
    /// (`basecombatcharacter.cpp:1395`) — three hundred kg·in/s per point of damage, so a sixty-damage kill is eighteen thousand, and
    /// `z1800` carries 16,793, 19,191 and 23,987. The struck bone weighs about ten kilos, so the whole force through its centre
    /// really is thousands of units a second.
    /// </remarks>
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
    /// <remarks>
    /// **A stated departure.** `RagdollApplyAnimationAsVelocity` (`ragdoll_shared.cpp:458`) derives a per-body velocity from TWO bone
    /// snapshots `boneDt` apart — `GetRagdollInitBoneArrays` with `boneDt = 0.05f` (`c_tf_player.cpp:890`) — so each limb inherits
    /// its own motion. A demo carries `m_vecRagdollVelocity` and not those snapshots, and a corpse is seeded from one pose, so every
    /// body gets the entity's single velocity: the corpse travels correctly and its limbs do not lead or trail. A second seed pose one
    /// `boneDt` earlier, which the timeline can produce, would close it.
    /// </remarks>
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
    /// <remarks>
    /// **Published, constant for constant** (`game/client/ragdoll.cpp:265-297`):
    ///
    /// <code>
    /// #define RAGDOLL_SLEEP_TOLERANCE 1.0f
    /// static ConVar ragdoll_sleepaftertime( "ragdoll_sleepaftertime", "5.0f", 0, … );
    ///
    /// Vector delta = GetRagdollOrigin() - m_vecLastOrigin;
    /// m_vecLastOrigin = GetRagdollOrigin();
    /// for ( int i = 0; i &lt; 3; ++i )
    ///     if ( fabs( delta[ i ] ) &gt; RAGDOLL_SLEEP_TOLERANCE )
    ///     { m_flLastOriginChangeTime = gpGlobals-&gt;curtime; return; }
    /// if ( dt &lt; ragdoll_sleepaftertime.GetFloat() ) return;
    /// PhysForceRagdollToSleep();
    /// </code>
    ///
    /// **Per AXIS and not by distance**, which is the engine's own test and a looser one — a body creeping 0.9 units along each of
    /// three axes is stationary by this rule. `PhysForceRagdollToSleep` zeroes each body's linear and angular velocity
    /// (`PhysForceClearVelocity`, `physics_shared.cpp:917`) and then stops it being integrated.
    ///
    /// **Its absence was measured**: dropped on `koth_harvest_final` without it, five scout ragdolls were still moving faster than a
    /// unit a second after ten seconds, and on `z1800` some of them left the map.
    /// </remarks>
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
    /// <remarks>
    /// **The object at the environment's clock, not at its core's last step** — `GetPosition` (`FUN_18001c030`) reads
    /// `FUN_180073b80`: the core interpolated to `env+0x188` (<see cref="IvpRigidBody.TransformAt"/>), turned into a matrix, and
    /// the object's offset put into the world through it. A frame on the fixed-step path leaves the clock almost a step past the
    /// last PSI, so what is reported is nearly where the next PSI will put the body.
    /// </remarks>
    public (Vector3 Position, Quaternion Orientation)[] State()
    {
        (Vector3 Position, Quaternion Orientation)[] state = new (Vector3, Quaternion)[_bodies.Length];

        for (int index = 0; index < _bodies.Length; index++)
        {
            IvpRigidBody body = _bodies[index];
            ((double X, double Y, double Z) position, (double X, double Y, double Z, double W) rotation) =
                body.TransformAt(_world.Simulation.Now);
            (double X, double Y, double Z) origin =
                IvpMatrix.FromRotation(rotation, position).ToWorld(body.ObjectOffset);
            (float x, float y, float z) = IvpTransform.SourcePosition((float)origin.X, (float)origin.Y, (float)origin.Z);
            (double qx, double qy, double qz, double qw) = rotation;

            // The inverse of `Rotation`: an IVP axis `(x, y, z)` is the Source axis `(x, z, −y)`.
            state[index] = (new Vector3(x, y, z), new Quaternion((float)qx, (float)qz, (float)-qy, (float)qw));
        }

        return state;
    }

    /// <summary>Where an object's core stands and how the object sits inside it — <c>FUN_180073df0</c>, for every object.</summary>
    /// <param name="massCentre">The surface's mass centre in the object's frame — the surface manager's slot 1, IVP metres.</param>
    /// <param name="rotation">The object's orientation, in IVP's axes.</param>
    /// <param name="objectPosition">Where the object stands, IVP metres.</param>
    /// <returns>The core's position, and the object's offset inside it: minus the mass centre, or nothing when that is negligible.</returns>
    /// <remarks>
    /// **`IvpObjectTemplate::ConstructCore` puts the core at the mass centre and sets the object back from it**, static objects
    /// included — the surface's radius is then taken about that centre. A mass centre nearer the origin than <c>1e-16</c> square metres
    /// (<c>DAT_1800fcf98</c>) leaves the object at the core.
    /// </remarks>
    internal static ((double X, double Y, double Z) Core, (float X, float Y, float Z) Offset) AtMassCentre(
        (float X, float Y, float Z) massCentre, (double X, double Y, double Z, double W) rotation, (double X, double Y, double Z) objectPosition)
    {
        float squared = (massCentre.X * massCentre.X) + (massCentre.Y * massCentre.Y) + (massCentre.Z * massCentre.Z);
        (float X, float Y, float Z) offset = squared < NegligibleSquared ? (0f, 0f, 0f) : (-massCentre.X, -massCentre.Y, -massCentre.Z);
        (double X, double Y, double Z) turned = IvpMatrix.FromRotation(rotation, (0d, 0d, 0d)).Rotate((offset.X, offset.Y, offset.Z));

        return ((objectPosition.X - turned.X, objectPosition.Y - turned.Y, objectPosition.Z - turned.Z), offset);
    }

    /// <summary>Writes the corpse's current bones into an accessor, marking what it drove — what <c>AnimatingEntity.Ragdoll</c> takes.</summary>
    /// <param name="into">The accessor to write through.</param>
    /// <param name="written">Marked for every bone the ragdoll drove.</param>
    public void PoseIntoAccessor(BoneAccessor into, BoneBitList written) => Body.PoseInto(State(), into, written);

    /// <summary>How many friction contacts hold the corpse's bodies — an instrument, the contacts each body's share files.</summary>
    public int Contacts
    {
        get
        {
            int count = 0;

            foreach (IvpRigidBody body in _bodies)
            {
                count += body.FrictionInfo?.Contacts.Count ?? 0;
            }

            return count;
        }
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

        int primary = RagdollJointAxes.Turning(attached, reference, attachedBody, referenceAnchor, attachedAnchor);
        int wider = RagdollJointAxes.Widest(ranges, primary, -1);
        int narrower = RagdollJointAxes.Widest(ranges, primary, wider);

        return IvpRagdollConstraint.FromLimits(
            limits[primary],
            limits[narrower],
            limits[wider],
            RagdollJointAxes.Frame(RagdollAxes.Identity, primary, narrower, wider),
            RagdollJointAxes.Frame(attached, primary, narrower, wider));
    }
}
