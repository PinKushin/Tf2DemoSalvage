using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// The name pairing, and the ordering that makes a chained merge correct by construction.
/// </summary>
/// <remarks>
/// **B180 was that a chained child merged onto its parent's UNMERGED bones**, because the old
/// arrangement kept two arrays and recorded the wrong one. The engine keeps ONE array per entity:
/// the merge writes into it, and every unmerged bone is then built from <c>GetBone( parent )</c> out
/// of that same array. So a bone whose parent was merged rides the merged position with nothing
/// written to make it happen.
///
/// The test that matters here is the three-deep one. It is not "B180 is fixed" — it is that the
/// state B180 described no longer exists to record.
/// </remarks>
public sealed class BoneMergeCacheTests
{
    private const int Vertices = 0x00000400;

    [Test]
    public void MergeMatchingBones_ABoneSharingAName_TakesTheWearersMatrix()
    {
        NamedPose wearer = new(["bip_pelvis", "bip_head"]);
        NamedPose hat = new(["bip_head", "brim"]);

        BoneAccessor wearerBones = new(2);
        wearerBones.BoneForWrite(1)[3] = 64f;   // the head, 64 units up

        BoneAccessor hatBones = new(2);
        BoneBitList marked = new(2);

        BoneMergeCache cache = new(hat);
        cache.UpdateCache(wearer);
        cache.MergeMatchingBones(wearerBones, hatBones, marked, Vertices);

        hatBones.Bone(0)[3].ShouldBe(64f);
        marked.IsMarked(0).ShouldBeTrue();

        // **The control, and it is what a count cannot give.** An item matching one bone of two is
        // correct when that one is bip_head; a merge that copied into every slot would satisfy the
        // assertion above and put the brim on the head as well.
        marked.IsMarked(1).ShouldBeFalse();
        cache.IsMerged(1).ShouldBeFalse();
    }

    [Test]
    public void UpdateCache_WhenEveryMatchedParentBoneIsMarked_KeepsTheNarrowMask()
    {
        NamedPose wearer = new(["bip_head"], StudioBoneFlags.UsedByBoneMerge);
        NamedPose hat = new(["bip_head"]);

        BoneMergeCache cache = new(hat);
        cache.UpdateCache(wearer);

        cache.FollowBoneSetupMask.ShouldBe(StudioBoneFlags.UsedByBoneMerge);
    }

    [Test]
    public void UpdateCache_WhenAMatchedParentBoneIsUnmarked_WidensToAnything()
    {
        // **The cost Valve's own commented-out warning describes** (bone_merge_cache.cpp:95), and
        // it calls it a PERFORMANCE warning rather than an error, which is exactly right: an
        // unmarked bone does not break the merge, it makes the wearer build its entire skeleton for
        // every item worn on it.
        NamedPose wearer = new(["bip_head"], flags: Vertices);
        NamedPose hat = new(["bip_head"]);

        BoneMergeCache cache = new(hat);
        cache.UpdateCache(wearer);

        cache.FollowBoneSetupMask.ShouldBe(StudioBoneFlags.UsedByAnything);
    }

    [Test]
    public void UpdateCache_WhenNothingMatches_AsksTheWearerForNothing()
    {
        // Valve slams the mask to zero rather than leaving it at BONE_USED_BY_BONE_MERGE, so an
        // unrelated pair of models does not make the wearer build anything at all.
        BoneMergeCache cache = new(new NamedPose(["mvm"]));
        cache.UpdateCache(new NamedPose(["bip_head", "bip_pelvis"]));

        cache.MatchedCount.ShouldBe(0);
        cache.FollowBoneSetupMask.ShouldBe(0);
    }

    [Test]
    public void UpdateCache_WhenTheWearerChanges_RepairsAgainstTheNewSkeleton()
    {
        // A scout's skeleton is not a heavy's. One pairing per worn item would pose every hat with
        // whichever class wore it first — wrong by a bone or two, which reads as a hat sitting
        // slightly off rather than as a bug.
        NamedPose hat = new(["bip_head"]);
        BoneMergeCache cache = new(hat);

        cache.UpdateCache(new NamedPose(["bip_head"]));
        cache.MatchedCount.ShouldBe(1);

        cache.UpdateCache(new NamedPose(["bip_pelvis", "bip_spine_0"]));
        cache.MatchedCount.ShouldBe(0);
    }

