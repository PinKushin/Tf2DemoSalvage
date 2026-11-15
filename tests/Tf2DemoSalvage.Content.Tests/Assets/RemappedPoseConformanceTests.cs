using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// An included model's animation sampled onto a base skeleton that orders and counts its bones differently (B621).
/// </summary>
/// <remarks>
/// **Valve:** <c>CalcVirtualAnimation</c> walks the BASE model's bones (<c>bone_setup.cpp:927</c>), seeds base bone
/// <c>i</c> from the sequence group's bone <c>j = boneMap[i]</c> (<c>:931-947</c>), and writes each track at
/// <c>masterBone[panim-&gt;bone]</c> (<c>:966</c>) from the animation model's own bone (<c>:973</c>). So the result is
/// the included model's own pose, carried to the base indices, with each base bone the group lacks left to the caller's
/// pose, which is <c>InitPose</c>'s base bind for a main sequence and weight zero for a layer (<c>SlerpBones</c>,
/// <c>:1417-1431</c>).
///
/// The fixture reverses the eight synthetic bones and adds two the included model lacks, and the base's own copies of
/// the eight carry different rest values, so a seed taken from the wrong skeleton shows. Shipped models need it: medic,
/// the MvM robots, the sentry and the tank (the <c>animation-remap</c> probe, 2026-11-15).
/// </remarks>
public sealed class RemappedPoseConformanceTests
{
    private const int Animation = 0;
    private const int Frame = 2;

    [TestCase(0.5f, TestName = "PoseBetweenInto_ABlendOntoAReorderedLongerBase_IsTheOwnPoseAtTheBaseIndices")]
    [TestCase(0f, TestName = "PoseBetweenInto_TheFrameAloneOntoAReorderedLongerBase_IsTheOwnPoseAtTheBaseIndices")]
    public void PoseBetweenInto_OntoAReorderedLongerBase_IsTheOwnPoseAtTheBaseIndices(float fraction)
    {
        byte[] model = FramePairDifferentialTests.Synthetic();
        List<StudioBone> included = FramePairDifferentialTests.Skeleton();
        List<StudioBone> skeleton =
        [
            .. Enumerable.Range(0, included.Count).Select(at => included[included.Count - 1 - at] with
            {
                Position = (100f + at, 200f, 300f),
                Rotation = (0f, 0f, 0f, 1f),
            }),
            Extra("x0", (5f, 6f, 7f)),
            Extra("x1", (-1f, -2f, -3f)),
        ];

        int[] remap = StudioBones.Remap(included, skeleton);
        StudioBone[] seeds = StudioBones.Seeds(skeleton, included);

        remap.ShouldBe([7, 6, 5, 4, 3, 2, 1, 0], "the control: the fixture reverses the included bones");

        StudioPoseWorkspace workspace = new();
        // A bone the frame names twice keeps its last entry, as an expansion writes it (the fixture names bone 2 twice).
        Dictionary<int, StudioBonePose> own = [];

        foreach (StudioBonePose pose in StudioAnimation.PoseBetweenInto(
            model, included, null, included, Animation, Frame, fraction, workspace, 0))
        {
            own[pose.Bone] = pose;
        }

        own.Count.ShouldBeGreaterThan(0, "the control: the frame names bones");

        List<StudioBonePose> got =
            [.. StudioAnimation.PoseBetweenInto(model, included, remap, seeds, Animation, Frame, fraction, workspace, 1)];

        List<StudioBonePose> want =
        [
            .. Enumerable.Range(0, skeleton.Count).Select(at =>
            {
                if (at >= included.Count)
                {
                    return new StudioBonePose(at, skeleton[at].Position, skeleton[at].Rotation);
                }

                int from = included.Count - 1 - at;

                return own.TryGetValue(from, out StudioBonePose sampled)
                    ? sampled with { Bone = at }
                    : new StudioBonePose(at, included[from].Position, included[from].Rotation);
            }),
        ];

        Bits(got).ShouldBe(Bits(want));
    }

