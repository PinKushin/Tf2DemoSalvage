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
