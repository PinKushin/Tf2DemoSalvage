using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// One authored pose a <c>STUDIO_PROC_QUATINTERP</c> bone blends towards.
/// </summary>
/// <param name="InverseTolerance">
/// <c>1 / radian angle of trigger influence</c>, Valve's own comment. A trigger with a wide
/// tolerance has a SMALL number here, because it is a reciprocal — reading it as an angle gives
/// weights that look plausible and fall off backwards.
/// </param>
/// <param name="TriggerX">The angle to match, as a quaternion. X.</param>
/// <param name="TriggerY">As above.</param>
/// <param name="TriggerZ">As above.</param>
/// <param name="TriggerW">As above.</param>
/// <param name="PositionX">The position this trigger wants. X.</param>
/// <param name="PositionY">As above.</param>
/// <param name="PositionZ">As above.</param>
/// <param name="QuatX">The rotation this trigger wants. X.</param>
/// <param name="QuatY">As above.</param>
/// <param name="QuatZ">As above.</param>
/// <param name="QuatW">As above.</param>
/// <remarks>
/// **Flattened to twelve floats rather than carrying vector types**, matching how the rest of this
/// project's studio readers hand values out: `Content` has no vector type of its own and taking one
/// from the animation layer would invert the dependency.
/// </remarks>
public readonly record struct StudioQuatInterpTrigger(
    float InverseTolerance,
    float TriggerX,
    float TriggerY,
    float TriggerZ,
    float TriggerW,
    float PositionX,
    float PositionY,
    float PositionZ,
    float QuatX,
    float QuatY,
    float QuatZ,
    float QuatW);

