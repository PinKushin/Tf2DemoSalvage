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
        SceneSound step = new Footsteps().Step(100, Player(speed: 350f), Concrete, NoWater, Scripts(), PlayerFlagLayout.Current).ShouldNotBeNull();

        // `PlayStepSound` resolves through a plain `GetParametersForSound` (`baseplayer_shared.cpp:693-713`): a read (B503).
        step.WaveDraw.ShouldNotBeNull().Emitted.ShouldBeFalse();

        // `PlayStepSound` runs from the animation event, in `SimulateEntities` (B505).
        step.Order.Phase.ShouldBe(ClientSoundPhase.Simulate);

        (step with { WaveDraw = null, Order = default }).ShouldBe(new SceneSound(
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

        footsteps.Step(100, Player(speed: 350f), Concrete, NoWater, Scripts(), PlayerFlagLayout.Current)!.Value.Name.ShouldBe("player/footsteps/concrete_right.wav");
        footsteps.Step(120, Player(speed: 350f), Concrete, NoWater, Scripts(), PlayerFlagLayout.Current)!.Value.Name.ShouldBe("player/footsteps/concrete_left.wav");
    }

    [Test]
    public void Step_WalkingOnDirt_IsAQuarterVolume()
    {
        // 200 is between 0.3 and 0.8 of 400: a walk, `fvol = 0.25` on dirt.
        new Footsteps().Step(100, Player(speed: 200f), Dirt, NoWater, Scripts(), PlayerFlagLayout.Current)!.Value.Volume.ShouldBe(0.25f);
    }

    [Test]
    public void Step_DuckedAndRunning_IsLoweredTo65Percent()
    {
        // Ducked, a run starts at 0.3 × 400 = 120; 0.5 × 0.65.
        new Footsteps().Step(100, Player(speed: 150f, flags: OnGround | Ducking), Concrete, NoWater, Scripts(), PlayerFlagLayout.Current)!
            .Value.Volume.ShouldBe(0.325f, 1e-6f);
    }

    [Test]
    public void Step_SlowerThanAWalk_IsSilent()
    {
        new Footsteps().Step(100, Player(speed: 110f), Concrete, NoWater, Scripts(), PlayerFlagLayout.Current).ShouldBeNull();
    }

    /// <remarks>
    /// `if ( GetFlags() &amp; (FL_FROZEN|FL_ATCONTROLS)) return;` (baseplayer_shared.cpp:530). In the nine-bit orangebox layout
    /// those are `1&lt;&lt;5` and `1&lt;&lt;6`, and `1&lt;&lt;7` is `FL_CLIENT`, which every player carries.
    /// </remarks>
    [TestCase(1 << 7, false, TestName = "Step_AClientInANineBitDemo_Sounds")]
    [TestCase(1 << 5, true, TestName = "Step_FrozenInANineBitDemo_IsSilent")]
    [TestCase(1 << 6, true, TestName = "Step_AtControlsInANineBitDemo_IsSilent")]
    public void Step_InANineBitDemo(int flag, bool silent) =>
        (new Footsteps().Step(100, Player(speed: 350f, flags: OnGround | flag), Concrete, NoWater, Scripts(), PlayerFlagLayout.OrangeBox) is null)
            .ShouldBe(silent);

    /// <remarks>In the current layout `FL_FROZEN` is `1&lt;&lt;6`, `FL_ATCONTROLS` `1&lt;&lt;7` and `FL_CLIENT` `1&lt;&lt;8`.</remarks>
    [TestCase(1 << 8, false, TestName = "Step_AClientInAnElevenBitDemo_Sounds")]
    [TestCase(1 << 6, true, TestName = "Step_FrozenInAnElevenBitDemo_IsSilent")]
    [TestCase(1 << 7, true, TestName = "Step_AtControlsInAnElevenBitDemo_IsSilent")]
    public void Step_InAnElevenBitDemo(int flag, bool silent) =>
        (new Footsteps().Step(100, Player(speed: 350f, flags: OnGround | flag), Concrete, NoWater, Scripts(), PlayerFlagLayout.Current) is null)
            .ShouldBe(silent);

    [Test]
    public void Step_InTheAir_IsSilent()
    {
        new Footsteps().Step(100, Player(speed: 350f, flags: 0), Concrete, NoWater, Scripts(), PlayerFlagLayout.Current).ShouldBeNull();
    }

    [Test]
    public void Step_FeetInWater_IsTheWaterSurface()
    {
        StepSurface water = new('S', "Water.StepLeft", "Water.StepRight");

        new Footsteps().Step(100, Player(speed: 350f) with { WaterLevel = 1 }, Concrete, name => name == "water" ? water : null, Scripts(), PlayerFlagLayout.Current)!
            .Value.Name.ShouldBe("player/footsteps/water_right.wav");
    }

    /// <remarks>
    /// `PlayStepSound` (`baseplayer_shared.cpp:693-713`) keeps `m_StepSoundCache[ nSide ]`: when the step name has ONE
    /// wave (`params.count == 1`) its whole `CSoundParameters` is reused, so the foot's later steps keep the first
    /// step's drawn pitch and soundlevel and draw nothing.
    /// </remarks>
    [Test]
    public void Step_ASingleWaveName_ReusesTheFootsFirstDraw()
    {
        Dictionary<string, SoundScriptEntry> scripts = Scripts();
        scripts["Concrete.StepRight"] = scripts["Concrete.StepRight"] with
        {
            Pitch = new SoundRange(50f, 150f),
            SoundLevel = new SoundRange(60f, 90f),
        };

        Footsteps footsteps = new();
        SceneSound first = footsteps.Step(100, Player(speed: 350f), Concrete, NoWater, scripts, PlayerFlagLayout.Current)!.Value;
        footsteps.Step(120, Player(speed: 350f), Concrete, NoWater, scripts, PlayerFlagLayout.Current);
        SceneSound third = footsteps.Step(140, Player(speed: 350f), Concrete, NoWater, scripts, PlayerFlagLayout.Current)!.Value;

        third.ShouldBe(first with { Tick = 140 });

        // The control: tick 140's own draw differs, so the equality above is the cache and not a coincidence.
        EntitySounds.Emit(140, 7, "Concrete.StepRight", (10f, 20f, 30f), scripts, emitted: false, ClientSoundPhase.Simulate)!.Value.Pitch.ShouldNotBe(first.Pitch);
    }

    [Test]
    public void Step_ARandomWaveName_DrawsEachStep()
    {
        Dictionary<string, SoundScriptEntry> scripts = Scripts();
        scripts["Concrete.StepRight"] = scripts["Concrete.StepRight"] with
        {
            Pitch = new SoundRange(50f, 150f),
            Waves = ["player/footsteps/concrete1.wav", "player/footsteps/concrete2.wav"],
        };

        Footsteps footsteps = new();
        footsteps.Step(100, Player(speed: 350f), Concrete, NoWater, scripts, PlayerFlagLayout.Current);
        footsteps.Step(120, Player(speed: 350f), Concrete, NoWater, scripts, PlayerFlagLayout.Current);
        SceneSound third = footsteps.Step(140, Player(speed: 350f), Concrete, NoWater, scripts, PlayerFlagLayout.Current)!.Value;

        third.Pitch.ShouldBe(EntitySounds.Emit(140, 7, "Concrete.StepRight", (10f, 20f, 30f), scripts, emitted: false, ClientSoundPhase.Simulate)!.Value.Pitch);
    }

    // ---- The landing: CGameMovement::PlayerRoughLandingEffects → PlayStepSound( origin, m_pSurfaceData, fvol, true ) ----

    [Test]
    public void Land_OnConcrete_IsTheNextFootAtTheLandingsVolume()
    {
        // gamemovement.cpp:3995 hands fvol straight to PlayStepSound, which plays it as ep.m_flVolume on CHAN_BODY.
        SceneSound landing = new Footsteps().Land(100, 7, (1f, 2f, 3f), Concrete, 0.85f, Scripts()).ShouldNotBeNull();

        (landing with { WaveDraw = null, Order = default }).ShouldBe(new SceneSound(
            Tick: 100,
            Name: "player/footsteps/concrete_right.wav",
            SoundNumber: ExplosionSounds.NotPrecached,
            EntityIndex: 7,
            Channel: 4,
            Volume: 0.85f,
            SoundLevel: 75,
            Pitch: 100,
            DelaySeconds: 0f,
            OriginX: 1f,
            OriginY: 2f,
            OriginZ: 3f));
    }

    [Test]
    public void Land_ThenAStep_SharesTheFootAlternation()
    {
        // One m_Local.m_nStepside serves both (baseplayer_shared.cpp:682-687).
        Footsteps footsteps = new();

        footsteps.Land(100, 7, (1f, 2f, 3f), Concrete, 1f, Scripts());

        footsteps.Step(120, Player(speed: 350f), Concrete, NoWater, Scripts(), PlayerFlagLayout.Current)!.Value.Name
            .ShouldBe("player/footsteps/concrete_left.wav");
    }

    [Test]
    public void Land_OnNothing_IsSilent()
    {
        // `if ( !psurface ) return;` (:676).
        new Footsteps().Land(100, 7, (1f, 2f, 3f), null, 1f, Scripts()).ShouldBeNull();
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

                scripts[name] = new SoundScriptEntry(name, 4, new SoundRange(0.9f, 0.9f), new SoundRange(100f, 100f), new SoundRange(75f, 75f),[wave]);
            }
        }

        return scripts;
    }
}
