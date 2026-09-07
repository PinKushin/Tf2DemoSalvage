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
        IReadOnlyList<(Vector3 Position, Quaternion Orientation)> start)
    {
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

            // **Inertia is a SCALE in Valve's parameters, not a tensor** — `objectparams_t::inertia`
            // multiplies whatever the hull computes. With no hull inertia yet, mass carries the
            // magnitude and the scale carries the model's intent.
            float inertia = Math.Max(element.Mass * element.Inertia, MinimumInertia);

            bodies[index] = new IvpRigidBody
            {
                // **NOT converted into IVP's convention, and that is a stated divergence.** See the
                // remarks on this type: the environment holds gravity as Source gives it, so a body
                // converted here would fall along the wrong axis in the wrong units.
                Position = (position.X, position.Y, position.Z),
                Orientation = (orientation.X, orientation.Y, orientation.Z, orientation.W),
                WorkingOrientation = (orientation.X, orientation.Y, orientation.Z, orientation.W),
                Inertia = (inertia, inertia, inertia),
                InverseInertia = (1f / inertia, 1f / inertia, 1f / inertia),
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
            environment.Constraints.Joints.Add(new IvpRagdollJoint
            {
                BodyA = bodies[constraint.Child],
                BodyB = bodies[constraint.Parent],
                Constraint = Joint(constraint, ragdoll.Elements[constraint.Child].AxesParentSpace),
            });
        }

        return new RagdollSimulation(ragdoll, environment, bodies);
    }

    /// <summary>Advances the simulation by one step.</summary>
    public void Step() => Environment.Simulate();

    /// <summary>Every element's current position and orientation, in Source space.</summary>
    /// <returns>One entry per element, ready for <c>RagdollBody.Pose</c>.</returns>
    public (Vector3 Position, Quaternion Orientation)[] State()
    {
        (Vector3 Position, Quaternion Orientation)[] state =
            new (Vector3, Quaternion)[_bodies.Length];

        for (int index = 0; index < _bodies.Length; index++)
        {
            IvpRigidBody body = _bodies[index];

            state[index] = (
                new Vector3((float)body.Position.X, (float)body.Position.Y, (float)body.Position.Z),
                new Quaternion(
                    body.Orientation.X, body.Orientation.Y, body.Orientation.Z, body.Orientation.W));
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
    /// **The engine picks which axis is the twist by MECHANICS, not by index** — `FUN_1800393d0`
    /// takes the axis whose rotation moves the two anchors most, weighted by inverse mass, and then
    /// orders the other two by declared range. Reproducing the anchor term needs the hull inertia
    /// this type does not have yet, so the primary is chosen by the part that IS available and
    /// dominant: the widest range is the axis a joint actually turns about.
    ///
    /// **Stated as a departure rather than presented as the rule.** Getting the permutation wrong
    /// shuffles which limit clamps which motion, and nothing in a corpse's pose reports it — so it
    /// is written down here and in `docs/findings/51` rather than left to look deliberate.
    /// </remarks>
    private static IvpRagdollConstraint Joint(RagdollConstraint constraint, RagdollAxes attached)
    {
        (float Minimum, float Maximum)[] axes =
        [
            (constraint.X.Minimum, constraint.X.Maximum),
            (constraint.Y.Minimum, constraint.Y.Maximum),
            (constraint.Z.Minimum, constraint.Z.Maximum),
        ];

        int primary = Widest(axes, -1, -1);
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

    /// <summary>The floor a body's inertia is held above, so nothing divides by zero.</summary>
    /// <remarks>
    /// **A `.phy` may declare a zero mass or a zero inertia scale**, and it is a stranger's file
    /// (D32). The engine's own guard is the one in `FUN_180037bd0`, which zeroes the multiplier
    /// rather than dividing — this floor keeps the reciprocal finite before it ever gets there.
    /// </remarks>
    private const float MinimumInertia = 1e-3f;
}
