using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// A model's ragdoll, running in an IVP environment (B58, D142, D146).
/// </summary>
/// <remarks>
/// **The bridge between the two halves of a corpse, and both were built before it existed.**
/// <see cref="RagdollBody"/> is the PUBLISHED half — `RagdollCreateObjects` and the bone read-back,
/// straight out of `ragdoll_shared.cpp`. <see cref="IvpEnvironment"/> is the CLOSED half,
/// transcribed out of `vphysics.dll` because `src/vphysics` ships only headers. This turns one into
/// the other: a solid becomes a body, a `ragdollconstraint` becomes a joint, and a step of the
/// environment becomes a new pose.
///
/// **The state it produces is exactly what `RagdollBody.Pose` wants** — an orientation per element
/// and a position per element, of which `Pose` keeps only the root's and rebuilds the rest from
/// each parent's finished matrix. Valve's own note on why: *"overwrite the position from physics to
/// force rigid attachment"*.
///
/// **One stated departure here, and it is narrower than it looks: inertia is isotropic.**
/// `CreatePolyObject` seeds it from the hull, and this project decodes hulls but does not yet
/// integrate one. Isotropic is what makes Euler's free-rotation terms vanish — `(Iy − Iz)` and its
/// siblings are all zero — so a wrong scalar changes how STIFF a joint is, not which way it turns.
/// Anisotropic inertia is filed rather than faked. The axis permutation is a second one, stated at
/// <see cref="Joint"/> where it is made.
///
/// **The constraint frames used to be a third and are not any more.** Both are carried: the child gets
/// `constraintToReference`, which is genuinely the identity (`ragdoll_shared.cpp:245`), and the
/// parent gets `constraintToAttached`, the rotation of `Studio_CalcBoneToBoneTransform` that
/// <see cref="RagdollBody"/> now keeps beside its translation. A joint therefore measures its
/// deflection from the BIND pose, and the bind pose reads `0`, `0`, `1` exactly.
///
/// **And one that belongs to <see cref="IvpEnvironment"/> rather than to this type: the
/// simulation runs in SOURCE units and axes, not IVP's.** `CPhysicsEnvironment::SetGravity`
/// converts on the way in — inches to metres, Z-up to Y-up — and our environment holds
/// `(0, 0, −800)` unconverted, which its own tests bake in. Nothing in the transcribed simulation
/// notices: the integrator carries no length constant, and the whole constraint solve is angular.
/// **It stops being harmless the moment hull collision arrives**, since a hull's extents are in
/// metres. So positions cross the seam here unchanged, deliberately, and the conversion is filed
/// against the environment where it belongs (`docs/findings/51`, [[ivp-is-a-third-convention]]).
/// </remarks>
public sealed class RagdollSimulation
{
    private readonly RagdollBody _ragdoll;
    private readonly IvpRigidBody[] _bodies;

    private RagdollSimulation(RagdollBody ragdoll, IvpEnvironment environment, IvpRigidBody[] bodies)
    {
        _ragdoll = ragdoll;
        _bodies = bodies;

        Environment = environment;
    }

    /// <summary>The environment this ragdoll's bodies are stepped in.</summary>
    public IvpEnvironment Environment { get; }

    /// <summary>Builds a running simulation from a model's ragdoll.</summary>
    /// <param name="ragdoll">The bodies and joints the <c>.phy</c> declares.</param>
    /// <param name="step">The simulation timestep — the demo's tick interval.</param>
    /// <param name="start">Each element's starting position and orientation, in Source space.</param>
    /// <returns>The simulation.</returns>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    /// <exception cref="ArgumentException">The starting state does not match the element count.</exception>
    /// <remarks>
    /// **Positions cross into IVP's convention on the way in** — axes, units and transpose all at
    /// once, through <see cref="IvpTransform"/>. Getting one of those three right and another wrong
    /// gives a corpse that settles at a plausible angle in the wrong place rather than something
    /// obviously broken, which is why the conversion lives in one place.
    /// </remarks>
    public static RagdollSimulation Create(
        RagdollBody ragdoll,
        float step,
        IReadOnlyList<(Vector3 Position, Quaternion Orientation)> start) =>
        Create(ragdoll, step, start, new VphysicsSurfaceProps([]));

