using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Tests;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// The decals a demo leaves on players' models and on the world, as they left the view (D214) — `ImpactCallback` into
/// `C_TFPlayer::AddDecal` and `CModelRender::AddDecal` for a model (tf_fx_impacts.cpp:51, c_tf_player.cpp), the
/// `RemoveAllDecals` `C_TFPlayer` calls, and `FireBullet`'s `UTIL_ImpactTrace` for the client's own bullets
/// (tf_player_shared.cpp:10510-10527).
/// </summary>
/// <remarks>
/// **None of this had a test while it lived in `MainForm`.** The demo is one this file wrote (D38, D213): a shooter, a
/// victim, and one server `Impact` dispatch on the victim. The victim's model is one triangle facing +x at the origin,
/// the fixture `ModelDecalsConformanceTests` shoots along −x, and both the server's impact and the client's bullet
/// arrive along that line, so a decal that passes every guard is placed and one that does not is not.
///
/// **Not here:** a decal on a static prop and the mesh the world's decals build, which need a map's faces and a placed
/// prop; `WorldDecals`, `DecalReplay` and `DecalMesh` have their own suites in Scene.Tests, and what is asserted here of
/// the world's half is when it uploads.
/// </remarks>
public sealed class DecalEffectsTests
{
    private const int Red = 2;
    private const int Blue = 3;
    private const int Shooter = 1;
    private const int Victim = 2;
    private const int Flesh = 1;
    private const int BloodMaterial = 5;
    private const int BulletTick = 200;
    private const string VictimModel = "models/player/scout.mdl";

    private const string DecalScript = """
        "TranslationData" { "-" "" "C" "Impact.Concrete" "F" "Impact.Flesh" }
        "Impact.Concrete" { "decals/concrete/shot1" "1" }
        "Impact.Flesh" { "decals/flesh/blood1" "1" }
        """;

    private static readonly float[] Identity = [1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f];

    // One triangle facing +X at the origin, which a shot along −X decals.
    private static readonly WorldVertex[] Face =
    [
        new(0f, 2f, 0f, 0f, 0f, 0f, 0f, 0f, NormalX: 1f, NormalZ: 0f, WeightA: 1f),
        new(0f, 0f, 2f, 0f, 0f, 0f, 0f, 0f, NormalX: 1f, NormalZ: 0f, WeightA: 1f),
        new(0f, -2f, -2f, 0f, 0f, 0f, 0f, 0f, NormalX: 1f, NormalZ: 0f, WeightA: 1f),
    ];

    private static readonly VphysicsSurfaceProps Surfaces = ParseSurfaces(
        "\"default\" { \"bulletimpact\" \"Default.BulletImpact\" } \"flesh\" { \"bulletimpact\" \"Flesh.BulletImpact\" }");

    private static readonly Dictionary<string, SoundScriptEntry> Scripts = new[] { "Default.BulletImpact", "Flesh.BulletImpact" }
        .ToDictionary(
            static name => name,
            static name => new SoundScriptEntry(
                name, 1, new SoundRange(1f, 1f), new SoundRange(100f, 100f), new SoundRange(75f, 75f), [name + ".wav"]),
            StringComparer.OrdinalIgnoreCase);

    private static readonly MapLevel Empty = new(
        null, null, null, new Dictionary<int, string>(), null, null, null, [], [], [], [], null, new VertexNormals([], []));

    /// <summary>A shooter and a victim standing still, and the server's impact on the victim arriving at tick 100.</summary>
    private static readonly DemoTimeline Timeline = DemoTimeline.Build(SyntheticEffects.Demo(
        [
            new(Shooter, SyntheticEffects.PlayerClass, SyntheticEffects.Standing(100f, 0f, -40f, Red)),
            new(Victim, SyntheticEffects.PlayerClass, SyntheticEffects.Standing(0f, 0f, -40f, Blue)),
        ],
        [string.Empty, "Impact"],
        [],
        [],
        new SyntheticEffects.Effect(100, "CTEEffectDispatch", new Dictionary<string, PropertyValue>(StringComparer.Ordinal)
        {
            ["m_iEffectName"] = PropertyValue.FromInt(1),
            ["entindex"] = PropertyValue.FromInt(Victim),
            ["m_vStart[0]"] = PropertyValue.FromFloat(100f),
            ["m_nSurfaceProp"] = PropertyValue.FromInt(Flesh + 1),
            ["m_nDamageType"] = PropertyValue.FromInt(Red),
            ["m_nHitBox"] = PropertyValue.FromInt(3),
        })));

