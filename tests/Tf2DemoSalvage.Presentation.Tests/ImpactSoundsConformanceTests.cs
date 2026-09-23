using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>`ImpactCallback`'s sounds (`tf_fx_impacts.cpp:51-134`): the surface's bullet impact within 1024 of the camera (B415).</summary>
public sealed class ImpactSoundsConformanceTests
{
    [Test]
    public void For_ALanding_IsItsSurfacesSoundGatedTo1024()
    {
        SceneSound sound = ImpactSounds.For(
            [new BulletLanding(50, (1f, 2f, 3f), "Concrete.BulletImpact", Ricochets: false)],
            Scripts("Concrete.BulletImpact", "Bounce.Shrapnel")).ShouldHaveSingleItem();

        sound.Name.ShouldBe("Concrete.BulletImpact.wav");
        sound.Tick.ShouldBe(50);
        (sound.OriginX, sound.OriginY, sound.OriginZ).ShouldBe((1f, 2f, 3f));
        sound.AudibleWithin.ShouldBe(1024f);
    }

    [Test]
    public void For_ADmgBulletLanding_RicochetsThreeTimesInTen()
    {
        // `random->RandomInt( 1, 10 ) <= 3`, offered only to exactly `DMG_BULLET`.
        BulletLanding[] landings = [.. Enumerable.Range(0, 1000).Select(tick => new BulletLanding(tick, default, null, Ricochets: true))];

        int bounces = ImpactSounds.For(landings, Scripts("Bounce.Shrapnel")).Count;

        bounces.ShouldBeInRange(240, 360, "three in ten of a thousand");
        ImpactSounds.For([new BulletLanding(1, default, null, Ricochets: false)], Scripts("Bounce.Shrapnel")).ShouldBeEmpty();
    }

    [Test]
    public void For_OneLandingAndItsSeed_MatchesThatLandingInAList()
    {
        // The list form seeds landing `i` at FirstSeed + i; one landing given that seed draws the same sounds.
        BulletLanding[] landings = [.. Enumerable.Range(0, 20).Select(tick => new BulletLanding(tick, default, "Concrete.BulletImpact", Ricochets: true))];
        Dictionary<string, SoundScriptEntry> scripts = Scripts("Concrete.BulletImpact", "Bounce.Shrapnel");

        SceneSound[] single = [.. landings.SelectMany((landing, index) => ImpactSounds.For(landing, ImpactSounds.SeedFor(index), scripts))];

        single.ShouldBe(ImpactSounds.For(landings, scripts));
    }

    [Test]
    public void InRange_ASoundGatedTo1024_PlaysNearerAndNotAtOrBeyond()
    {
        // `( MainViewOrigin() - vecOrigin ).LengthSqr() < 1024 * 1024`, strictly.
        SceneSound gated = new SceneSound(1, "a", -1, 0, 0, 1f, 75, 100, 0f, 0f, 0f, 0f) { AudibleWithin = 1024f };

        SoundPresenter.InRange(gated, (1023f, 0f, 0f)).ShouldBeTrue();
        SoundPresenter.InRange(gated, (1024f, 0f, 0f)).ShouldBeFalse();
        SoundPresenter.InRange(gated with { AudibleWithin = 0f }, (99999f, 0f, 0f)).ShouldBeTrue();
    }

    private static Dictionary<string, SoundScriptEntry> Scripts(params string[] names) =>
        names.ToDictionary(
            name => name,
            name => new SoundScriptEntry(name, 1, new SoundRange(1f, 1f), new SoundRange(100f, 100f), 75, [name + ".wav"]),
            StringComparer.OrdinalIgnoreCase);
}
