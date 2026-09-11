using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// <c>C_SpriteRenderer</c> — the basis, the glow blend and the quad an `env_sprite` becomes (B378) —
/// and <c>CEngineSprite</c>'s edges, which place that quad about its origin (B390).
/// </summary>
/// <remarks>
/// **Written from the engine before anything drew a sprite**, which is the order this project keeps
/// for a reason: a conformance test written afterwards describes what was built, and that is the one
/// thing a parity test must never be.
///
/// The three functions, in the order `CSprite::DrawModel` reaches them —
/// <c>GetSpriteAxes</c> (`c_sprite.cpp:226`), <c>StandardGlowBlend</c> (`:147`) and
/// <c>DrawSpriteModel</c> (`:45`) — and the edges `CEngineSprite::Init` hands the last of them
/// (`spritemodel.cpp:311-328`).
/// </remarks>
public sealed class EntitySpriteConformanceTests
{
    private static readonly Vector3 ViewRight = new(1f, 0f, 0f);
    private static readonly Vector3 ViewUp = new(0f, 0f, 1f);
    private static readonly Vector3 ViewForward = new(0f, 1f, 0f);

    private static (Vector3 Right, Vector3 Up)? Axes(SpriteOrientation orientation, float roll = 0f) =>
        EntitySprites.Axes(
            orientation, new Vector3(100f, 200f, 50f), (0f, 0f, roll), ViewRight, ViewUp, ViewForward);

    [Test]
    public void Axes_ForAViewplaneParallelSprite_TakesTheViewsOwnRightAndUp()
    {
        Axes(SpriteOrientation.Parallel).ShouldBe((ViewRight, ViewUp));
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
        (Vector3 right, Vector3 up) = Axes(SpriteOrientation.Parallel, roll: 90f)!.Value;

        right.X.ShouldBe(0f, 0.0001f);
        right.Z.ShouldBe(1f, 0.0001f);

        up.X.ShouldBe(-1f, 0.0001f);
        up.Z.ShouldBe(0f, 0.0001f);
    }

