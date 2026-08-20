using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Viewer3D.Tests;

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
}
