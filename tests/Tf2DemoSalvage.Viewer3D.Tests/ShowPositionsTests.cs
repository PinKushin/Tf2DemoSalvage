using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Tests the step between decoded world positions and what the viewport draws.
/// </summary>
/// <remarks>
/// The two halves are already covered on their own — <see cref="TopDownCameraTests"/> pins the
/// projection arithmetic and <see cref="PointRendererTests"/> pins what a clip-space point draws
/// as. What is left is the join: that the form actually pushes one into the other, and does not
/// quietly hand the renderer world coordinates in the thousands.
/// </remarks>
public sealed class ShowPositionsTests
{
    [Test]
    public void ShowPositions_WorldPositions_ArriveInClipSpace()
    {
        // Source units are in the thousands. Passing them through unprojected would put every
        // point far outside the [-1, 1] the rasteriser keeps, so the viewport would be empty and
        // nothing would say why.
        using MainForm form = new();

        form.ShowPositions([(-2000f, -1500f), (2000f, 1500f), (0f, 0f)]);

        form.Scene.Count.ShouldBe(3);

        foreach (ScenePoint point in form.Scene)
        {
            point.X.ShouldBeInRange(-1.0001f, 1.0001f);
            point.Y.ShouldBeInRange(-1.0001f, 1.0001f);
        }
    }

    [Test]
    public void ShowPositions_TheMiddleOfTheWorld_LandsInTheMiddleOfTheView()
    {
        // Three positions where the third is exactly the centre of the other two, so its
        // projection has a known answer: the origin of clip space.
        //
        // The first version of this test asserted against the LAST point - which is a corner, not
        // the middle - and expected -1. It failed for the right reason, which is the only useful
        // thing about it: the expectation was wrong, not the code.
        using MainForm form = new();

        form.ShowPositions([(0f, 0f), (100f, 100f), (50f, 50f)]);

        ScenePoint middle = form.Scene[2];

        middle.X.ShouldBe(0f, tolerance: 0.0001f);
        middle.Y.ShouldBe(0f, tolerance: 0.0001f);
    }

    [Test]
    public void ShowPositions_ShowingNothing_ClearsTheScene()
    {
        // Scrubbing to a tick before anyone has spawned is an ordinary event, not an error.
        using MainForm form = new();
        form.ShowPositions([(0f, 0f)]);

        form.ShowPositions([]);

        form.Scene.ShouldBeEmpty();
    }

    [Test]
    public void ShowPositions_EveryPoint_IsGivenAVisibleColour()
    {
        // A point drawn in the clear colour is invisible, and would look exactly like a renderer
        // that drew nothing at all.
        using MainForm form = new();

        form.ShowPositions([(0f, 0f), (10f, 10f)]);

        form.Scene.ShouldAllBe(p => p.Red > 0.5f || p.Green > 0.5f || p.Blue > 0.5f);
    }
}
