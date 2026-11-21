using System.Collections.Generic;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>
/// <c>vgui::MDLCache()</c> as a model panel reaches it: a model by path (<c>FindMDL</c>, mdlpanel.cpp:185) with the
/// studio header's skin table and body parts, and <c>FindBodygroupByName</c>/<c>::SetBodygroup</c> over them.
/// </summary>
/// <remarks><c>EntityModelSet</c> is the production cache; it already answers the bodygroup half for world players.</remarks>
public interface IMdlCache : IModelBodygroups
{
    /// <summary><c>FindMDL( pMDLName )</c>: the loaded model, or null for <c>MDLHANDLE_INVALID</c>.</summary>
    /// <param name="path">The model's path.</param>
    /// <returns>The model's frames, skins and parts, or null when it is not loaded.</returns>
    public PropModels.ModelFrames? FindMdl(string path);

    /// <summary>
    /// The studio render context's one view target the panels' draws share with the world's models (B725 5d), or null for a
    /// cache with no render context behind it (a panel then keeps its own).
    /// </summary>
    public StudioViewTarget? ViewTarget => null;

    /// <summary>
    /// Each buffer vertex's corner, as the model's geometry was packed for the device — what a flexed mesh's deltas are
    /// spread over (B779) — or null for a model not packed, which a panel then draws unflexed.
    /// </summary>
    /// <param name="path">The model's path.</param>
    /// <returns>The packed order, or null.</returns>
    public IReadOnlyList<int>? PackedOrder(string path) => null;
}
