using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// Picking a point in a sequence's blend grid, and which animations it mixes.
/// </summary>
/// <remarks>
/// A sequence names a grid of animations and two pose parameters are its coordinates. Taking the
/// corner instead — which is what the engine itself does when every parameter is zero — is correct
/// for a prop and wrong for a player: a nine-way movement blend's corner is one fixed direction, so
/// the legs run that way whatever the body is doing.
/// </remarks>
public sealed class StudioBlendGridTests
{
    /// <summary>A three-by-three grid on two parameters spanning −1 to 1, as a player's is.</summary>
    private static StudioBlendGrid Movement() =>
        new(3, 3, [0, 1, 2, 3, 4, 5, 6, 7, 8], 0, 1, -1f, 1f, -1f, 1f);

    private static IReadOnlyList<StudioPoseParameter> Parameters() =>
        [new("move_x", -1f, 1f, 0f), new("move_y", -1f, 1f, 0f)];

    /// <summary>
    /// A group whose own parameter order is already the shared one, so no translation happens.
    /// </summary>
    /// <remarks>
    /// These tests are about the grid arithmetic, and a one-group model genuinely has an identity
    /// <c>masterPose</c>. The translation itself is measured in
    /// <see cref="StudioPoseParameterMergeTests"/>, where the two lists differ — which is the only
    /// condition under which it can be wrong.
    /// </remarks>
    private static readonly int[] Identity = [0, 1];

    /// <summary>The parameters as the engine stores them: normalised to zero-to-one.</summary>
    private static float[] Stored(float moveX, float moveY) =>
    [
        StudioBlendGrid.Normalize(Parameters()[0], moveX),
        StudioBlendGrid.Normalize(Parameters()[1], moveY),
    ];

    [Test]
    public void RunningStraightForward_LandsAtTheTopOfTheFirstAxis()
    {
        // move_x = 1 is straight forward, which is the far end of a range running −1 to 1. Valve
        // steps the index back one at the top so the caller can always read index + 1, so the
        // answer is the LAST gap fully traversed rather than a cell past the end.
        StudioBlendGrid grid = Movement();

        (int index, float setting) = grid.Locate(0, Parameters(), Stored(1f, 0f), Identity);

        index.ShouldBe(1);
        setting.ShouldBe(1f, 1e-5f);
    }

    [Test]
    public void BackpedallingLandsAtTheBottom()
    {
        // **The control for the one above.** A mapping that ignored the value would answer the
        // same for both ends, and running backwards is exactly the case the owner reported.
        StudioBlendGrid grid = Movement();

        (int index, float setting) = grid.Locate(0, Parameters(), Stored(-1f, 0f), Identity);

        index.ShouldBe(0);
        setting.ShouldBe(0f, 1e-5f);
    }

    [Test]
    public void StandingStill_LandsInTheMiddle()
    {
        StudioBlendGrid grid = Movement();

        (int index, float setting) = grid.Locate(0, Parameters(), Stored(0f, 0f), Identity);

        // Halfway along a two-gap axis is the start of the second gap.
        index.ShouldBe(1);
        setting.ShouldBe(0f, 1e-5f);
    }

    [Test]
    public void AnAxisWithNoParameter_StaysAtTheStart()
    {
        // A sequence that blends on one axis only has −1 for the other's paramindex. Reading a
        // parameter that is not there would index off the end of the model's list.
        StudioBlendGrid grid = new(3, 1, [0, 1, 2], 0, -1, -1f, 1f, 0f, 0f);

        grid.Locate(1, Parameters(), Stored(1f, 1f), Identity).ShouldBe((0, 0f));
    }

    [Test]
    public void TheGridClampsRatherThanRunningOff()
    {
        // Valve's mstudioseqdesc_t::anim clamps both coordinates, which matters because the blend
        // arithmetic reaches index + 1 at the top of each axis by design.
        StudioBlendGrid grid = Movement();

        grid.Animation(5, 5).ShouldBe(8);
        grid.Animation(-3, 0).ShouldBe(0);
    }

    [Test]
    public void TheThreeWeightsAlwaysSumToOne()
    {
        // **The property that has to hold everywhere, so it is checked everywhere.** A blend whose
        // weights do not sum to one scales the whole pose: bones shrink toward the origin, which
        // reads as a model deforming rather than as arithmetic.
        StudioBlendGrid grid = Movement();

        for (int x = 0; x < 2; x++)
        {
            for (int y = 0; y < 2; y++)
            {
                foreach (float sx in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
                {
                    foreach (float sy in new[] { 0f, 0.3f, 0.6f, 1f })
                    {
                        (int[] _, float[] weights) = grid.ThreeWay(x, y, sx, sy);

                        (weights[0] + weights[1] + weights[2]).ShouldBe(1f, 1e-5f);

                        weights[0].ShouldBeGreaterThanOrEqualTo(-1e-5f);
                        weights[1].ShouldBeGreaterThanOrEqualTo(-1e-5f);
                        weights[2].ShouldBeGreaterThanOrEqualTo(-1e-5f);
                    }
                }
            }
        }
    }

    [Test]
    public void AtACornerTheWholeWeightGoesToThatCorner()
    {
        // The decisive case: at (0,0) of an even cell the point IS the first corner, so nothing
        // else may contribute. A blend that spread weight here would mix in a neighbouring
        // direction permanently.
        StudioBlendGrid grid = Movement();

        (int[] animations, float[] weights) = grid.ThreeWay(0, 0, 0f, 0f);

        int at = Array.IndexOf(animations, grid.Animation(0, 0));

        at.ShouldBeGreaterThanOrEqualTo(0);
        weights[at].ShouldBe(1f, 1e-5f);
    }
}
