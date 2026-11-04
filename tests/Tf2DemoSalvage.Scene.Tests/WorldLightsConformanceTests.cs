using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`r_worldlights`, default "4": how many world lights take a slot, every reader (B676).</summary>
/// <remarks>
/// *Read in decompile*, `engine.dll` build 11191711. `FUN_180010150` registers it. `FUN_1801b79e0`, `FUN_1801b6d40` and `FUN_1801b7e90` each
/// read `min( r_worldlights, MaxNumLights )` and append while a slot is under it, then evict or fold (`FUN_1801b76f0`). `FUN_1801bcf80` and
/// `FUN_1801bcbb0` flush the cache and relight every handle on a change, and write a value over the hardware's back. `FUN_1800f4310` cuts the drawn
/// count to it and folds a light that left past it.
/// </remarks>
public sealed class WorldLightsConformanceTests
{
    /// <summary>Two lamps at a known falloff from the point: 16 and 4 at 4 and 2 of green at the +X face.</summary>
    private static readonly BspWorldLight Stronger = new((10f, 0f, 0f), (800f, 800f, 800f), default, WorldLightKind.Point, QuadraticAttenuation: 1f, Radius: 1000f, LumpIndex: 0);

    private static readonly BspWorldLight Weaker = new((10f, 0f, 0f), (400f, 400f, 400f), default, WorldLightKind.Point, QuadraticAttenuation: 1f, Radius: 1000f, LumpIndex: 1);

    [Test]
    public void Parse_NothingSet_IsTheEnginesFour()
    {
        new ViewerSettings().WorldLights.ShouldBe(4);
        EngineConVars.ByName(ViewerSettings.WorldLightsCommand).Default.ShouldBe("4");
        ViewerSettings.Parse("r_worldlights 1").WorldLights.ShouldBe(1);
    }

    [Test]
    public void Build_OneSlot_KeepsTheStrongerAndFoldsTheWeaker()
    {
        LightingState state = LocalLights.Build([Weaker, Stronger], 0f, 0f, 0f, default, slots: 1);

        state.Count.ShouldBe(1);
        state.SlotAt(0).ShouldBe(Stronger);

        // 400 at 1/100, square on.
        state.Cube.PositiveX.Red.ShouldBe(4f, 0.001f);
    }

    [Test]
    public void Build_NoSlots_FoldsEverything()
    {
        LightingState state = LocalLights.Build([Weaker, Stronger], 0f, 0f, 0f, default, slots: 0);

        state.Count.ShouldBe(0);
        state.Cube.PositiveX.Red.ShouldBe(12f, 0.001f);
    }

    [Test]
    public void Add_PastTheSlots_EvictsTheWeakerIntoTheCube()
    {
        LightingState state = new(default);

        LocalLights.Add(state, Weaker, 0f, 0f, 0f, slots: 1);
        LocalLights.Add(state, Stronger, 0f, 0f, 0f, slots: 1);

        state.Count.ShouldBe(1);
        state.SlotAt(0).ShouldBe(Stronger);
        state.Cube.PositiveX.Red.ShouldBe(4f, 0.001f);
    }

    /// <summary>`FUN_1801bcbb0`: over the hardware's four it is written back to four, which is no change, and flushes nothing.</summary>
    [Test]
    public void WorldLightSlots_OverTheHardware_IsClampedToFour()
    {
        LevelLighting lighting = LevelLighting.Unlit(NullLogger.Instance);

        lighting.WorldLightSlots = 9;

        lighting.WorldLightSlots.ShouldBe(4);
    }

    /// <summary>The average's own reader: the production path sets its limit from the level's, and a light that left past it folds.</summary>
    [Test]
    public void Pose_AConfigsWorldLights_ReachesTheLevelAndTheAverage()
    {
        EntityModelSet models = new();
        MomentScene scene = new(models, new ViewmodelScene(), NullLogger.Instance);
        MomentInfo info = new(0d, 0, false, null, null, 0.015f, 54f, WorldLights: 2);

        scene.Build([], [], info);
        scene.Pose(info);

        scene.Lighting.WorldLightSlots.ShouldBe(2);
        models.LightAverage.Limit.ShouldBe(2);
    }
}
