using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Primitives;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// A script sound takes exactly the engine's draws, in the engine's order, against the waves available when it plays (B782).
/// </summary>
/// <remarks>
/// **Disassembly, x64 <c>soundemittersystem.dll</c> (Ghidra project <c>tf2soundemitter</c>).** <c>GetParametersForSound</c>
/// (FUN_180003370) draws <c>RandomFloat</c> for the volume, <c>RandomFloat</c> for the pitch, then calls FUN_180005680 —
/// which returns <c>list[ RandomInt( 0, count - 1 ) ]</c> over the AVAILABLE waves, through a stream adaptor whose slot
/// (<c>0x180003c90</c>) is <c>mov ecx, edx; mov edx, r8d; jmp [RandomInt]</c>, vstdlib's export — and clears the pick when
/// <c>isbeingemitted</c>, then draws <c>RandomFloat</c> for the soundlevel. vstdlib's <c>RandomInt</c> (<c>0x18000fb70</c>)
/// answers a span of one with NO number and draws again above <c>0x7fffffff - 0x80000000 % span</c> (finding 57).
/// </remarks>
public sealed class ScriptWaveDrawConformanceTests
{
    private static readonly (float X, float Y, float Z) Origin = (0f, 0f, 0f);

    /// <summary>A seed whose fourth number is <c>0x7ffffffe</c>, the one number <c>RandomInt( 0, 2 )</c> refuses.</summary>
    /// <remarks>Found by walking Park–Miller back from <c>0x7ffffffe</c>; the test's control re-reads it from the generator.</remarks>
    private const int RefusedFourthSeed = 2107827854;

    [Test]
    public void Deal_AOneWaveEntry_TakesNoWaveNumberSoItsSoundLevelIsTheThirdDraw()
    {
        SoundScriptEntry entry = Entry("Test.One", "one.wav");

        SceneSound dealt = new ScriptWaveDeck().Deal(ExplosionSounds.FromWorldAt(entry, Seeded(7), 1, Origin, emitted: true), Scripts(entry));

        UniformRandomStream control = Seeded(7);
        control.RandomFloat(entry.Volume.Low, entry.Volume.High);
        control.RandomFloat(entry.Pitch.Low, entry.Pitch.High);
        int third = (int)control.RandomFloat(entry.SoundLevel.Low, entry.SoundLevel.High);
        int fourth = (int)control.RandomFloat(entry.SoundLevel.Low, entry.SoundLevel.High);
        third.ShouldNotBe(fourth, "the control: the third and fourth numbers must give different soundlevels");

        dealt.SoundLevel.ShouldBe(third);
    }

    /// <remarks>
    /// The third emitted deal of a three-wave entry finds ONE wave available — `RandomInt( 0, 0 )` — and takes no number,
    /// so its soundlevel is its stream's third. The first two find three and two, and take one each.
    /// </remarks>
    [Test]
    public void Deal_TheLastCardOfAThreeWaveDeal_TakesNoWaveNumber()
    {
        SoundScriptEntry entry = Entry("Test.Three", "a.wav", "b.wav", "c.wav");
        ScriptWaveDeck deck = new();
        Dictionary<string, SoundScriptEntry> scripts = Scripts(entry);

        deck.Deal(ExplosionSounds.FromWorldAt(entry, Seeded(1), 1, Origin, emitted: true), scripts);
        deck.Deal(ExplosionSounds.FromWorldAt(entry, Seeded(2), 2, Origin, emitted: true), scripts);
        SceneSound last = deck.Deal(ExplosionSounds.FromWorldAt(entry, Seeded(3), 3, Origin, emitted: true), scripts);

        UniformRandomStream control = Seeded(3);
        control.RandomFloat(entry.Volume.Low, entry.Volume.High);
        control.RandomFloat(entry.Pitch.Low, entry.Pitch.High);
        int third = (int)control.RandomFloat(entry.SoundLevel.Low, entry.SoundLevel.High);
        int fourth = (int)control.RandomFloat(entry.SoundLevel.Low, entry.SoundLevel.High);
        third.ShouldNotBe(fourth, "the control: the third and fourth numbers must give different soundlevels");

        last.SoundLevel.ShouldBe(third);
    }

