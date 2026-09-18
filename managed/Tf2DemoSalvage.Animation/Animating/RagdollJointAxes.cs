using System.Numerics;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>How a <c>.phy</c> joint's three axes are given their roles — twist, narrower swing, wider swing (B306, B403).</summary>
/// <remarks>
/// **The engine picks which axis is the twist by MECHANICS, not by declared range**: `FUN_1800393d0` takes the axis whose rotation
/// moves the two anchors most, weighted by inverse mass, and then orders the other two by declared range.
///
/// **Guessing it from the range alone is what contorted a corpse** (B306). The departure was once written down as unobservable, and
/// that was wrong: the permutation decides WHICH LIMIT CLAMPS WHICH MOTION, so a wrong one lets an elbow swing where it should twist
/// and a knee twist where it should bend. The owner, looking at the first corpse to draw above ground: *"thats contorted as hell,
/// theres some other parity point you havent noticed"*. This was it.
/// </remarks>
internal static class RagdollJointAxes
{
    /// <summary>One bone-to-bone rotation, in the permutation this joint's limits are in.</summary>
    /// <remarks>
    /// **The SAME indices select on both frames, and that is the whole invariant.** The pair exists so that
    /// `R_ref · (toReference · e_k)` and `R_att · (toAttached · e_k)` are one world vector at the bind pose; permuting one side and
    /// not the other breaks it silently, and a corpse built that way looks like a joint with the wrong limits rather than like a
    /// wiring error. **`constraintToReference` IS the identity for a TF2 ragdoll** — `SetIdentityMatrix`, `ragdoll_shared.cpp:245` —
    /// but it is the identity PERMUTED, because the engine selects each role by column index rather than by name.
    /// </remarks>
    internal static IvpConstraintFrame Frame(RagdollAxes axes, int primary, int narrower, int wider) =>
        new(Axis(axes[primary]), Axis(axes[narrower]), Axis(axes[wider]));

    /// <summary>Which axis a joint actually turns about — <c>FUN_1800393d0</c>'s choice.</summary>
    /// <param name="attached">The attached body's constraint frame, whose columns are its candidate axes.</param>
    /// <param name="reference">The reference body — the child.</param>
    /// <param name="attachedBody">The attached body — the parent.</param>
    /// <param name="referenceAnchor">The joint, in the reference body's core frame.</param>
    /// <param name="attachedAnchor">The joint, in the attached body's core frame.</param>
    /// <returns>The axis index, 0 for x through 2 for z.</returns>
    /// <remarks>
    /// **Read from the disassembly** (`docs/findings/51`, *The joint's twist axis*). For each candidate axis the engine scores
    ///
    /// <code>
    ///   invMass_A · |r_A × a_A|²  +  (Σ a_A,k² · invInertia_A,k  +  Σ a_B,k² · invInertia_B,k)  +  invMass_B · |r_B × a_B|²
    /// </code>
    ///
    /// with `a_A` the reference frame's axis — the identity for a TF2 ragdoll — `a_B` the attached frame's, and each `r` its anchor
    /// from its own core; the best starts at −1 and an axis wins only by scoring strictly higher (`CMOVBE`). The bracketed sums are
    /// `FUN_18003d320` accumulating a purely angular row.
    ///
    /// **This project used to score one anchor, unsquared, measured from the bone, by the element's raw mass** (B306), which agreed
    /// while the reference anchor sat at the core. Once the core moved to the hull's mass center (B403) it could not.
    ///
    /// **The engine takes the arms and axes into the world and the axes back into each core**; a rotation changes neither the length
    /// of a cross product nor a vector's components in its own frame, so this scores in each body's frame and can differ only in the
    /// last bits that round trip adds.
    /// </remarks>
    internal static int Turning(
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

    /// <summary>The widest range not already taken.</summary>
    /// <param name="axes">Each axis's range.</param>
    /// <param name="first">An index already taken.</param>
    /// <param name="second">Another, or −1 for none.</param>
    /// <returns>The index of the widest remaining range.</returns>
    internal static int Widest((float Minimum, float Maximum)[] axes, int first, int second)
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

    /// <summary>A frame axis, in the tuple the constraint's own arithmetic uses.</summary>
    private static (float X, float Y, float Z) Axis(Vector3 axis) => (axis.X, axis.Y, axis.Z);

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
}
