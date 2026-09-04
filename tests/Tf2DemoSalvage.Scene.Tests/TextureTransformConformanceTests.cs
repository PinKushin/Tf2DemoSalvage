using System;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The matrix a <c>$basetexturetransform</c> builds — Valve's composition, in order (B332).
/// </summary>
/// <remarks>
/// **The parameter's own declared default states the string's form**, which is the SDK answering a
/// question about a parser it does not ship:
///
/// <code>
/// SHADER_PARAM( BASETEXTURETRANSFORM, SHADER_PARAM_TYPE_MATRIX,
///               "center .5 .5 scale 1 1 rotate 0 translate 0 0", "$baseTexture texcoord transform" )
/// </code>
///
/// 53 shaders declare it with that exact string.
///
/// **And the COMPOSITION is in the SDK too**, in `CTextureTransformProxy::OnBind` — which builds the
/// same matrix from separate variables rather than from the packed string, and is therefore the
/// authority on what the string means:
///
/// <code>
/// MatrixBuildTranslation( mat,  -center.x, -center.y, 0.0f );
/// MatrixBuildScale      ( temp,  scale.x,   scale.y,  1.0f );  MatrixMultiply( temp, mat, mat );
/// MatrixBuildRotateZ    ( temp,  angle );                      MatrixMultiply( temp, mat, mat );
/// MatrixBuildTranslation( temp,  center.x,  center.y, 0.0f );  MatrixMultiply( temp, mat, mat );
/// MatrixBuildTranslation( temp,  translation.x, translation.y, 0.0f ); MatrixMultiply( temp, mat, mat );
/// </code>
///
/// `matrixproxy.cpp:75-113`. Read-from-source. `MatrixMultiply( A, B, out )` is `out = A * B` and
/// Source applies matrices to column vectors, so each step composes on the LEFT and therefore runs
/// AFTER the ones before it: move the centre to the origin, scale, rotate, move it back, then
/// translate.
///
/// **The order is the whole content of this suite.** Every wrong order still produces a matrix, and
/// most of them are the identity for the default values — so the fixtures below deliberately use a
/// centre that is not the origin, a scale that is not one, and a rotation that is not zero, since
/// only then do the orders disagree.
/// </remarks>
public sealed class TextureTransformConformanceTests
{
    /// <remarks>
    /// **The declared default must be the identity**, which is the one case every candidate
    /// ordering agrees on — so this is a floor rather than a discriminator, and it is here because a
    /// parser that failed on the commonest string in the game would be caught by nothing else.
    /// </remarks>
    [Test]
    public void Parse_TheDeclaredDefault_IsTheIdentity()
    {
        MaterialProxies.TextureTransformFrom("center .5 .5 scale 1 1 rotate 0 translate 0 0")
            .ShouldBe(TextureTransform.Identity);
    }

    /// <remarks>
    /// **Scale happens about the CENTRE, not about the origin.** With centre (0.5, 0.5) and scale 2,
    /// the matrix is `T(+c) · S · T(-c)`, whose translation column is `c - c·s` — that is
    /// `0.5 - 1.0 = -0.5`, not zero. A parser that scaled about the origin would give a translation
    /// of zero and slide the texture half a repeat.
    /// </remarks>
    [Test]
    public void Parse_AScaleAboutTheDefaultCentre_TranslatesByHalfTheGrowth()
    {
        TextureTransform transform =
            MaterialProxies.TextureTransformFrom("center .5 .5 scale 2 2 rotate 0 translate 0 0");

        transform.Row0.X.ShouldBe(2f, 0.0001f);
        transform.Row1.Y.ShouldBe(2f, 0.0001f);

        transform.Row0.W.ShouldBe(-0.5f, 0.0001f);
        transform.Row1.W.ShouldBe(-0.5f, 0.0001f);
    }

