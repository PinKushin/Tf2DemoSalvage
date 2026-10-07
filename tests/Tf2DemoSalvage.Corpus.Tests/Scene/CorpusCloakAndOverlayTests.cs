using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;

// Namespaced away from `Tf2DemoSalvage.Corpus.Tests.*`, as CorpusHeldWeaponWorldModelTests explains.
namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// On real recordings: the recorder's screen overlay as the scene hands it to the device, and a SourceTV spy's cloak as
/// the invisibility proxies bind it. Moments from the probe `cloak` (2026-10-06).
/// </summary>
public sealed class CorpusCloakAndOverlayTests
{
    /// <remarks>
    /// rgl-pug-2026-08-10-pov (lcor): the recorder, a RED demoman, is übercharged from 95906 to 96113 — the overlay the
    /// scene draws through his eyes is RED's invulnerability material, and none once it ends. The control is his
    /// conditions at the same tick.
    /// </remarks>
    [Test]
    public void Build_TheUberedRecordersEyes_DrawTheRedInvulnOverlay()
    {
        DemoTimeline timeline = TimelineCache.For(Corpus.Demo("rgl-pug-2026-08-10-pov"));
        int recorder = timeline.RecorderEntityIndex ?? -1;
        MomentScene scene = new(new EntityModelSet(), new ViewmodelScene(), NullLogger.Instance);
        List<ScenePlayer> players = [];
        List<SceneProp> props = [];

        timeline.PlayersAt(95960, players);
        timeline.PropsAt(95960, props);

        ScenePlayer self = players.Single(player => player.EntityIndex == recorder);

        (self.Conditions.IsInvulnerable || self.Conditions.Has(28)).ShouldBeTrue("the control: an uber is on him");
        self.Team.ShouldBe(2);

        scene.Build(players, props, Info(95960, recorder, firstPerson: true, timeline.IntervalPerTick));
        scene.ScreenOverlay.ShouldBe(ScreenOverlay.InvulnRed);

        scene.Build(players, props, Info(95960, recorder, firstPerson: false, timeline.IntervalPerTick));
        scene.ScreenOverlay.ShouldBeNull("not his view, not his overlay");

        timeline.PlayersAt(96200, players);
        timeline.PropsAt(96200, props);
        scene.Build(players, props, Info(96200, recorder, firstPerson: true, timeline.IntervalPerTick));
        scene.ScreenOverlay.ShouldBeNull();
    }

    /// <remarks>
    /// serveme-627619-stv-2026-08-07 (lcor): spy 7 (RED) cloaks from 63693 and is fully stealthed by 63765. A SourceTV
    /// viewer is on no team, so `GetEffectiveInvisibilityLevel` caps him at 0.95 — drawn as the cloak pass alone, never
    /// skipped — and half way in both proxies give his percent.
    /// </remarks>
    [Test]
    public void CloakOf_ASourceTvSpyCloaking_RisesToTheTeammateCap()
    {
        const int SpyEntity = 7;
        DemoTimeline timeline = TimelineCache.For(Corpus.Demo("serveme-627619-stv-2026-08-07"));
        TimelineMoments moments = new(timeline);
        List<ScenePlayer> players = [];
        SceneProp body = new(SpyEntity, "models/player/spy.mdl", SceneModelKind.Studio, new ScenePose());

        timeline.PlayersAt(63730, players);
        players.Single(player => player.EntityIndex == SpyEntity).PlayerClass.ShouldBe(8, "the control: a spy");

        CloakBind half = MomentScene.CloakOf(body, players, timeline.RecorderEntityIndex, moments.ServerTimeAt(63730), null);

        half.Cloaking.ShouldBeTrue($"{half}");
        half.SpyInvis.ShouldBe(half.Invis);
        half.SpyInvis.ShouldBeInRange(0.3f, 0.8f);
        half.PlayerTint.ShouldBe(CloakPass.TeamTint(2));

        timeline.PlayersAt(63800, players);

        CloakBind full = MomentScene.CloakOf(body, players, timeline.RecorderEntityIndex, moments.ServerTimeAt(63800), null);

        full.ShouldBe(new CloakBind(0.95f, 0.95f, CloakPass.TeamTint(2), Undrawn: false));
    }

    /// <remarks>
    /// demostf-pl_upward_f12-1491354 (lcor), probe `cloak`: spy 15 is stealthed on an empty meter at full speed
    /// (320 of 320) from 55003, carrying the Cloak and Dagger (60). Through the scene's own wiring — `Build` with the
    /// install's `items_game.txt` — `InvisibilityThink`'s motion branch gives RemapVal( 320², 0, 320², 1, 0.5 ) = 0.5,
    /// under the SourceTV cap. The control is the same moment with no schema, where the watch reads as ordinary: 0.95.
    /// </remarks>
    [Test]
    public void Build_ACloakAndDaggerSpyRunningOnAnEmptyMeter_FadesBySpeed()
    {
        const int SpyEntity = 15;
        const int Tick = 55005;
        DemoTimeline timeline = TimelineCache.For(Corpus.Demo("demostf-pl_upward_f12-1491354"));
        TimelineMoments moments = new(timeline);
        GameContent content = GameContent.Open(SdkReference.GameInstall.Require(), NullLoggerFactory.Instance);
        List<ScenePlayer> players = [];
        List<SceneProp> props = [];

        timeline.PlayersAt(Tick, players);
        timeline.PropsAt(Tick, props);

        ScenePlayer spy = players.Single(player => player.EntityIndex == SpyEntity);

        spy.Items!.Single(item => item.ClassName == "CTFWeaponInvis").DefinitionIndex.ShouldBe(60, "the control: the Cloak and Dagger");

        SceneProp body = new(SpyEntity, "models/player/spy.mdl", SceneModelKind.Studio, new ScenePose());
        MomentInfo info = Info(Tick, timeline.RecorderEntityIndex ?? -1, firstPerson: false, timeline.IntervalPerTick) with
        {
            Recorder = timeline.RecorderEntityIndex,
            ServerTime = moments.ServerTimeAt(Tick),
        };

        EntityModelSet models = new();
        MomentScene scene = new(models, new ViewmodelScene(), NullLogger.Instance) { Weapons = content.Weapons };

        scene.Build(players, props, info);
        models.Cloak!(body).SpyInvis.ShouldBe(0.5f, 0.02f);

        EntityModelSet bare = new();

        new MomentScene(bare, new ViewmodelScene(), NullLogger.Instance).Build(players, props, info);
        bare.Cloak!(body).SpyInvis.ShouldBe(0.95f, "no schema: an ordinary watch, capped for SourceTV");
    }

    private static MomentInfo Info(int tick, int recorder, bool firstPerson, float interval) =>
        new(
            Tick: tick,
            CurrentTick: tick,
            FirstPerson: firstPerson,
            Followed: recorder,
            EyeCamera: null,
            IntervalPerTick: interval,
            ViewmodelFieldOfView: 54f,
            Recorder: recorder);
}