    /// <remarks>
    /// Three waves available: <c>maxAcceptable = 0x7fffffff - 2^31 % 3 = 0x7ffffffd</c>, so the generator's <c>0x7ffffffe</c>
    /// is refused and the NEXT number picks the wave; the soundlevel takes the one after.
    /// </remarks>
    [Test]
    public void Deal_ARefusedWaveNumber_IsDrawnAgain()
    {
        SoundScriptEntry entry = Entry("Test.Three", "a.wav", "b.wav", "c.wav");

        UniformRandomStream random = Seeded(RefusedFourthSeed);
        random.RandomFloat(0f, 1f);

        SceneSound dealt = new ScriptWaveDeck().Deal(ExplosionSounds.FromWorldAt(entry, random, 1, Origin, emitted: true), Scripts(entry));

        UniformRandomStream control = Seeded(RefusedFourthSeed);
        control.RandomFloat(0f, 1f);
        control.RandomFloat(entry.Volume.Low, entry.Volume.High);
        control.RandomFloat(entry.Pitch.Low, entry.Pitch.High);
        control.RandomInt(0, int.MaxValue - 1).ShouldBe(0x7ffffffe, "the condition: the wave's number is the refused one");

        UniformRandomStream unrefused = new();
        unrefused.MoveTo(control.Position);
        int wave = control.RandomInt(0, int.MaxValue - 1) % 3;
        int soundLevel = (int)control.RandomFloat(entry.SoundLevel.Low, entry.SoundLevel.High);
        ((int)unrefused.RandomFloat(entry.SoundLevel.Low, entry.SoundLevel.High)).ShouldNotBe(
            soundLevel, "the control: taking the refused number as the pick would give a different soundlevel");

        (dealt.Name, dealt.SoundLevel).ShouldBe((entry.Waves[wave], soundLevel));
    }

    /// <remarks>
    /// `ImpactCallback` (`tf_fx_impacts.cpp:51-134`) draws <c>RandomInt( 1, 10 )</c>, emits the shrapnel, then the impact —
    /// one stream. The shrapnel has two waves, but an earlier ricochet took one, so it finds ONE available and takes three
    /// numbers, not the four a full deck would: the impact's volume is the fifth.
    /// </remarks>
    [Test]
    public void Deal_AnImpactAfterAShrapnelWithOneWaveLeft_DrawsFromWhereTheShrapnelLeftTheStream()
    {
        SoundScriptEntry shrapnel = Entry(ImpactSounds.Shrapnel, "bounce1.wav", "bounce2.wav");
        SoundScriptEntry impact = Entry("Test.Impact", "x.wav", "y.wav");
        Dictionary<string, SoundScriptEntry> scripts = Scripts(shrapnel, impact);
        int seed = RicochetSeed();

        IReadOnlyList<SceneSound> sounds = ImpactSounds.For(new BulletLanding(5, Origin, impact.Name, Ricochets: true), seed, scripts);
        sounds.Count.ShouldBe(2, "the condition: the seed ricochets");

        ScriptWaveDeck deck = new();
        deck.Deal(ExplosionSounds.FromWorldAt(shrapnel, Seeded(99), 4, Origin, emitted: true), scripts);
        deck.Available(shrapnel.Name, 2).ShouldBe(1, "the condition: the earlier ricochet left one wave");
        deck.Deal(sounds[0], scripts);
        SceneSound dealt = deck.Deal(sounds[1], scripts);

        UniformRandomStream control = Seeded(seed);
        control.RandomInt(1, 10);
        control.RandomFloat(shrapnel.Volume.Low, shrapnel.Volume.High);
        control.RandomFloat(shrapnel.Pitch.Low, shrapnel.Pitch.High);
        control.RandomFloat(shrapnel.SoundLevel.Low, shrapnel.SoundLevel.High);
        float volume = control.RandomFloat(impact.Volume.Low, impact.Volume.High);
        int pitch = (int)control.RandomFloat(impact.Pitch.Low, impact.Pitch.High);
        string wave = impact.Waves[control.RandomInt(0, 1)];
        int soundLevel = (int)control.RandomFloat(impact.SoundLevel.Low, impact.SoundLevel.High);

        (dealt.Volume, dealt.Pitch, dealt.Name, dealt.SoundLevel).ShouldBe((volume, pitch, wave, soundLevel));
    }

