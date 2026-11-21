using System;
using System.Collections.Generic;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Primitives;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>A script's <c>rndwave</c> is dealt like a deck, one deck per entry for every client sound (B503).</summary>
/// <remarks>
/// **Disassembly, x64 <c>soundemittersystem.dll</c> (Ghidra project <c>tf2soundemitter</c>).** The wave pick,
/// FUN_180005680, walks the entry's 4-byte wave records (byte 2 the gender, byte 3 <c>available</c>) and lists, in
/// ascending order, the ones whose gender matches and whose <c>available</c> is set. When none is, it sets every
/// matching wave's <c>available</c> again and lists them all. It returns
/// <c>list[ RandomInt( 0, count - 1 ) ]</c>. Its one caller, <c>GetParametersForSound</c> (FUN_180003370), then
/// clears the chosen wave's <c>available</c> byte when its last argument — <c>isbeingemitted</c> — is set, which
/// <c>EmitSoundByHandle</c> passes (<c>SoundEmitterSystem.cpp:465</c>) and a plain <c>GetParametersForSound</c>
/// (footsteps, physics impacts, sound patches) does not. The flags live on the entry, so every emission of one
/// script name shares them, in the order the client makes them.
/// </remarks>
public sealed class ScriptWaveDeckConformanceTests
{
    private const string Script = "Test.Explode";

    [Test]
    public void Pick_FourEmittedDealsOfThreeWaves_DealEachWaveOnceBeforeAnyRepeats()
    {
        ScriptWaveDeck deck = new();

        int[] dealt = [deck.Pick(Script, 3, 0, emitted: true), deck.Pick(Script, 3, 0, emitted: true), deck.Pick(Script, 3, 0, emitted: true), deck.Pick(Script, 3, 2, emitted: true)];

        // The fourth finds nothing available, so every flag is set again and its draw of 2 picks from all three.
        dealt.ShouldBe([0, 1, 2, 2]);
    }

    [Test]
    public void Pick_ADrawAgainstTheAvailableWaves_IndexesThemInAscendingOrder()
    {
        ScriptWaveDeck deck = new();

        deck.Pick(Script, 3, 0, emitted: true).ShouldBe(0);

        // Available: waves 1 and 2, so RandomInt( 0, 1 ) answering 1 picks the list's second, wave 2.
        deck.Available(Script, 3).ShouldBe(2);
        deck.Pick(Script, 3, 1, emitted: true).ShouldBe(2);
    }

    [Test]
    public void Pick_ARead_NeverClearsTheWave()
    {
        ScriptWaveDeck deck = new();

        deck.Pick(Script, 3, 0, emitted: false).ShouldBe(0);
        deck.Pick(Script, 3, 0, emitted: false).ShouldBe(0);
        deck.Pick(Script, 3, 0, emitted: true).ShouldBe(0, "the reads left wave 0 available for the first deal");
    }

    [Test]
    public void Pick_ARead_SeesWhatAnEmittedDealCleared()
    {
        ScriptWaveDeck deck = new();

        deck.Pick(Script, 3, 0, emitted: true).ShouldBe(0);
        deck.Pick(Script, 3, 0, emitted: false).ShouldBe(1);
    }

    [Test]
    public void Pick_TwoEntries_KeepTheirOwnFlags()
    {
        ScriptWaveDeck deck = new();

        deck.Pick(Script, 3, 0, emitted: true).ShouldBe(0);
        deck.Pick("Test.Other", 3, 0, emitted: true).ShouldBe(0);
    }

    [Test]
    public void FromWorldAt_AnyEntry_CarriesItsScriptAndItsStreamFromWhereItsDrawsBegin()
    {
        SoundScriptEntry entry = Entry(channel:1, "a.wav", "b.wav", "c.wav");

        UniformRandomStream expected = new();
        expected.SetSeed(11);
        expected.RandomFloat(1f, 1f);
        expected.RandomFloat(100f, 100f);
        int wave = expected.RandomInt(0, 2);

        UniformRandomStream random = new();
        random.SetSeed(11);

        SceneSound sound = ExplosionSounds.FromWorldAt(entry, random, 1, (0f, 0f, 0f), emitted: true);

        (sound.WaveDraw!.Script, sound.WaveDraw.Emitted, sound.WaveDraw.After).ShouldBe((Script, true, null));
        sound.Name.ShouldBe(entry.Waves[wave], "before any deal, the wave is the one a full deck gives");

        sound.WaveDraw.Stream.ShouldBe(new RandomStreamPosition(11, 0), "the stream is carried from before the volume's draw");
    }

    /// <remarks>
    /// Every sound the client emits BY NAME goes through `EmitSoundByHandle`, which deals: `TFExplosionCallback`
    /// (`tf_fx_explosions.cpp:162`), `ImpactSound`'s `EmitSound( filter, …, pszSound, … )` (`fx_impact.cpp`), the tracer
    /// whiz (`fx_tracer.cpp`) and a HUD's `EmitSound` with `SOUND_FROM_LOCAL_PLAYER` (`tf_hud_deathnotice.cpp:751`).
    /// </remarks>
    [Test]
    public void Producers_EverySoundEmittedByName_Deals()
    {
        Dictionary<string, SoundScriptEntry> scripts = new(StringComparer.OrdinalIgnoreCase)
        {
            [Script] = Entry(channel: 0, "a.wav", "b.wav"),
            [TracerWhiz.Sound] = Entry(channel: 0, "a.wav", "b.wav") with { Name = TracerWhiz.Sound },
        };

        SceneSound blast = ExplosionSounds.For(
            [new SceneExplosion(5, 0f, 0f, 0f, (0f, 0f, 1f), 22, SceneExplosion.NoEntity, SceneExplosion.NoCustomParticle)],
            static _ => Script,
            scripts).ShouldHaveSingleItem();

        SceneSound impact = ImpactSounds.For(new BulletLanding(5, (0f, 0f, 0f), Script, Ricochets: false), seed: 1, scripts).ShouldHaveSingleItem();
        SceneSound? whiz = TracerWhiz.SoundAt(5, (0f, 0f, 0f), scripts, default);
        SceneSound? hud = HudSounds.Emit(5, Script, scripts);

        new[] { blast.WaveDraw, impact.WaveDraw, whiz?.WaveDraw, hud?.WaveDraw }.ShouldAllBe(draw => draw != null && draw.Emitted);
    }

