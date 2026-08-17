using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// A forward-running player selects the forward corner of the run's blend grid.
/// </summary>
/// <remarks>
/// **B101, measured on a real model rather than on a fixture.** Every moving player ran backwards.
/// The cause was not the pose parameter — a POV recording of deliberate forward running gives
/// <c>move_x = 1.000, move_y = -0.000</c> at every sampled tick, which is exactly right — and not
/// the grid arithmetic, which matches <c>Studio_LocalPoseParameter</c> including its <c>&gt; 2</c>
/// group test.
///
/// It was the pose parameter LIST. <c>scout.mdl</c> declares two parameters, <c>body_pitch</c> and
/// <c>body_yaw</c>; <c>move_x</c> and <c>move_y</c> exist only in <c>scout_animations.mdl</c>, which
/// it includes. The run sequence's <c>paramindex</c> is local to that included group and asks for
/// index 5, so reading it against the base model's two-entry list fell out of bounds, returned cell
/// zero with a setting of zero on both axes, and took the grid corner at
/// <c>move_x = −1, move_y = −1</c> — the backward-left run, for everyone, always.
///
/// **Nothing could report that.** Falling off the end of a list is a legitimate answer for a model
/// that has no such parameter, and cell zero is a real cell. The engine's own guard is
/// <c>CStudioHdr::GetSharedPoseParameter</c>, which translates through
/// <c>virtualgroup_t::masterPose</c> — and Valve's comment there is that returning the untranslated
/// index "is not correct, this should return -1 because otherwise it's just some random unrelated
/// index".
///
/// **Asserted as a difference between two directions rather than as one cell number**, because a
/// cell number is a property of how the model happens to be authored while "forward and backward
/// are not the same animation" is the actual claim. The broken code answered identically for both.
///
/// **What this test does NOT cover, checked by sabotage rather than assumed.** Replacing the
/// <c>masterPose</c> translation in <c>Locate</c> with the untranslated local index leaves this
/// test GREEN. For a player model the base model's parameters are a prefix of the animation
/// model's, so the map comes out as the identity and the translation cannot be observed here. The
/// fix this test measures is the merged LIST; the translation is measured in
/// <c>StudioPoseParameterMergeTests</c>, where the two orders genuinely differ. Both are needed and
/// neither substitutes for the other — a model whose animations declare parameters the base model
/// also declares in a different order would break with the translation removed and nothing here
/// would notice.
/// </remarks>
public sealed class PlayerMoveBlendTests
{
    [Test]
    public void ForwardAndBackwardRunning_SelectDifferentAnimations()
    {
        if (Environment.GetEnvironmentVariable("TF2_FOLDER") is not { Length: > 0 } folder ||
            !Directory.Exists(folder))
        {
            Assert.Ignore("Team Fortress 2 is not installed; set TF2_FOLDER to run this.");
            return;
        }

        GameArchives archives = GameArchives.Open(folder);

        if (archives.Read("models/player/scout.mdl") is not { } baseFile)
        {
            Assert.Ignore("scout.mdl not found in the install.");
            return;
        }

        List<byte[]> models = [baseFile];
        List<(int Group, IReadOnlyList<StudioSequence> Sequences)> groups =
            [(0, StudioSequences.Read(baseFile))];

        foreach (string include in StudioModelGroups.Read(baseFile))
        {
            if (archives.Read(include) is { } included)
            {
                groups.Add((models.Count, StudioSequences.Read(included)));
                models.Add(included);
            }
        }

        (IReadOnlyList<StudioPoseParameter> shared, IReadOnlyList<IReadOnlyList<int>> masterPose) =
            StudioPoseParameterMerge.Merge(
                [.. models.Select(file => StudioSequences.PoseParameters(file))]);

        // **The bug, stated directly.** Before the merge this list held two entries and neither was
        // a movement parameter, so everything below was reading a list that could not answer.
        shared.Select(parameter => parameter.Name).ShouldContain("move_x");
        shared.Select(parameter => parameter.Name).ShouldContain("move_y");

        (int group, StudioSequence run) = FindRun(groups);

        run.Blend.ShouldNotBeNull("run_PRIMARY blends a grid of directions");

        StudioBlendGrid grid = run.Blend;

        int forward = Selected(grid, shared, masterPose[group], moveX: 1f);
        int backward = Selected(grid, shared, masterPose[group], moveX: -1f);

        forward.ShouldNotBe(
            backward,
            "running forward and running backwards must not resolve to the same animation; " +
            "they did, because the movement parameters were not in the list being indexed");

        // And the direction, not merely a difference: the grid's second axis is move_x running
        // −1 to 1, so forward is the LAST row and backward the first. A pair that differed but was
        // swapped would satisfy the assertion above and still animate backwards.
        int rows = grid.GroupY;

        grid.Animation(1, rows - 1).ShouldBe(forward, "forward is the top of the move_x axis");
        grid.Animation(1, 0).ShouldBe(backward, "backward is the bottom of it");
    }

    /// <summary>The cell a given <c>move_x</c> selects, with <c>move_y</c> centred.</summary>
    private static int Selected(
        StudioBlendGrid grid,
        IReadOnlyList<StudioPoseParameter> parameters,
        IReadOnlyList<int> masterPose,
        float moveX)
    {
        float[] values = new float[parameters.Count];

        for (int index = 0; index < parameters.Count; index++)
        {
            float raw = parameters[index].Name switch
            {
                "move_x" => moveX,
                _ => 0f,
            };

            values[index] = StudioBlendGrid.Normalize(parameters[index], raw);
        }

        (int x, float alongX) = grid.Locate(0, parameters, values, masterPose);
        (int y, float alongY) = grid.Locate(1, parameters, values, masterPose);

        // The dominant corner: whichever end of each axis the setting has reached.
        return grid.Animation(
            alongX >= 0.5f ? x + 1 : x,
            alongY >= 0.5f ? y + 1 : y);
    }

    /// <summary>The group and sequence of <c>run_PRIMARY</c>.</summary>
    private static (int Group, StudioSequence Run) FindRun(
        IReadOnlyList<(int Group, IReadOnlyList<StudioSequence> Sequences)> groups)
    {
        foreach ((int group, IReadOnlyList<StudioSequence> sequences) in groups)
        {
            foreach (StudioSequence sequence in sequences)
            {
                if (sequence.Activity == "ACT_MP_RUN_PRIMARY" && sequence.Blend is { Blends: true })
                {
                    return (group, sequence);
                }
            }
        }

        throw new InvalidDataException("no ACT_MP_RUN_PRIMARY with a blend grid was found");
    }
}
