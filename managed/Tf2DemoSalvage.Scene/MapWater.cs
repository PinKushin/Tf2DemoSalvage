using System;
using System.Linq;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene;

/// <summary>A <c>Water</c> material's shading parameters, after <c>water.cpp</c>'s <c>SHADER_INIT_PARAMS</c>.</summary>
/// <param name="View">What <c>DetermineWaterRenderInfo</c> reads.</param>
/// <param name="NormalMap"><c>$normalmap</c>, decoded; null when it could not be read.</param>
/// <param name="RefractTint"><c>$refracttint</c>, gamma, white by default.</param>
/// <param name="ReflectTint"><c>$reflecttint</c>, gamma, white by default.</param>
/// <param name="RefractAmount"><c>$refractamount</c>, 0 by default.</param>
/// <param name="ReflectAmount"><c>$reflectamount</c>, 0.8 by default.</param>
/// <param name="FogColor"><c>$fogcolor</c>, gamma; red when undeclared, with Valve's warning.</param>
/// <param name="FogStart"><c>$fogstart</c>.</param>
/// <param name="FogEnd"><c>$fogend</c>.</param>
/// <param name="AboveWater"><c>$abovewater</c>, 1 when undeclared.</param>
/// <param name="Scroll1"><c>$scroll1</c>, zero when undeclared; a non-zero x selects the three-sample <c>MULTITEXTURE</c> combo.</param>
/// <param name="Scroll2"><c>$scroll2</c>.</param>
/// <param name="BumpTransform"><c>$bumptransform</c> as declared.</param>
/// <param name="NoFresnel"><c>$nofresnel</c>.</param>
/// <param name="ReflectBlendFactor"><c>$reflectblendfactor</c>, 1 by default.</param>
/// <param name="BlurRefract"><c>$blurrefract</c> — the <c>BLURRY_REFRACT</c> combo.</param>
/// <param name="CheapWaterStart"><c>$cheapwaterstartdistance</c>, 500 by default.</param>
/// <param name="CheapWaterEnd"><c>$cheapwaterenddistance</c>, 1000 by default.</param>
/// <param name="FollowsViewLod">The material runs the <c>WaterLOD</c> proxy, which overwrites both distances with
/// the view's own every bind (<c>WaterLODMaterialProxy.cpp:61-70</c>).</param>
/// <param name="BottomMaterial"><c>$bottommaterial</c>, the material drawn from inside the volume.</param>
public sealed record MapWater(
    WaterMaterialParameters View,
    MapTexture? NormalMap,
    (float Red, float Green, float Blue) RefractTint,
    (float Red, float Green, float Blue) ReflectTint,
    float RefractAmount,
    float ReflectAmount,
    (float Red, float Green, float Blue) FogColor,
    float FogStart,
    float FogEnd,
    bool AboveWater,
    (float X, float Y) Scroll1,
    (float X, float Y) Scroll2,
    TextureTransform BumpTransform,
    bool NoFresnel,
    float ReflectBlendFactor,
    bool BlurRefract,
    float CheapWaterStart,
    float CheapWaterEnd,
    bool FollowsViewLod,
    string? BottomMaterial)
{
    /// <summary><c>$normalmap</c>'s <c>SHADER_PARAM</c> default.</summary>
    public const string DefaultNormalMap = "dev/water_normal";

    /// <summary>Reads a water material.</summary>
    /// <param name="material">The material.</param>
    /// <param name="normalMap">Its decoded normal map.</param>
    /// <returns>The parameters.</returns>
    public static MapWater From(VmtMaterial material, MapTexture? normalMap)
    {
        ArgumentNullException.ThrowIfNull(material);

        (float, float) Vec2(string key) =>
            material.Value(key) is null ? (0f, 0f) : (material.Colour(key).Red, material.Colour(key).Green);

        bool Set(string key) => material.Value(key) is { } text && int.TryParse(
            text.Trim().Split('.')[0], System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out int value) && value != 0;

        return new(
            WaterMaterialParameters.From(material),
            normalMap,
            material.Colour("$refracttint"),
            material.Colour("$reflecttint"),
            material.Number("$refractamount", 0f),
            material.Number("$reflectamount", 0.8f),
            material.Value("$fogcolor") is null ? (1f, 0f, 0f) : material.Colour("$fogcolor"),
            material.Number("$fogstart", 0f),
            material.Number("$fogend", 0f),
            material.Value("$abovewater") is null || Set("$abovewater"),
            Vec2("$scroll1"),
            Vec2("$scroll2"),
            MaterialProxies.TextureTransformFrom(material.Value("$bumptransform")),
            Set("$nofresnel"),
            material.Number("$reflectblendfactor", 1f),
            Set("$blurrefract"),
            material.Number("$cheapwaterstartdistance", 500f),
            material.Number("$cheapwaterenddistance", 1000f),
            material.Proxies.Any(proxy => proxy.Name.Equals("WaterLOD", StringComparison.OrdinalIgnoreCase)),
            material.Value("$bottommaterial"));
    }
}
