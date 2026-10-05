using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;
using Tf2DemoSalvage.SdkReference;

// Namespaced away from `Tf2DemoSalvage.Corpus.Tests.*`, where `Corpus` binds to the namespace.
namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>A real match's explosions, played through the presenter, deal each script's waves like a deck (B503).</summary>
/// <remarks>
/// **Output level** (`docs/memory/output-level-assertion-or-it-is-not-done.md`): the names asked of the sample cache by
/// the presenter the viewer runs, for the sounds <see cref="ExplosionSounds.For"/> builds from the f12 recording and the
/// shipped scripts. The deck's rule is `ScriptWaveDeckConformanceTests`; this is the one test that fails if production
/// never wires it — every blast is dealt (`EmitSoundByHandle`) and none has a camera gate, so the n emissions of an
/// n-wave script from the start are a permutation of its waves, then the next n, and so on.
/// </remarks>
public sealed class CorpusScriptWaveDeckTests
{
    /// <summary>The f12 parity reference since 2026-09-30.</summary>
    private const string F12Recording = "demostf-cp_process_f12-2026-08-08-2207";

    [Test]
    public void Update_EveryExplosionOfARealMatch_DealsEachScriptsWavesBeforeRepeatingOne()
    {
        if (Corpus.Demo(F12Recording) is not { } path)
        {
            Assert.Ignore($"{F12Recording}.dem is not available");
            return;
        }

        if (GameInstall.Vpk("tf2_misc") is not { } directory)
        {
            Assert.Ignore(GameInstall.Missing);
            return;
        }

        VpkArchive archive = VpkArchive.Open(directory);
        byte[]? Read(string file) => archive.ReadFile(file.ToUpperInvariant());

        SoundScriptCatalog catalog = SoundScriptCatalog.Load(Read);
        DemoTimeline timeline = TimelineCache.For(path);
        ExplosionEffects effects = new(Read);

        IReadOnlyList<SceneSound> sounds = ExplosionSounds.For(
            timeline.Explosions.All, blast => effects.SoundFor(blast, static (_, _) => null, hasLocalPlayer: false), catalog.Entries);

        List<string> played = [];
        SoundSample sample = new(SampleRate: 22050, Channels: 1, Samples: new float[] { 0.5f });

        SoundPresenter presenter = new(
            new SoundscapeSystem(new ActiveLoops(), _ => null, NullLogger.Instance),
            new ActiveLoops(),
            name =>
            {
                played.Add(name);
                return sample;
            },
            NullLogger.Instance)
        {
            Scripts = catalog,
            Schedule = new SoundSchedule(sounds),
        };

        // Steps under `SoundSchedule.CatchUpTicks`, so every blast is played rather than skipped by a seek.
        for (int tick = 0; tick <= sounds[^1].Tick + 66; tick += 66)
        {
            presenter.Update(new Mute(), tick, (0f, 0f, 0f), (0f, -1f, 0f), now: tick / 66d);
        }

        played.Count.ShouldBe(sounds.Count, "the control: every blast starts, in schedule order");

        int checkedBlocks = 0;

        foreach (IGrouping<string, int> script in Enumerable.Range(0, sounds.Count).GroupBy(index => sounds[index].WaveDraw!.Value.Script))
        {
            int waves = catalog.Entries[script.Key].Waves.Count;
            int[] order = [.. script];

            for (int start = 0; waves > 1 && start + waves <= order.Length; start += waves)
            {
                order.Skip(start).Take(waves).Select(index => played[index]).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                    .ShouldBe(waves, $"{script.Key}'s emissions {start}..{start + waves - 1} repeat a wave before dealing all {waves}");
                checkedBlocks++;
            }

            TestContext.Out.WriteLine($"{script.Key}: {order.Length} blasts over {waves} waves");
        }

        checkedBlocks.ShouldBeGreaterThan(10, "the control: a real match deals multi-wave explosion scripts many times over");
    }

    /// <summary>Plays nothing.</summary>
    private sealed class Mute : IAudioSink
    {
        public void Play(SoundSample sample, float leftPan, float rightPan, float gain, float pitch, int entity, int channel)
        {
            // The presenter's sample lookup is the record.
        }

        public bool SetGain(int entity, int channel, float gain) => false;

        public bool SetPitch(int entity, int channel, float pitch) => false;

        public void SilenceAll()
        {
            // Nothing is held.
        }

        public int Reclaim() => 0;

        public void Silence(int entity, int channel)
        {
            // Explosions schedule no stops.
        }

        public void Silence(int entity, int channel, SoundSample sample)
        {
            // As above.
        }
    }
}