    /// <summary>Builds a running simulation, resolving each body's surface.</summary>
    /// <param name="ragdoll">The bodies and joints the <c>.phy</c> declares.</param>
    /// <param name="step">The simulation timestep — the demo's tick interval.</param>
    /// <param name="start">Each element's starting position and orientation, in Source space.</param>
    /// <param name="surfaces">The game's surfaces, parsed by vphysics' parser, for the friction a body collides with.</param>
    /// <returns>The simulation.</returns>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    /// <exception cref="ArgumentException">The starting state does not match the element count.</exception>
    public static RagdollSimulation Create(
        RagdollBody ragdoll,
        float step,
        IReadOnlyList<(Vector3 Position, Quaternion Orientation)> start,
        VphysicsSurfaceProps surfaces)
    {
        ArgumentNullException.ThrowIfNull(surfaces);
        ArgumentNullException.ThrowIfNull(ragdoll);
        ArgumentNullException.ThrowIfNull(start);

        if (start.Count != ragdoll.Elements.Count)
        {
            throw new ArgumentException(
                "A ragdoll starts with one state per element.", nameof(start));
        }

        IvpEnvironment environment = new(step);

        IvpRigidBody[] bodies = new IvpRigidBody[ragdoll.Elements.Count];

        for (int index = 0; index < bodies.Length; index++)
        {
            RagdollElement element = ragdoll.Elements[index];
            (Vector3 position, Quaternion orientation) = start[index];

            (float mass, float inverseMass, (float X, float Y, float Z) inertia) = MassAndInertia(element);
            (float X, float Y, float Z) offset = ObjectOffset(element.MassCenter);

            // **The CORE goes to the hull's mass center and the bone stays inside it** (B403), so the
            // bone's own origin — the start position — is exactly the core plus the turned offset.
            Vector3 core = position - Vector3.Transform(new Vector3(offset.X, offset.Y, offset.Z), orientation);

            bodies[index] = new IvpRigidBody
            {
                // **NOT converted into IVP's convention, and that is a stated divergence.** See the
                // remarks on this type: the environment holds gravity as Source gives it, so a body
                // converted here would fall along the wrong axis in the wrong units.
                Position = (core.X, core.Y, core.Z),
                ObjectOffset = offset,
                Orientation = (orientation.X, orientation.Y, orientation.Z, orientation.W),
                WorkingOrientation = (orientation.X, orientation.Y, orientation.Z, orientation.W),
                Inertia = inertia,
                InverseInertia = (1f / inertia.X, 1f / inertia.Y, 1f / inertia.Z),
                Mass = mass,
                InverseMass = inverseMass,

                Hull = Points(element.Hull),

                // **Valve's own ledge faces, carried rather than dropped** (B306). See
                // `IvpRigidBody.Faces` for what their absence cost.
                Faces = element.Faces,

                // **The surface this body is made of, resolved as the game resolves a ragdoll solid's** — its `surfaceprop`,
                // else `default`. Every player element says `flesh`; a prop says whatever its `.phy` declares.
                Friction = surfaces.ObjectMaterial(element.SurfaceProp)?.Physics.Friction ?? IvpRigidBody.NoSurfaceFriction,

                // **Both damping terms, which the `.phy` has carried since it was first read and
                // nothing applied** — see `IvpDamping`. Linear is zero on every TF2 ragdoll
                // element and rotational runs 4 to 16, so this is what stops a corpse spinning.
                Damping = element.Damping,
                RotationDamping = element.RotationDamping,
            };

            environment.Add(bodies[index]);
        }

        foreach (RagdollConstraint constraint in ragdoll.Constraints)
        {
            if (constraint.Parent < 0 || constraint.Parent >= bodies.Length ||
                constraint.Child < 0 || constraint.Child >= bodies.Length)
            {
                // A `.phy` is a stranger's file (D32); a constraint naming a body that is not there
                // is dropped rather than trusted.
                continue;
            }

            if (constraint.Child == constraint.Parent)
            {
                // **"Bogus constraint on ragdoll %s"** — `ragdoll_shared.cpp:217` nulls both ends,
                // so the `childIndex >= 0 && parentIndex >= 0` gate below it never opens and no
                // constraint is created at all. `RagdollBody.Build` drops the same one.
                continue;
            }

            // **The CHILD is the reference body and the parent is the attached one**, which is the
            // opposite of what `childElement.parentIndex` suggests:
            // `CreateRagdollConstraint( childElement.pObject, ragdoll.list[parentIndex].pObject, … )`
            // (`ragdoll_shared.cpp:253`) against *"Create a constraint in the space of
            // pReferenceObject which is attached by the constraint to pAttachedObject"*
            // (`vphysics_interface.h:572`). The frames are per side, so getting this round the
            // wrong way measures every joint against the wrong bone.
            // **The same point from each end, measured from each body's CORE** (B403) — see the anchors below.
            (float X, float Y, float Z) childAnchor = bodies[constraint.Child].ObjectOffset;
            Vector3 childOrigin = ragdoll.Elements[constraint.Child].OriginParentSpace;
            (float X, float Y, float Z) parentAnchor = (
                childOrigin.X + bodies[constraint.Parent].ObjectOffset.X,
                childOrigin.Y + bodies[constraint.Parent].ObjectOffset.Y,
                childOrigin.Z + bodies[constraint.Parent].ObjectOffset.Z);

            environment.Constraints.Joints.Add(new IvpRagdollJoint
            {
                BodyA = bodies[constraint.Child],
                BodyB = bodies[constraint.Parent],
                Constraint = Joint(
                    constraint,
                    ragdoll.Elements[constraint.Child].AxesParentSpace,
                    bodies[constraint.Child],
                    bodies[constraint.Parent],
                    childAnchor,
                    parentAnchor),

                // **The same point from each end, measured from each body's CORE** (B403), which is
                // what a ball-and-socket is. The reference body is the child and the joint is at its
                // own bone origin, which inside its core is its object offset; the parent's end is
                // where the child's bone stands in the parent's space, carried into the parent's core
                // by the parent's offset. The engine creates the constraint in object space and takes
                // it into core space the same way.
                AnchorA = childAnchor,
                AnchorB = parentAnchor,
            });
        }

        return new RagdollSimulation(ragdoll, environment, bodies);
    }

