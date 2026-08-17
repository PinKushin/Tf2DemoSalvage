using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// That a real map's brush entities reach the table the renderer looks in.
/// </summary>
/// <remarks>
/// **The component tests cannot catch what this catches.** `BrushModels.Build` is tested against
/// hand-built models and surfaces, and it passes whether or not anything ever calls it — which is
/// the failure this project has shipped three times, most recently instance baselines that were
/// decoded, stored, and read by nothing outside their own unit tests.
///
/// So this asserts on the artefact rather than the component: load cp_process the way the viewer
/// loads it, and require that the doors are in `EntityModels` under the `*N` names a demo actually
/// puts in `m_nModelIndex`.
///
/// Skips when the map or the game install is absent, because a test that quietly measured nothing
/// would be worse than one that says it did not run.
/// </remarks>
public sealed class BrushEntityWiringTests
{
    private const string GamePath = "F:/SteamLibrary/steamapps/common/Team Fortress 2/tf";

    private static string MapPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Tf2DemoSalvage", "maps", "cp_process_f12.bsp");

    [Test]
    public void ARealMapsBrushEntities_ReachTheEntityModelTable()
    {
        if (!Directory.Exists(GamePath) || !File.Exists(MapPath))
        {
            Assert.Ignore("cp_process_f12.bsp or the TF2 install is not on this machine.");
            return;
        }

        byte[] bytes = File.ReadAllBytes(MapPath);

        IReadOnlyList<BspModel> models = BspModels.Read(bytes);
        IReadOnlyList<BspSurface> surfaces = BspSurfaces.Read(bytes);

        MapAssets assets = MapAssets.Load(
            bytes,
            GameArchives.Open(GamePath),
            maximumTextureSize: 64,
            brushModels: BrushModels.Build(models, surfaces));

        IReadOnlyList<string> submodels =
            [.. assets.EntityModels.Keys.Where(key => key.StartsWith('*'))];

        // cp_process has well over a hundred: the draw log named 141 in one frame. A handful would
        // mean the face ranges were being read but mostly missing their surfaces.
        submodels.Count.ShouldBeGreaterThan(100);

        // Named individually because these are the ones the demo's own entities reference, taken
        // from the skip log that opened this investigation: "1x*57#Brush, 1x*61#Brush ...".
        submodels.ShouldContain("*57");
        submodels.ShouldContain("*61");

        // And they carry geometry, which is the half a key alone does not prove. An entry with no
        // triangles is exactly what a wired-up-but-empty build would produce.
        assets.EntityModels["*57"].Geometry[0].Count.ShouldBeGreaterThan(0);
    }

    [Test]
    public void TheWorldIsNotAmongThem()
    {
        if (!Directory.Exists(GamePath) || !File.Exists(MapPath))
        {
            Assert.Ignore("cp_process_f12.bsp or the TF2 install is not on this machine.");
            return;
        }

        byte[] bytes = File.ReadAllBytes(MapPath);

        MapAssets assets = MapAssets.Load(
            bytes,
            GameArchives.Open(GamePath),
            maximumTextureSize: 64,
            brushModels: BrushModels.Build(BspModels.Read(bytes), BspSurfaces.Read(bytes)));

        // *0 is worldspawn. Building it would draw the entire map a second time, as an entity,
        // on top of itself - which reads as z-fighting rather than as a duplicated map.
        assets.EntityModels.Keys.ShouldNotContain("*0");
    }
}
