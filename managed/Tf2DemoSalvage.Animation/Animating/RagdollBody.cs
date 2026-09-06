using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>One rigid body of a ragdoll — Valve's <c>ragdollelement_t</c>.</summary>
/// <param name="BoneIndex">Which bone of the model it drives.</param>
/// <param name="ParentIndex">
/// Which ELEMENT it hangs from, or −1 for the root. Not a bone index, and not the skeleton's own
/// parent: *"this parent/child pair is not usually a parent/child pair in the skeleton. There are
/// often bones in between that are collapsed for simulation"* (<c>ragdoll_shared.cpp:236</c>).
/// </param>
/// <param name="OriginParentSpace">
/// Where this element's bone sits in its parent element's space, in the bind pose. It is what makes
/// the ragdoll rigid: <see cref="RagdollBody.Pose"/> rebuilds every non-root POSITION from it.
/// </param>
/// <param name="Mass">From the <c>.phy</c>, or a tonne for a fixed-constraint statue.</param>
/// <param name="Inertia">The solid's rotational inertia scale.</param>
/// <param name="Damping">Linear damping.</param>
/// <param name="RotationDamping">Angular damping.</param>
/// <param name="Volume">The hull's volume, which is the only size the text carries.</param>
public readonly record struct RagdollElement(
    int BoneIndex,
    int ParentIndex,
    Vector3 OriginParentSpace,
    float Mass,
    float Inertia,
    float Damping,
    float RotationDamping,
    float Volume);

/// <summary>
/// A model's ragdoll: the rigid bodies its <c>.phy</c> declares and the joints between them (B58).
/// </summary>
/// <remarks>
/// **`RagdollCreateObjects`, `ragdoll_shared.cpp:264`**, and the two helpers it calls. This is the
/// part of a ragdoll that IS published: the construction, the constraint data, and the bone
/// read-back. The simulation is not — `src/vphysics` is absent from the SDK and only its headers
/// ship, so `CreatePolyObject` and `CreateRagdollConstraint` are declarations of a closed
/// Havok-derived solver. **D142 records that boundary**, and nothing in this type crosses it.
///
/// **The data does almost all the work.** `models/player/soldier.phy` ships 17 solids and 16
/// ragdoll constraints with every mass, inertia, damping value, surface property and per-axis limit
/// stated in degrees. A `.phy` is KeyValues text apart from its hulls, so none of that is inferred.
///
/// **Two things here make an own-solver far smaller than it sounds**, and both are the engine's own
/// arrangement rather than a shortcut:
///
/// - <see cref="Pose"/> overwrites every non-root POSITION from its parent, so a solver owes one
///   position — the root's — and an orientation per element, not seventeen free bodies.
/// - The joints are ball joints with three independent angular limits and a friction torque per
///   axis. There is no spring, no motor and no soft constraint to reproduce.
/// </remarks>
public sealed class RagdollBody
{
    /// <summary>Valve's cap on how many bodies one ragdoll may have.</summary>
    /// <remarks><c>#define RAGDOLL_MAX_ELEMENTS 24</c>, <c>ragdoll_shared.h</c>.</remarks>
    public const int MaximumElements = 24;

    /// <summary>The mass every solid takes when the constraints are fixed.</summary>
    /// <remarks>
    /// **`solid.params.mass = 1000.f`** (<c>ragdoll_shared.cpp:186</c>), reached when
    /// `params.fixedConstraints` is set — which `CreateTFRagdoll` does for the gold and the ice
    /// corpse. Valve's own comment on the constraint side: *"Makes the ragdoll a statue..."*.
    /// </remarks>
    public const float StatueMass = 1000f;

    private RagdollBody(
        IReadOnlyList<RagdollElement> elements, IReadOnlyList<RagdollConstraint> constraints)
    {
        Elements = elements;
        Constraints = constraints;
    }

    /// <summary>The rigid bodies, in the order the <c>.phy</c> declares its solids.</summary>
    /// <remarks>
    /// **The order is load-bearing**: a constraint names its parent and child by SOLID INDEX, and
    /// `RagdollAddSolid` asserts that the running count equals the solid's own index.
    /// </remarks>
    public IReadOnlyList<RagdollElement> Elements { get; }

    /// <summary>The joints, as the file states them.</summary>
    public IReadOnlyList<RagdollConstraint> Constraints { get; }

