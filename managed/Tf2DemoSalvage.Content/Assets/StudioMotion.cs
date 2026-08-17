using System;
using System.Buffers.Binary;
using System.Collections.Generic;

using static Tf2DemoSalvage.Content.Assets.StudioLayout;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// How far an animation carries the model, and therefore how fast it was authored to move.
/// </summary>
/// <remarks>
/// **An animation states its own travel rather than being measured from its bones.** Valve stores a
/// list of <c>mstudiomovement_t</c> blocks per animation — a piecewise description of the root's
/// path, each block carrying an end frame, a start and end velocity, a direction and the cumulative
/// position at its end. Walking them gives the displacement at any point in the cycle without
/// touching a single bone track.
///
/// **This exists for one caller: the speed scaling in <c>ComputePoseParam_MoveYaw</c>.** After the
/// direction is pushed out to the box, the engine does
///
/// <code>
/// float flMaxSpeed = GetBasePlayer()->GetSequenceGroundSpeed( GetBasePlayer()->GetSequence() );
/// if ( flMaxSpeed > flSpeed ) { vecCurrentMoveYaw.x *= flSpeed / flMaxSpeed; ... }
/// </code>
///
/// so a player moving slower than their animation was authored for is drawn back towards the middle
/// of the blend grid instead of animating at a full-magnitude run. <c>GetSequenceGroundSpeed</c> is
/// <c>GetSequenceMoveDist / SequenceDuration</c> (<c>baseanimating.cpp:1096</c>).
///
/// **The duration term is already here.** <c>Studio_CPS</c> is
/// <c>Σ weight[i] · fps[i]/(numframes[i]-1)</c> and the duration is its reciprocal, so the whole
/// expression collapses: dividing a distance by <c>1/cps</c> is multiplying by <c>cps</c>, and
/// <see cref="StudioAnimation.CyclesPerSecond"/> is exactly that per-animation term. The division
/// never appears, so neither does the divide-by-zero it would need guarding for.
/// </remarks>
public static class StudioMotion
{
    /// <summary>How many piecewise movement blocks an animation declares.</summary>
    /// <param name="file">The <c>.mdl</c>'s bytes.</param>
    /// <param name="animation">Which local animation.</param>
    /// <returns>The count, or zero when the animation does not move the model.</returns>
    /// <remarks>
    /// Zero is ordinary rather than exceptional: an idle stands still, and <c>Studio_AnimMovement</c>
    /// returns false for it rather than treating it as travelling nowhere over some time.
    /// </remarks>
    public static int MovementCount(ReadOnlyMemory<byte> file, int animation) =>
        Description(file, animation) is { } at
            ? Math.Max(0, BinaryPrimitives.ReadInt32LittleEndian(
                file.Span[(at + AnimationMovementCountOffset)..]))
            : 0;

    /// <summary>Where an animation has carried the model by a point in its cycle.</summary>
    /// <param name="file">The <c>.mdl</c>'s bytes.</param>
    /// <param name="animation">Which local animation.</param>
    /// <param name="cycle">How far through, from zero to one.</param>
    /// <returns>The displacement and the yaw turned, or null when it does not move.</returns>
    /// <remarks>
    /// **Ported from <c>Studio_AnimPosition</c>** (<c>bone_setup.cpp:5573</c>):
    ///
    /// <code>
    /// float flFrame = flCycle * (panim->numframes - 1);
    /// for (int i = 0; i &lt; panim->nummovements; i++)
    /// {
    ///     mstudiomovement_t *pmove = panim->pMovement( i );
    ///     if (pmove->endframe >= flFrame)
    ///     {
    ///         float f = (flFrame - prevframe) / (pmove->endframe - prevframe);
    ///         float d = pmove->v0 * f + 0.5 * (pmove->v1 - pmove->v0) * f * f;
    ///         vecPos = vecPos + d * pmove->vector;
    ///         vecAngle.y = vecAngle.y * (1 - f) + pmove->angle * f;
    ///         return true;
    ///     }
    ///     else { prevframe = pmove->endframe; vecPos = pmove->position; vecAngle.y = pmove->angle; }
    /// }
    /// </code>
    ///
    /// **The else branch ASSIGNS rather than accumulates**, which is the part a reimplementation
    /// gets wrong: <c>position</c> is already cumulative from the start of the animation, so adding
    /// it would count every earlier block twice over.
    ///
    /// The integral is Valve's: distance under a linear ramp from <c>v0</c> to <c>v1</c>. The
    /// looping branch for a cycle outside zero-to-one is not implemented, because every caller here
    /// asks for one whole cycle.
    /// </remarks>
    public static (float X, float Y, float Z, float Yaw)? Position(
        ReadOnlyMemory<byte> file, int animation, float cycle)
    {
        int count = MovementCount(file, animation);

        if (count == 0 || Description(file, animation) is not { } at)
        {
            return null;
        }

        ReadOnlySpan<byte> bytes = file.Span;

        int frames = StudioAnimation.Frames(file, animation);
        int first = at + BinaryPrimitives.ReadInt32LittleEndian(
            bytes[(at + AnimationMovementIndexOffset)..]);

        float frame = cycle * (frames - 1);
        float previous = 0f;

        (float X, float Y, float Z) position = (0f, 0f, 0f);
        float yaw = 0f;

        for (int index = 0; index < count; index++)
        {
            int block = first + (index * MovementStride);

            if (block < 0 || block + MovementStride > bytes.Length)
            {
                return null;
            }

            float end = BinaryPrimitives.ReadInt32LittleEndian(bytes[(block + MovementEndFrameOffset)..]);

            if (end < frame)
            {
                previous = end;
                position = Vector(bytes, block + MovementPositionOffset);
                yaw = BinaryPrimitives.ReadSingleLittleEndian(bytes[(block + MovementAngleOffset)..]);
                continue;
            }

            // Guarded where Valve divides outright. Two blocks ending on the same frame make this
            // zero, and the quotient would reach the caller as a NaN distance — which is a
            // plausible-looking "no movement" rather than an error.
            float span = end - previous;

            if (span == 0f)
            {
                return (position.X, position.Y, position.Z, yaw);
            }

            float f = (frame - previous) / span;

            float v0 = BinaryPrimitives.ReadSingleLittleEndian(bytes[(block + MovementStartVelocityOffset)..]);
            float v1 = BinaryPrimitives.ReadSingleLittleEndian(bytes[(block + MovementEndVelocityOffset)..]);
            float angle = BinaryPrimitives.ReadSingleLittleEndian(bytes[(block + MovementAngleOffset)..]);

            float distance = (v0 * f) + (0.5f * (v1 - v0) * f * f);

            (float X, float Y, float Z) direction = Vector(bytes, block + MovementVectorOffset);

            return (
                position.X + (distance * direction.X),
                position.Y + (distance * direction.Y),
                position.Z + (distance * direction.Z),
                (yaw * (1f - f)) + (angle * f));
        }

        return null;
    }

