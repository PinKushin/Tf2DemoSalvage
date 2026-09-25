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
}

/// <summary>Whether the hardware linearizes a sampler's texels, as Valve's shaders set it.</summary>
/// <remarks>
/// **`lightmappedgeneric_dx9_helper.cpp:421-450` and 488.** A picture is stored with the gamma curve and is read through
/// it; light and directions are not. The lightmap's row is the HDR one, `EnableSRGBRead( SHADER_SAMPLER1, false )`,
/// because this renderer's lightmap is linear. The bump sampler is enabled and never given sRGB read.
/// </remarks>
internal static class SamplerSrgb
{
    /// <summary>Whether a sampler reads through the sRGB curve.</summary>
    /// <param name="sampler">Which texture slot.</param>
    /// <returns>True for a picture, false for light or a direction.</returns>
    internal static bool ReadsAsSrgb(MaterialSampler sampler) =>
        sampler is MaterialSampler.BaseTexture or MaterialSampler.BaseTexture2;
}
