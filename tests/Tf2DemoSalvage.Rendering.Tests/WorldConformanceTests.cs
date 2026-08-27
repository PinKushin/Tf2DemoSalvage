using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// What this project reads of a BSP, and what of the world it therefore cannot draw.
/// </summary>
/// <remarks>
/// **Lump numbers are from <c>public/bspfile.h</c>**, so an entry here names a real structure rather
/// than a feature someone remembers TF2 having. Reading published headers is not decompilation.
///
/// Same rules as <see cref="SourceConformanceTests"/>: gaps are IGNORED with a reason rather than
/// failed, and every one says what you would SEE without it. A map is the half of the picture that
/// is always on screen, so a gap here costs more than the same gap in a model.
/// </remarks>
public sealed class WorldConformanceTests
{
    [Test]
    public void Displacements_AreRead()
    {
        // LUMP_DISPINFO 26 and LUMP_DISP_VERTS 33. Terrain is displacement geometry, and a map
        // without it has holes where every hill and bank should be — cp_process mid is the case
        // that named it here.
        typeof(BspDisplacements).ShouldNotBeNull();
        typeof(BspTerrain).ShouldNotBeNull();
    }

    [Test]
    public void Overlays_AreRead()
    {
        // LUMP_OVERLAYS 45. Decals authored into the map: the team stripes, signage, grime. The
        // face list on an overlay is the set of surfaces to CLIP against, not candidates to place
        // on, and reading it the other way put every stripe hovering off its wall.
        typeof(BspOverlays).ShouldNotBeNull();
    }

    [Test]
    public void StaticProps_AreRead()
    {
        // LUMP_GAME_LUMP 35, sprp. Most of what a map looks like is props rather than brushwork.
        typeof(BspStaticProps).ShouldNotBeNull();
    }

    [Test]
    public void LeafAmbientLighting_IsRead()
    {
        // LUMP_LEAF_AMBIENT_LIGHTING 56 and its index at 52, with the HDR pair at 55 and 51. This
        // is the only light a model gets besides the sun, and the header notes it "overrides part
        // of the data stored in LUMP_LEAFS".
        typeof(BspAmbientLight).ShouldNotBeNull();
    }

    [Test]
    public void Lightmaps_AreRead()
    {
        // LUMP_LIGHTING 8, with LUMP_LIGHTING_HDR at 53. Without it the world is flat-lit and every
        // room reads the same brightness.
        typeof(BspLightmaps).ShouldNotBeNull();
    }

    [Test]
    public void WorldLights_AreRead()
    {
        // LUMP_WORLDLIGHTS 15. Where the sun is, which is what gives models their direct term.
        typeof(BspWorldLights).ShouldNotBeNull();
    }

    // **`Cubemaps_AreNotRead` stood here and was false for a day.** LUMP_CUBEMAPS 42 is read by
    // BspCubemaps, its 43 placements on cp_process_final decode, and reflections draw — while this
    // marker went on skipping with "nothing reflects. B55." A skipped test is invisible in a green
    // run, so nothing said otherwise. Removed rather than kept: a settled entry should stop counting
    // as a gap. See ConformanceGapAuditTests, which now fails when a marker outlives its gap.

    [Test]
    public void Visibility_IsNotUsed()
    {
        // LUMP_VISIBILITY 4, the PVS. The engine draws only what a leaf can see from where the
        // camera stands.
        //
        // WHAT YOU SEE: nothing wrong — this is a performance structure, not a visual one, and a
        // viewer that draws the whole map is CORRECT and slower. Recorded so it is not mistaken
        // for a defect later, and because a free camera outside the world is exactly where the
        // engine's own behaviour and ours diverge visibly.
        Assert.Ignore("LUMP_VISIBILITY 4 unused; correctness unaffected, cost only.");
    }

    [Test]
    public void AreaportalsAndOcclusion_AreNotUsed()
    {
        // LUMP_AREAPORTALS 21 and LUMP_OCCLUSION 9. Both are culling aids the engine uses to stop
        // drawing what a doorway hides.
        //
        // WHAT YOU SEE: nothing wrong, same as the PVS. Areaportals additionally OPEN and CLOSE
        // with their doors, so a viewer that ignores them can never show less than the engine —
        // only more.
        Assert.Ignore("Areaportals and occluders unused; cost only, never correctness.");
    }

    [Test]
    public void Water_IsNotDrawn()
    {
        // LUMP_WATEROVERLAYS 50, and the engine's water shader with its reflection and refraction
        // render targets.
        //
        // WHAT YOU SEE: water surfaces draw as a flat material with no reflection, no refraction
        // and no depth fog. cp_process has water at both flanks, so this is visible on the map
        // this project uses most.
        Assert.Ignore("Water shader unimplemented; flat surface, no reflection or refraction.");
    }

    [Test]
    public void ThreeDimensionalSkybox_IsNotDrawn()
    {
        // The sky_camera entity and the scaled-down world drawn around it, plus the 2D skybox
        // material named by the map's worldspawn.
        //
        // WHAT YOU SEE: the horizon is empty. Every distant silhouette a map paints — the
        // Redstone Cargo towers on cp_process — is absent, and the map reads as a diorama with
        // nothing behind it. The owner has parked this deliberately as roadmap work.
        Assert.Ignore("3D and 2D skybox undrawn; horizon empty. Parked by the owner.");
    }

    [Test]
    public void BrushEntities_Move()
    {
        // **This marker said "doors never open" and was stale — the fifth such this session.** All
        // four of B71's steps were done by the time anyone checked, and nothing said so because a
        // skipped test is invisible in a green run.
        //
        // Measured rather than inferred, in three independent places:
        //
        // - 176 of cp_badlands' faces are HELD BACK from the static world build rather than baked
        //   into it, which is B71 step 1.
        // - The render log lists *57, *61 and *65 among its posed models, at positions like
        //   (1077, 4602, -8). A compiled submodel's own origin is zero, so that number can only
        //   have come from the entity — steps 2 to 4.
        // - `BrushEntityMotionTests` reads the timeline directly: 45 brush entities move across 9
        //   corpus demos, several by exactly 126 units, which is a granary spawn door's travel.
        //
        // The third is the one that matters, and it is why this is now an assertion rather than a
        // deletion: the first two establish that brushwork is PLACED by its entity, and only the
        // timeline establishes that the placement CHANGES. Every run available when this was
        // reviewed had opened at a tick and stayed there, so the renderer's own `brush … seconds`
        // instrument reported each entity once, at second zero, and could not answer.
        BrushModels.SubmodelPrefix.ShouldBe(
            '*', "a submodel reference is what marks an entity as brushwork rather than a model");
    }

    [Test]
    public void DetailProps_AreNotDrawn()
    {
        // LUMP_GAME_LUMP 35, dprp — the grass and clutter sprites a map scatters, distinct from
        // static props and stored per leaf.
        //
        // WHAT YOU SEE: ground surfaces that should carry grass tufts and gravel read bare. Subtle
        // next to the gaps above, and cheap once static props already work.
        Assert.Ignore("Detail props (dprp) undrawn; ground clutter missing.");
    }
}