    /// <remarks>
    /// `PhysFrictionSound` (`physics.cpp:976-1022`) draws `GetParametersForSound` for the scrape, then `SoundCreate` →
    /// `CSoundPatch::Init` (`soundenvelope.cpp:353-381`) draws again on the same stream and keeps that draw's script volume
    /// and soundlevel; `Play( patch, params.volume · v, params.pitch )`. The scrape has two waves, but an emitted sound of
    /// the same script took one, so the first draw — a read, never a sound of its own — finds ONE available and takes three
    /// numbers, not four. <c>v = (7750 / 15500)² = 0.25</c>.
    /// </remarks>
    [Test]
    public void Deal_AScrapesPatch_DrawsFromWhereTheScrapesOwnDrawLeftTheStream()
    {
        const int corpse = 40;
        SoundScriptEntry scrape = Entry("Flesh.ScrapeRough", "physics/flesh/rough1.wav", "physics/flesh/rough2.wav") with
        {
            Volume = new SoundRange(0.5f, 1f),
        };
        Dictionary<string, SoundScriptEntry> scripts = Scripts(scrape);

        VphysicsSurfaceProps surfaces = new([]);
        surfaces.ParseSurfaceData(Encoding.Latin1.GetBytes(
            "\"default\" { \"scraperough\" \"Stone.ScrapeRough\" } \"flesh\" { \"scraperough\" \"Flesh.ScrapeRough\" }"));

        CorpseFriction friction = new(corpse, 7750f, surfaces.GetSurfaceIndex("flesh"), surfaces.GetSurfaceIndex("default"));
        SceneSound loop = new PhysicsFrictionSounds().Step(100, 1.0, [friction], static (entity, tick) => (entity, tick, 0f), surfaces, scripts).Single();

        ScriptWaveDeck deck = new();
        deck.Deal(ExplosionSounds.FromWorldAt(scrape, Seeded(99), 90, Origin, emitted: true), scripts);
        deck.Available(scrape.Name, 2).ShouldBe(1, "the condition: the emitted sound left one wave");
        SceneSound dealt = deck.Deal(loop, scripts);

        UniformRandomStream control = EntitySounds.Stream(100, corpse);
        float scrapeVolume = control.RandomFloat(scrape.Volume.Low, scrape.Volume.High);
        int scrapePitch = (int)control.RandomFloat(scrape.Pitch.Low, scrape.Pitch.High);
        control.RandomFloat(scrape.SoundLevel.Low, scrape.SoundLevel.High);
        float scriptVolume = control.RandomFloat(scrape.Volume.Low, scrape.Volume.High);
        control.RandomFloat(scrape.Pitch.Low, scrape.Pitch.High);
        int soundLevel = (int)control.RandomFloat(scrape.SoundLevel.Low, scrape.SoundLevel.High);

        // `ENERGY_VOLUME_SCALE` and the square, as `PhysFrictionSound` computes them in float.
        float energy = 7750f * (1f / 15500f);

        (dealt.Volume, dealt.Pitch, dealt.SoundLevel).ShouldBe((scrapeVolume * (energy * energy) * scriptVolume, scrapePitch, soundLevel));
    }

