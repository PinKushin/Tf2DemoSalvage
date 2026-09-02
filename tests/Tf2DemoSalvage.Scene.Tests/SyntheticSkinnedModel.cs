using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// A skinned model whose sequences are chosen by the test rather than read from the game.
/// </summary>
/// <remarks>
/// **The animation fallback chain was untestable because every existing test needed TF2
/// installed.** `WeaponSlotAnimationTests` builds its model by opening the real VPKs and reading
/// `models/player/medic.mdl`, so on any machine without the game — every CI runner, and this one —
/// it skips, and the fallback chain in <c>PlayerAnimation.For</c> is never entered. Measured
/// 2026-08-19: 20 of that file's 28 mutants had no coverage at all, and the uncovered lines were
/// precisely the fallbacks.
///
/// That is the same shape as the demo corpus problem this project already solved once. A case the
/// installed game does not present can be WRITTEN instead of hunted for: the selector reads
/// nothing but sequence labels, activity names and weights, so a model carrying exactly those is
/// a faithful subject for it.
///
/// **What this deliberately cannot do** is stand in for the real merge. Which sequences a real
/// model has, and which of them live in an included animation model rather than the base one, is a
/// fact about TF2's files — `WeaponSlotAnimationTests` is the test for that and it is right to
/// skip without the game. This one covers the decision made once the sequences are known, which is
/// where the fallbacks live and where the wrong answer lies a player on their back.
/// </remarks>
internal static class SyntheticSkinnedModel
{
    /// <summary>Weight given to every activity-bearing sequence built here.</summary>
    /// <remarks>
    /// <c>ForActivity</c> ignores a sequence weighted zero or less, so a fixture that left this at
    /// the record's default would build a model whose every activity is invisible — and the
    /// fallback tests would then pass for the wrong reason, having found nothing at any level.
    /// </remarks>
    private const int Weight = 1;

    /// <summary>Builds a model carrying exactly the named sequences, in order.</summary>
    /// <param name="labels">
    /// Sequence labels, which are what <see cref="PropModels.SkinnedModel.Find"/> matches on.
    /// </param>
    /// <returns>The model.</returns>
    /// <remarks>
    /// Label and activity are set to the same string on purpose. The selector asks by activity
    /// name and the last-resort fallback asks by label, so a fixture that set only one of them
    /// would cover one path and silently skip the other.
    /// </remarks>
    public static PropModels.SkinnedModel With(params string[] labels)
    {
        List<StudioSequence> sequences =
        [
            .. labels.Select((label, index) => new StudioSequence(
                Animation: index,
                Flags: 0,
                Label: label,
                Blend: null,
                Activity: label,
                ActivityWeight: Weight)),
        ];

        List<(int Group, IReadOnlyList<StudioSequence> Sequences)> groups = [(0, sequences)];

        return new PropModels.SkinnedModel(
            Bones: [],
            Models: [[]],
            Sequences: StudioSequenceTable.Merge(groups),
            Groups: groups,
            PoseParameters: [],
            MasterPose: []);
    }

    /// <summary>A model with exactly one bone, for driving the pose path end to end.</summary>
    /// <remarks>
    /// **<see cref="With"/> builds <c>Bones: []</c>**, which is enough for the sequence-selection
    /// tests it was written for and not enough for anything that actually poses. A model with no
    /// bones is refused by <c>SetupBones</c> before it does anything, so a fixture built that way
    /// cannot tell a working pose path from an absent one.
    ///
    /// One bone at the origin with an identity rest pose, so whatever comes out the far end is the
    /// ENTITY's placement and nothing else — which is what makes it possible to assert where a
    /// skinned model ends up.
    /// </remarks>
    public static PropModels.SkinnedModel WithOneBone() => WithBones("root");

    /// <summary>A model with the named bones, all at the origin, all children of the first.</summary>
    /// <param name="names">
    /// Bone names. These decide what MERGES: a name the wearer also has is taken from the wearer, a
    /// name it does not have is built from this model's own placement.
    /// </param>
    /// <remarks>
    /// **The names are the whole point, and a fixture that shares all of them cannot see a merge
    /// failure.** A test giving both models one bone called <c>root</c> passes whether or not the
    /// entity's placement resolves, because the merge supplies the answer either way — measured
    /// 2026-08-24, when exactly that fixture failed to catch weapons drawn at the map origin.
    ///
    /// A real weapon shares two bones of five with its wielder. The three it does not share are the
    /// ones that expose whether the entity was placed, so a fixture for that question needs at
    /// least one unshared name.
    /// </remarks>
    public static PropModels.SkinnedModel WithBones(params string[] names)
    {
        List<StudioBone> bones =
        [
            .. names.Select((name, index) => new StudioBone(
                Name: name,
                Parent: index == 0 ? -1 : 0,
                Position: (0f, 0f, 0f),
                Rotation: (0f, 0f, 0f, 1f),
                PoseToBone: new float[] { 1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f },
                Flags: ~0)),
        ];

        List<(int Group, IReadOnlyList<StudioSequence> Sequences)> groups =
        [
            (0, [new StudioSequence(
                Animation: 0, Flags: 0, Label: "idle", Blend: null,
                Activity: "idle", ActivityWeight: Weight)]),
        ];

        return new PropModels.SkinnedModel(
            Bones: bones,
            Models: [[]],
            Sequences: StudioSequenceTable.Merge(groups),
            Groups: groups,
            PoseParameters: [],
            MasterPose: []);
    }

    /// <summary>One bone, and the pose parameters named — a building rather than a player.</summary>
    /// <param name="parameters">Each one's name, start, end and loop.</param>
    /// <remarks>
    /// **A sentry gun's shape, which is the case the wire's pose parameters exist for.** The
    /// values callers pass match `models/buildables/sentry3.mdl` as the model probe reports it:
    /// `aim_pitch` over −50..50 and `aim_yaw` over −180..180 looping at 360. The symmetric ranges
    /// are load-bearing rather than decorative — an uncomputed parameter normalises to 0.5 there,
    /// so a fixture built this way distinguishes "the wire's value arrived" from "the centre of the
    /// range", which is what a missing value looks like.
    /// </remarks>
    public static PropModels.SkinnedModel WithPoseParameters(
        params StudioPoseParameter[] parameters)
    {
        List<StudioBone> bones =
        [
            new StudioBone(
                Name: "root",
                Parent: -1,
                Position: (0f, 0f, 0f),
                Rotation: (0f, 0f, 0f, 1f),
                PoseToBone: new float[] { 1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f },
                Flags: ~0),
        ];

        List<(int Group, IReadOnlyList<StudioSequence> Sequences)> groups =
        [
            (0, [new StudioSequence(
                Animation: 0, Flags: 0, Label: "idle", Blend: null,
                Activity: "idle", ActivityWeight: Weight)]),
        ];

        return new PropModels.SkinnedModel(
            Bones: bones,
            Models: [[]],
            Sequences: StudioSequenceTable.Merge(groups),
            Groups: groups,
            PoseParameters: parameters,
            MasterPose: [[.. Enumerable.Range(0, parameters.Length)]]);
    }
}
