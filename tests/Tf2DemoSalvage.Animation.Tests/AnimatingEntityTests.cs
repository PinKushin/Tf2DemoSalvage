using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// The properties that make the bone cache Valve's rather than merely similar.
/// </summary>
/// <remarks>
/// **Every test here is a COUNT**, because the thing being built is not a value — it is a decision
/// about when work happens. "Posed once per frame" and "posed every time" produce identical
/// matrices, so no assertion on a matrix can tell them apart. The fake counts calls, and the count
/// is the measurement.
///
/// **That is also why this needs no model.** The seam is where the SDK's own is:
/// <c>C_BaseAnimating::SetupBones</c> owns the caching, the masks and the recursion and delegates
/// the blend. Testing the owner does not require the delegate to be real, and requiring it would
/// have put model loading in the way of the questions that matter.
///
/// The masks are the engine's: <c>BONE_USED_BY_VERTEX_LOD0</c> is 0x400,
/// <c>BONE_USED_BY_ATTACHMENT</c> is 0x200, and both are asserted against <c>studio.h</c> by
/// <c>BonePipelineStructTests</c> — so these are not magic numbers, they are the checked ones.
/// </remarks>
public sealed class AnimatingEntityTests
{
    private const int Vertices = 0x00000400;
    private const int Attachments = 0x00000200;

    [Test]
    public void SetupBones_CalledTwiceInOneFrame_BuildsOnce()
    {
        CountingPose pose = new();
        AnimatingEntity entity = new(pose, new BoneFrameCounter());

        entity.SetupBones(Vertices, 0d);
        entity.SetupBones(Vertices, 0d);

        // **The single most important number in this file.** Without the readable-bones early-out
        // a player worn by six items is posed six times, and nothing about the picture says so.
        pose.Builds.ShouldBe(1);
    }

    [Test]
    public void SetupBones_AfterTheFrameAdvances_BuildsAgain()
    {
        CountingPose pose = new();
        BoneFrameCounter clock = new();
        AnimatingEntity entity = new(pose, clock);

        entity.SetupBones(Vertices, 0d);
        clock.Advance();
        entity.SetupBones(Vertices, 0.015d);

        // The control for the test above. A cache that never invalidates satisfies "builds once"
        // perfectly and freezes every animation in the demo.
        pose.Builds.ShouldBe(2);
    }

    [Test]
    public void SetupBones_ForAMaskAlreadyCovered_DoesNotBuildAgain()
    {
        CountingPose pose = new();
        AnimatingEntity entity = new(pose, new BoneFrameCounter());

        entity.SetupBones(Vertices | Attachments, 0d);
        entity.SetupBones(Vertices, 0d);

        // A narrower request than one already satisfied is free. This is what lets an attachment
        // ask its parent for bones without paying for a pose.
        pose.Builds.ShouldBe(1);
    }

    [Test]
    public void SetupBones_ForABitNotYetBuilt_BuildsAgain()
    {
        CountingPose pose = new();
        AnimatingEntity entity = new(pose, new BoneFrameCounter());

        entity.SetupBones(Vertices, 0d);
        entity.SetupBones(Attachments, 0d);

        // **The other side of the mask, and the one a naive cache gets wrong.** Caching on "have we
        // posed this frame" rather than on WHICH bones would return attachment bones that were
        // never built — an attachment placed against an identity matrix, which is the map origin.
        pose.Builds.ShouldBe(2);
    }

    [Test]
    public void SetupBones_AParentSharedBySixChildren_BuildsThatParentOnce()
    {
        CountingPose wearerPose = new(shared: true);
        BoneFrameCounter clock = new();
        AnimatingEntity wearer = new(wearerPose, clock);

        List<AnimatingEntity> worn = [];

        for (int index = 0; index < 6; index++)
        {
            worn.Add(new AnimatingEntity(new CountingPose(shared: true), clock) { Follows = wearer });
        }

        foreach (AnimatingEntity item in worn)
        {
            item.SetupBones(Vertices, 0d);
        }

        // **This is what the depth sort got for free and the recursion must not lose** (B181). Six
        // items on a player, and the player's skeleton built once.
        wearerPose.Builds.ShouldBe(1);
    }

    [Test]
    public void SetupBones_AChainThreeDeep_BuildsEveryLinkParentFirst()
    {
        BoneFrameCounter clock = new();
        List<string> order = [];

        AnimatingEntity player = new(new CountingPose(order, "player"), clock);
        AnimatingEntity weapon = new(new CountingPose(order, "weapon"), clock) { Follows = player };
        AnimatingEntity sight = new(new CountingPose(order, "sight"), clock) { Follows = weapon };

        sight.SetupBones(Vertices, 0d).ShouldBeTrue();

        // **Order, not just presence.** A child merges onto bones its parent has already built, so
        // "all three were built" and "built in the right order" are different claims and only one
        // of them draws correctly. This is the case the two-group split could not express and the
        // depth sort was written for: CTFWeaponAttachmentModel parents to the WEAPON, which parents
        // to the player (tf_weaponbase.cpp:6960).
        order.ShouldBe(["player", "weapon", "sight"]);
    }

