using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// A brush entity is lightmapped like the wall beside it, not lit like a model.
/// </summary>
/// <remarks>
/// **B131.** A door drawn through this project's entity path took its light from the leaf's ambient
/// cube while the wall it sits in took a lightmap, so an open door was a flat panel against a shaded
/// corridor. The entry filed it as a real design choice — carry lightmap coordinates into the entity
/// vertex format, or draw brushwork with the world shader — and Valve's source turns out to answer
/// both halves, so neither was a choice.
///
/// **VRAD lights every model's faces, not just the world's.** <c>utils/vrad/vrad.cpp:703</c>,
/// <c>MakePatches</c>:
///
/// <code>
/// for (i=0 ; i&lt;nummodels ; i++)
/// {
///     mod = dmodels+i;
///     ent = EntityForModel (i);
///     VectorCopy (vec3_origin, origin);
///
///     // bmodels with origin brushes need to be offset into their
///     // in-use position
///     GetVectorForKey (ent, "origin", origin);
///
///     for (j=0 ; j&lt;mod->numfaces ; j++)
///     {
///         fn = mod->firstface + j;
///         face_entity[fn] = ent;
///         VectorCopy (origin, face_offset[fn]);
/// </code>
///
/// Two facts fall out of that loop, and the project needed both:
///
/// - **A brush entity's faces carry real baked lightmap samples**, in the same lighting lump and
///   addressed the same way as the world's. Nothing has to be invented for them.
/// - **They are lit at their IN-USE position** — where the mapper left the door, which is closed —
///   and the samples never move afterwards. So an opening door carries its closed-position lighting
///   with it. That is not an approximation to apologise for; it is what Source looks like, and it is
///   why no relighting step is needed when the entity moves.
///
/// **And the engine draws them through the world's own path when it can.**
/// <c>C_BaseEntity::DrawBrushModel</c> opens with the comment
/// <c>// Identity brushes are drawn in view->DrawWorld as an optimization</c>
/// (<c>game/client/c_baseentity.cpp:1962</c>), so a brush entity that has not moved is literally
/// drawn by the world renderer. One that has moved goes to <c>DrawBrushModelEx</c> with a transform
/// — same surfaces, same materials, same lightmaps, different matrix.
///
/// So the answer is: same lighting as the world, per-instance transform. This project already has
/// one vertex format and one shader for both, which is why the fix was carrying two floats rather
/// than building a second path.
/// </remarks>
public sealed class BrushLightingConformanceTests
{
    /// <summary>cp_process_f12, which has doors and other brush entities.</summary>
    private static string MapPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Tf2DemoSalvage", "maps", "cp_process_f12.bsp");