    /// <summary>The tick the server's impact fires on: an interpolation window after it arrives.</summary>
    private static readonly int ImpactTick = Timeline.Dispatches.All.Single().Tick;

    private static readonly ScenePlayer VictimShown = new(Victim, 0f, 0f, -40f, Blue, 100, 2, LifeState: 0) { MaxHealth = 150 };

    private static readonly ScenePlayer ShooterShown = new(Shooter, 100f, 0f, -40f, Red, 100, 3, LifeState: 0);

    private static VphysicsSurfaceProps ParseSurfaces(string text)
    {
        VphysicsSurfaceProps props = new([]);
        props.ParseSurfaceData(Encoding.Latin1.GetBytes(text));
        return props;
    }

    private static ImpactDecals Decals() =>
        new(
            DecalEmitters.Parse(Encoding.UTF8.GetBytes(DecalScript)),
            new DecalMaterials(
                static path => path.StartsWith("materials/decals/", StringComparison.Ordinal)
                    ? Encoding.UTF8.GetBytes("\"LightmappedGeneric\" { \"$basetexture\" \"decals/any\" }")
                    : null,
                static _ => (64, 64)),
            [new BspTexinfo(SurfaceProperties.None, 0), new BspTexinfo(SurfaceProperties.Sky, 0)],
            ['C'],
            static surfaceProp => surfaceProp == Flesh ? 'F' : '\0');

    private static DecalAssets Assets(bool blood = true)
    {
        Dictionary<string, int> materials = new(StringComparer.OrdinalIgnoreCase) { ["decals/concrete/shot1"] = 6 };

        if (blood)
        {
            materials["decals/flesh/blood1"] = BloodMaterial;
        }

        return new DecalAssets(materials, [], [], new Dictionary<int, PlacedProp>());
    }

    private static DecalContent Content(IReadOnlyList<ShotImpact>? bullets = null, bool blood = true) =>
        new(new DecalMap(Empty, bullets ?? [], Decals(), SolidBrushEntities.None, Assets(blood)), Surfaces, Scripts);

    private static DecalFrame Frame(int tick, bool silent = false, params ScenePlayer[] shown) =>
        new(
            Timeline,
            null,
            null,
            tick,
            shown.Length > 0 ? shown : [ShooterShown, VictimShown],
            new BulletScene(Timeline, Empty, null, tick, true),
            2048,
            50,
            null,
            silent);

    /// <summary>The presenter over a victim posed and skinned, a device holding his model, and what was logged.</summary>
    private static (DecalEffects Decals, FakeDecalUpload Device, FakePosedEntities Models, RecordingLogger Log) Presenter()
    {
        FakePosedEntities models = new()
        {
            Posed = { Victim },
            Fractions = { [Victim] = 0.1f },
            Skinning = { [Victim] = (VictimModel, [Identity], 0, null) },
            Vertices = { [VictimModel] = Face },
        };
        FakeDecalUpload device = new()
        {
            Batches = { [VictimModel] = [new WorldBatch(0, 0, 3)] },
        };
        RecordingLogger log = new();

        return (new DecalEffects(models, new BulletStrikes(models, new RecordingLogger()), log), device, models, log);
    }

    [Test]
    public void StepOver_AServerImpactOnAnEnemy_PutsItsFleshDecalOnHisModel()
    {
        (DecalEffects decals, FakeDecalUpload device, _, RecordingLogger log) = Presenter();

        decals.StepOver(Frame(ImpactTick), Content(), device).ShouldBeEmpty("the server's impact has its own sound, in the landings");

        ModelDecals pool = device.ModelDecals.ShouldNotBeNull();

        pool.PerModel.ShouldBe(50, "r_maxmodeldecal");
        pool.Count.ShouldBe(1);
        pool.For(Victim).ShouldNotBeNull().Batches.ShouldHaveSingleItem().MaterialIndex.ShouldBe(BloodMaterial);
        log.Count("decals/flesh/blood1 radius 32 placed; 1 held").ShouldBe(1, "radius = max( w, h ) · $decalScale / 2");
    }