    /// <summary>Applies the killing blow, as <c>RagdollCreate</c> does (B58).</summary>
    /// <param name="force">The wire's <c>m_vecForce</c>, an impulse in kg·in/s.</param>
    /// <param name="forceBone">The wire's <c>m_nForceBone</c>, or negative for none.</param>
    /// <remarks>
    /// **This is why a TF2 corpse flies and ours only fell.** `m_vecForce`, `m_vecRagdollVelocity`
    /// and `m_nForceBone` are decoded, carried into `SceneRagdoll`, and were read by nothing —
    /// three more fields with no consumer, and between them the wire's entire account of how a
    /// body left its feet ([[a-tf2-corpse-is-simulated-not-sent]]).
    ///
    /// **`RagdollCreate`, `ragdoll_shared.cpp:405`, and every line of it matters:**
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
    /// - **The struck bone takes the WHOLE force through its centre**, so it gains speed and no
    ///   spin of its own.
    /// - **Every other body takes a share by MASS, at the struck bone's position** — an offset
    ///   push, so the rest of the body swings about the hit. That is the difference between a
    ///   corpse that tumbles away from a rocket and one that slides.
    /// - **`forcePosition` gates the second loop**, and with no force bone it stays whatever the
    ///   caller passed. A corpse whose killer sent neither is pushed not at all, which is correct:
    ///   `m_vecForce` is zeroed for a death animation (`c_tf_player.cpp:847`).
    ///
    /// **The scale divides by the TOTAL mass, not by the body's own**, so the shares sum to less
    /// than one force — Valve's own comment beside it reads *"UNDONE: Test scaling the force by
    /// total mass on all bones"*, so this is deliberate and unfinished in the engine too.
    ///
    /// **The magnitudes are Valve's own and they are large**, which is worth knowing before anyone
    /// concludes the decode is wrong. `CalcDamageForceVector` sizes the impulse as
    ///
    /// <code>
    /// // Calculate an impulse large enough to push a 75kg man 4 in/sec per point of damage
    /// float forceScale = info.GetDamage() * 75 * 4;
    /// </code>
    ///
    /// `basecombatcharacter.cpp:1395` — three hundred kg·in/s per point of damage. A sixty-damage
    /// kill is eighteen thousand, and `z1800` carries 16,793, 19,191 and 23,987, which is that
    /// formula for three ordinary kills. **The struck bone weighs about ten kilos, not
    /// seventy-five**, so the whole force through its centre really is thousands of units a second
    /// — and that is why <see cref="IvpEnvironment.MaximumVelocity"/> exists.
    /// </remarks>
    public void Kill((float X, float Y, float Z) force, int forceBone)
    {
        float total = 0f;

        foreach (IvpRigidBody body in _bodies)
        {
            total += body.InverseMass > 0f ? 1f / body.InverseMass : 0f;
        }

        total = MathF.Max(total, 1f);

        if (forceBone < 0 || forceBone >= _bodies.Length)
        {
            // No struck bone means no `forcePosition`, and the engine's second loop is gated on it.
            return;
        }

        IvpPush.ApplyForceCenter(_bodies[forceBone], force);

        // `GetPosition( &forcePosition, NULL )` reports the OBJECT's origin — the bone — not its core (B403).
        (double X, double Y, double Z) struck = _bodies[forceBone].ObjectOrigin();

        (float X, float Y, float Z) at = ((float)struck.X, (float)struck.Y, (float)struck.Z);

        for (int index = 0; index < _bodies.Length; index++)
        {
            if (index == forceBone)
            {
                continue;
            }

            float share = (_bodies[index].InverseMass > 0f ? 1f / _bodies[index].InverseMass : 0f)
                / total;

            IvpPush.ApplyForceOffset(
                _bodies[index],
                (force.X * share, force.Y * share, force.Z * share),
                at);
        }
    }

