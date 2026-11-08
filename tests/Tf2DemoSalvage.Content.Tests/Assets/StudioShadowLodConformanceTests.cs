using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// Which level of detail a render-to-texture shadow's caster is drawn at — `engine.dll` `FUN_1800eba80`
/// (`DrawModelShadowSetup`, disassembled; finding 107).
/// </summary>
/// <remarks>
/// **The engine's rule under the default `r_shadowlod −1`**: the last LOD for a model whose header carries
/// <c>STUDIOHDR_FLAGS_HASSHADOWLOD</c> (`0x40`, `studio.h:2047`; `0x1800ebbb6`), else the second-to-last, floored at 0
/// (`0x1800ebc75`, overwriting the distance LOD computed just before it).
/// </remarks>
public sealed class StudioShadowLodConformanceTests
{
    [TestCase(StudioModelFlags.HasShadowLod, 7, 6)]
    [TestCase(StudioModelFlags.HasShadowLod, 1, 0)]
    [TestCase(0, 7, 5)]
    [TestCase(0, 4, 2)]
    [TestCase(0, 2, 0)]
    [TestCase(0, 1, 0)]
    [TestCase(0, 0, 0)]
    public void ShadowLod_TheEnginesRule_IsTheLastWithAShadowLodElseTheSecondToLast(int flags, int lods, int expected) =>
        StudioTriangles.ShadowLod(flags, lods).ShouldBe(expected);

    /// <remarks>
    /// **A differential against a second reader**: the research agent's own parser of the shipped scout counted 7814
    /// triangles at LOD 0 and 1421 at LOD 6, summed over every bodygroup, from seven LODs whose last is the shadow's.
    /// This reader, asked for those LODs, must count the same.
    /// </remarks>
    [Test]
    public void Read_TheScoutAtLodZeroAndItsShadowLod_CountsTheShippedTriangles()
    {
        if (GameInstall.Root is not { } folder)
        {
            Assert.Ignore("Team Fortress 2 is not installed; set TF2_FOLDER to run this.");
            return;
        }

        VpkArchive models = VpkArchive.Open(Path.Combine(folder, "tf2_misc_dir.vpk"));
        byte[] modelFile = models.ReadFile("models/player/scout.mdl").ShouldNotBeNull();
        byte[] indexFile = models.ReadFile("models/player/scout.dx90.vtx").ShouldNotBeNull();
        StudioModelInfo model = StudioModel.Read(modelFile);

        StudioTriangles.LodCount(indexFile).ShouldBe(7);
        (model.Flags & StudioModelFlags.HasShadowLod).ShouldBe(StudioModelFlags.HasShadowLod);
        StudioTriangles.ShadowLod(model.Flags, StudioTriangles.LodCount(indexFile)).ShouldBe(6);

        Triangles(StudioTriangles.Read(indexFile, model)).ShouldBe(7814);
        Triangles(StudioTriangles.Read(indexFile, model, lod: 6)).ShouldBe(1421);
    }

    private static int Triangles(IReadOnlyList<IReadOnlyList<StudioCorner>> meshes) => meshes.Sum(mesh => mesh.Count) / 3;
}
