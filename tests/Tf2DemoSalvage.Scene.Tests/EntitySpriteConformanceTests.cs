using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// <c>C_SpriteRenderer</c> — the basis, the glow blend and the quad an `env_sprite` becomes (B378).
/// </summary>
/// <remarks>
/// **Written from the engine before anything drew a sprite**, which is the order this project keeps
/// for a reason: a conformance test written afterwards describes what was built, and that is the one
/// thing a parity test must never be.
///
/// The three functions, in the order `CSprite::DrawModel` reaches them —
/// <c>GetSpriteAxes</c> (`c_sprite.cpp:226`), <c>StandardGlowBlend</c> (`:147`) and
/// <c>DrawSpriteModel</c> (`:45`).
/// </remarks>
public sealed class EntitySpriteConformanceTests
{
    private static readonly Vector3 ViewRight = new(1f, 0f, 0f);
    private static readonly Vector3 ViewUp = new(0f, 0f, 1f);
    private static readonly Vector3 ViewForward = new(0f, 1f, 0f);

    private static (Vector3 Right, Vector3 Up)? Axes(int orientation, float roll = 0f) =>
        EntitySprites.Axes(
            orientation, new Vector3(100f, 200f, 50f), roll, ViewRight, ViewUp, ViewForward);

    [Test]
    public void Axes_ForAViewplaneParallelSprite_TakesTheViewsOwnRightAndUp()
    {
        Axes(EntitySprites.Parallel).ShouldBe((ViewRight, ViewUp));
    }

    /// <remarks>
    /// **The promotion happens BEFORE the switch and is the easiest line in the function to miss:**
    /// <c>if ( angles[2] != 0 &amp;&amp; type == SPR_VP_PARALLEL ) type = SPR_VP_PARALLEL_ORIENTED;</c>.
    /// A sprite with any roll at all stops being a plain billboard, so an implementation that
    /// switched first would ignore roll entirely and nothing on screen would look obviously wrong.
    ///
    /// Ninety degrees exactly, so the rotated basis is the view's up and its negated right — a
    /// quarter turn is the one angle whose result can be written down rather than computed by the
    /// same formula the code uses.
    /// </remarks>
    [Test]
    public void Axes_ForAParallelSpriteWithRoll_IsPromotedToTheRolledOrientation()
    {
        (Vector3 right, Vector3 up) = Axes(EntitySprites.Parallel, roll: 90f)!.Value;

        right.X.ShouldBe(0f, 0.0001f);
        right.Z.ShouldBe(1f, 0.0001f);

        up.X.ShouldBe(-1f, 0.0001f);
        up.Z.ShouldBe(0f, 0.0001f);
    }

    /// <remarks>
    /// **World up, and a right perpendicular to the view** — <c>up = (0,0,1)</c> with
    /// <c>right = ( forward.y, -forward.x, 0 )</c> normalised. This is the DEFAULT orientation for a
    /// `.vmt` sprite: `CEngineSprite::Init` falls back to `SPR_VP_PARALLEL_UPRIGHT` when the material
    /// declares no `$spriteorientation` (`spritemodel.cpp:309`), and TF2's `light_glow03` declares
    /// none — so this branch, not the plain billboard, is what draws every lamp halo in the game.
    /// </remarks>
    [Test]
    public void Axes_ForTheDefaultOrientation_StandsTheSpriteUprightInTheWorld()
    {
        (Vector3 right, Vector3 up) = Axes(EntitySprites.ParallelUpright)!.Value;

        up.ShouldBe(Vector3.UnitZ);

        right.X.ShouldBe(1f, 0.0001f);
        right.Y.ShouldBe(0f, 0.0001f);
        right.Z.ShouldBe(0f, 0.0001f);
    }

