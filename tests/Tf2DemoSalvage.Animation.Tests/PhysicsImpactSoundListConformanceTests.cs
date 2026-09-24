using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// `physicssound::AddImpactSound` (`vphysics_sound.h:82`), which `CPhysicsSystem::PhysicsSimulate` fills across a whole client
/// FRAME and plays once at its end (`game/client/physics.cpp:473`) — so every impact of the frame's ticks merges together.
/// </summary>
/// <remarks>
/// Speeds are squared inches a second, as `ObjectSound` passes them, and chosen at 10,000 and above so the `+1e-4` that
/// `AddImpactSound` adds is below a float's resolution there and the expected values are exact.
/// </remarks>
public sealed class PhysicsImpactSoundListConformanceTests
{
    [Test]
    public void Add_OneImpact_AddsTheSmallSpeedBias()
    {
        List<PhysicsImpactSound> list = [];

        PhysicsImpactSoundList.Add(list, Impact(surface: 3, volume: 0.25f, speed: 0f, x: 1f));

        list[0].ImpactSpeed.ShouldBe(1e-4f);
    }

    [Test]
    public void Add_TheSameSurfaceTwice_MergesVolumeAndKeepsTheFasterSpeed()
    {
        List<PhysicsImpactSound> list = [];

        PhysicsImpactSoundList.Add(list, Impact(surface: 3, volume: 0.25f, speed: 90000f, x: 1f));
        PhysicsImpactSoundList.Add(list, Impact(surface: 3, volume: 0.5f, speed: 40000f, x: 2f));

        // The louder one moves the sound to where it happened; the volumes sum and the speed is the larger.
        list.ShouldBe([new PhysicsImpactSound(3, 30, 0.75f, 90000f, new Vector3(2f, 0f, 0f))]);
    }

    [Test]
    public void Add_AQuieterImpact_KeepsTheLouderOnesPlace()
    {
        List<PhysicsImpactSound> list = [];

        PhysicsImpactSoundList.Add(list, Impact(surface: 3, volume: 0.5f, speed: 40000f, x: 1f));
        PhysicsImpactSoundList.Add(list, Impact(surface: 3, volume: 0.25f, speed: 90000f, x: 2f));

        list.ShouldBe([new PhysicsImpactSound(3, 30, 0.75f, 90000f, new Vector3(1f, 0f, 0f))]);
    }

    [Test]
    public void Add_ASixthSurface_MergesIntoTheLastOnceFiveAreListed()
    {
        // `if ( surfaceProps == sound.surfaceProps || list.Count() > 4 )`: the test is on the count BEFORE adding, so a fifth
        // distinct surface still gets its own entry and only the sixth merges — into the last, the search running from the back.
        List<PhysicsImpactSound> list = [];

        for (int surface = 1; surface <= 6; surface++)
        {
            PhysicsImpactSoundList.Add(list, Impact(surface, volume: 0.125f, speed: 10000f, x: surface));
        }

        list.Count.ShouldBe(5);
        list[4].ShouldBe(new PhysicsImpactSound(5, 50, 0.25f, 10000f, new Vector3(5f, 0f, 0f)));
    }

    private static PhysicsImpactSound Impact(int surface, float volume, float speed, float x) =>
        new(surface, surface * 10, volume, speed, new Vector3(x, 0f, 0f));
}