    /// <summary>Builds a ragdoll from a model's physics and its skeleton.</summary>
    /// <param name="physics">The model's <c>.phy</c>.</param>
    /// <param name="bones">The model's bones, for the name lookup and the bind pose.</param>
    /// <param name="fixedConstraints">Whether this is a statue — the gold or ice corpse.</param>
    /// <returns>The body, or null when the file and the skeleton do not agree.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **Solids map to bones BY NAME** — `Studio_BoneIndexByName( params.pStudioHdr, solid.name )`
    /// — and the elements keep the file's order, because that is what the constraints index into.
    ///
    /// **A solid naming no bone is refused rather than skipped, and this is the one deliberate
    /// departure in this type.** `RagdollAddSolid` increments its count only on a successful
    /// lookup while constraints go on referencing the solid's own index, which is exactly what
    /// `Assert( ragdoll.listCount == solid.index )` exists to catch — and that assert is compiled
    /// out of a release build. So the engine's behaviour past that point is a mis-wired skeleton
    /// rather than a decision, and a corpse with its shin joined to its skull is worse than a
    /// corpse posed by its animation. Said out loud rather than left as a silent improvement.
    /// </remarks>
    public static RagdollBody? Build(
        PhysicsModel physics,
        IReadOnlyList<StudioBone> bones,
        bool fixedConstraints = false)
    {
        ArgumentNullException.ThrowIfNull(physics);
        ArgumentNullException.ThrowIfNull(bones);

        // `if ( !params.pCollide || params.pCollide->solidCount > RAGDOLL_MAX_ELEMENTS ) return;`
        if (physics.Solids.Count == 0 || physics.Solids.Count > MaximumElements)
        {
            return null;
        }

        RagdollElement[] elements = new RagdollElement[physics.Solids.Count];

        for (int index = 0; index < physics.Solids.Count; index++)
        {
            PhysicsSolid solid = physics.Solids[index];

            int bone = BoneIndexByName(bones, solid.Name);

            if (bone < 0)
            {
                // See the remarks: continuing would desynchronise every constraint index.
                return null;
            }

            elements[index] = new RagdollElement(
                bone,

                // `RagdollAddSolid` writes −1 here and only `RagdollAddConstraint` changes it.
                ParentIndex: -1,
                OriginParentSpace: Vector3.Zero,
                fixedConstraints ? StatueMass : solid.Mass,
                solid.Inertia,
                solid.Damping,
                solid.RotationDamping,
                solid.Volume);
        }

        foreach (RagdollConstraint constraint in physics.Constraints)
        {
            // **"Bogus constraint on ragdoll %s"** — `ragdoll_shared.cpp:217`. Valve nulls both
            // ends rather than joining a body to itself, which leaves the child parentless.
            if (constraint.Child == constraint.Parent ||
                constraint.Child < 0 || constraint.Child >= elements.Length ||
                constraint.Parent < 0 || constraint.Parent >= elements.Length)
            {
                continue;
            }

            elements[constraint.Child] = elements[constraint.Child] with
            {
                ParentIndex = constraint.Parent,
                OriginParentSpace = OriginInParentSpace(
                    bones,
                    elements[constraint.Child].BoneIndex,
                    elements[constraint.Parent].BoneIndex),
            };
        }

        return new RagdollBody(elements, physics.Constraints);
    }

