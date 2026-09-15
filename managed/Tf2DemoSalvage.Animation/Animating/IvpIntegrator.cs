using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// One rigid body's state, as <c>IVP_Core</c> holds it (B58, D142, D146).
/// </summary>
/// <remarks>
/// **Field for field from the decompiled binary**, with each offset recorded so a later reader can
/// check it rather than trust this list:
///
/// | offset | field |
/// |---|---|
/// | `+0x20/0x24/0x28` | inertia about each axis |
/// | `+0x40/0x44/0x48` | its reciprocals (INFERRED — the division was not found) |
/// | `+0x4c` | inverse mass |
/// | `+0x130/0x134/0x138` | angular velocity |
/// | `+0x140/0x144/0x148` | linear velocity |
/// | `+0x150/0x158/0x160` | position, as DOUBLES |
/// | `+0x170/0x174/0x178` | the previous step's linear velocity |
/// | `+0x180..0x198` | the orientation everything outside reads |
/// | `+0x1a0..0x1b8` | the orientation the integrator works in |
/// | `+0x1d0` | the absolute time this body was last stepped |
///
/// **Position is double and velocity is float, and that is not an oversight to tidy up.** A corpse
/// accumulates position over thousands of steps and its velocity is rewritten every one, so the
/// engine spends the precision where it accumulates. Flattening both to float drifts; both to
/// double diverges from the engine's own rounding.
///
/// **Two of these fields exist so that what is READ lags what is COMPUTED**, by exactly one step —
/// see <see cref="IvpIntegrator.Step"/>.
/// </remarks>
public sealed class IvpRigidBody
{
    /// <summary>Where the body is — <c>core+0x150/0x158/0x160</c>, in doubles.</summary>
    public (double X, double Y, double Z) Position { get; set; }

    /// <summary>How fast it is moving — <c>core+0x140/0x144/0x148</c>.</summary>
    public (float X, float Y, float Z) Velocity { get; set; }

    /// <summary>What <see cref="Velocity"/> was last step — <c>core+0x170/0x174/0x178</c>.</summary>
    /// <remarks>
    /// **This is what moves the body, not <see cref="Velocity"/>**, which is why it is a field
    /// rather than a local. See <see cref="IvpIntegrator.Step"/>.
    /// </remarks>
    public (float X, float Y, float Z) PreviousVelocity { get; set; }

    /// <summary>How fast it is turning — <c>core+0x130/0x134/0x138</c>, radians per second.</summary>
    public (float X, float Y, float Z) AngularVelocity { get; set; }

    /// <summary>The orientation everything outside the solver reads — <c>core+0x180</c>, in doubles.</summary>
    /// <remarks>**One step behind <see cref="WorkingOrientation"/>, deliberately.**</remarks>
    public (double X, double Y, double Z, double W) Orientation { get; set; } = (0d, 0d, 0d, 1d);

    /// <summary>The orientation the integrator advances — <c>core+0x1a0</c>, in doubles.</summary>
    public (double X, double Y, double Z, double W) WorkingOrientation { get; set; } = (0d, 0d, 0d, 1d);

    /// <summary>Rotational inertia about each axis — <c>core+0x20/0x24/0x28</c>.</summary>
    public (float X, float Y, float Z) Inertia { get; set; } = (1f, 1f, 1f);

    /// <summary>Its reciprocal — <c>core+0x40/0x44/0x48</c>.</summary>
    /// <remarks>
    /// **Reciprocals, read**: `FUN_180074530`, which moves a core between movable and not, writes `+0x40..0x4c` as
    /// `1f / +0x20..0x2c` — four `DIVSS` from `DAT_1800ea988` (`1f`), the inverse mass at `+0x4c` among them — before it runs the
    /// broad phase (`docs/findings/51`).
    /// </remarks>
    public (float X, float Y, float Z) InverseInertia { get; set; } = (1f, 1f, 1f);

    /// <summary>This body's mass — <c>core+0x2c</c>.</summary>
    /// <remarks>
    /// **Written by the core's construction, `FUN_180073df0`** — <see cref="IvpObjectTemplate.CoreInertia"/> — and read as
    /// mass by the heap's kinetic energy `FUN_180077e80`, which multiplies the squared speed by it, and by the pair damping
    /// `FUN_180086670`, which takes it beside <see cref="InverseMass"/> (`docs/findings/51`). The default of one matches
    /// <see cref="InverseMass"/>'s.
    /// </remarks>
    public float Mass { get; set; } = 1f;

    /// <summary>This body's kinetic energy at a velocity and a spin — <c>FUN_180077e80(core, v, ω)</c>.</summary>
    /// <param name="velocity">The velocity, in metres per second.</param>
    /// <param name="spin">The angular velocity.</param>
    /// <returns><c>((d)(f)((ω.y²·I.y + ω.x²·I.x) + ω.z²·I.z) + (d)(f)((v.x² + v.y²) + v.z²)·(d)m)·0.5</c>.</returns>
    /// <remarks>
    /// The heap solve sums it over a friction system's cores before and after its pushes (`FUN_1800aa1a0`), with each core's
    /// staged changes added in. Every product and sum names the binary's destination first (<see cref="IvpMath.Mulss"/>).
    /// </remarks>
    internal double KineticEnergy((float X, float Y, float Z) velocity, (float X, float Y, float Z) spin)
    {
        float turning = IvpMath.Addss(
            IvpMath.Addss(IvpMath.Mulss(spin.Y * spin.Y, Inertia.Y), IvpMath.Mulss(spin.X * spin.X, Inertia.X)),
            IvpMath.Mulss(spin.Z * spin.Z, Inertia.Z));
        float moving = IvpMath.Addss(IvpMath.Addss(velocity.X * velocity.X, velocity.Y * velocity.Y), velocity.Z * velocity.Z);

        return IvpMath.Mulsd(IvpMath.Addsd(turning, IvpMath.Mulsd(moving, Mass)), 0.5d);
    }

    /// <summary>The reciprocal of this body's mass — <c>core+0x4c</c>.</summary>
    /// <remarks>
    /// **Named by the contact builder, which is the only traced site that reads it.**
    /// `FUN_18008d0c0` computes a contact's effective inverse mass as
    /// `rx*rx*core[+0x44] + ry*ry*core[+0x40] + rz*rz*core[+0x48] + core[+0x4c]` — three rotational
    /// terms and one that carries no lever arm, which is what a linear inverse mass is.
    ///
    /// **Zero for an immovable body**, because the same builder zeroes the whole term rather than
    /// dividing: static map geometry has infinite mass by having none to contribute.
    ///
    /// **The default is 1 rather than 0** so a body built without one is light rather than
    /// immovable — a zero default would make every un-massed body silently unpushable, which is the
    /// failure that looks like working collision.
    /// </remarks>
    public float InverseMass { get; set; } = 1f;

    /// <summary>Velocity waiting to be added at the next step — <c>core+0x120/0x124/0x128</c>.</summary>
    /// <remarks>
    /// **A push does not reach a body's velocity where it is applied; it is STAGED.**
    /// `FUN_180077950` drains this pair into the real velocities and zeroes it, once per body per
    /// step, from inside the same gate as gravity:
    ///
    /// <code>
    /// *(float *)(core + 0x130) = *(float *)(core + 0x110) + *(float *)(core + 0x130);
    /// *(float *)(core + 0x140) = *(float *)(core + 0x120) + *(float *)(core + 0x140);
    /// ...
    /// *(undefined8 *)(core + 0x124) = 0;   *(undefined4 *)(core + 0x120) = 0;
    /// *(undefined8 *)(core + 0x114) = 0;   *(undefined4 *)(core + 0x110) = 0;
    /// </code>
    ///
    /// **So an impulse applied between steps lands on the NEXT one**, which is the same one-step
    /// lag the integrator has by moving on the previous velocity. A ragdoll's creation force is
    /// applied through exactly this — `ApplyForceCenter` and `AddVelocity` stage, they do not set.
    /// </remarks>
    public (float X, float Y, float Z) PendingVelocity { get; set; }

