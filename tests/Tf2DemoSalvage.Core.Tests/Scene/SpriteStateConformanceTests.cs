using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// <c>DT_Sprite</c> — what an entity sprite says about itself, and what each field MEANS (B378).
/// </summary>
/// <remarks>
/// **Every field of this table was decoded by nothing until B378.** `ScenePropTrack.Classify` named a
/// `.vmt` or `.spr` model reference `SceneModelKind.Sprite` and the value had exactly one reference in
/// the whole solution — its own assignment. So every `env_sprite` in every demo was offered to the draw
/// path, rejected as not a studio model, and counted: eleven per frame on `cp_granary`, and 2,840 over
/// 355 sampled frames of a 2026 `cp_process`.
///
/// **The send table, `Sprite.cpp:131`:**
///
/// <code>
/// SendPropEHandle( SENDINFO(m_hAttachedToEntity )),
/// SendPropInt(   SENDINFO(m_nAttachment ), 8 ),
/// SendPropFloat( SENDINFO(m_flScaleTime ),      0, SPROP_NOSCALE ),
/// SendPropFloat( SENDINFO(m_flSpriteScale ),    8, SPROP_ROUNDUP, 0.0f, MAX_SPRITE_SCALE ),
/// SendPropFloat( SENDINFO(m_flGlowProxySize ),  6, SPROP_ROUNDUP, 0.0f, MAX_GLOW_PROXY_SIZE ),
/// SendPropFloat( SENDINFO(m_flHDRColorScale ),  0, SPROP_NOSCALE, 0.0f, 100.0f ),
/// SendPropFloat( SENDINFO(m_flSpriteFramerate ),8, SPROP_ROUNDUP, 0,    60.0f ),
/// SendPropFloat( SENDINFO(m_flFrame),          20, SPROP_ROUNDDOWN, 0.0f, 256.0f ),
/// SendPropFloat( SENDINFO(m_flBrightnessTime ), 0, SPROP_NOSCALE ),
/// SendPropInt(   SENDINFO(m_nBrightness), 8, SPROP_UNSIGNED ),
/// SendPropBool(  SENDINFO(m_bWorldSpaceScale) ),
/// </code>
///
/// with <c>MAX_SPRITE_SCALE</c> and <c>MAX_GLOW_PROXY_SIZE</c> both 64 (`Sprite.cpp:28`).
///
/// **The values here are the ones a real demo carries**, read from `CSprite`'s instance baseline on
/// `20130518_0313_cp_granary_blu_blu` with the `baseline` probe — scale 0.25, glow proxy 4, HDR scale
/// 1, framerate 10.078, brightness 120, world-space scale clear. A fixture invented from the send
/// table alone would prove the accessor compiles; these prove it reads what TF2 actually sends.
///
/// **Synthetic rather than a corpus test**, per the project's order: the question is whether the
/// accessor names the right table and property, and a hand-built entity has ground truth where a
/// corpus one would only compare two readings of the same file. That the KEY FORMAT is right —
/// <c>DT_Sprite.m_flSpriteScale</c> rather than a flattened name — is the only part a real demo
/// settles, and it is settled and recorded in B378 rather than re-measured on every run.
/// </remarks>
public sealed class SpriteStateConformanceTests
{
    private const string Sprite = "DT_Sprite";

    /// <summary>A sprite carrying what granary's lamp glows carry.</summary>
    private static EntityState Glow()
    {
        EntityState sprite = new(1, 0, 0, "CSprite");

        sprite.Set($"{Sprite}.m_flSpriteScale", PropertyValue.FromFloat(0.25f));
        sprite.Set($"{Sprite}.m_nBrightness", PropertyValue.FromInt(120));
        sprite.Set($"{Sprite}.m_flGlowProxySize", PropertyValue.FromFloat(4f));
        sprite.Set($"{Sprite}.m_flHDRColorScale", PropertyValue.FromFloat(1f));
        sprite.Set($"{Sprite}.m_flSpriteFramerate", PropertyValue.FromFloat(10.078f));
        sprite.Set($"{Sprite}.m_flFrame", PropertyValue.FromFloat(0f));
        sprite.Set($"{Sprite}.m_bWorldSpaceScale", PropertyValue.FromInt(0));

        return sprite;
    }

