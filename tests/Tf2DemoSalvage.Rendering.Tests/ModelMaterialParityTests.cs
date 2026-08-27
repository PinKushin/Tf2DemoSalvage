using System;
using System.IO;
using System.Linq;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// Whether a model's materials keep everything a brush material keeps.
/// </summary>
/// <remarks>
/// **A prop's materials continue the map's own table, and four of the seven lists indexed by it
/// were being filled with nulls for them.** <c>PropModels.Register</c> appended to the table entry,
/// the texture and the second texture; the detail texture, bump map, cubemap and proxies were
/// "padded" afterwards by the caller with <c>while (list.Count &lt; textures.Count) list.Add(null)</c>.
///
/// That padding was not padding. It was every model material losing four of its properties, and it
/// reads as a prop that is slightly flat — indistinguishable from art direction, which is why it
/// survived. It is also why a capture point's <c>Sine</c> proxy never ran: the material carrying it
/// is an entity model, and its proxies were discarded at exactly this point.
///
/// **The same shape had already been found and patched twice.** The comment at the append site read
/// "Three lists, not two, and for the same reason the comment above gives" — added when the second
/// texture went missing the same way and a capture point beam kept its stripes only for BLU. Each
/// fix added one more <c>Add</c> call, which works until the next list appears.
///
/// So the fix is structural rather than another <c>Add</c>: <c>MaterialTable</c> owns all seven and
/// exposes only <c>Add</c>, and the padding loops are deleted because there is nothing left to pad.
/// These tests are the regression guard on that.
/// </remarks>
public sealed class ModelMaterialParityTests
{

    [Test]
    public void ModelMaterials_AModelMaterial_CanCarryADetailTexture()
    {
        // **Measured beyond the brush count**, because the bug was specific to materials appended
        // after the map's own. A test that looked at the whole table would pass on the brushwork
        // alone and never notice that every prop had been emptied.
        (MapAssets assets, int brushes) = LoadTheMap();

        int withDetail = Enumerable.Range(brushes, assets.Details.Count - brushes)
            .Count(index => assets.Details[index] is not null);

        TestContext.Out.WriteLine(
            $"{withDetail} of {assets.Details.Count - brushes} model materials carry a detail texture");

        withDetail.ShouldBeGreaterThan(
            0, "TF2's prop materials declare $detail and it must survive registration");
    }

    [Test]
    public void ModelMaterials_AModelMaterial_CanCarryABumpMap()
    {
        (MapAssets assets, int brushes) = LoadTheMap();

        int withBump = Enumerable.Range(brushes, assets.Bumps.Count - brushes)
            .Count(index => assets.Bumps[index] is not null);

        TestContext.Out.WriteLine(
            $"{withBump} of {assets.Bumps.Count - brushes} model materials carry a bump map");

        withBump.ShouldBeGreaterThan(0, "TF2's prop materials declare $bumpmap");
    }

    [Test]
    public void ModelMaterials_EveryListIndexedByMaterial_IsTheSameLength()
    {
        // **The invariant itself, asserted rather than trusted.** These seven are indexed by one
        // number, so any disagreement in length means some material's properties belong to a
        // different material — and the failure is silent by construction.
        (MapAssets assets, _) = LoadTheMap();

        int count = assets.Materials.Count;

        assets.Textures.Count.ShouldBe(count);
        assets.BlendTextures.Count.ShouldBe(count);
        assets.Details.Count.ShouldBe(count);
        assets.Bumps.Count.ShouldBe(count);
        assets.Cubemaps.Count.ShouldBe(count);
        assets.Proxies.Count.ShouldBe(count);
    }

    [Test]
    public void ModelMaterials_TheMap_HasModelMaterialsToMeasure()
    {
        // The control. Without it the two counts above are taken over an empty range and pass
        // having compared nothing — which is exactly how the original bug stayed invisible.
        (MapAssets assets, int brushes) = LoadTheMap();

        TestContext.Out.WriteLine(
            $"{brushes} brush materials, {assets.Materials.Count - brushes} from models");

        (assets.Materials.Count - brushes).ShouldBeGreaterThan(
            0, "cp_process_final places props and their materials extend the table");
    }

    /// <summary>The map, and how many materials came from its brushwork.</summary>
    private static (MapAssets Assets, int BrushMaterials) LoadTheMap()
    {
        MapAssets assets = MapCache.Load();

        return (assets, assets.BrushMaterialCount);
    }
}