    /// <remarks>
    /// A chain's head is found however many deals land between it and its follower: the impact starts where its
    /// shrapnel's DEALT draw ended (two waves, so a number) and clears nothing more — the shrapnel's own deal cleared one,
    /// so one of two is left.
    /// </remarks>
    [Test]
    public void Deal_AnUnrelatedDealBetweenAShrapnelAndItsImpact_LeavesTheImpactOnTheShrapnelsDealtCount()
    {
        SoundScriptEntry shrapnel = Entry(ImpactSounds.Shrapnel, "bounce1.wav", "bounce2.wav");
        SoundScriptEntry impact = Entry("Test.Impact", "x.wav", "y.wav");
        SoundScriptEntry other = Entry("Test.Other", "o.wav");
        Dictionary<string, SoundScriptEntry> scripts = Scripts(shrapnel, impact, other);
        int seed = RicochetSeed();

        IReadOnlyList<SceneSound> sounds = ImpactSounds.For(new BulletLanding(5, Origin, impact.Name, Ricochets: true), seed, scripts);

        ScriptWaveDeck deck = new();
        deck.Deal(sounds[0], scripts);
        deck.Deal(ExplosionSounds.FromWorldAt(other, Seeded(99), 5, Origin, emitted: true), scripts);
        SceneSound dealt = deck.Deal(sounds[1], scripts);

        UniformRandomStream control = Seeded(seed);
        control.RandomInt(1, 10);
        control.RandomFloat(shrapnel.Volume.Low, shrapnel.Volume.High);
        control.RandomFloat(shrapnel.Pitch.Low, shrapnel.Pitch.High);
        control.RandomInt(0, 1);
        control.RandomFloat(shrapnel.SoundLevel.Low, shrapnel.SoundLevel.High);
        float volume = control.RandomFloat(impact.Volume.Low, impact.Volume.High);
        int pitch = (int)control.RandomFloat(impact.Pitch.Low, impact.Pitch.High);

        (dealt.Volume, dealt.Pitch, deck.Available(shrapnel.Name, 2), deck.HeadsHeld).ShouldBe((volume, pitch, 1, 0));
    }

    /// <remarks>
    /// No producer chains three draws — an impact follows its shrapnel, a patch's `Init` follows the scrape's read — and the
    /// deck's head records assume two, so a third link is refused where it is made.
    /// </remarks>
    [Test]
    public void After_AChainOfThree_IsRefused()
    {
        ScriptWaveDraw head = new("Test.A", Emitted: true, new RandomStreamPosition(1, 0));
        ScriptWaveDraw second = new("Test.B", Emitted: true, head.Stream) { After = head };

        Should.Throw<ArgumentException>(() => new ScriptWaveDraw("Test.C", Emitted: true, head.Stream) { After = second });
    }

    /// <remarks>
    /// When the flags reset, what was dealt against them goes too: a chained impact dealt after the reset replays its
    /// shrapnel against the fresh flags (two waves, so a number) rather than the one wave left when it was dealt.
    /// </remarks>
    [Test]
    public void Deal_AfterTheFlagsReset_AChainedImpactReplaysItsShrapnelAgainstTheFreshFlags()
    {
        SoundScriptEntry shrapnel = Entry(ImpactSounds.Shrapnel, "bounce1.wav", "bounce2.wav");
        SoundScriptEntry impact = Entry("Test.Impact", "x.wav", "y.wav");
        Dictionary<string, SoundScriptEntry> scripts = Scripts(shrapnel, impact);
        int seed = RicochetSeed();

        IReadOnlyList<SceneSound> sounds = ImpactSounds.For(new BulletLanding(5, Origin, impact.Name, Ricochets: true), seed, scripts);

        ScriptWaveDeck deck = new();
        deck.Deal(ExplosionSounds.FromWorldAt(shrapnel, Seeded(99), 4, Origin, emitted: true), scripts);
        deck.Deal(sounds[0], scripts);
        deck.Clear();
        SceneSound dealt = deck.Deal(sounds[1], scripts);

        UniformRandomStream control = Seeded(seed);
        control.RandomInt(1, 10);
        control.RandomFloat(shrapnel.Volume.Low, shrapnel.Volume.High);
        control.RandomFloat(shrapnel.Pitch.Low, shrapnel.Pitch.High);
        control.RandomInt(0, 1);
        control.RandomFloat(shrapnel.SoundLevel.Low, shrapnel.SoundLevel.High);
        float volume = control.RandomFloat(impact.Volume.Low, impact.Volume.High);
        int pitch = (int)control.RandomFloat(impact.Pitch.Low, impact.Pitch.High);

        (dealt.Volume, dealt.Pitch).ShouldBe((volume, pitch));
    }