    /// <summary>Angular velocity waiting the same way — <c>core+0x110/0x114/0x118</c>.</summary>
    public (float X, float Y, float Z) PendingAngularVelocity { get; set; }

    /// <summary>How fast this body loses speed — <c>core+0x50</c>, the <c>.phy</c>'s <c>damping</c>.</summary>
    /// <remarks>
    /// **Zero on every element of every TF2 ragdoll**, measured — so this is the term that does
    /// nothing for a corpse and everything for a prop. Carried because the engine has it.
    ///
    /// **0.1 by default and not zero**, which is `g_PhysDefaultObjectParams`
    /// (`game/shared/physics_shared.cpp:43-56`) — the same struct <see cref="Friction"/> already
    /// takes its 1 from. A body that names no damping is not a body without damping, and defaulting
    /// to zero made every such body frictionless in rotation as well as translation.
    /// </remarks>
    public float Damping { get; set; } = 0.1f;

    /// <summary>How fast it loses spin — <c>core+0x30/0x34/0x38</c>, the <c>rotdamping</c>.</summary>
    /// <remarks>
    /// **IVP holds three of these and Valve supplies one.** `core+0x30` is a per-axis vector and
    /// `objectparams_t::rotdamping` is a scalar, so the engine writes the same number into all
    /// three lanes; a scalar here is that, not a simplification of it. It matters for a corpse:
    /// rotational damping runs 4 to 16 per joint across the game's ragdolls.
    /// </remarks>
    ///
    /// **0.1 by default, from `g_PhysDefaultObjectParams`** (`physics_shared.cpp:43-56`), for the
    /// reason beside <see cref="Damping"/>: zero is not the engine's answer for a body that names
    /// no value, and a body with no rotational damping never stops spinning once something sets it
    /// turning.
    public float RotationDamping { get; set; } = 0.1f;

    /// <summary>How many impacts this core has taken — the signed word at <c>core+0x2</c>.</summary>
    /// <remarks>
    /// **Compared against `maxCollisionsPerObjectPerTimestep`, whose own comment says what happens at the limit** —
    /// *"object will be frozen after this many collisions (visual hitching vs. CPU cost)"* (`performance.h:21`). The impact
    /// solver's commit adds one for a movable core (`FUN_18008deb0`), takes it back from a core it holds back, and asks the
    /// anomaly manager once the word exceeds the limit (`FUN_18008ddf0`) — see <see cref="IvpImpactSolver"/>.
    ///
    /// **The client runs 6, not 10**: the server raises Valve's default before `SetPerformanceSettings`
    /// (`game/server/physics.cpp:222-226`), and the client's `PhysicsLevelInit` never calls it
    /// (`game/client/physics.cpp:163-187`) — see <see cref="IvpPerformanceSettings"/>. *What resets the word is unread.*
    /// </remarks>
    public short Collisions { get; set; }


    /// <summary>Whether this body has been frozen for the rest of the step.</summary>
    /// <remarks>
    /// **Frozen, not asleep.** It lasts until the step ends and the count is cleared; the engine's
    /// word for it is the same one its comment uses, and the cost it trades against is *"visual
    /// hitching"*.
    /// </remarks>
    public bool Frozen { get; set; }

    /// <summary>The friction a body keeps when no surface file was parsed — a viewer with no install (D83).</summary>
    /// <remarks>
    /// **This project's own number**, which the engine never runs with. It was once cited as `g_PhysDefaultObjectParams`'
    /// friction, a struct with no friction whose `1.0` is mass (`physics_shared.cpp:46`, `docs/findings/51`).
    /// </remarks>
    public const float NoSurfaceFriction = 1f;

    /// <summary>This body's coefficient of friction — its surface's, from the game's own surface files.</summary>
    /// <remarks>
    /// **`surfacephysicsparams_t::friction` of the surface its `.phy` solid names**, resolved as the game resolves it: the
    /// `surfaceprop`, else `default` (<see cref="VphysicsSurfaceProps.ObjectMaterial"/>).
    /// </remarks>
    public float Friction { get; set; } = NoSurfaceFriction;

    /// <summary>The hull this body collides with, in its own space and in SOURCE units.</summary>
    /// <remarks>
    /// **Empty for a body that is not collided**, which is every body this project had until
    /// contacts existed. The points are the ledge geometry read out of the model's `.phy` — see
    /// `PhysicsHull` — converted at the seam, because this simulation runs in Source units where
    /// IVP's own runs in metres.
    /// </remarks>
    public IReadOnlyList<(float X, float Y, float Z)> Hull
    {
        get => _hull;

        set => _hull = value ?? [];
    }

    /// <summary>Where the object sits inside this core — <c>object+0x60</c> (B403).</summary>
    /// <remarks>
    /// **The core is at the hull's mass center and the object is kept at `−massCenter` inside it**, stored by
    /// `FUN_180074380` and composed back into every transform the engine hands out for the object
    /// (`FUN_1800734e0`, `FUN_180032740`, `FUN_180037620`). Zero — the engine's bit `0x800` — when the mass
    /// center is negligibly close to the object's origin. **The core-object rotation is the identity** for
    /// every object `FUN_180073df0` creates, so no rotation goes with it.
    /// </remarks>
    public (float X, float Y, float Z) ObjectOffset { get; set; }

    /// <summary>A hull point in this core's frame: the object-frame point plus the offset.</summary>
    /// <param name="index">The point's index in <see cref="Hull"/>.</param>
    /// <returns>The point, relative to the core and in its axes.</returns>
    /// <remarks>
    /// **The engine stores ledge points in the object's frame and composes the offset each time it places
    /// them**, so this is asked per use rather than baked into <see cref="Hull"/> — which is also what the
    /// time-of-impact search will need when it measures a point through the object's transform.
    /// </remarks>
    public (float X, float Y, float Z) CoreHullPoint(int index)
    {
        (float x, float y, float z) = _hull[index];

        return (x + ObjectOffset.X, y + ObjectOffset.Y, z + ObjectOffset.Z);
    }

    /// <summary>Where the object's own origin is — what vphysics reports as the object's position.</summary>
    /// <returns>The core's position plus the offset turned by the core's orientation.</returns>
    public (double X, double Y, double Z) ObjectOrigin()
    {
        (float X, float Y, float Z) turned = IvpQuaternion.Rotate(Orientation, ObjectOffset);

        return (Position.X + turned.X, Position.Y + turned.Y, Position.Z + turned.Z);
    }

    /// <summary>The hull's FACES, indexing <see cref="Hull"/> — the ledge triangles from the `.phy`.</summary>
    /// <remarks>
    /// **Read all along and thrown away one line before the physics saw them** (B306).
    /// `PhysicsLedge` carries `Points` AND `Triangles` out of the `IVPS` compact ledge, and
    /// `RagdollBody.HullInBoneSpace` kept only the points — so a body reached the solver as a bare
    /// point cloud.
    ///
    /// **That is what forced the narrow phase to be ours rather than the engine's.** With no faces
    /// there is no incident face to clip, no edge to test another edge against, and no way to ask
    /// vphysics' own question — the hull against a triangle (`virtualmesh.h`,
    /// `IVirtualMeshEvent::GetTrianglesInSphere`) — so what remained was sampling each vertex
    /// against a triangle PLANE inside a slab, and every compensator around it.
    ///
    /// **Empty is legitimate** and means the same as it always did: a body whose solid declared no
    /// ledge geometry, which the per-vertex path already handles.
    /// </remarks>
    public IReadOnlyList<(int A, int B, int C)> Faces
    {
        get => _faces;

        set => _faces = value ?? [];
    }

    /// <summary>The body's undiscarded ledges — what a real mindist needs, kept beside the GJK path's flat <see cref="Hull"/>/<see cref="Faces"/> (B369).</summary>
    /// <remarks>
    /// `IvpMindist` and <see cref="IvpLedgeSide.FromLedge"/> both need a <see cref="PhysicsLedge"/>'s triangles, edge
    /// offsets and material indices, which flattening into <see cref="Hull"/>/<see cref="Faces"/> already discards.
    /// See <see cref="LedgeTreeRoot"/> for what a full mindist search over this list still needs.
    /// </remarks>
    public IReadOnlyList<PhysicsLedge> Ledges { get; set; } = [];

