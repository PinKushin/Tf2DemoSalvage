using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Tests;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// The sounds the client makes each frame, as they left the view (D214): `AE_CL_PLAYSOUND` from attachment 1
/// (`C_BaseAnimating::FireEvent`, `c_baseanimating.cpp:3988`), TF2's footstep (`C_TFPlayer::FireEvent`,
/// `c_tf_player.cpp:9066`) and a corpse's impacts and scrapes (`CPhysicsSystem::PhysicsSimulate`,
/// `physicssound::PlayImpactSounds`).
/// </summary>
/// <remarks>
/// **What each sound is has its own suites** (`FootstepsConformanceTests`, `PhysicsImpactSoundsConformanceTests`,
/// `PhysicsFrictionSoundsConformanceTests`); asserted here is which events and reports a frame turns into sounds, from
/// where, and that each reaches <see cref="SoundPresenter.Emit"/>.
///
/// **Not here:** the recorder's predicted landing, which needs a prediction that has run a fall; and a footstep on
/// ground a trace found, which needs a map with a floor and its surface properties.
/// </remarks>
public sealed class SoundPresenterClientSoundsTests
{
    private const int Tick = 100;
    private const int Corpse = 40;
    private const int Walker = 5;
    private const int OnGround = 1 << 0;

    private static readonly MapLevel Empty = new(
        null, null, null, new Dictionary<int, string>(), null, null, null, [], [], [], [], null, new VertexNormals([], []));

    private static readonly VphysicsSurfaceProps Surfaces = Parse(
        "\"default\" { \"impacthard\" \"Stone.ImpactHard\" \"impactsoft\" \"Stone.ImpactSoft\" \"audiohardnessfactor\" \"1\" \"impacthardthreshold\" \"0.5\" " +
        "\"scraperough\" \"Stone.ScrapeRough\" \"scrapesmooth\" \"Stone.ScrapeSmooth\" \"audioroughnessfactor\" \"1\" \"scraperoughthreshold\" \"0.5\" } " +
        "\"flesh\" { \"impacthard\" \"Flesh.ImpactHard\" \"impactsoft\" \"Flesh.ImpactSoft\" \"audiohardnessfactor\" \"0.2\" \"impacthardthreshold\" \"0.5\" " +
        "\"scraperough\" \"Flesh.ScrapeRough\" \"scrapesmooth\" \"Flesh.ScrapeSmooth\" \"audioroughnessfactor\" \"0.1\" } " +
        "\"water\" { \"stepleft\" \"Water.StepLeft\" \"stepright\" \"Water.StepRight\" }");

    private static readonly (string Name, string Wave)[] Sounds =
    [
        ("Test.Reload", "weapons/reload.wav"),
        ("Flesh.ImpactHard", "physics/flesh/hard.wav"),
        ("Flesh.ImpactSoft", "physics/flesh/soft.wav"),
        ("Stone.ImpactHard", "physics/stone/hard.wav"),
        ("Stone.ImpactSoft", "physics/stone/soft.wav"),
        ("Flesh.ScrapeRough", "physics/flesh/rough.wav"),
        ("Flesh.ScrapeSmooth", "physics/flesh/smooth.wav"),
        ("Stone.ScrapeRough", "physics/stone/rough.wav"),
        ("Stone.ScrapeSmooth", "physics/stone/smooth.wav"),
        ("Water.StepLeft", "player/footsteps/slosh_left.wav"),
        ("Water.StepRight", "player/footsteps/slosh_right.wav"),
    ];

    private static VphysicsSurfaceProps Parse(string text)
    {
        VphysicsSurfaceProps props = new([]);

        props.ParseSurfaceData(Encoding.Latin1.GetBytes(text));

        return props;
    }