    /// <summary>How fast a blend of animations was authored to travel, in units a second.</summary>
    /// <param name="file">The <c>.mdl</c>'s bytes.</param>
    /// <param name="blend">Each animation of the blend and its weight.</param>
    /// <returns>The ground speed, or zero when nothing in the blend moves.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="blend"/> is null.</exception>
    /// <remarks>
    /// **<c>Studio_SeqMovement</c> sums the VECTORS and takes the length of the sum**
    /// (<c>bone_setup.cpp:5738</c>) — not the weighted mean of the lengths. The difference is real
    /// for a blend of two directions: two animations travelling opposite ways at equal weight cancel
    /// to a standstill, which is what the engine draws, while averaging their speeds would report
    /// full pace.
    ///
    /// The cycle range is zero to one, because ground speed is a property of the whole animation.
    /// </remarks>
    public static float GroundSpeed(
        ReadOnlyMemory<byte> file, IReadOnlyList<(int Animation, float Weight)> blend)
    {
        ArgumentNullException.ThrowIfNull(blend);

        (float X, float Y, float Z) travelled = (0f, 0f, 0f);
        float rate = 0f;

        foreach ((int animation, float weight) in blend)
        {
            if (weight <= 0f)
            {
                continue;
            }

            // Both terms are weighted, and both come from the same animation — Studio_CPS skips an
            // animation of one frame, and Position returns null for one that does not travel, so a
            // blend of a run and a stationary idle contributes the run's distance and both of their
            // rates exactly as the engine does.
            rate += StudioAnimation.CyclesPerSecond(file, animation) * weight;

            if (Position(file, animation, 1f) is not { } moved)
            {
                continue;
            }

            // The start of the cycle is the origin by construction — at frame zero the first block
            // gives f = 0 and therefore no distance and no rotation — so the delta is the end
            // position and Studio_AnimMovement's yaw rotation by -startAngle is the identity.
            travelled = (
                travelled.X + (moved.X * weight),
                travelled.Y + (moved.Y * weight),
                travelled.Z + (moved.Z * weight));
        }

        float distance = MathF.Sqrt(
            (travelled.X * travelled.X) + (travelled.Y * travelled.Y) + (travelled.Z * travelled.Z));

        // distance / duration, where duration is 1/cps — so the division cancels.
        return distance * rate;
    }

    /// <summary>Where one animation's description starts, or null when there is no such animation.</summary>
    private static int? Description(ReadOnlyMemory<byte> file, int animation)
    {
        if (animation < 0 || animation >= StudioAnimation.Count(file))
        {
            return null;
        }

        ReadOnlySpan<byte> bytes = file.Span;

        if (bytes.Length < HeaderAnimationIndexOffset + 4)
        {
            return null;
        }

        int at = BinaryPrimitives.ReadInt32LittleEndian(bytes[HeaderAnimationIndexOffset..]) +
            (animation * AnimationStride);

        return at >= 0 && at + AnimationStride <= bytes.Length ? at : null;
    }

    /// <summary>Reads three consecutive floats.</summary>
    private static (float X, float Y, float Z) Vector(ReadOnlySpan<byte> bytes, int at) =>
    (
        BinaryPrimitives.ReadSingleLittleEndian(bytes[at..]),
        BinaryPrimitives.ReadSingleLittleEndian(bytes[(at + 4)..]),
        BinaryPrimitives.ReadSingleLittleEndian(bytes[(at + 8)..])
    );
}
