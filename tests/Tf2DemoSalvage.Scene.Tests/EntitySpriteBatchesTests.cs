using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// That the sprite pass draws what each sprite's MATERIAL asks for (B390).
/// </summary>
/// <remarks>
/// **The output-level test B378 did not have.** `EntitySprites` was conformance-tested function by
/// function and every branch of `GetSpriteAxes` passed — while the one caller passed the literal
/// <c>SPR_VP_PARALLEL_UPRIGHT</c> for every sprite, so four of the five branches were reachable only
/// from their own tests (`docs/memory/output-level-assertion-or-it-is-not-done.md`).
///
/// **Straight down is where the orientations disagree about whether to draw at all.** The upright
/// kinds build their basis from a cross product against world up and REFUSE within one degree of it;
/// the viewplane kinds take the view's own axes and never refuse. So a glow seen from above is drawn
/// or skipped according to nothing but its orientation — a count, not a picture, and exact.
/// </remarks>
public sealed class EntitySpriteBatchesTests
{
    private const string Glow = "materials/sprites/light_glow03.vmt";

    /// <remarks>
    /// `light_glow03` as it ships: `"$spriteorientation" "vp_parallel"`. Drawn from overhead, because
    /// nothing about a viewplane-parallel sprite depends on where the camera points.
    /// </remarks>
    [Test]
    public void Build_AViewplaneParallelGlowSeenFromStraightAbove_IsDrawn()
    {
        EntitySpriteBatches sprites = new();

        IReadOnlyList<ParticleBatch> batches = Overhead(sprites, SpriteOrientation.Parallel);

        sprites.Drawn.ShouldBe(1);
        batches.Count.ShouldBe(1);
        batches[0].Corners.Count.ShouldBe(6, "two triangles");
    }

    /// <remarks>
    /// **The control, and the reason the test above means anything.** The same glow with the DEFAULT
    /// orientation is refused from the same place — <c>if ((dot &gt; 0.999848f) || (dot &lt;
    /// -0.999848f)) return;</c> — so the only difference between the two runs is which orientation
    /// reached `GetSpriteAxes`. A pass that ignored the material would give this answer for both.
    /// </remarks>
    [Test]
    public void Build_AnUprightGlowSeenFromStraightAbove_IsRefused()
    {
        EntitySpriteBatches sprites = new();

        Overhead(sprites, SpriteOrientation.ParallelUpright).ShouldBeEmpty();

        sprites.Drawn.ShouldBe(0);
        sprites.Skipped.ShouldBe(1);
    }

    /// <remarks>
    /// **The origin reaches the corners.** `[ 0.50 0.00 ]` puts the quad's top edge on the sprite's
    /// origin, so every corner is at or below it along the view's up, where a centred quad would put
    /// half of them above.
    /// </remarks>
    [Test]
    public void Build_AGlowWhoseOriginIsItsTopEdge_HangsBelowIt()
    {
        EntitySpriteBatches sprites = new();

        IReadOnlyList<ParticleBatch> batches = Overhead(
            sprites, SpriteOrientation.Parallel, SpriteExtents.Of(128, 128, (0.5f, 0f)));

        // Looking straight down with the view's up along +Y, "above the origin" is +Y.
        batches[0].Corners.ShouldAllBe(corner => corner.Y <= 0f);
        batches[0].Corners.ShouldContain(corner => corner.Y < -1f);
    }

    /// <remarks>
    /// **The black square** (B391). `light_glow03`'s own text reads translucent — its `$additive` is
    /// commented out — and its texture is opaque black around the glow, so drawing it by the material's
    /// text paints the black. The engine never asks the text: `CEngineSprite::Init` builds one material
    /// per render mode (`spritemodel.cpp:279`) and the `Sprite` shader draws `kRenderWorldGlow` with
    /// <c>BlendFunc( SHADER_BLEND_SRC_ALPHA, SHADER_BLEND_ONE )</c> (`sprite_dx9.cpp:260`), where black
    /// adds nothing.
    /// </remarks>
    [Test]
    public void Build_AWorldGlowWhoseMaterialReadsTranslucent_IsDrawnAdditive()
    {
        EntitySpriteBatches sprites = new();

        IReadOnlyList<ParticleBatch> batches = Seen(sprites, RenderModes.WorldGlow);

        batches.Count.ShouldBe(1);
        batches[0].Material.Blend.ShouldBe(SpriteBlend.Additive);
    }

