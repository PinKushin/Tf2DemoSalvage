using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// That the static world is built from the world model's faces and nobody else's.
/// </summary>
/// <remarks>
/// **B71, and the amendment is the whole point.** The doors were not missing from the wire and
/// were not skipped by the renderer for want of geometry — they were baked into the STATIC vertex
/// buffer at the position they were compiled in, and so could never move. A door compiled retracted
/// sits inside the ceiling, which from a screenshot is indistinguishable from a door that was never
/// drawn. Measured on cp_process_f12: 1,030 of the surfaces read belong to entity models rather
/// than the world, and 141 brush entities are submitted per frame.
///
/// A BSP's faces lump holds the world model's faces first and every brush entity's after, so a
/// reader that walks the whole lump draws them all. `models[0].FaceCount` is the boundary and has
/// been read all along; only nothing acted on it.
///
/// **Two surfaces is the smallest input that can distinguish the two outcomes**, and the world face
/// is a control rather than decoration: an implementation that dropped everything would satisfy
/// "the submodel face is absent" perfectly. Both halves have to be asserted or the test is passed
/// by a build that produces nothing at all — which is exactly how an earlier height-cut test in
/// this assembly passed for the wrong reason.
/// </remarks>
public sealed class BrushEntityFaceTests
{
    private static readonly BspMaterial[] Materials =
        [.. Enumerable.Range(0, 4).Select(index =>
            new BspMaterial($"concrete/wall{index}", (0.5f, 0.5f, 0.5f), 512, 512))];

    private static readonly TopDownCamera Camera =
        TopDownCamera.Fit([(0f, 0f), (1000f, 1000f)], 800, 600);

    /// <summary>
    /// One world model owning face 0 alone, so face 1 belongs to a brush entity.
    /// </summary>
    /// <remarks>
    /// Face 1 is what a `func_door` looks like in the lump: its own model, its faces following the
    /// world's. The second model is the door itself, and its presence is what makes face 1
    /// attributable rather than merely out of range.
    /// </remarks>
    private static IReadOnlyList<BspModel> TwoModels =>
    [
        Model(firstFace: 0, faceCount: 1),
        Model(firstFace: 1, faceCount: 1),
    ];

    [Test]
    public void TheStaticWorld_ExcludesABrushEntitysFaces()
    {
        // A triangle each, so a surface that is built contributes three vertices and one that is
        // not contributes zero. Distinct materials so the batches can be told apart.
        BspSurface worldFace = Surface(faceIndex: 0, material: 1);
        BspSurface doorFace = Surface(faceIndex: 1, material: 2);

        MapWorld world = MapWorldBuilder.Build(
            null,
            [worldFace, doorFace],
            Materials,
            LightmapAtlas.Pack([]),
            [],
            Camera,
            null,
            models: TwoModels);

        IReadOnlyList<int> built = [.. world.Batches.Select(batch => batch.MaterialIndex)];

        // The control: the world's own face still builds. Without this the assertion below is
        // satisfied by a builder that dropped everything.
        built.ShouldContain(1);

        // The claim: the door's face does not, because it has to be free to move.
        built.ShouldNotContain(2);
    }

    [Test]
    public void WithNoModels_EveryFaceIsStillBuilt()
    {
        // The boundary is unknown when the models lump was not read, and an unknown boundary must
        // not silently discard geometry. This pins the fallback: absent means build everything,
        // which is what the viewer did before the models lump existed.
        MapWorld world = MapWorldBuilder.Build(
            null,
            [Surface(faceIndex: 0, material: 1), Surface(faceIndex: 1, material: 2)],
            Materials,
            LightmapAtlas.Pack([]),
            [],
            Camera,
            null);

        IReadOnlyList<int> built = [.. world.Batches.Select(batch => batch.MaterialIndex)];

        built.ShouldContain(1);
        built.ShouldContain(2);
    }

    private static BspModel Model(int firstFace, int faceCount) =>
        new((0f, 0f, 0f), (0f, 0f, 0f), (0f, 0f, 0f), 0, firstFace, faceCount);

    private static BspSurface Surface(int faceIndex, int material)
    {
        List<SurfaceVertex> vertices =
        [
            new(0f, 0f, 0f, 0.25f, 0.5f, 0f, 0.5f),
            new(100f, 0f, 0f, 0.25f, 0.5f, 0.33f, 0.5f),
            new(100f, 50f, 0f, 0.25f, 0.5f, 0.66f, 0.5f),
        ];

        return new BspSurface(
            faceIndex, vertices, material, default, (0f, 0f, 1f), SurfaceProperties.None, -1);
    }
}
