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
/// <c>masterBone[panim-&gt;bone]</c> (<c>:966</c>) from the animation model's own bone (<c>:973</c>). So each base bone
/// the group has is the group's own pose, carried to the base index. A base bone the group lacks is never written, so it
/// keeps <c>InitPose</c>'s base bind, for a delta as much as for a pose.
///
/// The fixture reverses the eight synthetic bones and adds two the group lacks. The base's copies of the eight carry
/// other rest values and no fixed-alignment flag, so a seed or a flag taken from the wrong skeleton shows. Shipped models
/// need it: medic, the MvM robots, the sentry and the tank (the <c>animation-remap</c> probe, 2026-11-15).
/// </remarks>
public sealed class RemappedPoseConformanceTests
{
    /// <remarks>
    /// Each path through the sampler: one chain (animation 0), a pair across two sections (animation 2, frames 3 and 4),
    /// a frame whose next reads nothing (animation 9, frame 8: the trailing section is in an external block), and a delta
    /// (animation 1). At zero the frame is read alone, at a half it is blended; a half keeps an unchanged bone exact.
    /// </remarks>
    [TestCase(0, 2, 0.5f, TestName = "PoseBetweenInto_OneChainBlendedOntoAReorderedLongerBase_IsTheOwnPoseAtTheBaseIndices")]
    [TestCase(0, 2, 0f, TestName = "PoseBetweenInto_OneChainAloneOntoAReorderedLongerBase_IsTheOwnPoseAtTheBaseIndices")]
    [TestCase(2, 3, 0.5f, TestName = "PoseBetweenInto_TwoSectionsBlendedOntoAReorderedLongerBase_IsTheOwnPoseAtTheBaseIndices")]
    [TestCase(9, 8, 0.5f, TestName = "PoseBetweenInto_AFrameWhoseNextReadsNothingOntoAReorderedLongerBase_IsTheOwnPoseAtTheBaseIndices")]
    [TestCase(1, 1, 0.5f, TestName = "PoseBetweenInto_ADeltaBlendedOntoAReorderedLongerBase_KeepsTheUnmappedBonesAtTheBaseBind")]
    [TestCase(1, 1, 0f, TestName = "PoseBetweenInto_ADeltaAloneOntoAReorderedLongerBase_KeepsTheUnmappedBonesAtTheBaseBind")]
    public void PoseBetweenInto_OntoAReorderedLongerBase_IsTheOwnPoseAtTheBaseIndices(int animation, int frame, float fraction)
    {
        byte[] model = SyntheticStudioAnimation.Synthetic();
        List<StudioBone> included = SyntheticStudioAnimation.Skeleton();
        List<StudioBone> skeleton =
        [
            .. Enumerable.Range(0, included.Count).Select(at => included[included.Count - 1 - at] with
            {
                Position = (100f + at, 200f, 300f),
                Rotation = (0f, 0f, 0f, 1f),
                Flags = 0,
                Alignment = default,
            }),
            Extra("x0", (5f, 6f, 7f)),
            Extra("x1", (-1f, -2f, -3f)),
        ];

        StudioRemap group = StudioRemap.Onto(skeleton, included);

        StudioBones.Remap(included, skeleton).ShouldBe([7, 6, 5, 4, 3, 2, 1, 0], "the control: the fixture reverses the bones");
        included[5].Flags.ShouldNotBe(skeleton[2].Flags, "the control: the two copies of bone 5 differ in their flag");

        StudioPoseWorkspace workspace = new();
        List<StudioBonePose> own =
            [.. StudioAnimation.PoseBetweenInto(model, StudioRemap.Own(included), animation, frame, fraction, workspace, 0)];

        own.Count.ShouldBe(included.Count, "the control: the frame names bones, and comes back one entry per bone");

        List<StudioBonePose> got =
            [.. StudioAnimation.PoseBetweenInto(model, group, animation, frame, fraction, workspace, 1)];

        List<StudioBonePose> want =
        [
            .. Enumerable.Range(0, skeleton.Count).Select(at => at < included.Count
                ? own[included.Count - 1 - at] with { Bone = at }
                : new StudioBonePose(at, skeleton[at].Position, skeleton[at].Rotation)),
        ];

        Bits(got).ShouldBe(Bits(want));
    }

    /// <remarks>
    /// **The real shape, on the shipped model the probe found**: <c>medic.mdl</c> has 92 bones and
    /// <c>medic_user_animations.mdl</c> 84, 51 of them at another index. Each animation that reads from the file, at a
    /// half: the remapped pose has one entry per BASE bone, and each base bone the group has is the group's own pose at
    /// that base index, to the bit. Measured 2026-11-15: one animation reads from the file and the rest read empty here.
    /// </remarks>
    [Test]
    public void PoseBetweenInto_TheMedicUserAnimationsOntoTheMedic_IsTheOwnPoseAtTheBaseIndices()
    {
        GameInstall.Require();

        VpkArchive archive = VpkArchive.Open(GameInstall.Find("tf2_misc_dir.vpk")!);
        byte[] model = archive.ReadFile("models/player/medic_user_animations.mdl").ShouldNotBeNull();
        IReadOnlyList<StudioBone> included = StudioBones.Read(model);
        IReadOnlyList<StudioBone> skeleton = StudioBones.Read(archive.ReadFile("models/player/medic.mdl").ShouldNotBeNull());

        StudioRemap group = StudioRemap.Onto(skeleton, included);
        int[] map = StudioBones.Remap(skeleton, included);

        StudioBones.Remap(included, skeleton).Where((target, at) => target >= 0 && target != at).Count()
            .ShouldBeGreaterThan(0, "the control: the medic's group is not at its own indices");

        StudioPoseWorkspace workspace = new();
        int animations = 0;
        List<string> wrong = [];

        for (int animation = 0; animation < StudioAnimation.Count(model); animation++)
        {
            int frame = StudioAnimation.Frames(model, animation) / 2;

            List<StudioBonePose> own =
                [.. StudioAnimation.PoseBetweenInto(model, StudioRemap.Own(included), animation, frame, 0.5f, workspace, 0)];

            if (own.Count == 0)
            {
                continue;
            }

            animations++;
            own.Count.ShouldBe(included.Count, $"animation {animation}: the group's own pose is one entry per group bone");

            List<StudioBonePose> got =
                [.. StudioAnimation.PoseBetweenInto(model, group, animation, frame, 0.5f, workspace, 1)];

            got.Count.ShouldBe(skeleton.Count, $"animation {animation}");

            for (int bone = 0; bone < skeleton.Count && wrong.Count < 5; bone++)
            {
                if (map[bone] >= 0 && Bits([got[bone]])[0] != Bits([own[map[bone]] with { Bone = bone }])[0])
                {
                    wrong.Add($"animation {animation}, {skeleton[bone].Name}: {got[bone]}, want {own[map[bone]]}");
                }
            }
        }

        animations.ShouldBeGreaterThan(0, "the control: an animation was compared");
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