    [Test]
    public void StepOver_AnImpactAlreadyPlaced_IsNotPlacedAgainOnTheNextTick()
    {
        (DecalEffects decals, FakeDecalUpload device, _, _) = Presenter();

        decals.StepOver(Frame(ImpactTick), Content(), device);
        decals.StepOver(Frame(ImpactTick + 1), Content(), device);

        decals.ModelDecalCount.ShouldBe(1);
    }

    [TestCase(8, 0, TestName = "StepOver_AnImpactEightTicksBehindTheFirstFrame_IsSkipped")]
    [TestCase(7, 1, TestName = "StepOver_AnImpactSevenTicksBehindTheFirstFrame_IsStillProjected")]
    public void StepOver_AnImpactBehindTheFrame_IsProjectedOnlyWithinAFramesWorth(int behind, int expected)
    {
        (DecalEffects decals, FakeDecalUpload device, _, _) = Presenter();

        decals.StepOver(Frame(ImpactTick + behind), Content(), device);

        decals.ModelDecalCount.ShouldBe(expected, "the poses of the ticks passed over were never built");
    }

    [Test]
    public void StepOver_AStepBackward_ClearsTheModelsDecals()
    {
        (DecalEffects decals, FakeDecalUpload device, _, _) = Presenter();

        decals.StepOver(Frame(ImpactTick), Content(), device);
        decals.StepOver(Frame(ImpactTick - 1), Content(), device);

        decals.ModelDecalCount.ShouldBe(0);

        // And the tick it stepped back to is where it is now: the impact lands again when playback reaches it.
        decals.StepOver(Frame(ImpactTick), Content(), device);

        decals.ModelDecalCount.ShouldBe(1);
    }

    [Test]
    public void StepOver_AnImpactFromTheVictimsOwnTeam_LeavesNothing()
    {
        (DecalEffects decals, FakeDecalUpload device, _, RecordingLogger log) = Presenter();

        decals.StepOver(Frame(ImpactTick, false, VictimShown with { Team = Red }), Content(), device);

        decals.ModelDecalCount.ShouldBe(0);
        log.Count("none, same team").ShouldBe(1, "ImpactCallback: m_nDamageType carries the shooter's team");
    }

    [TestCase(PlayerConditions.Stealthed, TestName = "StepOver_AnImpactOnACloakedSpy_LeavesNothing")]
    [TestCase(PlayerConditions.Disguised, TestName = "StepOver_AnImpactOnADisguisedSpy_LeavesNothing")]
    [TestCase(PlayerConditions.Invulnerable, TestName = "StepOver_AnImpactOnAnUberchargedPlayer_LeavesNothing")]
    public void StepOver_AnImpactOnAPlayerAddDecalRefuses_LeavesNothing(int condition)
    {
        (DecalEffects decals, FakeDecalUpload device, _, RecordingLogger log) = Presenter();
        ScenePlayer hidden = VictimShown with { Conditions = new PlayerConditions(1 << condition, 0, 0, 0, 0) };

        decals.StepOver(Frame(ImpactTick, false, hidden), Content(), device);

        decals.ModelDecalCount.ShouldBe(0);
        log.Count("none, stealthed, disguised or invulnerable").ShouldBe(1);
    }

    [Test]
    public void StepOver_AnImpactWhoseRayMissesTheHitboxes_LeavesNothingAndSaysHowFarOff()
    {
        (DecalEffects decals, FakeDecalUpload device, FakePosedEntities models, RecordingLogger log) = Presenter();

        // Posed, and the ray meets no box: a miss is a miss, the collision box does not answer for him.
        models.Fractions.Clear();
        decals.StepOver(Frame(ImpactTick), Content(), device);

        decals.ModelDecalCount.ShouldBe(0);
        log.Count("the ray misses the hitboxes, the hit 0 across from the networked origin, 40 up; the server's box 3 is 7.5 outside it").ShouldBe(1);
    }

    [Test]
    public void StepOver_AnImpactOnAPlayerTheDeviceHoldsNoSkinnedModelOf_LeavesNothing()
    {
        (DecalEffects decals, FakeDecalUpload device, FakePosedEntities models, RecordingLogger log) = Presenter();

        models.Vertices.Clear();
        decals.StepOver(Frame(ImpactTick), Content(), device);

        decals.ModelDecalCount.ShouldBe(0);
        log.Count("none, no skinned model").ShouldBe(1);
    }

