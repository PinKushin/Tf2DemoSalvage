using Silk.NET.Direct3D11;

namespace Tf2DemoSalvage.Viewer3D;

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
    /// <summary><c>m_DepthBias_Decal</c>, in units of the buffer's smallest resolvable step.</summary>
    /// <remarks>
    /// **Valve's constant, on Valve's buffer format, for the first time.** Both previous attempts at
    /// it — 2026-08-14 and earlier on 2026-08-21 — ran against a <c>D32_FLOAT</c> buffer, where
    /// D3D11 scales the same integer by a data-dependent 2^(exponent−23) instead of a fixed 1/2²⁴.
    /// Neither was a test of this number. The format was only matched to the engine's in D48, after
    /// the second revert.
    ///
    /// **The one thing it depends on is the PROJECTION, and the projection is being fixed.**
    /// −0.015625 is a fraction of the depth RANGE. Under perspective, which is what Valve draws
    /// with, most of that range sits near the camera, so the offset is a fraction of a world unit
    /// where a decal actually is. Under the overhead ORTHOGRAPHIC camera depth is linear over the
    /// whole map's height, so the same fraction is about twenty-five world units — which is what
    /// painted a marking over the health pack standing on it, and what got this constant reverted.
    ///
    /// **That was a fact about the orthographic camera, and the orthographic camera is going out**
    /// (D49, owner's direction). So it is not a reason to hold a Valve constant at the wrong value.
    /// </remarks>
    internal const int ConstantBias = -262144;

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
