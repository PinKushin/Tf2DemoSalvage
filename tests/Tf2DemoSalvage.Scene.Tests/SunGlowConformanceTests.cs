using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary><c>C_Sun</c>'s two <c>C_SunGlowOverlay</c>s and <c>CGlowOverlay::Draw</c> (B378).</summary>
/// <remarks>
/// *Read from published source*: `game/client/c_sun.cpp` (<c>OnDataChanged</c>, `:50-138`), `c_sun.h`
/// (<c>C_SunGlowOverlay::CalcSpriteColorAndSize</c>, `:166-183`) and `glow_overlay.cpp` (<c>CalcBasis</c> `:322-343`,
/// <c>UpdateGlowObstruction</c> `:205-216`, <c>Draw</c> `:346-440`). The sun is koth_lakeside_final's: rendercolor
/// 228 202 186, size 32, overlay size 32 (the server's <c>-1</c> default, `sun.cpp:135-138`), overlay colour 0 0 0.
/// </remarks>
public sealed class SunGlowConformanceTests
{
    private const string Material = "sprites/light_glow02_add_noz.vmt";

    private static readonly (byte, byte, byte) Lakeside = (228, 202, 186);

    private static SceneSun Sun(bool on = true) => new(
        189, (1f, 0f, 0f), on, 32, 32, Lakeside, (0, 0, 0), Material, Material, 1f);

    /// <remarks>
    /// "we renormalize the components to make sure only hue is altered": <c>vMainColor = m_clrRender / maxComponent</c>,
    /// and an all-zero <c>m_clrOverlay</c> means the overlay takes the main colour (`c_sun.cpp:60-89`).
    /// </remarks>
    [Test]
    public void Colours_LakesidesSun_IsTheRenderColourOverItsLargestComponentForBoth()
    {
        (Vector3 core, Vector3 glow) = SunGlow.Colours(Lakeside, (0, 0, 0));

        core.ShouldBe(new Vector3(1f, 202f / 228f, 186f / 228f));
        glow.ShouldBe(core);
    }

    [Test]
    public void Colours_AnOverlayColour_IsItsOwnOverTwoFiftyFive() =>
        SunGlow.Colours(Lakeside, (255, 51, 0)).Glow.ShouldBe(new Vector3(1f, 0.2f, 0f));

    /// <remarks>"This is an error, set to pure white".</remarks>
    [Test]
    public void Colours_ABlackRenderColour_IsWhite() =>
        SunGlow.Colours((0, 0, 0), (0, 0, 0)).Core.ShouldBe(Vector3.One);

    /// <remarks>
    /// <code>
    /// float alpha = RemapVal( flDot, 1.0f, 0.9f, 0.75f, 0.0f );
    /// alpha = clamp( alpha, 0.0f, 0.75f );
    /// *flHorzSize = pSprite-&gt;m_flHorzSize * 6.0f;
    /// *vColor = pSprite-&gt;m_vColor * alpha * m_flGlowObstructionScale;
    /// </code>
    /// </remarks>
    [TestCase(1f, 0.75f)]
    [TestCase(0.95f, 0.375f)]
    [TestCase(0.85f, 0f)]
    public void CalcSpriteColorAndSize_ModulatedByDot_IsSixTimesTheSizeAndFadesOverATenth(float dot, float alpha)
    {
        (float horizontal, float vertical, Vector3 colour) = SunGlow.CalcSpriteColorAndSize(
            modulateByDot: true, dot, 32f, Vector3.One, obstruction: 0.5f);

        horizontal.ShouldBe(192f);
        vertical.ShouldBe(192f);
        colour.X.ShouldBe(alpha * 0.5f, 1e-5f);
    }

    /// <remarks>The core is never modulated: its own size, its colour times the obstruction alone.</remarks>
    [Test]
    public void CalcSpriteColorAndSize_TheCore_IsItsSizeAndColourTimesTheObstruction()
    {
        (float horizontal, _, Vector3 colour) = SunGlow.CalcSpriteColorAndSize(
            modulateByDot: false, 0.2f, 32f, new Vector3(1f, 0.5f, 0.25f), obstruction: 0.5f);

        horizontal.ShouldBe(32f);
        colour.ShouldBe(new Vector3(0.5f, 0.25f, 0.125f));
    }

    /// <remarks>
    /// <c>vBasePt = CurrentViewOrigin() + vToGlow * 100</c>; <c>vRight = vToGlow × (0,0,1)</c> normalized; <c>vUp =
    /// vRight × vToGlow</c> normalized; each scaled by its size.
    /// </remarks>
    [Test]
    public void CalcBasis_ASunDueEast_IsAHundredUnitsOutWithWorldUpAsItsUp()
    {
        (Vector3 at, Vector3 right, Vector3 up) = SunGlow.CalcBasis(new Vector3(10f, 0f, 0f), Vector3.UnitX, 32f, 16f);

        at.ShouldBe(new Vector3(110f, 0f, 0f));
        right.ShouldBe(new Vector3(0f, -32f, 0f));
        up.ShouldBe(new Vector3(0f, 0f, 16f));
    }

