namespace Tf2DemoSalvage.Render;

/// <summary>A texture slot of a material, by what it holds.</summary>
public enum MaterialSampler
{
    /// <summary>`$basetexture`, `SHADER_SAMPLER0`.</summary>
    BaseTexture,

    /// <summary>`$basetexture2`, `SHADER_SAMPLER7`.</summary>
    BaseTexture2,

    /// <summary>The lightmap, `SHADER_SAMPLER1`.</summary>
    Lightmap,

    /// <summary>`$bumpmap`, `SHADER_SAMPLER4`: a normal map or an ssbump's three light weights.</summary>
    Bump,

    /// <summary>`$lightwarptexture`, the diffuse warp: a ramp the lighting indexes.</summary>
    DiffuseWarp,

    /// <summary>`$phongexponenttexture`: exponent, albedo tint and rim mask as numbers.</summary>
    PhongExponent,

    /// <summary>`$selfillummask`: how much of each texel glows.</summary>
    SelfIllumMask,
}

/// <summary>Whether the hardware linearizes a sampler's texels, as Valve's shaders set it.</summary>
/// <remarks>
/// **`lightmappedgeneric_dx9_helper.cpp:421-450` and 488.** A picture is stored with the gamma curve and is read through
/// it; light and directions are not. The lightmap's row is the HDR one, `EnableSRGBRead( SHADER_SAMPLER1, false )`,
/// because this renderer's lightmap is linear. The bump sampler is enabled and never given sRGB read. For models,
/// `vertexlitgeneric_dx9_helper.cpp:633` and 646 and `skin_dx9_helper.cpp:379` and 388 enable the diffuse warp, the
/// self-illum mask and the specular exponent map without it, and lines 356 and 361 load them without `TEXTUREFLAGS_SRGB`.
/// </remarks>
internal static class SamplerSrgb
{
    /// <summary>Whether a sampler reads through the sRGB curve.</summary>
    /// <param name="sampler">Which texture slot.</param>
    /// <returns>True for a picture, false for light or a direction.</returns>
    internal static bool ReadsAsSrgb(MaterialSampler sampler) =>
        sampler is MaterialSampler.BaseTexture or MaterialSampler.BaseTexture2;

    /// <summary>Whether a material's `$detail` reads through the sRGB curve, which depends on its shader's helper.</summary>
    /// <param name="shader">The VMT shader name.</param>
    /// <param name="mode">The resolved `$detailblendmode`.</param>
    /// <returns>True when the detail is sampled as a picture.</returns>
    /// <remarks>
    /// **Two helpers, two rules.** `lightmappedgeneric_dx9_helper.cpp:482` reads it through the curve for mode 1 alone;
    /// `vertexlitgeneric_dx9_helper.cpp:607`, which `UnlitGeneric` shares, for every mode but 0. Mod2x is raw in both,
    /// which is what makes its 0.5 grey a neutral once doubled.
    /// </remarks>
    internal static bool DetailReadsAsSrgb(string shader, int mode) =>
        shader.Equals("VertexLitGeneric", System.StringComparison.OrdinalIgnoreCase) ||
        shader.Equals("UnlitGeneric", System.StringComparison.OrdinalIgnoreCase)
            ? mode != 0
            : mode == 1;
}
