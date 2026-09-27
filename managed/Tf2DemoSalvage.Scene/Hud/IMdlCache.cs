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
}
