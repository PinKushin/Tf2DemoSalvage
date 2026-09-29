using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>B425: the engine's dynamic lights — the list, its decay, and what a model draw makes of one.</summary>
/// <remarks>
/// **Read from `engine.dll`; `dlight_t` is `public/dlight.h:43`.** `IVEfx` (`VEngineEffects001`, vtable `0x180393760`)
/// slot 4 `CL_AllocDlight` → `0x18008a9c0` over 32 dlights at `0x180533c80`, slot 5 `CL_AllocElight` → `0x18008aa60`
/// over 64 elights at `0x180534480`, both 64-byte `dlight_t`. The slot (`0x18008aac0`): the first whose key equals a
/// nonzero key, else the first whose `die` is below the client time, else slot 0; the slot is zeroed and keyed, and a
/// dlight's bit is set in the active mask `0x1806996c4`. `CL_DecayLights` (`0x18008b2f0`, from `_Host_RunFrame_Render`
/// AFTER `SCR_UpdateScreen`): nothing when the frame time is not positive; a light past `die` goes to radius 0, else
/// loses `decay · frametime`, clamped at 0. The model draw (`LightcacheGet` `0x1801b9cd0` flag 2 →
/// `0x1801b7a10`) takes each active dlight and each elight of positive radius whose `flags &amp; 0xe` is zero and whose
/// cluster is in the PVS of the model's leaf, converts it (`0x1801bb940`) and ranks it with the world lights.
/// </remarks>
public sealed class DynamicLightConformanceTests
{
    [Test]
    public void AllocDlight_ANewKey_IsZeroedKeyedAndActive()
    {
        DynamicLights lights = new() { Time = 1f };
        DynamicLight first = lights.AllocDlight(5);
        first.Radius = 50f;
        first.Die = 9f;

        DynamicLight again = lights.AllocDlight(5);

        again.ShouldBeSameAs(first);
        again.Key.ShouldBe(5);
        again.Radius.ShouldBe(0f, "the slot is zeroed on every allocation");
        again.Die.ShouldBe(0f);
    }

    [Test]
    public void AllocDlight_KeyZeroWithSlotZeroLive_TakesTheFirstExpiredSlot()
    {
        DynamicLights lights = new() { Time = 1f };
        lights.AllocDlight(0).Die = 5f;

        DynamicLight next = lights.AllocDlight(0);

        next.ShouldBeSameAs(lights.Dlights[1]);
    }

    [Test]
    public void AllocDlight_EverySlotLive_StealsSlotZero()
    {
        DynamicLights lights = new() { Time = 1f };

        for (int slot = 0; slot < DynamicLights.MaxDlights; slot++)
        {
            lights.AllocDlight(slot + 1).Die = 5f;
        }

        lights.AllocDlight(100).ShouldBeSameAs(lights.Dlights[0]);
    }

    [Test]
    public void Slots_Counts_AreTheEnginesArrays()
    {
        DynamicLights lights = new();

        lights.Dlights.Count.ShouldBe(32, "MAX_DLIGHTS, iefx.h:22; 0x800 bytes cleared at 0x180533c80");
        lights.Elights.Count.ShouldBe(64, "0x1000 bytes cleared at 0x180534480");
    }

    [TestCase(0.5f, 155f)]
    [TestCase(1.0f, 55f)]
    [TestCase(2.0f, 0f)]
    public void Decay_ADlightWithDecay200_LosesDecayTimesFrameTimeClampedAtZero(float frameTime, float radius)
    {
        DynamicLights lights = new() { Time = 1f };
        DynamicLight light = lights.AllocDlight(1);
        light.Radius = 255f;
        light.Decay = 200f;
        light.Die = 100f;

        lights.Decay(frameTime);

        light.Radius.ShouldBe(radius);
    }

    [TestCase(0.5f, 0f)]
    [TestCase(1.0f, 255f)]
    public void Decay_PastOrAtDie_ZeroesOnlyWhenTheTimeIsPastIt(float die, float radius)
    {
        DynamicLights lights = new() { Time = 1f };
        DynamicLight light = lights.AllocDlight(1);
        light.Radius = 255f;
        light.Die = die;

        lights.Decay(0.1f);

        light.Radius.ShouldBe(radius, "fVar5 < die || fVar5 == die keeps it; decay 0 leaves the radius");
    }

