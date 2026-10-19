using System.Collections.Generic;

using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>A device that holds the models a test gives it and keeps every decal mesh it is handed.</summary>
internal sealed class FakeDecalUpload : IDecalUpload
{
    /// <summary>Models' batches by path.</summary>
    public Dictionary<string, IReadOnlyList<WorldBatch>> Batches { get; } = [];

    /// <summary>Every world decal mesh uploaded, in order, copied as it stood.</summary>
    public List<(WorldVertex[] Vertices, WorldBatch[] Batches, WorldBatch[] EntityBatches)> Uploads { get; } = [];

    public ModelDecals? ModelDecals { get; set; }

    public IReadOnlyList<WorldBatch> ModelBatches(string modelPath) => Batches.GetValueOrDefault(modelPath) ?? [];

    public void UploadShotDecals(
        IReadOnlyList<WorldVertex> vertices, IReadOnlyList<WorldBatch> batches, IReadOnlyList<WorldBatch> entityBatches) =>
        Uploads.Add(([.. vertices], [.. batches], [.. entityBatches]));
}