    /// <summary>
    /// This body's one ledge, as the terminal node a mindist search needs to name it — <see cref="PhysicsLedgeTree.SingleLedge"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException"><see cref="Ledges"/> is empty.</exception>
    /// <exception cref="NotSupportedException">
    /// <see cref="Ledges"/> has more than one entry. A genuinely compound body needs the real ledge tree
    /// `IvpWorldCollision` already builds for the world — matching a body against a REAL multi-ledge `.phy` solid is
    /// not yet ported; see `docs/HANDOFF.md`, item 3. Every TF2 ragdoll bone this project has read has exactly one.
    /// </exception>
    public PhysicsLedgeTreeNode LedgeTreeRoot => Ledges.Count switch
    {
        0 => throw new InvalidOperationException("A core with no ledges was asked for its ledge tree."),
        1 => PhysicsLedgeTree.SingleLedge(Ledges[0]),
        _ => throw new NotSupportedException(
            "LedgeTreeRoot is ported for exactly one ledge - a ragdoll bone's ordinary single-convex .phy solid. "
            + "A genuinely compound body (more than one ledge) needs the real ledge tree IvpWorldCollision already "
            + "builds for the world, which is not ported for a moving body."),
    };

    /// <summary>Each contact FEATURE's tangential slip, carried between steps, keyed by normal.</summary>
    /// <remarks>
    /// **A friction contact in IVP SURVIVES between PSIs, and this is what that survival needs.**
    /// `FUN_1800857c0` builds its right-hand side as `weight × stored − current velocity`, reading
    /// the tangential pair at `contact+0x68`/`+0x6c` that its own previous solve wrote back. The
    /// contact object is persistent — cached on the mindist, split and merged by `FUN_180086e80` as
    /// an object's contact list changes — so friction converges across steps instead of being
    /// rediscovered from nothing in each one.
    ///
    /// **Keyed by the manifold's NORMAL, and the key is the whole of it.** This was keyed by hull
    /// point first, which looks equivalent and is not: a manifold's representative is whichever of
    /// its vertices comes first in the contact list, and that changes as a body rocks — so each
    /// step's warm start read a slot the previous step had not written. Measured, the difference is
    /// total. Keyed by point, a body on a one-in-ten slope ACCELERATED, 8.2 units a second at six
    /// seconds and 18.5 at twelve. With the warm start switched off entirely it came to rest.
    /// A normal is stable for exactly as long as the feature is, which is what the mindist's own
    /// identity means.
    ///
    /// **Warm starting is not an optimisation here; it is where a resting body's holding force
    /// comes from** — a stateless pass can only react to the slide it can already see.
    /// </remarks>
    public IList<(
        (float X, float Y, float Z) Normal,
        float Holding,
        float First,
        float Second,
        (float X, float Y, float Z) Local,
        int Point)> Sliding { get; } = [];


    private IReadOnlyList<(float X, float Y, float Z)> _hull = [];

    private IReadOnlyList<(int A, int B, int C)> _faces = [];

    /// <summary>When this body was last stepped — <c>core+0x1d0</c>, absolute.</summary>
    public double LastStepped { get; set; }

    /// <summary>The inverse of the step this body is in — <c>core+0x1d8</c>.</summary>
    /// <remarks>
    /// **What turns an elapsed time into a fraction of the step**, so a body can be placed at any moment
    /// inside it (<see cref="TransformAt"/>). Two engine paths write it, and they differ in one respect
    /// (B369, `docs/findings/51`):
    ///
    /// <code>
    /// integrator, FUN_180099a00 (both callers):  dt ≤ 1e-10 ? 1e10 : (float)(1.0 / dt)
    /// sleep reset, FUN_180078bd0 from env+0x110: (float)(1.0 / step), unguarded
    /// </code>
    /// </remarks>
    public float InverseStep { get; set; }

    /// <summary>Where this body is at a moment inside its current step — <c>FUN_1800734e0</c>.</summary>
    /// <param name="time">An absolute environment time, normally between this body's stamp and the next.</param>
    /// <returns>The position, and the orientation interpolated through the step.</returns>
    /// <remarks>
    /// **This is what IVP's time-of-impact search evaluates** — both bodies of a pair at lattice times
    /// inside the step, so "where is the limb at t" has exactly this meaning to the engine (B369):
    ///
    /// <code>
    /// position(t) = core+0x150 + core+0x170 × (float)(t − core+0x1d0)
    /// rotation(t) = FUN_180071060(core+0x180, core+0x1a0, (float)(t − core+0x1d0) × core+0x1d8)
    /// </code>
    ///
    /// **The elapsed time is narrowed to `float` before either use**, as the engine narrows it, and the
    /// position moves by the COMMITTED velocity — the same one-step lag the integrator has.
    ///
    /// **This is the CORE at `t`.** The engine's routine goes on to compose the object's offset inside the core
    /// (`object+0x60`) into the translation; that half is <see cref="IvpMotionCache.Fresh"/>, which builds the
    /// matrix. This remark used to say the offset was skipped "because in this project a body and its core
    /// are one thing" — which was never true of the engine and stopped being true here with B403.
    /// </remarks>
    public ((double X, double Y, double Z) Position, (double X, double Y, double Z, double W) Orientation)
        TransformAt(double time)
    {
        float elapsed = (float)(time - LastStepped);

        (double X, double Y, double Z) position = (
            Position.X + ((double)PreviousVelocity.X * elapsed),
            Position.Y + ((double)PreviousVelocity.Y * elapsed),
            Position.Z + ((double)PreviousVelocity.Z * elapsed));

        // MULSS with the elapsed time the destination, widened for FUN_180071060's double fraction.
        (double X, double Y, double Z, double W) orientation =
            IvpQuaternion.Interpolate(Orientation, WorkingOrientation, IvpMath.Mulss(elapsed, InverseStep));

        return (position, orientation);
    }

    /// <summary>Brings this body to rest as a sleeping core — <c>FUN_180078bd0</c>.</summary>
    /// <param name="step">The environment's simulation step, <c>env+0x108</c>.</param>
    /// <remarks>
    /// **The core resets itself**, which is the engine's shape: `FUN_180078c90` puts a core to sleep by
    /// calling this for it. It does three things this project's sleep used to do only the first of:
    ///
    /// - **Zeroes every velocity, the STAGED ones included** — `core+0x110` and `+0x120` beside `+0x130`,
    ///   `+0x140` and `+0x170`. A push staged just before sleep would otherwise land after waking.
    /// - **Copies the committed orientation over the predicted one** (`0x1a0 := 0x180`), so both ends of
    ///   <see cref="TransformAt"/>'s interpolation are the same and a sleeping body stays put mid-step.
    /// - **Sets <see cref="InverseStep"/> from `env+0x110`**, which `SetSimulationTimestep` writes as
    ///   `1.0 / step` with no guard.
    ///
    /// Not carried, because nothing here names them: `+0x80`, `+0x1dc`, `+0x254`, `+0x1c0..0x1c8`, and the
    /// state byte at `+1`.
    /// </remarks>
    public void Sleep(float step)
    {
        Velocity = (0f, 0f, 0f);
        AngularVelocity = (0f, 0f, 0f);
        PreviousVelocity = (0f, 0f, 0f);
        PendingVelocity = (0f, 0f, 0f);
        PendingAngularVelocity = (0f, 0f, 0f);

        InverseStep = (float)(1.0 / step);

        WorkingOrientation = Orientation;
    }

    /// <summary>Whether gravity passes this body by — bit <c>0x10</c> of <c>core+0x0</c>.</summary>
    /// <remarks>
    /// **The whole gravity step is inside `if ((*pbVar4 &amp; 0x10) == 0)`**, so a body carrying this
    /// bit skips the two helper calls as well as the acceleration.
    /// </remarks>
    public bool SkipsGravity { get; set; }

