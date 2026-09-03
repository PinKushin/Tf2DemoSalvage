using Tf2DemoSalvage.Content.Bsp;

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

    /// <remarks>
    /// **`r_3dsky` is an INT with three meanings and a bool would silently delete the third.**
    /// `PreRender3dSkyboxWorld` (<c>viewrender.cpp:4843</c>) tests it twice, in this order:
    ///
    /// <code>
    ///   if ( ( nSkyboxVisible != SKYBOX_3DSKYBOX_VISIBLE ) &amp;&amp; r_3dsky.GetInt() != 2 ) return NULL;
    ///   if ( !r_3dsky.GetInt() ) return NULL;
    ///   ...
    ///   if ( local-&gt;m_skybox3d.area == 255 ) return NULL;
    /// </code>
    ///
    /// The third state matters more here than in TF2: this viewer has a free camera that can stand
    /// where the map never expected, and `2` is how the room is seen from there.
    /// </remarks>
    [Test]
    public void Draws_AtOne_NeedsAThreeDimensionalSkyInView()
    {
        SkyboxView.Draws(1, SkyboxVisibility.ThreeDimensional, skyArea: 1).ShouldBeTrue();

        SkyboxView.Draws(1, SkyboxVisibility.TwoDimensional, skyArea: 1).ShouldBeFalse(
            "SURF_SKY2D skylights and draws the flat sky but explicitly not the 3D room");

        SkyboxView.Draws(1, SkyboxVisibility.None, skyArea: 1).ShouldBeFalse();
    }

    [Test]
    public void Draws_AtTwo_DrawsEvenWithNoSkyInView()
    {
        // The state a bool cannot express, and the one a free camera outside the level needs.
        SkyboxView.Draws(2, SkyboxVisibility.None, skyArea: 1).ShouldBeTrue();
        SkyboxView.Draws(2, SkyboxVisibility.TwoDimensional, skyArea: 1).ShouldBeTrue();
    }

    [Test]
    public void Draws_AtZero_DrawsNothingHowevrVisibleTheSkyIs()
    {
        // The second test in Valve's order, and it comes AFTER the visibility one — so zero has to
        // beat a fully visible 3D sky rather than merely agreeing with an invisible one.
        SkyboxView.Draws(0, SkyboxVisibility.ThreeDimensional, skyArea: 1).ShouldBeFalse();
    }

    [Test]
    public void Draws_ForAMapWithNoSkyCamera_DrawsNothingAtAnySetting()
    {
        // `if ( local->m_skybox3d.area == 255 ) return NULL;` — Valve's byte sentinel for "this map
        // has no 3D sky", checked last. Ours is -1, and it has to beat r_3dsky 2 as well.
        SkyboxView.Draws(1, SkyboxVisibility.ThreeDimensional, skyArea: -1).ShouldBeFalse();
        SkyboxView.Draws(2, SkyboxVisibility.ThreeDimensional, skyArea: -1).ShouldBeFalse();
    }

    [Test]
    public void DrawsByDefault_IsOneAndNotCheatGated()
    {
        // `static ConVar r_3dsky( "r_3dsky","1", 0, ... )` — the 0 is the FLAGS argument, where
        // r_skybox on the next line carries FCVAR_CHEAT. Turning the 3D sky off is something a
        // player may do in a real game.
        SkyboxView.DrawsByDefault.ShouldBe(1);
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