    /// <summary>Gives every body the velocity the corpse inherited — <c>AddVelocity</c>.</summary>
    /// <param name="velocity">The wire's <c>m_vecRagdollVelocity</c>.</param>
    /// <remarks>
    /// **The engine gets this from the animation rather than the wire, and cannot here.**
    /// `RagdollApplyAnimationAsVelocity` (`ragdoll_shared.cpp:458`) derives a per-body velocity from
    /// TWO bone snapshots `boneDt` apart — `GetRagdollInitBoneArrays` with `boneDt = 0.05f`
    /// (`c_tf_player.cpp:890`) — so each limb inherits its own motion, an outflung arm keeping more
    /// than the hip.
    ///
    /// **A demo carries `m_vecRagdollVelocity` and not those two snapshots**, and this project
    /// seeds a corpse from one pose. So every body gets the entity's single velocity: the corpse
    /// travels correctly and its limbs do not lead or trail. **A stated departure**, and the thing
    /// that would close it is a second seed pose one `boneDt` earlier, which the timeline can
    /// produce.
    /// </remarks>
    public void Inherit((float X, float Y, float Z) velocity)
    {
        foreach (IvpRigidBody body in _bodies)
        {
            IvpPush.AddVelocity(body, velocity, (0f, 0f, 0f));
        }
    }

    /// <summary>Whether this corpse has settled and been put to sleep.</summary>
    /// <remarks>
    /// **A TF2 ragdoll that stops moving is FORCED to sleep, and this project had no such thing.**
    /// `CRagdoll::CheckSettleStationaryRagdoll` runs every frame
    /// (`game/client/ragdoll.cpp:268-297`) and it is published, not decompiled.
    /// </remarks>
    public bool Asleep { get; private set; }

    /// <summary>Advances the simulation by one step, unless it has settled.</summary>
    /// <remarks>
    /// **The settle check is the engine's, constant for constant:**
    ///
    /// <code>
    /// #define RAGDOLL_SLEEP_TOLERANCE 1.0f
    /// static ConVar ragdoll_sleepaftertime( "ragdoll_sleepaftertime", "5.0f", 0,
    ///     "After this many seconds of being basically stationary, the ragdoll will go to sleep." );
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
    /// **Per AXIS and not by distance**, which is the engine's own test and a looser one — a body
    /// creeping 0.9 units along each of three axes is stationary by this rule.
    /// `PhysForceRagdollToSleep` then does two things and both matter:
    /// `PhysForceClearVelocity` ZEROES each body's linear and angular velocity
    /// (`physics_shared.cpp:917`), and `Sleep()` stops it being integrated.
    ///
    /// **This is what stops a corpse wandering off, and its absence was measured.** Dropped on
    /// `koth_harvest_final`, five real scout ragdolls all reported NEVER SETTLED after ten seconds
    /// — still moving faster than a unit a second — because nothing here ever brought one to a
    /// stop. A body that never stops has the rest of the demo to drift, and on `z1800` some of them
    /// used it to leave the map.
    /// </remarks>
    public void Step()
    {
        if (Asleep)
        {
            return;
        }

        Environment.Simulate();

        _since += Environment.Step;

        (double X, double Y, double Z) origin = Environment.Bodies.Count > 0
            ? Environment.Bodies[0].Position
            : default;

        (double X, double Y, double Z) moved = (
            origin.X - _origin.X, origin.Y - _origin.Y, origin.Z - _origin.Z);

        _origin = origin;

        if (Math.Abs(moved.X) > SleepTolerance ||
            Math.Abs(moved.Y) > SleepTolerance ||
            Math.Abs(moved.Z) > SleepTolerance)
        {
            _since = 0f;
            return;
        }

        if (_since < SleepAfterTime)
        {
            return;
        }

        Asleep = true;

        // **Each core resets itself, as the engine's does** (B369): `FUN_180078c90` sleeps a core through
        // `FUN_180078bd0`, which zeroes the staged velocities as well as the real ones and copies the
        // committed orientation over the predicted one. This loop used to zero three velocities and
        // nothing else.
        for (int index = 0; index < Environment.Bodies.Count; index++)
        {
            Environment.Bodies[index].Sleep(Environment.Step);
        }
    }

