using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary><c>DT_SpriteTrail</c>, and the two <c>DT_Sprite</c> fields a trail's render origin reads (B474).</summary>
/// <remarks>
/// **The send table, `SpriteTrail.cpp:88-108`**: eight <c>SPROP_NOSCALE</c> floats and a vector — lifetime, start
/// width, end width, width variance, texture resolution, minimum fade length, skybox origin and skybox scale. The
/// values below are a Sandman ball's, as `demostf-pl_badwater_pro_v12-1491221` sends them (`entity-census`): life
/// 0.4, width 9, end width −1, texture resolution 0.01, render mode 4, brightness 128.
/// </remarks>
public sealed class SpriteTrailStateConformanceTests
{
    private const string Trail = "DT_SpriteTrail";

    private const string Sprite = "DT_Sprite";

    private const int BallHandle = (5 << 11) | 301;

    /// <remarks>
    /// **Every field from its own table** — the trail's from <c>DT_SpriteTrail</c>, the attachment from
    /// <c>DT_Sprite</c>, whose <c>m_hAttachedToEntity</c> and <c>m_nAttachment</c> are what
    /// <c>CSpriteTrail::GetRenderOrigin</c> asks (`SpriteTrail.cpp:542-548`).
    /// </remarks>
    [Test]
    public void SpriteTrail_ForASandmanBall_ReadsItsOwnTableAndTheSpritesAttachment()
    {
        EntityState ball = new(301, 0, 0, "CSpriteTrail");

        ball.Set($"{Trail}.m_flLifeTime", PropertyValue.FromFloat(0.4f));
        ball.Set($"{Trail}.m_flStartWidth", PropertyValue.FromFloat(9f));
        ball.Set($"{Trail}.m_flEndWidth", PropertyValue.FromFloat(-1f));
        ball.Set($"{Trail}.m_flStartWidthVariance", PropertyValue.FromFloat(0.5f));
        ball.Set($"{Trail}.m_flTextureRes", PropertyValue.FromFloat(0.01f));
        ball.Set($"{Trail}.m_flMinFadeLength", PropertyValue.FromFloat(24f));
        ball.Set($"{Trail}.m_vecSkyboxOrigin", PropertyValue.FromVector(1f, 2f, 3f));
        ball.Set($"{Trail}.m_flSkyboxScale", PropertyValue.FromFloat(16f));
        ball.Set($"{Sprite}.m_hAttachedToEntity", PropertyValue.FromInt(BallHandle));
        ball.Set($"{Sprite}.m_nAttachment", PropertyValue.FromInt(3));

        ball.SpriteTrail().ShouldBe(new SceneSpriteTrail(
            LifeTime: 0.4f,
            StartWidth: 9f,
            EndWidth: -1f,
            StartWidthVariance: 0.5f,
            TextureRes: 0.01f,
            MinFadeLength: 24f,
            SkyboxOrigin: (1f, 2f, 3f),
            SkyboxScale: 16f,
            AttachedTo: BallHandle,
            Attachment: 3));
    }

    /// <remarks>
    /// **What the constructor leaves** (`SpriteTrail.cpp:119-131`): <c>m_flEndWidth = -1</c>, variance 0, skybox
    /// scale 1 and origin zero; and no attachment is the invalid handle, which is "not attached" rather than slot 0.
    /// </remarks>
    [Test]
    public void SpriteTrail_WithOnlyItsLifetime_TakesTheConstructorsDefaults()
    {
        EntityState trail = new(301, 0, 0, "CSpriteTrail");

        trail.Set($"{Trail}.m_flLifeTime", PropertyValue.FromFloat(1f));

        SceneSpriteTrail read = trail.SpriteTrail().ShouldNotBeNull();

        (read.EndWidth, read.StartWidthVariance, read.SkyboxScale).ShouldBe((-1f, 0f, 1f));
        read.SkyboxOrigin.ShouldBe((0f, 0f, 0f));
        (read.AttachedTo, read.Attachment).ShouldBe(((1 << 21) - 1, 0));
    }

    /// <remarks>
    /// **A plain sprite is not a trail.** <c>DT_Sprite</c> alone sends the attachment too, so the attachment cannot be
    /// what identifies the class — the lifetime, which only <c>DT_SpriteTrail</c> declares, is.
    /// </remarks>
    [Test]
    public void SpriteTrail_ForAPlainSpriteWithAnAttachment_IsNull()
    {
        EntityState glow = new(12, 0, 0, "CSprite");

        glow.Set($"{Sprite}.m_flSpriteScale", PropertyValue.FromFloat(0.25f));
        glow.Set($"{Sprite}.m_hAttachedToEntity", PropertyValue.FromInt(BallHandle));

        glow.SpriteTrail().ShouldBeNull();
    }
}
