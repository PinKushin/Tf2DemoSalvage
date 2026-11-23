using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// The client's scrape loop: `CCollisionEvent::Friction` (`game/client/physics.cpp:661`), `PhysFrictionSound`
/// (`physics_shared.cpp:982` and `physics.cpp:976`) and `UpdateFrictionSounds` (`physics.cpp:719`).
/// </summary>
/// <remarks>
/// The script's volume is 0.8 throughout, so a start needs `0.8 · (energy / 15500)²` above 0.1. An energy of 7750 gives
/// `0.8 · 0.25 = 0.2`.
/// </remarks>
public sealed class PhysicsFrictionSoundsConformanceTests
{
    private const int Corpse = 40;

    private static readonly VphysicsSurfaceProps Surfaces = Parse(
        "\"default\" { \"scraperough\" \"Stone.ScrapeRough\" \"scrapesmooth\" \"Stone.ScrapeSmooth\" \"audioroughnessfactor\" \"1\" " +
        "\"scraperoughthreshold\" \"0.5\" } " +
        "\"flesh\" { \"scraperough\" \"Flesh.ScrapeRough\" \"scrapesmooth\" \"Flesh.ScrapeSmooth\" \"audioroughnessfactor\" \"0.1\" } " +
        "\"sky\" { \"gamematerial\" \"X\" }");

    [Test]
    public void Step_ALoudScrape_StartsTheRoughLoopOnTheScriptsChannelFromTheCorpse()
    {
        // `SoundCreate( filter, entindex, CHAN_BODY, name, params.soundlevel )` → `CSoundPatch::Init`
        // (`soundenvelope.cpp:353-381`) replaces the channel with the script's own, keeps the script volume, and `Play`
        // multiplies it by `params.volume · v` from `PhysFrictionSound`'s own draw: 0.8 · 0.8 · 0.25.
        PhysicsFrictionSounds sounds = new();

        IReadOnlyList<SceneSound> played = sounds.Step(100, 1.0, [Scrape(7750f)], At, Surfaces, Scripts());

        // The patch's script volume is its own draw's, which is drawn when it plays (B782).
        SceneSound loop = new ScriptWaveDeck().Deal(played.ShouldHaveSingleItem(), Scripts()).ShouldNotBeNull();
        loop.Name.ShouldBe("physics/flesh/rough.wav");
        loop.EntityIndex.ShouldBe(Corpse);
        loop.Channel.ShouldBe(1, "the script's channel, not the CHAN_BODY the caller asked for");
        loop.Volume.ShouldBe(0.16f, 1e-6f);
        loop.IsStop.ShouldBeFalse();
    }

    /// <remarks>
    /// Two draws, in this order: `PhysFrictionSound`'s `GetParametersForSound` (`physics.cpp:991`), whose pitch `Play`
    /// starts the patch at, then `CSoundPatch::Init`'s (`soundenvelope.cpp:361`), whose wave and soundlevel the patch
    /// plays.
    /// </remarks>
    [Test]
    public void Step_ALoudScrape_TakesPitchFromTheFirstDrawAndSoundLevelFromThePatchs()
    {
        Dictionary<string, SoundScriptEntry> scripts = Scripts(pitch: new SoundRange(50f, 150f));
        scripts["Flesh.ScrapeRough"] = scripts["Flesh.ScrapeRough"] with { SoundLevel = new SoundRange(60f, 90f) };
        SoundScriptEntry entry = scripts["Flesh.ScrapeRough"];

        Tf2DemoSalvage.Core.Primitives.UniformRandomStream random = EntitySounds.Stream(100, Corpse);
        SceneSound first = ExplosionSounds.FromWorldAt(entry, random, 100, At(Corpse, 100), emitted: false);
        SceneSound patch = ExplosionSounds.FromWorldAt(entry, random, 100, At(Corpse, 100), emitted: false);
        first.SoundLevel.ShouldNotBe(patch.SoundLevel, "the control: the two draws must differ for the test to tell them apart");

        SceneSound loop = new PhysicsFrictionSounds().Step(100, 1.0, [Scrape(7750f)], At, Surfaces, scripts).Single();

        (loop.Pitch, loop.SoundLevel).ShouldBe((first.Pitch, patch.SoundLevel));
    }