    [Test]
    public void Emit_AnEntitysSoundAsked_DealsOnlyWhenEmitted()
    {
        Dictionary<string, SoundScriptEntry> scripts = new(StringComparer.OrdinalIgnoreCase) { [Script] = Entry(channel: 0, "a.wav", "b.wav") };

        EntitySounds.Emit(5, 3, Script, (0f, 0f, 0f), scripts, emitted: true, ClientSoundPhase.Simulate)!.Value.WaveDraw!.Emitted.ShouldBeTrue();
        EntitySounds.Emit(5, 3, Script, (0f, 0f, 0f), scripts, emitted: false, ClientSoundPhase.Simulate)!.Value.WaveDraw!.Emitted.ShouldBeFalse();
    }

    [Test]
    public void Update_ThreeScheduledDealsOfOneThreeWaveScript_PlayEachWaveOnce()
    {
        List<string> played = [];
        SoundPresenter presenter = Presenter(played, Entry(channel:0, "a.wav", "b.wav", "c.wav"));

        presenter.Schedule = new SoundSchedule([Dealt(10, 0), Dealt(20, 0), Dealt(30, 0)]);

        presenter.Update(new Silent(), 0, Listener, Right, now: 0d);
        presenter.Update(new Silent(), 40, Listener, Right, now: 0.6d);

        played.ShouldBe(["a.wav", "b.wav", "c.wav"]);
    }

    /// <remarks>
    /// B789. `EmitSoundByHandle` (`SoundEmitterSystem.cpp:465-468`): `if ( !GetParametersForSoundEx( … ) ) return;` — a
    /// script the level's catalog does not declare plays nothing, whatever its producer drew against another catalog.
    /// The control is the same sound with its script declared.
    /// </remarks>
    [TestCase(true, ExpectedResult = 1)]
    [TestCase(false, ExpectedResult = 0)]
    public int Update_AScriptSoundTheCatalogDeclaresOrNot_PlaysOnlyWhenDeclared(bool declared)
    {
        List<string> played = [];
        SoundScriptEntry entry = Entry(channel: 0, "a.wav");
        SoundPresenter presenter = Presenter(played, declared ? entry : entry with { Name = "Test.Elsewhere" });
        Silent sink = new();

        presenter.Schedule = new SoundSchedule([Dealt(10, 0)]);
        presenter.Update(sink, 0, Listener, Right, now: 0d);
        presenter.Update(sink, 20, Listener, Right, now: 0.3d);

        return sink.Started.Count;
    }

    [Test]
    public void Update_AnEmittedSoundBetweenTwoScheduledOnes_IsDealtInTickOrder()
    {
        List<string> played = [];
        SoundPresenter presenter = Presenter(played, Entry(channel:0, "a.wav", "b.wav", "c.wav"));

        presenter.Schedule = new SoundSchedule([Dealt(10, 0), Dealt(30, 0)]);

        Silent sink = new();

        presenter.Update(sink, 0, Listener, Right, now: 0d);
        presenter.Emit(Dealt(20, 0) with { EntityIndex = 9 });
        presenter.Update(sink, 40, Listener, Right, now: 0.6d);

        // Dealt 10, 20, 30 — the emitted one (entity 9) second. Scheduled first would have given it c, last.
        sink.Started.ShouldBe([(0, "a.wav"), (9, "b.wav"), (0, "c.wav")]);
    }

    [Test]
    public void Update_AStopOfADealtLoop_SilencesTheWaveItsStartPlayed()
    {
        List<string> played = [];
        SoundPresenter presenter = Presenter(played, Entry(channel:6, "a.wav", "b.wav"));
        Silent sink = new();

        // A patch (`CSoundPatch`), the only kind a stop names.
        SceneSound loop = Dealt(20, 0, emitted: false) with { EntityIndex = 5, Channel = 6 };
        loop = loop with { WaveDraw = loop.WaveDraw! with { Scaled = true, Kept = true } };

        presenter.Schedule = new SoundSchedule([Dealt(10, 0), loop, Dealt(25, 0), loop with { Tick = 30, IsStop = true }]);

        presenter.Update(sink, 0, Listener, Right, now: 0d);
        presenter.Update(sink, 40, Listener, Right, now: 0.6d);

        // The deal at 10 took a; the patch read the deck and found only b; the deal at 25 took b, emptying the deck — so
        // a fresh pick at 30 would reset it and find a. The stop names b, the wave its start is playing; it asks what is
        // held rather than opening, so it adds nothing here (finding 90).
        played.ShouldBe(["a.wav", "b.wav", "b.wav"]);
        sink.Silenced.ShouldBe(["b.wav"]);
    }

    /// <remarks>
    /// A seek back reloads, and the reload's `S_StopAllSounds` (B550) ends every patch playing, so the deck stops holding
    /// it for a stop that can no longer name anything.
    /// </remarks>
    [Test]
    public void Update_ASeekBackPastAPlayingPatch_ForgetsIt()
    {
        List<string> played = [];
        SoundPresenter presenter = Presenter(played, Entry(channel: 6, "a.wav", "b.wav"));
        SceneSound loop = Dealt(20, 0, emitted: false) with { EntityIndex = 5, Channel = 6 };
        presenter.Schedule = new SoundSchedule([loop with { WaveDraw = loop.WaveDraw! with { Scaled = true, Kept = true } }]);

        presenter.Update(new Silent(), 0, Listener, Right, now: 0d);
        presenter.Update(new Silent(), 25, Listener, Right, now: 0.4d);
        presenter.PatchesHeld.ShouldBe(1, "the condition: the patch was dealt");

        presenter.Update(new Silent(), 10, Listener, Right, now: 1d);
        presenter.PatchesHeld.ShouldBe(0);
    }

    /// <remarks>
    /// A reload's `S_StopAllSounds` (B550) silences the corpse scrapes with everything else, and the deck forgets their
    /// patches; the friction slots must go too, whichever way the reload lands. A slot left behind would send its next
    /// change against a patch nothing holds.
    /// </remarks>
    [Test]
    public void Update_AReloadLandingBehind_ForgetsTheScrapeLoops()
    {
        SoundPresenter presenter = PresenterWithAScrape();

        presenter.Update(new Silent(), 10, Listener, Right, now: 1d);
        presenter.FrictionSounds.Playing.ShouldBe(0);
    }

    /// <remarks>
    /// The case the review found: a walk that reloads and lands AHEAD of where playback was. Its frame's batch, read after
    /// the reload, dispatches with the reload's stop of everything (`PlaySkipFrames`), and the scrape slots must go then too —
    /// the step-back clear in the corpse pass never sees a step back here.
    /// </remarks>
    [Test]
    public void PlaySkipFrames_AReloadLandingAhead_ForgetsTheScrapeLoops()
    {
        SoundPresenter presenter = PresenterWithAScrape();

        presenter.SkipFrameRead(40, reload: true);
        presenter.PlaySkipFrames(new Silent(), Listener, Right);

        presenter.FrictionSounds.Playing.ShouldBe(0);
    }