    private static SoundPresenter Presenter(bool scripts = true)
    {
        SoundPresenter presenter = new(
            new SoundscapeSystem(new ActiveLoops(), static _ => null, NullLogger.Instance),
            new ActiveLoops(),
            static _ => null,
            NullLogger.Instance);

        if (scripts)
        {
            byte[] manifest = Encoding.UTF8.GetBytes("\"game_sounds_manifest\"\n{\n\t\"precache_file\" \"scripts/test.txt\"\n}\n");
            byte[] script = Encoding.UTF8.GetBytes(string.Concat(Sounds.Select(
                static sound => $"\"{sound.Name}\"\n{{\n\t\"channel\" \"CHAN_STATIC\"\n\t\"volume\" \"0.8\"\n\t\"wave\" \"{sound.Wave}\"\n}}\n")));

            presenter.Scripts = SoundScriptCatalog.Load(path => path switch
            {
                "scripts/game_sounds_manifest.txt" => manifest,
                "scripts/test.txt" => script,
                _ => null,
            });

            presenter.Scripts.Entries.Count.ShouldBe(Sounds.Length, "the control: the catalog holds every script this file names");
        }

        return presenter;
    }

    private static ClientSoundWorld World(Dictionary<int, Vector3>? roots = null) =>
        new(Empty, null, SolidBrushEntities.None, Surfaces, Surfaces, roots ?? []);

    private static ClientSoundFrame Frame(int tick = Tick, bool silent = false, IMomentSource? source = null, double seconds = 0.015d) =>
        new(null, null, null, tick, Vector3.Zero, source, tick + 0.5d, true, silent, seconds);

    private static FiredAnimationEvent PlaySound(int entity = 9) =>
        new(entity, new StudioEvent(0.5f, SoundPresenter.PlaySoundEvent, StudioEvent.ClientType, "Test.Reload"), (10f, 20f, 30f));

    private static FiredAnimationEvent Step(int entity = Walker) =>
        new(entity, new StudioEvent(0.5f, SoundPresenter.FootstepEvent, StudioEvent.ClientType, "left"), (0f, 0f, 0f));

    [Test]
    public void StepClientSoundsOver_APlaySoundEvent_EmitsItsScriptFromAttachmentOne()
    {
        SoundPresenter presenter = Presenter();
        FakePosedEntities models = new() { Fired = { PlaySound() } };

        models.Numbered[(9, 1)] = ParticleControlPoint.Unoriented(new Vector3(1f, 2f, 3f));
        presenter.StepClientSoundsOver(Frame(), World(), models);

        SceneSound sound = presenter.Emitted.ShouldHaveSingleItem();

        sound.Name.ShouldBe("weapons/reload.wav");
        sound.Tick.ShouldBe(Tick);
        sound.EntityIndex.ShouldBe(9);
        (sound.OriginX, sound.OriginY, sound.OriginZ).ShouldBe((1f, 2f, 3f));
    }

    [Test]
    public void StepClientSoundsOver_APlaySoundEventOnAModelWithNoAttachment_EmitsFromTheEventsOrigin()
    {
        SoundPresenter presenter = Presenter();
        FakePosedEntities models = new() { Fired = { PlaySound() } };

        presenter.StepClientSoundsOver(Frame(), World(), models);

        SceneSound sound = presenter.Emitted.ShouldHaveSingleItem();

        (sound.OriginX, sound.OriginY, sound.OriginZ).ShouldBe((10f, 20f, 30f));
    }

    [Test]
    public void StepClientSoundsOver_APassNobodyHears_TakesItsEventsAndNeverSoundsThem()
    {
        SoundPresenter presenter = Presenter();
        FakePosedEntities models = new() { Fired = { PlaySound() } };

        presenter.StepClientSoundsOver(Frame(silent: true), World(), models);

        models.Fired.ShouldBeEmpty("taken before any early return: an event is crossed once");
        presenter.Emitted.ShouldBeEmpty();

        presenter.StepClientSoundsOver(Frame(Tick + 1), World(), models);

        presenter.Emitted.ShouldBeEmpty("dropped, never sounded later");
    }

    /// <summary>The shown moment's players: one walker, and what the presenter asked with.</summary>
    private sealed class Moment(ScenePlayer walker) : IMomentSource
    {
        public (double Tick, bool Interpolating)? Asked { get; private set; }

        public float IntervalPerTick => 0.015f;

        public int? Recorder => null;

        public void PlayersAt(double tick, ICollection<ScenePlayer> into, bool interpolating = true)
        {
            Asked = (tick, interpolating);
            into.Add(walker);
        }

        public void PropsAt(double tick, ICollection<SceneProp> into, int? viewEntity = null, bool interpolating = true)
        {
        }

