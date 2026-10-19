using System.Collections.Generic;
using System.Linq;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>A packed model's vertices rebuilt from what the set keeps, for a decal on a skinned model (finding 80).</summary>
/// <remarks>
/// **Why it exists.** `Device3D` kept its own copy of every skinned model's packed vertices for one reader, the decal a
/// bullet leaves on a player. That was 623 MB on f12. The set already keeps each model's corners and its packing order,
/// and the engine's decal projects onto the model's own mesh (`CStudioRender::AddDecal`). Rebuilt on demand, the copy can
/// go. The rebuild must be the packing exactly, so the oracle is the list the set packed before it let it go.
/// </remarks>
public sealed class PackedVerticesTests
{
    private const string Path = "models/player/test.mdl";

    /// <summary>Four corners in two materials, interleaved, so the packing reorders them; distinct values everywhere.</summary>
    private static PropModels.ModelFrames Skinned(string path) =>
        new(
            [
                new PropVertex[]
                {
                    new(1f, 2f, 3f, 0.1f, 0.2f, MaterialIndex: 5, NormalX: 1f) { Weights = (1f, 0f, 0f), Bones = (2, 0, 0), TangentX = 0.5f },
                    new(4f, 5f, 6f, 0.3f, 0.4f, MaterialIndex: 7, NormalY: 1f) { Weights = (0.5f, 0.5f, 0f), Bones = (1, 3, 0), TangentW = -1f },
                    new(7f, 8f, 9f, 0.5f, 0.6f, MaterialIndex: 5, NormalZ: 1f) { Weights = (0.25f, 0.75f, 0f), Bones = (4, 1, 0) },
                    new(1.5f, 2.5f, 3.5f, 0.7f, 0.8f, MaterialIndex: 7) { Weights = (1f, 0f, 0f), Bones = (6, 0, 0) },
                },
            ],
            new Dictionary<int, (int Start, int Frames, float CyclesPerSecond)> { [0] = (0, 1, 0f) },
            [0],
            [true]);

    [Test]
    public void PackedVertices_AfterTheSetLetsItsCopyGo_AreTheVerticesItPacked()
    {
        EntityModelSet models = new() { Geometry = Skinned };

        models.Precache([Path]);
        List<WorldVertex> packed = [.. models.Vertices];
        models.Uploaded();
        models.ReleaseUploaded();

        models.Vertices.ShouldBeEmpty("the control: the set's own copy is gone");
        packed.Select(vertex => vertex.X).ShouldBe([1f, 7f, 4f, 1.5f], "the control: packing reordered the corners");
        models.PackedVertices(Path).ShouldBe(packed);
    }

    /// <remarks>
    /// **Rebuilt once per model struck, not once per decal** (finding 80: rebuilding per decal was about 40 MB of
    /// large-object garbage per 20 s of f12). A second decal on the same model gets the same list; a level's end lets
    /// it go with the model.
    /// </remarks>
    /// <remarks>
    /// **Reserved once for the whole call, then filled** (finding 80: the list's growth by doubling was about 1.45 GB of
    /// large-object garbage over f12's precache). Three models of four corners: grown model by model, the list ends at a
    /// capacity of 16 for 12 vertices.
    /// </remarks>
    [Test]
    public void Precache_ThreeModels_ReservesExactlyTheVerticesItPacks()
    {
        EntityModelSet models = new() { Geometry = Skinned };

        models.Precache(["models/a.mdl", "models/b.mdl", "models/c.mdl"]);

        models.Vertices.Count.ShouldBe(12, "the control: all three models packed");
        models.VertexBytes.ShouldBe(12L * System.Runtime.CompilerServices.Unsafe.SizeOf<WorldVertex>());
    }

    [Test]
    public void PackedVertices_ASecondDecalOnTheSameModel_IsTheListTheFirstGot()
    {
        EntityModelSet models = new() { Geometry = Skinned };

        models.Precache([Path]);
        IReadOnlyList<WorldVertex>? first = models.PackedVertices(Path);

        models.PackedVertices(Path).ShouldBeSameAs(first);
    }
}