    /// <summary>Turns one simulated state into bone-to-world matrices.</summary>
    /// <param name="state">Each element's position and orientation, in element order.</param>
    /// <param name="boneCount">How many bones the model has.</param>
    /// <returns>Twelve floats per bone, row-major; identity for a bone the ragdoll does not drive.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="state"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="state"/> is not one entry per element.</exception>
    /// <remarks>
    /// **`RagdollGetBoneMatrix`, `ragdoll_shared.cpp:562`**, and it is the reason a solver here owes
    /// so little:
    ///
    /// <code>
    ///   element.pObject-&gt;GetPositionMatrix( &amp;pBoneToWorld.GetBoneForWrite( boneIndex ) );
    ///   if ( element.parentIndex &gt;= 0 &amp;&amp; !ragdoll.allowStretch )
    ///   {
    ///       int parentBoneIndex = ragdoll.boneIndex[element.parentIndex];
    ///       Vector out;
    ///       VectorTransform( element.originParentSpace, pBoneToWorld.GetBone( parentBoneIndex ), out );
    ///       MatrixSetColumn( out, 3, pBoneToWorld.GetBoneForWrite( boneIndex ) );
    ///   }
    /// </code>
    ///
    /// The simulation supplies every element's ORIENTATION and the root's position; every other
    /// position is rebuilt from its parent's finished matrix and the fixed offset recorded at
    /// build. Valve's own note on why: *"overwrite the position from physics to force rigid
    /// attachment"*.
    ///
    /// **`allowStretch` is not carried**, because nothing in TF2 sets it: `ragdollparams_t` is
    /// filled by `CreateTFRagdoll` with `params.allowStretch = false` and no TF2 path changes it.
    ///
    /// **A parent is finished before its child**, which the engine gets from element order and this
    /// does not assume: a `.phy` is a stranger's file and a constraint may name a parent later in
    /// the list, so the parent is resolved on demand.
    /// </remarks>
    public float[][] Pose(
        IReadOnlyList<(Vector3 Position, Quaternion Orientation)> state, int boneCount)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.Count != Elements.Count)
        {
            throw new ArgumentException(
                "A ragdoll is posed with one state per element.", nameof(state));
        }

        float[][] bones = new float[Math.Max(boneCount, 0)][];

        for (int bone = 0; bone < bones.Length; bone++)
        {
            bones[bone] = [1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f];
        }

        bool[] done = new bool[Elements.Count];

        for (int element = 0; element < Elements.Count; element++)
        {
            Fill(element, state, bones, done);
        }

        return bones;
    }

    private void Fill(
        int element,
        IReadOnlyList<(Vector3 Position, Quaternion Orientation)> state,
        float[][] bones,
        bool[] done)
    {
        if (done[element])
        {
            return;
        }

        // Marked before the parent is resolved, so a file whose constraints form a cycle costs
        // itself one wrong bone rather than the stack. A `.phy` is untrusted input (D32).
        done[element] = true;

        RagdollElement body = Elements[element];

        if (body.BoneIndex < 0 || body.BoneIndex >= bones.Length)
        {
            return;
        }

        (Vector3 position, Quaternion orientation) = state[element];

        StudioBones.FromQuaternion(
            (orientation.X, orientation.Y, orientation.Z, orientation.W),
            (position.X, position.Y, position.Z),
            bones[body.BoneIndex]);

        if (body.ParentIndex < 0 || body.ParentIndex >= Elements.Count)
        {
            return;
        }

        Fill(body.ParentIndex, state, bones, done);

        int parentBone = Elements[body.ParentIndex].BoneIndex;

        if (parentBone < 0 || parentBone >= bones.Length)
        {
            return;
        }

        // `VectorTransform( element.originParentSpace, parentBoneToWorld, out )` — the whole
        // matrix, so a rotated parent carries its child around with it.
        float[] parent = bones[parentBone];
        Vector3 offset = body.OriginParentSpace;

        bones[body.BoneIndex][3] =
            (parent[0] * offset.X) + (parent[1] * offset.Y) + (parent[2] * offset.Z) + parent[3];

        bones[body.BoneIndex][7] =
            (parent[4] * offset.X) + (parent[5] * offset.Y) + (parent[6] * offset.Z) + parent[7];

        bones[body.BoneIndex][11] =
            (parent[8] * offset.X) + (parent[9] * offset.Y) + (parent[10] * offset.Z) + parent[11];
    }

    /// <summary>Which bone a solid names — <c>Studio_BoneIndexByName</c>.</summary>
    /// <remarks>
    /// **Case-insensitive, as the engine's is**: `Studio_BoneIndexByName` compares with
    /// `stricmp` (`bone_setup.cpp`), and a `.phy` is authored by hand where the `.mdl`'s bone names
    /// come from the compiler.
    /// </remarks>
    private static int BoneIndexByName(IReadOnlyList<StudioBone> bones, string name)
    {
        for (int bone = 0; bone < bones.Count; bone++)
        {
            if (string.Equals(bones[bone].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return bone;
            }
        }

        return -1;
    }

    /// <summary>The child bone's origin in its parent's space, in the bind pose.</summary>
    /// <remarks>
    /// **`Studio_CalcBoneToBoneTransform( hdr, child, parent, out )`, `bone_setup.cpp:1770`:**
    ///
    /// <code>
    ///   MatrixInvert( pStudioHdr-&gt;pBone( inputBoneIndex )-&gt;poseToBone, inputToPose );
    ///   ConcatTransforms( pStudioHdr-&gt;pBone( outputBoneIndex )-&gt;poseToBone, inputToPose, matrixOut );
    /// </code>
    ///
    /// with the CHILD as the input and the PARENT as the output, so the result maps child-bone
    /// space to parent-bone space and its translation column is what
    /// `MatrixGetColumn( …, 3, childElement.originParentSpace )` takes.
    ///
    /// **Swapping the two produces the negated offset**, which is a plausible-looking ragdoll
    /// assembled inside out — the failure this project keeps meeting, and why the conformance test
    /// picks bind positions that differ in two axes.
    /// </remarks>
    private static Vector3 OriginInParentSpace(
        IReadOnlyList<StudioBone> bones, int childBone, int parentBone)
    {
        if (childBone < 0 || childBone >= bones.Count ||
            parentBone < 0 || parentBone >= bones.Count)
        {
            return Vector3.Zero;
        }

        Span<float> childToPose = stackalloc float[12];

        StudioBones.Invert(bones[childBone].PoseToBone.Span, childToPose);

        Span<float> childToParent = stackalloc float[12];

        StudioBones.Concatenate(bones[parentBone].PoseToBone.Span, childToPose, childToParent);

        return new Vector3(childToParent[3], childToParent[7], childToParent[11]);
    }
}