    [Test]
    public void SetupBones_AChainThreeDeep_MergesOntoTheParentsMERGEDBones()
    {
        // **This is B180, and it is not "fixed" — the state it described does not exist.** One
        // array per entity, the merge writes into it before the transform stage, so what the third
        // link reads is where its parent actually ended up.
        //
        // The numbers make the two readings distinguishable, which is the whole design of the case:
        // the player's hand is at 40, the weapon's own unmerged bone is at 7. A sight that reads 40
        // rode the merged position; one that reads 7 read the weapon's own skeleton, which is
        // exactly the defect.
        BoneFrameCounter clock = new();

        NamedPose playerPose = new(["hand"], StudioBoneFlags.UsedByBoneMerge);
        NamedPose weaponPose = new(["hand", "muzzle"], StudioBoneFlags.UsedByBoneMerge);
        NamedPose sightPose = new(["muzzle"]);

        AnimatingEntity player = new(playerPose, clock);
        AnimatingEntity weapon = new(weaponPose, clock) { Follows = player };
        AnimatingEntity sight = new(sightPose, clock) { Follows = weapon };

        // The player's hand ends up at 40; the weapon's own rest pose would put its bones at 7.
        playerPose.Places(0, 40f);
        weaponPose.Places(0, 7f);
        weaponPose.Places(1, 7f);

        sight.SetupBones(Vertices, 0d).ShouldBeTrue();

        weapon.Bones.Bone(0)[3].ShouldBe(40f, "the weapon's hand bone is the player's");

        // The muzzle is the weapon's OWN bone, built by its transform stage from the merged hand.
        // NamedPose builds an unmerged bone at its placed value plus its parent's, so 7 + 40.
        weapon.Bones.Bone(1)[3].ShouldBe(47f, "the muzzle rides the merged hand");

        sight.Bones.Bone(0)[3].ShouldBe(
            47f,
            "the sight merges onto the weapon's MERGED muzzle, not the 7 its own skeleton says");
    }

    [Test]
    public void ShrinkToNothing_TheMatchedBones_AreCollapsedAndMarkedSoNothingRebuildsThem()
    {
        // **Valve's fallback for a wearer that could not be built** (bone_merge_cache.cpp:136), and
        // its own comment says why: "This routine has no way to tell its caller not to draw itself
        // unfortunately. But we can shrink all the bones down to zero size." A zero matrix draws
        // nothing; leaving the item at its own rest pose draws it at full size in the middle of the
        // map.
        //
        // Tested directly rather than through SetupBones, because reaching it that way needs a
        // wearer that BOTH pairs and fails — and pairing requires bones, which is what makes a
        // wearer succeed. The path exists for a failure this project's model cannot currently
        // produce, which is worth saying rather than contriving.
        NamedPose hat = new(["bip_head", "brim"]);
        BoneMergeCache cache = new(hat);
        cache.UpdateCache(new NamedPose(["bip_head"]));

        BoneAccessor bones = new(2);
        BoneBitList marked = new(2);

        cache.ShrinkToNothing(bones, marked);

        bones.Bone(0).ToArray().ShouldAllBe(cell => cell == 0f);
        marked.IsMarked(0).ShouldBeTrue();

        // The control: an unmatched bone is not the wearer's to collapse, so it keeps its identity
        // and is still the transform stage's to build.
        bones.Bone(1)[0].ShouldBe(1f);
        marked.IsMarked(1).ShouldBeFalse();
    }

    [Test]
    public void SetupBones_AWearerThatCannotBuild_ReportsFailureRatherThanPlacingTheItem()
    {
        BoneFrameCounter clock = new();

        AnimatingEntity wearer = new(new NamedPose([]), clock);
        NamedPose hatPose = new(["bip_head"]);
        AnimatingEntity hat = new(hatPose, clock) { Follows = wearer };

        // **A wearer with no bones pairs with nothing**, so the follow mask is 0 — and asking for
        // mask 0 is satisfied by the early-out, which is why this returns FALSE from the parent's
        // own bone-count guard rather than from the merge. Worth pinning: the two paths to false
        // look alike from here and only one of them is the merge's.
        hat.SetupBones(Vertices, 0d).ShouldBeFalse();
    }

    /// <summary>A pose source with real bone names and a settable placement per bone.</summary>
    /// <remarks>
    /// **Builds unmerged bones from their parent**, which is the one behaviour of the real transform
    /// stage these tests depend on — without it the three-deep case cannot distinguish a merged
    /// parent from an unmerged one, which is the only thing it exists to measure.
    /// </remarks>
    private sealed class NamedPose(IReadOnlyList<string> names, int flags = ~0) : IBonePose
    {
        private readonly float[] _places = new float[names.Count];

        public int BoneCount => names.Count;

        public int FlagsOf(int bone) => flags;

        public string NameOf(int bone) => names[bone];

        /// <summary>Where this bone sits relative to its parent, along the matrix's X translation.</summary>
        public void Places(int bone, float at) => _places[bone] = at;

        public void Build(int boneMask, double currentTime, BoneAccessor into, BoneBitList alreadyWritten)
        {
            for (int bone = 0; bone < names.Count; bone++)
            {
                if (alreadyWritten.IsMarked(bone))
                {
                    continue;
                }

                // Bone 0 is the root; everything after it hangs off bone 0, so a merged root is
                // visible in its children exactly as the real hierarchy makes it.
                float parent = bone == 0 ? 0f : into.Bone(0)[3];

                into.BoneForWrite(bone)[3] = _places[bone] + parent;
                alreadyWritten.Mark(bone);
            }
        }
    }
}
