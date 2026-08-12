using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Tests the mapping from world coordinates to the viewport.
/// </summary>
/// <remarks>
/// **Kept separate from Direct3D on purpose.** The camera is arithmetic, so it can be tested
/// exactly and without a graphics adapter; the renderer's job is then only to draw points it is
/// handed. Putting the fitting logic inside the draw call would make it testable only by looking
/// at a screen, which is the least reliable instrument available.
///
/// Source coordinates are Source engine world units, where a TF2 map spans a few thousand units
/// and the origin is somewhere in the middle - so the camera has to fit arbitrary bounds rather
/// than assume anything about where the map sits.
/// </remarks>
public sealed class TopDownCameraTests
{
    [Test]
    public void TheCentreOfTheBoundsLandsInTheMiddleOfTheViewport()
    {
        // The one mapping every other property is measured against.
        TopDownCamera camera = TopDownCamera.Fit(
            [(0f, 0f), (100f, 100f)], viewportWidth: 800, viewportHeight: 800);

        (float x, float y) = camera.Project(50f, 50f);

        x.ShouldBe(0f, tolerance: 0.0001f);
        y.ShouldBe(0f, tolerance: 0.0001f);
    }

    [Test]
    public void AWideViewportDoesNotStretchTheMap()
    {
        // The failure this prevents is a map that looks fine at one window size and subtly
        // distorted at another - players drifting apart horizontally as the window widens.
        // Equal scale on both axes is what "not stretched" means, so it is asserted directly.
        TopDownCamera camera = TopDownCamera.Fit(
            [(0f, 0f), (100f, 100f)], viewportWidth: 1600, viewportHeight: 800);

        (float rightX, float _) = camera.Project(100f, 50f);
        (float _, float topY) = camera.Project(50f, 100f);

        // A square world in a 2:1 viewport: the vertical extent fills the height, and the
        // horizontal extent uses only half the width because the scale is shared.
        topY.ShouldBe(1f, tolerance: 0.001f);
        rightX.ShouldBe(0.5f, tolerance: 0.001f);
    }

    [Test]
    public void EveryFittedPointLandsInsideTheViewport()
    {
        // Fitting means exactly this. Asserted over a deliberately lopsided set, since a bug in
        // the offset shows up on the axis with the larger extent.
        List<(float X, float Y)> points =
        [
            (-2000f, -300f), (1500f, 250f), (0f, 0f), (-500f, 100f),
        ];

        TopDownCamera camera = TopDownCamera.Fit(points, 1280, 720);

        foreach ((float worldX, float worldY) in points)
        {
            (float x, float y) = camera.Project(worldX, worldY);

            x.ShouldBeInRange(-1.0001f, 1.0001f);
            y.ShouldBeInRange(-1.0001f, 1.0001f);
        }
    }

    [Test]
    public void WorldYIncreasesUpTheScreen()
    {
        // Source's Y axis points north and the screen's points down, so one of them has to be
        // flipped. Getting it wrong mirrors the map, which is easy to miss on a symmetric one -
        // and every competitive TF2 map is close to symmetric.
        TopDownCamera camera = TopDownCamera.Fit([(0f, 0f), (100f, 100f)], 800, 800);

        (float _, float low) = camera.Project(50f, 10f);
        (float _, float high) = camera.Project(50f, 90f);

        high.ShouldBeGreaterThan(low);
    }

    [Test]
    public void ASinglePointDoesNotDivideByZero()
    {
        // A demo's first tick may have one entity, and a zero-extent bound is a division waiting
        // to happen. It must produce a usable camera rather than NaN, which would silently
        // discard every vertex downstream.
        TopDownCamera camera = TopDownCamera.Fit([(512f, 512f)], 800, 800);

        (float x, float y) = camera.Project(512f, 512f);

        float.IsFinite(x).ShouldBeTrue();
        float.IsFinite(y).ShouldBeTrue();
    }

    [Test]
    public void NoPointsGivesAUsableCameraRatherThanThrowing()
    {
        // Before a demo is loaded there is nothing to fit, and the render loop still runs.
        TopDownCamera camera = TopDownCamera.Fit([], 800, 800);

        (float x, float y) = camera.Project(0f, 0f);

        float.IsFinite(x).ShouldBeTrue();
        float.IsFinite(y).ShouldBeTrue();
    }

    [Test]
    public void ZoomScalesAboutTheCentre()
    {
        // Zooming has to keep the point under the cursor's centre fixed, or the view lurches.
        TopDownCamera camera = TopDownCamera.Fit([(0f, 0f), (100f, 100f)], 800, 800)
            .WithZoom(2f);

        (float centreX, float centreY) = camera.Project(50f, 50f);
        (float edgeX, float _) = camera.Project(100f, 50f);

        centreX.ShouldBe(0f, tolerance: 0.0001f);
        centreY.ShouldBe(0f, tolerance: 0.0001f);
        edgeX.ShouldBe(2f, tolerance: 0.001f);
    }
}
