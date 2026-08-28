namespace Tf2DemoSalvage.Render;

/// <summary>
/// How much of a model one draw covers — <c>STUDIORENDER_DRAW_*</c>.
/// </summary>
/// <remarks>
/// **Valve's three values, from <c>public/istudiorender.h:100</c>**, and the first of them is the
/// one this project did not have:
///
/// <code>
/// STUDIORENDER_DRAW_ENTIRE_MODEL     = 0,
/// STUDIORENDER_DRAW_OPAQUE_ONLY      = 0x01,
/// STUDIORENDER_DRAW_TRANSLUCENT_ONLY = 0x02,
/// </code>
///
/// The engine reaches them through a pair of <c>DrawModel</c> flags: <c>STUDIO_TWOPASS</c> says
/// "draw half of me" and <c>STUDIO_TRANSPARENCY</c> says which half. Without <c>STUDIO_TWOPASS</c>
/// neither half is selected and the whole model draws — <c>c_func_areaportalwindow.cpp:95-99</c> is
/// the clearest statement of it, and <c>C_BaseEntity::DrawBrushModel</c> the same shape for brushes:
///
/// <code>
/// DrawBrushModelMode_t mode = DBM_DRAW_ALL;
/// if ( bTwoPass ) mode = bDrawingTranslucency ? DBM_DRAW_TRANSLUCENT_ONLY : DBM_DRAW_OPAQUE_ONLY;
/// </code>
///
/// **This renderer had only the last two, and applied them to every model.** Which is a faithful
/// implementation of two-pass drawing attached to nothing that decided whether a model was two-pass
/// — see <see cref="RenderGroups"/>. <see cref="EntireModel"/> is the missing default, and it is
/// default here as it is in the SDK, where it is literally zero.
/// </remarks>
public enum ModelPass
{
    /// <summary><c>STUDIORENDER_DRAW_ENTIRE_MODEL</c> — every mesh, whatever its material.</summary>
    /// <remarks>
    /// What a model drawn in exactly one pass gets, which is nearly all of them. Its blended meshes
    /// still blend and its solid meshes still write depth, because each carries its own render state
    /// — what it does NOT get is its solid half drawn early with the opaque geometry.
    /// </remarks>
    EntireModel,

    /// <summary><c>STUDIORENDER_DRAW_OPAQUE_ONLY</c> — the solid half of a two-pass model.</summary>
    OpaqueOnly,

    /// <summary><c>STUDIORENDER_DRAW_TRANSLUCENT_ONLY</c> — the blended half.</summary>
    TranslucentOnly,
}
