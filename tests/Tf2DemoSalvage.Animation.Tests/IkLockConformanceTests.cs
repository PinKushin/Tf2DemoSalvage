using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// A sequence's IK locks — <c>AddSequenceLocks</c> and <c>SolveSequenceLocks</c>.
/// </summary>
/// <remarks>
/// **The claim under test is a behaviour, not a plumbing detail: the effector stays put while the
/// skeleton moves under it.** `AccumulatePose` records where a locked chain ends before a sequence
/// is applied (`bone_setup.cpp:2425`) and restores it afterwards (`:2451`), which is what keeps a
/// foot planted while an aim matrix turns the body over it.
///
/// **Measured on TF2's own content** (B310, B311): every one of the 2,666 locks it ships carries
/// `flPosWeight` 1 and `flLocalQWeight` 0, on chains 2 and 3 — both feet, pinned fully in position,
/// rotation left to the solve. So the weight-1 case below is not a convenient extreme; it is the
/// only case that occurs.
///
/// **A three-link chain along one axis, because the arithmetic then has an answer a reader can
/// check.** Bones at 0, 10 and 20 along +X give a chain of reach 20 whose solutions are easy to
/// state, and moving the root is a manipulation whose correct outcome is "the tip did not move".
/// </remarks>
public sealed class IkLockConformanceTests
{
    private const double Tolerance = 1e-3;

    /// <remarks>
    /// **The whole point, in one assertion.** The root is moved five units along +Y after the
    /// capture; without the lock the tip goes with it, and with the lock it stays where it was.
    /// </remarks>
    [Test]
    public void Solve_AfterTheRootMoves_PutsTheEffectorBack()
    {
        IkLocks locks = new(Parents, BoneCount);

        StudioBonePose[] pose = Straight();

        locks.Capture([Held(weight: 1f)], Chains, pose);

        // The sequence: the whole leg is carried five units sideways.
        pose[0] = pose[0] with { Position = (0f, 5f, 0f) };

        locks.Solve([Held(weight: 1f)], Chains, pose);

        (float x, float y, float z) = Tip(pose);

        y.ShouldBe(0f, Tolerance, "the effector was pinned where it started, not carried along");
        x.ShouldBe(20f, Tolerance);
        z.ShouldBe(0f, Tolerance);
    }

    /// <remarks>
    /// **The control, and it is what says the LOCK did it rather than the solve.** The same
    /// movement with no lock applied must carry the tip along — so a `Solve` that pinned
    /// unconditionally, or a chain builder that ignored the root, fails here while passing above.
    /// </remarks>
    [Test]
    public void Solve_WithNoLockAtAll_LetsTheEffectorFollowTheRoot()
    {
        IkLocks locks = new(Parents, BoneCount);

        StudioBonePose[] pose = Straight();

        locks.Capture([], Chains, pose);

        pose[0] = pose[0] with { Position = (0f, 5f, 0f) };

        locks.Solve([], Chains, pose);

        Tip(pose).Y.ShouldBe(5f, Tolerance, "nothing pinned it, so it went with the root");
    }

    /// <remarks>
    /// **<c>flPosWeight</c> is a BLEND, not a switch** — `p3 = p1 * (1 - w) + saved * w`. At a half
    /// the effector lands halfway between where the sequence put it and where it was, and a reader
    /// that treated any non-zero weight as a full pin would pass the test above and fail here.
    /// </remarks>
    [Test]
    public void Solve_AtHalfWeight_LandsBetweenTheTwoPositions()
    {
        IkLocks locks = new(Parents, BoneCount);

        StudioBonePose[] pose = Straight();

        locks.Capture([Held(weight: 0.5f)], Chains, pose);

        pose[0] = pose[0] with { Position = (0f, 4f, 0f) };

        locks.Solve([Held(weight: 0.5f)], Chains, pose);

        Tip(pose).Y.ShouldBe(2f, Tolerance, "halfway between 4 and 0");
    }

    /// <remarks>
    /// **A weight of zero is the identity, and the engine still runs the whole bracket for it.**
    /// `p3 = p1 * 1 + saved * 0` is where the sequence left it, so the solve is a no-op — which is
    /// worth pinning because it is the difference between "the lock did nothing" and "the lock was
    /// skipped", and only one of those is the engine.
    /// </remarks>
    [Test]
    public void Solve_AtZeroWeight_LeavesTheSequenceWhereItWas()
    {
        IkLocks locks = new(Parents, BoneCount);

        StudioBonePose[] pose = Straight();

        locks.Capture([Held(weight: 0f)], Chains, pose);

        pose[0] = pose[0] with { Position = (0f, 3f, 0f) };

        locks.Solve([Held(weight: 0f)], Chains, pose);

        Tip(pose).Y.ShouldBe(3f, Tolerance, "no pull toward the remembered position at all");
    }