    /// <remarks>
    /// A level change frees every friction slot: `CHLClient::LevelShutdown` releases the client entity list
    /// (`cdll_client_int.cpp:1734-1735`; that a ragdoll's `Release` reaches its destructor is inferred), and `C_TFRagdoll::~C_TFRagdoll`
    /// (`c_tf_player.cpp:590-593`) calls `PhysCleanupFrictionSounds` (`physics.cpp:1024-1031`) → `ShutdownFriction`
    /// (`:711-717`), which destroys the patch and empties the slot; `CSoundControllerImp::SystemReset`
    /// (`soundenvelope.cpp:862-878`) shuts down whatever patch is left.
    /// </remarks>
    [Test]
    public void Scripts_ALevelChange_ForgetsTheScrapeLoops()
    {
        SoundPresenter presenter = PresenterWithAScrape();

        SoundScriptCatalog? level = presenter.Scripts;
        presenter.Scripts = level;

        presenter.FrictionSounds.Playing.ShouldBe(0);
    }

    /// <remarks>
    /// A level change STOPS the scrape, it does not only forget it: `ShutdownFriction` → `SoundDestroy`
    /// (`physics.cpp:711-717`) and `SystemReset` → `Shutdown` (`soundenvelope.cpp:862-878`) end the patch's channel. The
    /// stop names its wave, as every stop does (`S_AlterChannel`, B416): the patch's own channel and nothing else on the
    /// key. The sink is only reached in a pass, so the stop lands at the next one, before anything else.
    /// </remarks>
    [Test]
    public void Scripts_ALevelChange_StopsAPlayingScrapeByNameAtTheNextPass()
    {
        (SoundPresenter presenter, Silent sink) = PlayingScrape();

        LevelChange(presenter);
        presenter.Update(sink, 27, Listener, Right, now: 0.43d);

        sink.Silenced.ShouldBe(["a.wav"]);
        sink.SilencedChannels.ShouldBeEmpty("the stop names its wave; it does not free the whole key");
    }

    [Test]
    public void PlaySkipFrames_AfterALevelChange_StopsThePatch()
    {
        (SoundPresenter presenter, Silent sink) = PlayingScrape();

        LevelChange(presenter);
        presenter.PlaySkipFrames(sink, Listener, Right);

        sink.Silenced.ShouldBe(["a.wav"]);
    }

    [Test]
    public void FollowListener_AfterALevelChange_StopsThePatch()
    {
        (SoundPresenter presenter, Silent sink) = PlayingScrape();

        LevelChange(presenter);
        presenter.FollowListener(sink, Listener, Right);

        sink.Silenced.ShouldBe(["a.wav"]);
    }

    /// <remarks>
    /// The stop belongs to the level change, which came before anything the next pass starts: a start on the same entity
    /// and channel after it is the new level's, and survives — the stop is issued first.
    /// </remarks>
    [Test]
    public void Update_AStartOnThePatchsChannelAfterALevelChange_IsNotStopped()
    {
        (SoundPresenter presenter, Silent sink) = PlayingScrape(out SceneSound scrape);
        sink.Events.Clear();

        LevelChange(presenter);
        presenter.Emit(scrape with { Tick = 27 });
        presenter.Update(sink, 27, Listener, Right, now: 0.43d);

        sink.Events.ShouldBe([$"stop 40/{scrape.Channel} a.wav", $"start 40/{scrape.Channel} a.wav"]);
    }

    /// <remarks>
    /// A patch still waiting for its wave at the level change is a channel too — the engine keeps a loading channel in its
    /// active set (finding 90) — so the stop frees it, and it never starts when its data arrives.
    /// </remarks>
    [Test]
    public void Scripts_ALevelChange_DropsAPatchStillLoading()
    {
        ScrapeWhileLoading(levelChange: false).ShouldContain(
            (40, "a.wav"), "the control: without the level change, the waiting scrape starts when its wave arrives");

        ScrapeWhileLoading(levelChange: true).ShouldNotContain((40, "a.wav"));
    }

    /// <summary>
    /// A scrape emitted while its wave is loading — waiting, not started — then, optionally, a level change, then the wave
    /// arriving and a pass; what the sink started.
    /// </summary>
    private static List<(int Entity, string Wave)> ScrapeWhileLoading(bool levelChange)
    {
        bool[] arrived = [false];
        SoundPresenter presenter = PresenterWithAScrape(out SceneSound scrape, arrived);
        Silent sink = new();
        presenter.Emit(scrape with { Tick = 26 });
        presenter.Update(sink, 26, Listener, Right, now: 0.42d);
        sink.Started.ShouldBeEmpty("the condition: the scrape waits for its wave");

        if (levelChange)
        {
            LevelChange(presenter);
        }

        arrived[0] = true;
        presenter.Update(sink, 27, Listener, Right, now: 0.43d);

        return sink.Started;
    }

    /// <remarks>
    /// A stop that finds the voice — a sink that hands out handles — tells the loop tracking which voice ended
    /// (`ForgetVoice`), so a scrape on <c>CHAN_STATIC</c>, which is tracked by its handle, stops being followed: the
    /// pass's re-gain never asks the freed voice again. (Without the stop telling it, the re-gain finds the voice gone and
    /// drops the loop itself — so the loop's absence alone cannot show the hit path; the re-gain of a freed voice can.)
    /// </remarks>
    [Test]
    public void Scripts_ALevelChange_StopsATrackedScrapesVoiceAndForgetsItsLoop()
    {
        ActiveLoops loops = new();
        SoundPresenter presenter = PresenterWithAScrape(out SceneSound scrape, arrived: null, channel: SoundScript.StaticChannel, loops);
        Silent sink = new() { HandsOutVoices = true };
        presenter.Emit(scrape with { Tick = 26 });
        presenter.Update(sink, 26, Listener, Right, now: 0.42d);
        loops.GainAt(40, SoundScript.StaticChannel, Listener, 1f).ShouldNotBeNull("the condition: the scrape's loop is tracked");

        LevelChange(presenter);
        presenter.Update(sink, 27, Listener, Right, now: 0.43d);

        (sink.Hits, sink.GainsOfStoppedVoices, loops.GainAt(40, SoundScript.StaticChannel, Listener, 1f)).ShouldBe((1, 0, (float?)null));
    }

    /// <summary>A level change: the level's scripts set again.</summary>
    private static void LevelChange(SoundPresenter presenter)
    {
        SoundScriptCatalog? level = presenter.Scripts;
        presenter.Scripts = level;
    }

    /// <summary>A presenter whose corpse scrape plays at tick 26, and the sink it plays on.</summary>
    private static (SoundPresenter Presenter, Silent Sink) PlayingScrape() => PlayingScrape(out _);