    [Test]
    public void Step_UnderSeventyFiveEnergy_PlaysNothing() =>
        new PhysicsFrictionSounds().Step(100, 1.0, [Scrape(74f)], At, Surfaces, Scripts()).ShouldBeEmpty();

    [Test]
    public void Step_ATenthOrQuieter_DoesNotStart()
    {
        // 0.8 · (5000 / 15500)² = 0.083.
        new PhysicsFrictionSounds().Step(100, 1.0, [Scrape(5000f)], At, Surfaces, Scripts()).ShouldBeEmpty();
    }

    [Test]
    public void Step_ASmootherSurfaceHit_PlaysTheSmoothLoop()
    {
        // Flesh's roughness 0.1 is under stone's threshold 0.5.
        IReadOnlyList<SceneSound> played =
            new PhysicsFrictionSounds().Step(100, 1.0, [Scrape(7750f, sliding: "default", on: "flesh")], At, Surfaces, Scripts());

        played.ShouldHaveSingleItem().Name.ShouldBe("physics/stone/smooth.wav");
    }

    [Test]
    public void Step_ASurfaceMarkedX_PlaysNothing() =>
        new PhysicsFrictionSounds().Step(100, 1.0, [Scrape(7750f, on: "sky")], At, Surfaces, Scripts()).ShouldBeEmpty();

    [Test]
    public void Step_NoUpdateForMoreThanATenth_StopsTheLoop()
    {
        PhysicsFrictionSounds sounds = new();
        SceneSound loop = sounds.Step(100, 1.0, [Scrape(7750f)], At, Surfaces, Scripts()).Single();

        sounds.Step(105, 1.05, [], At, Surfaces, Scripts()).ShouldBeEmpty();
        sounds.Step(107, 1.11, [], At, Surfaces, Scripts()).ShouldBe([loop with { Tick = 107, IsStop = true }]);
    }

    [Test]
    public void Step_AnyFrictionWithinHalfASecond_KeepsTheLoopWithoutRestartingIt()
    {
        // The multiplayer gate: inside half a second of the last effect, a report only marks the loop as updated, whatever
        // its energy.
        PhysicsFrictionSounds sounds = new();
        sounds.Step(100, 1.0, [Scrape(7750f)], At, Surfaces, Scripts());

        sounds.Step(105, 1.08, [Scrape(1f)], At, Surfaces, Scripts()).ShouldBeEmpty();
        sounds.Step(107, 1.15, [], At, Surfaces, Scripts()).ShouldBeEmpty();
    }

    [Test]
    public void Step_AScrapeAfterHalfASecond_ChangesThePlayingLoopsVolumeAndPitch()
    {
        // `SoundChangeVolume( patch, params.volume · v )` and `SoundChangePitch( patch, v · (high − low) + low )`: with the
        // script's pitch 90..110 and v = (3875 / 15500)² = 0.0625, the pitch is 91.25, sent as the engine's whole percent.
        PhysicsFrictionSounds sounds = new();
        SceneSound loop = sounds.Step(100, 1.0, [Scrape(7750f)], At, Surfaces, Scripts(pitch: new SoundRange(90f, 110f))).Single();

        SceneSound changed = sounds.Step(135, 1.51, [Scrape(3875f)], At, Surfaces, Scripts(pitch: new SoundRange(90f, 110f))).Single();

        // The patch keeps the script volume it was made with: `m_flScriptVolume · m_volume` = 0.8 · 0.8 · 0.0625 — the start's
        // deal supplies the first, which the change names (B782).
        changed.ShouldBe(loop with { Tick = 135, Volume = changed.Volume, Pitch = 91, ChangesVolume = true, ChangesPitch = true });

        ScriptWaveDeck deck = new();
        deck.Deal(loop, Scripts());
        deck.Named(changed).Volume.ShouldBe(0.04f, 1e-6f);
    }

