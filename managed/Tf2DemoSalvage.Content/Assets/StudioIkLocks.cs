using System;
using System.Buffers.Binary;
using System.Collections.Generic;

using static Tf2DemoSalvage.Content.Assets.StudioLayout;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>One IK chain a sequence pins in place while it plays.</summary>
/// <param name="Chain">Which of the model's IK chains, by index.</param>
/// <param name="PositionWeight">How much of the remembered POSITION to restore, 0 to 1.</param>
/// <param name="LocalRotationWeight">How much of the remembered ROTATION to restore, 0 to 1.</param>
/// <param name="Flags">Unused by the engine; kept so a reader can report what a file declares.</param>
/// <remarks>
/// **The two weights are separate and mean different things.** `SolveLock` blends the end
/// effector's POSITION toward the remembered one by <c>flPosWeight</c> and then slerps the end
/// bone's local ROTATION back by <c>flLocalQWeight</c> — a hand can be pinned in place while still
/// being allowed to turn, or held at an angle while free to move. Collapsing them to one number
/// would look right whenever an animator happened to author them equal.
/// </remarks>
public readonly record struct StudioIkLock(
    int Chain,
    float PositionWeight,
    float LocalRotationWeight,
    int Flags);

/// <summary>
/// The IK chains each sequence locks — <c>mstudioseqdesc_t::pIKLock</c>.
/// </summary>
/// <remarks>
/// **A lock is what keeps a hand still while the body moves under it.** `AccumulatePose` brackets
/// its whole body with these (`bone_setup.cpp:2425` and `:2451`): `AddSequenceLocks` records where
/// each locked chain's end effector is BEFORE the sequence is applied, and `SolveSequenceLocks`
/// puts it back afterwards.
///
/// **Both run in the model's own space, not the world's.** The engine builds a throwaway context
/// with an identity transform for exactly this — `seq_ik.Init( m_pStudioHdr, vec3_angle,
/// vec3_origin, 0.0, 0, m_boneMask )`, under a comment reading *"local space relative so absolute
/// position doesn't mater"*. So a lock needs no entity placement and no world matrices.
///
/// **Measured before it was read** (B310): 1,333 of 26,387 sequences across all 14,109 models in
/// `tf2_misc_dir.vpk` declare locks, 2,666 chains in total — and **814 of those sequences are under
/// `models/player/`**. They are `PRIMARY_aimmatrix_idle`, `PRIMARY_aimmatrix_run` and
/// `AttackStand_PRIMARY`, which is to say the sequences every match draws every frame rather than
/// Halloween boss content.
///
/// <code>
///   dotnet run --project tools/Tf2DemoSalvage.Probe -c Release -- sequence-flags
/// </code>
/// </remarks>
public static class StudioIkLocks
{
    /// <summary>A sequence is untrusted input; TF2 declares two locks on the sequences that have any.</summary>
    private const int MaximumLocks = 64;

    /// <summary>Reads one sequence's IK locks.</summary>
    /// <param name="file">The <c>.mdl</c>'s bytes.</param>
    /// <param name="sequence">Which local sequence.</param>
    /// <returns>Its locks, empty when it declares none or the table is out of range.</returns>
    /// <remarks>
    /// **<c>iklockindex</c> is relative to the SEQUENCE, not to the file**, which is the convention
    /// that bites hardest in this format because a file-relative read still lands on data and
    /// returns plausible numbers.
    /// </remarks>
    public static IReadOnlyList<StudioIkLock> Read(ReadOnlyMemory<byte> file, int sequence)
    {
        ReadOnlySpan<byte> bytes = file.Span;

        if (bytes.Length < HeaderSequenceIndexOffset + 4 || sequence < 0)
        {
            return [];
        }

        int count = BinaryPrimitives.ReadInt32LittleEndian(bytes[HeaderSequenceCountOffset..]);
        int at = BinaryPrimitives.ReadInt32LittleEndian(bytes[HeaderSequenceIndexOffset..]);

        if (sequence >= count || at < 0 ||
            (long)at + ((long)(sequence + 1) * SequenceStride) > bytes.Length)
        {
            return [];
        }

        int start = at + (sequence * SequenceStride);
        ReadOnlySpan<byte> entry = bytes.Slice(start, SequenceStride);

        int locks = BinaryPrimitives.ReadInt32LittleEndian(entry[SequenceIkLockCountOffset..]);
        int table = BinaryPrimitives.ReadInt32LittleEndian(entry[SequenceIkLockIndexOffset..]);

        if (locks <= 0 || locks > MaximumLocks)
        {
            return [];
        }

        long from = (long)start + table;

        if (table == 0 || from < 0 || from + ((long)locks * IkLockStride) > bytes.Length)
        {
            return [];
        }

        List<StudioIkLock> read = new(locks);

        for (int index = 0; index < locks; index++)
        {
            ReadOnlySpan<byte> row = bytes.Slice((int)from + (index * IkLockStride), IkLockStride);

            read.Add(new StudioIkLock(
                BinaryPrimitives.ReadInt32LittleEndian(row),
                BinaryPrimitives.ReadSingleLittleEndian(row[IkLockPositionWeightOffset..]),
                BinaryPrimitives.ReadSingleLittleEndian(row[IkLockRotationWeightOffset..]),
                BinaryPrimitives.ReadInt32LittleEndian(row[IkLockFlagsOffset..])));
        }

        return read;
    }
}