    /// <remarks>
    /// **World up, and a right perpendicular to the view** — <c>up = (0,0,1)</c> with
    /// <c>right = ( forward.y, -forward.x, 0 )</c> normalised. The DEFAULT: the `Sprite` shader assigns
    /// it when a material declares no <c>$spriteorientation</c>, or names one it does not know
    /// (`sprite_dx9.cpp:99` and `:105`).
    ///
    /// **This remark used to say that `light_glow03` declares none, so that this branch draws every
    /// lamp halo in the game.** It declares `vp_parallel` (B390): the plain billboard draws them, and
    /// that sentence is how every glow came to be drawn upright.
    /// </remarks>
    [Test]
    public void Axes_ForTheDefaultOrientation_StandsTheSpriteUprightInTheWorld()
    {
        (Vector3 right, Vector3 up) = Axes(SpriteOrientation.ParallelUpright)!.Value;

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
            SpriteOrientation.ParallelUpright,
            new Vector3(100f, 200f, 50f),
            angles: (0f, 0f, 0f),
            ViewRight,
            ViewUp,
            viewForward: new Vector3(0f, 0f, -1f))
            .ShouldBeNull();
    }

    /// <remarks>
    /// **The entity's own basis, through `AngleVectors`** — <c>case SPR_ORIENTED: AngleVectors( angles,
    /// &amp;forward, &amp;right, &amp;up );</c> (`c_sprite.cpp:320-325`). This branch drew nothing until
    /// B390.
    ///
    /// Pitched a quarter turn so the answer can be written down rather than computed by the formula
    /// under test: a sprite pitched to face straight down has its right along −Y and its up along +X,
    /// and no other orientation produces either from this fixture's view.
    /// </remarks>
    [Test]
    public void Axes_ForAnOrientedSprite_TakesTheEntitysOwnAngles()
    {
        (Vector3 right, Vector3 up) = EntitySprites.Axes(
            SpriteOrientation.Oriented,
            new Vector3(100f, 200f, 50f),
            (Pitch: 90f, Yaw: 0f, Roll: 0f),
            ViewRight,
            ViewUp,
            ViewForward)!.Value;

        right.X.ShouldBe(0f, 0.0001f);
        right.Y.ShouldBe(-1f, 0.0001f);
        right.Z.ShouldBe(0f, 0.0001f);

        up.X.ShouldBe(1f, 0.0001f);
        up.Y.ShouldBe(0f, 0.0001f);
        up.Z.ShouldBe(0f, 0.0001f);
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
    /// **The `Sprite` shader's own switch, mode by mode** (`sprite_dx9.cpp:227`): the entity's render
    /// mode picks the blend, through the material `CEngineSprite::Init` built for that mode
    /// (`spritemodel.cpp:279`) — never the material's own text (B391). Additive is <c>SRC_ALPHA,
    /// ONE</c>; translucent is <c>SRC_ALPHA, ONE_MINUS_SRC_ALPHA</c>.
    /// </remarks>
    [TestCase(RenderModes.TransColor, SpriteBlend.Translucent)]
    [TestCase(RenderModes.TransTexture, SpriteBlend.Translucent)]
    [TestCase(RenderModes.Glow, SpriteBlend.Additive)]
    [TestCase(RenderModes.TransAlpha, SpriteBlend.Translucent)]
    [TestCase(RenderModes.TransAdd, SpriteBlend.Additive)]
    [TestCase(RenderModes.TransAddFrameBlend, SpriteBlend.Additive)]
    [TestCase(RenderModes.WorldGlow, SpriteBlend.Additive)]
    public void BlendFor_EachModeTheShaderBlends_IsTheShadersBlend(int renderMode, SpriteBlend expected)
    {
        EntitySprites.BlendFor(renderMode).ShouldBe(expected);
    }

    /// <remarks>
    /// **`kRenderEnvironmental` and `kRenderNone` have no material at all** — `CEngineSprite::Init`
    /// skips both (`spritemodel.cpp:281`) — so there is nothing to draw, which is a different answer
    /// from any blend.
    /// </remarks>
    [TestCase(RenderModes.Environmental)]
    [TestCase(RenderModes.None)]
    public void BlendFor_AModeWithNoMaterial_DrawsNothing(int renderMode)
    {
        EntitySprites.BlendFor(renderMode).ShouldBeNull();
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
    /// **The default, and the only symmetric case**: <c>origin = ( -width * 0.5f, height * 0.5f )</c>
    /// when the material declares no vector, then <c>up = origin[1]; down = origin[1] - m_height; left
    /// = origin[0]; right = m_width + origin[0];</c> (`spritemodel.cpp:313-328`).
    /// </remarks>
    [Test]
    public void Extents_WhenTheMaterialDeclaresNoOrigin_AreCentred()
    {
        SpriteExtents.Of(64, 32, origin: null).ShouldBe(new SpriteExtents(-32f, 32f, 16f, -16f));
    }

    /// <remarks>
    /// **An origin chosen so that all four edges differ**, which is what makes a swapped axis or a
    /// dropped negation visible: a quarter across and three quarters down a 64×32 texture gives
    /// <c>origin = ( -64 * 0.25, 32 * 0.75 ) = ( -16, 24 )</c>, so left −16, right 48, up 24 and down
    /// −8. A centred reading gives ±32 and ±16, and swapping X for Y gives none of these.
    /// </remarks>
    [Test]
    public void Extents_ForAnOffCentreOrigin_PutEachEdgeWhereTheEngineDoes()
    {
        SpriteExtents.Of(64, 32, (0.25f, 0.75f)).ShouldBe(new SpriteExtents(-16f, 48f, 24f, -8f));
    }

    /// <remarks>
    /// **The value three shipped materials declare**, `[ 0.50 0.00 ]`: the top edge ON the origin and
    /// the whole quad below it.
    /// </remarks>
    [Test]
    public void Extents_ForAnOriginOnTheTopEdge_HangTheQuadBelowIt()
    {
        SpriteExtents.Of(64, 32, (0.5f, 0f)).ShouldBe(new SpriteExtents(-32f, 32f, 0f, -32f));
    }

    /// <remarks>
    /// **The quad spans the material's size, centred**, because `CEngineSprite::Init` falls back to
    /// <c>origin = ( -width * 0.5f, height * 0.5f )</c> when `$spriteorigin` is absent and then sets
    /// up/down/left/right from it. A 64×32 material at scale 2 is therefore 128 by 64 world units
    /// about its origin — and the exact corner is what says the halves were not confused.
    /// </remarks>
    [Test]
    public void Corners_ForACentredSprite_SpanTheMaterialsSizeAboutTheOrigin()
    {
        List<DetailSpriteVertex> corners = [];

        EntitySprites.Corners(
            origin: Vector3.Zero,
            right: ViewRight,
            up: ViewUp,
            extents: SpriteExtents.Of(64, 32, origin: null),
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
            Vector3.Zero, ViewRight, ViewUp, SpriteExtents.Of(64, 64, origin: null), 1f,
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
