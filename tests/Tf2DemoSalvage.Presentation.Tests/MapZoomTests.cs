namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>How far the overhead camera is zoomed in, and what a wheel notch does to it.</summary>
/// <remarks>
/// **This was `_zoom`, a bare float, plus `Math.Clamp(_zoom * step, 1f, 64f)` and the recentring
/// formula inside `MainForm.OnViewportWheel`** (B208).
///
/// **Moving it answers nothing about B205**, which asks whether the overhead camera should survive
/// at all now that it is reachable only as a first-person fallback. That is a behaviour question and
/// is still the owner's; this is only about where the arithmetic lives.
/// </remarks>
public sealed class MapZoomTests
{
    [Test]
    public void Of_ATypicalFactor_KeepsIt()
    {
        MapZoom.Of(4f).Factor.ShouldBe(4f);
    }

    [Test]
    public void Of_BeyondEitherEnd_IsClamped()
    {
        // **The constructor is private so this cannot be skipped.** A launch option supplies this
        // factor from the command line, so an arbitrary number reaches it — `--zoom 1000` must not
        // produce a camera inside a wall, and `--zoom -3` must not invert the projection.
        MapZoom.Of(1000f).Factor.ShouldBe(MapZoom.Closest);
        MapZoom.Of(-3f).Factor.ShouldBe(MapZoom.Fitted);
    }

    [Test]
    public void In_FromFitted_MovesOneStepCloser()
    {
        MapZoom.None.In().Factor.ShouldBe(MapZoom.Step, 0.0001);
    }

    [Test]
    public void Out_FromFitted_StaysFitted()
    {
        // Fitted is the whole map; there is nothing further out to go, and letting it past 1 would
        // shrink the map inside the viewport rather than showing more of anything.
        MapZoom.None.Out().Factor.ShouldBe(MapZoom.Fitted);
    }

    [Test]
    public void In_ThenOut_ReturnsToWhereItStarted()
    {
        // **A round trip, which is what makes a wheel feel right.** Multiply-in and divide-out are
        // inverses; step-in and step-out by a fixed ADDITION would not be, and the drift only shows
        // after several notches — by which point it reads as the wheel being imprecise.
        MapZoom started = MapZoom.Of(8f);

        started.In().Out().Factor.ShouldBe(started.Factor, 0.0001);
    }

    [Test]
    public void In_RepeatedlyPastTheLimit_StopsAtTheClosest()
    {
        MapZoom zoom = MapZoom.None;

        for (int notch = 0; notch < 100; notch++)
        {
            zoom = zoom.In();
        }

        zoom.Factor.ShouldBe(MapZoom.Closest);
    }

    [Test]
    public void Recentre_WhenThePointUnderTheCursorMoved_PutsItBack()
    {
        // **This is the whole point of zoom-at-cursor**: whatever was under the pointer before the
        // zoom must still be under it afterwards. The world point drifted 30 east and 10 south, so
        // the camera centre moves by exactly that to cancel it.
        (float X, float Y) centred = MapZoom.Recentre(
            centre: (100f, 200f), before: (150f, 250f), after: (180f, 240f));

        centred.X.ShouldBe(70f, 0.0001);
        centred.Y.ShouldBe(210f, 0.0001);
    }

    [Test]
    public void Recentre_WhenNothingMoved_LeavesTheCentreAlone()
    {
        // **The control, and it catches a sign error the case above cannot.** If the two operands
        // were subtracted the other way round, this still returns the centre — so a test with no
        // movement is necessary but not sufficient, and the asymmetric case above is what decides.
        MapZoom.Recentre(centre: (100f, 200f), before: (150f, 250f), after: (150f, 250f))
            .ShouldBe((100f, 200f));
    }
}
