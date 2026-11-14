using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// Which loads keep their model and which are substituted by <c>models/error.mdl</c> (B158).
/// </summary>
/// <remarks>
/// **Valve's substitution is for a model that is missing or could not be read** — `props.cpp:243-246` for a
/// prop with no model name, `detailobjectsystem.cpp:1599-1604` for a vertex-lit detail model. A rig that loads
/// and declares no meshes, such as `comp_stage_cam.mdl`, is neither: it has bones and sequences and draws
/// nothing, as the engine's mesh loop does with `nummeshes == 0`.
/// </remarks>
public sealed class ModelFramesLoadedTests
{
    private static readonly PropVertex[] Triangle =
    [
        new(1f, 0f, 0f, 0f, 0f, MaterialIndex: 0),
        new(0f, 1f, 0f, 1f, 0f, MaterialIndex: 0),
        new(0f, 0f, 1f, 0f, 1f, MaterialIndex: 0),
    ];

    [Test]
    public void Loaded_AModelThatFailedToLoad_IsSubstituted()
    {
        PropModels.ModelFrames.Loaded(null).ShouldBeFalse();
    }

    [Test]
    public void Loaded_AModelWithTriangles_IsKept()
    {
        PropModels.ModelFrames.Loaded(Frames(Triangle, declaresNoMeshes: false)).ShouldBeTrue();
    }

    [Test]
    public void Loaded_AModelThatDeclaresNoMeshes_IsKept()
    {
        PropModels.ModelFrames.Loaded(Frames([], declaresNoMeshes: true)).ShouldBeTrue();
    }

    [Test]
    public void Loaded_AModelWithMeshesButNoCorners_IsSubstituted()
    {
        // The control for the case above: empty geometry alone is not the fact, the declaration is.
        PropModels.ModelFrames.Loaded(Frames([], declaresNoMeshes: false)).ShouldBeFalse();
    }

    [Test]
    public void Loaded_AModelWithNoFramesAtAll_IsSubstituted()
    {
        PropModels.ModelFrames frames =
            new([], new Dictionary<int, (int, int, float)>(), [], []) { DeclaresNoMeshes = true };

        PropModels.ModelFrames.Loaded(frames).ShouldBeFalse();
    }

    private static PropModels.ModelFrames Frames(PropVertex[] corners, bool declaresNoMeshes) =>
        new([corners], new Dictionary<int, (int, int, float)>(), [0], [false]) { DeclaresNoMeshes = declaresNoMeshes };
}