    /// <summary>A presenter whose corpse scrape plays at tick 26, the sink it plays on, and the scrape's start.</summary>
    private static (SoundPresenter Presenter, Silent Sink) PlayingScrape(out SceneSound scrape)
    {
        SoundPresenter presenter = PresenterWithAScrape(out scrape);
        Silent sink = new();
        presenter.Emit(scrape with { Tick = 26 });
        presenter.Update(sink, 26, Listener, Right, now: 0.42d);
        sink.Started.ShouldContain((40, "a.wav"), "the condition: the scrape plays");
        sink.Silenced.ShouldBeEmpty("the condition: nothing stopped it yet");

        return (presenter, sink);
    }

    /// <summary>A presenter played to tick 25 with one corpse scrape loop playing.</summary>
    private static SoundPresenter PresenterWithAScrape() => PresenterWithAScrape(out _);

    /// <summary>A presenter played to tick 25 with one corpse scrape loop playing, and that loop's start.</summary>
    private static SoundPresenter PresenterWithAScrape(
        out SceneSound scrape, bool[]? arrived = null, int channel = 1, ActiveLoops? loops = null)
    {
        List<string> played = [];
        SoundPresenter presenter = Presenter(played, Entry(channel: 0, "a.wav"), arrived, loops);
        presenter.Schedule = new SoundSchedule([Dealt(20, 0)]);

        VphysicsSurfaceProps surfaces = new([]);
        surfaces.ParseSurfaceData(System.Text.Encoding.Latin1.GetBytes(
            $"\"default\" {{ \"scraperough\" \"{Script}\" }} \"flesh\" {{ \"scraperough\" \"{Script}\" }}"));

        // The presenter's catalog's one script, so the scrape's start is dealt — and kept — when it plays.
        Dictionary<string, SoundScriptEntry> scripts = new(StringComparer.OrdinalIgnoreCase)
        {
            [Script] = new(Script, channel, new SoundRange(0.8f, 0.8f), new SoundRange(100f, 100f), new SoundRange(75f, 75f), ["a.wav"]),
        };
        CorpseFriction friction = new(40, 7750f, surfaces.GetSurfaceIndex("flesh"), surfaces.GetSurfaceIndex("default"));

        presenter.Update(new Silent(), 0, Listener, Right, now: 0d);
        presenter.Update(new Silent(), 25, Listener, Right, now: 0.4d);
        scrape = presenter.FrictionSounds.Step(25, 0.4, [friction], static (entity, tick) => (entity, tick, 0f), surfaces, scripts).ShouldHaveSingleItem();
        presenter.FrictionSounds.Playing.ShouldBe(1, "the condition: a scrape loop plays");

        return presenter;
    }

    /// <remarks>
    /// A level change resets the sound controller — `CSoundControllerImp::SystemReset`, "level change, etc."
    /// (`soundenvelope.cpp:862-878`), shuts down every patch — so the deck stops holding them when the level's scripts
    /// arrive (`LevelSystems` sets them for each map).
    /// </remarks>
    [Test]
    public void Scripts_ALevelChange_ForgetsEveryPatch()
    {
        List<string> played = [];
        SoundPresenter presenter = Presenter(played, Entry(channel: 6, "a.wav", "b.wav"));
        SceneSound loop = Dealt(20, 0, emitted: false) with { EntityIndex = 5, Channel = 6 };
        presenter.Schedule = new SoundSchedule([loop with { WaveDraw = loop.WaveDraw! with { Scaled = true, Kept = true } }]);

        presenter.Update(new Silent(), 0, Listener, Right, now: 0d);
        presenter.Update(new Silent(), 25, Listener, Right, now: 0.4d);
        presenter.PatchesHeld.ShouldBe(1, "the condition: the patch was dealt");

        SoundScriptCatalog? level = presenter.Scripts;
        presenter.Scripts = level;
        presenter.PatchesHeld.ShouldBe(0);
    }

    /// <remarks>
    /// **A skip drops every unreliable temp entity.** `CL_ParseTempEntities`' queueing (engine.dll FUN_1801f9bc0) returns
    /// before queueing anything when the message is unreliable (a non-zero count) and the demo player's skip test — the
    /// vtable slot +0x48 that `CDemoPlayer::ReadPacket` (FUN_180072ee0) asks too — is true. A blast skipped over never
    /// fires, so it never deals.
    /// </remarks>
    [Test]
    public void Update_ASeekPastTwoUnreliableBlasts_DealsNeither()
    {
        List<string> played = [];
        SoundPresenter seeking = Presenter(played, Entry(channel: 0, "a.wav", "b.wav", "c.wav"));
        seeking.Schedule = new SoundSchedule([Dealt(100, 0), Dealt(200, 0), Dealt(400, 0)]);

        seeking.Update(new Silent(), 0, Listener, Right, now: 0d);
        seeking.Update(new Silent(), 350, Listener, Right, now: 1d);
        seeking.Update(new Silent(), 400, Listener, Right, now: 1.1d);

        played.ShouldBe(["a.wav"], "the skip dropped both blasts, so the deck is full at 400");
    }

    /// <remarks>A RELIABLE temp entity (a zero count, FUN_1801f9bc0's first branch) is queued while skipping, and fires.</remarks>
    [Test]
    public void Update_ASeekPastTwoReliableTempEntities_DealsBoth()
    {
        List<string> played = [];
        SoundPresenter seeking = Presenter(played, Entry(channel: 0, "a.wav", "b.wav", "c.wav"));
        seeking.Schedule = new SoundSchedule([Reliable(100, 0), Reliable(200, 0), Dealt(400, 0)]);

        seeking.Update(new Silent(), 0, Listener, Right, now: 0d);
        seeking.Update(new Silent(), 350, Listener, Right, now: 1d);
        seeking.Update(new Silent(), 400, Listener, Right, now: 1.1d);

        played.ShouldBe(["c.wav"], "the skip dealt a and b without playing them");
    }

    /// <remarks>An unreliable blast behind a skip back was dealt by playing and is not dealt again; the deck is not rewound.</remarks>
    [Test]
    public void Update_ASeekBackPastAnUnreliableBlast_LeavesTheDeckAsPlayingLeftIt()
    {
        List<string> played = [];
        SoundPresenter presenter = Presenter(played, Entry(channel: 0, "a.wav", "b.wav", "c.wav"));
        presenter.Schedule = new SoundSchedule([Dealt(100, 0), Dealt(200, 0)]);

        presenter.Update(new Silent(), 0, Listener, Right, now: 0d);
        presenter.Update(new Silent(), 100, Listener, Right, now: 1.5d);
        presenter.Update(new Silent(), 200, Listener, Right, now: 3d);
        presenter.Update(new Silent(), 150, Listener, Right, now: 4d);
        presenter.Update(new Silent(), 200, Listener, Right, now: 5d);

        played.ShouldBe(["a.wav", "b.wav", "c.wav"]);
    }

