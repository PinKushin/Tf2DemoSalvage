using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// On a real match, an unusual hat's effect is offered to the particle system and its creation takes the hat off the
/// fast-cull origin (B69 g, h): output level, through the production pass.
/// </summary>
/// <remarks>
/// **Found with the `econ-effects` probe**: on `serveme-627619-stv-2026-08-07`, entity 578 is a "Mann of the Hour" on
/// player 2 carrying `unusual_deadlyaroma_teamcolor_red`, shown in 93 of 109 ten-second samples; the same probe's World
/// Traveler (entity 668) is never shown, as the donation rule says, which is the instrument's control. The scene, the
/// burst presenter and the model set are the viewer's, wired as `FramePresenter` wires them; the systems are the
/// install's own manifest.
/// </remarks>
public sealed class CorpusEconParticleTests
{
    private const string Demo = "serveme-627619-stv-2026-08-07";
    private const int Hat = 578;
    private const int Tick = 661;
    private const string Unusual = "unusual_deadlyaroma_teamcolor_red";

    [Test]
    public void Pose_AnUnusualHatOnAMatch_IsOfferedAndLeavesTheFastCullOriginTheSameFrame()
    {
        string path = Corpus.Demo(Demo);
        string root = GameInstall.Require();
        DemoTimeline timeline = TimelineCache.For(path);
        GameContent game = GameContent.Open(root, NullLoggerFactory.Instance);
        IReadOnlyDictionary<string, ParticleSystem> systems = ParticleManifest.Layer(
            new Dictionary<string, ParticleSystem>(), ParticleManifest.Files(game.Archives.Read), game.Archives.Read, _ => { });

        EntityModelSet models = new();
        MomentScene scene = new(models, new ViewmodelScene(), NullLogger.Instance) { Weapons = game.Weapons };
        BurstEffects bursts = new(new ParticleEffects(), models, new BulletStrikes(models, NullLogger.Instance), NullLogger.Instance);
        scene.FastCullClearedAt = bursts.FastCullClearedAt;

        // Wired as `FramePresenter` wires it: decided at the start of the pose, the landing told once (B69 g).
        bool landed = true;
        scene.DecideEconParticles = moment =>
        {
            bursts.DecideEconParticles(scene.EconItems, moment.CurrentTick, landed, systems.ContainsKey);
            landed = false;
        };

        MomentInfo info = Build(scene, timeline, Tick);
        SceneProp before = scene.Drawn.Single(prop => prop.EntityIndex == Hat);
        before.BoneMergeFastCull.ShouldBeTrue("the control: a wearable fast-culls before any effect is made");
        before.BoneMerged.ShouldBeTrue();

        scene.Pose(info);

        bursts.FastCullClearedAt(Hat).ShouldBe(Tick);
        scene.Drawn.Single(prop => prop.EntityIndex == Hat).BoneMergeFastCull
            .ShouldBeFalse("GetAimEntOrigin's aligned branch, in the frame that made the effect");

        bursts.DrawEconParticles(Tick, systems);

        // The key's low eleven bits are the item's entity (BurstEffects.DrawEconParticles). Other items carry their own
        // static effects at this tick — two drg_pipe_smoke — so the hat's is selected by its entity, not by being alone.
        ParticleBurst offered = bursts.Bursts.Where(burst => (burst.Key & 0x7FF) == Hat)
            .ShouldHaveSingleItem("the hat's one attached effect");
        offered.Definition.Name.ShouldBe(Unusual);
        bursts.Bursts.Where(burst => burst.Definition.Name == Unusual).ShouldHaveSingleItem("no other item carries it");

        scene.Pose(Build(scene, timeline, Tick + 1));
        scene.Drawn.Single(prop => prop.EntityIndex == Hat).BoneMergeFastCull
            .ShouldBeFalse("still cleared the next frame: no m_fEffects has arrived since");

        // No restoring control here: this hat never receives `m_fEffects` again in the demo, so the bit coming back on a
        // later receive is held by the synthetic EconParticleRuns.FastCull tests instead.
    }

