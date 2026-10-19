using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene;

/// <summary>What a VGUI material's `UnlitGeneric` does beyond texture times vertex colour: its `$detail` and its distance alpha.</summary>
/// <param name="Detail">
/// The `$detail` texture and how it combines, or null. Any `$detail` material combines it — `TextureCombine` runs under
/// `#if DETAILTEXTURE` (vertexlit_and_unlit_generic_ps2x.fxc:262-279), not only inside `#if DISTANCEALPHA`.
/// </param>
/// <param name="DistanceAlpha">The `$distancealpha` parameters, or null.</param>
public sealed record VguiShading(MapDetail? Detail, VguiDistanceAlpha? DistanceAlpha)
{
    /// <summary>The shading a VMT asks for, or null when it names neither a detail nor distance alpha.</summary>
    /// <param name="material">The parsed VMT.</param>
    /// <param name="detail">The material's resolved `$detail` texture, or null.</param>
    /// <returns>The shading, or null.</returns>
    /// <remarks>
    /// **The mode is the VMT's.** `vertexlitgeneric_dx9_helper.cpp:422` reads `$detailblendmode` as written: unlike
    /// LightmappedGeneric it forces no ssbump mode, so the resolved world detail's mode is not this one.
    ///
    /// **The tint is white, whatever the VMT says.** `UnlitGeneric` declares no `$detailtint` and leaves
    /// `m_nDetailTint` at -1 (unlitgeneric_dx9.cpp:72-89; the vars' constructor memsets 0xFF), so the helper takes its
    /// else branch and sets c10 to 1 1 1 (vertexlitgeneric_dx9_helper.cpp:988-993).
    /// </remarks>
    public static VguiShading? Read(VmtMaterial material, MapTexture? detail)
    {
        System.ArgumentNullException.ThrowIfNull(material);

        VguiDistanceAlpha? distanceAlpha = VguiDistanceAlpha.Read(material.Value);
        MapDetail? combined = detail is { } texture
            ? new MapDetail(texture, material.DetailScale, material.DetailBlendFactor, material.DetailBlendMode, (1f, 1f, 1f))
            : null;

        return combined is null && distanceAlpha is null ? null : new VguiShading(combined, distanceAlpha);
    }
}