    /// <remarks>
    /// Only a draw another follows (a shrapnel before its impact) is held for it, and only a patch a change or stop will
    /// name is held for that: a hundred one-shots hold nothing.
    /// </remarks>
    [Test]
    public void Deal_AHundredOneShots_HoldsNothing()
    {
        SoundScriptEntry entry = Entry("Test.Three", "a.wav", "b.wav", "c.wav");
        ScriptWaveDeck deck = new();

        for (int seed = 1; seed <= 100; seed++)
        {
            deck.Deal(ExplosionSounds.FromWorldAt(entry, Seeded(seed), seed, Origin, emitted: true), Scripts(entry));
        }

        (deck.HeadsHeld, deck.PatchesHeld).ShouldBe((0, 0));
    }

    /// <remarks>
    /// The medigun's detach is a patch nothing ever stops or changes — the next heal start destroys it,
    /// `StopHealSound( false, false, true )` (`tf_weapon_medigun.cpp:2325`) — so a hundred heals' detaches hold nothing.
    /// The heal loops, each stopped, hold nothing either once stopped.
    /// </remarks>
    [Test]
    public void Deal_AHundredMedigunDetaches_HoldNothing()
    {
        Dictionary<string, SoundScriptEntry> scripts = Scripts(
            Entry("WeaponMedigun.HealingWorld", "heal.wav"), Entry("WeaponMedigun.HealingDetachWorld", "detach.wav"));
        SceneHealBeam[] beams = [.. Enumerable.Range(0, 100).Select(static heal => new SceneHealBeam(40, 3, false, 2, heal * 100, (heal * 100) + 50) { Owner = 7 })];
        ScriptWaveDeck deck = new();

        foreach (SceneSound sound in MedigunSounds.For(beams, static (_, _) => (0f, 0f, 0f), scripts))
        {
            if (sound.IsStop)
            {
                deck.Named(sound);
            }
            else
            {
                deck.Deal(sound, scripts);
            }
        }

        deck.PatchesHeld.ShouldBe(0);
    }

    /// <remarks>A patch is held while it plays — its change is named against it — and forgotten at its stop.</remarks>
    [Test]
    public void Named_APatchsStop_ForgetsIt()
    {
        SoundScriptEntry entry = Entry("Test.Patch", "loop.wav");
        SceneSound patch = ExplosionSounds.FromWorldAt(entry, Seeded(7), 1, Origin, emitted: false, scaled: true, kept: true);
        ScriptWaveDeck deck = new();

        deck.Deal(patch, Scripts(entry));
        deck.Named(patch with { Tick = 2, ChangesVolume = true });
        deck.PatchesHeld.ShouldBe(1, "the change leaves it held");

        deck.Named(patch with { Tick = 3, IsStop = true });
        deck.PatchesHeld.ShouldBe(0);
    }

