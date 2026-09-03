using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// An IK rule pulls a chain's end to where the animation says it should be — <c>SolveDependencies</c>.
/// </summary>
/// <remarks>
/// **<c>bone_setup.cpp:4046</c>, reduced to the one rule type TF2 uses.** Measured: of the scout's
/// 2035 IK rules, 1829 are <c>IK_RELEASE</c> — which solves nothing — 206 are <c>IK_SELF</c>, and
/// there are zero of the other four types (B296). So the whole of TF2's IK is a hand held to another
/// bone on the same model.
///
/// **The claim these tests make is the one that matters and the one a unit test can make**: given a
/// chain, a rule and an error, does the chain's END BONE finish nearer the target than it started?
/// Everything else — the exact knee, the exact roll — is the solver's, and has its own tests.
/// </remarks>
public sealed class IkContextConformanceTests
{
    /// <summary>How close two floats must be to count as equal here.</summary>
    private const double Tolerance = 1e-3;

    [Test]
    public void Solve_ASelfRuleAtFullWeight_MovesTheChainsEndTowardTheTarget()
    {
        // The chain runs along X: thigh at the origin, knee at 10, foot at 20. The rule's target
        // bone is the root, and the error asks for a point off to the side — so a correct solve
        // bends the chain and brings the foot nearer that point than the straight pose left it.
        Harness harness = new();

        Vector3 wanted = new(12f, 8f, 0f);

        float before = (harness.Foot() - wanted).Length();

        harness.Solve(wanted, weight: 1f);

        float after = (harness.Foot() - wanted).Length();

        after.ShouldBeLessThan(before, "a rule at full weight reaches for its target");

        after.ShouldBeLessThan(1f, "and a target inside the chain's reach is reached");
    }

    /// <remarks>
    /// **The control, and without it "solved" and "moved the bones for any reason" are the same
    /// observation.** A rule of a type TF2 has but never solves — `IK_RELEASE` — must leave the
    /// skeleton exactly as the animation left it.
    /// </remarks>
    [Test]
    public void Solve_ARuleThatIsNotSelf_LeavesTheSkeletonAlone()
    {
        Harness harness = new();

        Vector3 was = harness.Foot();

        harness.Solve(new Vector3(12f, 8f, 0f), weight: 1f, type: StudioIkRuleType.Release);

        harness.Foot().X.ShouldBe(was.X, Tolerance);
        harness.Foot().Y.ShouldBe(was.Y, Tolerance);
        harness.Solved.ShouldBe(0, "a release solves nothing");
    }

    [Test]
    public void Solve_AtZeroWeight_LeavesTheSkeletonAlone()
    {
        // `if (pChainResult->flWeight > 0.0)` — a chain nothing asked for is not solved at all,
        // which is what stops every chain being rebuilt on every frame of every animation.
        Harness harness = new();

        Vector3 was = harness.Foot();

        harness.Solve(new Vector3(12f, 8f, 0f), weight: 0f);

        harness.Foot().X.ShouldBe(was.X, Tolerance);
        harness.Solved.ShouldBe(0);
    }

    [Test]
    public void Solve_AtHalfWeight_LandsBetweenTheAnimationAndTheTarget()
    {
        // `pChainResult->pos = pChainResult->pos * (1.0 - flWeight) + p2 * flWeight` — the target is
        // blended with where the animation left the chain, so half weight asks for the midpoint.
        // **This is the case that separates a weight that is honoured from one that is ignored**,
        // which full weight cannot: at one, blended and unblended are the same point.
        Harness full = new();
        Harness half = new();

        Vector3 wanted = new(12f, 8f, 0f);

        full.Solve(wanted, weight: 1f);
        half.Solve(wanted, weight: 0.5f);

        float reachedFully = (full.Foot() - wanted).Length();
        float reachedHalf = (half.Foot() - wanted).Length();

        reachedHalf.ShouldBeGreaterThan(
            reachedFully, "half weight stops half way to the target");

        half.Foot().Y.ShouldBeGreaterThan(0f, "but it did move off the animation's pose");
    }