    [Test]
    public void SpriteScale_ForAGlowStatingAQuarter_ReadsIt()
    {
        Glow().SpriteScale().ShouldBe(0.25f);
    }

    /// <remarks>
    /// **A sprite's alpha is its BRIGHTNESS, not its render colour's alpha byte** —
    /// `CSprite::DrawModel` passes `GetRenderBrightness()` as `DrawSprite`'s `alpha` while passing
    /// `m_clrRender->r/g/b` separately (`Sprite.cpp:753`). An implementation reading
    /// <see cref="EntityState.RenderAlpha"/> for a sprite would get 255 from the default and draw
    /// every lamp glow at twice the brightness TF2 does.
    /// </remarks>
    [Test]
    public void SpriteBrightness_ForAGlow_IsTheAlphaRatherThanTheRenderColours()
    {
        Glow().SpriteBrightness().ShouldBe(120);
        Glow().RenderAlpha().ShouldBe((byte)255, "the render colour says nothing, and its default is opaque");
    }

    /// <remarks>
    /// **Clear means the scale is a MULTIPLE of the sprite's own extents**, not world units. With it
    /// set, `CSprite::DrawModel` divides by the material's smaller dimension
    /// (<c>renderscale /= MIN( GetWidth(), GetHeight() )</c>, `Sprite.cpp:753`); with it clear
    /// `GetRenderBounds` multiplies by <c>MAX( width, height )</c> instead. Granary's glows send it
    /// clear, so 0.25 there means a quarter of the material's mapping size rather than a quarter of a
    /// world unit — a distinction worth about two orders of magnitude on screen.
    /// </remarks>
    [Test]
    public void SpriteScaleIsWorldSpace_ForAGlowSendingZero_IsFalse()
    {
        Glow().SpriteScaleIsWorldSpace().ShouldBe(false);
    }

    [Test]
    public void SpriteGlowProxySize_ForAGlow_ReadsTheOcclusionProxyRatherThanTheDrawnSize()
    {
        Glow().SpriteGlowProxySize().ShouldBe(4f);
    }

    [Test]
    public void SpriteFrame_ForAGlowOnItsFirstFrame_ReadsZeroRatherThanAnsweringAbsent()
    {
        Glow().SpriteFrame().ShouldBe(0f);
    }

    [Test]
    public void SpriteFramerate_ForAGlow_ReadsTheRateTheServerAnimatesAt()
    {
        Glow().SpriteFramerate().ShouldBe(10.078f);
    }

    [Test]
    public void SpriteHdrColourScale_ForAGlow_ReadsTheMaterialOverride()
    {
        Glow().SpriteHdrColourScale().ShouldBe(1f);
    }

    /// <remarks>
    /// **The control, and every one of these has a legitimate zero.** A brightness of nought, a scale
    /// of nought and a clear world-space flag are all real states, so an accessor that answered zero
    /// or false for "never sent" would be indistinguishable from a sprite that said so — which is
    /// `docs/memory/sentinels-conflate-unknown-with-answer.md` exactly. Null is the only answer that
    /// lets a caller apply `CSprite`'s own constructor defaults instead.
    /// </remarks>
    [Test]
    public void SpriteFields_ForAnEntityThatIsNotASprite_AllAnswerAbsent()
    {
        EntityState crate = new(1, 0, 0, "CBaseAnimating");

        crate.SpriteScale().ShouldBeNull();
        crate.SpriteBrightness().ShouldBeNull();
        crate.SpriteFrame().ShouldBeNull();
        crate.SpriteFramerate().ShouldBeNull();
        crate.SpriteGlowProxySize().ShouldBeNull();
        crate.SpriteHdrColourScale().ShouldBeNull();
        crate.SpriteScaleIsWorldSpace().ShouldBeNull();
    }
}