    /// <remarks>
    /// **The control: the MODE decides, in both directions.** A `kRenderTransColor` sprite whose
    /// material text says additive is drawn translucent, because that mode's material is
    /// <c>SRC_ALPHA, ONE_MINUS_SRC_ALPHA</c> whatever the text says. A pass that made every sprite
    /// additive would pass the test above and fail this one.
    /// </remarks>
    [Test]
    public void Build_ATranslucentModeSpriteWhoseMaterialReadsAdditive_IsDrawnTranslucent()
    {
        EntitySpriteBatches sprites = new();

        Seen(sprites, RenderModes.TransColor, SpriteBlend.Additive)[0].Material.Blend
            .ShouldBe(SpriteBlend.Translucent);
    }

    /// <remarks>
    /// **The entity's color reaches every corner** (B391): `CSprite::DrawModel` passes
    /// <c>m_clrRender-&gt;r/g/b</c> (`Sprite.cpp:806`), a glow multiplies them by its blend, and the
    /// pixel shader multiplies the texture by the result. At 500 units a world glow's blend is one, so
    /// the corners carry the color exactly — where this drew every sprite white.
    /// </remarks>
    [Test]
    public void Build_ATintedGlow_CarriesItsRenderColorIntoEveryCorner()
    {
        EntitySpriteBatches sprites = new();

        IReadOnlyList<ParticleBatch> batches =
            Seen(sprites, RenderModes.WorldGlow, color: (255, 128, 0));

        foreach (DetailSpriteVertex corner in batches[0].Corners)
        {
            corner.Red.ShouldBe(1f, 0.0001f);
            corner.Green.ShouldBe(128f / 255f, 0.0001f);
            corner.Blue.ShouldBe(0f, 0.0001f);
        }
    }

    /// <remarks>
    /// **`GlowBlend` is for the two glow modes and nothing else** — <c>if (( rendermode == kRenderGlow )
    /// || ( rendermode == kRenderWorldGlow )) blend *= GlowBlend( ... );</c> (`c_sprite.cpp:414`). A
    /// `kRenderTransAdd` sprite 2,400 units away is therefore neither faded — the glow fade there would
    /// be a quarter — nor grown, where `GlowBlend` would multiply a non-world glow's scale by
    /// <c>dist / 200</c>, twelve. So its corners carry full white and sit at the material's own ±64.
    /// </remarks>
    [Test]
    public void Build_AnAdditiveSpriteFarAway_IsNeitherFadedNorGrownByTheGlowRule()
    {
        EntitySpriteBatches sprites = new();

        IReadOnlyList<ParticleBatch> batches = Seen(sprites, RenderModes.TransAdd, height: 2400f);

        foreach (DetailSpriteVertex corner in batches[0].Corners)
        {
            corner.Red.ShouldBe(1f, 0.0001f);
            System.MathF.Abs(corner.X).ShouldBe(64f, 0.001f);
        }
    }