    /// <summary>Whether this body takes the environment's SECOND acceleration — bit <c>0x20</c>.</summary>
    /// <remarks>
    /// **IVP holds two gravity vectors and picks between them per body**, at
    /// `controller+0x10/0x14/0x18` and `controller+0x20/0x24/0x28`. Nothing in TF2 sets it, and it
    /// is carried anyway because the engine has it — a transcription with one global vector is
    /// right for this game and wrong for the engine.
    /// </remarks>
    public bool UsesAlternateGravity { get; set; }

    /// <summary>Whether this body is immovable — bits <c>0x2</c> and <c>0x10</c> of <c>core+0x0</c>.</summary>
    /// <remarks>
    /// **Read from two independent sites that never shared a session**, which is what makes it
    /// trustworthy: the contact builder zeroes a body's mass and inertia contribution on
    /// `core+0x0 &amp; 2`, and the island driver skips integrating a core on the same bit. The
    /// constraint solver's cache builder tests `&amp; 0x12` — two bits — so there is a second bit,
    /// `0x10`, that stops a body being pushed by a constraint while still letting it integrate.
    ///
    /// **This is how STATIC MAP GEOMETRY is represented**: an ordinary body with the bit set, not a
    /// separate type. `CreatePolyObjectStatic` takes the identical `CPhysCollide *` as the moving
    /// variant, so there is no separate world path at the API boundary either.
    /// </remarks>
    public bool Immovable { get; set; }

    /// <summary>Bit <c>0x1</c> of <c>core+0x0</c>.</summary>
    /// <remarks>
    /// *Named by its bit because what IVP calls it is unread.* The friction system sets it on every movable core when it freezes a
    /// heap of more than 150 contacts (`FUN_1800a9bf0`), and the same routine moves a contact to the head of its list only when
    /// both its objects' friction cores carry it.
    /// </remarks>
    public bool FlagBit0 { get; set; }

    /// <summary>Bit <c>0x8</c> of <c>core+0x0</c>.</summary>
    /// <remarks>
    /// *Named by its bit because what IVP calls it, and what sets it, are unread.* The rotation step `FUN_180099fc0` takes its
    /// second route for a core carrying it — real sines, no sub-steps and no Euler update
    /// (<see cref="IvpIntegrator.Rotate(IvpRigidBody, float, int, bool)"/>).
    /// </remarks>
    public bool FlagBit3 { get; set; }

    /// <summary>A movable core's share of the one friction system it is in — <c>core+0x60</c>.</summary>
    public IvpFrictionInfo? FrictionInfo { get; set; }

    /// <summary>An immovable core's share of each friction system it is in — the hash at <c>core+0x60</c>, keyed by system.</summary>
    public Dictionary<IvpFrictionSystem, IvpFrictionInfo> FrictionInfos { get; } = [];

    /// <summary>This core's share of a friction system, or none — <c>FUN_180077f00(core, system)</c>.</summary>
    /// <param name="system">The system.</param>
    /// <returns>The share, or null when this core is not in the system.</returns>
    /// <remarks>
    /// **An immovable core looks the system up in its hash (`FUN_180072350`); a movable one answers its one share when that share's
    /// system is this one.**
    /// </remarks>
    public IvpFrictionInfo? FrictionInfoIn(IvpFrictionSystem system)
    {
        if (Immovable)
        {
            return FrictionInfos.GetValueOrDefault(system);
        }

        return FrictionInfo is { } info && ReferenceEquals(info.System, system) ? info : null;
    }

    /// <summary>What a collision's freeze check left in bits 6–7 of <c>core+0x0</c>, zero to three (B369).</summary>
    /// <remarks>
    /// **Two writers are read.** Committing an impact, `FUN_18008ddf0` sets the bits from the anomaly manager's answer once
    /// <see cref="Collisions"/> exceeds the limit; and the impact solver `FUN_18008e290`, finding either core's bits set, sets
    /// both cores' to one — zero for an immovable core — and moves each core's velocities into its pending ones. *What
    /// reads the bits after the solve, and what clears them, is unread.*
    /// </remarks>
    public int CollisionFreeze { get; set; }

    /// <summary>The core's transform at <c>core+0x90</c>, in doubles — what a contact's arm and normal are measured in.</summary>
    /// <remarks>
    /// **The frame every contact routine turns through**: the contact record's arms (`FUN_18008d0c0`), a point's velocity
    /// (`FUN_180077fa0`) and the impact solver's pushes (`FUN_180070620`). A core brought to an event's time has it rebuilt
    /// from its working orientation and extrapolated position (`FUN_180078d60`). The identity by default.
    /// </remarks>
    public IvpMatrix CoreMatrix { get; set; } = IvpMatrix.FromRotation((0f, 0f, 0f, 1f), (0d, 0d, 0d));

    /// <summary>The float at <c>core+0x8</c>, which the anomaly check reads beside <see cref="HasOffset58"/>.</summary>
    /// <remarks>*Named by its offset because nothing read so far says what it is; no ragdoll element sets it.*</remarks>
    public float Offset08 { get; set; }

    /// <summary>The core's radius — the float at <c>core+0x4</c>, which the push-out estimate turns a spin into a distance with.</summary>
    /// <remarks>
    /// Set once by `FUN_180078b90`: the surface manager's upper radius, how far the surface's mass centre sits from the centre
    /// asked about, and the object's extra radius (`docs/findings/51`, beside `core+0x54 = 0.5f / core+0x4`). *That writer is
    /// not ported; no ragdoll element sets this yet.*
    /// </remarks>
    public float Radius { get; set; }

    /// <summary>The core's linear speed, <c>core+0x1dc</c> — what the range manager and a hull's gradient read.</summary>
    public float LinearSpeed { get; set; }

    /// <summary>How fast a point on the core's surface can move because of its spin, <c>core+0x254</c>.</summary>
    public float SurfaceSpeedBound { get; set; }

    /// <summary>Whether the pointer at <c>core+0x58</c> is set.</summary>
    /// <remarks>
    /// *Named by its offset because its writer is unread.* Two readers are read: the anomaly check skips a core's spin limit
    /// when this is set and <see cref="Offset08"/> is zero or NaN (`FUN_18008dd00`), and the impact solver's entry gives a
    /// pair with either core's set a cone of `(1, 0)` (`FUN_18008ed60`). The rotation step's second route turns such a core
    /// about <see cref="Offset58Axis"/> by a sine of its own (`FUN_180099fc0`). No ragdoll element sets it.
    /// </remarks>
    public bool HasOffset58 { get; set; }

    /// <summary>The axis the object at <c>core+0x58</c> names — the int at <c>+0x48</c> of that object's <c>+0x8</c>.</summary>
    /// <remarks>
    /// *Named by its offset because its writer is unread.* `FUN_180099fc0`'s second route, for a core with
    /// <see cref="HasOffset58"/> set and <see cref="Offset08"/> zero or NaN, turns the other two axes through `FUN_180070f50`
    /// and this one by `sin((ω·0.5)·dt)` of its own, and composes the two. Read as `0`, `1` or `2`.
    /// </remarks>
    public int Offset58Axis { get; set; }

    /// <summary>
    /// The core's state saved before <see cref="RebuildMatrixAtEventTime"/> refines it for a collision, or null before the
    /// first — <c>core+0x260</c>'s arena block, restored by <see cref="RestoreFromSnapshot"/> (<c>FUN_180079120</c>).
    /// </summary>
    public IvpCoreSnapshot? PendingSnapshot { get; set; }

    /// <summary>
    /// The core's position at the exact collision event time, extrapolated by its committed velocity —
    /// <c>core+0xf0/0xf8/0x100</c>, written by <see cref="RebuildMatrixAtEventTime"/>.
    /// </summary>
    public (double X, double Y, double Z) EventPosition { get; set; }

    /// <summary>Restores a queued snapshot into this core's bounds and transform, then clears it — <c>FUN_180079120</c>.</summary>
    /// <remarks>Called at the top of a PSI, once, for a core that named a snapshot whose <see cref="IvpCoreBounds"/> agree.</remarks>
    public void RestoreFromSnapshot(IvpCoreSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        AngularVelocity = snapshot.AngularVelocity;
        Orientation = snapshot.Orientation;
        WorkingOrientation = snapshot.WorkingOrientation;
        PendingSnapshot = null;
    }

