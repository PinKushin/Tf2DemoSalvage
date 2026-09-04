using System;
using System.Buffers.Binary;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// The IK locks a sequence declares — <c>mstudioseqdesc_t::pIKLock</c>.
/// </summary>
/// <remarks>
/// **A lock is what keeps a hand still while the body moves under it.** `AccumulatePose` records
/// where each locked chain's end effector sits BEFORE the sequence is applied
/// (`bone_setup.cpp:2425`) and puts it back afterwards (`:2451`).
///
/// **Read against real models rather than a synthetic fixture, deliberately.** The question these
/// answer is not "does the arithmetic work" — there is none — but "does this reader find the table
/// a shipped `.mdl` actually contains", and the offsets are relative to the sequence rather than to
/// the file, which is the mistake that still lands on data and returns plausible numbers.
/// </remarks>
public sealed class StudioIkLockTests
{
    /// <remarks>
    /// **Measured before this was written** (B310): 814 of the 1,333 lock-declaring sequences are
    /// under `models/player/`, and the examples are `PRIMARY_aimmatrix_idle`,
    /// `PRIMARY_aimmatrix_run` and `AttackStand_PRIMARY` — what every match draws every frame.
    /// A player animation model is therefore the right subject, and finding zero here would mean
    /// the reader, not the content.
    /// </remarks>
    [Test]
    public void Read_ForAPlayerAnimationModel_FindsTheLocksItDeclares()
    {
        if (Read("models/player/engineer_animations.mdl") is not { } model)
        {
            Assert.Ignore("the game is not installed");
            return;
        }

        int sequences = 0;
        int locked = 0;
        int chains = 0;

        for (int sequence = 0; sequence < StudioSequences.Read(model).Count; sequence++)
        {
            sequences++;

            IReadOnlyList<StudioIkLock> locks = StudioIkLocks.Read(model, sequence);

            if (locks.Count == 0)
            {
                continue;
            }

            locked++;
            chains += locks.Count;

            foreach (StudioIkLock entry in locks)
            {
                // **A chain INDEX, so it must address a chain this model has.** An offset read
                // from the wrong place gives a large or negative number here, which is the
                // failure this whole test exists to catch — and it would otherwise reach
                // `pIKChain( plock->chain )` and index off the end.
                entry.Chain.ShouldBeInRange(
                    0,
                    StudioIkChains.Read(model).Count - 1,
                    "a lock names one of the model's own chains");

                entry.PositionWeight.ShouldBeInRange(0f, 1f, "flPosWeight is a blend fraction");
                entry.LocalRotationWeight.ShouldBeInRange(0f, 1f, "flLocalQWeight is one too");
            }
        }

        sequences.ShouldBeGreaterThan(
            0, "the control: this model declares sequences at all, so a zero below means something");

        locked.ShouldBeGreaterThan(
            0,
            "a player animation model declares IK locks — 814 lock-declaring sequences live under " +
            "models/player/, so zero here is a fact about the reader");

        chains.ShouldBeGreaterThanOrEqualTo(locked, "a locking sequence locks at least one chain");
    }

    /// <remarks>
    /// **The control, and it is the assertion that says the reader is not simply returning
    /// something for everything.** A sequence declaring no locks must come back empty — so a reader
    /// that ignored `numiklocks` and walked the table regardless would fail here while passing the
    /// test above. Most of a player model's sequences declare none.
    /// </remarks>
    [Test]
    public void Read_ForASequenceThatDeclaresNoLocks_IsEmpty()
    {
        if (Read("models/player/engineer_animations.mdl") is not { } model)
        {
            Assert.Ignore("the game is not installed");
            return;
        }

        int unlocked = 0;

        for (int sequence = 0; sequence < StudioSequences.Read(model).Count; sequence++)
        {
            if (StudioIkLocks.Read(model, sequence).Count == 0)
            {
                unlocked++;
            }
        }

        unlocked.ShouldBeGreaterThan(
            0, "most sequences declare no locks, and those must read as none rather than as some");
    }

    [Test]
    public void Read_WithASequenceNumberTheModelDoesNotHave_IsEmpty()
    {
        // Reached by a demo naming a sequence a replaced model no longer has, which is an ordinary
        // consequence of TF2's own updates rather than a corrupt file.
        if (Read("models/player/engineer_animations.mdl") is not { } model)
        {
            Assert.Ignore("the game is not installed");
            return;
        }

        StudioIkLocks.Read(model, -1).ShouldBeEmpty();
        StudioIkLocks.Read(model, 100_000).ShouldBeEmpty();
    }

    [Test]
    public void Read_FromAFileTooShortToDescribeItself_IsEmpty()
    {
        StudioIkLocks.Read(new byte[8], 0).ShouldBeEmpty();
    }

