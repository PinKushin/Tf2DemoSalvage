namespace Tf2DemoSalvage.Render;

/// <summary>Valve's per-surface debug visualisations, as a set of switches.</summary>
/// <param name="DrawFlat">
/// <c>mat_drawflat</c> — the material's texture is replaced by flat white, leaving the lighting and
/// the geometry. `MaterialSystem_Config_t::bDrawFlat`. Answers "is that shape in the model or is it
/// painted into the texture", which a textured picture cannot separate.
/// </param>
/// <param name="Luxels">
/// <c>mat_luxels</c> — Valve's luxel grid drawn at the LIGHTMAP coordinate, so one square is one
/// baked lighting sample. `vertexlitgeneric_dx9_helper.cpp:35` declares it and line 1271 binds
/// <c>TEXTURE_DEBUG_LUXELS</c>. Answers "how coarse is the light here", which is what a shadow that
/// should be sharp and is not actually is.
/// </param>
/// <param name="NormalMaps">
/// <c>mat_normalmaps</c> — the surface normal drawn as colour rather than lit with.
/// `MaterialSystem_Config_t::bShowNormalMap`. A correctly decoded tangent-space normal map reads
/// lilac where the surface is flat, so a wrong decode is obvious rather than merely odd-looking.
/// </param>
/// <param name="LeafVis">
/// <c>mat_leafvis</c> — the BSP leaf the camera stands in, drawn as a box. Unlike the others this
/// is not a per-surface substitution: it is an annotation, so it is drawn as lines rather than in
/// the pixel shader and takes no register.
/// </param>
/// <param name="ShowLowResImage">
/// <c>mat_showlowresimage</c> — the material is drawn from the tiny copy of itself that every VTF
/// stores ahead of its mip chain, rather than from the texture. The only one of these that needed
/// DATA rather than a shader branch, which is why it was implemented last: the thumbnail had always
/// been skipped on the way past.
/// <para>
/// **Its honest value here is lower than the rest of the set**, and that is worth saying next to it.
/// The others each answer a question a textured picture cannot — geometry against texture, lightmap
/// coarseness, a bad tangent decode. This one mostly answers what the thumbnail looks like, and the
/// load-bearing fact about the thumbnail (that it is always DXT1, so the skip past it is the right
/// size) is pinned by <c>VtfLowResolutionConformanceTests</c> rather than by looking. Implemented
/// for parity with Valve's set, on the owner's call, having already read the data.
/// </para>
/// </param>
/// <param name="BumpBasis">
/// <c>mat_bumpbasis</c> — which of Valve's three lightmap basis vectors a surface leans on, as red,
/// green and blue. Uses the weights the bumped lighting already computes (`bumpvects.h`), so it
/// shows the quantity actually in use rather than a parallel calculation that could disagree with
/// it. A flat surface leans evenly and comes out grey.
/// </param>
/// <remarks>
/// **One record rather than a growing parameter list**, and one shader register rather than one per
/// mode. Valve's own budget is the reason: `common_vs_fxc.h` reserves c0–c37 for the engine and
/// gives a shader twelve float4s of its own (`SHADER_SPECIFIC_CONST_0..9` at c38–c47, plus c14 and
/// c15), so an entire Source shader's parameters fit in twelve registers by packing several values
/// into the components of one. A register per debug feature would exhaust that at a dozen features;
/// Valve never needed to spend them that way and neither does this.
///
/// **All of these are `FCVAR_CHEAT` in the engine and none are gated here** — see D75. There is no
/// server to protect and no opponent to gain an advantage over.
/// </remarks>
public readonly record struct DebugModes(
    bool DrawFlat = false,
    bool Luxels = false,
    bool NormalMaps = false,
    bool BumpBasis = false,
    bool LeafVis = false,
    bool ShowLowResImage = false)
{
    /// <summary>Everything off, which is what the viewer draws normally.</summary>
    public static DebugModes None => default;

    /// <summary>Whether any mode is on, so callers can skip work the modes make pointless.</summary>
    public bool Any =>
        DrawFlat || Luxels || NormalMaps || BumpBasis || LeafVis || ShowLowResImage;
}
