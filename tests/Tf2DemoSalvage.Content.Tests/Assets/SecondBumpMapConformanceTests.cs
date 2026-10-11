using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// `$bumpmap2`: a blended surface's second normal map, mixed with the first by the blend factor (B514).
/// </summary>
/// <remarks>
/// *Read from published source*, `lightmappedgeneric_ps2_3_x.h:345-384` and `lightmappedgeneric_dx9_helper.cpp:310`:
///
/// <code>
/// bool hasBump2 = hasBump &amp;&amp; params[info.m_nBumpmap2]->IsTexture();
/// ...
/// if ( BUMPMAP == 2 ) vNormal2 = tex2D( BumpmapSampler2, b2TexCoord );
/// else                vNormal2 = DecompressNormal( BumpmapSampler2, b2TexCoord, NORMAL_DECODE_MODE, AlphaMapSampler2 );
/// ...
/// if ( FANCY_BLENDING &amp;&amp; bNormalMapAlphaEnvmapMask ) vNormal = lerp( vNormal, vNormal2, blendfactor);
/// else                                                vNormal.xyz = lerp( vNormal.xyz, vNormal2.xyz, blendfactor);
/// </code>
///
/// 65 shipped materials name one (`vmt-param $bumpmap2`), 63 of them WorldVertexTransition. Not modeled: `$bumpmask`
/// (which needs no second base texture, so no terrain blend has it) and `$basetexture2noenvmap`.
/// </remarks>
public sealed class SecondBumpMapConformanceTests
{
    [Test]
    public void BumpMap2_AMaterialNamingOne_IsThatTexture() =>
        Parse("\"$bumpmap\" \"a\"\n\"$bumpmap2\" \"Nature/rockground002_height-ssbump\"").BumpMap2
            .ShouldBe("Nature/rockground002_height-ssbump");

    [Test]
    public void BumpMap2_WhenTheMaterialDeclaresNone_IsNull() =>
        Parse("\"$bumpmap\" \"a\"").BumpMap2.ShouldBeNull();

    /// <remarks>The mode is the pixel shader's: 0 no bump map, 1 one, 2 two mixed by the blend, 3 two with their alphas mixed too.</remarks>
    [TestCase(false, false, false, false, SecondBumpMap.None)]
    [TestCase(true, false, false, false, SecondBumpMap.One)]
    [TestCase(false, true, false, false, SecondBumpMap.None, Description = "hasBump2 = hasBump && ...")]
    [TestCase(true, true, false, false, SecondBumpMap.Mixed)]
    [TestCase(true, true, true, false, SecondBumpMap.Mixed, Description = "fancy blending alone leaves the alpha")]
    [TestCase(true, true, false, true, SecondBumpMap.Mixed, Description = "the alpha mask alone leaves the alpha")]
    [TestCase(true, true, true, true, SecondBumpMap.MixedWithAlpha)]
    [TestCase(true, false, true, true, SecondBumpMap.One)]
    public void Mode_ByTheTexturesAndTheTwoSwitches_IsTheShadersBranch(
        bool bump, bool bump2, bool fancyBlending, bool normalMapAlphaEnvMapMask, SecondBumpMap expected) =>
        SecondBumpMaps.Mode(bump, bump2, fancyBlending, normalMapAlphaEnvMapMask).ShouldBe(expected);

    private static VmtMaterial Parse(string lines) =>
        VmtMaterial.Parse(Encoding.UTF8.GetBytes("\"WorldVertexTransition\"\n{\n" + lines + "\n}\n"));
}
