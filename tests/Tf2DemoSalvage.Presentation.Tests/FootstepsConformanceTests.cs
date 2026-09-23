using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// A footstep, as TF2's animation event 7001 makes one: `C_TFPlayer::FireEvent` (`c_tf_player.cpp:9066`) forces
/// `UpdateStepSound` (`baseplayer_shared.cpp:506`), which picks the volume and calls `PlayStepSound` (`:668`).
/// </summary>
/// <remarks>
/// `CTFPlayer::GetStepSoundVelocities` (`tf_player_shared.cpp:11975`): walking below 0.3 of `m_flMaxspeed` is silent, and
/// below 0.8 is a walk; ducked, 0.25 and 0.3. The sound is the surface's `stepright` then `stepleft` alternately, on
/// `CHAN_BODY` from the player, at the volume `UpdateStepSound` chose — not the script's.
/// </remarks>
public sealed class FootstepsConformanceTests
{
    private const int OnGround = 1 << 0;
    private const int Ducking = 1 << 1;

    private static readonly StepSurface Concrete = new('C', "Concrete.StepLeft", "Concrete.StepRight");
    private static readonly StepSurface Dirt = new('D', "Dirt.StepLeft", "Dirt.StepRight");

    [Test]
    public void Step_RunningOnConcrete_IsTheRightFootFromThePlayerAtHalfVolume()
    {
        // 350 is past 0.8 × 400, a run: `fvol = 0.5`.
        SceneSound step = new Footsteps().Step(100, Player(speed: 350f), Concrete, NoWater, Scripts()).ShouldNotBeNull();

        step.ShouldBe(new SceneSound(
            Tick: 100,
            Name: "player/footsteps/concrete_right.wav",
            SoundNumber: ExplosionSounds.NotPrecached,
            EntityIndex: 7,
            Channel: 4,
            Volume: 0.5f,
            SoundLevel: 75,
            Pitch: 100,
            DelaySeconds: 0f,
            OriginX: 10f,
            OriginY: 20f,
            OriginZ: 30f));
    }

    [Test]
    public void Step_TwiceInARow_AlternatesTheFeet()
    {
        Footsteps footsteps = new();

        footsteps.Step(100, Player(speed: 350f), Concrete, NoWater, Scripts())!.Value.Name.ShouldBe("player/footsteps/concrete_right.wav");
        footsteps.Step(120, Player(speed: 350f), Concrete, NoWater, Scripts())!.Value.Name.ShouldBe("player/footsteps/concrete_left.wav");
    }

    [Test]
    public void Step_WalkingOnDirt_IsAQuarterVolume()
    {
        // 200 is between 0.3 and 0.8 of 400: a walk, `fvol = 0.25` on dirt.
        new Footsteps().Step(100, Player(speed: 200f), Dirt, NoWater, Scripts())!.Value.Volume.ShouldBe(0.25f);
    }

    [Test]
    public void Step_DuckedAndRunning_IsLoweredTo65Percent()
    {
        // Ducked, a run starts at 0.3 × 400 = 120; 0.5 × 0.65.
        new Footsteps().Step(100, Player(speed: 150f, flags: OnGround | Ducking), Concrete, NoWater, Scripts())!
            .Value.Volume.ShouldBe(0.325f, 1e-6f);
    }

    [Test]
    public void Step_SlowerThanAWalk_IsSilent()
    {
        new Footsteps().Step(100, Player(speed: 110f), Concrete, NoWater, Scripts()).ShouldBeNull();
    }

    [Test]
    public void Step_InTheAir_IsSilent()
    {
        new Footsteps().Step(100, Player(speed: 350f, flags: 0), Concrete, NoWater, Scripts()).ShouldBeNull();
    }

    [Test]
    public void Step_FeetInWater_IsTheWaterSurface()
    {
        StepSurface water = new('S', "Water.StepLeft", "Water.StepRight");

        new Footsteps().Step(100, Player(speed: 350f) with { WaterLevel = 1 }, Concrete, name => name == "water" ? water : null, Scripts())!
            .Value.Name.ShouldBe("player/footsteps/water_right.wav");
    }

    private static readonly Func<string, StepSurface?> NoWater = static _ => null;

    private static ScenePlayer Player(float speed, int flags = OnGround) =>
        new(7, 10f, 20f, 30f, Team: 2, Health: 125, PlayerClass: 1, Speed: speed, Flags: flags, WaterLevel: 0, MaxSpeed: 400f);

    private static Dictionary<string, SoundScriptEntry> Scripts()
    {
        Dictionary<string, SoundScriptEntry> scripts = new(StringComparer.OrdinalIgnoreCase);

        foreach ((string surface, string file) in (ReadOnlySpan<(string, string)>)[("Concrete", "concrete"), ("Dirt", "dirt"), ("Water", "water")])
        {
            foreach ((string foot, string side) in (ReadOnlySpan<(string, string)>)[("Left", "left"), ("Right", "right")])
            {
                string name = $"{surface}.Step{foot}";
                string wave = $"player/footsteps/{file}_{side}.wav";

                scripts[name] = new SoundScriptEntry(name, 4, new SoundRange(0.9f, 0.9f), new SoundRange(100f, 100f), 75, [wave]);
            }
        }

        return scripts;
    }
}