        public int? RoundStateAt(double tick) => null;

        public void OnNewModel(int entityIndex, IReadOnlyList<bool> looping, bool staticProp)
        {
        }
    }

    private static ScenePlayer Walking(int waterLevel) =>
        new(Walker, 7f, 8f, 9f, Team: 2, Health: 125, PlayerClass: 1, Speed: 350f, Flags: OnGround, WaterLevel: waterLevel, MaxSpeed: 400f);

    [Test]
    public void StepClientSoundsOver_AFootstepWithFeetInWater_IsTheWaterSurfacesStepForThePlayerAsShown()
    {
        SoundPresenter presenter = Presenter();
        FakePosedEntities models = new() { Fired = { Step() } };
        Moment moment = new(Walking(waterLevel: 1));
        DemoTimeline timeline = SyntheticEffects.TimelineWrittenWith(PlayerFlagLayout.Current);

        presenter.StepClientSoundsOver(Frame(source: moment) with { Timeline = timeline }, World(), models);

        SceneSound sound = presenter.Emitted.ShouldHaveSingleItem();

        sound.Name.ShouldStartWith("player/footsteps/slosh_");
        sound.EntityIndex.ShouldBe(Walker);
        (sound.OriginX, sound.OriginY, sound.OriginZ).ShouldBe((7f, 8f, 9f));
        moment.Asked.ShouldBe((Tick + 0.5d, true), "the shown tick, interpolated while playing");
    }

    [Test]
    public void StepClientSoundsOver_AFootstepOnAnotherEntityThanTheWalker_IsSilent()
    {
        SoundPresenter presenter = Presenter();
        FakePosedEntities models = new() { Fired = { Step(entity: Walker + 1) } };
        DemoTimeline timeline = SyntheticEffects.TimelineWrittenWith(PlayerFlagLayout.Current);

        presenter.StepClientSoundsOver(Frame(source: new Moment(Walking(waterLevel: 1))) with { Timeline = timeline }, World(), models);

        presenter.Emitted.ShouldBeEmpty("no player at the shown moment carries the event's entity");
    }

    [Test]
    public void StepClientSoundsOver_AFootstepOverNoGround_IsSilent()
    {
        SoundPresenter presenter = Presenter();
        FakePosedEntities models = new() { Fired = { Step() } };
        DemoTimeline timeline = SyntheticEffects.TimelineWrittenWith(PlayerFlagLayout.Current);

        presenter.StepClientSoundsOver(Frame(source: new Moment(Walking(waterLevel: 0))) with { Timeline = timeline }, World(), models);

        presenter.Emitted.ShouldBeEmpty("GetGroundSurface found nothing within 64 units");
    }

    private static PhysicsImpactSound Impact(float volume, Vector3 at) =>
        new(Surfaces.GetSurfaceIndex("flesh"), Surfaces.GetSurfaceIndex("default"), volume, 40000f, at);

    [Test]
    public void StepClientSoundsOver_TwoCorpseImpactsInOneFramesReach_AreMergedIntoOneSoundAndPlayedOnce()
    {
        SoundPresenter presenter = Presenter();
        FakePosedEntities models = new();
        PhysicsImpactSound first = Impact(0.2f, new Vector3(1f, 2f, 3f));
        PhysicsImpactSound second = Impact(0.3f, new Vector3(4f, 5f, 6f));

        presenter.CorpseImpactHeard(Tick - 1, first);
        presenter.CorpseImpactHeard(Tick, second);
        presenter.StepClientSoundsOver(Frame(), World(), models);

        List<PhysicsImpactSound> merged = [];

        PhysicsImpactSoundList.Add(merged, first);
        PhysicsImpactSoundList.Add(merged, second);

        SceneSound expected = PhysicsImpactSounds.For(Tick, merged.ShouldHaveSingleItem(), Surfaces, presenter.Scripts.ShouldNotBeNull().Entries).ShouldNotBeNull();

        presenter.Emitted.ShouldHaveSingleItem().ShouldBe(expected, "m_impactSounds is one list for the frame");

        presenter.StepClientSoundsOver(Frame(Tick + 1), World(), models);

        presenter.Emitted.Count.ShouldBe(1, "consumed");
    }

