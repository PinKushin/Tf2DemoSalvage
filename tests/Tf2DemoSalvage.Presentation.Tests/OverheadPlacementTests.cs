using System;

using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// The overhead viewpoint that replaces the orthographic camera (D49).
/// </summary>
/// <remarks>
/// **The requirement was always "a top down map view".** An orthographic projection was one reading
/// of it; a perspective camera placed high and pointed down is the other, and gives the same view
/// with one projection instead of two. These tests pin the placement, which is all that reading
/// needs.
/// </remarks>
public sealed class OverheadPlacementTests
{
    [Test]
    public void For_ASquareMap_SitsAboveItsCentre()
    {
        ((float X, float Y, float Z) origin, _, _) =
            OverheadPlacement.For(-1000f, -1000f, 1000f, 1000f, highestGeometry: 0f);

        origin.X.ShouldBe(0f, 0.01);
        origin.Y.ShouldBe(0f, 0.01);
    }

    [Test]
    public void For_AnOffCentreMap_FollowsTheCentreRatherThanTheOrigin()
    {
        // **The control for the test above.** A map whose bounds straddle (0,0) cannot distinguish
        // "centres on the map" from "sits at the world origin", and plenty of Source maps are built
        // well away from it.
        ((float X, float Y, float Z) origin, _, _) =
            OverheadPlacement.For(4000f, 2000f, 6000f, 3000f, highestGeometry: 0f);

        origin.X.ShouldBe(5000f, 0.01);
        origin.Y.ShouldBe(2500f, 0.01);
    }

    [Test]
    public void For_LooksDownButNotStraightDown()
    {
        // **89, not 90, and the one degree is load-bearing.** At exactly vertical the camera basis
        // is degenerate — forward is parallel to world up and there is no right vector. This project
        // has already paid for that: a movement vector that cancelled at pitch 90 left a residue
        // that normalised up to full speed (D65).
        (_, float pitch, float yaw) =
            OverheadPlacement.For(-100f, -100f, 100f, 100f, highestGeometry: 0f);

        pitch.ShouldBe(89f);
        pitch.ShouldBeLessThan(90f, "exactly vertical is degenerate");
        yaw.ShouldBe(0f);
    }

    [Test]
    public void For_ATallMap_ClearsTheGeometryRatherThanStartingInsideIt()
    {
        // **The "not under the map" guarantee**, and the case that motivates it: a wide, flat play
        // area needs little height to frame, so framing alone could place the camera below a tall
        // skybox brush or inside a roof.
        ((float X, float Y, float Z) origin, _, _) =
            OverheadPlacement.For(-200f, -200f, 200f, 200f, highestGeometry: 9000f);

        origin.Z.ShouldBeGreaterThan(9000f, "above the tallest geometry");
        origin.Z.ShouldBe(9000f + OverheadPlacement.ClearanceAboveGeometry, 0.01);
    }

    [Test]
    public void For_ALargeMap_RisesFarEnoughToFrameIt()
    {
        // The other half: on a big map the framing distance dominates and the clearance is
        // irrelevant. Both branches must be exercised or the maximum is untested in one direction.
        ((float X, float Y, float Z) origin, _, _) =
            OverheadPlacement.For(-8000f, -8000f, 8000f, 8000f, highestGeometry: 0f);

        origin.Z.ShouldBeGreaterThan(
            OverheadPlacement.ClearanceAboveGeometry, "framing wins here, not clearance");

        // Half of 16,000 at half of a 75-degree field of view.
        float expected = 8000f / MathF.Tan(37.5f * (MathF.PI / 180f));

        origin.Z.ShouldBe(expected, expected * 0.01);
    }

    [Test]
    public void For_AWideMapOnAWideViewport_FitsTheWidthNotJustTheDepth()
    {
        // **A map is rarely square and a viewport never is.** Fitting the depth alone leaves a wide
        // map cropped left and right — the classic "zoom to fit" mistake, and one that looks like
        // the map being bigger than it is rather than like a framing bug.
        //
        // 20,000 across by 1,000 deep is far wider than a 16:9 viewport can hold at the height that
        // 1,000 of depth would need.
        ((float X, float Y, float Z) wide, _, _) =
            OverheadPlacement.For(-10_000f, -500f, 10_000f, 500f, highestGeometry: 0f);

        ((float X, float Y, float Z) deep, _, _) =
            OverheadPlacement.For(-500f, -500f, 500f, 500f, highestGeometry: 0f);

        wide.Z.ShouldBeGreaterThan(deep.Z * 5f, "the width is what drove the distance");
    }

    [Test]
    public void For_ANarrowerViewport_HasToRiseFurther()
    {
        // Aspect is an input rather than an assumption: the same map framed on a 4:3 window needs
        // more height than on a 16:9 one, because the horizontal half-angle is smaller.
        ((float X, float Y, float Z) wide, _, _) = OverheadPlacement.For(
            -5000f, -100f, 5000f, 100f, highestGeometry: 0f, aspect: 16f / 9f);

        ((float X, float Y, float Z) narrow, _, _) = OverheadPlacement.For(
            -5000f, -100f, 5000f, 100f, highestGeometry: 0f, aspect: 4f / 3f);

        narrow.Z.ShouldBeGreaterThan(wide.Z);
    }