    [Test]
    public void Decay_AZeroFrameTime_ChangesNothingEvenPastDie()
    {
        DynamicLights lights = new() { Time = 10f };
        DynamicLight light = lights.AllocDlight(1);
        light.Radius = 255f;
        light.Die = 1f;

        lights.Decay(0f);

        light.Radius.ShouldBe(255f);
    }

    [Test]
    public void Decay_AnElight_DecaysAndDiesLikeADlight()
    {
        DynamicLights lights = new() { Time = 1f };
        DynamicLight decaying = lights.AllocElight(1);
        decaying.Radius = 64f;
        decaying.Decay = 1280f;
        decaying.Die = 1.05f;
        DynamicLight dead = lights.AllocElight(2);
        dead.Radius = 64f;
        dead.Die = 0.5f;

        lights.Decay(0.025f);

        decaying.Radius.ShouldBe(32f);
        dead.Radius.ShouldBe(0f);
    }

    /// <summary>`0x1801bb940`: an explosion-shaped dlight, `fx_explosion.cpp:721`'s fields.</summary>
    [Test]
    public void ToWorldLight_APointDlight_IsThePointLightTheEngineBuilds()
    {
        DynamicLight light = new()
        {
            X = 1f, Y = 2f, Z = 3f, Radius = 255f, Red = 255, Green = 220, Blue = 128, Style = 4,
        };

        BspWorldLight world = DynamicLights.ToWorldLight(light, cluster: 7);

        world.Kind.ShouldBe(WorldLightKind.Point);
        world.Origin.ShouldBe((1f, 2f, 3f));
        world.Intensity.ShouldBe((1f, 220f * (1f / 255f), 128f * (1f / 255f)), "color · table[exponent], 2^e / 255 at 0x18047e280");
        world.Radius.ShouldBe(255f);
        world.ConstantAttenuation.ShouldBe(0f);
        world.LinearAttenuation.ShouldBe(0f);
        world.QuadraticAttenuation.ShouldBe(1f / (255f * (1f / 256f) * 255f), "1 / (r · max(minlight, 1/256) · r)");
        world.Style.ShouldBe(4);
        world.Cluster.ShouldBe(7);
        world.Exponent.ShouldBe(0f, "never written; the static buffer is zero");
    }

    [Test]
    public void ToWorldLight_ExponentAndFloors_ScaleAndClampAsTheEngine()
    {
        DynamicLight light = new() { Radius = 0.05f, MinLight = 0.5f, Red = 255, Green = 100, Blue = 30, Exponent = 8 };

        BspWorldLight world = DynamicLights.ToWorldLight(light, cluster: 0);

        world.Intensity.ShouldBe((255f * (256f / 255f), 100f * (256f / 255f), 30f * (256f / 255f)));
        world.Radius.ShouldBe(0.1f, "a radius at or below 0.1 is 0.1");
        world.QuadraticAttenuation.ShouldBe(1f / (0.1f * 0.5f * 0.1f), "a minlight above 1/256 is kept");
    }

    [Test]
    public void ToWorldLight_AnOuterAngle_IsASpotlightWithCosines()
    {
        DynamicLight light = new() { Radius = 100f, InnerAngle = 30f, OuterAngle = 45f, Direction = (0f, 0f, -1f) };

        BspWorldLight world = DynamicLights.ToWorldLight(light, cluster: 0);

        world.Kind.ShouldBe(WorldLightKind.Spotlight);
        world.Normal.ShouldBe((0f, 0f, -1f));
        world.StopDot.ShouldBe((float)Math.Cos(30d * 0.017453292519943295));
        world.StopDot2.ShouldBe((float)Math.Cos(45d * 0.017453292519943295));
    }