    [Test]
    public void StepOver_AnImpactWhoseDecalMaterialTheMapDidNotLoad_LeavesNothing()
    {
        (DecalEffects decals, FakeDecalUpload device, _, RecordingLogger log) = Presenter();

        decals.StepOver(Frame(ImpactTick), Content(blood: false), device);

        decals.ModelDecalCount.ShouldBe(0);
        log.Count("none, no decal material loaded").ShouldBe(1);
    }

    [Test]
    public void StepOver_AnImpactOnSomeoneTheFrameDoesNotShow_LeavesNothing()
    {
        (DecalEffects decals, FakeDecalUpload device, _, _) = Presenter();

        // An enemy of the shooter's is shown, and he is not the one the impact names.
        decals.StepOver(Frame(ImpactTick, false, VictimShown with { EntityIndex = 9 }), Content(), device);

        decals.ModelDecalCount.ShouldBe(0);
    }

    [Test]
    public void StepOver_AVictimHealedToFull_LosesHisDecals()
    {
        (DecalEffects decals, FakeDecalUpload device, _, _) = Presenter();

        decals.StepOver(Frame(ImpactTick), Content(), device);
        decals.StepOver(Frame(ImpactTick + 1, false, VictimShown with { Health = 150 }), Content(), device);

        decals.ModelDecalCount.ShouldBe(0, "C_TFPlayer calls RemoveAllDecals on a heal to full");
    }

    [Test]
    public void StepOver_AVictimHealedShortOfFull_KeepsHisDecals()
    {
        (DecalEffects decals, FakeDecalUpload device, _, _) = Presenter();

        decals.StepOver(Frame(ImpactTick), Content(), device);
        decals.StepOver(Frame(ImpactTick + 1, false, VictimShown with { Health = 149 }), Content(), device);

        decals.ModelDecalCount.ShouldBe(1);
    }

    /// <summary>The client's own bullet along −x from the shooter's side, which the world stops far behind the victim.</summary>
    private static ShotImpact Pellet(int bullet = 0, int team = Red) =>
        new(0, bullet, BulletTick, Shooter, team, (100f, 0f, 0f), (-500f, 0f, 0f), (-900f, 0f, 0f), 0);

    private static IReadOnlyList<SceneSound> FleshSounds(int bullet) => ImpactSounds.For(
        new BulletLanding(BulletTick, (0f, 0f, 0f), "Flesh.BulletImpact", false)
        {
            Order = BulletStrikes.ShotOrder(Timeline, 0, bullet),
        },
        ImpactSounds.SeedFor(bullet),
        Scripts);

    [Test]
    public void StepOver_AClientBulletThatEndsOnAnEnemy_SoundsOnHimAndDecalsHisModel()
    {
        (DecalEffects decals, FakeDecalUpload device, _, _) = Presenter();

        IReadOnlyList<SceneSound> heard = decals.StepOver(Frame(BulletTick), Content([Pellet()]), device);

        heard.ShouldNotBeEmpty();
        heard.ShouldBe(FleshSounds(0), "flesh's bulletimpact at the trace's end, in the shot's place in the stream");
        device.ModelDecals.ShouldNotBeNull().For(Victim).ShouldNotBeNull().Batches.ShouldHaveSingleItem().MaterialIndex.ShouldBe(BloodMaterial);
    }

    [Test]
    public void StepOver_AMatchOfBulletsOutsideTheFramesWindow_ExaminesOnlyTheOneThatLandedInIt()
    {
        (DecalEffects decals, FakeDecalUpload device, _, _) = Presenter();

        // The first frame's window is (192, 200]: a thousand on its open edge, the frame's own pellet, one a tick after.
        ShotImpact[] bullets =
        [
            .. Enumerable.Range(1, 1000).Select(static shot => Pellet() with { Shot = shot, Tick = BulletTick - 8 }),
            Pellet(),
            Pellet() with { Shot = 2000, Tick = BulletTick + 1 },
        ];

        IReadOnlyList<SceneSound> heard = decals.StepOver(Frame(BulletTick), Content(bullets), device);

        heard.ShouldBe(FleshSounds(0));
        decals.ModelDecalCount.ShouldBe(1);
        decals.ModelDecalExamined.ShouldBe((0, 1), "the server's impact and every other bullet are outside the window");
    }