/// <summary>
/// A <c>STUDIO_PROC_QUATINTERP</c> rule — a bone driven by another bone's rotation.
/// </summary>
/// <param name="Control">The bone whose local rotation is read.</param>
/// <param name="Triggers">The authored poses, blended by how close the control is to each.</param>
/// <remarks>
/// **This is the helper-bone rule, and TF2 puts it on scout, heavy and demoman.** Measured with the
/// `procedural-bones` probe over all 14,109 models the game ships: `hlp_forearm_L` and
/// `hlp_forearm_R` on those three classes — each as two files, the ordinary model and its HWM twin —
/// plus `hlp_patella_L`/`_R`, knee helpers, on the Mann-vs-Machine bot engineer. Fourteen bones over
/// seven models, and every one **skinned to vertices**, so leaving it unimplemented is not
/// bookkeeping: it is a forearm that does not twist with the wrist.
///
/// <code>
/// struct mstudioquatinterpbone_t
/// {
///     int control;      // local transformation to check
///     int numtriggers;
///     int triggerindex;
/// };
/// </code>
///
/// `studio.h:171-183`. Twelve bytes, and `triggerindex` is relative to the structure's own address
/// exactly as `procindex` is relative to the bone's — <c>pTrigger(i)</c> is
/// <c>(((byte *)this) + triggerindex) + i</c>.
/// </remarks>
public readonly record struct StudioQuatInterp(int Control, IReadOnlyList<StudioQuatInterpTrigger> Triggers)
{
    /// <summary>Bytes of <c>mstudioquatinterpbone_t</c>.</summary>
    private const int RuleStride = 12;

    /// <summary>Bytes of <c>mstudioquatinterpinfo_t</c> — a float, then three quaternions' worth.</summary>
    /// <remarks>
    /// `inv_tolerance` (4) + `trigger` (16) + `pos` (12) + `quat` (16). No padding: every member is
    /// four-byte aligned already, so the sum and the `sizeof` agree here — which is NOT the general
    /// rule for these structures (`docs/memory/struct-padding-is-on-disk.md`) and is why it is
    /// stated rather than assumed.
    /// </remarks>
    private const int TriggerStride = 48;

    /// <summary>Most triggers a rule may declare, as a guard against a malformed header.</summary>
    /// <remarks>
    /// **Thirty-two, and it is Valve's own limit rather than a number chosen here.**
    /// `DoQuatInterpBone` weighs into `float weight[32]` (`bone_setup.cpp:4713`) with no bounds
    /// check at all, so a model declaring more would smash the engine's stack. Refusing above it
    /// matches what the engine can actually consume.
    /// </remarks>
    private const int MaximumTriggers = 32;

    /// <summary>Reads a bone's quaternion-interpolation rule.</summary>
    /// <param name="model">The ROOT model's bytes — <c>procindex</c> is an offset within them.</param>
    /// <param name="bone">Which bone.</param>
    /// <returns>The rule, or null when this bone does not carry one.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bone"/> is negative.</exception>
    public static StudioQuatInterp? Read(ReadOnlyMemory<byte> model, int bone)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bone);

        ReadOnlySpan<byte> bytes = model.Span;

        if (bytes.Length < StudioLayout.HeaderBoneIndexOffset + sizeof(int))
        {
            return null;
        }

        int count = BinaryPrimitives.ReadInt32LittleEndian(
            bytes[StudioLayout.HeaderBoneCountOffset..]);

        int table = BinaryPrimitives.ReadInt32LittleEndian(
            bytes[StudioLayout.HeaderBoneIndexOffset..]);

        if (bone >= count ||
            table < 0 ||
            (long)table + ((long)count * StudioLayout.BoneStride) > bytes.Length)
        {
            return null;
        }

        int start = table + (bone * StudioLayout.BoneStride);

        int type = BinaryPrimitives.ReadInt32LittleEndian(
            bytes[(start + StudioLayout.BoneProcedureTypeOffset)..]);

        int index = BinaryPrimitives.ReadInt32LittleEndian(
            bytes[(start + StudioLayout.BoneProcedureIndexOffset)..]);

        // **`==` here, where the jiggle reader uses `&`, and the difference is Valve's own.**
        // `CalcProceduralBone` dispatches on `switch( pbones[iBone].proctype )` — an exact match
        // (`bone_setup.cpp:4942`) — while the jiggle path is reached by a later `&` test in
        // `BuildTransformations`. Reproducing each as written rather than making them agree.
        if (index == 0 || type != StudioProcedureType.QuaternionInterpolate)
        {
            return null;
        }

        long rule = (long)start + index;

        if (rule < 0 || rule + RuleStride > bytes.Length)
        {
            return null;
        }

        int control = BinaryPrimitives.ReadInt32LittleEndian(bytes[(int)rule..]);
        int triggers = BinaryPrimitives.ReadInt32LittleEndian(bytes[((int)rule + 4)..]);
        int at = BinaryPrimitives.ReadInt32LittleEndian(bytes[((int)rule + 8)..]);

        if (control < 0 || control >= count || triggers <= 0 || triggers > MaximumTriggers)
        {
            return null;
        }

        long first = rule + at;

        if (at == 0 || first < 0 || first + ((long)triggers * TriggerStride) > bytes.Length)
        {
            return null;
        }

        StudioQuatInterpTrigger[] read = new StudioQuatInterpTrigger[triggers];

        for (int trigger = 0; trigger < triggers; trigger++)
        {
            ReadOnlySpan<byte> one = bytes[(int)(first + (trigger * TriggerStride))..];

            read[trigger] = new StudioQuatInterpTrigger(
                BinaryPrimitives.ReadSingleLittleEndian(one),
                BinaryPrimitives.ReadSingleLittleEndian(one[4..]),
                BinaryPrimitives.ReadSingleLittleEndian(one[8..]),
                BinaryPrimitives.ReadSingleLittleEndian(one[12..]),
                BinaryPrimitives.ReadSingleLittleEndian(one[16..]),
                BinaryPrimitives.ReadSingleLittleEndian(one[20..]),
                BinaryPrimitives.ReadSingleLittleEndian(one[24..]),
                BinaryPrimitives.ReadSingleLittleEndian(one[28..]),
                BinaryPrimitives.ReadSingleLittleEndian(one[32..]),
                BinaryPrimitives.ReadSingleLittleEndian(one[36..]),
                BinaryPrimitives.ReadSingleLittleEndian(one[40..]),
                BinaryPrimitives.ReadSingleLittleEndian(one[44..]));
        }

        return new StudioQuatInterp(control, read);
    }
}
