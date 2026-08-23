using Silk.NET.Direct3D11;

namespace Tf2DemoSalvage.Render;

/// <summary>
/// The render state a surface marking is drawn with — Valve's values, in Valve's units.
/// </summary>
/// <remarks>
/// **Extracted for the same reason as <see cref="BlendStates"/>: a conformance test has to be able
/// to reach it.** These were literals inside the two methods that build the states, so the only way
/// to check any of them against Valve's was to read the renderer.
///
/// <c>DecalRenderStateConformanceTests</c> parses Valve's numbers out of
/// <c>materialsystem_config.h</c> and compares them against the fields below, and the states
/// themselves are built from these fields in <c>WorldRenderer</c> — so the value the test reads is
/// the value the state is made of.
///
/// **The units were settled from `togl`, and that reading replaced an inference that was wrong.**
/// This project carried a note saying Valve's <c>m_DepthBias_Decal = -262144</c> was a Direct3D 9
/// number that could not carry to Direct3D 11, because "D3D9's is a float added to depth, D3D11's
/// an integer scaled by the buffer format". Valve's own D3D9-to-OpenGL layer says otherwise —
/// <c>public/togl/linuxwin/dxabstract.h:966</c>:
///
/// <code>
/// case D3DRS_DEPTHBIAS:            // kGLDepthBias
/// {
///     // the value in the dword is actually a float
///     float fvalue = *(float*)&amp;Value;
///     gl.m_DepthBias.units = fvalue;
/// </code>
///
/// <c>units</c> is the second argument of <c>glPolygonOffset(factor, units)</c>, and OpenGL scales
/// it by **r, the smallest resolvable depth difference** — 1/2²⁴ on a 24-bit fixed-point buffer.
/// Direct3D 11 defines its integer <c>DepthBias</c> with the same scale on a UNORM format:
/// <c>bias = DepthBias · r + SlopeScaledDepthBias · maxDepthSlope</c>.
///
/// **So it is one quantity under three APIs, and Valve's number transfers unchanged.** −262144 · r
/// is −0.015625 of the depth range, whichever of the three draws it.
/// </remarks>
internal static class DecalState
{
    /// <summary>No constant bias, because an overlay's shader never asks for one.</summary>
    /// <remarks>
    /// **Zero, and it is Valve's answer rather than a tuning of ours.** Valve's
    /// <c>m_DepthBias_Decal</c> really is −262144, it really is <c>glPolygonOffset</c>'s
    /// <c>units</c>, and 262144 being 2¹⁸ makes it exactly 1/64 of a 24-bit depth range. All of
    /// that is true and none of it applies here, because of the question nobody asked for three
    /// attempts: **which surfaces does Valve apply it to?**
    ///
    /// **A polygon offset in Source is a property of the SHADER.** <c>EnablePolyOffset</c> is
    /// declared once in the entire published tree, on <c>IShaderShadow</c>
    /// (<c>public/shaderapi/ishadershadow.h:255</c>), and it is called only from
    /// <c>materialsystem/stdshaders</c> — by <c>decal</c>, <c>decalmodulate</c>, the portal
    /// overlays and a terrain test shader. There is no equivalent on <c>IMaterialSystem</c>,
    /// <c>IMatRenderContext</c>, <c>IMesh</c> or <c>IShaderAPI</c>, so the engine cannot add one to
    /// a material whose shader did not request it. <c>lightmappedgeneric_dx9.cpp</c> does not
    /// request it.
    ///
    /// An <c>info_overlay</c> is ordinarily LightmappedGeneric. So Valve's decal bias governs
    /// bullet holes and sprays, and never the stripes painted down a corridor — which is what this
    /// project kept applying it to.
    ///
    /// **Why the wrong answer looked right three times.** −0.015625 is a fraction of the depth
    /// RANGE, and window depth goes as z ≈ 1 − N/d, so the world distance it represents grows with
    /// d². At <c>VIEW_NEARZ</c> 7 a marking 500 units away tests as though it were at 236, in front
    /// of everything between. The owner has now seen that picture three times — 2026-08-14,
    /// 2026-08-21, and again on 2026-08-23 as "there are overlays showing through everywhere" —
    /// and each time the arithmetic was available and the mechanism was not checked. B70.
    ///
    /// **What holds the markings down instead** is B134: a fragment is the wall's own vertices
    /// clipped in the wall's own plane, so it is coplanar by construction rather than projected
    /// onto the surface, and a <c>LESS_EQUAL</c> comparison needs no constant term. Only the
    /// slope-scaled term below is still required, and for a different reason.
    /// </remarks>
    internal const int ConstantBias = 0;

    /// <summary><c>m_SlopeScaleDepthBias_Decal</c>, a multiple of the polygon's depth gradient.</summary>
    /// <remarks>
    /// **Needed, and measured rather than assumed.** With both terms at zero the stripes on
    /// cp_process tore into diagonal hatching where the overlay and its wall alternate per pixel. A
    /// fragment is the face's polygon CLIPPED, so it is a differently triangulated piece of the same
    /// plane, and two triangulations interpolate depth slightly differently across a pixel. That
    /// difference is proportional to the gradient, which is what this term scales — so it is what
    /// <c>SHADER_POLYOFFSET_DECAL</c> is trading against, and the answer to "name the trade" (D46).
    /// </remarks>
    internal const float SlopeScaledBias = -0.5f;

    /// <summary>Back faces are culled, as <c>MATERIAL_CULLMODE_CCW</c> has the engine do.</summary>
    /// <remarks>
    /// **B135.** The overlay state was copied from the both-sided one the world uses, so an overlay
    /// drew on the far side of a wall as well as the near one — cp_process's REDSTONE CARGO
    /// lettering appeared MIRRORED through its own silo, which is the back face of the overlay seen
    /// from behind.
    /// </remarks>
    internal const CullMode Cull = CullMode.Back;

    /// <summary>A marking never writes depth.</summary>
    /// <remarks>
    /// <c>EnableDepthWrites( false )</c>, <c>DecalModulate_dx9.cpp:66</c> — and the same line in
    /// every other decal shader, which is what makes it the convention rather than one shader's
    /// special case.
    ///
    /// **Writing depth is what let a stripe hide a pipe (B135).** An overlay marks a wall that has
    /// already written its own depth; writing again — especially through a rasteriser bias, which
    /// puts a NEARER value in the buffer than the wall ever had — leaves everything drawn afterwards
    /// testing against a surface that does not exist.
    /// </remarks>
    internal const bool WritesDepth = false;

    /// <summary>Depth comparison the overlay pass draws with.</summary>
    /// <remarks>
    /// **<c>LessEqual</c>, because an overlay fragment is coplanar with the wall by construction.**
    /// Since B134 it is built from that wall's own vertices, clipped in the wall's own plane — so it
    /// rasterises to exactly the wall's depth, and a comparison that rejects equality would reject
    /// the marking outright.
    /// </remarks>
    internal const ComparisonFunc DepthFunc = ComparisonFunc.LessEqual;
}