    [Test]
    public void StepOver_BulletsOutOfTickOrder_AreAllExaminedAndTheFramesStillLands()
    {
        (DecalEffects decals, FakeDecalUpload device, _, _) = Presenter();

        decals.StepOver(Frame(BulletTick), Content([Pellet(), Pellet() with { Shot = 1, Tick = BulletTick - 50 }]), device);

        decals.ModelDecalCount.ShouldBe(1);
        decals.ModelDecalExamined.ShouldBe((0, 2), "a list out of tick order cannot be searched, so it is walked whole");
    }

    [Test]
    public void StepOver_AClientBulletInASeeksReplay_DecalsAndIsNotHeard()
    {
        (DecalEffects decals, FakeDecalUpload device, _, _) = Presenter();

        decals.StepOver(Frame(BulletTick, silent: true), Content([Pellet()]), device).ShouldBeEmpty("TF2's fast-forward is heard no more than ours");

        decals.ModelDecalCount.ShouldBe(1);
    }

    [Test]
    public void StepOver_AClientBulletATeammateStopped_IsSilentAndLeavesNothing()
    {
        (DecalEffects decals, FakeDecalUpload device, _, _) = Presenter();

        decals.StepOver(Frame(BulletTick), Content([Pellet(team: Blue)]), device).ShouldBeEmpty("only an enemy gets UTIL_ImpactTrace");

        decals.ModelDecalCount.ShouldBe(0);
    }

    [Test]
    public void StepOver_TwoPelletsOfOneShotOnTheSameSpot_SoundOnce()
    {
        (DecalEffects decals, FakeDecalUpload device, _, _) = Presenter();

        IReadOnlyList<SceneSound> heard = decals.StepOver(Frame(BulletTick), Content([Pellet(0), Pellet(1)]), device);

        heard.ShouldBe(FleshSounds(0), "ImpactSoundGroup: the same sound within 300 units of one this shot played");
        decals.ModelDecalCount.ShouldBe(2, "and each pellet still leaves its own decal");
    }

    [Test]
    public void StepOver_AClientBulletIntoWater_SoundsNowhereAndDecalsNobody()
    {
        (DecalEffects decals, FakeDecalUpload device, _, _) = Presenter();

        decals.StepOver(Frame(BulletTick), Content([Pellet() with { WaterEntry = (50f, 0f, 0f) }]), device).ShouldBeEmpty();

        decals.ModelDecalCount.ShouldBe(0);
    }

    [Test]
    public void StepOver_AClientBulletThatMissesEveryone_SoundsOnTheWorldBehind()
    {
        (DecalEffects decals, FakeDecalUpload device, FakePosedEntities models, _) = Presenter();

        models.Fractions.Clear();

        IReadOnlyList<SceneSound> heard = decals.StepOver(Frame(BulletTick), Content([Pellet()]), device);

        heard.ShouldBe(
            ImpactSounds.For(
                new BulletLanding(BulletTick, (-500f, 0f, 0f), "Default.BulletImpact", false) { Order = BulletStrikes.ShotOrder(Timeline, 0, 0) },
                ImpactSounds.SeedFor(0),
                Scripts),
            "the struck texinfo's surface, at the world's end");
        decals.ModelDecalCount.ShouldBe(0);
    }

    [Test]
    public void StepOver_TheWorldsDecals_UploadWhenThePoolChangesAndNotOtherwise()
    {
        (DecalEffects decals, FakeDecalUpload device, _, _) = Presenter();

        decals.StepOver(Frame(50), Content(), device);
        device.Uploads.Count.ShouldBe(1, "the first frame of a map");

        decals.StepOver(Frame(51), Content(), device);
        device.Uploads.Count.ShouldBe(1, "an unchanged pool costs nothing");

        decals.ClearMap();
        decals.StepOver(Frame(52), Content(), device);
        device.Uploads.Count.ShouldBe(2, "a new map's pool is new");
    }