    [Test]
    public void Update_ASeekBack_DealsFromTheStartAgainOntoTheDeckPlayingLeft()
    {
        // **The engine has no seek backwards: `demo_gototick` to an earlier tick RELOADS the demo and skips forward from
        // its start** (engine.dll FUN_180073b10, "DemoPlayer: Reloading demo file"), and the flags live in
        // soundemittersystem.dll, which a reload does not touch. So the deck is not rewound: the skip deals every
        // reliable emitted sound through the tick again, on top of what playing had already dealt.
        SoundScriptEntry entry = Entry(channel: 0, "a.wav", "b.wav", "c.wav");

        List<string> played = [];
        SoundPresenter presenter = Presenter(played, entry);
        presenter.Schedule = new SoundSchedule([Reliable(100, 0), Reliable(200, 0)]);

        presenter.Update(new Silent(), 0, Listener, Right, now: 0d);
        presenter.Update(new Silent(), 100, Listener, Right, now: 1.5d);
        presenter.Update(new Silent(), 200, Listener, Right, now: 3d);

        // Back to 150: the skip from the start deals the sound at 100 again — c, the only wave left — emptying the deck,
        // so the sound at 200 resets it and takes a.
        presenter.Update(new Silent(), 150, Listener, Right, now: 4d);
        presenter.Update(new Silent(), 200, Listener, Right, now: 5d);

        played.ShouldBe(["a.wav", "b.wav", "a.wav"]);
    }

    /// <remarks>A skip forward reads ahead from where playback is: only the ticks it passes over deal (FUN_180073b10).</remarks>
    [Test]
    public void Update_ASeekForwardFromMidway_DealsOnlyTheTicksItSkips()
    {
        SoundScriptEntry entry = Entry(channel: 0, "a.wav", "b.wav", "c.wav");

        List<string> played = [];
        SoundPresenter presenter = Presenter(played, entry);
        presenter.Schedule = new SoundSchedule([Reliable(100, 0), Reliable(200, 0), Reliable(400, 0), Reliable(600, 0)]);

        presenter.Update(new Silent(), 0, Listener, Right, now: 0d);
        presenter.Update(new Silent(), 100, Listener, Right, now: 1.5d);
        presenter.Update(new Silent(), 200, Listener, Right, now: 3d);
        presenter.Update(new Silent(), 500, Listener, Right, now: 7d);
        presenter.Update(new Silent(), 600, Listener, Right, now: 9d);

        // a and b played; the skip dealt c at 400; the deck is empty, so 600 resets it and takes a.
        played.ShouldBe(["a.wav", "b.wav", "a.wav"]);
    }

    /// <remarks>
    /// **A skip deals what `CL_FireEvents` fires, and only that.** A queued temp entity fires with no skipping test
    /// (engine.dll FUN_1800905d0), so a reliable one's emitted sound deals; a READ never changes which waves are available,
    /// so a skip has nothing to do for one.
    /// </remarks>
    [Test]
    public void Update_ASeekPastAReadAndADeal_DealsOnlyTheEmittedOne()
    {
        SoundScriptEntry entry = Entry(channel: 0, "a.wav", "b.wav", "c.wav");

        List<string> played = [];
        SoundPresenter presenter = Presenter(played, entry);
        presenter.Schedule = new SoundSchedule([Reliable(100, 1, emitted: false), Reliable(200, 1), Dealt(400, 1)]);

        presenter.Update(new Silent(), 0, Listener, Right, now: 0d);
        presenter.Update(new Silent(), 350, Listener, Right, now: 1d);
        presenter.Update(new Silent(), 400, Listener, Right, now: 1.1d);

        // The deal at 200 took b (1 mod 3); at 400, 1 mod 2 over a and c is c.
        played.ShouldBe(["c.wav"]);
    }

    /// <remarks>
    /// **`CHLClient::OnRenderStart` (`cdll_client_int.cpp:2137-2255`) fixes the order on one frame**: entities simulate
    /// — animation events and footsteps, `C_BaseAnimating::Simulate` → `DoAnimationEvents` — then physics, then
    /// `engine->FireEvents()` fires the temp entities in queue order. So on one tick an animation event deals first.
    /// </remarks>
    [Test]
    public void Update_AnAnimationEventAndABlastOnOneTick_DealTheAnimationEventFirst()
    {
        List<string> played = [];
        SoundPresenter presenter = Presenter(played, Entry(channel: 0, "a.wav", "b.wav", "c.wav"));
        Silent sink = new();

        presenter.Schedule = new SoundSchedule([Dealt(20, 0) with { Order = new ClientSoundOrder(ClientSoundPhase.TempEntities, 1, 0) }]);

        presenter.Update(sink, 0, Listener, Right, now: 0d);
        presenter.Emit(Dealt(20, 0) with { EntityIndex = 9, Order = new ClientSoundOrder(ClientSoundPhase.Simulate, 0, 0) });
        presenter.Update(sink, 40, Listener, Right, now: 0.6d);

        sink.Started.ShouldBe([(9, "a.wav"), (0, "b.wav")]);
    }

    /// <remarks>Two temp entities on one tick fire in the order the packet carried them (FUN_1800905d0).</remarks>
    [Test]
    public void Update_TwoTempEntityDealsOnOneTick_AreDealtInStreamOrder()
    {
        List<string> played = [];
        SoundPresenter presenter = Presenter(played, Entry(channel: 0, "a.wav", "b.wav", "c.wav"));
        Silent sink = new();

        presenter.Schedule = new SoundSchedule([Dealt(20, 0) with { Order = new ClientSoundOrder(ClientSoundPhase.TempEntities, 9, 0) }]);

        presenter.Update(sink, 0, Listener, Right, now: 0d);
        presenter.Emit(Dealt(20, 0) with { EntityIndex = 9, Order = new ClientSoundOrder(ClientSoundPhase.TempEntities, 5, 0) });
        presenter.Update(sink, 40, Listener, Right, now: 0.6d);

        sink.Started.ShouldBe([(9, "a.wav"), (0, "b.wav")]);
    }

