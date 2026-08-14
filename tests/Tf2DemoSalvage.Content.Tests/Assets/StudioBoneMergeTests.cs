using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// Posing a worn model's bones from the bones of whoever wears it.
/// </summary>
/// <remarks>
/// **Only the matching bones are copied, and the rest are NOT left where they were.** Valve's
/// <c>CBoneMergeCache::MergeMatchingBones</c> runs after the worn model has already done its own
/// full <c>SetupBones</c>, so an unmatched bone holds a position built by walking the worn model's
/// own hierarchy — from its parent, which may itself have been merged.
///
/// Getting that wrong does not hide the item, it tears it: measured in the viewer, a
/// <c>ghostly_gibus</c> matched 1 bone of 8 on a scout, the other seven stayed at the model origin
/// while the matched one sat at head height, and the triangles between them stretched from the
/// player's head to their feet as a large flat sheet.
/// </remarks>
public sealed class StudioBoneMergeTests
{
    [Test]
    public void AMatchedBone_TakesTheWearersPlace()
    {
        // The wearer's head is four feet up. A hat merged onto it belongs there too.
        IReadOnlyList<float[]> merged = StudioBones.MergeOnto(
            [Bone("bip_head", parent: -1, x: 0f)],
            [At(0f, 0f, 48f)],
            [0]);

        Translation(merged[0]).ShouldBe((0f, 0f, 48f));
    }

    [Test]
    public void AnUnmatchedChildBone_FollowsItsMergedParent()
    {
        // **The case that tore the models.** A hat whose brim the player has no bone for: the brim
        // must ride ten units in front of the merged head, not sit at the model origin.
        IReadOnlyList<float[]> merged = StudioBones.MergeOnto(
            [
                Bone("bip_head", parent: -1, x: 0f),
                Bone("hat_brim", parent: 0, x: 10f),
            ],
            [At(0f, 0f, 48f)],
            [0, -1]);

        Translation(merged[0]).ShouldBe((0f, 0f, 48f));

        // Ten along the merged parent, which is at head height — NOT (10, 0, 0) at the feet.
        Translation(merged[1]).ShouldBe((10f, 0f, 48f));
    }

    [Test]
    public void AnUnmatchedRootBone_KeepsItsOwnPlace()
    {
        // **The control.** With no merged parent to inherit from there is nothing to follow, so a
        // root bone the wearer does not have stays where the model puts it. A rule that dragged
        // every unmatched bone to the wearer's origin would pass the test above and collapse any
        // item whose root is unmatched.
        IReadOnlyList<float[]> merged = StudioBones.MergeOnto(
            [Bone("odd_root", parent: -1, x: 7f)],
            [At(0f, 0f, 48f)],
            [-1]);

        Translation(merged[0]).ShouldBe((7f, 0f, 0f));
    }

    /// <summary>A bone at an offset along x, with an identity bind pose.</summary>
    /// <remarks>
    /// The pose-to-bone matrix is the identity, so a skinning matrix here is the bone's own place
    /// and the assertions can read translations directly rather than through a bind pose.
    /// </remarks>
    private static StudioBone Bone(string name, int parent, float x) =>
        new(name, parent, (x, 0f, 0f), (0f, 0f, 0f, 1f), Identity());

    /// <summary>A wearer bone standing at one point, unrotated.</summary>
    private static float[] At(float x, float y, float z)
    {
        float[] matrix = Identity();

        (matrix[3], matrix[7], matrix[11]) = (x, y, z);

        return matrix;
    }

    private static float[] Identity() =>
        [1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f];

    /// <summary>Where a row-major three-by-four matrix puts the origin.</summary>
    private static (float X, float Y, float Z) Translation(float[] matrix) =>
        (matrix[3], matrix[7], matrix[11]);
}