    [Test]
    public void StepOver_AClientBulletAStaticPropStopped_DecalsThePropAndDrawsItAfterTheWorldsOwn()
    {
        (DecalEffects decals, FakeDecalUpload device, FakePosedEntities models, _) = Presenter();

        // Nobody is in the way: the victim is posed and the ray misses his hitboxes.
        models.Fractions.Clear();

        // The prop is the one triangle facing +x at the origin; the bullet stops on it, coming along −x.
        DecalAssets assets = Assets() with
        {
            Shaders = [string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, "DecalModulate"],
            PlacedProps = new Dictionary<int, PlacedProp>
            {
                [0] = new PlacedProp(
                    new PropShape(
                        [new Vector3(0f, 2f, 0f), new Vector3(0f, 0f, 2f), new Vector3(0f, -2f, -2f)],
                        [Vector3.UnitX, Vector3.UnitX, Vector3.UnitX]),
                    PropTransform.Identity),
            },
        };
        ShotImpact onTheProp = new(0, 0, 50, Shooter, Red, (100f, 0f, 0f), (0f, 0f, 0f), (-900f, 0f, 0f), -1, Normal: (1f, 0f, 0f))
        {
            StaticProp = 0,
        };

        decals.StepOver(
            Frame(50), new DecalContent(new DecalMap(Empty, [onTheProp], Decals(), SolidBrushEntities.None, assets), Surfaces, Scripts), device);

        (WorldVertex[] vertices, WorldBatch[] batches, WorldBatch[] entityBatches) = device.Uploads[^1];

        batches.ShouldBeEmpty("the world itself took no decal");
        entityBatches.ShouldHaveSingleItem().MaterialIndex.ShouldBe(6, "decals/concrete/shot1, the prop's own decal run");
        vertices.Length.ShouldBe(entityBatches[0].VertexCount);
        vertices.Select(static vertex => (vertex.Red, vertex.Green, vertex.Blue)).Distinct().ShouldBe(
            [(Render.Device3D.ModulateTwiceLight, Render.Device3D.ModulateTwiceLight, Render.Device3D.ModulateTwiceLight)],
            "a mod2x decal's corners carry the pipeline's light");
        decals.ModelDecalCount.ShouldBe(1, "one pool for every model: the prop's decal is held with the players'");
    }

    [Test]
    public void StepOver_WithNoMapAssets_TouchesNothing()
    {
        (DecalEffects decals, FakeDecalUpload device, _, _) = Presenter();

        decals.StepOver(Frame(ImpactTick), new DecalContent(new DecalMap(Empty, [], Decals(), SolidBrushEntities.None, null), Surfaces, Scripts), device);

        device.Uploads.ShouldBeEmpty();
        device.ModelDecals.ShouldBeNull();
    }

    private static ScenePlayer Was(int health = 100, int playerClass = 2, PlayerConditions conditions = default, int? disguise = null) =>
        new(Victim, 0f, 0f, 0f, Blue, health, playerClass, Conditions: conditions, DisguiseClass: disguise) { MaxHealth = 150 };

    [Test]
    public void WipesDecals_EachChangeCTFPlayerClearsOn_Wipes()
    {
        DecalEffects.WipesDecals(Was(), Was(health: 150)).ShouldBeTrue("a heal to full");
        DecalEffects.WipesDecals(Was(), Was(conditions: new PlayerConditions(1 << PlayerConditions.Invulnerable, 0, 0, 0, 0))).ShouldBeTrue("an übercharge");
        DecalEffects.WipesDecals(Was(), Was(playerClass: 5)).ShouldBeTrue("a new class is a new model");
        DecalEffects.WipesDecals(Was(), Was(conditions: new PlayerConditions(1 << PlayerConditions.Stealthed, 0, 0, 0, 0))).ShouldBeTrue("a cloak");
        DecalEffects.WipesDecals(Was(), Was(disguise: 7)).ShouldBeTrue("a new disguise class");
    }