    /// <summary>
    /// Refines this core's transform to the exact collision event time — <c>IvpRigidBody::RebuildMatrixAtEventTime</c>
    /// (<c>180078d60</c>).
    /// </summary>
    /// <param name="now">The environment's time — the collision event's own.</param>
    /// <remarks>
    /// **Saves the current state first** (<see cref="PendingSnapshot"/>), so a later PSI's own integration is not left
    /// working from a value this refinement already consumed. Computes the full step's rotation delta
    /// (<c>conjugate(Orientation) ⊗ WorkingOrientation</c>) BEFORE narrowing <see cref="WorkingOrientation"/> to the
    /// event-time interpolation, then rebuilds <see cref="CoreMatrix"/> and <see cref="EventPosition"/> at that instant.
    /// **Unless <see cref="FlagBit3"/>**, recovers the exact instantaneous angular velocity the discrete step implied —
    /// `asin` of each delta axis, scaled by twice the inverse step — rather than trusting whatever Euler approximation is
    /// already stored.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Never; kept for parity with other transform methods.</exception>
    public void RebuildMatrixAtEventTime(double now)
    {
        PendingSnapshot = new IvpCoreSnapshot(AngularVelocity, Orientation, WorkingOrientation);

        (double X, double Y, double Z, double W) delta = IvpQuaternion.Product(Conjugate(Orientation), WorkingOrientation);

        float elapsed = (float)(now - LastStepped);

        WorkingOrientation = IvpQuaternion.Interpolate(Orientation, WorkingOrientation, IvpMath.Mulss(elapsed, InverseStep));

        EventPosition = (
            Position.X + ((double)PreviousVelocity.X * elapsed),
            Position.Y + ((double)PreviousVelocity.Y * elapsed),
            Position.Z + ((double)PreviousVelocity.Z * elapsed));

        CoreMatrix = IvpMatrix.FromRotation(WorkingOrientation, EventPosition);

        if (!FlagBit3)
        {
            double scale = (double)InverseStep + InverseStep;

            AngularVelocity = (
                (float)(Math.Asin(delta.X) * scale),
                (float)(Math.Asin(delta.Y) * scale),
                (float)(Math.Asin(delta.Z) * scale));
        }
    }

    /// <summary>The conjugate of a rotation: its imaginary components negated.</summary>
    private static (double X, double Y, double Z, double W) Conjugate((double X, double Y, double Z, double W) rotation) =>
        (-rotation.X, -rotation.Y, -rotation.Z, rotation.W);

    /// <summary>The velocity of a point fixed to this core — <c>FUN_180077fa0</c>.</summary>
    /// <param name="arm">The point, in the core's frame.</param>
    /// <param name="velocity">The core's velocity, as the caller holds it.</param>
    /// <param name="spin">The core's angular velocity, as the caller holds it.</param>
    /// <returns>The point's velocity in the world.</returns>
    /// <remarks>
    /// **`spin × arm` in float, turned into the world by <see cref="CoreMatrix"/> in double and narrowed, then the velocity added
    /// in float.** The callers pass their own velocities rather than the core's, which is why they are arguments.
    /// </remarks>
    public (float X, float Y, float Z) PointVelocity(
        (float X, float Y, float Z) arm, (float X, float Y, float Z) velocity, (float X, float Y, float Z) spin)
    {
        (float X, float Y, float Z) swept = (
            (spin.Y * arm.Z) - (spin.Z * arm.Y),
            (arm.X * spin.Z) - (spin.X * arm.Z),
            (spin.X * arm.Y) - (arm.X * spin.Y));

        (double X, double Y, double Z) turned = CoreMatrix.Rotate((swept.X, swept.Y, swept.Z));

        return ((float)turned.X + velocity.X, (float)turned.Y + velocity.Y, (float)turned.Z + velocity.Z);
    }

    /// <summary>What a push of one unit at a point does to this core — <c>FUN_180078f50</c>.</summary>
    /// <param name="arm">The point, in the core's frame.</param>
    /// <param name="local">The push's direction in the core's frame.</param>
    /// <param name="world">The same direction in the world.</param>
    /// <returns>The change in velocity and in angular velocity.</returns>
    /// <remarks>
    /// **`(arm × local) ⊙ inverse inertia` in float for the spin, and `world · inverse mass` in double, narrowed, for the
    /// velocity** — the cross product taken `(local.z·arm.y − local.y·arm.z, local.x·arm.z − arm.x·local.z, arm.x·local.y −
    /// local.x·arm.y)`.
    /// </remarks>
    public ((float X, float Y, float Z) Velocity, (float X, float Y, float Z) Spin) UnitPush(
        (float X, float Y, float Z) arm, (float X, float Y, float Z) local, (float X, float Y, float Z) world)
    {
        (float X, float Y, float Z) spin = (
            ((local.Z * arm.Y) - (local.Y * arm.Z)) * InverseInertia.X,
            ((local.X * arm.Z) - (arm.X * local.Z)) * InverseInertia.Y,
            ((arm.X * local.Y) - (local.X * arm.Y)) * InverseInertia.Z);

        double mass = InverseMass;

        return (((float)(world.X * mass), (float)(world.Y * mass), (float)(world.Z * mass)), spin);
    }

    /// <summary>The mass this core presents to a push at a point — <c>FUN_1800770f0</c>.</summary>
    /// <param name="arm">The point, in the core's frame.</param>
    /// <param name="local">The push's direction in the core's frame.</param>
    /// <param name="world">The same direction in the world.</param>
    /// <returns><c>1</c> for a core flagged <c>0x10</c>; otherwise the reciprocal of the point's whole response.</returns>
    /// <remarks>
    /// **The reciprocal of the response's LENGTH, not of its component along the push**: <see cref="UnitPush"/>'s changes become
    /// a point velocity through <see cref="PointVelocity"/>, and the answer is `1.0 / FUN_18006e120` of that.
    /// </remarks>
    public double VirtualMass(
        (float X, float Y, float Z) arm, (float X, float Y, float Z) local, (float X, float Y, float Z) world)
    {
        if (SkipsGravity)
        {
            return 1d;
        }

        ((float X, float Y, float Z) velocity, (float X, float Y, float Z) spin) = UnitPush(arm, local, world);

        return 1d / IvpVector.Length(PointVelocity(arm, velocity, spin));
    }

    /// <summary><c>DAT_1800fcfd8</c>: how far, squared, a core may drift from its anchor.</summary>
    private static readonly double NearAnchor = BitConverter.Int64BitsToDouble(0x3f1a36e2d7731900);

    /// <summary><c>DAT_1800fcfd0</c>: how far a core may turn from its anchor, as <c>2·(1 − dot²)·r²</c>.</summary>
    private static readonly double TurnedFromAnchor = BitConverter.Int64BitsToDouble(0x3efa36e2d7731900);

    /// <summary><c>DAT_1800fcfe0</c>: the settle anchor's drift, squared.</summary>
    private static readonly double NearSettleAnchor = BitConverter.Int64BitsToDouble(0x3f847ae151eb8520);

    /// <summary><c>DAT_1800fcfe8</c>: the settle anchor's turn.</summary>
    private static readonly double TurnedFromSettleAnchor = BitConverter.Int64BitsToDouble(0x3fa47ae151eb8520);

    /// <summary><c>DAT_1800fcff0</c>: <c>3π/4</c>, the turn a resting core may make over the rest delay.</summary>
    private static readonly double SpinAllowance = BitConverter.Int64BitsToDouble(0x4002d97c7f3321d2);

    /// <summary><c>DAT_1800ed2f8</c>: how long, in seconds, a core must hold its settle anchor to count as resting after re-anchoring.</summary>
    private const float SettleTime = 4f;

    /// <summary>The position the rest test last anchored this core at — the floats at <c>core+0x230</c>.</summary>
    public (float X, float Y, float Z) RestAnchorPosition { get; set; }

    /// <summary>The working orientation at that moment — <c>core+0x210</c>.</summary>
    public (float X, float Y, float Z, float W) RestAnchorOrientation { get; set; }