    /// <summary><c>RAGDOLL_SLEEP_TOLERANCE</c>, `game/client/ragdoll.cpp:265`.</summary>
    private const double SleepTolerance = 1.0;

    /// <summary><c>ragdoll_sleepaftertime</c>, whose default the same line declares as 5.</summary>
    private const float SleepAfterTime = 5f;

    private (double X, double Y, double Z) _origin;
    private float _since;

    /// <summary>A hull in the tuple shape the body holds it in.</summary>
    private static (float X, float Y, float Z)[] Points(IReadOnlyList<Vector3> hull)
    {
        (float X, float Y, float Z)[] points = new (float, float, float)[hull.Count];

        for (int index = 0; index < hull.Count; index++)
        {
            points[index] = (hull[index].X, hull[index].Y, hull[index].Z);
        }

        return points;
    }

    /// <summary>A body's inverse mass and per-axis inertia, as the engine derives them for its core (B403).</summary>
    /// <remarks>
    /// **`objectparams_t` becomes an IVP template and the template and the hull become the core** —
    /// `FUN_18001c9d0` then `FUN_180073df0`, ported as <see cref="IvpObjectTemplate"/>. Every element carries its
    /// hull's inertia, since the engine has no object without one; the single `mass × scale` this project gave
    /// every body before B403 is gone.
    /// </remarks>
    private static (float Mass, float InverseMass, (float X, float Y, float Z) Inertia) MassAndInertia(RagdollElement element)
    {
        IvpObjectTemplate template = IvpObjectTemplate.FromParameters(
            element.Mass, element.Inertia, element.Damping, element.RotationDamping, element.RotationInertiaLimit);

        Vector3 hull = element.HullInertia;
        (float mass, (float X, float Y, float Z) inertia) = template.CoreInertia((hull.X, hull.Y, hull.Z));

        return (mass, 1f / mass, inertia);
    }

    /// <summary>Where a bone sits inside its core: <c>−massCenter</c>, as <c>FUN_180074380</c> stores it.</summary>
    /// <remarks>
    /// **Zeroed when its squared length is under `1e-16` square metres** (`DAT_1800fcf98`, where the engine sets bit
    /// `0x800`), carried here in square inches because this simulation runs in Source units.
    /// </remarks>
    private static (float X, float Y, float Z) ObjectOffset(Vector3 massCenter)
    {
        const float NegligibleSquared = 1e-16f * IvpTransform.InchesPerMetre * IvpTransform.InchesPerMetre;

        return massCenter.LengthSquared() < NegligibleSquared
            ? (0f, 0f, 0f)
            : (-massCenter.X, -massCenter.Y, -massCenter.Z);
    }

    /// <summary>Every element's current position and orientation, in Source space.</summary>
    /// <returns>One entry per element, ready for <c>RagdollBody.Pose</c>.</returns>
    public (Vector3 Position, Quaternion Orientation)[] State()
    {
        (Vector3 Position, Quaternion Orientation)[] state =
            new (Vector3, Quaternion)[_bodies.Length];

        for (int index = 0; index < _bodies.Length; index++)
        {
            IvpRigidBody body = _bodies[index];

            // **The bone, not the core** (B403) — vphysics' `GetPosition` composes the object offset back in.
            (double X, double Y, double Z) origin = body.ObjectOrigin();

            state[index] = (
                new Vector3((float)origin.X, (float)origin.Y, (float)origin.Z),
                new Quaternion(
                    (float)body.Orientation.X, (float)body.Orientation.Y, (float)body.Orientation.Z, (float)body.Orientation.W));
        }

        return state;
    }

