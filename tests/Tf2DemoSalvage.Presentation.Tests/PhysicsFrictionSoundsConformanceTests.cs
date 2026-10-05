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

        SceneSound loop = played.ShouldHaveSingleItem();
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

        // The patch keeps the script volume it was made with: `m_flScriptVolume · m_volume` = 0.8 · 0.8 · 0.0625.
        changed.ShouldBe(loop with { Tick = 135, Volume = changed.Volume, Pitch = 91, ChangesVolume = true, ChangesPitch = true });
        changed.Volume.ShouldBe(0.04f, 1e-6f);
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
