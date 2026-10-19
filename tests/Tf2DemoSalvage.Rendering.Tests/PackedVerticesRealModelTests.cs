using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>A real skinned model's packed vertices, rebuilt after the set let its copy go (finding 80).</summary>
/// <remarks>
/// The decal on a player now projects onto <see cref="EntityModelSet.PackedVertices"/> instead of a copy the renderer kept
/// (623 MB on f12). On the shipped soldier, the rebuild must be exactly the slice the renderer was handed: the model's own
/// range of the packed list, rebased to zero, as `Device3D` slices it.
///
/// **What this cannot see:** the soldier's corners are already grouped by material, so its packing order is the identity
/// and ignoring the order passes here (tried). `PackedVerticesTests` in Scene.Tests holds the reordering on a model built
/// to interleave materials. This one holds the real model's size, skinning and equality end to end.
/// </remarks>
public sealed class PackedVerticesRealModelTests
{
    private const string SoldierModel = "models/player/soldier.mdl";

    [Test]
    [Category(TestCategories.MutationHeavy)]
    public void PackedVertices_TheShippedSoldier_AreTheSliceTheRendererWasHanded()
    {
        if (GameInstall.Root is null)
        {
            Assert.Ignore("Team Fortress 2 is not installed.");
            return;
        }

        MapAssets assets = MapCache.Load(entityModels: [SoldierModel]);
        EntityModelSet models = new() { Geometry = assets.Geometry };

        models.Precache([SoldierModel]);

        IReadOnlyList<WorldBatch> batches = models.AllFrames(SoldierModel)[0];
        int lowest = batches.Min(batch => batch.FirstVertex);
        int highest = batches.Max(batch => batch.FirstVertex + batch.VertexCount);
        List<WorldVertex> handed = [.. Enumerable.Range(lowest - models.VertexBase, highest - lowest).Select(at => models.Vertices[at])];

        models.Uploaded();
        models.ReleaseUploaded();

        handed.Count.ShouldBeGreaterThan(10_000, "the control: a whole player");
        handed[0].WeightA.ShouldBeGreaterThan(0f, "the control: a skinned model, as the decal reader requires");
        models.Vertices.ShouldBeEmpty("the set's own copy is gone");
        models.PackedVertices(SoldierModel).ShouldBe(handed);
    }
}