    [TestCase(0, true)]
    [TestCase(DynamicLights.NoWorldIllumination, true)]
    [TestCase(DynamicLights.NoModelIllumination, false)]
    [TestCase(4, false)]
    [TestCase(8, false)]
    public void ModelLights_ByFlags_KeepOnlyThoseWithNoBitIn0xE(int flags, bool kept)
    {
        DynamicLights lights = new() { Time = 1f };
        DynamicLight dlight = lights.AllocDlight(1);
        dlight.Radius = 10f;
        dlight.Flags = flags;
        DynamicLight elight = lights.AllocElight(1);
        elight.Radius = 10f;
        elight.Flags = flags;

        List<DynamicLight> found = [];
        lights.ModelLights(found);

        found.Count.ShouldBe(kept ? 2 : 0);
    }

    [Test]
    public void ModelLights_ADlightDecayedOut_IsNoLongerActive()
    {
        DynamicLights lights = new() { Time = 1f };
        DynamicLight light = lights.AllocDlight(1);
        light.Radius = 10f;
        light.Die = 0.5f;

        lights.Decay(0.01f);

        List<DynamicLight> found = [];
        lights.ModelLights(found);
        found.ShouldBeEmpty();
    }

    /// <summary>The model draw: a live dlight 20 units above a model is one of its local lights, exactly.</summary>
    [Test]
    public void ModelLightingAt_ALiveDlightInPvs_IsALocalLight()
    {
        LevelLighting lighting = StaticPlusDynamicLightingConformanceTests.Map([]);
        DynamicLights lights = new() { Time = 1f };
        lighting.Dynamic = lights;
        DynamicLight light = lights.AllocDlight(1);
        (light.X, light.Y, light.Z, light.Radius, light.Red, light.Green, light.Blue) = (0f, 0f, 120f, 255f, 255, 220, 128);

        LocalLight local = lighting.ModelLightingAt(0f, 0f, 100f).Locals.ShouldHaveSingleItem();

        local.ShouldBe(new LocalLight(
            0f, 0f, 120f, 1f, 220f * (1f / 255f), 128f * (1f / 255f), 0f, 0f, 1f / (255f * (1f / 256f) * 255f), 255f));
    }

    [Test]
    public void ModelLightingAt_ADlightOutOfPvs_IsNotALocalLight()
    {
        LevelLighting lighting = StaticPlusDynamicLightingConformanceTests.Map([]);
        DynamicLights lights = new() { Time = 1f };
        lighting.Dynamic = lights;
        DynamicLight light = lights.AllocDlight(1);
        (light.X, light.Y, light.Z, light.Radius, light.Red) = (0f, 0f, -20f, 255f, 255);

        lighting.ModelLightingAt(0f, 0f, 100f).Locals.ShouldBeEmpty("cluster 1 is not in cluster 0's PVS");
    }

    [Test]
    public void ModelLightingAt_AfterTheDlightDies_HasNoLocalLight()
    {
        LevelLighting lighting = StaticPlusDynamicLightingConformanceTests.Map([]);
        DynamicLights lights = new() { Time = 1f };
        lighting.Dynamic = lights;
        DynamicLight light = lights.AllocDlight(1);
        (light.X, light.Y, light.Z, light.Radius, light.Red, light.Die) = (0f, 0f, 120f, 255f, 255, 1.05f);

        lighting.ModelLightingAt(0f, 0f, 100f).Locals.Count.ShouldBe(1);

        lights.Time = 1.1f;
        lights.Decay(0.05f);

        lighting.ModelLightingAt(0f, 0f, 100f).Locals.ShouldBeEmpty();
    }

    /// <summary>`tf_projectile_dragons_fury.cpp:509-528`: the local player's own fireball.</summary>
    [Test]
    public void DragonsFury_TheRecordersFireball_AllocatesTheEnginesDlight()
    {
        DynamicLights lights = new() { Time = 2f };

        DynamicLights.DragonsFury(lights, entity: 300, (1f, 2f, 3f));

        DynamicLight light = lights.Dlights[0];
        light.Key.ShouldBe(300);
        (light.X, light.Y, light.Z).ShouldBe((1f, 2f, 3f));
        light.Radius.ShouldBe(100f);
        (light.Red, light.Green, light.Blue, light.Exponent).ShouldBe(((byte)255, (byte)100, (byte)30, (sbyte)8));
        light.Die.ShouldBe(2.05f);
        light.Decay.ShouldBe(0f);
        light.Flags.ShouldBe(0);
    }
}
