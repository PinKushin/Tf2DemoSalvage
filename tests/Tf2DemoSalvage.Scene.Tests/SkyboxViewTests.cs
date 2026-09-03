namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The 3D skybox camera transform — <c>CSkyboxView::DrawInternal</c>.
/// </summary>
/// <remarks>
/// **Three lines of engine, and every one of them is a case here** (<c>viewrender.cpp:4886</c>):
/// the division by scale, its guard, and the offset that happens either way. The measured facts
/// they act on are in the `sky-camera` probe: both corpus maps declare `scale 16`, which is
/// `base.fgd`'s default.
/// </remarks>
public sealed class SkyboxViewTests
{
    /// <summary>How close two floats must be to count as equal here.</summary>
    private const double Tolerance = 1e-3;

    [Test]
    public void OriginFor_AtTheMapOrigin_IsTheSkyCameraItself()
    {
        // A viewer at the world origin scales to the world origin, so the sky view stands exactly
        // where the sky_camera does — which is the only position the map's author placed by hand.
        (float x, float y, float z) =
            SkyboxView.OriginFor((0f, 0f, 0f), (9680f, -8672f, 534f), scale: 16f);

        x.ShouldBe(9680f, Tolerance);
        y.ShouldBe(-8672f, Tolerance);
        z.ShouldBe(534f, Tolerance);
    }

    [Test]
    public void OriginFor_WhenTheViewerMoves_MovesASixteenthAsFar()
    {
        // **The whole illusion, as a number.** 1600 units of player movement become 100 units of
        // sky movement at scale 16, which is why distant scenery barely shifts.
        (float near, _, _) = SkyboxView.OriginFor((0f, 0f, 0f), (1000f, 0f, 0f), scale: 16f);
        (float far, _, _) = SkyboxView.OriginFor((1600f, 0f, 0f), (1000f, 0f, 0f), scale: 16f);

        (far - near).ShouldBe(100f, Tolerance);
    }

    /// <remarks>
    /// **The control that makes the case above about the SCALE rather than about arithmetic.** At
    /// scale 1 the sky moves with the player exactly, so a transform that ignored the scale
    /// entirely would pass the first test and fail this one, and a transform that ignored the
    /// viewer would pass neither.
    /// </remarks>
    [Test]
    public void OriginFor_AtScaleOne_MovesWithTheViewer()
    {
        (float x, _, _) = SkyboxView.OriginFor((1600f, 0f, 0f), (1000f, 0f, 0f), scale: 1f);

        x.ShouldBe(2600f, Tolerance);
    }

    /// <remarks>
    /// **`if ( m_pSky3dParams-&gt;scale &gt; 0 )` guards the DIVISION, not the transform.** A zero
    /// scale still receives the offset, which puts the sky room around the player instead of
    /// leaving it where it was built — and skipping the whole transform instead would leave it
    /// where it was built, which is the bug this is all about.
    /// </remarks>
    [Test]
    public void OriginFor_AtScaleZero_StillOffsetsBySkyCamera()
    {
        (float x, _, _) = SkyboxView.OriginFor((1600f, 0f, 0f), (1000f, 0f, 0f), scale: 0f);

        x.ShouldBe(2600f, Tolerance, "the offset applies; only the division is guarded");
    }

    [Test]
    public void FarPlane_IsTheDiagonalOfTheCoordinateSpace()
    {
        // MAX_TRACE_LENGTH = 1.732050807569 * COORD_EXTENT, COORD_EXTENT = 2 * 16384
        // (worldsize.h:19-32). Asserted because a round number in its place would be a departure
        // with nothing behind it.
        SkyboxView.FarPlane.ShouldBe(56755.83f, 0.5d);
        SkyboxView.NearPlane.ShouldBe(2f, Tolerance);
    }
}
