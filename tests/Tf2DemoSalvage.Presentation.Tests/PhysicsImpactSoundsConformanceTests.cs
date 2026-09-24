using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// `physicssound::PlayImpactSounds` (`vphysics_sound.h:40`): the sounding surface's `impacthard`, or its `impactsoft` when the
/// struck surface is softer than its `impacthardthreshold` or the impact is under its `audiohardminvelocity`; the script's
/// volume times the frame's summed volume, clamped to one; `CHAN_STATIC` from the world, at the object.
/// </summary>
public sealed class PhysicsImpactSoundsConformanceTests
{
    private static readonly VphysicsSurfaceProps Surfaces = Parse(
        "\"default\" { \"impacthard\" \"Stone.ImpactHard\" \"impactsoft\" \"Stone.ImpactSoft\" \"audiohardnessfactor\" \"1\" \"impacthardthreshold\" \"0.5\" } " +
        "\"flesh\" { \"impacthard\" \"Flesh.ImpactHard\" \"impactsoft\" \"Flesh.ImpactSoft\" \"audiohardnessfactor\" \"0.2\" \"impacthardthreshold\" \"0.5\" } " +
        "\"quiet\" { \"impacthard\" \"Flesh.ImpactHard\" \"impactsoft\" \"Flesh.ImpactSoft\" \"audiohardminvelocity\" \"10000\" }");

    [Test]
    public void For_FleshOnStone_IsTheHardSoundFromTheWorldAtTheObject()
    {
        SceneSound sound = PhysicsImpactSounds.For(100, Impact("flesh", "default", volume: 0.5f), Surfaces, Scripts()).ShouldNotBeNull();

        sound.ShouldBe(new SceneSound(100, "physics/flesh/hard.wav", ExplosionSounds.NotPrecached, 0, 6, 0.4f, 75, 100, 0f, 1f, 2f, 3f));
    }

    [Test]
    public void For_StoneOnFlesh_IsTheSoftSound()
    {
        // Flesh's hardness 0.2 is under stone's threshold 0.5.
        PhysicsImpactSounds.For(100, Impact("default", "flesh", volume: 0.5f), Surfaces, Scripts())!.Value.Name.ShouldBe("physics/stone/soft.wav");
    }

    [Test]
    public void For_SlowerThanTheHardMinimum_IsTheSoftSound()
    {
        // `impactSpeed` is speed squared: 5000 is under 10000.
        PhysicsImpactSounds.For(100, Impact("quiet", "default", volume: 0.5f, speedSquared: 5000f), Surfaces, Scripts())!.Value.Name
            .ShouldBe("physics/flesh/soft.wav");
    }

    [Test]
    public void For_ASummedVolumeOverOne_IsClampedToOne()
    {
        PhysicsImpactSounds.For(100, Impact("flesh", "default", volume: 2.5f), Surfaces, Scripts())!.Value.Volume.ShouldBe(0.8f);
    }

    private static PhysicsImpactSound Impact(string surface, string hit, float volume, float speedSquared = 40000f) =>
        new(Surfaces.GetSurfaceIndex(surface), Surfaces.GetSurfaceIndex(hit), volume, speedSquared, new Vector3(1f, 2f, 3f));

    private static VphysicsSurfaceProps Parse(string text)
    {
        VphysicsSurfaceProps props = new([]);
        props.ParseSurfaceData(Encoding.Latin1.GetBytes(text));
        return props;
    }

    private static Dictionary<string, SoundScriptEntry> Scripts() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["Flesh.ImpactHard"] = Entry("Flesh.ImpactHard", "physics/flesh/hard.wav"),
        ["Flesh.ImpactSoft"] = Entry("Flesh.ImpactSoft", "physics/flesh/soft.wav"),
        ["Stone.ImpactHard"] = Entry("Stone.ImpactHard", "physics/stone/hard.wav"),
        ["Stone.ImpactSoft"] = Entry("Stone.ImpactSoft", "physics/stone/soft.wav"),
    };

    private static SoundScriptEntry Entry(string name, string wave) =>
        new(name, 1, new SoundRange(0.8f, 0.8f), new SoundRange(100f, 100f), 75, [wave]);
}
