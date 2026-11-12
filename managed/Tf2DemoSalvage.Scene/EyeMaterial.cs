using System;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene;

/// <summary>Which of the three face shaders a material is drawn with (B725).</summary>
public enum EyeShader
{
    /// <summary>None of the three.</summary>
    None = 0,

    /// <summary><c>EyeRefract</c>: every TF2 player's eyes.</summary>
    EyeRefract = 1,

    /// <summary><c>Eyes</c>: HL2's eyes.</summary>
    Eyes = 2,

    /// <summary><c>Teeth</c> without a <c>$bumpmap</c>.</summary>
    Teeth = 3,
}

/// <summary>What the <c>EyeRefract</c>, <c>Eyes</c> and <c>Teeth</c> shaders draw a mesh with, beside its base slot (B725).</summary>
/// <param name="Shader">Which shader.</param>
/// <param name="HalfLambert"><c>MATERIAL_VAR_HALFLAMBERT</c>, the vertex shader's <c>HALFLAMBERT</c> combo; never for teeth.</param>
/// <param name="Cornea">EyeRefract's <c>$corneatexture</c>, read raw; null otherwise.</param>
/// <param name="AmbientOcclusion">EyeRefract's <c>$ambientoccltexture</c>, sRGB; null otherwise.</param>
/// <param name="Iris">Eyes' <c>$iris</c>, sRGB; null otherwise (EyeRefract's iris is the base slot).</param>
/// <param name="Dilation"><c>$dilation</c>, 0.5 unstated.</param>
/// <param name="Glossiness"><c>$glossiness</c>, 1 unstated.</param>
/// <param name="CorneaBumpStrength"><c>$corneabumpstrength</c>, 1 unstated.</param>
/// <param name="AmbientOcclusionColour"><c>$ambientocclcolor</c>, 0.33 grey unstated, sent raw (<c>c5</c>).</param>
/// <param name="EyeballRadius"><c>$eyeballradius</c>, 0.5 unstated.</param>
/// <param name="ParallaxStrength"><c>$parallaxstrength</c>, 0.25 unstated.</param>
/// <param name="Forward">Teeth's <c>$forward</c> as the material states it, <c>[1 0 0]</c> unstated.</param>
/// <param name="IllumFactor">Teeth's <c>$illumfactor</c>, 1 unstated.</param>
/// <param name="RaytraceSphere">EyeRefract's <c>$raytracesphere</c>, the <c>RAYTRACESPHERE</c> combo; off unstated.</param>
/// <param name="SphereTexKill"><c>$spheretexkillcombo</c> under the ray cast.</param>
/// <remarks>
/// **The defaults are <c>InitParams_Eyes_Refract</c>'s** (<c>eye_refract_helper.cpp:43-87</c>, values in
/// <c>eye_refract_helper.h:50-62</c>), which set each where the material leaves it undefined; the parameters' own
/// declared defaults (<c>eye_refract.cpp:14-39</c>) differ for <c>$dilation</c>, <c>$ambientocclcolor</c> and
/// <c>$eyeballradius</c>. *Interpolated:* that a declared default does not count as defined, so the helper's value is
/// the one drawn — the material system that decides it is closed. Every TF2 eye states all three, so no TF2 eye depends on it.
/// </remarks>
public sealed record EyeMaterial(
    EyeShader Shader,
    bool HalfLambert,
    MapTexture? Cornea = null,
    MapTexture? AmbientOcclusion = null,
    MapTexture? Iris = null,
    float Dilation = EyeMaterial.DefaultDilation,
    float Glossiness = 1f,
    float CorneaBumpStrength = 1f,
    (float Red, float Green, float Blue) AmbientOcclusionColour = default,
    float EyeballRadius = EyeMaterial.DefaultEyeballRadius,
    float ParallaxStrength = EyeMaterial.DefaultParallaxStrength,
    (float X, float Y, float Z) Forward = default,
    float IllumFactor = 1f,
    bool RaytraceSphere = false,
    bool SphereTexKill = false)
{
    /// <summary><c>kDefaultDilation</c>.</summary>
    public const float DefaultDilation = 0.5f;

    /// <summary><c>kDefaultEyeballRadius</c>.</summary>
    public const float DefaultEyeballRadius = 0.5f;

    /// <summary><c>kDefaultParallaxStrength</c>.</summary>
    public const float DefaultParallaxStrength = 0.25f;

    /// <summary><c>kDefaultAmbientOcclColor</c>.</summary>
    public const float DefaultAmbientOcclusionGrey = 0.33f;

    /// <summary>Which face shader a material names, or null for any other (the fallback chain's names included).</summary>
    /// <param name="shader">The VMT's shader name.</param>
    /// <returns>The shader, or null.</returns>
    public static EyeShader? ShaderOf(string shader)
    {
        ArgumentNullException.ThrowIfNull(shader);

        return shader.ToUpperInvariant() switch
        {
            "EYEREFRACT" or "EYEREFRACT_DX9" => EyeShader.EyeRefract,
            "EYES" or "EYES_DX9" => EyeShader.Eyes,
            "TEETH" or "TEETH_DX9" => EyeShader.Teeth,
            _ => null,
        };
    }

    /// <summary>Reads a face material's parameters.</summary>
    /// <param name="material">The material.</param>
    /// <param name="shader">Its shader.</param>
    /// <param name="cornea">EyeRefract's cornea, decoded.</param>
    /// <param name="occlusion">EyeRefract's occlusion map, decoded.</param>
    /// <param name="iris">Eyes' iris, decoded.</param>
    /// <returns>The parameters.</returns>
    public static EyeMaterial Read(
        VmtMaterial material, EyeShader shader, MapTexture? cornea, MapTexture? occlusion, MapTexture? iris)
    {
        ArgumentNullException.ThrowIfNull(material);

        (float Red, float Green, float Blue) occlusionColour = material.Value("$ambientocclcolor") is not null
            ? material.Colour("$ambientocclcolor")
            : (DefaultAmbientOcclusionGrey, DefaultAmbientOcclusionGrey, DefaultAmbientOcclusionGrey);

        (float Red, float Green, float Blue) forward = material.Value("$forward") is not null
            ? material.Colour("$forward")
            : (1f, 0f, 0f);

        return new EyeMaterial(
            shader,

            // `teeth_vs20` passes `false` to DoLighting whatever the flag (`teeth_vs20.fxc:109`).
            shader != EyeShader.Teeth && material.IsHalfLambert,
            cornea,
            occlusion,
            iris,
            material.Number("$dilation", DefaultDilation),
            material.Number("$glossiness", 1f),
            material.Number("$corneabumpstrength", 1f),
            occlusionColour,
            material.Number("$eyeballradius", DefaultEyeballRadius),
            material.Number("$parallaxstrength", DefaultParallaxStrength),
            (forward.Red, forward.Green, forward.Blue),
            material.Number("$illumfactor", 1f),

            // `kDefaultRaytraceSphere` and `kDefaultSphereTexKillCombo` are 0; the texkill combo is skipped without the ray
            // cast (`eye_refract_ps2x.fxc:32-33`).
            material.Number("$raytracesphere", 0f) != 0f,
            material.Number("$raytracesphere", 0f) != 0f && material.Number("$spheretexkillcombo", 0f) != 0f);
    }
}