    /// <remarks>
    /// **A lock naming a chain the model does not have is skipped, not clamped.** A demo can carry
    /// a sequence from a model that has since been replaced, and indexing off the end of the chain
    /// list would reach `pIKChain` with a number the engine's own Assert exists to catch — and
    /// that Assert is compiled out of a release build.
    /// </remarks>
    [Test]
    public void Solve_WithALockNamingNoSuchChain_ChangesNothing()
    {
        IkLocks locks = new(Parents, BoneCount);

        StudioBonePose[] pose = Straight();

        IReadOnlyList<StudioIkLock> absent = [new StudioIkLock(9, 1f, 0f, 0)];

        locks.Capture(absent, Chains, pose);

        pose[0] = pose[0] with { Position = (0f, 5f, 0f) };

        locks.Solve(absent, Chains, pose);

        Tip(pose).Y.ShouldBe(5f, Tolerance, "the lock named nothing, so nothing was pinned");
    }

    /// <remarks>
    /// **A failed solve still slams the captured rotation onto the foot** (B622). `SolveLock` calls
    /// `Studio_SolveIK` and ignores its return (`bone_setup.cpp:4431-4438`), then reads the foot's
    /// position — unsolved — and puts the captured rotation on it (`:4441-4442`), and rebuilds all
    /// three bones (`:4446`, `:4449`, `:4450`). `SolveDependencies` is the control: it DOES gate on
    /// the return (`:4211`), so the omission in `SolveLock` is not house style.
    ///
    /// **The failure is the engine's own:** a dead-straight leg has `l3` = 20 against
    /// `(l1 + l2) * 0.9998` = 19.996, so the no-knee-direction overload returns false at `:2735`
    /// before touching a matrix — and the seven-argument overload, on its own failure, also writes
    /// nothing (`:2859-2867`). So the engine's answer is: the foot keeps the SEQUENCE's position
    /// (carried 5 along +Y with the root — not pinned) and takes the CAPTURED rotation (90° about Z,
    /// slerped toward the sequence's by `flLocalQWeight` 0, so kept); thigh and knee are untouched.
    /// The position assertion catches a fix that also places the foot; the rotation one is red on a
    /// `continue` that skips the slam.
    /// </remarks>
    [Test]
    public void Solve_WithALegTooStraightToSolve_SlamsTheCapturedRotationWhereTheSequenceLeftTheFoot()
    {
        IkLocks locks = new(Parents, BoneCount);

        const float Half = 0.70710678f;

        StudioBonePose[] pose = DeadStraight();
        pose[3] = pose[3] with { Rotation = (0f, 0f, Half, Half) };

        locks.Capture([Held(weight: 1f)], Chains, pose);

        // The sequence: the leg carried five units sideways, the foot turned back to identity.
        pose[0] = pose[0] with { Position = (0f, 5f, 0f) };
        pose[3] = pose[3] with { Rotation = (0f, 0f, 0f, 1f) };

        locks.Solve([Held(weight: 1f)], Chains, pose);

        BoneChain chain = new(Parents, BoneCount);

        chain.Build(3, pose);

        // 90° about Z: X → +Y, Y → −X. Rows of a 3×4, translation in the last column.
        float[] foot = [0f, -1f, 0f, 20f, 1f, 0f, 0f, 5f, 0f, 0f, 1f, 0f];
        float[] thigh = [1f, 0f, 0f, 0f, 0f, 1f, 0f, 5f, 0f, 0f, 1f, 0f];
        float[] knee = [1f, 0f, 0f, 10f, 0f, 1f, 0f, 5f, 0f, 0f, 1f, 0f];

        for (int at = 0; at < 12; at++)
        {
            chain.Matrix(3)[at].ShouldBe(foot[at], Tolerance, $"foot matrix element {at}");
            chain.Matrix(1)[at].ShouldBe(thigh[at], Tolerance, $"thigh matrix element {at}");
            chain.Matrix(2)[at].ShouldBe(knee[at], Tolerance, $"knee matrix element {at}");
        }
    }

    /// <remarks>
    /// **The slerp runs on a failed solve too, and toward the SEQUENCE's rotation** —
    /// `QuaternionSlerp( q[bone], q2, plock->flLocalQWeight, q[bone] )` with `q2 = q[ bone ]` taken
    /// before the rebuild (`bone_setup.cpp:4445-4447`). At 0.5 between the captured 90° about Z and
    /// the sequence's identity the foot lands at 45° about Z: (0, 0, sin 22.5°, cos 22.5°). A slerp
    /// toward the captured rotation would leave 90°.
    /// </remarks>
    [Test]
    public void Solve_WithALegTooStraightAtHalfRotationWeight_LandsHalfwayToTheSequencesRotation()
    {
        IkLocks locks = new(Parents, BoneCount);

        const float Half = 0.70710678f;

        StudioBonePose[] pose = DeadStraight();
        pose[3] = pose[3] with { Rotation = (0f, 0f, Half, Half) };

        IReadOnlyList<StudioIkLock> halfTurn = [new StudioIkLock(0, 1f, 0.5f, 0)];

        locks.Capture(halfTurn, Chains, pose);

        pose[3] = pose[3] with { Rotation = (0f, 0f, 0f, 1f) };

        locks.Solve(halfTurn, Chains, pose);

        (float x, float y, float z, float w) = pose[3].Rotation;
        float sign = w < 0f ? -1f : 1f;

        (sign * x).ShouldBe(0f, Tolerance);
        (sign * y).ShouldBe(0f, Tolerance);
        (sign * z).ShouldBe(0.38268343f, Tolerance, "sin 22.5°: halfway between 90° and 0°");
        (sign * w).ShouldBe(0.92387953f, Tolerance, "cos 22.5°");
    }