    [Test]
    public void WipesDecals_AChangeThatClearsNothing_DoesNot()
    {
        PlayerConditions cloaked = new(1 << PlayerConditions.Stealthed, 0, 0, 0, 0);

        DecalEffects.WipesDecals(Was(), Was()).ShouldBeFalse();
        DecalEffects.WipesDecals(Was(), Was(health: 149)).ShouldBeFalse("healed, and not to full");
        DecalEffects.WipesDecals(Was(health: 150), Was(health: 150)).ShouldBeFalse("full already: no heal happened");
        DecalEffects.WipesDecals(Was(health: 150), Was(health: 100)).ShouldBeFalse("hurt");
        DecalEffects.WipesDecals(Was(conditions: cloaked), Was(conditions: cloaked)).ShouldBeFalse("still cloaked: the cloak is not new");
    }

    [TestCase(72f, true, TestName = "Reaches_ABulletPassingSeventyTwoUnitsFromAnEnemysCentre_Can")]
    [TestCase(73f, false, TestName = "Reaches_ABulletPassingSeventyThreeUnitsFromAnEnemysCentre_Cannot")]
    public void Reaches_ABulletsPath_MustComeWithinReachOfTheHullsCentre(float beside, bool expected)
    {
        // His hull's centre is 41 up; the path runs along x at that height, `beside` units to one side.
        ShotImpact bullet = new(0, 0, 100, Shooter, Red, (-500f, beside, 41f), (500f, beside, 41f), (900f, beside, 41f), 0);

        DecalEffects.Reaches(bullet, Was()).ShouldBe(expected);
    }

    [Test]
    public void Reaches_ATeammateOrADeadMan_IsNeverReached()
    {
        ShotImpact bullet = new(0, 0, 100, Shooter, Red, (-500f, 0f, 41f), (500f, 0f, 41f), (900f, 0f, 41f), 0);

        DecalEffects.Reaches(bullet, Was()).ShouldBeTrue("the control: a living enemy on the path");
        DecalEffects.Reaches(bullet, Was() with { Team = Red }).ShouldBeFalse("FireBullet decals nobody on the shooter's team");
        DecalEffects.Reaches(bullet, Was() with { LifeState = 1 }).ShouldBeFalse();
    }

    [Test]
    public void Reaches_ABulletThatStopsShortOfHim_CannotReachHim()
    {
        ShotImpact bullet = new(0, 0, 100, Shooter, Red, (-500f, 0f, 41f), (-100f, 0f, 41f), (900f, 0f, 41f), 0);

        DecalEffects.Reaches(bullet, Was()).ShouldBeFalse("the path ends at the world's end, a hundred units before him");
    }

    [Test]
    public void DrawnVertices_ABodyNumber_DrawsOnlyTheAlternativeItChoosesOfEachPart()
    {
        WorldBatch[] batches = [new(0, 0, 3, BodyPart: 0, BodyModel: 0), new(0, 3, 3, BodyPart: 0, BodyModel: 1)];

        Func<int, bool> drawn = DecalEffects.DrawnVertices(batches, 6, body: 1, [(1, 2)]);

        Enumerable.Range(-1, 8).Where(drawn).ShouldBe([3, 4, 5], "body 1 of a part with two alternatives is the second");
        Enumerable.Range(0, 6).Where(DecalEffects.DrawnVertices(batches, 6, body: 0, null)).ShouldBe([0, 1, 2, 3, 4, 5], "no parts: every run");
    }

