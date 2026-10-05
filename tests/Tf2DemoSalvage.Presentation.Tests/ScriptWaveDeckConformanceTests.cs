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
        SceneSound? whiz = TracerWhiz.SoundAt(5, (0f, 0f, 0f), scripts);
        SceneSound? hud = HudSounds.Emit(5, Script, scripts);

        new[] { blast.WaveDraw, impact.WaveDraw, whiz?.WaveDraw, hud?.WaveDraw }.ShouldAllBe(draw => draw != null && draw.Value.Emitted);
    }

    [Test]
    public void Emit_AnEntitysSoundAsked_DealsOnlyWhenEmitted()
    {
        Dictionary<string, SoundScriptEntry> scripts = new(StringComparer.OrdinalIgnoreCase) { [Script] = Entry(channel: 0, "a.wav", "b.wav") };

        EntitySounds.Emit(5, 3, Script, (0f, 0f, 0f), scripts, emitted: true)!.Value.WaveDraw!.Value.Emitted.ShouldBeTrue();
        EntitySounds.Emit(5, 3, Script, (0f, 0f, 0f), scripts, emitted: false)!.Value.WaveDraw!.Value.Emitted.ShouldBeFalse();
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

    /// <remarks>The seek-equivalence the deck must keep: the state a seek lands in is the state playing there left.</remarks>
    [Test]
    public void Update_ASeekPastTwoDeals_DealsTheNextAsPlayingThroughThemDid()
    {
        SoundScriptEntry entry = Entry(channel:0, "a.wav", "b.wav", "c.wav");
        SceneSound[] schedule = [Dealt(100, 0), Dealt(200, 0), Dealt(400, 0)];

        List<string> playedThrough = [];
        SoundPresenter playing = Presenter(playedThrough, entry);
        playing.Schedule = new SoundSchedule(schedule);

        foreach (int tick in new[] { 0, 100, 200, 300, 400 })
        {
            playing.Update(new Silent(), tick, Listener, Right, now: tick / 66d);
        }

        List<string> playedAfterSeek = [];
        SoundPresenter seeking = Presenter(playedAfterSeek, entry);
        seeking.Schedule = new SoundSchedule(schedule);

        seeking.Update(new Silent(), 0, Listener, Right, now: 0d);
        seeking.Update(new Silent(), 350, Listener, Right, now: 1d);
        seeking.Update(new Silent(), 400, Listener, Right, now: 1.1d);

        playedThrough.ShouldBe(["a.wav", "b.wav", "c.wav"]);
        playedAfterSeek.ShouldBe(["c.wav"], "the seek skipped hearing a and b, not dealing them");
    }

    [Test]
    public void Update_ASeekBackPastALiveEmission_KeepsItsDealButDropsLaterOnes()
    {
        SoundScriptEntry entry = Entry(channel:0, "a.wav", "b.wav", "c.wav");

        List<string> played = [];
        SoundPresenter presenter = Presenter(played, entry);
        presenter.Schedule = new SoundSchedule([Dealt(300, 0)]);

        presenter.Update(new Silent(), 0, Listener, Right, now: 0d);
        presenter.Emit(Dealt(50, 0));
        presenter.Update(new Silent(), 50, Listener, Right, now: 0.7d);
        presenter.Emit(Dealt(100, 0));
        presenter.Update(new Silent(), 100, Listener, Right, now: 1.5d);

        // Back to 60: the emission at 50 was played before it and stays dealt; the one at 100 is in its future.
        presenter.Update(new Silent(), 60, Listener, Right, now: 2d);
        presenter.Update(new Silent(), 180, Listener, Right, now: 3d);
        presenter.Update(new Silent(), 300, Listener, Right, now: 4d);

        played.ShouldBe(["a.wav", "b.wav", "b.wav"]);
    }

    private static readonly (float X, float Y, float Z) Listener = (0f, 0f, 0f);

    private static readonly (float X, float Y, float Z) Right = (0f, -1f, 0f);

    private static SceneSound Dealt(int tick, int draw, bool emitted = true) =>
        new SceneSound(tick, "a.wav", ExplosionSounds.NotPrecached, ExplosionSounds.FromWorld, 0, 1f, 75, 100, 0f, 0f, 0f, 0f)
        {
            WaveDraw = new ScriptWaveDraw(Script, draw, emitted),
        };

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
