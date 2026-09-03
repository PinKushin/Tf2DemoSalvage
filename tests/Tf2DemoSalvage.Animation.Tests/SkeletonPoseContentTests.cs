using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// The whole pipeline, on models TF2 ships: a hat merged onto a scout, end to end.
/// </summary>
/// <remarks>
/// **This is the assertion the component tests cannot make.** Every other test in this assembly
/// uses a fake pose source, so together they prove the architecture agrees with fakes written by
/// the same hand. This one loads Valve's own files, runs <see cref="AnimatingEntity.SetupBones(int, double)"/>
/// through <see cref="SkeletonPose"/> and the merge, and reads the number that decides whether a
/// hat is on a head.
///
/// **The number is 64 and it is not arbitrary.** A scout's origin is at their feet and
/// <c>bip_head</c> sits around sixty-four units above it. A hat reporting a height near zero is a
/// hat on the grass however well its bones matched — which is the state B82 described, and the
/// exact quantity the old code's log line was printing to diagnose it.
///
/// Skips rather than fails without the game, in the house pattern.
/// </remarks>
public sealed class SkeletonPoseContentTests
{
    private static string Game => GameInstall.Require();

    private const int Vertices = 0x00000400;

    [Test]
    public void SetupBones_AScoutsSkeleton_StandsUpWithItsHeadAboveItsOrigin()
    {
        AnimatingEntity scout = Entity("models/player/scout.mdl", out IReadOnlyList<StudioBone> bones);

        scout.SetupBones(Vertices, 0d).ShouldBeTrue();

        int head = IndexOf(bones, "bip_head");

        // **The control for every other assertion in this file.** If the skeleton does not extend,
        // nothing about where a hat lands means anything — a model whose bones all came back at the
        // origin would satisfy a merge test perfectly.
        //
        // **Y, not Z, and getting that wrong is the trap this project has already paid for once.**
        // The bind pose stored in the file is Y-UP: a player measures about 84 along Y and
        // bip_head rests near (0, 75, -1). Z-up is what an ANIMATION produces, and this builds the
        // rest pose deliberately, because every claim here is about the hierarchy and the merge
        // rather than about a sequence.
        //
        // Written after asserting Z first and measuring -1.43 — which reads as "the head is at the
        // feet" and is really "the head is where the artist modelled it". An instrument answering
        // confidently about the wrong axis, which is the same shape as measuring at a tick the demo
        // does not contain.
        scout.Bones.Bone(head)[7].ShouldBeGreaterThan(50f);
        scout.Bones.Bone(head)[7].ShouldBeLessThan(90f);
    }