    /// <remarks>
    /// **A frame that crosses several ticks parses every tick's packet before it renders once** (B505, B504). `_Host_RunFrame`
    /// (engine.dll FUN_1801a4570) calls `_Host_RunFrame_Client` (FUN_1801a5860) → `CL_ReadPackets` once per tick it crossed
    /// — each firing that tick's game events as it parses — and `_Host_RunFrame_Render` once after the loop, where
    /// `OnRenderStart` simulates and `FireEvents` fires everything queued. So a HUD sound on the later tick deals before a
    /// blast on the earlier one.
    /// </remarks>
    [Test]
    public void Update_OneFrameOverABlastThenAHudSoundOnTheNextTick_DealsTheHudSoundFirst()
    {
        List<string> played = [];
        SoundPresenter presenter = Presenter(played, Entry(channel: 0, "a.wav", "b.wav", "c.wav"));
        Silent sink = new();

        presenter.Schedule = new SoundSchedule([Dealt(20, 0) with { Order = new ClientSoundOrder(ClientSoundPhase.TempEntities, 1, 0) }]);

        presenter.Update(sink, 19, Listener, Right, now: 0d);
        presenter.Emit(Dealt(21, 0) with { EntityIndex = 9, Order = new ClientSoundOrder(ClientSoundPhase.Network, 0, 0) });
        presenter.Update(sink, 21, Listener, Right, now: 0.03d);

        sink.Started.ShouldBe([(9, "a.wav"), (0, "b.wav")]);
    }

    /// <remarks>The control for the test above: one tick per frame, and the blast's frame comes first.</remarks>
    [Test]
    public void Update_ABlastThenAHudSoundOnTheNextFrame_DealsTheBlastFirst()
    {
        List<string> played = [];
        SoundPresenter presenter = Presenter(played, Entry(channel: 0, "a.wav", "b.wav", "c.wav"));
        Silent sink = new();

        presenter.Schedule = new SoundSchedule([Dealt(20, 0) with { Order = new ClientSoundOrder(ClientSoundPhase.TempEntities, 1, 0) }]);

        presenter.Update(sink, 19, Listener, Right, now: 0d);
        presenter.Update(sink, 20, Listener, Right, now: 0.015d);
        presenter.Emit(Dealt(21, 0) with { EntityIndex = 9, Order = new ClientSoundOrder(ClientSoundPhase.Network, 0, 0) });
        presenter.Update(sink, 21, Listener, Right, now: 0.03d);

        sink.Started.ShouldBe([(0, "a.wav"), (9, "b.wav")]);
    }

    /// <remarks>Within the frame's temp entities, the queue's order: an earlier tick's before a later tick's.</remarks>
    [Test]
    public void Update_OneFrameOverTwoTicksOfTempEntities_DealsThemInQueueOrder()
    {
        List<string> played = [];
        SoundPresenter presenter = Presenter(played, Entry(channel: 0, "a.wav", "b.wav", "c.wav"));
        Silent sink = new();

        presenter.Schedule = new SoundSchedule([
            Dealt(20, 0) with { Order = new ClientSoundOrder(ClientSoundPhase.TempEntities, 1, 0) },
            Dealt(21, 0) with { EntityIndex = 9, Order = new ClientSoundOrder(ClientSoundPhase.TempEntities, 2, 0) },
        ]);

        presenter.Update(sink, 19, Listener, Right, now: 0d);
        presenter.Emit(Dealt(21, 0) with { EntityIndex = 7, Order = new ClientSoundOrder(ClientSoundPhase.Simulate, 0, 0) });
        presenter.Update(sink, 21, Listener, Right, now: 0.03d);

        sink.Started.ShouldBe([(7, "a.wav"), (0, "b.wav"), (9, "c.wav")]);
    }

    /// <remarks>
    /// **A skip renders a frame per batch of packets, and each frame keeps the frame's order** (B504): a temp entity in the
    /// first batch fires at that batch's render, before a later batch's game events parse. Draw 0 is the blast's, draw 1
    /// the HUD sound's and the one at 400's: blast first leaves b for it, HUD first — or no HUD sound — leaves c.
    /// </remarks>
    [Test]
    public void Update_ASkipWhoseHudSoundIsInALaterFrame_DealsTheEarlierFramesBlastFirst()
    {
        List<string> played = [];
        SoundPresenter presenter = Presenter(played, Entry(channel: 0, "a.wav", "b.wav", "c.wav"));
        presenter.Schedule = new SoundSchedule([ReliableBlast(100, 0), Dealt(400, 1)]);

        presenter.Update(new Silent(), 0, Listener, Right, now: 0d);
        presenter.Skipped(new SoundSkip(0, 350, [new SkipFrame(120, []), new SkipFrame(350, [HudSound(300, 1)])]));
        presenter.Update(new Silent(), 350, Listener, Right, now: 1d);
        presenter.Update(new Silent(), 400, Listener, Right, now: 1.1d);

        played.ShouldBe(["b.wav"]);
    }

    /// <remarks>One batch: its game events parse before its render fires the blast, though the blast's tick is earlier.</remarks>
    [Test]
    public void Update_ASkipFrameWithABlastAndALaterHudSound_DealsTheHudSoundFirst()
    {
        List<string> played = [];
        SoundPresenter presenter = Presenter(played, Entry(channel: 0, "a.wav", "b.wav", "c.wav"));
        presenter.Schedule = new SoundSchedule([ReliableBlast(100, 0), Dealt(400, 0)]);

        presenter.Update(new Silent(), 0, Listener, Right, now: 0d);
        presenter.Skipped(new SoundSkip(0, 350, [new SkipFrame(350, [HudSound(300, 1)])]));
        presenter.Update(new Silent(), 350, Listener, Right, now: 1d);
        presenter.Update(new Silent(), 400, Listener, Right, now: 1.1d);

        played.ShouldBe(["c.wav"]);
    }

    /// <remarks>An animation event a skip frame crosses is emitted, so it deals (`AE_CL_PLAYSOUND`, `EmitSound`).</remarks>
    [Test]
    public void Update_ASkipFrameWithAnAnimationSound_DealsIt()
    {
        List<string> played = [];
        SoundPresenter presenter = Presenter(played, Entry(channel: 0, "a.wav", "b.wav", "c.wav"));
        presenter.Schedule = new SoundSchedule([Dealt(400, 0)]);

        presenter.Update(new Silent(), 0, Listener, Right, now: 0d);
        presenter.Skipped(new SoundSkip(0, 350, [new SkipFrame(350, [Dealt(350, 0) with { Order = new ClientSoundOrder(ClientSoundPhase.Simulate, 0, 0) }])]));
        presenter.Update(new Silent(), 350, Listener, Right, now: 1d);
        presenter.Update(new Silent(), 400, Listener, Right, now: 1.1d);

        played.ShouldBe(["b.wav"], "the skip dealt a");
    }