    /// <remarks>
    /// **`Applied` counts solves, not slams.** The dead-straight leg is refused at `:2735`, so it
    /// is slammed but not counted; the bent leg of <see cref="Straight"/> solves, and is. The
    /// control is what makes the zero mean "refused" rather than "never reached".
    /// </remarks>
    [Test]
    public void Applied_ARefusedLegAndASolvableOne_CountsOnlyTheSolve()
    {
        IkLocks refused = new(Parents, BoneCount);
        StudioBonePose[] straight = DeadStraight();
        refused.Capture([Held(weight: 1f)], Chains, straight);
        refused.Solve([Held(weight: 1f)], Chains, straight);

        IkLocks solvable = new(Parents, BoneCount);
        StudioBonePose[] bent = Straight();
        solvable.Capture([Held(weight: 1f)], Chains, bent);
        solvable.Solve([Held(weight: 1f)], Chains, bent);

        (refused.Applied, solvable.Applied).ShouldBe((0, 1));
    }

    /// <summary>The chain dead straight along +X: bones at 0, 10 and 20, all unrotated.</summary>
    private static StudioBonePose[] DeadStraight() =>
    [
        new StudioBonePose(0, (0f, 0f, 0f), (0f, 0f, 0f, 1f)),
        new StudioBonePose(1, (0f, 0f, 0f), (0f, 0f, 0f, 1f)),
        new StudioBonePose(2, (10f, 0f, 0f), (0f, 0f, 0f, 1f)),
        new StudioBonePose(3, (10f, 0f, 0f), (0f, 0f, 0f, 1f)),
    ];

    /// <summary>Four bones: a root and a three-link chain running along +X.</summary>
    private static int[] Parents => [-1, 0, 1, 2];

    private const int BoneCount = 4;

    /// <summary>One chain over bones 1, 2 and 3, with no stated knee direction.</summary>
    private static IReadOnlyList<StudioIkChain> Chains =>
    [
        new StudioIkChain(
            "leg",
            0,
            [
                new StudioIkLink(1, (0f, 0f, 0f)),
                new StudioIkLink(2, (0f, 0f, 0f)),
                new StudioIkLink(3, (0f, 0f, 0f)),
            ]),
    ];

    private static StudioIkLock Held(float weight) => new(0, weight, 0f, 0);

    /// <summary>
    /// The chain laid out along +X with a slight bend, so the solver has a plane to work in.
    /// </summary>
    /// <remarks>
    /// **Not perfectly straight, deliberately.** `Studio_SolveIK` refuses a chain already at full
    /// reach — `StraightEnough` — and a dead-straight leg is exactly that case, so a fixture built
    /// that way would exercise the refusal rather than the solve.
    ///
    /// **And the bend has to leave SLACK, which the first version of this did not.** With links of
    /// ±2 the chain reaches 20.40 while the tip sits 20.00 away, so moving the root five units puts
    /// the pinned target 20.62 away — beyond reach. The solver then placed the foot as close as it
    /// could and the test failed by 0.054, which looked like a defect and was a fixture whose leg
    /// was too short. ±5 gives a reach of 22.36 against the same 20.62, so the target is inside it.
    /// </remarks>
    private static StudioBonePose[] Straight() =>
    [
        new StudioBonePose(0, (0f, 0f, 0f), (0f, 0f, 0f, 1f)),
        new StudioBonePose(1, (0f, 0f, 0f), (0f, 0f, 0f, 1f)),
        new StudioBonePose(2, (10f, 0f, 5f), (0f, 0f, 0f, 1f)),
        new StudioBonePose(3, (10f, 0f, -5f), (0f, 0f, 0f, 1f)),
    ];

    /// <summary>Where the chain's end effector sits, in model space.</summary>
    private static (float X, float Y, float Z) Tip(StudioBonePose[] pose)
    {
        BoneChain chain = new(Parents, BoneCount);

        chain.Build(3, pose);

        ReadOnlySpan<float> matrix = chain.Matrix(3);

        return (matrix[3], matrix[7], matrix[11]);
    }
}