    [Test]
    public void BrushModels_TheGeometryThisProjectBuilds_CarriesTheAtlasCoordinatesTheWorldUses()
    {
        // **The half the two tests below cannot supply.** They read vrad and c_baseentity and
        // establish that Valve lightmaps brush entities; neither can fail for any reason concerning
        // this project, and B131 shipped a door lit by the leaf's ambient cube while both would
        // have stayed green.
        //
        // What is measurable here is the vertex data: a brush entity's corners must carry lightmap
        // coordinates that address the same atlas the world's do. If they were left at zero — which
        // is what "lit like a model" produces, since the cube branch overwrites the sample — every
        // corner would land on one texel.
        if (!System.IO.File.Exists(MapPath))
        {
            Assert.Ignore("cp_process_f12.bsp is not on this machine");
            return;
        }

        byte[] bytes = System.IO.File.ReadAllBytes(MapPath);

        IReadOnlyList<BspSurface> surfaces = [.. BspSurfaces.Read(bytes)];
        IReadOnlyList<BspModel> models = [.. BspModels.Read(bytes)];

        models.Count.ShouldBeGreaterThan(
            1, "index 0 is worldspawn; a map with no brush entities cannot test this");

        LightmapAtlas atlas = LightmapAtlas.PackAll(BspLightmaps.ReadAll(bytes));

        IReadOnlyDictionary<string, PropModels.ModelFrames> built =
            BrushModels.Build(models, surfaces, atlas);

        built.ShouldNotBeEmpty("no brush entity geometry was built at all");

        int corners = 0;
        int addressed = 0;
        HashSet<(float U, float V)> distinct = [];

        foreach (PropModels.ModelFrames frames in built.Values)
        {
            foreach (IReadOnlyList<PropVertex> frame in frames.Geometry)
            {
                foreach (PropVertex corner in frame)
                {
                    corners++;

                    // **`>= 0`, and the first draft wrote `> 0`.** Zero is a perfectly good atlas
                    // coordinate — the first face packed lands on the origin — so excluding it
                    // reported 4,590 of 6,630 corners "unaddressed" and accused correct code. The
                    // instrument was wrong, not the reader.
                    if (corner.LightU is >= 0f and <= 1f && corner.LightV is >= 0f and <= 1f)
                    {
                        addressed++;
                        distinct.Add((corner.LightU, corner.LightV));
                    }
                }
            }
        }

        corners.ShouldBeGreaterThan(0, "the brush entities produced no vertices");

        // **Every corner, not most.** vrad lights every face of every model, so a brush entity with
        // an unaddressed corner means a face this project failed to look up rather than one the
        // compiler skipped.
        addressed.ShouldBe(
            corners,
            "vrad's MakePatches loops i<nummodels, so a brush entity's faces carry baked samples "
            + "exactly as the world's do; a corner at the atlas origin is one that was never "
            + "looked up (B131)");

        TestContext.Out.WriteLine($"corners {corners}, distinct {distinct.Count}");

        // **The control, and its first version was far too weak.** It asked only that more than ten
        // distinct coordinates appear, which survives a sabotage that pins every corner of a face
        // to that face's rectangle ORIGIN — each face still contributes one distinct value, so a
        // map with hundreds of brush faces passes while every door is lit from a single texel,
        // which is exactly the flat panel B131 was filed for.
        //
        // The discriminating quantity is how distinct values scale: with the lookup working they
        // scale with CORNERS, and with it broken they scale with FACES. Measured on cp_process:
        // **2,935 distinct against 6,630 corners working, 705 broken**, so a quarter of the corner
        // count sits cleanly between the two.
        distinct.Count.ShouldBeGreaterThan(
            corners / 4,
            "the lightmap coordinate has to vary ACROSS a face, not just between faces — pinning "
            + "each face to one texel is the flat panel B131 was filed for");
    }

    [Test]
    public void MakePatches_TheLoopOverModels_CoversEveryModelAndNotJustTheWorld()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
            return;
        }

        string text = SourceSdk.Text("src/utils/vrad/vrad.cpp")
            ?? throw new InvalidOperationException("vrad.cpp is missing from the SDK");

        Match body = new Regex(
            @"void MakePatches \(void\)(?s).{0,2000}?\n\}",
            RegexOptions.Compiled,
            TimeSpan.FromSeconds(10)).Match(text);

        body.Success.ShouldBeTrue("MakePatches was not found in vrad");

        // Every model, not model zero. This is the line that says a door has a lightmap at all.
        body.Value.ShouldContain("i<nummodels");

        // And lit where it is used, which is why the samples do not follow it when it moves.
        body.Value.ShouldContain("in-use position");
        body.Value.ShouldContain("face_offset");
    }

    [Test]
    public void DrawBrushModel_AnUnmovedBrushEntity_IsDrawnByTheWorldPath()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
            return;
        }

        string text = SourceSdk.Text("src/game/client/c_baseentity.cpp")
            ?? throw new InvalidOperationException("c_baseentity.cpp is missing from the SDK");

        // The engine's own statement that brush entities and the world share a drawing path, which
        // is what makes "same shader, same lightmap, different matrix" a transcription rather than
        // an interpretation.
        text.ShouldContain("Identity brushes are drawn in view->DrawWorld as an optimization");
    }
}