    /// <remarks>
    /// The change's `params.volume` is `PhysFrictionSound`'s own draw at the CHANGE's tick — the first number of that tick's
    /// stream. A ranged script volume so that a stream seeded for any other tick or entity gives another value (the
    /// control), unlike the fixed 0.8 above.
    /// </remarks>
    [Test]
    public void Step_AScrapeAfterHalfASecondWithARangedVolume_ChangesByTheChangeTicksFirstNumber()
    {
        Dictionary<string, SoundScriptEntry> scripts = Scripts();
        scripts["Flesh.ScrapeRough"] = scripts["Flesh.ScrapeRough"] with { Volume = new SoundRange(0.5f, 1f) };
        PhysicsFrictionSounds sounds = new();
        sounds.Step(100, 1.0, [Scrape(7750f)], At, Surfaces, scripts).ShouldHaveSingleItem();

        SceneSound changed = sounds.Step(135, 1.51, [Scrape(3875f)], At, Surfaces, scripts).Single();

        float energy = 3875f * (1f / 15500f);
        float scriptVolume = EntitySounds.Stream(135, Corpse).RandomFloat(0.5f, 1f);
        EntitySounds.Stream(100, Corpse).RandomFloat(0.5f, 1f).ShouldNotBe(scriptVolume, "the control: the start tick's stream differs");
        EntitySounds.Stream(135, Corpse + 1).RandomFloat(0.5f, 1f).ShouldNotBe(scriptVolume, "the control: another entity's stream differs");

        changed.Volume.ShouldBe(scriptVolume * (energy * energy));
    }

    /// <remarks>
    /// A steady scrape changes its loop every half second, and that change needs only `PhysFrictionSound`'s script volume —
    /// the first number of its draw. Nothing else is built for it: what the change allocates is the list `Step` returns,
    /// predicted by a control list of one sound made the same way. The least of five changes, so a runtime allocation
    /// on one call (a tiered JIT, a resize elsewhere) cannot decide it.
    /// </remarks>
    [Test]
    public void Step_ASteadyScrapesChange_AllocatesOnlyTheListItReturns()
    {
        PhysicsFrictionSounds sounds = new();
        Dictionary<string, SoundScriptEntry> scripts = Scripts();
        Func<int, int, (float X, float Y, float Z)> at = At;
        CorpseFriction[] scrape = [Scrape(7750f)];
        sounds.Step(100, 1.0, scrape, at, Surfaces, scripts).ShouldHaveSingleItem();

        long least = long.MaxValue;
        long control = long.MaxValue;

        for (int change = 1; change <= 5; change++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            IReadOnlyList<SceneSound> changed = sounds.Step(100 + (change * 35), 1.0 + (change * 0.51), scrape, at, Surfaces, scripts);
            least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
            changed.ShouldHaveSingleItem().ChangesVolume.ShouldBeTrue("the condition: each call is the change path");

            before = GC.GetAllocatedBytesForCurrentThread();
            List<SceneSound> one = [];
            one.Add(changed[0]);
            control = Math.Min(control, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        least.ShouldBe(control);
    }

    [Test]
    public void Step_ANinthCorpse_FindsNoSlot()
    {
        PhysicsFrictionSounds sounds = new();
        CorpseFriction[] nine = [.. Enumerable.Range(1, 9).Select(entity => Scrape(7750f) with { Entity = entity })];

        sounds.Step(100, 1.0, nine, At, Surfaces, Scripts()).Count.ShouldBe(8);
    }

    private static (float X, float Y, float Z) At(int entity, int tick) => (entity, tick, 0f);

    private static CorpseFriction Scrape(float energy, string sliding = "flesh", string on = "default") =>
        new(Corpse, energy, Surfaces.GetSurfaceIndex(sliding), Surfaces.GetSurfaceIndex(on));

    private static VphysicsSurfaceProps Parse(string text)
    {
        VphysicsSurfaceProps props = new([]);
        props.ParseSurfaceData(Encoding.Latin1.GetBytes(text));
        return props;
    }

    private static Dictionary<string, SoundScriptEntry> Scripts(SoundRange? pitch = null) => new(StringComparer.OrdinalIgnoreCase)
    {
        ["Flesh.ScrapeRough"] = Entry("Flesh.ScrapeRough", "physics/flesh/rough.wav", pitch),
        ["Flesh.ScrapeSmooth"] = Entry("Flesh.ScrapeSmooth", "physics/flesh/smooth.wav", pitch),
        ["Stone.ScrapeRough"] = Entry("Stone.ScrapeRough", "physics/stone/rough.wav", pitch),
        ["Stone.ScrapeSmooth"] = Entry("Stone.ScrapeSmooth", "physics/stone/smooth.wav", pitch),
    };

    private static SoundScriptEntry Entry(string name, string wave, SoundRange? pitch) =>
        new(name, 1, new SoundRange(0.8f, 0.8f), pitch ?? new SoundRange(100f, 100f), new SoundRange(75f, 75f), [wave]);
}