    /// <remarks>
    /// **The engine REFUSES rather than drawing something wrong**, and this is the control that says
    /// the refusal is implemented: *"this will not work if the view direction is very close to
    /// straight up or down, because the cross product will be between two nearly parallel vectors"*,
    /// so `GetSpriteAxes` returns leaving the basis untouched. Looking straight down is not an exotic
    /// case in a demo viewer with a free camera.
    /// </remarks>
    [Test]
    public void Axes_ForTheDefaultOrientationLookingStraightDown_DrawsNothing()
    {
        EntitySprites.Axes(
            EntitySprites.ParallelUpright,
            new Vector3(100f, 200f, 50f),
            roll: 0f,
            ViewRight,
            ViewUp,
            viewForward: new Vector3(0f, 0f, -1f))
            .ShouldBeNull();
    }

    /// <remarks>
    /// **A world glow keeps its world size; every other glow does not.** The branch is
    /// <c>if (rendermode != kRenderWorldGlow) *pscale *= dist * (1.0f/200.0f);</c>, and TF2's lamp
    /// halos are `kRenderWorldGlow` — so getting this backwards would make every glow in the game
    /// swell as the camera retreated. At 600 units a screen-space glow would come back at three times
    /// the scale, which is the number this pins.
    /// </remarks>
    [Test]
    public void GlowBlend_ForAScreenSpaceGlow_GrowsTheScaleWithDistance()
    {
        (float _, float scale) = EntitySprites.GlowBlend(
            RenderModes.Glow, renderFx: 0, brightness: 255, distance: 600f, visible: 1f, scale: 1f);

        scale.ShouldBe(3f, 0.0001f);
    }

    [Test]
    public void GlowBlend_ForAWorldGlow_LeavesTheScaleAlone()
    {
        (float _, float scale) = EntitySprites.GlowBlend(
            RenderModes.WorldGlow, renderFx: 0, brightness: 255, distance: 600f, visible: 1f,
            scale: 1f);

        scale.ShouldBe(1f);
    }

    /// <remarks>
    /// **`(1200*1200)/(dist*dist)`, clamped** — so a glow is at full strength anywhere inside 1,200
    /// units and falls off with the square beyond it. At 2,400 units that is a quarter exactly, which
    /// is the value a wrong exponent or a wrong constant could not also produce.
    /// </remarks>
    [Test]
    public void GlowBlend_AtTwiceTheFullBrightnessDistance_IsAQuarter()
    {
        (float blend, float _) = EntitySprites.GlowBlend(
            RenderModes.WorldGlow, renderFx: 0, brightness: 255, distance: 2400f, visible: 1f,
            scale: 1f);

        blend.ShouldBe(0.25f, 0.0001f);
    }

    [Test]
    public void GlowBlend_InsideTheFullBrightnessDistance_IsClampedToOne()
    {
        (float blend, float _) = EntitySprites.GlowBlend(
            RenderModes.WorldGlow, renderFx: 0, brightness: 255, distance: 100f, visible: 1f,
            scale: 1f);

        blend.ShouldBe(1f);
    }

    /// <remarks>
    /// **`kRenderFxNoDissipation` returns before the distance fade is computed at all**, so such a
    /// glow is its own brightness times its visibility however far away it is. The early return is
    /// what makes it a separate test rather than a value of the one above.
    /// </remarks>
    [Test]
    public void GlowBlend_ForNoDissipation_IgnoresDistanceEntirely()
    {
        (float blend, float _) = EntitySprites.GlowBlend(
            RenderModes.WorldGlow, EntitySprites.NoDissipation, brightness: 51, distance: 100000f,
            visible: 1f, scale: 1f);

        blend.ShouldBe(0.2f, 0.0001f);
    }

    /// <remarks>
    /// **Blocked line of sight draws nothing**, which is `PixelVisibility_FractionVisible`'s
    /// no-query-handle path: <c>GlowSightDistance( position, true ) &gt; 0.0f ? 1.0f : 0.0f</c>, and
    /// `GlowSightDistance` answers −1 when its trace hits anything.
    /// </remarks>
    [Test]
    public void GlowBlend_WhenNothingOfTheGlowIsVisible_IsZero()
    {
        (float blend, float _) = EntitySprites.GlowBlend(
            RenderModes.WorldGlow, renderFx: 0, brightness: 255, distance: 100f, visible: 0f,
            scale: 1f);

        blend.ShouldBe(0f);
    }

