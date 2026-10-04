using System.Collections.Generic;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>Draws effect-entity batches alone on black, as the viewer's particle pass draws them.</summary>
internal static class StripPicture
{
    /// <summary>Clears to black, binds the target and draws every batch.</summary>
    /// <param name="target">The offscreen target.</param>
    /// <param name="matrix">The camera's view-projection.</param>
    /// <param name="assets">The map load the batches' materials came from.</param>
    /// <param name="batches">The batches.</param>
    /// <remarks>
    /// <see cref="OffscreenTarget.DrawSprites"/> binds no target of its own; <see cref="OffscreenTarget.DrawWorld"/> binds
    /// one and clears its depth, so it is asked for one degenerate triangle with the world switched off.
    /// </remarks>
    public static void Draw(
        OffscreenTarget target, float[] matrix, MapAssets assets, IReadOnlyList<ParticleBatch> batches)
    {
        List<WorldVertex> nothing =
        [
            new(0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f),
            new(0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f),
            new(0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f),
        ];

        target.Clear(0f, 0f, 0f);
        target.DrawWorld(
            nothing, [new WorldBatch(0, 0, nothing.Count)], matrix, assets, surfaceColours: true, drawWorld: false,
            translucent: false);

        foreach (ParticleBatch batch in batches)
        {
            target.DrawSprites(batch, matrix);
        }
    }
}
