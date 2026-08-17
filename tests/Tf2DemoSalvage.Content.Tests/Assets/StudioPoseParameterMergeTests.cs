using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// Merging every group's pose parameters into one shared list, the way a virtual model does.
/// </summary>
/// <remarks>
/// **This is why every player ran backwards (B101).** A player model declares almost no pose
/// parameters of its own — <c>scout.mdl</c> has exactly two, <c>body_pitch</c> and <c>body_yaw</c> —
/// while <c>move_x</c> and <c>move_y</c> live in the animation model it INCLUDES. A sequence's
/// <c>paramindex</c> is local to the group that owns the sequence, so reading it against the base
/// model's list asks for index 5 of a list with two entries.
///
/// The result was not an error. It fell out of bounds, returned cell zero with a setting of zero on
/// both axes, and selected the corner of the run's blend grid at <c>move_x = −1, move_y = −1</c> —
/// the backward-left run, played by every moving player forever.
///
/// **The engine's own answer is `CVirtualModel::AppendPoseParameters`**
/// (<c>studio_virtualmodel.cpp:445</c>), and it does three things this asserts:
///
/// <code>
/// char *s1 = pStudioHdr->pLocalPoseParameter( j )->pszName();
/// for (k = 0; k &lt; numCheck; k++) { ... if (stricmp( s1, s2 ) == 0) break; }
/// if (k == numCheck) { ... k = pose.AddToTail( tmp ); }
/// else { // duplicate, reset start and end to fit full dynamic range
///     float start = min( pPose2->end, min( pPose1->end, min( pPose2->start, pPose1->start ) ) );
///     float end   = max( pPose2->end, max( pPose1->end, max( pPose2->start, pPose1->start ) ) ); }
/// m_group[ group ].masterPose[ j ] = k;
/// </code>
///
/// Matching is **by name and case-insensitive**, a duplicate **widens the shared range** rather than
/// being ignored, and each group keeps a map from its own index to the shared one. The widening is
/// not academic here: <c>body_pitch</c> is −45..45 in <c>scout.mdl</c> and −45..90 in
/// <c>scout_animations.mdl</c>, so the shared parameter must span −45..90 or every pitch is
/// normalised against the wrong denominator.
/// </remarks>
public sealed class StudioPoseParameterMergeTests
{
    [Test]
    public void ParametersOnlyTheIncludedModelDeclares_ReachTheSharedList()
    {
        // The real shape: a base model with two, an animation model with six, four of them new.
        (IReadOnlyList<StudioPoseParameter> shared, IReadOnlyList<IReadOnlyList<int>> map) =
            StudioPoseParameterMerge.Merge(
            [
                [Parameter("body_pitch", -45f, 45f), Parameter("body_yaw", -45f, 45f)],
                [
                    Parameter("body_pitch", -45f, 90f),
                    Parameter("body_yaw", -45f, 45f),
                    Parameter("r_hand_grip", 0f, 16f),
                    Parameter("r_arm", 0f, 3f),
                    Parameter("move_x", -1f, 1f),
                    Parameter("move_y", -1f, 1f),
                ],
            ]);

        shared.Count.ShouldBe(6);

        // Discovery order, which is what makes the shared index stable across groups.
        shared[0].Name.ShouldBe("body_pitch");
        shared[4].Name.ShouldBe("move_x");
        shared[5].Name.ShouldBe("move_y");

        // The animation model's local index 4 is move_x, and it must map to shared index 4 — which
        // it does here only because the base model contributed the two the animation model repeats.
        map[1][4].ShouldBe(4);
        map[1][5].ShouldBe(5);

        // The base model's own two map to themselves.
        map[0][0].ShouldBe(0);
        map[0][1].ShouldBe(1);
    }

    [Test]
    public void ADuplicateWidensTheSharedRange()
    {
        // The measured case: -45..45 in the base, -45..90 in the animations.
        (IReadOnlyList<StudioPoseParameter> shared, _) = StudioPoseParameterMerge.Merge(
        [
            [Parameter("body_pitch", -45f, 45f)],
            [Parameter("body_pitch", -45f, 90f)],
        ]);

        shared.Count.ShouldBe(1, "a duplicate name is one shared parameter, not two");

        // Not the first seen and not the last: the min and max across all four endpoints.
        shared[0].Start.ShouldBe(-45f);
        shared[0].End.ShouldBe(90f);
    }

    [Test]
    public void TheWideningTakesTheExtremeOfAllFourEndpoints()
    {
        // Valve nests min/max over start AND end of both, which is not the same as taking the
        // wider of the two ranges when one is inverted. A parameter authored end-before-start is
        // rare but legal, and this is the case that separates the two implementations.
        (IReadOnlyList<StudioPoseParameter> shared, _) = StudioPoseParameterMerge.Merge(
        [
            [Parameter("odd", 10f, -10f)],
            [Parameter("odd", -2f, 30f)],
        ]);

        shared[0].Start.ShouldBe(-10f);
        shared[0].End.ShouldBe(30f);
    }

    [Test]
    public void MatchingIsCaseInsensitive()
    {
        // stricmp, so a model that spells it Move_X shares the slot rather than adding a second one
        // that nothing writes to.
        (IReadOnlyList<StudioPoseParameter> shared, IReadOnlyList<IReadOnlyList<int>> map) =
            StudioPoseParameterMerge.Merge(
            [
                [Parameter("move_x", -1f, 1f)],
                [Parameter("MOVE_X", -1f, 1f)],
            ]);

        shared.Count.ShouldBe(1);
        map[1][0].ShouldBe(0);
    }

    [Test]
    public void AGroupThatDeclaresNothing_GetsAnEmptyMap()
    {
        // Rather than a missing one: the caller indexes the map by group number, so every group
        // needs an entry or the numbering shifts under it.
        (_, IReadOnlyList<IReadOnlyList<int>> map) = StudioPoseParameterMerge.Merge(
        [
            [Parameter("move_x", -1f, 1f)],
            [],
        ]);

        map.Count.ShouldBe(2);
        map[1].ShouldBeEmpty();
    }

    private static StudioPoseParameter Parameter(string name, float start, float end) =>
        new(name, start, end, 0f);
}