    /// <remarks>
    /// **A world-space scale is divided by the material's SMALLER dimension** —
    /// <c>renderscale /= MIN( GetWidth(), GetHeight() )</c>. Using the larger, or using
    /// `GetRenderBounds`' <c>MAX</c> from the other branch, is wrong for any sprite that is not
    /// square: 64 over a 128×256 material is 0.5, where the wrong dimension gives 0.25.
    /// </remarks>
    [Test]
    public void RenderScale_ForAWorldSpaceScale_DividesByTheSmallerDimension()
    {
        EntitySprites.RenderScale(64f, worldSpace: true, width: 128, height: 256)
            .ShouldBe(0.5f);
    }

    [Test]
    public void RenderScale_ForAnOrdinaryScale_IsTheMultiplierUntouched()
    {
        EntitySprites.RenderScale(0.25f, worldSpace: false, width: 128, height: 256)
            .ShouldBe(0.25f);
    }

    /// <remarks>
    /// <c>if ( fscale &gt; 0 ) scale = fscale; else scale = 1.0f;</c> — `DrawSpriteModel`'s first
    /// statement. A sprite that never sent a scale is drawn at its authored size rather than
    /// vanishing, which is the difference between a missing feature and an invisible one.
    /// </remarks>
    [Test]
    public void RenderScale_ForANonPositiveScale_IsOne()
    {
        EntitySprites.RenderScale(0f, worldSpace: false, width: 128, height: 256).ShouldBe(1f);
    }

    /// <remarks>
    /// **The quad spans the material's mapping size, centred**, because `CEngineSprite::Init` falls
    /// back to <c>origin = ( -width * 0.5f, height * 0.5f )</c> when `$spriteorigin` is absent and
    /// then sets up/down/left/right from it. A 64×32 material at scale 2 is therefore 128 by 64 world
    /// units about its origin — and the exact corner is what says the halves were not confused.
    /// </remarks>
    [Test]
    public void Corners_ForACentredSprite_SpanTheMaterialsSizeAboutTheOrigin()
    {
        List<DetailSpriteVertex> corners = [];

        EntitySprites.Corners(
            origin: Vector3.Zero,
            right: ViewRight,
            up: ViewUp,
            width: 64,
            height: 32,
            scale: 2f,
            colour: Vector3.One,
            alpha: 1f,
            corners);

        corners.Count.ShouldBe(6, "two triangles, which is what the renderer draws");

        corners[0].X.ShouldBe(-64f, "half of 64, doubled");
        corners[0].Z.ShouldBe(-32f, "half of 32, doubled");

        corners[2].X.ShouldBe(64f);
        corners[2].Z.ShouldBe(32f);
    }

    /// <remarks>
    /// The colour and alpha reach every corner, since the engine writes one `Color4ubv` per vertex
    /// and the blend has already been multiplied into r/g/b by the time `DrawSpriteModel` runs.
    /// </remarks>
    [Test]
    public void Corners_ForADimmedGlow_CarryTheBlendedColourOnEveryCorner()
    {
        List<DetailSpriteVertex> corners = [];

        EntitySprites.Corners(
            Vector3.Zero, ViewRight, ViewUp, 64, 64, 1f,
            colour: new Vector3(0.25f, 0.5f, 0.75f), alpha: 0.5f, corners);

        foreach (DetailSpriteVertex corner in corners)
        {
            corner.Red.ShouldBe(0.25f);
            corner.Green.ShouldBe(0.5f);
            corner.Blue.ShouldBe(0.75f);
            corner.Alpha.ShouldBe(0.5f);
        }
    }
}