    /// <summary>
    /// **An unusual whose item asks for its head origin stands on its head bone, raised by its offset** (B69 review K): the
    /// attribute census found <c>particle_effect_use_head_origin</c> (520) networked in 31 of 146 demos, this hat among
    /// them; <c>UpdateControlPoint</c> then places control point 0 on <c>bip_head</c> / <c>prp_helmet</c> / <c>prp_hat</c> /
    /// bone 0 plus <c>particle_effect_vertical_offset</c> in height (particle_property.cpp:558-593, :661-665), not on its
    /// attachment. Output level: the burst offered after the viewer's pass, with the map's models posed. The control: the
    /// item decides the attribute on, and its head bone is not where its abs origin stands. **The reference is independent**
    /// (B69 review 1): the wearer's own <c>head</c> attachment and his feet, not the production <c>HeadBone</c>.
    /// </summary>
    [Test]
    public void DrawEconParticles_AHeadOriginUnusualOnAMatch_StandsOnItsHeadBone()
    {
        string path = Corpus.Demo(Demo);
        string root = GameInstall.Require();
        byte[] bytes = System.IO.File.ReadAllBytes(path);
        string mapName = Tf2DemoSalvage.Core.Container.DemoHeader.Parse(bytes).MapName;
        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);

        if (locator.Find(mapName) is not { } map)
        {
            Assert.Ignore($"{mapName} is not installed, so no model can be posed");
            return;
        }

        DemoTimeline timeline = TimelineCache.For(path);
        GameContent game = GameContent.Open(root, NullLoggerFactory.Instance);
        IReadOnlyDictionary<string, ParticleSystem> systems = ParticleManifest.Layer(
            new Dictionary<string, ParticleSystem>(), ParticleManifest.Files(game.Archives.Read), game.Archives.Read, _ => { });

        // With the models the demo shows and the worn ones skinned, as `LoadedMap` loads them: without the lists the map's
        // `Geometry` answers no entity model, so nothing packs, nothing draws, and no bone is ever set up.
        MapAssets assets = MapAssets.Load(
            System.IO.File.ReadAllBytes(map), game.Archives, 64, DemoModels.Needed(timeline, game), DemoModels.Worn(timeline, game),
            mapName: map);

        EntityModelSet models = new() { Geometry = assets.Geometry };
        MomentScene scene = new(models, new ViewmodelScene(), NullLogger.Instance)
        {
            Weapons = game.Weapons,
            Appearance = DemoAppearance.Ensure(DemoAppearance.None, timeline, game, NullLogger.Instance),
        };
        BurstEffects bursts = new(new ParticleEffects(), models, new BulletStrikes(models, NullLogger.Instance), NullLogger.Instance);
        scene.FastCullClearedAt = bursts.FastCullClearedAt;
        scene.DecideEconParticles = moment => bursts.DecideEconParticles(scene.EconItems, moment.CurrentTick, true, systems.ContainsKey);

        // `GetBone` reads the bones as the frame's draw last set them up (particle_property.cpp:584): the pose's world pass
        // is that draw, and `Build` packed the models it reads.
        scene.Pose(Build(scene, timeline, Tick));
        scene.Instances.ShouldContain(instance => instance.EntityIndex == Hat, "the control: the hat is drawn, so set up");

        EconParticleItem hat = scene.EconItems.Single(item => item.Entity == Hat);
        hat.HeadOrigin.ShouldBeTrue("the control: the demo's attribute 520 decides it on");
        ParticleControlPoint head = models.HeadBone(Hat).ShouldNotBeNull("the hat was posed");
        ParticleControlPoint origin = models.AbsOrigin(Hat).ShouldNotBeNull();
        System.Numerics.Vector3.Distance(head.At, origin.At).ShouldBeGreaterThan(1f, "the control: the head is not the abs origin");