    /// <remarks>
    /// <c>pos = CurrentViewOrigin() + m_vDirection * (pViewSetup-&gt;zFar * 0.999f)</c>,
    /// <c>params.Init( pos, m_flProxyRadius, CalcGlowAspect() )</c> with <c>m_flProxyRadius = 0.05f</c>, and
    /// <c>params.bSizeInScreenspace = true</c>.
    /// </remarks>
    [Test]
    public void Query_ForTheSky_IsAtTheFarPlaneAndSizedInScreenSpace()
    {
        GlowQuery query = SunGlow.Query(-379, new Vector3(0f, 0f, 10f), Vector3.UnitX, 10000f, 1f);

        query.Position.ShouldBe(new Vector3(9990f, 0f, 10f));
        query.ProxySize.ShouldBe(0.05f);
        query.ProxyAspect.ShouldBe(1f);
        query.SizeInScreenspace.ShouldBeTrue();
        query.Entity.ShouldBe(-379);
    }

    /// <remarks>
    /// **Two overlays, two query handles** (`c_sun.h:213-214`, `glow_overlay.h:695`): the core and the outer glow each
    /// ask their own set, so neither key may be an entity index or the other's.
    /// </remarks>
    [Test]
    public void Keys_OfOneSun_AreTwoAndNeitherIsAnEntity()
    {
        SunGlow.CoreKey(189).ShouldBeLessThan(0);
        SunGlow.GlowKey(189).ShouldBeLessThan(0);
        SunGlow.CoreKey(189).ShouldNotBe(SunGlow.GlowKey(189));
        SunGlow.GlowKey(189).ShouldNotBe(SunGlow.CoreKey(190));
    }

    /// <remarks>
    /// Looking straight at it (<c>flDot = 1</c>), fully seen: the core at its colour and the overlay at 0.75 of it, both
    /// additive and <c>$ignorez</c> — `light_glow02_add_noz` as shipped.
    /// </remarks>
    [Test]
    public void Build_ASunStraightAhead_DrawsTheCoreAndTheGlowUntestedAndAdditive()
    {
        IReadOnlyList<ParticleBatch> batches = Build([Sun()], _ => 1f);

        batches.Count.ShouldBe(2);
        batches[0].Material.Blend.ShouldBe(SpriteBlend.Additive);
        batches[0].Material.Depth.ShouldBe(SpriteDepth.Off);
        batches[1].Material.Depth.ShouldBe(SpriteDepth.Off);

        DetailSpriteVertex core = batches[0].Corners[0];
        DetailSpriteVertex glow = batches[1].Corners[0];

        core.Red.ShouldBe(1f, 1e-5f);
        core.Blue.ShouldBe(186f / 228f, 1e-5f);
        glow.Red.ShouldBe(0.75f, 1e-5f);

        // The core's quad: 100 out, ±32 across and up.
        core.X.ShouldBe(100f, 1e-3f);
        System.Math.Abs(core.Y).ShouldBe(32f, 1e-3f);
        System.Math.Abs(glow.Y).ShouldBe(192f, 1e-3f);
    }

    /// <remarks><c>if( m_flGlowObstructionScale == 0 ) return;</c></remarks>
    [Test]
    public void Build_ASunTheSceneHides_DrawsNothing() =>
        Build([Sun()], _ => 0f).ShouldBeEmpty();

    /// <remarks><c>m_bOn</c> clear deactivates both overlays; <c>DrawOverlays</c> skips an inactive one.</remarks>
    [Test]
    public void Build_ASunTurnedOff_DrawsNothing() =>
        Build([Sun(on: false)], _ => 1f).ShouldBeEmpty();

    /// <remarks>The visibility the gate is asked is the one the colour is scaled by.</remarks>
    [Test]
    public void Build_HalfSeen_IsHalfAsBright()
    {
        IReadOnlyList<ParticleBatch> batches = Build([Sun()], _ => 0.5f);

        batches[0].Corners[0].Red.ShouldBe(0.5f, 1e-5f);
    }

    private static IReadOnlyList<ParticleBatch> Build(IReadOnlyList<SceneSun> suns, System.Func<GlowQuery, float> visible)
    {
        EngineSprite noz = new(
            new ParticleMaterial(
                new MapTexture(128, 128, 128, 128, TextureImage.None, IsTransparent: true), [], SpriteBlend.Additive),
            128,
            128,
            SpriteOrientation.ParallelUpright,
            SpriteExtents.Of(128, 128, origin: null),
            (1f, 1f, 1f, 1f),
            IgnoresVertexColors: false,
            Shader: "UnlitGeneric",
            Modulation: (1f, 1f, 1f, 1f),
            VertexColour: true);

        return new SunGlow().Build(
            suns,
            Vector3.Zero,
            Vector3.UnitX,
            10000f,
            new Dictionary<string, EngineSprite>(System.StringComparer.OrdinalIgnoreCase) { [Material] = noz },
            visible);
    }
}
