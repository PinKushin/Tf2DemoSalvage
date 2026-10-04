using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Render;

/// <summary>Whether a model's materials are translucent — a flag on the MODEL, asked once (B262).</summary>
/// <remarks>
/// **Read from engine.dll, not inferred.** <c>C_BaseEntity::IsTransparent</c> asks <c>modelinfo->IsTranslucent(model)</c>
/// (<c>c_baseentity.cpp:1825</c>); <c>CModelInfoClient</c>'s implementation (vtable <c>0x1803af8b8</c> slot 12,
/// <c>0x1801cabf0</c>) is a single bit on the model, <c>[model + 0x24] &amp; 2</c>. Only <c>Mod_RecomputeTranslucency</c>
/// (<c>0x1801046f0</c>) writes it — from the materials of one skin and body — and the SDK's client calls that for
/// detail models alone (<c>detailobjectsystem.cpp:2809</c>). So an entity's skin or body never re-asks it; what the
/// engine recomputes every frame is the GROUP, from this flag, the alpha and the render mode
/// (<c>ComputeFxBlend</c> → <c>SetRenderGroup( GetRenderGroup() )</c>, <c>:3545</c>, <c>:5661-5701</c>), which
/// <c>RenderGroups</c> does here per frame on top of this.
///
/// **Interpolated:** the skin and body the flag is first computed with. The engine's load-time call was not located;
/// the default skin and body (0, 0) are what <c>Device3D.Classify</c> passes.
/// </remarks>
public sealed class TranslucencyCache
{
    private readonly Dictionary<string, bool> _byModel = new(StringComparer.Ordinal);

    /// <summary>The model's translucency flag, asked once.</summary>
    /// <param name="model">The model path.</param>
    /// <param name="ask">Asks its materials.</param>
    /// <returns>Whether it has a translucent material.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public bool For(string model, Func<bool> ask)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(ask);

        if (!_byModel.TryGetValue(model, out bool translucent))
        {
            translucent = ask();
            _byModel[model] = translucent;
        }

        return translucent;
    }

    /// <summary>Forgets every model — a new map.</summary>
    public void Clear() => _byModel.Clear();
}