    /// <remarks>
    /// **The real shape, on the shipped model the probe found**: <c>medic.mdl</c> has 92 bones and
    /// <c>medic_user_animations.mdl</c> 84, 51 of them at another index. Every animation at a half: the remapped pose has
    /// one entry per BASE bone, and each base bone the group has is the group's own pose at its own index, to the bit.
    /// </remarks>
    [Test]
    public void PoseBetweenInto_EveryMedicUserAnimationOntoTheMedic_IsTheOwnPoseAtTheBaseIndices()
    {
        GameInstall.Require();

        VpkArchive archive = VpkArchive.Open(GameInstall.Find("tf2_misc_dir.vpk")!);
        byte[] model = archive.ReadFile("models/player/medic_user_animations.mdl").ShouldNotBeNull();
        IReadOnlyList<StudioBone> included = StudioBones.Read(model);
        IReadOnlyList<StudioBone> skeleton = StudioBones.Read(archive.ReadFile("models/player/medic.mdl").ShouldNotBeNull());

        int[] remap = StudioBones.Remap(included, skeleton);
        int[] map = StudioBones.Remap(skeleton, included);
        StudioBone[] seeds = StudioBones.Seeds(skeleton, included);

        remap.Where((target, at) => target >= 0 && target != at).Count()
            .ShouldBeGreaterThan(0, "the control: the medic's group is not at its own indices");

        StudioPoseWorkspace workspace = new();
        int compared = 0;
        List<string> wrong = [];

        for (int animation = 0; animation < StudioAnimation.Count(model); animation++)
        {
            int frame = StudioAnimation.Frames(model, animation) / 2;

            List<StudioBonePose> own =
                [.. StudioAnimation.PoseBetweenInto(model, included, null, included, animation, frame, 0.5f, workspace, 0)];

            if (own.Count == 0)
            {
                continue;
            }

            List<StudioBonePose> got =
                [.. StudioAnimation.PoseBetweenInto(model, included, remap, seeds, animation, frame, 0.5f, workspace, 1)];

            got.Count.ShouldBe(skeleton.Count, $"animation {animation}");

            for (int bone = 0; bone < skeleton.Count; bone++)
            {
                if (map[bone] < 0)
                {
                    continue;
                }

                compared++;

                if (Bits([got[bone] with { Bone = 0 }])[0] != Bits([own[map[bone]] with { Bone = 0 }])[0] && wrong.Count < 5)
                {
                    wrong.Add($"animation {animation}, {skeleton[bone].Name}: {got[bone]}, want {own[map[bone]]}");
                }
            }
        }

        // Measured 2026-11-15: one animation of the group reads from the .mdl itself and the rest read empty, so the
        // control is one whole animation's mapped bones, not a corpus.
        compared.ShouldBeGreaterThanOrEqualTo(map.Count(found => found >= 0), "the control: a whole animation was compared");
        wrong.ShouldBeEmpty();
    }

    private static StudioBone Extra(string name, (float X, float Y, float Z) position) =>
        new(
            Name: name,
            Parent: -1,
            Position: position,
            Rotation: (0f, 0f, 0f, 1f),
            PoseToBone: default,
            Euler: default,
            PositionScale: default,
            RotationScale: default,
            Flags: 0,
            Alignment: default);

    /// <summary>Each entry's bone and its seven floats as bits, so <c>-0</c> and <c>+0</c> differ.</summary>
    private static List<string> Bits(IEnumerable<StudioBonePose> poses) =>
    [
        .. poses.Select(pose =>
            $"{pose.Bone}: " + string.Join(
                ',',
                new[]
                {
                    pose.Position.X, pose.Position.Y, pose.Position.Z,
                    pose.Rotation.X, pose.Rotation.Y, pose.Rotation.Z, pose.Rotation.W,
                }.Select(value => BitConverter.SingleToInt32Bits(value).ToString("X8", System.Globalization.CultureInfo.InvariantCulture)))),
    ];
}