    /// <remarks>
    /// A skip's frames are handed as the view builds the moment, and the presenter may reach it a few ticks later — so a
    /// skip to 300 lands on an update at 350, and one to a tick this update has not reached does not.
    /// </remarks>
    [TestCase(300, "b.wav")]
    [TestCase(351, "a.wav")]
    public void Update_ASkipHandedForATick_DealsOnlyWhenThisUpdateReachedIt(int to, string expected)
    {
        List<string> played = [];
        SoundPresenter presenter = Presenter(played, Entry(channel: 0, "a.wav", "b.wav", "c.wav"));
        presenter.Schedule = new SoundSchedule([Dealt(400, 0)]);

        presenter.Update(new Silent(), 0, Listener, Right, now: 0d);
        presenter.Skipped(new SoundSkip(0, to, [new SkipFrame(to, [Dealt(to, 0)])]));
        presenter.Update(new Silent(), 350, Listener, Right, now: 1d);
        presenter.Update(new Silent(), 400, Listener, Right, now: 1.1d);

        played.ShouldBe([expected]);
    }

    /// <remarks>
    /// `CDemoPlayer::ReadPacket` (FUN_180072ee0) counts packets while skipping and returns null at the 100th, so a read
    /// loop takes 99 and the frame renders; the batch that reaches the target is the frame the skip lands on, which the
    /// viewer draws — and sounds — itself, so it is not one of the skip's.
    /// </remarks>
    [Test]
    public void Frames_ASkipOf301Ticks_RendersEvery99BeforeTheTarget()
    {
        DemoSkip.Frames(1000, 1300).ShouldBe([1098, 1197, 1296]);
        DemoSkip.Frames(1000, 1099).ShouldBe([1098]);
        DemoSkip.Frames(1000, 1098).ShouldBe([]);
    }

    /// <remarks>The schedule's own rule for a seek — backwards, the first call, or past its catch-up — shared, not copied.</remarks>
    [Test]
    public void IsSkip_EachKindOfMove_IsTheSchedulesSeek()
    {
        (SoundSchedule.IsSkip(null, 5), SoundSchedule.IsSkip(100, 99), SoundSchedule.IsSkip(100, 233), SoundSchedule.IsSkip(100, 234))
            .ShouldBe((true, true, false, true));
    }

    private static SceneSound ReliableBlast(int tick, int draw) =>
        Reliable(tick, draw) with { Order = new ClientSoundOrder(ClientSoundPhase.TempEntities, 1, 0) };

    private static SceneSound HudSound(int tick, int draw) =>
        Dealt(tick, draw) with { EntityIndex = 9, Order = new ClientSoundOrder(ClientSoundPhase.Network, 0, 0) };

    [Test]
    public void Producers_EachSound_IsStampedWithItsPlaceInOnRenderStart()
    {
        Dictionary<string, SoundScriptEntry> scripts = new(StringComparer.OrdinalIgnoreCase)
        {
            [Script] = Entry(channel: 0, "a.wav", "b.wav"),
            [TracerWhiz.Sound] = Entry(channel: 0, "a.wav", "b.wav") with { Name = TracerWhiz.Sound },
        };

        SceneSound blast = ExplosionSounds.For(
            [new SceneExplosion(5, 0f, 0f, 0f, (0f, 0f, 1f), 22, SceneExplosion.NoEntity, SceneExplosion.NoCustomParticle) { TempEntity = 7, Reliable = true }],
            static _ => Script,
            scripts).ShouldHaveSingleItem();

        SceneSound impact = ImpactSounds.For(
            new BulletLanding(5, (0f, 0f, 0f), Script, Ricochets: false) { Order = new ClientSoundOrder(ClientSoundPhase.TempEntities, 8, 2) },
            seed: 1,
            scripts).ShouldHaveSingleItem();

        SceneSound whiz = TracerWhiz.SoundAt(5, (0f, 0f, 0f), scripts, new ClientSoundOrder(ClientSoundPhase.TempEntities, 8, 2)).ShouldNotBeNull();
        SceneSound hud = HudSounds.Emit(5, Script, scripts).ShouldNotBeNull();

        (blast.Order, impact.Order, whiz.Order, hud.Order).ShouldBe((
            new ClientSoundOrder(ClientSoundPhase.TempEntities, 7, 0),
            new ClientSoundOrder(ClientSoundPhase.TempEntities, 8, 2),
            new ClientSoundOrder(ClientSoundPhase.TempEntities, 8, 2),
            new ClientSoundOrder(ClientSoundPhase.Network, 0, 0)));

        // A reliable temp entity survives a skip (FUN_1801f9bc0); an unreliable one does not.
        (blast.DealtBySkip, impact.DealtBySkip).ShouldBe((true, false));
    }

    /// <remarks>
    /// **`PrecacheScriptSound` precaches every wave of the script** (`SoundEmitterSystem.cpp:367-370`,
    /// `InternalPrecacheWaves`), not the one a draw will pick — so a dealt wave is never a first-use decode.
    /// </remarks>
    [Test]
    public void ScriptWaves_DealtSounds_NameEveryWaveOfTheirScriptsOnce()
    {
        Dictionary<string, SoundScriptEntry> scripts = new(StringComparer.OrdinalIgnoreCase) { [Script] = Entry(channel: 0, "a.wav", "b.wav", "c.wav") };

        DemoSounds.ScriptWaves([Dealt(10, 0), Dealt(20, 1), new SceneSound(30, "demo.wav", 1, 0, 0, 1f, 75, 100, 0f, 0f, 0f, 0f)], scripts)
            .ShouldBe(["a.wav", "b.wav", "c.wav"]);
    }

    private static readonly (float X, float Y, float Z) Listener = (0f, 0f, 0f);

    private static readonly (float X, float Y, float Z) Right = (0f, -1f, 0f);

    /// <summary>A sound whose wave's number is <paramref name="draw"/> modulo 6 — so modulo 1, 2 and 3 alike.</summary>
    private static SceneSound Dealt(int tick, int draw, bool emitted = true) =>
        new SceneSound(tick, "a.wav", ExplosionSounds.NotPrecached, ExplosionSounds.FromWorld, 0, 1f, 75, 100, 0f, 0f, 0f, 0f)
        {
            WaveDraw = new ScriptWaveDraw(Script, emitted, StreamWhoseWaveNumberIs(draw)),
        };

    /// <summary>The first seeded stream whose third number — the wave's, after the volume and the pitch — is <paramref name="residue"/> mod 6.</summary>
    private static RandomStreamPosition StreamWhoseWaveNumberIs(int residue)
    {
        for (int seed = 1; seed < 1000; seed++)
        {
            UniformRandomStream probe = new();
            probe.SetSeed(seed);
            probe.RandomFloat(1f, 1f);
            probe.RandomFloat(100f, 100f);

            if (probe.RandomInt(0, int.MaxValue - 1) % 6 == residue)
            {
                return new RandomStreamPosition(seed, 0);
            }
        }

        throw new InvalidOperationException($"no seed under 1000 draws {residue} mod 6");
    }

