using System;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// That the two whole-model override materials join the material table on a real install (B325).
/// </summary>
/// <remarks>
/// **These two are in no map's material table and no model's, and that is the whole reason this
/// exists.** A map's materials come from its BSP and a model's from its `.mdl`; `gold_player.vmt` is
/// named by neither — `C_TFRagdoll::CreateTFRagdoll` passes the string to `m_MaterialOverride.Init`
/// directly (`c_tf_player.cpp:981`), so nothing about a loaded map or a loaded model would ever pull
/// it in. It has to be asked for by name, and a name asked for and never found fails silently: the
/// renderer looks the path up, misses, and draws the model's own materials, which is exactly what an
/// unimplemented override looks like.
///
/// **This asserts the OUTPUT of the load, not that the code path exists**
/// (`docs/memory/measure-the-output-not-the-capability.md`). A predicate-shaped test — "the loader
/// was called with two paths" — passes against an install where neither resolves.
/// </remarks>
public sealed class OverrideMaterialLoadingTests
{
    /// <remarks>
    /// **Both, by name, rather than a count.** A count of two is satisfied by loading one material
    /// twice, and the two paths are typed out in one array in the loader — a transposition there
    /// would give the right total and the wrong contents.
    /// </remarks>
    [Test]
    public void OverrideMaterials_AfterALoadFromTheGameArchives_HoldBothVmtsTheEngineNames()
    {
        MapAssets assets = MapCache.Load();

        assets.OverrideMaterials.Keys.OrderBy(key => key, StringComparer.Ordinal).ShouldBe(
            [RagdollAppearance.GoldMaterial, RagdollAppearance.IceMaterial]);
    }

    /// <remarks>
    /// **The index is into the ordinary table, appended after everything that indexes it.** An
    /// override landing anywhere below `BrushMaterialCount` would mean it had displaced a brush
    /// material, and every face in the map would then paint from the wrong entry.
    /// </remarks>
    [Test]
    public void OverrideMaterials_TheIndicesTheyTook_AreAppendedPastEveryMapAndModelMaterial()
    {
        MapAssets assets = MapCache.Load();

        int gold = assets.OverrideMaterials[RagdollAppearance.GoldMaterial];
        int ice = assets.OverrideMaterials[RagdollAppearance.IceMaterial];

        gold.ShouldBeGreaterThanOrEqualTo(assets.BrushMaterialCount);
        ice.ShouldBe(gold + 1, "they are appended in the order the loader lists them");

        assets.Materials[gold].Name.ShouldBe("models/player/shared/gold_player");
        assets.Materials[ice].Name.ShouldBe("models/player/shared/ice_player");
    }

    /// <remarks>
    /// **This is the assertion the first implementation would have failed, and it is the point of
    /// the whole design.** That version stored one `MapTexture` per path and swapped the base
    /// texture at the bind, which draws something — a flat swatch — with the PLAYER material's
    /// cubemap, phong, detail and blend state still applied. Every test above would have passed
    /// against it.
    ///
    /// So the claim under test is that the WHOLE material came across, and the two VMTs carry a
    /// clean discriminating pair: ice declares
    /// `$lightwarptexture models/player/shared/ice_player_lightwarp` and gold declares none.
    ///
    /// **That asymmetry is the control.** One entry returned under both keys, or an index that
    /// pointed at the player material, would answer the same light warp for both.
    ///
    /// **Gold's cubemap was a documented gap here for exactly one commit, and is now asserted**
    /// (B326). `$envmap cubemaps/cubemap_gold001` is nearly all of what makes gold look like metal
    /// rather than brown, and it arrived as nothing because the VMT declares it inside a `"&gt;=DX90"`
    /// block that the reader did not descend into — while `$envmaptint [1.5 1.2 .2]`, outside the
    /// block, did arrive. The material carried the tint of a reflection it had no cubemap for.
    /// </remarks>
    [Test]
    public void OverrideMaterials_TheEntriesTheyPointAt_CarryTheirOwnPhongCubemapAndWarps()
    {
        MapAssets assets = MapCache.Load();

        int gold = assets.OverrideMaterials[RagdollAppearance.GoldMaterial];
        int ice = assets.OverrideMaterials[RagdollAppearance.IceMaterial];

        assets.Phong[gold].ShouldNotBeNull("gold declares $phong 1");
        assets.Phong[ice].ShouldNotBeNull("ice declares $phong 1");

        assets.Cubemaps[gold].ShouldNotBeNull(
            "gold reflects cubemaps/cubemap_gold001, declared inside its >=DX90 block (B326)");

        assets.LightWarps[ice].ShouldNotBeNull(
            "ice declares $lightwarptexture and gold declares none");

        assets.LightWarps[gold].ShouldBeNull(
            "gold declares no light warp — the control on the line above");
    }
}
