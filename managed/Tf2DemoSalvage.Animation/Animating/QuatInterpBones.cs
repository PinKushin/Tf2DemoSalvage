using System;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// <c>STUDIO_PROC_QUATINTERP</c> — a helper bone whose pose is driven by another bone's rotation
/// (B317).
/// </summary>
/// <remarks>
/// **`DoQuatInterpBone`, `bone_setup.cpp:4700-4770`.** Read-from-source. The control bone's
/// transform relative to its own parent becomes a quaternion; each authored trigger is weighed by
/// the angle between itself and that; and the triggers' positions and rotations are blended by those
/// weights.
///
/// **This REPLACES the bone's animated transform rather than adjusting it.** `CalcProceduralBone`
/// returns true and `BuildTransformations` then does `continue` (`c_baseanimating.cpp:1527`), so the
/// keyframed rotation for a procedural bone never reaches the skeleton at all. A reader who applied
/// this on top of the animation would be blending two answers where the engine takes one.
///
/// **Where it is on screen:** TF2 gives scout, heavy and demoman `hlp_forearm_L` and `hlp_forearm_R` under
/// this rule, and the `bone-flags` probe reports each of them SKINNED — the forearm mesh is weighted
/// to them, so this is the twist that keeps a forearm from pinching as the wrist turns.
/// </remarks>
public static class QuatInterpBones
{
    /// <summary>Below this the weights are treated as none at all.</summary>
    /// <remarks>
    /// `if (scale &lt;= 0.001)  // EPSILON?` — Valve's own value and Valve's own uncertainty about
    /// it (`bone_setup.cpp:4735`). Carried rather than replaced with a named epsilon, because the
    /// number decides which of two visible poses a bone takes.
    /// </remarks>
    private const float NoWeightAtAll = 0.001f;

    /// <summary>Poses one procedural bone.</summary>
    /// <param name="rule">The bone's rule, from its model.</param>
    /// <param name="controlWorld">The control bone's world matrix, already built.</param>
    /// <param name="controlParentWorld">The control bone's PARENT's world matrix.</param>
    /// <param name="parentWorld">This bone's own parent's world matrix.</param>
    /// <param name="destination">The bone's world matrix, overwritten.</param>
    /// <exception cref="ArgumentException">A span is too short to hold a 3x4 matrix.</exception>
    /// <remarks>
    /// **The control is read RELATIVE to its parent**, which is the pair of lines the rule opens
    /// with:
    ///
    /// <code>
    /// MatrixInvert( bonetoworld.GetBone( pbones[pProc->control].parent ), tmpmatrix );
    /// ConcatTransforms( tmpmatrix, bonetoworld.GetBone( pProc->control ), controlmatrix );
    /// </code>
    ///
    /// Reading the control's world matrix instead gives the right answer whenever its parent happens
    /// to be unrotated, which is every simple fixture and no real skeleton.
    /// </remarks>
    public static void Build(
        StudioQuatInterp rule,
        ReadOnlySpan<float> controlWorld,
        ReadOnlySpan<float> controlParentWorld,
        ReadOnlySpan<float> parentWorld,
        Span<float> destination)
    {
        if (rule.Triggers is not { Count: > 0 } triggers)
        {
            return;
        }

        Span<float> inverted = stackalloc float[12];
        Span<float> control = stackalloc float[12];

        StudioBones.Invert(controlParentWorld, inverted);
        StudioBones.Concatenate(inverted, controlWorld, control);

        // `MatrixAngles( controlmatrix, src, pos )` — only the rotation is used. Valve's own
        // comment on that line notes the position argument is unwanted and asks for an overload
        // without it, so dropping it here is what the engine would do given the chance rather than
        // a shortcut taken here.
        (float X, float Y, float Z, float W) src = StudioBones.ToQuaternion(control);

        Span<float> weights = stackalloc float[triggers.Count];

        float scale = 0f;

        for (int trigger = 0; trigger < triggers.Count; trigger++)
        {
            StudioQuatInterpTrigger one = triggers[trigger];

            // **The ABSOLUTE dot, because a quaternion and its negation are the same rotation.**
            // `fabs( QuaternionDotProduct( ... ) )` — without it, a control on the far side of the
            // hemisphere weighs nothing and the bone snaps to the fallback pose.
            float dot = MathF.Abs(
                (one.TriggerX * src.X) + (one.TriggerY * src.Y) +
                (one.TriggerZ * src.Z) + (one.TriggerW * src.W));

            dot = Math.Clamp(dot, -1f, 1f);

            weights[trigger] = MathF.Max(0f, 1f - (2f * MathF.Acos(dot) * one.InverseTolerance));

            scale += weights[trigger];
        }

        (float X, float Y, float Z, float W) rotation;
        (float X, float Y, float Z) position;

        if (scale <= NoWeightAtAll)
        {
            // **Trigger ZERO outright, not the nearest and not nothing.** A control far from every
            // authored angle takes the first pose the model declares.
            StudioQuatInterpTrigger first = triggers[0];

            rotation = (first.QuatX, first.QuatY, first.QuatZ, first.QuatW);
            position = (first.PositionX, first.PositionY, first.PositionZ);
        }
        else
        {
            scale = 1f / scale;

            rotation = (0f, 0f, 0f, 0f);
            position = (0f, 0f, 0f);

            for (int trigger = 0; trigger < triggers.Count; trigger++)
            {
                if (weights[trigger] == 0f)
                {
                    continue;
                }

                float share = weights[trigger] * scale;

                StudioQuatInterpTrigger one = triggers[trigger];

                // **Aligned to the ACCUMULATOR before adding, not to the previous trigger.**
                // `QuaternionAlign( pTrigger->quat, quat, quat )` flips a trigger into the running
                // sum's hemisphere; adding two rotations that are numerically opposite and
                // geometrically equal would otherwise cancel to nothing.
                (float X, float Y, float Z, float W) aligned = StudioBones.Align(
                    rotation, (one.QuatX, one.QuatY, one.QuatZ, one.QuatW));

                rotation = (
                    rotation.X + (share * aligned.X),
                    rotation.Y + (share * aligned.Y),
                    rotation.Z + (share * aligned.Z),
                    rotation.W + (share * aligned.W));

                position = (
                    position.X + (share * one.PositionX),
                    position.Y + (share * one.PositionY),
                    position.Z + (share * one.PositionZ));
            }

            // `Assert( QuaternionNormalize( quat ) != 0 )` — an assert in Valve's build and a
            // normalise in every build, since the sum of weighted quaternions is not a unit one.
            float length = MathF.Sqrt(
                (rotation.X * rotation.X) + (rotation.Y * rotation.Y) +
                (rotation.Z * rotation.Z) + (rotation.W * rotation.W));

            if (length > 0f)
            {
                rotation = (
                    rotation.X / length, rotation.Y / length,
                    rotation.Z / length, rotation.W / length);
            }
        }

        Span<float> local = stackalloc float[12];

        StudioBones.FromQuaternion(rotation, position, local);

        StudioBones.Concatenate(parentWorld, local, destination);
    }

