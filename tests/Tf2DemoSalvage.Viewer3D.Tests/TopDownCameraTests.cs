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
    public void TopDownCamera_TheCentreOfTheBounds_LandsInTheMiddleOfTheViewport()
    {
        // The one mapping every other property is measured against.
        TopDownCamera camera = TopDownCamera.Fit(
            [(0f, 0f), (100f, 100f)], viewportWidth: 800, viewportHeight: 800);

        (float x, float y) = camera.Project(50f, 50f);

        x.ShouldBe(0f, tolerance: 0.0001f);
        y.ShouldBe(0f, tolerance: 0.0001f);
    }

    [Test]
    public void TopDownCamera_AWideViewport_DoesNotStretchTheMap()
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
    public void TopDownCamera_EveryFittedPoint_LandsInsideTheViewport()
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
    public void TopDownCamera_WorldY_IncreasesUpTheScreen()
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
    public void TopDownCamera_ASinglePoint_DoesNotDivideByZero()
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
    public void TopDownCamera_NoPoints_GivesAUsableCamera()
    {
        // Before a demo is loaded there is nothing to fit, and the render loop still runs.
        TopDownCamera camera = TopDownCamera.Fit([], 800, 800);

        (float x, float y) = camera.Project(0f, 0f);

        float.IsFinite(x).ShouldBeTrue();
        float.IsFinite(y).ShouldBeTrue();
    }

    [Test]
    public void TopDownCamera_Zoom_ScalesAboutTheCentre()
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

    [Test]
    public void Unproject_TheMiddlePixel_IsTheCameraCentre()
    {
        // **This was `MainForm.WorldAt`** (B208), untested for as long as it lived in a mouse
        // handler. The middle pixel is the anchor: whatever the zoom or the pan, it must be exactly
        // where the camera is looking.
        TopDownCamera camera = TopDownCamera.Fit([(-1000f, -1000f), (1000f, 1000f)], 800, 600);

        (float x, float y) = camera.Unproject(400, 300, 800, 600);

        x.ShouldBe(camera.Centre.X, tolerance: 0.001f);
        y.ShouldBe(camera.Centre.Y, tolerance: 0.001f);
    }

    [Test]
    public void Unproject_APixelBelowTheMiddle_IsSOUTHOfTheCentre()
    {
        // **The sign that is easy to get wrong, asserted on its own.** Screen Y grows downward and
        // world Y grows northward, so a pixel further DOWN the screen must map to a SMALLER world
        // Y. Flipping this mirrors every drag and zoom-at-cursor vertically, which reads as an
        // inverted preference rather than a coordinate error — and so gets "fixed" in the caller.
        TopDownCamera camera = TopDownCamera.Fit([(-1000f, -1000f), (1000f, 1000f)], 800, 600);

        (float _, float lower) = camera.Unproject(400, 500, 800, 600);

        lower.ShouldBeLessThan(camera.Centre.Y);
    }

    [Test]
    public void Unproject_APixelRightOfTheMiddle_IsEASTOfTheCentre()
    {
        // The control for the case above: X is NOT flipped, so right on screen is greater in world
        // X. Without this, a version that negated both axes would satisfy the Y test perfectly.
        TopDownCamera camera = TopDownCamera.Fit([(-1000f, -1000f), (1000f, 1000f)], 800, 600);

        (float right, float _) = camera.Unproject(600, 300, 800, 600);

        right.ShouldBeGreaterThan(camera.Centre.X);
    }

    [Test]
    public void Unproject_ThenProject_ReturnsTheSamePixelAcrossTheViewport()
    {
        // **A round trip, which is the strongest property available here.** `Project` gives
        // normalised device coordinates in [-1, 1]; mapping those back to pixels must land on the
        // pixel we started from. Checked away from the centre, because the centre is the one point
        // a wrong scale still gets right.
        TopDownCamera camera = TopDownCamera.Fit([(-1000f, -1000f), (1000f, 1000f)], 800, 600);

        (float worldX, float worldY) = camera.Unproject(650, 120, 800, 600);
        (float ndcX, float ndcY) = camera.Project(worldX, worldY);

        float backX = ((ndcX + 1f) / 2f) * 800;
        float backY = ((1f - ndcY) / 2f) * 600;

        backX.ShouldBe(650f, tolerance: 0.05f);
        backY.ShouldBe(120f, tolerance: 0.05f);
    }
}