    /// <summary>A sound from a reliable temp entity, which a skip still fires.</summary>
    private static SceneSound Reliable(int tick, int draw, bool emitted = true) => Dealt(tick, draw, emitted) with { DealtBySkip = true };

    private static SoundScriptEntry Entry(int channel, params string[] waves) =>
        new(Script, channel,new SoundRange(1f, 1f), new SoundRange(100f, 100f), new SoundRange(75f, 75f), waves);

    /// <summary>One sample per wave name — its own array, which is what its equality compares — so a sample says its wave.</summary>
    private static readonly Dictionary<SoundSample, string> NameOf = [];

    private static readonly Dictionary<string, SoundSample> SampleFor = new(StringComparer.Ordinal);

    private static SoundSample Sample(string name)
    {
        lock (NameOf)
        {
            if (!SampleFor.TryGetValue(name, out SoundSample sample))
            {
                sample = new SoundSample(SampleRate: 22050, Channels: 1, Samples: new float[] { 0.5f });
                SampleFor[name] = sample;
                NameOf[sample] = name;
            }

            return sample;
        }
    }

    /// <summary>A presenter whose samples record the name each start asked for, resolving through a catalog of one entry.</summary>
    private static SoundPresenter Presenter(List<string> played, SoundScriptEntry entry) => Presenter(played, entry, arrived: null);

    /// <param name="played">Every name the presenter asked to open.</param>
    /// <param name="entry">The catalog's one entry.</param>
    /// <param name="arrived">While its one value is false, every wave is still loading: opening answers null.</param>
    /// <param name="loops">The loop tracking the presenter is given, for a test to read; a new one when null.</param>
    private static SoundPresenter Presenter(List<string> played, SoundScriptEntry entry, bool[]? arrived, ActiveLoops? loops = null)
    {
        SoundPresenter presenter = new(
            new SoundscapeSystem(new ActiveLoops(), _ => null, NullLogger.Instance),
            loops ?? new ActiveLoops(),
            name =>
            {
                played.Add(name);
                return arrived is null || arrived[0] ? Sample(name) : null;
            },
            NullLogger.Instance)
        {
            // A stop asks what is held, which records no play: every wave here is held once dealt.
            Held = name => arrived is null || arrived[0] ? Sample(name) : null,
            Loading = _ => arrived is not null && !arrived[0],
        };

        string waves = string.Concat(Array.ConvertAll([.. entry.Waves], wave => $"\t\t\"wave\" \"{wave}\"\n"));
        byte[] manifest = System.Text.Encoding.UTF8.GetBytes("\"game_sounds_manifest\"\n{\n\t\"precache_file\" \"scripts/test.txt\"\n}\n");
        byte[] script = System.Text.Encoding.UTF8.GetBytes($"\"{entry.Name}\"\n{{\n\t\"rndwave\"\n\t{{\n{waves}\t}}\n}}\n");

        presenter.Scripts = SoundScriptCatalog.Load(path => path switch
        {
            "scripts/game_sounds_manifest.txt" => manifest,
            "scripts/test.txt" => script,
            _ => null,
        });

        presenter.Scripts.Entries[entry.Name].Waves.ShouldBe(entry.Waves, "the control: the catalog the presenter resolves with holds the entry");

        return presenter;
    }

    /// <summary>Plays nothing; records the wave each named stop silences.</summary>
    private sealed class Silent : IAudioSink
    {
        public float MasterGain { get; set; } = 1f;

        public List<string> Silenced { get; } = [];

        public List<(int Entity, string Wave)> Started { get; } = [];

        /// <summary>Every start and stop, in the order the sink was asked.</summary>
        public List<string> Events { get; } = [];

        /// <summary>Whether starts are given handles, and a named stop that finds one reports it — off by default.</summary>
        public bool HandsOutVoices { get; init; }

        /// <summary>How many named stops found a voice.</summary>
        public int Hits { get; private set; }

        /// <summary>The voices playing, with their handles, when handles are handed out.</summary>
        private readonly List<(int Voice, int Entity, int Channel, string Wave)> _voices = [];

        /// <summary>How often a voice was re-gained after a stop had freed it — the loop tracking still following it.</summary>
        public int GainsOfStoppedVoices { get; private set; }

        /// <summary>The voices a named stop freed.</summary>
        private readonly HashSet<int> _stopped = [];

        public int Play(SoundSample sample, float leftPan, float rightPan, float gain, float pitch, int entity, int channel)
        {
            Started.Add((entity, NameOf[sample]));
            Events.Add($"start {entity}/{channel} {NameOf[sample]}");

            if (!HandsOutVoices)
            {
                return 0;
            }

            int voice = Started.Count;
            _voices.Add((voice, entity, channel, NameOf[sample]));
            return voice;
        }

        // A voice handed out and not stopped is still there, so the per-pass re-gain keeps following it. One the loop
        // tracking still follows after its stop is asked here too, which is what shows a stop did not tell it.
        public bool SetVoiceGain(int voice, float gain)
        {
            GainsOfStoppedVoices += _stopped.Contains(voice) ? 1 : 0;
            return _voices.Exists(playing => playing.Voice == voice);
        }

        public bool SetVoicePitch(int voice, float pitch) => _voices.Exists(playing => playing.Voice == voice);

        public bool SetGain(int entity, int channel, float gain) => false;

        public bool SetPitch(int entity, int channel, float pitch) => false;

        public void SilenceAll()
        {
            // Nothing is held.
        }

        public int Reclaim() => 0;

        public List<(int Entity, int Channel)> SilencedChannels { get; } = [];

        public void Silence(int entity, int channel)
        {
            SilencedChannels.Add((entity, channel));
            Events.Add($"stop-all {entity}/{channel}");
        }

        public int Silence(int entity, int channel, SoundSample sample)
        {
            // A wave not yet held is named by a key-only sample, which no recorded sample is.
            string name = NameOf.TryGetValue(sample, out string? known) ? known : $"key:{sample.Key}";
            Silenced.Add(name);
            Events.Add($"stop {entity}/{channel} {name}");

            int found = _voices.FindIndex(voice => voice.Entity == entity && voice.Channel == channel && voice.Wave == name);

            if (found < 0)
            {
                return 0;
            }

            int stopped = _voices[found].Voice;
            _voices.RemoveAt(found);
            _stopped.Add(stopped);
            Hits++;
            return stopped;
        }
    }
}