    [Test]
    public void For_ADegenerateFieldOfView_DoesNotPlaceTheCameraAtInfinity()
    {
        // A zero or negative field of view would divide by a zero tangent. The result reads as the
        // map having vanished rather than as a bad argument, so it is guarded rather than trusted.
        foreach (float fov in new[] { 0f, -30f, 180f, 400f })
        {
            ((float X, float Y, float Z) origin, _, _) =
                OverheadPlacement.For(-100f, -100f, 100f, 100f, 0f, fieldOfView: fov);

            float.IsFinite(origin.Z).ShouldBeTrue($"at fov {fov}");
            origin.Z.ShouldBeGreaterThan(0f, $"at fov {fov}");
        }
    }

    [Test]
    public void For_AZeroSizedMap_StillPlacesTheCameraAboveIt()
    {
        // A demo whose map failed to load leaves empty bounds. Placing the camera at height zero
        // would put it exactly on the ground plane, which draws nothing and looks like a broken
        // renderer.
        ((float X, float Y, float Z) origin, _, _) =
            OverheadPlacement.For(0f, 0f, 0f, 0f, highestGeometry: 0f);

        origin.Z.ShouldBe(OverheadPlacement.ClearanceAboveGeometry, 0.01);
    }

    // **`--look` and `--zoom` were parsed and consumed by NOTHING** until 2026-08-29 (B226). D98
    // removed the orthographic camera they were written for and kept the fields, filing what they
    // should mean for a camera placed in the world as "a question for whoever reimplements them".
    // The answer needs no invention: the overhead placement IS the map view's successor, so `--look`
    // moves what it is centred on and `--zoom` changes how much of the map it frames. Both are
    // expressed by reshaping the rectangle handed to `For`, which is why no new placement maths
    // exists here.

    [Test]
    public void Framed_WithNoLookAtAndUnitZoom_IsTheMapUnchanged()
    {
        // **The control, and it is the one that matters most here.** Every launch that passes
        // neither option must frame exactly what it framed before; a `Framed` that shifted or
        // scaled by a hair would change the opening view of every demo and no other test would say
        // so.
        MapBounds framed = OverheadPlacement.Framed(
            new MapBounds(4000f, 2000f, 6000f, 3000f), lookAt: null, zoom: 1f);

        framed.ShouldBe(new MapBounds(4000f, 2000f, 6000f, 3000f));
    }

    [Test]
    public void Framed_WithALookAt_RecentresWithoutChangingTheExtent()
    {
        // Off-centre bounds, so "moved to the look point" and "left where it was" differ — the same
        // reason For_AnOffCentreMap_FollowsTheCentreRatherThanTheOrigin exists.
        MapBounds framed = OverheadPlacement.Framed(
            new MapBounds(4000f, 2000f, 6000f, 3000f), lookAt: (0f, 0f), zoom: 1f);

        framed.ShouldBe(new MapBounds(-1000f, -500f, 1000f, 500f));
    }

    [Test]
    public void Framed_WithZoomTwo_FramesHalfAsMuchAboutTheSameCentre()
    {
        // **Higher zoom frames LESS, which is what zoom means everywhere else.** Stated as an exact
        // rectangle rather than "smaller": a test asserting only that the extent shrank would pass
        // against a factor of 10 as readily as 2.
        MapBounds framed = OverheadPlacement.Framed(
            new MapBounds(-1000f, -1000f, 1000f, 1000f), lookAt: null, zoom: 2f);

        framed.ShouldBe(new MapBounds(-500f, -500f, 500f, 500f));
    }

    [Test]
    public void Framed_WithZoomTwoAndALookAt_AppliesBoth()
    {
        // Both at once, because each alone cannot see the other being dropped.
        MapBounds framed = OverheadPlacement.Framed(
            new MapBounds(-1000f, -1000f, 1000f, 1000f), lookAt: (500f, 200f), zoom: 2f);

        framed.ShouldBe(new MapBounds(0f, -300f, 1000f, 700f));
    }

    [Test]
    public void Framed_WithZoomOfZeroOrLess_IsIgnoredRatherThanCollapsingTheMap()
    {
        // **A zoom of zero divides the extent by nothing and a negative one turns the rectangle
        // inside out.** `--zoom 0` is a typo, and the honest answer to a typo here is the map as it
        // would otherwise be framed — `LaunchOptionsReader` already refuses values that are not
        // numbers, so this guards the ones that parse and are still nonsense.
        MapBounds bounds = new(-1000f, -1000f, 1000f, 1000f);

        OverheadPlacement.Framed(bounds, lookAt: null, zoom: 0f).ShouldBe(bounds);
        OverheadPlacement.Framed(bounds, lookAt: null, zoom: -2f).ShouldBe(bounds);
    }
}
