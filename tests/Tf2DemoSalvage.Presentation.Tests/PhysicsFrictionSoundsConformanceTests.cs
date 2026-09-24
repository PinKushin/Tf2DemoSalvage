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
    public void Step_ALoudScrape_StartsTheRoughLoopOnChanBodyFromTheCorpse()
    {
        PhysicsFrictionSounds sounds = new();

        IReadOnlyList<SceneSound> played = sounds.Step(100, 1.0, [Scrape(7750f)], At, Surfaces, Scripts());

        SceneSound loop = played.ShouldHaveSingleItem();
        loop.Name.ShouldBe("physics/flesh/rough.wav");
        loop.EntityIndex.ShouldBe(Corpse);
        loop.Channel.ShouldBe(4);
        loop.Volume.ShouldBe(0.2f, 1e-6f);
        loop.IsStop.ShouldBeFalse();
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

    private static Dictionary<string, SoundScriptEntry> Scripts() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["Flesh.ScrapeRough"] = Entry("Flesh.ScrapeRough", "physics/flesh/rough.wav"),
        ["Flesh.ScrapeSmooth"] = Entry("Flesh.ScrapeSmooth", "physics/flesh/smooth.wav"),
        ["Stone.ScrapeRough"] = Entry("Stone.ScrapeRough", "physics/stone/rough.wav"),
        ["Stone.ScrapeSmooth"] = Entry("Stone.ScrapeSmooth", "physics/stone/smooth.wav"),
    };

    private static SoundScriptEntry Entry(string name, string wave) =>
        new(name, 1, new SoundRange(0.8f, 0.8f), new SoundRange(100f, 100f), 75, [wave]);
}