    /// <remarks>
    /// A reload's `S_StopAllSounds` (B550) ends every patch, so nothing can name one after it, and a chain cannot span it;
    /// the flags, which live in soundemittersystem.dll, stay (B504).
    /// </remarks>
    [Test]
    public void ForgetPlaying_AfterAPatchAndAChainHead_HoldsNothingButKeepsTheFlags()
    {
        SoundScriptEntry entry = Entry("Test.Three", "a.wav", "b.wav", "c.wav");
        ScriptWaveDeck deck = new();
        deck.Deal(ExplosionSounds.FromWorldAt(entry, Seeded(7), 1, Origin, emitted: false, scaled: true, kept: true), Scripts(entry));
        deck.Deal(ExplosionSounds.FromWorldAt(entry, Seeded(8), 2, Origin, emitted: true, leads: true), Scripts(entry));
        (deck.HeadsHeld, deck.PatchesHeld).ShouldBe((1, 1), "the condition: both are held");

        deck.ForgetPlaying();

        (deck.HeadsHeld, deck.PatchesHeld, deck.Available(entry.Name, 3)).ShouldBe((0, 0, 2));
    }

    /// <remarks>
    /// The shrapnel before an impact was EMITTED (`EmitSound` → `EmitSoundByHandle`, `tf_fx_impacts.cpp:88`), so its
    /// `GetParametersForSound` cleared the wave it picked (FUN_180003370, `isbeingemitted`). When the impact is dealt
    /// without its shrapnel having been, the replay that finds where the impact starts IS that emit: it deals the
    /// shrapnel's wave as the engine's would, leaving one of two.
    /// </remarks>
    [Test]
    public void Deal_AnImpactWhoseShrapnelWasNeverDealt_ClearsTheWaveTheShrapnelsEmitPicks()
    {
        SoundScriptEntry shrapnel = Entry(ImpactSounds.Shrapnel, "bounce1.wav", "bounce2.wav");
        SoundScriptEntry impact = Entry("Test.Impact", "x.wav", "y.wav");
        Dictionary<string, SoundScriptEntry> scripts = Scripts(shrapnel, impact);

        IReadOnlyList<SceneSound> sounds = ImpactSounds.For(new BulletLanding(5, Origin, impact.Name, Ricochets: true), RicochetSeed(), scripts);
        sounds.Count.ShouldBe(2, "the condition: the seed ricochets");

        ScriptWaveDeck deck = new();
        deck.Deal(sounds[1], scripts);

        deck.Available(shrapnel.Name, 2).ShouldBe(1);
    }

    /// <remarks>
    /// A patch made with no draw before it — a medigun's `SoundCreate` (`tf_weapon_medigun.cpp:2061`) — is played by
    /// `Play( patch, volume, pitch )` all the same: `GetVolumeForEngine` multiplies the volume `Play` gave by the script
    /// volume `CSoundPatch::Init` drew (`soundenvelope.cpp:353-381`), and the pitch is `Play`'s.
    /// </remarks>
    [Test]
    public void Deal_AScaledFirstDraw_MultipliesItsVolumeByTheScriptVolumeItDraws()
    {
        SoundScriptEntry entry = Entry("Test.Patch", "loop.wav");
        SceneSound patch = ExplosionSounds.FromWorldAt(entry, Seeded(7), 1, Origin, emitted: false, scaled: true) with
        {
            Volume = 0.5f,
            Pitch = 100,
        };

        SceneSound dealt = new ScriptWaveDeck().Deal(patch, Scripts(entry));

        float scriptVolume = Seeded(7).RandomFloat(entry.Volume.Low, entry.Volume.High);
        (dealt.Volume, dealt.Pitch).ShouldBe((0.5f * scriptVolume, 100));
    }

    /// <remarks>
    /// A deal moves its own stream to the sound's position, so dealing the same sound again — the skip of a seek back —
    /// draws the same numbers against whatever the flags then hold.
    /// </remarks>
    [Test]
    public void Deal_TheSameSoundTwice_DrawsTheSameNumbers()
    {
        SoundScriptEntry entry = Entry("Test.One", "one.wav");
        SceneSound sound = ExplosionSounds.FromWorldAt(entry, Seeded(7), 1, Origin, emitted: true);
        ScriptWaveDeck deck = new();

        deck.Deal(sound, Scripts(entry)).ShouldBe(deck.Deal(sound, Scripts(entry)));
    }

