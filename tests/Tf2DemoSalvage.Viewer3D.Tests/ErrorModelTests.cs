using System;
using System.IO;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// A model that will not load is drawn as Valve's ERROR mesh rather than as nothing.
/// </summary>
/// <remarks>
/// **Nothing has to be broken to test this, which is the point.** Asking for a model that does not
/// exist is a legitimate input rather than sabotage — absence is a thing that happens to a real
/// install — so the failure path can be exercised honestly and repeatably instead of by damaging a
/// file and putting it back.
///
/// That matters more here than usual, because the behaviour under test is precisely what makes a
/// failure VISIBLE. Valve substitutes `models/error.mdl` when a prop's model is missing
/// (`game/server/props.cpp:245`), and the reason is that a chequer needs a surface to sit on while
/// a model that failed to load has none. Drawing nothing is the failure this project already has a
/// memory about: a hole reads as art direction and nobody investigates it.
/// </remarks>
public sealed class ErrorModelTests
{
    [Test]
    public void Load_AModelThatDoesNotExist_IsDrawnAsValvesErrorMesh()
    {
        string tf = "F:/SteamLibrary/steamapps/common/Team Fortress 2/tf";
        string map = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Tf2DemoSalvage", "maps", "cp_process_f12.bsp");

        if (!Directory.Exists(tf) || !File.Exists(map))
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        const string absent = "models/this_model_does_not_exist_anywhere.mdl";

        MapAssets assets = MapAssets.Load(
            File.ReadAllBytes(map),
            GameArchives.Open(tf),
            maximumTextureSize: 256,
            entityModels: [absent]);

        // **Present, not absent.** The whole change is that a name which resolves to nothing still
        // produces geometry, so the viewer draws something a person will report rather than a hole
        // nobody notices.
        assets.EntityModels.ShouldContainKey(
            absent, "a model that failed to load should still have geometry to draw");

        assets.EntityModels[absent].Geometry.Count.ShouldBeGreaterThan(0);
        assets.EntityModels[absent].Geometry[0].Count.ShouldBeGreaterThan(
            0, "the substituted model has no vertices, so nothing would be drawn after all");

        // The control, and it is what stops this passing against a substitution that quietly
        // reuses some other model: Valve's error mesh is a specific thing, and a real one loads to
        // the same geometry.
        MapAssets valves = MapAssets.Load(
            File.ReadAllBytes(map),
            GameArchives.Open(tf),
            maximumTextureSize: 256,
            entityModels: [MapAssets.ErrorModel]);

        valves.EntityModels.ShouldContainKey(
            MapAssets.ErrorModel, "Valve's error model should itself load from the game");

        assets.EntityModels[absent].Geometry[0].Count.ShouldBe(
            valves.EntityModels[MapAssets.ErrorModel].Geometry[0].Count,
            "the substitute should be Valve's error model, not some other model that happened to load");
    }
}
