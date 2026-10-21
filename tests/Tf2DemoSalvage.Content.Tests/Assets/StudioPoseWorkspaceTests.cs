using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// Posing into storage the caller owns: <see cref="StudioAnimation.PoseInto"/> and <see cref="StudioPoseBlend.BlendInto"/>.
/// </summary>
/// <remarks>
/// **Two properties, each with its own test.** The pose built in reused storage is the pose built fresh, exactly; and the
/// second call into the same storage allocates nothing (finding 88, B567: 1.7 GB of bone poses per 20 s of f12 before).
/// The first is read on a real player model, every animation, because a list reused across calls can only leak a
/// previous call's bones when the calls differ in which bones they name, and a synthetic fixture names one.
/// </remarks>
public sealed class StudioPoseWorkspaceTests
{
    private static string Game => GameInstall.Require();

    [Test]
    public void PoseInto_ReusedAcrossEveryAnimationOfAPlayer_EqualsPoseAndBlendIntoEqualsBlend()
    {
        // The animations model a player includes, not the player's own .mdl, which holds two.
        if (Read("models/player/scout_animations.mdl") is not { } model)
        {
            Assert.Ignore("the game is not installed");
            return;
        }

        IReadOnlyList<StudioBone> bones = StudioBones.Read(model);
        List<StudioBonePose> frame = [];
        List<StudioBonePose> next = [];
        List<StudioBonePose> blended = [];
        List<StudioBonePose> scratch = [];
        List<(int Bone, int Flags, int At)> chain = [];
        int compared = 0;

        // Backwards, so each call follows one that named a different set of bones.
        for (int animation = StudioAnimation.Count(model) - 1; animation >= 0; animation--)
        {
            bool delta = StudioAnimation.IsDelta(model, animation);

            StudioAnimation.PoseInto(model, bones, animation, 1, frame, chain)
                .ShouldBe(StudioAnimation.Pose(model, bones, animation, 1), $"animation {animation}, frame 1");
            StudioAnimation.PoseInto(model, bones, animation, 2, next, chain)
                .ShouldBe(StudioAnimation.Pose(model, bones, animation, 2), $"animation {animation}, frame 2");

            StudioPoseBlend.BlendInto(bones, frame, next, 0.3f, delta, blended, scratch)
                .ShouldBe(StudioPoseBlend.Blend(bones, [.. frame], [.. next], 0.3f, delta), $"animation {animation}, blended");

            compared++;
        }

        compared.ShouldBeGreaterThan(100, "a player model carries hundreds of animations; a few means the read failed");
    }

    [Test]
    public void BlendInto_ASecondCallIntoTheSameLists_AllocatesNothing()
    {
        IReadOnlyList<StudioBone> bones = Bones(80);
        List<StudioBonePose> first = [new StudioBonePose(3, (1f, 2f, 3f), (0f, 0f, 0f, 1f))];
        List<StudioBonePose> second = [new StudioBonePose(3, (4f, 5f, 6f), (0f, 0f, 0f, 1f))];
        List<StudioBonePose> into = [];
        List<StudioBonePose> scratch = [];

        StudioPoseBlend.BlendInto(bones, first, second, 0.5f, false, into, scratch);

        long before = GC.GetAllocatedBytesForCurrentThread();
        StudioPoseBlend.BlendInto(bones, first, second, 0.5f, false, into, scratch);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.ShouldBe(0L);
        into.Count.ShouldBe(80);
        into[3].Position.X.ShouldBe(2.5f, 1e-5);
    }

    [Test]
    public void BlendInto_IntoAPoseItReads_IsRefused()
    {
        IReadOnlyList<StudioBone> bones = Bones(2);
        List<StudioBonePose> pose = [new StudioBonePose(0, (1f, 2f, 3f), (0f, 0f, 0f, 1f))];
        List<StudioBonePose> other = [];

        Should.Throw<ArgumentException>(() => StudioPoseBlend.BlendInto(bones, pose, other, 0.5f, false, pose, []));
    }

    /// <summary>A skeleton of <paramref name="count"/> bones at distinct rest positions.</summary>
    private static List<StudioBone> Bones(int count) =>
    [
        .. Enumerable.Range(0, count).Select(bone => new StudioBone(
            Name: $"b{bone}", Parent: bone - 1, Position: (bone, 0f, 0f), Rotation: (0f, 0f, 0f, 1f), PoseToBone: default)),
    ];

    private static byte[]? Read(string path)
    {
        if (!Directory.Exists(Game))
        {
            return null;
        }

        foreach (string name in new[] { "tf2_misc_dir.vpk", "tf2_textures_dir.vpk" })
        {
            string archive = Path.Combine(Game, name);

            if (File.Exists(archive) && VpkArchive.Open(archive).ReadFile(path) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