    /// <summary>When that was — <c>core+0x200</c>.</summary>
    public double RestAnchorTime { get; set; }

    /// <summary>The wider anchor, moved only past its own bounds — <c>core+0x240</c>.</summary>
    public (float X, float Y, float Z) SettleAnchorPosition { get; set; }

    /// <summary>The committed orientation when the wider anchor moved — <c>core+0x220</c>.</summary>
    public (float X, float Y, float Z, float W) SettleAnchorOrientation { get; set; }

    /// <summary>When that was — <c>core+0x208</c>.</summary>
    public double SettleAnchorTime { get; set; }

    /// <summary>Whether this core is moving, still, or at rest — <c>FUN_180077220</c>.</summary>
    /// <param name="now">The environment's time, <c>env+0x188</c>.</param>
    /// <param name="restDelay">How long a core must stay by its anchor — the environment's float at <c>+0xc8</c>.</param>
    /// <returns>What the unit's PSI keeps in the core's byte <c>+0x1</c>.</returns>
    /// <remarks>
    /// <code>
    /// |P − A|² > DAT_1800fcfd8, or 2·(1 − (q·Q)²)·r·r > DAT_1800fcfd0      (A, q the anchor; Q working; r core+0x4) → re-anchor
    /// (float)(now − core+0x200) ≤ delay, a NaN too → Still
    /// (ωx² + ωy²) + ωz² ≤ ((float)(3π/4 / delay))², or the turn of (float)Q′ against Q ≤ DAT_1800fcfd0 → Resting
    /// re-anchor:  q = (float)Q;  A = (float)P;  core+0x200 = now
    ///     |P − B|² > DAT_1800fcfe0, or the turn of q₂ against Q′ > DAT_1800fcfe8 → q₂ = (float)Q′;  B = (float)P;  core+0x208 = now → Moving
    ///     (float)(now − core+0x208) > 4 → Resting, else Moving
    /// </code>
    /// Every dot is `(a.w·Q.w + a.z·Q.z) + (a.y·Q.y + a.x·Q.x)` with the float anchor widened and the destination, every
    /// distance `((P.y − B.y)² + (P.x − B.x)²) + (P.z − B.z)²`; each comparison falls the way `COMISD`/`JA` or `JBE` does on a
    /// NaN. Pinned by the `vphysics-rest` probe (`IvpRestConformanceTests`). **The operand destinations in those sums and
    /// products are carried but no output can see them**: every one feeds a comparison, and a NaN compares false whatever its
    /// payload.
    /// </remarks>
    public IvpCoreMotion TestRest(double now, float restDelay)
    {
        if (SquaredDistance(RestAnchorPosition) > NearAnchor || Turn(RestAnchorOrientation, WorkingOrientation) > TurnedFromAnchor)
        {
            return Reanchor(now);
        }

        if ((float)(now - RestAnchorTime) > restDelay)
        {
            float limit = (float)(SpinAllowance / restDelay);
            float spin = IvpMath.Addss(
                IvpMath.Addss(IvpMath.Mulss(AngularVelocity.X, AngularVelocity.X), IvpMath.Mulss(AngularVelocity.Y, AngularVelocity.Y)),
                IvpMath.Mulss(AngularVelocity.Z, AngularVelocity.Z));

            if (spin > IvpMath.Mulss(limit, limit) && Turn(Narrow(Orientation), WorkingOrientation) > TurnedFromAnchor)
            {
                return Reanchor(now);
            }

            return IvpCoreMotion.Resting;
        }

        return IvpCoreMotion.Still;
    }

    private static (float X, float Y, float Z, float W) Narrow((double X, double Y, double Z, double W) rotation) =>
        ((float)rotation.X, (float)rotation.Y, (float)rotation.Z, (float)rotation.W);

    /// <summary>The rest test's re-anchoring tail: the near anchor always, the wider one past its bounds.</summary>
    private IvpCoreMotion Reanchor(double now)
    {
        RestAnchorOrientation = Narrow(WorkingOrientation);
        RestAnchorPosition = ((float)Position.X, (float)Position.Y, (float)Position.Z);
        RestAnchorTime = now;

        if (SquaredDistance(SettleAnchorPosition) > NearSettleAnchor || Turn(SettleAnchorOrientation, Orientation) > TurnedFromSettleAnchor)
        {
            SettleAnchorOrientation = Narrow(Orientation);
            SettleAnchorPosition = ((float)Position.X, (float)Position.Y, (float)Position.Z);
            SettleAnchorTime = now;

            return IvpCoreMotion.Moving;
        }

        return (float)(now - SettleAnchorTime) > SettleTime ? IvpCoreMotion.Resting : IvpCoreMotion.Moving;
    }

    private double SquaredDistance((float X, float Y, float Z) anchor)
    {
        double x = Position.X - anchor.X;
        double y = Position.Y - anchor.Y;
        double z = Position.Z - anchor.Z;

        return IvpMath.Addsd(IvpMath.Addsd(IvpMath.Mulsd(y, y), IvpMath.Mulsd(x, x)), IvpMath.Mulsd(z, z));
    }

    /// <summary>The rest test's turn: <c>2·(1 − dot²)·r·r</c>, the float anchor widened.</summary>
    private double Turn((float X, float Y, float Z, float W) anchor, (double X, double Y, double Z, double W) rotation)
    {
        double dot = IvpMath.Addsd(
            IvpMath.Addsd(IvpMath.Mulsd(anchor.W, rotation.W), IvpMath.Mulsd(anchor.Z, rotation.Z)),
            IvpMath.Addsd(IvpMath.Mulsd(anchor.Y, rotation.Y), IvpMath.Mulsd(anchor.X, rotation.X)));
        double gap = 1d - IvpMath.Mulsd(dot, dot);

        return IvpMath.Mulsd(IvpMath.Mulsd(IvpMath.Addsd(gap, gap), Radius), Radius);
    }
}

/// <summary>A core's state saved before a collision refines it — <c>core+0x260</c>'s arena block (B369, D172).</summary>
/// <param name="AngularVelocity">The angular velocity before the refinement.</param>
/// <param name="Orientation">The committed orientation before it.</param>
/// <param name="WorkingOrientation">The working orientation before it.</param>
public sealed record IvpCoreSnapshot(
    (float X, float Y, float Z) AngularVelocity,
    (double X, double Y, double Z, double W) Orientation,
    (double X, double Y, double Z, double W) WorkingOrientation);