    /// <summary>The bone matrices this ragdoll's current state produces.</summary>
    /// <param name="boneCount">How many bones the model has.</param>
    /// <returns>One 3×4 matrix per bone.</returns>
    public float[][] Pose(int boneCount) => _ragdoll.Pose(State(), boneCount);

    /// <summary>Writes this ragdoll's current bones into an accessor, marking what it drove.</summary>
    /// <param name="into">The accessor to write through.</param>
    /// <param name="written">Marked for every bone the ragdoll drove.</param>
    /// <remarks>
    /// **The shape `AnimatingEntity.Ragdoll` wants**, so a caller hands over a method group rather
    /// than a closure that rebuilds the state array every frame.
    /// </remarks>
    public void PoseIntoAccessor(BoneAccessor into, BoneBitList written) =>
        _ragdoll.PoseInto(State(), into, written);

    /// <summary>Turns a <c>.phy</c> constraint into a solvable joint.</summary>
    /// <remarks>
    /// **The engine picks which axis is the twist by MECHANICS, not by declared range** —
    /// `FUN_1800393d0` takes the axis whose rotation moves the two anchors most, weighted by
    /// inverse mass, and then orders the other two by declared range.
    ///
    /// **This used to guess it from the range alone, and that guess is what contorted a corpse**
    /// (B306). The departure was written down as unobservable — *"nothing in a corpse's pose
    /// reports it"* — and that was wrong: the permutation decides WHICH LIMIT CLAMPS WHICH MOTION,
    /// so a wrong one lets an elbow swing where it should twist and a knee twist where it should
    /// bend. The owner, looking at the first corpse to draw above ground: *"thats contorted as
    /// hell, theres some other parity point you havent noticed"*. This was it.
    ///
    /// **The reason given for the shortcut no longer holds either.** It said the anchor term needed
    /// hull inertia "this type does not have yet" — the anchors are `AnchorA`/`AnchorB` at the call
    /// site and the masses are on the elements, so the engine's own rule is computable here.
    ///
    /// **Both anchors count, measured from each body's core, with their arms squared, and so does how
    /// easily each core turns about the axis** — see <see cref="Turning"/>, read from the
    /// disassembly (B403).
    /// </remarks>
    private static IvpRagdollConstraint Joint(
        RagdollConstraint constraint,
        RagdollAxes attached,
        IvpRigidBody reference,
        IvpRigidBody attachedBody,
        (float X, float Y, float Z) referenceAnchor,
        (float X, float Y, float Z) attachedAnchor)
    {
        (float Minimum, float Maximum)[] axes =
        [
            (constraint.X.Minimum, constraint.X.Maximum),
            (constraint.Y.Minimum, constraint.Y.Maximum),
            (constraint.Z.Minimum, constraint.Z.Maximum),
        ];

        int primary = Turning(attached, reference, attachedBody, referenceAnchor, attachedAnchor);
        int wider = Widest(axes, primary, -1);
        int narrower = Widest(axes, primary, wider);

        return IvpRagdollConstraint.FromDegrees(
            primary: axes[primary],
            narrower: axes[narrower],
            wider: axes[wider],

            // **`constraintToReference` IS the identity for a TF2 ragdoll** — `SetIdentityMatrix`,
            // `ragdoll_shared.cpp:245` — but it is the identity PERMUTED here, because the engine
            // selects each role by column index (`iVar9` and its two siblings) rather than by name.
            reference: Frame(RagdollAxes.Identity, primary, narrower, wider),
            attached: Frame(attached, primary, narrower, wider));
    }

    /// <summary>One bone-to-bone rotation, in the permutation this joint's limits are in.</summary>
    /// <remarks>
    /// **The SAME indices select on both frames, and that is the whole invariant.** The pair exists
    /// so that `R_ref · (toReference · e_k)` and `R_att · (toAttached · e_k)` are one world vector
    /// at the bind pose; permuting one side and not the other breaks it silently, and a corpse
    /// built that way looks like a joint with the wrong limits rather than like a wiring error.
    /// </remarks>
    private static IvpConstraintFrame Frame(
        RagdollAxes axes, int primary, int narrower, int wider) =>
        new(Axis(axes[primary]), Axis(axes[narrower]), Axis(axes[wider]));