    /// <remarks>
    /// **A quarter turn about the centre**, which pins both the rotation's sense and that it happens
    /// after the centre has moved to the origin. At 90 degrees the top row becomes (0, -1) and the
    /// translation is `c` rotated away from itself: `(0.5 + 0.5, 0.5 - 0.5)` = `(1, 0)`.
    ///
    /// A rotation about the ORIGIN instead would leave the translation at zero, and the opposite
    /// sense would give `(0, 1)` — three outcomes, all matrices, only one right.
    /// </remarks>
    [Test]
    public void Parse_AQuarterTurn_RotatesAboutTheCentreInValvesSense()
    {
        TextureTransform transform =
            MaterialProxies.TextureTransformFrom("center .5 .5 scale 1 1 rotate 90 translate 0 0");

        transform.Row0.X.ShouldBe(0f, 0.0001f);
        transform.Row0.Y.ShouldBe(-1f, 0.0001f);
        transform.Row1.X.ShouldBe(1f, 0.0001f);
        transform.Row1.Y.ShouldBe(0f, 0.0001f);

        transform.Row0.W.ShouldBe(1f, 0.0001f);
        transform.Row1.W.ShouldBe(0f, 0.0001f);
    }

    /// <remarks>
    /// **Translation is applied LAST and is not scaled by the scale**, because Valve composes it
    /// after the centre has been restored. With scale 2 and translate 0.25, the offset is
    /// `-0.5 + 0.25`; a parser that translated before scaling would give `-0.5 + 0.5` and put the
    /// texture somewhere else entirely.
    /// </remarks>
    [Test]
    public void Parse_ATranslationBesideAScale_IsAppliedAfterIt()
    {
        TextureTransform transform = MaterialProxies.TextureTransformFrom(
            "center .5 .5 scale 2 2 rotate 0 translate .25 .25");

        transform.Row0.W.ShouldBe(-0.25f, 0.0001f);
        transform.Row1.W.ShouldBe(-0.25f, 0.0001f);
    }

    /// <remarks>
    /// **A missing keyword takes Valve's own default rather than zero**, which the proxy states by
    /// initialising `center( 0.5, 0.5 )` and `translation( 0, 0 )` before reading anything and by
    /// skipping each step when its variable is absent. A centre defaulting to zero would scale and
    /// rotate about a corner.
    /// </remarks>
    [Test]
    public void Parse_AStringNamingOnlyAScale_CentresItTheWayValveDoes()
    {
        TextureTransform transform = MaterialProxies.TextureTransformFrom("scale 2 2");

        transform.Row0.X.ShouldBe(2f, 0.0001f);
        transform.Row0.W.ShouldBe(-0.5f, 0.0001f, "an absent centre is (0.5, 0.5), not the origin");
    }

    /// <remarks>
    /// **Anything unparseable is the identity, not a crash and not a partial matrix.** Valve's own
    /// fallback for a variable that is not a matrix is exactly this — `transformation[0].Init( 1, 0,
    /// 0, 0 )` (`BaseVSShader.cpp:317-321`) — and a material naming a malformed transform should
    /// draw as though it named none.
    /// </remarks>
    [Test]
    public void Parse_AStringThatIsNotATransform_IsTheIdentity()
    {
        MaterialProxies.TextureTransformFrom("").ShouldBe(TextureTransform.Identity);
        MaterialProxies.TextureTransformFrom("nonsense").ShouldBe(TextureTransform.Identity);
        MaterialProxies.TextureTransformFrom("scale").ShouldBe(TextureTransform.Identity);
        MaterialProxies.TextureTransformFrom(null).ShouldBe(TextureTransform.Identity);
    }

    /// <remarks>
    /// **Keywords are matched case-insensitively, because KeyValues is** and TF2's own materials do
    /// not agree on case — the same reason `MaterialProxy.Argument` is. A case-sensitive reader
    /// silently takes every default and reports a perfectly valid identity.
    /// </remarks>
    [Test]
    public void Parse_TheKeywordsInAnyCase_AreStillRead()
    {
        MaterialProxies.TextureTransformFrom("Center .5 .5 Scale 2 2 Rotate 0 Translate 0 0")
            .Row0.X.ShouldBe(2f, 0.0001f);
    }
}