/// <summary>
/// IVP's per-core integration step — <c>FUN_180099a00</c> (B58, D142, D146).
/// </summary>
/// <remarks>
/// **Transcribed from the decompiled body, not designed.** `src/vphysics` ships no source, so this
/// is the one part of a corpse that had to be read out of `vphysics.dll` rather than out of the SDK
/// — and the reason to transcribe rather than write a plausible Euler integrator is that a
/// plausible one produces a corpse that settles somewhere else, silently.
///
/// **The chain above it:** the PSI event `FUN_18008a020` runs the pipeline `FUN_180082560`, which
/// assembles islands in `FUN_180090700`, and `FUN_1800909d0` calls this once per awake core in each.
///
/// **Everything a viewer reads is one step old, and it is old in the same way twice.** Position
/// integrates against the PREVIOUS step's velocity, and the visible orientation is the PREVIOUS
/// step's working one. Get either backwards and the corpse draws a step ahead of TF2.
/// </remarks>
public static class IvpIntegrator
{
    /// <summary>Advances one body by one step.</summary>
    /// <param name="body">The body.</param>
    /// <param name="positionDelta">
    /// Seconds since this body was last stepped — <c>env+0x188 − core+0x1d0</c>, which is NOT the
    /// same number as <paramref name="orientationDelta"/>.
    /// </param>
    /// <param name="orientationDelta">
    /// The island's nominal step — <c>env+0x190 − env+0x188</c>, shared by every core in it.
    /// </param>
    /// <param name="phase">The environment's phase, <c>env+0x1ac</c> — see <see cref="Rotate(IvpRigidBody, float, int, bool)"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="body"/> is null.</exception>
    /// <remarks>
    /// **The order is the finding.** Verbatim, and every line of it matters:
    ///
    /// <code>
    /// dVar9 = env[0x188] - core[0x1d0];  core[0x1d0] = env[0x188];        // per-core dt
    /// core[0x150] += (double)core[0x170] * dVar9;                          // move by LAST velocity
    /// core[0x160] += (double)core[0x178] * dVar9;
    /// core[0x158] += (double)core[0x174] * dVar9;
    /// core[0x170] = core[0x140];  core[0x174] = core[0x144];  core[0x178] = core[0x148];
    /// core[0x180..0x198] = core[0x1a0..0x1b8];                             // commit, THEN integrate
    /// FUN_180070d60(core+0x1a0, core+0x1a0, delta);
    /// FUN_180070c60(core+0x1a0);
    /// </code>
    ///
    /// - **A body does not move on the step its velocity was first set.** A ragdoll handed a
    ///   velocity from its death animation stands still for one step and then goes.
    /// - **The visible orientation is committed BEFORE the working one advances.** An earlier note
    ///   in `docs/findings/51` had this the other way round; it would draw a corpse a step ahead.
    /// - **Two different clocks.** A body that has been asleep catches its POSITION up in one long
    ///   step while its ORIENTATION advances by one nominal step, because one dt is per core and the
    ///   other is per island.
    /// - **The per-core dt is narrowed to `float` before use** — `(double)(float)(dVar9 - dVar2)` —
    ///   so the arithmetic runs at single precision even though both operands are doubles.
    /// </remarks>
    public static void Step(IvpRigidBody body, double positionDelta, float orientationDelta, int phase)
    {
        ArgumentNullException.ThrowIfNull(body);

        // `(double)(float)(env[0x188] - core[0x1d0])` — the difference is taken in double and then
        // narrowed, so a long catch-up carries single-precision error exactly as the engine's does.
        double delta = (float)positionDelta;

        // **`core+0x1d8`, set before the stamp and the advance, as `FUN_180099a00` does** (B369). The
        // island driver builds it from the step being run, guarded by `DAT_1800fcfa0` — the float `1e-10`
        // widened — so a vanishing step gives `1e10` rather than an infinity. `TransformAt` reads it.
        const double VanishingStep = 1e-10f;

        body.InverseStep = orientationDelta <= VanishingStep
            ? 1e10f
            : (float)(1.0 / orientationDelta);

        // FUN_180099fc0 runs before the stamp, the move and the commit, as FUN_180099a00 calls it.
        (double X, double Y, double Z, double W) turn = Rotate(body, orientationDelta, phase);

        body.Position = (
            body.Position.X + (body.PreviousVelocity.X * delta),
            body.Position.Y + (body.PreviousVelocity.Y * delta),
            body.Position.Z + (body.PreviousVelocity.Z * delta));

        body.PreviousVelocity = body.Velocity;

        // **Commit first.** What the rest of the engine reads is last step's working orientation.
        body.Orientation = body.WorkingOrientation;
        body.WorkingOrientation = IvpQuaternion.Normalise(IvpQuaternion.Product(body.WorkingOrientation, turn));
    }

    /// <summary>Builds a step's rotation and advances the angular velocity — <c>FUN_180099fc0</c>, on the path the runtime chose.</summary>
    /// <param name="body">The body, whose angular velocity this updates.</param>
    /// <param name="delta">The step, in seconds.</param>
    /// <param name="phase">The environment's phase, <c>env+0x1ac</c>.</param>
    /// <returns>The rotation to apply to the orientation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="body"/> is null.</exception>
    public static (double X, double Y, double Z, double W) Rotate(IvpRigidBody body, float delta, int phase) =>
        Rotate(body, delta, phase, IvpMath.FusedPath);

    /// <summary><c>FUN_180099fc0</c>, with vphysics' <c>sin</c> on a given path.</summary>
    /// <param name="body">The body, whose angular velocity this updates.</param>
    /// <param name="delta">The step, in seconds.</param>
    /// <param name="phase">The environment's phase, <c>env+0x1ac</c>.</param>
    /// <param name="fused">Whether <c>sin</c> takes its fused-multiply-add path.</param>
    /// <returns>The rotation to apply to the orientation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="body"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The second route's axis is not 0, 1 or 2.</exception>
    /// <remarks>
    /// <code>
    /// bit 0x8 of core+0x0, or env+0x1ac == 5 → the second route below
    /// h = (double)dt;  k = SubSteps;  k ≠ 1 → h = h / (double)(float)k
    /// out = FUN_180071680(ω, h);  ω = FreeRotation(ω, h)
    /// k − 1 times:  d = FUN_180071680(ω, h);  out = d ⊗ out, FUN_180070d60 inlined with the NEW delta on the left;  ω = FreeRotation
    /// core+0x130 = ω
    ///
    /// second route:  core+0x58 set and core+0x8 zero or NaN (UCOMISS/JNZ) →
    ///                    a = FUN_180070f50(ω with the axis lane zeroed, dt);  s = sin(((double)ω[axis]·0.5)·dt)
    ///                    out = FUN_180070d60(a, (the axis lane (double)(float)s, the others 0, √(1 − s·s)))
    ///                otherwise out = FUN_180070f50(ω, dt);   neither updates ω
    /// </code>
    /// **Phase 5 is set after the PSI's last section** (`FUN_180082560`), so the second route is what a core integrated between
    /// PSIs takes; the integrate section itself runs in phase 0. **The inlined product is not `FUN_180070d60` with its operands
    /// swapped**: four of its multiplications take the other operand as the destination, which only a pair of NaNs can see.
    /// </remarks>
    public static (double X, double Y, double Z, double W) Rotate(IvpRigidBody body, float delta, int phase, bool fused)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (body.FlagBit3 || phase == FinishedPhase)
        {
            return Unstepped(body, delta, fused);
        }

        int steps = SubSteps(body.AngularVelocity, delta);
        double step = delta;

        if (steps != 1)
        {
            step /= (float)steps;
        }

        (float X, float Y, float Z) spin = body.AngularVelocity;
        (double X, double Y, double Z, double W) turn = IvpQuaternion.Delta(spin, step);

        spin = FreeRotation(spin, body.Inertia, body.InverseInertia, step);

        for (int index = 1; index < steps; index++)
        {
            turn = Accumulate(IvpQuaternion.Delta(spin, step), turn);
            spin = FreeRotation(spin, body.Inertia, body.InverseInertia, step);
        }

        body.AngularVelocity = spin;