    [Test]
    public void Solve_AfterSolving_RewritesTheLocalPoseNotJustTheWorldMatrices()
    {
        // **The half that is easy to leave out.** `Studio_SolveIK` writes world matrices, and
        // everything downstream reads LOCAL positions and rotations — so the three bones have to be
        // converted back through their parents by `SolveBone`. Without it the world matrices are
        // right and every later stage reads the pose the animation had.
        Harness harness = new();

        Vector3 was = harness.LocalOf(2);

        harness.Solve(new Vector3(12f, 8f, 0f), weight: 1f);

        (harness.LocalOf(2) - was).Length().ShouldBeGreaterThan(
            0.01f, "the local pose was rebuilt from the solved world matrices");
    }

    /// <remarks>
    /// **`IK_RELEASE` is not a no-op, and TF2 declares eight of them for every <c>IK_SELF</c>.**
    /// Measured over every animation of every model `z1800` draws: 1674 SELF, 13359 RELEASE, and
    /// zero of the other four types. `SolveDependencies` (`bone_setup.cpp:4128`) gives it its own
    /// case, under Valve's comment *"move target back towards original location"*:
    ///
    /// <code>
    ///   float flWeight = pRule-&gt;flWeight * pRule-&gt;flRuleWeight;
    ///   BuildBoneChain( pos, q, bone, boneToWorld, boneComputed );
    ///   MatrixAngles( boneToWorld[bone], q2, p2 );
    ///   pChainResult-&gt;pos = pChainResult-&gt;pos * (1.0 - flWeight) + p2 * flWeight;
    ///   QuaternionSlerp( pChainResult-&gt;q, q2, flWeight, pChainResult-&gt;q );
    /// </code>
    ///
    /// **It does NOT touch <c>flWeight</c>**, which is why a chain carrying only releases is still
    /// never solved — that is the control below, and it is what made "release solves nothing" look
    /// like the whole story.
    ///
    /// **The weight here is 0.99 and not 1, because 1 cannot arrive.** `AddDependencies` drops a
    /// release whose `flRuleWeight * flWeight` exceeds 0.999, after clearing every rule already on
    /// its chain (`bone_setup.cpp:3319`) — so a full-weight release reaches the solver as an empty
    /// chain rather than as a rule. Asserting on an input the gathering stage cannot produce would
    /// be a change-detector.
    /// </remarks>
    [Test]
    public void Solve_ASelfRuleThenANearlyFullRelease_ReturnsTheChainToTheAnimationsOwnPose()
    {
        Harness harness = new();

        Vector3 wanted = new(12f, 8f, 0f);
        Vector3 was = harness.Foot();

        harness.SolveAll(
            (StudioIkRuleType.Self, wanted, 1f),
            (StudioIkRuleType.Release, wanted, 0.99f));

        harness.Solved.ShouldBe(1, "the self rule still gives the chain a weight to solve at");

        (harness.Foot() - was).Length().ShouldBeLessThan(
            0.5f, "and the release pulled the target almost all the way back to where it started");
    }

    [Test]
    public void Solve_ASelfRuleThenAReleaseAtHalfWeight_LandsBetweenTheTargetAndTheAnimation()
    {
        // **The case that separates a release that is HONOURED from one that is ignored.** At full
        // weight the chain returns to its own pose, which a solver that refused to run would also
        // produce; at half it must sit between, which only an applied blend gives.
        Harness alone = new();
        Harness released = new();

        Vector3 wanted = new(12f, 8f, 0f);

        alone.SolveAll((StudioIkRuleType.Self, wanted, 1f));
        released.SolveAll(
            (StudioIkRuleType.Self, wanted, 1f),
            (StudioIkRuleType.Release, wanted, 0.5f));

        float reached = (alone.Foot() - wanted).Length();
        float pulled = (released.Foot() - wanted).Length();

        pulled.ShouldBeGreaterThan(reached, "a half release gives back half the correction");

        released.Foot().Y.ShouldBeGreaterThan(
            0.5f, "but it did not give back all of it");
    }

    /// <summary>A three-bone chain along X, with a context and the scratch the solve needs.</summary>
    private sealed class Harness
    {
        /// <summary>Bone parents: root, then each hanging off the last.</summary>
        private readonly int[] _parents = [-1, 0, 1];

        /// <summary>Each bone's local matrix, which the solve rebuilds for the three it moves.</summary>
        private readonly float[][] _local = [new float[12], new float[12], new float[12]];

