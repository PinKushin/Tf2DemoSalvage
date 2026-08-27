using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Rendering.Tests;

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
    public void AMovingGatesGeometry_IsCompiledWhereTheDemoSaysItRests()
    {
        // **B94's second question, once the demo excluded the mapper.** Three gates move on
        // cp_process -- submodels 80, 81 and 186 -- and the demo's own m_vecOrigin says all three
        // rest at Z 640 and rise 145 units. So the motion is upward and the fault is this side.
        //
        // That leaves where the geometry sits. vbsp shifts an entity's brushes to be relative to
        // its origin brush and writes that point as the entity's origin keyvalue, so a gate WITH an
        // origin brush is compiled near zero and placed by the networked origin. One without keeps
        // world coordinates and carries an origin of zero. Both are correct under
        // `world = origin + vertex`; what breaks is a gate compiled in world space whose entity
        // still reports a non-zero origin, because then the two are added twice.
        //
        // The models lump answers it directly, and the answer decides whether BrushModels is right
        // to keep vertices as stored.
        if (!File.Exists(MapPath))
        {
            Assert.Ignore("cp_process_f12.bsp is not on this machine.");
            return;
        }

        IReadOnlyList<BspModel> models = BspModels.Read(File.ReadAllBytes(MapPath));

        List<string> bounds = [];

        // The submodels the demo shows moving, across all the rest heights it reports.
        foreach (int index in (int[])[78, 80, 81, 132, 135, 137, 139, 141, 143, 144, 146, 185, 186])
        {
            BspModel gate = models[index];

            // **Compiled about its own origin, which is what "relative" means here.** Submodel 80
            // measures -64 to 80: 144 units tall, straddling zero, matching the 145 units the demo
            // says it travels. That is an origin brush placed at the shutter's centre, and with a
            // resting origin of 640 it puts the shutter at 576..720 and lifts it to 721..865.
            //
            // A previous version of this assertion demanded the minimum be non-negative, on a guess
            // that a negative one would hang the shutter below its frame. Straddling zero is
            // ordinary for a centred origin brush, so that was a hypothesis written as a test —
            // and it failed against correct data. What actually distinguishes relative from absolute
            // is magnitude, not sign: world-space vertices here would read near 640.
            Math.Abs(gate.Minimum.Z).ShouldBeLessThan(
                320f,
                $"submodel {index} spans {gate.Minimum.Z:0} to {gate.Maximum.Z:0}");

            Math.Abs(gate.Maximum.Z).ShouldBeLessThan(320f);

            // **Where the shutter sits relative to its own origin, which decides whether "closed"
            // fills the frame.** The demo's resting origin IS the closed position, so a shutter
            // whose geometry straddles zero hangs half below that point: submodel 80 spans -64 to
            // 80, which at a rest of 640 occupies 576..720 and puts its lower edge 64 units under
            // the sill. That is the reported symptom, and it is the difference between an origin
            // brush at the shutter's centre and one at its base.
            //
            // Reported rather than asserted, because which of those a mapper used is not something
            // this project gets to require -- what matters is that the renderer agrees with the
            // engine, and the engine applies origin + vertex either way.
            bounds.Add($"{index}: {gate.Minimum.Z:0}..{gate.Maximum.Z:0}");
        }

        // Every gate compiles about its own origin, which is the claim BrushModels rests on.
        bounds.Count.ShouldBe(13, string.Join(", ", bounds));
    }

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
            brushModels: atlas => BrushModels.Build(models, surfaces, atlas));

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
    public void BrushEntities_TheWorldModel_IsExcluded()
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
            brushModels: atlas =>
                BrushModels.Build(BspModels.Read(bytes), BspSurfaces.Read(bytes), atlas));

        // *0 is worldspawn. Building it would draw the entire map a second time, as an entity,
        // on top of itself - which reads as z-fighting rather than as a duplicated map.
        assets.EntityModels.Keys.ShouldNotContain("*0");
    }
}