    /// <summary>How far a rule moved a bone from where the animation had put it.</summary>
    /// <param name="was">The bone's world matrix before the rule ran.</param>
    /// <param name="now">And after.</param>
    /// <returns>
    /// The furthest a point one unit from the bone's origin travels, in units. Zero means the rule
    /// reproduced the animated transform exactly.
    /// </returns>
    /// <remarks>
    /// **"It ran" and "it mattered" are two claims** (`docs/memory/it-ran-and-it-mattered-are-two-
    /// claims.md`). A count of bones the rule reached says the wiring works; only a magnitude says
    /// the picture changed, and a rule whose triggers happen to reproduce the animated pose would
    /// score a full count and change nothing.
    ///
    /// **This measures the AXES and not just the translation, and the first version did not.** It
    /// reported `furthest move 0 units` for ten driven bones, which reads as "the rule does
    /// nothing" and is instead a fact about the instrument: `hlp_forearm` is a TWIST, so it rotates
    /// about a fixed origin and its translation is identical by construction. Measuring the one
    /// quantity that cannot change is `CLAUDE.md`'s "wrong instrument" — the proxy was unfaithful to
    /// the variable.
    ///
    /// Taking the furthest of the origin and the three unit axes gives one number in units that
    /// covers rotation and translation together: for a pure rotation of θ it is 2·sin(θ/2), so a
    /// ten-degree twist reads about 0.17.
    /// </remarks>
    public static float Moved(ReadOnlySpan<float> was, ReadOnlySpan<float> now)
    {
        float furthest = Apart(was, now, 3);

        for (int axis = 0; axis < 3; axis++)
        {
            furthest = MathF.Max(furthest, Apart(was, now, axis));
        }

        return furthest;
    }

    /// <summary>The distance between one column of two matrices.</summary>
    /// <param name="was">The earlier matrix.</param>
    /// <param name="now">The later one.</param>
    /// <param name="column">Which column: 0-2 are the axes, 3 is the translation.</param>
    /// <returns>The distance.</returns>
    private static float Apart(ReadOnlySpan<float> was, ReadOnlySpan<float> now, int column)
    {
        float x = now[column] - was[column];
        float y = now[column + 4] - was[column + 4];
        float z = now[column + 8] - was[column + 8];

        return MathF.Sqrt((x * x) + (y * y) + (z * z));
    }
}
