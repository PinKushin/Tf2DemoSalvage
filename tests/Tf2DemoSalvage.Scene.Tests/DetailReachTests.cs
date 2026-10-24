using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// B580: reaching detail props through the leaf runs near the eye builds exactly what walking every prop builds.
/// </summary>
/// <remarks>
/// The engine visits only the leaves in a sphere of <c>cl_detaildist</c> (<c>detailobjectsystem.cpp:2839-2841</c>).
/// The control is the old whole-list walk (<c>reach: null</c>); the map is a 24 × 24 grid of 64-unit leaves with fast
/// sprites, ordinary sprites and models in each, and leaf 10 split into two runs around a far one, so the skipped run
/// between them would join their half groups of four if the run start were not a group restart. Many views stand near
/// leaf 10 so that case is exercised.
/// </remarks>
public sealed class DetailReachTests
{
    private const int Grid = 24;
    private const float Cell = 64f;

    private static readonly BspDetailSprite Rectangle = new((-8f, 20f), (8f, 0f), (0f, 0f), (0.5f, 0.5f));

    [Test]
    public void Build_ReachedThroughNearRuns_EmitsTheWholeWalksCornersAndModels()
    {
        IReadOnlyList<BspDetailProp> props = Map();
        DetailReach reach = new(props, [Rectangle]);
        DetailFade fade = DetailFade.For(distance: 300f, fade: 100f);
        int skipped = 0;

        for (int view = 0; view < 200; view++)
        {
            // A golden-ratio scatter: deterministic, and every view somewhere new.
            (float X, float Y, float Z) eye = (
                (float)(Fraction(view * 0.618034) * Grid * Cell),
                (float)(Fraction(view * 0.754878) * Grid * Cell),
                (float)(Fraction(view * 0.569840) * 64));

            if (view % 4 == 0)
            {
                eye = ((10 * Cell) + (eye.X % Cell), eye.Y % Cell, eye.Z);
            }
            double yaw = Fraction(view * 0.414214) * Math.Tau;
            (float X, float Y, float Z) forward = ((float)Math.Cos(yaw), (float)Math.Sin(yaw), 0f);

            List<DetailSpriteVertex> whole = [];
            List<DetailSpriteVertex> near = [];
            DetailSprites.Frame wholeFrame = DetailSprites.Build(props, [Rectangle], eye, fade, whole, forward: forward);
            DetailSprites.Frame nearFrame = DetailSprites.Build(
                props, [Rectangle], eye, fade, near, forward: forward, reach: reach);

            near.ShouldBe(whole, $"view {view} at {eye}");
            (nearFrame.Built, nearFrame.Aligned, nearFrame.Fast)
                .ShouldBe((wholeFrame.Built, wholeFrame.Aligned, wholeFrame.Fast));

            List<DetailModelInstance> wholeModels = [];
            List<DetailModelInstance> nearModels = [];
            DetailModels.Build(props, eye, fade, wholeModels);
            DetailModels.Build(props, eye, fade, nearModels, reach);
            nearModels.ShouldBe(wholeModels, $"view {view} at {eye}");

            for (int run = 0; run < reach.Runs; run++)
            {
                skipped += reach.Near(run, eye, fade.MaximumSquared) ? 0 : 1;
            }

            whole.Count.ShouldBeGreaterThan(0, $"view {view}: the control must build something");
        }

        // The reach must actually skip: a 300-unit sphere touches at most ~100 of the 577 runs.
        reach.Runs.ShouldBe((Grid * Grid) + 2);
        skipped.ShouldBeGreaterThan(200 * 400);
    }

    private static double Fraction(double value) => value - Math.Floor(value);

    private static List<BspDetailProp> Map()
    {
        List<BspDetailProp> props = [];

        for (int leaf = 0; leaf < Grid * Grid; leaf++)
        {
            float x = (leaf % Grid) * Cell;
            float y = (leaf / Grid) * Cell;

            for (int item = 0; item < 7; item++)
            {
                props.Add(Prop(leaf, x + (item * 9f), y + (item * 7f), item));
            }

            // Leaf 10 ends on half a group of four, then a far leaf, then leaf 10 again with the other half: two runs of
            // one leaf with a skipped run between, which must not become one group.
            if (leaf == 10)
            {
                props.Add(Prop(10, x + 40f, y + 50f, 0));
                props.Add(Prop(10, x - 30f, y + 20f, 0));
                props.Add(Prop(Grid * Grid, Grid * Cell * 3f, Grid * Cell * 3f, 0));
                props.Add(Prop(10, x + 20f, y - 40f, 0));
                props.Add(Prop(10, x - 50f, y - 10f, 0));
            }
        }

        return props;
    }

    private static BspDetailProp Prop(int leaf, float x, float y, int item) =>
        new(
            Origin: (x, y, 0f),
            Angles: (0f, item * 40f, 0f),
            DetailModel: 0,
            Leaf: leaf,
            Type: item == 6 ? DetailPropType.Model : DetailPropType.Sprite,
            Orientation: item < 4 ? 2 : item - 4,
            SwayAmount: 0,
            ShapeAngle: 0,
            ShapeSize: 0,
            Scale: 1f,
            Lighting: (100f, 120f, 140f),
            Flipped: item % 2 == 1);
}
