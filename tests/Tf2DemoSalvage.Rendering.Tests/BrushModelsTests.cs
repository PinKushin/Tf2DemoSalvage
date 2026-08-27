using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// Building a brush entity's geometry from the map's models lump.
/// </summary>
/// <remarks>
/// A door is <c>*12</c> — a run of faces in the map — where a health pack is a <c>.mdl</c>. Valve's
/// own note on the lump is the whole specification: "submodels just draw faces without walking the
/// bsp tree", so a submodel is <c>firstface</c> and <c>numfaces</c> and nothing else.
///
/// **Vertices stay in world space, and that is checked here rather than assumed.** vbsp shifts an
/// entity's brushes to be relative to its origin brush and writes that point as the entity's
/// `origin` keyvalue (`utils/vbsp/map.cpp`); an entity without one keeps world coordinates and an
/// origin of zero. Either way the entity path's existing transform is correct, so this must not
/// re-centre anything — a builder that helpfully subtracted the model's bounds would put every
/// closed door at the map's centre.
/// </remarks>
public sealed class BrushModelsTests
{
    [Test]
    public void ASubmodel_TakesOnlyItsOwnFaces()
    {
        // Face 0 is the world's, faces 1 and 2 belong to model 1. A single-face submodel could not
        // tell "took its faces" apart from "took one face and stopped".
        IReadOnlyList<BspModel> models =
        [
            Model(firstFace: 0, faceCount: 1),
            Model(firstFace: 1, faceCount: 2),
        ];

        IReadOnlyDictionary<string, PropModels.ModelFrames> built = Build(
            models,
            [Face(0, material: 1), Face(1, material: 2), Face(2, material: 3)]);

        built.Keys.ShouldBe(["*1"]);

        IReadOnlyList<PropVertex> corners = built["*1"].Geometry[0];

        // Two triangles, one per face, three corners each.
        corners.Count.ShouldBe(6);

        // Its own faces' materials, and not the world's. Asserting the set rather than the count
        // is what makes this fail if the run started one face early.
        corners.Select(corner => corner.MaterialIndex).Distinct().Order().ShouldBe([2, 3]);
    }

    [Test]
    public void TheWorldModel_IsNotABrushEntity()
    {
        // Index 0 is worldspawn. Nothing references *0, and building it would duplicate the whole
        // map as an entity - the exact double-draw this work exists to remove.
        IReadOnlyDictionary<string, PropModels.ModelFrames> built = Build(
            [Model(firstFace: 0, faceCount: 1)],
            [Face(0, material: 1)]);

        built.ShouldBeEmpty();
    }

    [Test]
    public void ASubmodelOfToolBrushes_BuildsNothingRatherThanAnEmptyModel()
    {
        // A trigger volume is a brush entity whose faces the map never gives us. Recording it with
        // no geometry would make the renderer report a model that loaded and drew zero triangles,
        // which is indistinguishable in the log from a model that failed to load.
        IReadOnlyDictionary<string, PropModels.ModelFrames> built = Build(
            [Model(firstFace: 0, faceCount: 1), Model(firstFace: 1, faceCount: 1)],
            [Face(0, material: 1)]);

        built.ShouldBeEmpty();
    }

    [Test]
    public void Vertices_KeepTheirWorldCoordinates()
    {
        // The claim vbsp's origin-brush handling rests on. If this ever re-centres, every closed
        // door moves to wherever the re-centring put it.
        IReadOnlyDictionary<string, PropModels.ModelFrames> built = Build(
            [Model(firstFace: 0, faceCount: 1), Model(firstFace: 1, faceCount: 1)],
            [Face(0, material: 1), Face(1, material: 2)]);

        IReadOnlyList<PropVertex> corners = built["*1"].Geometry[0];

        corners[0].X.ShouldBe(1000f);
        corners[0].Y.ShouldBe(2000f);
        corners[0].Z.ShouldBe(3000f);
    }