        private readonly IkContext _context = new();
        private readonly BoneAccessor _bones = new(3);

        /// <summary>Builds a slightly bent chain: 0 at the origin, 1 at (10, 1, 0), 2 at (20, 0, 0).</summary>
        /// <remarks>
        /// **A PERFECTLY straight chain is refused by the solver, and the first version of this was
        /// one.** Without a stated knee direction the solver derives the preference from where the
        /// animation put the knee — and a knee exactly on the line between hip and foot gives no
        /// direction at all, so `l3 > (l1 + l2) * KNEEMAX_EPSILON` returns false and nothing moves.
        /// That is Valve's behaviour and correct; the fixture was the degenerate case.
        ///
        /// One unit of bend is enough, and it is also what real content looks like — an animator
        /// does not author a limb locked straight.
        /// </remarks>
        public Harness()
        {
            Vector3[] at = [new(0f, 0f, 0f), new(10f, 1f, 0f), new(20f, 0f, 0f)];

            for (int bone = 0; bone < 3; bone++)
            {
                float[] matrix = _bones.BoneForWrite(bone);

                matrix[0] = 1f;
                matrix[5] = 1f;
                matrix[10] = 1f;
                matrix[3] = at[bone].X;
                matrix[7] = at[bone].Y;
                matrix[11] = at[bone].Z;

                Vector3 relative = bone == 0 ? at[0] : at[bone] - at[bone - 1];

                _local[bone][0] = 1f;
                _local[bone][5] = 1f;
                _local[bone][10] = 1f;
                _local[bone][3] = relative.X;
                _local[bone][7] = relative.Y;
                _local[bone][11] = relative.Z;
            }
        }

        /// <summary>How many chains the last solve actually reached.</summary>
        public int Solved => _context.Solved;

        /// <summary>Where the chain's end bone is now.</summary>
        public Vector3 Foot() => new(_bones.Bone(2)[3], _bones.Bone(2)[7], _bones.Bone(2)[11]);

        /// <summary>One bone's local position, which the rebuild must update.</summary>
        public Vector3 LocalOf(int bone) =>
            new(_local[bone][3], _local[bone][7], _local[bone][11]);

        /// <summary>Runs several rules against the chain, in the order given.</summary>
        /// <remarks>
        /// **Order is part of the mechanism**, not an artefact of the fixture: `SolveDependencies`
        /// walks a chain's rules in sequence and each blends onto the result of the last, so a
        /// release after a self gives back part of what the self asked for and a release before it
        /// gives back nothing.
        /// </remarks>
        public void SolveAll(params (int Type, Vector3 Target, float Weight)[] rules)
        {
            List<(StudioIkRule, Vector3, Quaternion, float)> asked = [];

            foreach ((int type, Vector3 target, float weight) in rules)
            {
                asked.Add((Rule(type), target, Quaternion.Identity, weight));
            }

            _context.Solve([Chain()], asked, _bones, _parents, _local);
        }

        /// <summary>Runs one rule against the chain.</summary>
        public void Solve(Vector3 target, float weight, int type = StudioIkRuleType.Self) =>
            SolveAll((type, target, weight));

        /// <summary>The three-link chain every case here solves.</summary>
        private static StudioIkChain Chain() =>
            new(
                "arm",
                0,
                [
                    new StudioIkLink(0, default),
                    new StudioIkLink(1, default),
                    new StudioIkLink(2, default),
                ]);

        /// <summary>One rule of the given type, with an envelope that is always in force.</summary>
        private static StudioIkRule Rule(int type)
        {
            StudioIkRule rule = new(
                Type: type,
                Chain: 0,
                Bone: 0,
                Slot: 0,
                Height: 0f,
                Radius: 0f,
                Floor: 0f,
                Position: default,
                Rotation: default,
                CompressedError: 0,
                FirstFrame: 0,
                ErrorIndex: 0,
                Start: 0f,
                Peak: 0f,
                Tail: 1f,
                End: 1f,
                Contact: 0f,
                Drop: 0f,
                Top: 0f,
                AttachmentName: 0);

            // The error is stated in the target bone's space, and bone 0 is the identity at the
            // origin — so the error IS the world target, which keeps the fixture's arithmetic
            // visible rather than hidden behind a transform.
            return rule;
        }
    }
}