    [Test]
    public void SetupBones_AFollowCycle_TerminatesRatherThanOverflowingTheStack()
    {
        BoneFrameCounter clock = new();

        // Shared bone names, so the merge actually pairs and the recursion actually fires. Without
        // that, nothing matches, the follow mask is 0, and the early-out returns before any
        // recursion happens — a test that would pass while measuring nothing.
        AnimatingEntity first = new(new CountingPose(shared: true), clock);
        AnimatingEntity second = new(new CountingPose(shared: true), clock) { Follows = first };

        first.Follows = second;

        // **It terminates by the EARLY-OUT, not by the depth budget, and that was a surprise.**
        // Readable bones are set before the parent is asked — Valve does the same, so a stage
        // part-way through a build can read what an earlier stage wrote — and the side effect is
        // that re-entering an entity at the same mask inside one frame hits
        // `(readable & wanted) == wanted` and returns. A two-entity cycle therefore resolves after
        // one lap instead of recursing.
        //
        // So this asserts TERMINATION rather than refusal. The first version of this test asserted
        // false and was wrong about which mechanism was doing the work — the depth budget is a
        // backstop for a chain that keeps widening its mask, not the guard that catches this.
        Should.NotThrow(() => second.SetupBones(Vertices, 0d));
    }

    [Test]
    public void SetupBones_AChainDeeperThanTheBudget_IsRefused()
    {
        BoneFrameCounter clock = new();

        // Twenty links against a budget of sixteen. The deepest legitimate chain is three — an
        // attachment on a weapon on a player — so this is a corruption check rather than a feature
        // limit, and it needs a case that actually reaches it.
        List<AnimatingEntity> chain = [];

        for (int index = 0; index < 20; index++)
        {
            AnimatingEntity link = new(new CountingPose(shared: true), clock);

            if (chain.Count > 0)
            {
                link.Follows = chain[^1];
            }

            chain.Add(link);
        }

        chain[^1].SetupBones(Vertices, 0d).ShouldBeFalse();

        // The control: the same chain within the budget builds. Without it, a guard that refused
        // everything would satisfy the assertion above.
        chain[8].SetupBones(Vertices, 0d).ShouldBeTrue();
    }

    [Test]
    public void SetupBones_AnEntityWithNoBones_ReportsFailureRatherThanThrowing()
    {
        // Valve's contract: MergeMatchingBones checks the result and shrinks the merged bones to
        // zero rather than drawing at the origin (bone_merge_cache.cpp:134). A caller that treated
        // this as an exception would refuse the whole scene over one unloadable model.
        AnimatingEntity entity = new(new CountingPose(bones: 0), new BoneFrameCounter());

        entity.SetupBones(Vertices, 0d).ShouldBeFalse();
    }

    [Test]
    public void SetupBones_AChildWhoseParentCannotBuild_ReportsFailureAndDoesNotBuildItself()
    {
        CountingPose childPose = new(shared: true);
        BoneFrameCounter clock = new();

        AnimatingEntity parent = new(new CountingPose(bones: 0, shared: true), clock);
        AnimatingEntity child = new(childPose, clock) { Follows = parent };

        child.SetupBones(Vertices, 0d).ShouldBeFalse();

        // Valve's `if ( baseDrawn )`: a child of an entity that could not be built is not drawn,
        // because the alternative is an item hanging in the air at the map origin.
        childPose.Builds.ShouldBe(0);
    }

    /// <summary>A pose source that records what was asked of it and nothing else.</summary>
    /// <remarks>
    /// **The <c>shared</c> flag decides whether a merge pairs, and that changes what a test
    /// measures.** An entity whose bones share no name with its parent's does not cause the parent
    /// to be posed at all: nothing matches, the follow mask is 0, and Valve's readable-bones
    /// early-out returns immediately. That is correct — and it means every test about the
    /// RECURSION has to give the two skeletons a bone in common, or it is measuring an entity that
    /// never asks.
    ///
    /// Found by writing those tests before the merge existed and watching them go red when it
    /// arrived. The premise had changed, not the code.
    /// </remarks>
    private sealed class CountingPose : IBonePose
    {
        private readonly List<string>? _order;
        private readonly string _name;
        private readonly bool _shared;

        public CountingPose(int bones = 4, bool shared = false)
        {
            BoneCount = bones;
            _name = "unnamed";
            _shared = shared;
        }

        public CountingPose(List<string> order, string name, bool shared = true)
        {
            BoneCount = 4;
            _order = order;
            _name = name;
            _shared = shared;
        }

        public int BoneCount { get; }

        /// <summary>How many times the blend actually ran.</summary>
        public int Builds { get; private set; }

        /// <summary>Every bone claims every use, so a mask never excludes one by accident.</summary>
        /// <remarks>
        /// Deliberate: these tests are about the CACHE, and a model whose bones happened not to
        /// carry the requested bit would make a skipped build look like a working cache.
        /// </remarks>
        public int FlagsOf(int bone) => ~0;

        /// <summary>Shared across entities when the test needs a merge to pair, unique otherwise.</summary>
        public string NameOf(int bone) => _shared ? $"bone_{bone}" : $"{_name}_{bone}";

        public void Build(int boneMask, double currentTime, BoneAccessor into, BoneBitList alreadyWritten)
        {
            Builds++;
            _order?.Add(_name);
        }
    }
}
