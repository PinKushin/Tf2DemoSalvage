using System;
using System.Collections.Generic;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Primitives;
using Tf2DemoSalvage.Core.Scene;

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

        // Available: waves 1 and 2. RandomInt( 0, 1 ) is the draw mod 2, so a draw of 3 picks the list's second, wave 2.
        deck.Pick(Script, 3, 3, emitted: true).ShouldBe(2);
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
    public void FromWorldAt_AnyEntry_CarriesItsScriptAndTheWaveDrawsRawNumber()
    {
        SoundScriptEntry entry = Entry(channel:1, "a.wav", "b.wav", "c.wav");

        UniformRandomStream expected = new();
        expected.SetSeed(11);
        expected.RandomFloat(1f, 1f);
        expected.RandomFloat(100f, 100f);

        // `RandomInt( 0, int.MaxValue - 1 )` is the generator's own number: it is below 2^31 - 1, so the modulo keeps it.
        int raw = expected.RandomInt(0, int.MaxValue - 1);

        UniformRandomStream random = new();
        random.SetSeed(11);

        SceneSound sound = ExplosionSounds.FromWorldAt(entry, random, 1, (0f, 0f, 0f), emitted: true);

        sound.WaveDraw.ShouldBe(new ScriptWaveDraw(Script, raw, Emitted: true));
        sound.Name.ShouldBe(entry.Waves[raw % 3], "before any deal, the wave is the one a full deck gives");
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

        new[] { blast.WaveDraw, impact.WaveDraw, whiz?.WaveDraw, hud?.WaveDraw }.ShouldAllBe(draw => draw != null && draw.Value.Emitted);
    }

    [Test]
    public void Emit_AnEntitysSoundAsked_DealsOnlyWhenEmitted()
    {
        Dictionary<string, SoundScriptEntry> scripts = new(StringComparer.OrdinalIgnoreCase) { [Script] = Entry(channel: 0, "a.wav", "b.wav") };

        EntitySounds.Emit(5, 3, Script, (0f, 0f, 0f), scripts, emitted: true, ClientSoundPhase.Simulate)!.Value.WaveDraw!.Value.Emitted.ShouldBeTrue();
        EntitySounds.Emit(5, 3, Script, (0f, 0f, 0f), scripts, emitted: false, ClientSoundPhase.Simulate)!.Value.WaveDraw!.Value.Emitted.ShouldBeFalse();
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

        SceneSound loop = Dealt(20, 0, emitted: false) with { EntityIndex = 5, Channel = 6 };

        presenter.Schedule = new SoundSchedule([Dealt(10, 0), loop, Dealt(25, 0), loop with { Tick = 30, IsStop = true }]);

        presenter.Update(sink, 0, Listener, Right, now: 0d);
        presenter.Update(sink, 40, Listener, Right, now: 0.6d);

        // The deal at 10 took a; the patch read the deck and found only b; the deal at 25 took b, emptying the deck — so
        // a fresh pick at 30 would reset it and find a. The stop names b, the wave its start is playing.
        played.ShouldBe(["a.wav", "b.wav", "b.wav", "b.wav"]);
        sink.Silenced.ShouldBe(["b.wav"]);
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

    /// <remarks>A skip's frames belong to the tick it went to; handed for another, they are not this seek's.</remarks>
    [Test]
    public void Update_ASkipHandedForAnotherTick_DealsNothing()
    {
        List<string> played = [];
        SoundPresenter presenter = Presenter(played, Entry(channel: 0, "a.wav", "b.wav", "c.wav"));
        presenter.Schedule = new SoundSchedule([Dealt(400, 0)]);

        presenter.Update(new Silent(), 0, Listener, Right, now: 0d);
        presenter.Skipped(new SoundSkip(0, 300, [new SkipFrame(300, [Dealt(300, 0)])]));
        presenter.Update(new Silent(), 350, Listener, Right, now: 1d);
        presenter.Update(new Silent(), 400, Listener, Right, now: 1.1d);

        played.ShouldBe(["a.wav"]);
    }

    /// <remarks>
    /// `CDemoPlayer::ReadPacket` (FUN_180072ee0) counts packets while skipping and returns null at the 100th, so a read
    /// loop takes 99 and the frame renders; the last batch ends at the target, where the skip is done.
    /// </remarks>
    [Test]
    public void Frames_ASkipOf301Ticks_RendersEvery99AndAtTheTarget()
    {
        DemoSkip.Frames(1000, 1300).ShouldBe([1098, 1197, 1296, 1300]);
        DemoSkip.Frames(1000, 1098).ShouldBe([1098]);
        DemoSkip.Frames(1000, 1000).ShouldBe([1000]);
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

    private static SceneSound Dealt(int tick, int draw, bool emitted = true) =>
        new SceneSound(tick, "a.wav", ExplosionSounds.NotPrecached, ExplosionSounds.FromWorld, 0, 1f, 75, 100, 0f, 0f, 0f, 0f)
        {
            WaveDraw = new ScriptWaveDraw(Script, draw, emitted),
        };

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
    private static SoundPresenter Presenter(List<string> played, SoundScriptEntry entry)
    {
        SoundPresenter presenter = new(
            new SoundscapeSystem(new ActiveLoops(), _ => null, NullLogger.Instance),
            new ActiveLoops(),
            name =>
            {
                played.Add(name);
                return Sample(name);
            },
            NullLogger.Instance);

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
        public List<string> Silenced { get; } = [];

        public List<(int Entity, string Wave)> Started { get; } = [];

        public void Play(SoundSample sample, float leftPan, float rightPan, float gain, float pitch, int entity, int channel) =>
            Started.Add((entity, NameOf[sample]));

        public bool SetGain(int entity, int channel, float gain) => false;

        public bool SetPitch(int entity, int channel, float pitch) => false;

        public void SilenceAll()
        {
            // Nothing is held.
        }

        public int Reclaim() => 0;

        public void Silence(int entity, int channel)
        {
            // Only the named stop is asked about.
        }

        public void Silence(int entity, int channel, SoundSample sample) => Silenced.Add(NameOf[sample]);
    }
}
