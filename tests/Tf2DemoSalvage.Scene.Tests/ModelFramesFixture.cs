using System.Collections.Generic;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The smallest model that draws, for tests about everything except geometry.
/// </summary>
/// <remarks>
/// **Three files had a byte-identical copy of this** — `EntityModelsTests`, `MomentSceneTests` and
/// the fade wiring — which is the second occurrence the DRY rule names. It is one triangle because
/// the cases that use it are about placement, alpha and upload counts: with no geometry at all the
/// set never grows, so "did not upload" and "had nothing to upload" become the same observation.
/// </remarks>
public static class ModelFramesFixture
{
    /// <summary>One triangle, one frame, one material.</summary>
    /// <param name="path">Ignored; the parameter exists so this matches the loader delegate.</param>
    /// <returns>Frames a model set will accept and draw.</returns>
    public static PropModels.ModelFrames OneTriangle(string path) =>
        new(
            [
                new PropVertex[]
                {
                    new(1f, 0f, 0f, 0f, 0f, MaterialIndex: 3),
                    new(0f, 1f, 0f, 1f, 0f, MaterialIndex: 3),
                    new(0f, 0f, 1f, 0f, 1f, MaterialIndex: 3),
                },
            ],
            new Dictionary<int, (int Start, int Frames, float CyclesPerSecond)> { [0] = (0, 1, 0f) },
            [0],
            [true]);
}