    /// <remarks>
    /// **`kRenderNormal` draws the texture alone.** Its shader branch passes no color flags —
    /// <c>SetSpriteCommonShadowState( 0 )</c> under <c>case kRenderNormal</c> (`sprite_dx9.cpp:229`) — so
    /// the vertex format carries no color and the pixel shader never multiplies by one: neither
    /// `m_clrRender` nor the brightness reaches it. A tinted, half-bright normal sprite therefore comes
    /// out white and opaque. Its blend remains B391's named divergence — translucent here, none in the
    /// engine.
    /// </remarks>
    [Test]
    public void Build_ANormalModeSprite_DrawsTheTextureAlone()
    {
        EntitySpriteBatches sprites = new();

        IReadOnlyList<ParticleBatch> batches =
            Seen(sprites, RenderModes.Normal, color: (255, 128, 0), brightness: 128);

        foreach (DetailSpriteVertex corner in batches[0].Corners)
        {
            corner.Red.ShouldBe(1f);
            corner.Green.ShouldBe(1f);
            corner.Blue.ShouldBe(1f);
            corner.Alpha.ShouldBe(1f);
        }
    }

    /// <summary>One viewplane-parallel sprite at the origin, seen from straight above.</summary>
    /// <param name="sprites">The pass under test.</param>
    /// <param name="renderMode">The entity's <c>m_nRenderMode</c>.</param>
    /// <param name="materialBlend">What the material's own TEXT says; translucent, as `light_glow03`'s does.</param>
    /// <param name="color">The entity's <c>m_clrRender</c> red, green and blue; white when omitted.</param>
    /// <param name="height">How far above the sprite the camera is.</param>
    /// <param name="brightness">The sprite's <c>m_nBrightness</c>, which is its alpha.</param>
    private static IReadOnlyList<ParticleBatch> Seen(
        EntitySpriteBatches sprites,
        int renderMode,
        SpriteBlend materialBlend = SpriteBlend.Translucent,
        (byte Red, byte Green, byte Blue)? color = null,
        float height = 500f,
        int brightness = 255)
    {
        EngineSprite sprite = new(
            new ParticleMaterial(
                new MapTexture(128, 128, TextureImage.None, IsTransparent: true), [], materialBlend),
            128,
            128,
            SpriteOrientation.Parallel,
            SpriteExtents.Of(128, 128, origin: null));

        SceneProp prop = new(
            EntityIndex: 42,
            ModelPath: Glow,
            Kind: SceneModelKind.Sprite,
            Pose: new ScenePose
            {
                RenderMode = renderMode,
                RenderColor = color ?? ((byte)255, (byte)255, (byte)255),
                Sprite = SceneSprite.Default with { Brightness = brightness },
            });

        return sprites.Build(
            [prop],
            eye: new Vector3(0f, 0f, height),
            viewRight: Vector3.UnitX,
            viewUp: Vector3.UnitY,
            viewForward: -Vector3.UnitZ,
            sprites: new Dictionary<string, EngineSprite>(System.StringComparer.OrdinalIgnoreCase)
            {
                [Glow] = sprite,
            },
            visible: _ => true);
    }

    /// <summary>One world glow at the origin, drawn by a camera 500 units above it looking down.</summary>
    private static IReadOnlyList<ParticleBatch> Overhead(
        EntitySpriteBatches sprites, SpriteOrientation orientation, SpriteExtents? extents = null)
    {
        EngineSprite glow = new(
            new ParticleMaterial(
                new MapTexture(128, 128, TextureImage.None, IsTransparent: true),
                [],
                SpriteBlend.Additive),
            128,
            128,
            orientation,
            extents ?? SpriteExtents.Of(128, 128, origin: null));

        SceneProp prop = new(
            EntityIndex: 42,
            ModelPath: Glow,
            Kind: SceneModelKind.Sprite,
            Pose: new ScenePose { RenderMode = RenderModes.WorldGlow, Sprite = SceneSprite.Default });

        return sprites.Build(
            [prop],
            eye: new Vector3(0f, 0f, 500f),
            viewRight: Vector3.UnitX,
            viewUp: Vector3.UnitY,
            viewForward: -Vector3.UnitZ,
            sprites: new Dictionary<string, EngineSprite>(System.StringComparer.OrdinalIgnoreCase)
            {
                [Glow] = glow,
            },
            visible: _ => true);
    }
}