    [TestCase(CorpsePhysics.HearingCatchUp - 1, 1, TestName = "StepClientSoundsOver_ACorpseImpactFifteenTicksOld_IsPlayed")]
    [TestCase(CorpsePhysics.HearingCatchUp, 0, TestName = "StepClientSoundsOver_ACorpseImpactOlderThanTheCatchUp_IsNotPlayed")]
    [TestCase(-1, 0, TestName = "StepClientSoundsOver_ACorpseImpactAheadOfTheFrame_IsNotPlayed")]
    public void StepClientSoundsOver_ACorpseImpactAtAnAge_IsPlayedOnlyInsideTheCatchUp(int ticksOld, int expected)
    {
        SoundPresenter presenter = Presenter();

        presenter.CorpseImpactHeard(Tick - ticksOld, Impact(0.5f, Vector3.Zero));
        presenter.StepClientSoundsOver(Frame(), World(), new FakePosedEntities());

        presenter.Emitted.Count.ShouldBe(expected);
    }

    [Test]
    public void ForgetCorpseSounds_WithImpactsAndScrapesHeard_PlaysNeither()
    {
        SoundPresenter presenter = Presenter();

        presenter.CorpseImpactHeard(Tick, Impact(0.5f, Vector3.Zero));
        presenter.CorpseFrictionHeard(Tick, new CorpseFriction(Corpse, 7750f, Surfaces.GetSurfaceIndex("flesh"), Surfaces.GetSurfaceIndex("default")));
        presenter.ForgetCorpseSounds();
        presenter.StepClientSoundsOver(Frame(), World(), new FakePosedEntities());

        presenter.Emitted.ShouldBeEmpty();
    }

    [Test]
    public void StepClientSoundsOver_ACorpseScrape_StartsItsLoopAtTheCorpsesRoot()
    {
        SoundPresenter presenter = Presenter();

        presenter.CorpseFrictionHeard(Tick, new CorpseFriction(Corpse, 7750f, Surfaces.GetSurfaceIndex("flesh"), Surfaces.GetSurfaceIndex("default")));
        presenter.StepClientSoundsOver(Frame(), World(new Dictionary<int, Vector3> { [Corpse] = new(1f, 2f, 3f) }), new FakePosedEntities());

        SceneSound loop = presenter.Emitted.ShouldHaveSingleItem();

        loop.Name.ShouldBe("physics/flesh/rough.wav");
        loop.EntityIndex.ShouldBe(Corpse);
        loop.IsStop.ShouldBeFalse();
        (loop.OriginX, loop.OriginY, loop.OriginZ).ShouldBe((1f, 2f, 3f));
    }

    [Test]
    public void StepClientSoundsOver_AStepBackwards_ForgetsTheScrapeLoopsSoTheSameScrapeStartsAgain()
    {
        SoundPresenter presenter = Presenter();
        CorpseFriction scrape = new(Corpse, 7750f, Surfaces.GetSurfaceIndex("flesh"), Surfaces.GetSurfaceIndex("default"));

        presenter.CorpseFrictionHeard(Tick, scrape);
        presenter.StepClientSoundsOver(Frame(), World(), new FakePosedEntities());
        presenter.CorpseFrictionHeard(Tick - 10, scrape);
        presenter.StepClientSoundsOver(Frame(Tick - 10), World(), new FakePosedEntities());

        presenter.Emitted.Count(static sound => !sound.IsStop).ShouldBe(2, "a seek back forgets the loop, and the scrape is new again");
    }

    [Test]
    public void StepClientSoundsOver_WithNoScripts_StillTakesTheEventsAndTheCorpsesReports()
    {
        SoundPresenter presenter = Presenter(scripts: false);
        FakePosedEntities models = new() { Fired = { PlaySound() } };

        presenter.CorpseImpactHeard(Tick, Impact(0.5f, Vector3.Zero));
        presenter.StepClientSoundsOver(Frame(), World(), models);

        models.Fired.ShouldBeEmpty();

        presenter.Scripts = Presenter().Scripts;
        presenter.StepClientSoundsOver(Frame(Tick + 1), World(), models);

        presenter.Emitted.ShouldBeEmpty("the impact was consumed by the frame that could not sound it");
    }
}
