using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// The models lump: the world, and the brushwork every moving entity is made of.
/// </summary>
/// <remarks>
/// A door, a lift and a payload cart are each compiled into their own model, referenced by the
/// entity that owns them as <c>*N</c>. A demo carries those names beside ordinary ones — the scene
/// layer already tracks <c>*1</c>, <c>*2</c>, <c>*5</c> — so reading this lump is what turns a
/// tracked door into a drawn one.
/// </remarks>
public sealed class BspModelsTests
{
    [Test]
    public void AStarReferenceNamesAModelIndex()
    {
        BspModels.IndexOf("*12").ShouldBe(12);
        BspModels.IndexOf("*0").ShouldBe(0);
    }

    [Test]
    public void AnOrdinaryModelPathIsNotASubmodel()
    {
        // **The control.** A rule that answered for real paths too would send every health pack
        // through the brushwork path and draw none of them.
        BspModels.IndexOf("models/items/medkit_small.mdl").ShouldBe(-1);
        BspModels.IndexOf("*").ShouldBe(-1);
        BspModels.IndexOf("*door").ShouldBe(-1);
        BspModels.IndexOf(null).ShouldBe(-1);
    }

    [Test]
    public void ARealMapDeclaresAWorldAndSubmodelsThatDoNotOverlap()
    {
        string? path = null;

        foreach (string? root in new[]
        {
            Environment.GetEnvironmentVariable("TF2_FOLDER"),
            @"F:SteamLibrarysteamappsmmonTeam Fortress 2	f",
        })
        {
            if (!string.IsNullOrWhiteSpace(root) &&
                System.IO.File.Exists(System.IO.Path.Combine(root, "maps", "cp_process_final.bsp")))
            {
                path = System.IO.Path.Combine(root, "maps", "cp_process_final.bsp");
                break;
            }
        }

        if (path is null)
        {
            Assert.Ignore("No map available; set TF2_FOLDER to run this.");
            return;
        }

        IReadOnlyList<BspModel> models = BspModels.Read(System.IO.File.ReadAllBytes(path));

        models.Count.ShouldBeGreaterThan(1, "a real map has moving brushwork");

        // **Model zero is the world and owns face zero.** Every other model is a run of faces after
        // it, and the runs are contiguous and non-overlapping — which is what lets a submodel be
        // drawn without walking the tree, as Valve's own comment says.
        models[0].FirstFace.ShouldBe(0);
        models[0].FaceCount.ShouldBeGreaterThan(0);

        for (int index = 1; index < models.Count; index++)
        {
            models[index].FaceCount.ShouldBeGreaterThan(0, $"model {index} has faces");

            models[index].FirstFace.ShouldBeGreaterThanOrEqualTo(
                models[index - 1].FirstFace + models[index - 1].FaceCount,
                $"model {index} starts after model {index - 1} ends");
        }

        int placed = 0;

        foreach (BspModel model in models)
        {
            placed += model.Origin != (0f, 0f, 0f) ? 1 : 0;
        }

        TestContext.Out.WriteLine(
            $"MODELS {placed} of {models.Count} carry a non-zero origin");

        TestContext.Out.WriteLine(
            $"MODELS {models.Count} models; world has {models[0].FaceCount} faces, " +
            $"the other {models.Count - 1} have {models.Skip(1).Sum(model => model.FaceCount)} between them");
    }
}