    /// <remarks>
    /// The same sound dealt twice on a three-wave entry: the same numbers each time, against the flags each time — three
    /// waves, then the two the first left. Both take one wave number, so the soundlevel is the fourth number both times.
    /// </remarks>
    [Test]
    public void Deal_TheSameMultiWaveSoundTwice_DrawsTheSameNumbersAgainstTheFlagsThen()
    {
        SoundScriptEntry entry = Entry("Test.Three", "a.wav", "b.wav", "c.wav");
        SceneSound sound = ExplosionSounds.FromWorldAt(entry, Seeded(7), 1, Origin, emitted: true);
        ScriptWaveDeck deck = new();

        SceneSound first = deck.Deal(sound, Scripts(entry));
        SceneSound second = deck.Deal(sound, Scripts(entry));

        int firstWave = Numbers(entry, available: 3).Wave;
        (int secondPlace, int soundLevel) = Numbers(entry, available: 2);
        string[] left = [.. entry.Waves.Where((_, index) => index != firstWave)];

        // The control: the soundlevel range is wide enough that the third number — what a deal taking no wave number would
        // read — gives a different soundlevel, so the assertion can fail.
        Numbers(entry, available: 1).SoundLevel.ShouldNotBe(soundLevel);

        (first.Name, first.SoundLevel, second.Name, second.SoundLevel).ShouldBe(
            (entry.Waves[firstWave], soundLevel, left[secondPlace], soundLevel));
    }

    /// <remarks>
    /// A patch's change whose start was never dealt — dropped by a seek, say — names nothing: the engine's
    /// `SoundChangeVolume` finds no such patch playing, so the change is left as its producer carried it.
    /// </remarks>
    [Test]
    public void Named_AChangeOfAPatchWhoseStartWasNeverDealt_IsLeftAsCarried()
    {
        SoundScriptEntry entry = Entry("Test.Patch", "loop.wav");
        SceneSound change = ExplosionSounds.FromWorldAt(entry, Seeded(7), 1, Origin, emitted: false, scaled: true) with
        {
            Volume = 0.5f,
            ChangesVolume = true,
        };

        new ScriptWaveDeck().Named(change).ShouldBe(change);
    }

    /// <summary>Seed 7's wave place and soundlevel over an entry with <paramref name="available"/> waves left.</summary>
    private static (int Wave, int SoundLevel) Numbers(SoundScriptEntry entry, int available)
    {
        UniformRandomStream control = Seeded(7);
        control.RandomFloat(entry.Volume.Low, entry.Volume.High);
        control.RandomFloat(entry.Pitch.Low, entry.Pitch.High);
        int wave = control.RandomInt(0, available - 1);
        return (wave, (int)control.RandomFloat(entry.SoundLevel.Low, entry.SoundLevel.High));
    }

    /// <summary>The first seed whose <c>RandomInt( 1, 10 )</c> is at most 3 — `ImpactCallback`'s ricochet.</summary>
    private static int RicochetSeed()
    {
        for (int seed = 1; seed < 100; seed++)
        {
            if (Seeded(seed).RandomInt(1, 10) <= 3)
            {
                return seed;
            }
        }

        throw new InvalidOperationException("no seed under 100 ricochets");
    }

    private static UniformRandomStream Seeded(int seed)
    {
        UniformRandomStream random = new();
        random.SetSeed(seed);
        return random;
    }

    /// <summary>An entry whose every value is drawn from a real range, so each number taken shows.</summary>
    private static SoundScriptEntry Entry(string name, params string[] waves) =>
        new(name, 0, new SoundRange(0.25f, 1f), new SoundRange(50f, 150f), new SoundRange(60f, 140f), waves);

    private static Dictionary<string, SoundScriptEntry> Scripts(params SoundScriptEntry[] entries) =>
        entries.ToDictionary(static entry => entry.Name, StringComparer.OrdinalIgnoreCase);
}
