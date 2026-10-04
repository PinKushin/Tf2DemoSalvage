using System;
using System.Collections.Generic;
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
    /// <summary><c>$fogenable</c>, read as an int by <c>R_SetFogVolumeState</c> (engine.dll <c>0x1800e0d98</c>).</summary>
    public bool FogEnable { get; init; }

    /// <summary>The proxies the material runs, in order.</summary>
    public IReadOnlyList<MaterialProxy> Proxies { get; init; } = [];

    /// <summary>The material's declared numeric variables, which the proxy chain starts from.</summary>
    public IReadOnlyDictionary<string, (float Red, float Green, float Blue)> Variables { get; init; } =
        new Dictionary<string, (float Red, float Green, float Blue)>();

    /// <summary>Every frame of <c>$normalmap</c>; the first is <see cref="NormalMap"/>.</summary>
    public IReadOnlyList<MapTexture> NormalFrames { get; init; } = [];

    /// <summary><c>$bumptransform</c> at a moment: the material's proxy chain run as each bind runs it.</summary>
    /// <param name="seconds">Playback time.</param>
    /// <returns>The transform the shader is handed (<c>water.cpp</c>: <c>SetVertexShaderTextureTransform( ..., BUMPTRANSFORM )</c>).</returns>
    /// <remarks>
    /// Runs <c>Sine</c> (<c>mathproxy.cpp:396</c>), <c>Equals</c> and <c>TextureTransform</c> (<c>matrixproxy.cpp:75</c>) in
    /// declaration order, which is the bind order; the result is the declared transform when no
    /// <c>TextureTransform</c> writes <c>$bumptransform</c>. Other proxies are not run here.
    /// </remarks>
    public TextureTransform BumpTransformAt(double seconds)
    {
        Dictionary<string, (float Red, float Green, float Blue)> values = new(Variables, StringComparer.OrdinalIgnoreCase);
        TextureTransform result = BumpTransform;

        (float Red, float Green, float Blue) Read(string? reference)
        {
            (string name, int component) = MaterialProxies.Reference(reference);

            return MaterialProxies.ReadComponent(values.GetValueOrDefault(name), component);
        }

        void Write(string? reference, float value)
        {
            (string name, int component) = MaterialProxies.Reference(reference);

            if (name.Length > 0)
            {
                values[name] = MaterialProxies.WriteComponent(values.GetValueOrDefault(name), component, value);
            }
        }

        foreach (MaterialProxy proxy in Proxies)
        {
            if (proxy.Name.Equals("Sine", StringComparison.OrdinalIgnoreCase))
            {
                Write(proxy.Argument("resultVar"), MaterialProxies.Sine(
                    seconds,
                    MaterialProxies.Number(proxy.Argument("sinePeriod"), 1f),
                    MaterialProxies.Number(proxy.Argument("sineMin"), 0f),
                    MaterialProxies.Number(proxy.Argument("sineMax"), 1f)));
            }
            else if (proxy.Name.Equals("Equals", StringComparison.OrdinalIgnoreCase))
            {
                Write(proxy.Argument("resultVar"), Read(proxy.Argument("srcVar1")).Red);
            }
            else if (proxy.Name.Equals("TextureTransform", StringComparison.OrdinalIgnoreCase) &&
                     MaterialProxies.Reference(proxy.Argument("resultVar")).Name.Equals("$bumptransform", StringComparison.OrdinalIgnoreCase))
            {
                (float, float) Pair(string key, (float, float) fallback) =>
                    proxy.Argument(key) is { Length: > 0 } name && values.TryGetValue(MaterialProxies.Reference(name).Name, out (float Red, float Green, float Blue) v)
                        ? (v.Red, v.Green)
                        : fallback;

                float rotate = proxy.Argument("rotateVar") is { Length: > 0 } angle ? Read(angle).Red : 0f;

                result = MaterialProxies.TextureTransformOf(
                    Pair("centerVar", (0.5f, 0.5f)), Pair("scaleVar", (1f, 1f)), rotate, Pair("translateVar", (0f, 0f)));
            }
        }

        return result;
    }

    /// <summary>Which <c>$normalmap</c> frame binds at a moment — an <c>AnimatedTexture</c> on <c>$normalmap</c>.</summary>
    /// <param name="seconds">Playback time.</param>
    /// <param name="frames">How many frames the texture has.</param>
    /// <returns>The frame; 0 with no such proxy or one frame.</returns>
    public int NormalFrameAt(double seconds, int frames)
    {
        foreach (MaterialProxy proxy in Proxies)
        {
            if (proxy.Name.Equals("AnimatedTexture", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(proxy.Argument("animatedTextureVar"), "$normalmap", StringComparison.OrdinalIgnoreCase))
            {
                return frames <= 1
                    ? 0
                    : MaterialProxies.AnimationFrame(
                        seconds,
                        MaterialProxies.Number(proxy.Argument("animatedTextureFrameRate"), MaterialProxies.DefaultAnimationRate),
                        frames);
            }
        }

        return 0;
    }

    /// <summary><c>$normalmap</c>'s <c>SHADER_PARAM</c> default.</summary>
    public const string DefaultNormalMap = "dev/water_normal";

    /// <summary>Reads a water material.</summary>
    /// <param name="material">The material.</param>
    /// <param name="normalFrames">Every frame of its normal map, first to last; null or empty when it could not be read.</param>
    /// <returns>The parameters.</returns>
    public static MapWater From(VmtMaterial material, IReadOnlyList<MapTexture>? normalFrames)
    {
        normalFrames ??= [];

        return Read(material, normalFrames.Count > 0 ? normalFrames[0] : null) with
        {
            FogEnable = material.Value("$fogenable") is { } fog && int.TryParse(
                fog.Trim().Split('.')[0], System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out int on) && on != 0,
            Proxies = material.Proxies,
            Variables = material.NumericValues(),
            NormalFrames = normalFrames,
        };
    }

    private static MapWater Read(VmtMaterial material, MapTexture? normalMap)
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
