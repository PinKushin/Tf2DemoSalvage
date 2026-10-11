namespace Tf2DemoSalvage.Content.Assets;

/// <summary>How a lightmapped surface reads its normal maps. The values are the pixel shader's (<c>bump.x</c>).</summary>
public enum SecondBumpMap
{
    /// <summary>No bump map.</summary>
    None = 0,

    /// <summary>One bump map.</summary>
    One = 1,

    /// <summary>Two, their directions mixed by the blend factor — <c>vNormal.xyz = lerp( vNormal.xyz, vNormal2.xyz, blendfactor )</c>.</summary>
    Mixed = 2,

    /// <summary>Two, alpha included — <c>vNormal = lerp( vNormal, vNormal2, blendfactor )</c>.</summary>
    MixedWithAlpha = 3,
}

/// <summary>`$bumpmap2` on the lightmapped shaders (B514).</summary>
/// <remarks>
/// *Read from published source*: `hasBump2 = hasBump &amp;&amp; params[info.m_nBumpmap2]->IsTexture()`
/// (`lightmappedgeneric_dx9_helper.cpp:310`), and the mix at `lightmappedgeneric_ps2_3_x.h:376-383`. The second map is
/// sampled at the first's coordinate and decoded as the first is (`:345-357`), so mixing the two TEXELS and decoding
/// once is the same arithmetic — the decode is affine. Not modeled: `$bumpmask`, `$basetexture2noenvmap`.
/// </remarks>
public static class SecondBumpMaps
{
    /// <summary>Which branch the pixel shader takes.</summary>
    /// <param name="hasBump">The first bump map is a texture.</param>
    /// <param name="hasBump2">The second is.</param>
    /// <param name="fancyBlending"><c>FANCY_BLENDING</c>: a <c>$blendmodulatetexture</c> is a texture.</param>
    /// <param name="normalMapAlphaEnvMapMask"><c>$normalmapalphaenvmapmask</c>.</param>
    /// <returns>The branch.</returns>
    public static SecondBumpMap Mode(bool hasBump, bool hasBump2, bool fancyBlending, bool normalMapAlphaEnvMapMask)
    {
        if (!hasBump)
        {
            return SecondBumpMap.None;
        }

        if (!hasBump2)
        {
            return SecondBumpMap.One;
        }

        return fancyBlending && normalMapAlphaEnvMapMask ? SecondBumpMap.MixedWithAlpha : SecondBumpMap.Mixed;
    }
}