    /// <summary>A frame axis, in the tuple the constraint's own arithmetic uses.</summary>
    private static (float X, float Y, float Z) Axis(Vector3 axis) => (axis.X, axis.Y, axis.Z);

    /// <summary>The widest range not already taken.</summary>
    /// <summary>Which axis a joint actually turns about — <c>FUN_1800393d0</c>'s choice.</summary>
    /// <param name="attached">The attached body's constraint frame, whose columns are its candidate axes.</param>
    /// <param name="reference">The reference body — the child.</param>
    /// <param name="attachedBody">The attached body — the parent.</param>
    /// <param name="referenceAnchor">The joint, in the reference body's core frame.</param>
    /// <param name="attachedAnchor">The joint, in the attached body's core frame.</param>
    /// <returns>The axis index, 0 for x through 2 for z.</returns>
    /// <remarks>
    /// **Read from the disassembly** (`docs/findings/51`, *The joint's twist axis*). For each candidate axis
    /// the engine scores
    ///
    /// <code>
    ///   invMass_A · |r_A × a_A|²  +  (Σ a_A,k² · invInertia_A,k  +  Σ a_B,k² · invInertia_B,k)  +  invMass_B · |r_B × a_B|²
    /// </code>
    ///
    /// with `a_A` the reference frame's axis — the identity for a TF2 ragdoll — `a_B` the attached frame's, and
    /// each `r` its anchor from its own core; the best starts at −1 and an axis wins only by scoring strictly
    /// higher (`CMOVBE`). The bracketed sums are `FUN_18003d320` accumulating a purely angular row.
    ///
    /// **This project used to score one anchor, unsquared, measured from the bone, by the element's raw mass**
    /// (B306), which agreed while the reference anchor sat at the core. Once the core moved to the hull's mass
    /// center (B403) it could not.
    ///
    /// **The engine takes the arms and axes into the world and the axes back into each core**; a rotation
    /// changes neither the length of a cross product nor a vector's components in its own frame, so this
    /// scores in each body's frame and can differ only in the last bits that round trip adds.
    /// </remarks>
    private static int Turning(
        RagdollAxes attached,
        IvpRigidBody reference,
        IvpRigidBody attachedBody,
        (float X, float Y, float Z) referenceAnchor,
        (float X, float Y, float Z) attachedAnchor)
    {
        int turning = 0;
        float best = -1f;

        for (int axis = 0; axis < 3; axis++)
        {
            Vector3 referenceAxis = RagdollAxes.Identity[axis];
            Vector3 attachedAxis = attached[axis];

            float score = (ArmSquared(referenceAnchor, referenceAxis) * reference.InverseMass)
                + (Turn(referenceAxis, reference.InverseInertia) + Turn(attachedAxis, attachedBody.InverseInertia))
                + (ArmSquared(attachedAnchor, attachedAxis) * attachedBody.InverseMass);

            if (score > best)
            {
                best = score;
                turning = axis;
            }
        }

        return turning;
    }

    /// <summary><c>|anchor × axis|²</c> — how far turning about the axis carries the anchor, squared.</summary>
    private static float ArmSquared((float X, float Y, float Z) anchor, Vector3 axis)
    {
        float x = (anchor.Y * axis.Z) - (anchor.Z * axis.Y);
        float y = (anchor.Z * axis.X) - (anchor.X * axis.Z);
        float z = (anchor.X * axis.Y) - (anchor.Y * axis.X);

        return (x * x) + (y * y) + (z * z);
    }

    /// <summary><c>Σ a_k · (a_k · invInertia_k)</c> — <c>FUN_18003d320</c>'s diagonal for a purely angular row.</summary>
    private static float Turn(Vector3 axis, (float X, float Y, float Z) inverseInertia) =>
        (axis.X * (axis.X * inverseInertia.X))
        + (axis.Y * (axis.Y * inverseInertia.Y))
        + (axis.Z * (axis.Z * inverseInertia.Z));

    private static int Widest((float Minimum, float Maximum)[] axes, int first, int second)
    {
        int widest = -1;

        for (int index = 0; index < axes.Length; index++)
        {
            if (index == first || index == second)
            {
                continue;
            }

            if (widest < 0 ||
                axes[index].Maximum - axes[index].Minimum > axes[widest].Maximum - axes[widest].Minimum)
            {
                widest = index;
            }
        }

        return widest;
    }
}