    [Test]
    public void SetupBones_AHatMergedOntoAScout_PutsItsHeadBoneWhereTheScoutsIs()
    {
        AnimatingEntity scout = Entity("models/player/scout.mdl", out IReadOnlyList<StudioBone> scoutBones);
        AnimatingEntity hat = Entity("models/player/items/scout/scout_cap.mdl", out IReadOnlyList<StudioBone> hatBones);

        hat.Follows = scout;

        hat.SetupBones(Vertices, 0d).ShouldBeTrue();

        // **Whatever bone the two skeletons share, compared position for position.** Naming
        // bip_head here would be an assumption about a specific hat; the claim is that every
        // matched bone takes the wearer's matrix, which is what bone merging MEANS.
        List<string> shared = hatBones
            .Select(bone => bone.Name)
            .Where(name => scoutBones.Any(other => string.Equals(other.Name, name, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        shared.ShouldNotBeEmpty("a hat that shares no bone with a player cannot be merged at all");

        foreach (string name in shared)
        {
            int mine = IndexOf(hatBones, name);
            int theirs = IndexOf(scoutBones, name);

            hat.Bones.Bone(mine).ToArray()
                .ShouldBe(scout.Bones.Bone(theirs).ToArray(), $"{name} should be the scout's");
        }

        // **And the merge must have moved something.** Every assertion above is satisfied by two
        // identical identity matrices, which is what a merge that copied nothing onto a skeleton
        // that built nothing would produce.
        int head = IndexOf(hatBones, shared[0]);

        hat.Bones.Bone(head).ToArray().ShouldNotBe(
            [1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f],
            "the merged bone is still identity, so nothing was actually posed");
    }

    [Test]
    public void SetupBones_TheHatsUnmatchedBones_RideTheBoneTheyHangFrom()
    {
        // **The half a merge alone does not do, and the half B180 was about.** An unmatched bone is
        // built by this model's own transform stage FROM ITS PARENT — which may itself have been
        // merged. The engine gets this by having one array; here it means a hat's brim follows the
        // scout's head without the brim ever matching anything.
        AnimatingEntity scout = Entity("models/player/scout.mdl", out IReadOnlyList<StudioBone> scoutBones);
        AnimatingEntity hat = Entity("models/player/items/scout/scout_cap.mdl", out IReadOnlyList<StudioBone> hatBones);

        hat.Follows = scout;
        hat.SetupBones(Vertices, 0d).ShouldBeTrue();

        List<int> unmatched = [];

        for (int bone = 0; bone < hatBones.Count; bone++)
        {
            string name = hatBones[bone].Name;

            if (!scoutBones.Any(other => string.Equals(other.Name, name, StringComparison.OrdinalIgnoreCase)) &&
                hatBones[bone].Parent >= 0)
            {
                unmatched.Add(bone);
            }
        }

        if (unmatched.Count == 0)
        {
            Assert.Ignore("this hat has no unmatched child bones, so there is nothing to measure");
            return;
        }

        foreach (int bone in unmatched)
        {
            // Near its parent rather than at the model origin. A hat's parts are inches apart and
            // the scout's head is around seventy-five units along the bind pose's up axis, so
            // "within a foot of the parent" separates riding the head from sitting at the origin by
            // a wide margin.
            //
            // Index 7 is Y, which is up in the BIND pose — see the control test above, where
            // asserting Z measured -1.43 and read as a head at the feet.
            float mine = hat.Bones.Bone(bone)[7];
            float parent = hat.Bones.Bone(hatBones[bone].Parent)[7];

            Math.Abs(mine - parent).ShouldBeLessThan(12f, $"{hatBones[bone].Name} is not near its parent");
            mine.ShouldBeGreaterThan(40f, $"{hatBones[bone].Name} is at the origin, so it did not ride the head");
        }
    }

    [Test]
    public void SetupBones_AHatOnAScoutStandingAwayFromTheOrigin_NeedsNoTransformOfItsOwn()
    {
        // **The property that deletes a whole category of bookkeeping.** Valve's bone-to-world is
        // WORLD space: BuildTransformations concatenates the entity's placement into every root
        // bone (c_baseanimating.cpp:1591) and children inherit it. So a merged item's bones are
        // already where the wearer is, and the item is never told where its wearer stands.
        //
        // The arrangement this replaces carried the wearer's transform ALONGSIDE the bones and
        // applied it at draw time, which is the bookkeeping that let a three-deep chain mix two
        // spaces (B180).
        AnimatingEntity scout = Entity("models/player/scout.mdl", out IReadOnlyList<StudioBone> scoutBones);
        AnimatingEntity hat = Entity("models/player/items/scout/scout_cap.mdl", out IReadOnlyList<StudioBone> hatBones);

        // A thousand units along X, which no bind pose is anywhere near — so a bone that arrives
        // near 1000 got there through the wearer and a bone near 0 did not.
        Placed(scout).EntityTransform =
            [1f, 0f, 0f, 1000f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f];

        hat.Follows = scout;
        hat.SetupBones(Vertices, 0d).ShouldBeTrue();

        string shared = hatBones
            .Select(bone => bone.Name)
            .First(name => scoutBones.Any(other => string.Equals(other.Name, name, StringComparison.OrdinalIgnoreCase)));

        // The hat's EntityTransform was never set — it is still null. If the bone is out at 1000
        // anyway, it got there entirely through the merge, which is the claim.
        hat.Bones.Bone(IndexOf(hatBones, shared))[3].ShouldBeGreaterThan(900f);

        // The control: without the wearer's placement the same bone sits near the model origin, so
        // this is measuring the transform rather than something the bind pose already did.
        AnimatingEntity alone = Entity("models/player/items/scout/scout_cap.mdl", out IReadOnlyList<StudioBone> aloneBones);

        alone.SetupBones(Vertices, 0d).ShouldBeTrue();
        alone.Bones.Bone(IndexOf(aloneBones, shared))[3].ShouldBeLessThan(100f);
    }

    /// <summary>The pose source behind an entity, for a test that needs to drive it.</summary>
    /// <remarks>
    /// **Not a lookup table keyed by entity**, which was the first shape and is the same shared
    /// mutable state that leaked a bone name across parallel fixtures earlier in this suite's
    /// history. The entity exposes what it was built over, so nothing has to be remembered.
    /// </remarks>
    private static SkeletonPose Placed(AnimatingEntity entity) => (SkeletonPose)entity.Pose;

    private static int IndexOf(IReadOnlyList<StudioBone> bones, string name)
    {
        for (int bone = 0; bone < bones.Count; bone++)
        {
            if (string.Equals(bones[bone].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return bone;
            }
        }

        throw new InvalidOperationException($"no bone named {name}");
    }

    private static AnimatingEntity Entity(string path, out IReadOnlyList<StudioBone> bones)
    {
        bones = [];

        // Assert.Ignore is [DoesNotReturn], so nothing follows these — no `return null!` is needed
        // and adding one is what S8969 objects to. Worth knowing rather than fighting: the house
        // skip pattern elsewhere returns a value because those helpers return a collection the
        // caller then reads, and this one does not.
        if (!Directory.Exists(Game))
        {
            Assert.Ignore("Team Fortress 2 is not installed");
        }

        byte[]? file = GameArchives.Open(Game).Read(path);

        if (file is null)
        {
            Assert.Ignore($"{path} is not in this install");
        }

        bones = StudioBones.Read(file);

        // The rest pose: no animation overrides. Enough for every claim here, which are all about
        // the HIERARCHY and the merge rather than about a sequence.
        return new AnimatingEntity(
            new SkeletonPose(bones, (_, _, _, _) => []), new BoneFrameCounter());
    }
}
