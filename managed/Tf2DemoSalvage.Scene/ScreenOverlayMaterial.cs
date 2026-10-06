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
    /// <summary>The overlay's parameters after its proxies run at a moment.</summary>
    /// <param name="seconds">`gpGlobals->curtime`, which the time-driven proxies read.</param>
    /// <returns>The <c>$refractamount</c>, gamma <c>$refracttint</c> and <c>$bumptransform</c> the draw binds.</returns>
    /// <remarks>
    /// **The proxies these materials run** (probe <c>vmt</c>, 2026-10-06): <c>Sine</c> on the invulnerability overlay's
    /// <c>$refractamount</c>; <c>Sine</c> then two <c>Equals</c> putting the bleed overlay's pulse into
    /// <c>$refracttint[1]</c> and <c>[2]</c>; <c>TextureScroll</c> on <c>$bumptransform</c>. **Not ported, named:**
    /// <c>AnimatedTexture</c> on <c>$normalmap</c> — the normal map draws its first frame.
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