    [Test]
    public void Build_ASubmodelFace_TakesItsLightmapCoordinatesFromTheAtlas()
    {
        // **B131, at the level where the coordinates are produced.** A door's faces are lit by vrad
        // exactly as the world's are — `MakePatches` loops `i<nummodels`, not model zero alone
        // (vrad.cpp:703) — and their samples land in the same atlas. Dropping them here is what made
        // an open door a flat panel against a shaded corridor.
        //
        // Two faces with lightmaps, so face 1's rectangle is NOT at the atlas origin. With one face
        // it would pack at the reserved corner and every coordinate would read plausibly as zero,
        // which is exactly the value a builder that ignored the atlas produces.
        LightmapAtlas atlas = LightmapAtlas.Pack(
        [
            new BspLightmap(16, 16, new byte[16 * 16 * 4]),
            new BspLightmap(8, 8, new byte[8 * 8 * 4]),
        ]);

        AtlasRect rectangle = atlas.Rectangles[1];

        rectangle.Width.ShouldBeGreaterThan(0f, "face 1 must be packed for this to measure anything");

        IReadOnlyDictionary<string, PropModels.ModelFrames> built = BrushModels.Build(
            [Model(firstFace: 0, faceCount: 1), Model(firstFace: 1, faceCount: 1)],
            [Face(0, material: 1), Face(1, material: 2)],
            atlas);

        IReadOnlyList<PropVertex> corners = built["*1"].Geometry[0];

        // The fixture's three corners carry face-local LightU of 0, 0.33 and 0.66 at LightV 0.5.
        // Remapped, each must land at its own fraction across face 1's rectangle — the same
        // arithmetic MapWorld.Append does, checked against the same numbers.
        corners[0].LightU.ShouldBe(rectangle.U, 1e-6f);
        corners[1].LightU.ShouldBe(rectangle.U + (0.33f * rectangle.Width), 1e-6f);
        corners[2].LightU.ShouldBe(rectangle.U + (0.66f * rectangle.Width), 1e-6f);

        corners[0].LightV.ShouldBe(rectangle.V + (0.5f * rectangle.Height), 1e-6f);

        // The control: face 1 is not at the origin, so these are not the white texel a studio model
        // gets. Without it every assertion above passes against a builder that wrote zeroes.
        rectangle.U.ShouldBeGreaterThan(0f);
    }

    /// <summary>Builds with no baked lighting, for the tests that are about geometry.</summary>
    /// <remarks>
    /// An empty atlas gives every face a zero-width rectangle, which is the reserved white texel —
    /// the same answer a face with <c>lightofs</c> of -1 legitimately gets. Stated once here rather
    /// than at four call sites, and deliberately NOT the default on
    /// <see cref="BrushModels.Build"/>: a caller that forgets the atlas in production gets a door
    /// lit like a model, which is a plausible picture and was the whole of B131.
    /// </remarks>
    private static IReadOnlyDictionary<string, PropModels.ModelFrames> Build(
        IReadOnlyList<BspModel> models, IReadOnlyList<BspSurface> surfaces) =>
        BrushModels.Build(models, surfaces, LightmapAtlas.Pack([]));

    private static BspModel Model(int firstFace, int faceCount) =>
        new((0f, 0f, 0f), (0f, 0f, 0f), (0f, 0f, 0f), 0, firstFace, faceCount);

    private static BspSurface Face(int faceIndex, int material)
    {
        // Far from the origin on every axis, so a re-centring bug cannot hide behind a zero.
        List<SurfaceVertex> vertices =
        [
            new(1000f, 2000f, 3000f, 0.25f, 0.5f, 0f, 0.5f),
            new(1100f, 2000f, 3000f, 0.25f, 0.5f, 0.33f, 0.5f),
            new(1100f, 2050f, 3000f, 0.25f, 0.5f, 0.66f, 0.5f),
        ];

        return new BspSurface(
            faceIndex, vertices, material, default, (0f, 0f, 1f), SurfaceProperties.None, -1);
    }
}