        return turn;
    }

    /// <summary>How many sub-steps a step's rotation is broken into.</summary>
    /// <param name="angularVelocity">Radians per second about each axis.</param>
    /// <param name="delta">The step, in seconds.</param>
    /// <returns>At least one.</returns>
    /// <remarks>
    /// **Both constants were dumped rather than inferred from the shape of the arithmetic:**
    ///
    /// <code>
    /// dVar10 = (double)(wy*wy + wx*wx + wz*wz) * dt * dt;
    /// if (_DAT_1800fdf90 &lt; dVar10) {                       // DAT_1800fdf90 = 0.027777777777777776
    ///     auVar13 = sqrtpd(dVar10 * _DAT_1800fdfa0, …);     // DAT_1800fdfa0 = 144.0
    ///     iVar6 = (int)auVar13._0_8_ + 1;
    ///     dVar11 = dVar11 / (double)iVar6;
    /// }
    /// </code>
    ///
    /// `0.0277…` is 1/36 and 144 is 12², so the test is `|ω|·dt &gt; 1/6` radian — about 9.55 degrees
    /// of turn in one step — and the count is `floor( 12·|ω|·dt ) + 1`.
    ///
    /// **The comparison is strictly less-than**, so a turn of exactly 1/6 radian does not sub-step.
    ///
    /// **The squared spin is summed in FLOAT, `(ωy² + ωx²) + ωz²`**, then widened and multiplied by the double step twice.
    /// **The count is `CVTTSD2SI` plus one**, so a turn too large for an int truncates to `int.MinValue` and the count comes out
    /// negative: one delta over a negative step, and no further sub-steps.
    ///
    /// **A transcription that stepped rotation once per PSI is right for a settling corpse and wrong
    /// for a limb that is whipping** — which is the frame anyone watching a demo is looking at.
    /// </remarks>
    public static int SubSteps((float X, float Y, float Z) angularVelocity, float delta)
    {
        float square = IvpMath.Addss(
            IvpMath.Addss(IvpMath.Mulss(angularVelocity.Y, angularVelocity.Y), IvpMath.Mulss(angularVelocity.X, angularVelocity.X)),
            IvpMath.Mulss(angularVelocity.Z, angularVelocity.Z));
        double turn = IvpMath.Mulsd(IvpMath.Mulsd(square, delta), delta);

        return turn > SubStepThreshold ? unchecked(Truncate(Math.Sqrt(IvpMath.Mulsd(turn, SubStepScale))) + 1) : 1;
    }

    /// <summary>Euler's torque-free equations for one step.</summary>
    /// <param name="angularVelocity">Radians per second about each axis.</param>
    /// <param name="inertia">Rotational inertia about each axis.</param>
    /// <param name="inverseInertia">Its reciprocal.</param>
    /// <param name="delta">The step, in seconds.</param>
    /// <returns>The angular velocity after the step.</returns>
    /// <remarks>
    /// **All three axes read the OLD angular velocity**, which the decompiled body arranges by
    /// precomputing the cross products it needs before writing any of them back:
    ///
    /// <code>
    /// fVar17 = local_110 * local_118;    // wz * wx, the OLD wx
    /// fVar18 = local_114 * local_118;    // wy * wx, the OLD wx
    /// local_118 = (local_110 * local_114 * fVar9) * dVar11 + local_118;
    /// local_114 = (fVar17 * fVar16) * dVar11 + local_114;
    /// local_110 = (fVar18 * fVar15) * dVar11 + local_110;
    /// </code>
    ///
    /// **It is a simultaneous update.** Writing the three in sequence — which is what anyone would
    /// do — feeds the new `ωx` into `ωy` and both into `ωz`, and the error grows with the timestep.
    ///
    /// **The coefficients pair each difference with the reciprocal of the axis being written:**
    /// `(Iy − Iz)·(1/Ix)`, `(Iz − Ix)·(1/Iy)`, `(Ix − Iy)·(1/Iz)`, each in float.
    ///
    /// **The spin products are float and the rest is double**, read from the disassembly:
    /// `ωx′ = (float)((double)(float)((double)(ωz·ωy)·(double)about) · h + (double)ωx)`. The coefficient's product is taken in
    /// double and narrowed, which is carried as the binary's instructions but equals the float product for every input: two
    /// floats' mantissas multiply exactly in 48 bits, so the narrowing is the only rounding either way.
    /// </remarks>
    public static (float X, float Y, float Z) FreeRotation(
        (float X, float Y, float Z) angularVelocity,
        (float X, float Y, float Z) inertia,
        (float X, float Y, float Z) inverseInertia,
        double delta)
    {
        double aboutX = IvpMath.Mulss(inertia.Y - inertia.Z, inverseInertia.X);
        double aboutY = IvpMath.Mulss(inertia.Z - inertia.X, inverseInertia.Y);
        double aboutZ = IvpMath.Mulss(inertia.X - inertia.Y, inverseInertia.Z);

        // Every product is taken from the values passed in, never from a partly updated triple.
        float zy = IvpMath.Mulss(angularVelocity.Z, angularVelocity.Y);
        float zx = IvpMath.Mulss(angularVelocity.Z, angularVelocity.X);
        float yx = IvpMath.Mulss(angularVelocity.Y, angularVelocity.X);

        return (
            Advance(zy, aboutX, delta, angularVelocity.X),
            Advance(zx, aboutY, delta, angularVelocity.Y),
            Advance(yx, aboutZ, delta, angularVelocity.Z));
    }

    /// <summary>One lane of <see cref="FreeRotation"/>: <c>(float)((double)(float)(product·about)·h + rate)</c>.</summary>
    private static float Advance(float product, double about, double delta, float rate) =>
        (float)IvpMath.Addsd(IvpMath.Mulsd((float)IvpMath.Mulsd(product, about), delta), rate);

    /// <summary><c>CVTTSD2SI</c>: truncation toward zero, and <c>int.MinValue</c> for anything an int cannot hold.</summary>
    /// <remarks>
    /// .NET's own cast saturates instead, since .NET 9 — which no output can see: a count past an int's range comes out
    /// negative either way, and <c>(float)</c> rounds both negative counts to <c>−2³¹</c>. Carried as the binary's instruction.
    /// </remarks>
    private static int Truncate(double value) =>
        value is >= -2147483648d and < 2147483648d ? (int)value : int.MinValue;

    /// <summary><c>FUN_180099fc0</c>'s inlined product for a later sub-step: the new delta on the left, the turn so far on the right.</summary>
    private static (double X, double Y, double Z, double W) Accumulate(
        (double X, double Y, double Z, double W) delta, (double X, double Y, double Z, double W) turn)
    {
        (double d0, double d1, double d2, double d3) = delta;
        (double t0, double t1, double t2, double t3) = turn;

        return (
            IvpMath.Addsd(IvpMath.Addsd(IvpMath.Mulsd(t0, d3), IvpMath.Mulsd(t3, d0)), IvpMath.Mulsd(t2, d1)) - IvpMath.Mulsd(t1, d2),
            IvpMath.Addsd(IvpMath.Addsd(IvpMath.Mulsd(t1, d3), IvpMath.Mulsd(t3, d1)), IvpMath.Mulsd(d2, t0)) - IvpMath.Mulsd(t2, d0),
            IvpMath.Addsd(IvpMath.Addsd(IvpMath.Mulsd(t2, d3), IvpMath.Mulsd(t3, d2)), IvpMath.Mulsd(t1, d0)) - IvpMath.Mulsd(d1, t0),
            ((IvpMath.Mulsd(t3, d3) - IvpMath.Mulsd(t0, d0)) - IvpMath.Mulsd(t1, d1)) - IvpMath.Mulsd(t2, d2));
    }

    /// <summary><c>FUN_180099fc0</c>'s second route: real sines, and no sub-steps or Euler update.</summary>
    private static (double X, double Y, double Z, double W) Unstepped(IvpRigidBody body, float delta, bool fused)
    {
        (float X, float Y, float Z) spin = body.AngularVelocity;

        if (!body.HasOffset58 || body.Offset08 < 0f || body.Offset08 > 0f)
        {
            return IvpQuaternion.SineDelta(spin, delta, fused);
        }

        int axis = body.Offset58Axis;
        double rate = axis switch
        {
            0 => spin.X,
            1 => spin.Y,
            2 => spin.Z,
            _ => throw new InvalidOperationException($"core+0x58's axis must be 0, 1 or 2, not {axis}."),
        };

        (double X, double Y, double Z, double W) others =
            IvpQuaternion.SineDelta((axis == 0 ? 0f : spin.X, axis == 1 ? 0f : spin.Y, axis == 2 ? 0f : spin.Z), delta, fused);
        double sine = IvpMath.Sin(IvpMath.Mulsd(IvpMath.Mulsd(rate, 0.5d), delta), fused);
        double lane = (float)sine;
        (double X, double Y, double Z, double W) about = (
            axis == 0 ? lane : 0d, axis == 1 ? lane : 0d, axis == 2 ? lane : 0d, Math.Sqrt(1d - IvpMath.Mulsd(sine, sine)));

        return IvpQuaternion.Product(others, about);
    }

    /// <summary><c>env+0x1ac</c> once <c>FUN_180082560</c> has run its last section.</summary>
    private const int FinishedPhase = 5;

    /// <summary><c>DAT_1800fdf90</c>, dumped — 1/36, so the test is on 1/6 radian.</summary>
    private const double SubStepThreshold = 0.027777777777777776d;

    /// <summary><c>DAT_1800fdfa0</c>, dumped — 144, which is 12 squared.</summary>
    private const double SubStepScale = 144d;
}