        // **An independent reference** (B69 review 1): HeadBone is the production route, so comparing the burst with it alone
        // is circular. The wearer's own `head` attachment comes from HIS bone setup and his model's attachment, never the
        // hat's merged skeleton; his feet are his abs origin.
        int owner = scene.Drawn.Single(prop => prop.EntityIndex == Hat).OwnedBy.ShouldNotBeNull();
        ParticleControlPoint wearerHead = models.AttachmentPoint(owner, "head").ShouldNotBeNull("the wearer's head attachment");
        ParticleControlPoint feet = models.AbsOrigin(owner).ShouldNotBeNull();

        bursts.DrawEconParticles(Tick, systems);

        ParticleBurst offered = bursts.Bursts.Where(burst => (burst.Key & 0x7FF) == Hat).ShouldHaveSingleItem();
        offered.At.At.ShouldBe(head.At + new System.Numerics.Vector3(0f, 0f, hat.VerticalOffset));

        // Measured 2026-11-20: the head bone 8.5 units from his `head` attachment and 61.8 above his feet. The bounds are the
        // model's and the engine's: the attachment rides `bip_head` a few units out, and a standing hull is 0..82 with its
        // centre at 41 (VEC_HULL, tf_gamerules.cpp:1313-1317) — a head below the centre or above the top is not his.
        System.Numerics.Vector3 burst = offered.At.At - new System.Numerics.Vector3(0f, 0f, hat.VerticalOffset);
        System.Numerics.Vector3.Distance(burst, wearerHead.At).ShouldBeLessThan(12f, "on the wearer's head, by his own attachment");
        (burst.Z - feet.At.Z).ShouldBeInRange(41f, 82f, "between his hull's centre and its top");
    }

    /// <summary>
    /// **The demo names a weapon's extra wearable, and the timeline keeps it** (B69 part 4): every banner
    /// (<c>CTFBuffItem</c>) on a real match that is not a disguise's carries a live <c>m_hExtraWearable</c> — the backpack its
    /// effects would hang from. Measured: eleven banner tracks on this demo, nine entering with a handle (entity-census), the
    /// two without being a spy's disguise weapons. The handle must name an entity whose track holds that serial.
    /// </summary>
    [Test]
    public void Props_EveryBannerOnAMatch_NamesItsExtraWearable()
    {
        DemoTimeline timeline = TimelineCache.For(Corpus.Demo("demostf-cp_steel_f12-1491261"));

        // Not a disguise's: a spy's borrowed banner is a weapon of its own with no backpack (two on this match).
        List<ScenePropTrack> banners = [.. timeline.Props.Where(track => track.ClassName == "CTFBuffItem" && !track.OfDisguise)];
        banners.ShouldNotBeEmpty("the control: the match has banners");

        List<string> unnamed = [];

        foreach (ScenePropTrack banner in banners)
        {
            if (banner.FactsHistory.Select(stamp => stamp.Facts.ExtraWearable).FirstOrDefault(handle => handle is not null)
                is not var (entity, serial))
            {
                unnamed.Add($"{banner.EntityIndex}/{banner.SerialNumber} {banner.ItemDefinitionIndex} {banner.FirstTick}..{banner.EndTick} stamps {banner.FactsHistory.Count()} now {banner.ExtraWearable}");
                continue;
            }

            timeline.TracksFor(entity).ShouldContain(track => track.SerialNumber == serial, $"{entity}/{serial} is an entity");
        }

        unnamed.ShouldBeEmpty($"of {banners.Count} banners");
    }

    private static MomentInfo Build(MomentScene scene, DemoTimeline timeline, int tick)
    {
        List<ScenePlayer> players = [];
        List<SceneProp> props = [];
        timeline.PlayersAt(tick, players);
        timeline.PropsAt(tick, props);

        MomentInfo info = new(
            Tick: tick,
            CurrentTick: tick,
            FirstPerson: false,
            Followed: null,
            EyeCamera: null,
            IntervalPerTick: timeline.IntervalPerTick,
            ViewmodelFieldOfView: 54f) { Recorder = timeline.RecorderEntityIndex };

        scene.Build(players, [.. props], info);

        return info;
    }
}
