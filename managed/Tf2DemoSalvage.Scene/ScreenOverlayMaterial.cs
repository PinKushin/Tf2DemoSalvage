using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene;

/// <summary>A material the screen overlay slot can hold (<c>Core.Scene.ScreenOverlay</c>).</summary>
/// <param name="Name">The material, as the slot names it.</param>
/// <param name="Refract">
/// Its <c>Refract</c> parameters — what <c>PerformScreenOverlay</c>'s power-of-two branch draws over the frame copy
/// (viewrender.cpp:1228-1240). Null for an overlay this port draws as nothing (see <c>MapAssets.LoadScreenOverlays</c>).
/// </param>
/// <param name="Proxies">The material's proxies, run per frame with no entity bound.</param>
/// <param name="Variables">Its declared numeric parameters, which the proxies read and write.</param>
public sealed record ScreenOverlayMaterial(
    string Name,
    RefractMaterial? Refract,
    IReadOnlyList<MaterialProxy> Proxies,
    IReadOnlyDictionary<string, (float Red, float Green, float Blue)> Variables)
{
    /// <summary>Every frame of the Refract <c>$normalmap</c>, the first being <see cref="RefractMaterial.NormalMap"/>.</summary>
    /// <remarks>
    /// `BindTexture( SHADER_SAMPLER2, NORMALMAP, BUMPFRAME )` (refract_dx9_helper.cpp), <c>$bumpframe</c> written by
    /// <c>AnimatedTexture</c>: jarate, bleed and gas animate <c>water/tfwater001_normal</c> at 30 frames a second.
    /// </remarks>
    public IReadOnlyList<MapTexture> NormalFrames { get; init; } = [];

    /// <summary>
    /// The <c>$basetexture</c> of an overlay drawn by <c>render-&gt;ViewDrawFade</c> — the branch for a material that
    /// needs no frame copy (viewrender.cpp:1242-1246) — or null.
    /// </summary>
    /// <remarks>
    /// Only <c>effects/stealth_overlay</c> takes it: a translucent <c>VertexLitGeneric</c> over <c>effects/grey</c>, whose
    /// colour is black throughout (probe <c>vmt</c>: mean RGBA 0 0 0 108), so the overlay darkens the frame by the
    /// texture's alpha whatever lighting the engine's fade quad is drawn under (*interpolated*: the quad's lighting and
    /// texcoords are inside closed `engine.dll`; the black makes the first moot). <c>effects/imcookin</c> is additive, its
    /// <c>$color</c> written 0 by <c>BurnLevel</c> with no entity to read — `IVRenderView::ViewDrawFade( byte *color,
    /// IMaterial * )` (ivrenderview.h:255) is handed none — so it adds black and keeps no fade texture.
    /// </remarks>
    public MapTexture? Fade { get; init; }

    /// <summary>Which <see cref="NormalFrames"/> entry binds at a moment.</summary>
    /// <param name="seconds">`gpGlobals->curtime`.</param>
    /// <returns>The frame index; 0 for a still normal map.</returns>
    public int NormalFrameAt(double seconds) => MapWater.NormalFrameAt(Proxies, seconds, NormalFrames.Count);

    /// <summary>The overlay's parameters after its proxies run at a moment.</summary>
    /// <param name="seconds">`gpGlobals->curtime`, which the time-driven proxies read.</param>
    /// <returns>The <c>$refractamount</c>, gamma <c>$refracttint</c> and <c>$bumptransform</c> the draw binds.</returns>
    /// <remarks>
    /// **The proxies these materials run** (probe <c>vmt</c>, 2026-10-06): <c>Sine</c> on the invulnerability overlay's
    /// <c>$refractamount</c>; <c>Sine</c> then two <c>Equals</c> putting the bleed overlay's pulse into
    /// <c>$refracttint[1]</c> and <c>[2]</c>; <c>TextureScroll</c> on <c>$bumptransform</c>. <c>AnimatedTexture</c> on
    /// <c>$normalmap</c> is <see cref="NormalFrameAt"/>.
    /// </remarks>
    public (float RefractAmount, (float Red, float Green, float Blue) RefractTint, TextureTransform BumpTransform) Bind(
        double seconds)
    {
        if (Refract is not { } refract)
        {
            return (0f, (1f, 1f, 1f), TextureTransform.Identity);
        }

        Dictionary<string, (float Red, float Green, float Blue)> variables = new(Variables, StringComparer.OrdinalIgnoreCase)
        {
            ["$refractamount"] = (refract.RefractAmount, refract.RefractAmount, refract.RefractAmount),
            ["$refracttint"] = refract.RefractTint,
        };

        TextureTransform bump = TextureTransform.Identity;

        foreach (MaterialProxy proxy in Proxies)
        {
            if (proxy.Name.Equals("Sine", StringComparison.OrdinalIgnoreCase))
            {
                // CSineProxy::Init's defaults: sineperiod 1, sinemin 0, sinemax 1.
                float value = MaterialProxies.Sine(
                    seconds,
                    MaterialProxies.Number(proxy.Argument("sineperiod"), 1f),
                    MaterialProxies.Number(proxy.Argument("sinemin"), 0f),
                    MaterialProxies.Number(proxy.Argument("sinemax"), 1f));

                Write(variables, proxy.Argument("resultvar"), value);
            }
            else if (proxy.Name.Equals("Equals", StringComparison.OrdinalIgnoreCase))
            {
                (string source, int component) = MaterialProxies.Reference(proxy.Argument("srcvar1"));

                if (variables.TryGetValue(source, out (float Red, float Green, float Blue) read))
                {
                    Write(variables, proxy.Argument("resultvar"), MaterialProxies.ReadComponent(read, component).Red);
                }
            }
            else if (proxy.Name.Equals("TextureScroll", StringComparison.OrdinalIgnoreCase) &&
                     string.Equals(proxy.Argument("texturescrollvar"), "$bumptransform", StringComparison.OrdinalIgnoreCase))
            {
                bump = MaterialProxies.TextureScroll(
                    seconds,
                    MaterialProxies.Number(proxy.Argument("texturescrollrate"), 1f),
                    MaterialProxies.Number(proxy.Argument("texturescrollangle"), 0f),
                    MaterialProxies.Number(proxy.Argument("texturescale"), 1f));
            }
        }

        return (variables["$refractamount"].Red, variables["$refracttint"], bump);
    }

    private static void Write(
        Dictionary<string, (float Red, float Green, float Blue)> variables, string? reference, float value)
    {
        (string name, int component) = MaterialProxies.Reference(reference);

        if (name.Length == 0)
        {
            return;
        }

        variables[name] = MaterialProxies.WriteComponent(
            variables.TryGetValue(name, out (float Red, float Green, float Blue) into) ? into : (0f, 0f, 0f), component, value);
    }
}
