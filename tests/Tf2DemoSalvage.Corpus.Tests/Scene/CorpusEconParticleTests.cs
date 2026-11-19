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
    public void AddEconParticles_AnUnusualHatOnAMatch_IsOfferedAndLeavesTheFastCullOrigin()
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

        Build(scene, timeline, Tick);
        SceneProp before = scene.Drawn.Single(prop => prop.EntityIndex == Hat);
        before.BoneMergeFastCull.ShouldBeTrue("the control: a wearable fast-culls before any effect is made");
        before.BoneMerged.ShouldBeTrue();

        bursts.AddEconParticles(scene.EconItems, Tick, seeked: true, systems);

        // The key's low eleven bits are the item's entity (BurstEffects.AddEconParticles). Other items carry their own
        // static effects at this tick — two drg_pipe_smoke — so the hat's is selected by its entity, not by being alone.
        ParticleBurst offered = bursts.Bursts.Where(burst => (burst.Key & 0x7FF) == Hat)
            .ShouldHaveSingleItem("the hat's one attached effect");
        offered.Definition.Name.ShouldBe(Unusual);
        bursts.Bursts.Where(burst => burst.Definition.Name == Unusual).ShouldHaveSingleItem("no other item carries it");
        bursts.FastCullClearedAt(Hat).ShouldBe(Tick);

        Build(scene, timeline, Tick + 1);
        scene.Drawn.Single(prop => prop.EntityIndex == Hat).BoneMergeFastCull
            .ShouldBeFalse("GetAimEntOrigin's aligned branch, not the wearer's centre");
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

    private static void Build(MomentScene scene, DemoTimeline timeline, int tick)
    {
        List<ScenePlayer> players = [];
        List<SceneProp> props = [];
        timeline.PlayersAt(tick, players);
        timeline.PropsAt(tick, props);

        scene.Build(
            players,
            [.. props],
            new MomentInfo(
                Tick: tick,
                CurrentTick: tick,
                FirstPerson: false,
                Followed: null,
                EyeCamera: null,
                IntervalPerTick: timeline.IntervalPerTick,
                ViewmodelFieldOfView: 54f) { Recorder = timeline.RecorderEntityIndex });
    }
}