    /// <remarks>
    /// **The corpus tests above cannot see two whole classes of defect, and a sabotage proved it
    /// rather than a reading.** They assert `ShouldBeInRange`, so:
    ///
    /// - **A wrong STRIDE was invisible.** Halving it to 16 reads lock 1 out of lock 0's unused
    ///   run — four reserved integers — and those decode to a chain of 0 and weights of 0, which
    ///   satisfy both ranges. Only the SDK-derived struct test caught it.
    /// - **Swapping the two WEIGHTS was invisible by construction.** Both are checked with the
    ///   identical predicate over the identical domain, so exchanging them cannot be detected by
    ///   any input at all.
    ///
    /// **A hand-built table has ground truth, which is the whole argument for synthetic fixtures**
    /// (D38): the test put the values there, so it can predict them exactly. TWO locks with
    /// DIFFERENT values is what makes the stride visible, and two DISTINCT weights is what makes
    /// the swap visible — neither would be caught by one lock or by equal weights.
    /// </remarks>
    [Test]
    public void Read_FromAHandBuiltTable_ReturnsExactlyWhatWasWritten()
    {
        byte[] model = new byte[4096];

        // One sequence, at a start of the reader's choosing.
        Write(model, 188, 1);
        Write(model, 192, SequenceAt);

        // `numiklocks` and `iklockindex`, the latter RELATIVE TO THE SEQUENCE.
        Write(model, SequenceAt + 164, 2);
        Write(model, SequenceAt + 168, LockTable - SequenceAt);

        Write(model, LockTable, 1);
        WriteFloat(model, LockTable + 4, 0.25f);
        WriteFloat(model, LockTable + 8, 0.75f);
        Write(model, LockTable + 12, 7);

        // The second lock is what a wrong stride misses: at 16 it would read the first lock's
        // reserved run instead, which is zeros.
        Write(model, LockTable + 32, 2);
        WriteFloat(model, LockTable + 36, 0.5f);
        WriteFloat(model, LockTable + 40, 0.125f);
        Write(model, LockTable + 44, 9);

        IReadOnlyList<StudioIkLock> locks = StudioIkLocks.Read(model, 0);

        locks.Count.ShouldBe(2);

        locks[0].Chain.ShouldBe(1);
        locks[0].PositionWeight.ShouldBe(0.25f, "flPosWeight, not flLocalQWeight");
        locks[0].LocalRotationWeight.ShouldBe(0.75f, "flLocalQWeight, and the two are not equal");
        locks[0].Flags.ShouldBe(7);

        locks[1].Chain.ShouldBe(2, "a 32-byte stride, not the 16 the four fields alone would give");
        locks[1].PositionWeight.ShouldBe(0.5f);
        locks[1].LocalRotationWeight.ShouldBe(0.125f);
        locks[1].Flags.ShouldBe(9);
    }

    /// <remarks>
    /// **The count BOUNDARY, and a sabotage is what said it was missing.** Changing the guard from
    /// `locks &lt;= 0` to `locks &lt;= 1` reddened nothing: the hand-built table above always writes
    /// two, and the corpus tests assert only `locked &gt; 0` and `chains &gt;= locked` — a sequence
    /// declaring exactly one lock silently reading as empty lowers both counters equally, so the
    /// invariant survives.
    ///
    /// **One lock is not a degenerate case.** `AddSequenceLocks` loops `numiklocks` times whatever
    /// the count, and a sequence pinning a single hand is the obvious authoring.
    /// </remarks>
    [Test]
    public void Read_FromATableDeclaringOneLock_ReturnsThatOne()
    {
        byte[] model = new byte[4096];

        Write(model, 188, 1);
        Write(model, 192, SequenceAt);
        Write(model, SequenceAt + 164, 1);
        Write(model, SequenceAt + 168, LockTable - SequenceAt);

        Write(model, LockTable, 3);
        WriteFloat(model, LockTable + 4, 1f);
        WriteFloat(model, LockTable + 8, 0.4f);
        Write(model, LockTable + 12, 0);

        IReadOnlyList<StudioIkLock> locks = StudioIkLocks.Read(model, 0);

        locks.Count.ShouldBe(1, "one is a real count, not a boundary to round away");
        locks[0].Chain.ShouldBe(3);
        locks[0].PositionWeight.ShouldBe(1f);
        locks[0].LocalRotationWeight.ShouldBe(0.4f);
    }

    /// <summary>Where the hand-built fixture puts its one sequence.</summary>
    private const int SequenceAt = 256;

    /// <summary>Where it puts the lock table, past the 212-byte sequence.</summary>
    private const int LockTable = 768;

    private static void Write(byte[] into, int at, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(into.AsSpan(at), value);

    private static void WriteFloat(byte[] into, int at, float value) =>
        BinaryPrimitives.WriteSingleLittleEndian(into.AsSpan(at), value);

    /// <summary>The model's bytes, or null when the game is not installed.</summary>
    /// <remarks>
    /// **Through <see cref="GameInstall"/> rather than a path spelled here**, which three other
    /// files in this assembly do spell out. One more copy is one more place that goes wrong when
    /// the install moves, and this needs only the archive.
    /// </remarks>
    private static ReadOnlyMemory<byte>? Read(string path) =>
        GameInstall.Vpk("tf2_misc") is { } archive &&
        VpkArchive.Open(archive).ReadFile(path) is { } bytes
            ? bytes
            : null;
}