    [Test]
    public void LastWipes_APlayerWhoWasDeadAndOneWhoNeverWas_AreHisLastDeadTickAndTheFloor()
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticEffects.Demo(
            [
                new(Shooter, SyntheticEffects.PlayerClass, SyntheticEffects.Standing(100f, 0f, 0f, Red)),
                new(Victim, SyntheticEffects.PlayerClass, SyntheticEffects.Standing(0f, 0f, 0f, Blue)),
            ],
            [],
            [],
            [
                new SyntheticEffects.Change(150, Victim, new Dictionary<string, PropertyValue>(StringComparer.Ordinal) { ["m_lifeState"] = PropertyValue.FromInt(1) }),
                new SyntheticEffects.Change(160, Victim, new Dictionary<string, PropertyValue>(StringComparer.Ordinal) { ["m_lifeState"] = PropertyValue.FromInt(0) }),
            ]));

        Dictionary<int, int> wiped = DecalEffects.LastWipes(timeline, target: 300, floor: 20);

        wiped[Victim].ShouldBe(159, "the last tick he was dead: nothing before it can still show");
        wiped[Shooter].ShouldBe(20, "nothing wiped him that far back");
    }

    [Test]
    public void ReplayStartOver_AJumpPastAnImpactOnALivingPlayer_StartsItsWarmUpBeforeIt()
    {
        (DecalEffects decals, _, _, _) = Presenter();

        decals.ReplayStartOver(Frame(300), Content(), 300).ShouldBe(ImpactTick - 16 - 1, "sixteen ticks of warm-up, and the tick before");
    }

    [Test]
    public void ReplayStartOver_AStepForwardOfASecondOrLess_NeedsNoReplay()
    {
        (DecalEffects decals, FakeDecalUpload device, _, _) = Presenter();

        decals.StepOver(Frame(300), Content(), device);

        decals.ReplayStartOver(Frame(366), Content(), 366).ShouldBeNull("the ordinary step covers it");
        decals.ReplayStartOver(Frame(367), Content(), 367).ShouldNotBeNull("the control: one tick more is a seek");
        decals.ReplayStartOver(Frame(299), Content(), 299).ShouldNotBeNull("and so is any step back");
    }

    [Test]
    public void ReplayOver_ASeekPastAnImpact_PosesItsWarmUpAndItsArrivalAndLandsIt()
    {
        (DecalEffects decals, FakeDecalUpload device, _, _) = Presenter();
        List<int> posed = [];
        int start = decals.ReplayStartOver(Frame(300), Content(), 300).ShouldNotBeNull();

        DecalReplayReport report = decals.ReplayOver(
            start,
            300,
            device,
            tick =>
            {
                posed.Add(tick);

                // The demo's own players, as a posed tick shows them: a state that differs from the demo's would
                // read as a change of class on the ticks between, which wipes.
                return (Frame(tick, silent: true, [.. Timeline.PlayersAt(tick)]), Content());
            });

        posed.ShouldBe(Enumerable.Range(ImpactTick - 16, 25), "sixteen ticks before the landing to eight after, and no others");
        report.Stepped.ShouldBe(25);
        report.Held.ShouldBe(1, "the impact landed on the pose of its own tick");
        device.ModelDecals.ShouldNotBeNull().For(Victim).ShouldNotBeNull();
    }

    [Test]
    public void ReplayOver_ASeek_ForgetsWhatTheModelsHeldBefore()
    {
        (DecalEffects decals, FakeDecalUpload device, _, _) = Presenter();

        decals.StepOver(Frame(BulletTick), Content([Pellet()]), device);
        decals.ModelDecalCount.ShouldBe(1);

        // Back to before anything landed: the replay finds nothing to pose and the pellet's decal is gone.
        int start = decals.ReplayStartOver(Frame(50), Content(), 50).ShouldNotBeNull();
        DecalReplayReport report = decals.ReplayOver(start, 50, device, tick => (Frame(tick, silent: true), Content()));

        report.ShouldBe(new DecalReplayReport(0, report.Skipping, 0));
    }

    [Test]
    public void LandingsOver_TheServersImpacts_AreTheWorldsItLetsThroughAndEveryOneOnAnEntityInTickOrder()
    {
        ShotImpact onAWall = new(-1, 0, 50, Shooter, Red, (0f, 0f, 0f), (10f, 20f, 30f), (40f, 0f, 0f), 0, SurfaceProp: Flesh);
        ShotImpact onTheSky = onAWall with { Texinfo = 1 };

        IReadOnlyList<BulletLanding> landings = DecalEffects.LandingsOver([onTheSky, onAWall, Pellet()], Decals(), Surfaces, Timeline);

        SceneEffectDispatch dispatch = Timeline.Dispatches.All.Single();

        // The surface file's reader lowercases what it reads; the soundscripts are looked up without regard to case.
        landings.ShouldBe(
        [
            new BulletLanding(50, (10f, 20f, 30f), "flesh.bulletimpact", false)
            {
                Order = BulletStrikes.ShotOrder(Timeline, -1, 0),
                Reliable = dispatch.Reliable,
            },
            new BulletLanding(ImpactTick, (0f, 0f, 0f), "flesh.bulletimpact", true)
            {
                Order = new ClientSoundOrder(ClientSoundPhase.TempEntities, dispatch.TempEntity, 0),
                Reliable = dispatch.Reliable,
            },
        ]);
    }
}
