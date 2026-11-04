using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;

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
    /// <summary>Two lamps ten units along +X of the point, a falloff of 1/100: 8 and 4 on the +X face. Handed weaker first, so ranking keeps the stronger and not the list's order.</summary>
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

    /// <summary>
    /// The production path, to what the draw carries: one slot from a config, through `MomentScene.Build` and `Pose`, onto the lamp that wins it, the
    /// other in the cube. The level's light is a real state through a real leaf tree; a light at (10 0 100) from the walker at (0 0 100).
    /// </summary>
    [Test]
    public void Pose_AConfigsWorldLightsOfOne_DrawsOneLampAndTheOtherInTheCube()
    {
        ModelInstance drawn = Drawn(worldLights: 1);

        drawn.Locals.ShouldNotBeNull().Single().Red.ShouldBe(800f, 0.001f);
        drawn.Light.ShouldNotBeNull().PositiveX.Red.ShouldBe(4f, 0.001f);

        // The control, the default: both lamps in slots and nothing in the cube.
        ModelInstance control = Drawn(worldLights: 4);

        control.Locals.ShouldNotBeNull().Count.ShouldBe(2);
        control.Light.ShouldNotBeNull().PositiveX.Red.ShouldBe(0f, 0.001f);
    }

    private static ModelInstance Drawn(int worldLights)
    {
        BspWorldLight weaker = Weaker with { Origin = (10f, 0f, 100f), Cluster = 0 };
        BspWorldLight stronger = Stronger with { Origin = (10f, 0f, 100f), Cluster = 0 };
        SceneProp prop = EntityModelSetPrePoseTests.Prop("models/player/demo.mdl", 5) with { Pose = new ScenePose { Z = 100f, Scale = 1f } };
        EntityModelSet models = new();

        models.Add([prop], static _ => EntityModelSetPrePoseTests.Skinned("root"));

        MomentScene scene = new(models, new ViewmodelScene(), NullLogger.Instance)
        {
            Lighting = StaticPlusDynamicLightingConformanceTests.Map([weaker, stronger]),
        };
        MomentInfo info = new(0d, 0, false, null, null, 0.015f, 54f, WorldLights: worldLights);

        scene.Build([], [prop], info);
        scene.Pose(info);

        models.LightAverage.Limit.ShouldBe(scene.Lighting.WorldLightSlots);

        return scene.Instances.Single();
    }

    /// <summary>A change relights every handle (`FUN_1801bcbb0`) and a handle keeps the world box it was made with, or its list and its dlights would test the point's cell.</summary>
    [Test]
    public void WorldLightSlots_Changed_RanksAHandleAgainWithItsListAndBox()
    {
        // Only the box lists this lamp, and only a listed lamp makes a ranking: a list rebuilt from the point's cell would rank nothing.
        BspWorldLight onlyTheBox = new((0f, 0f, 160f), (9f, 9f, 9f), default, WorldLightKind.Point, QuadraticAttenuation: 1f, Radius: 10f, Style: 5, Cluster: 0, LumpIndex: 0);
        LevelLighting lighting = StaticPlusDynamicLightingConformanceTests.Map([onlyTheBox]);

        lighting.PrecacheStaticProp(1000, 0f, 0f, 100f, new LightBounds((0f, 0f, 0f), (0f, 0f, 155f)));
        lighting.StaticPlusDynamicLights(1000, 0f, 0f, 100f);
        lighting.StyledRankings.ShouldBe(1, "the control: the box listed it");

        lighting.WorldLightSlots = 1;
        lighting.StaticPlusDynamicLights(1000, 0f, 0f, 100f);

        lighting.StyledRankings.ShouldBe(2);
        lighting.StyledListsBuilt.ShouldBe(1, "the list was kept, not built again");
    }

    /// <summary>A dlight lighting a static prop writes the table as a moving model's does: `FUN_1801bbed0` passes `DAT_180775730` (`0x1801bc569`).</summary>
    [Test]
    public void StaticPropFullLightingAt_ADlightOnAStaticProp_WritesItsConvertedSlot()
    {
        LevelLighting lighting = StaticPlusDynamicLightingConformanceTests.Map([]);
        DynamicLights dynamic = new();
        DynamicLight light = dynamic.AllocDlight(1);

        (light.X, light.Y, light.Z, light.Radius, light.Red, light.Green, light.Blue) = (0f, 0f, 120f, 100f, 200, 200, 200);
        light.Die = 100f;
        lighting.Dynamic = dynamic;

        lighting.StaticPropFullLightingAt(1000, 0f, 0f, 100f);

        lighting.ConvertedDlights[light.Slot].Origin.ShouldBe((0f, 0f, 120f));
    }
}
