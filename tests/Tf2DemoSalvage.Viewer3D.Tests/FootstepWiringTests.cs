using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// B501's viewer wiring: the footstep event is gated by the LOADED demo's flag list, through the call
/// <c>StepAnimationSounds</c> makes.
/// </summary>
/// <remarks>
/// <c>1&lt;&lt;7</c> is <c>FL_CLIENT</c> in a nine-bit (2007-2009) demo, set on every player, and <c>FL_ATCONTROLS</c>
/// in the current list — so a viewer that passed the current list silenced every step of every 2009 player.
/// </remarks>
public sealed class FootstepWiringTests
{
    private const int OnGround = 1 << 0;
    private const int Bit7 = 1 << 7;

    private static readonly StepSurface Concrete = new('C', "Concrete.StepLeft", "Concrete.StepRight");

    [Test]
    public void Footstep_AClientInANineBitDemo_IsTheRightFoot() =>
        Step(DemoTimeline.ForRecorder([], PlayerFlagLayout.OrangeBox, [], [], 0.015f)).ShouldNotBeNull()
            .Name.ShouldBe("player/footsteps/concrete_right.wav");

    [Test]
    public void Footstep_AtControlsInAnElevenBitDemo_IsSilent() =>
        Step(DemoTimeline.ForRecorder([], PlayerFlagLayout.Current, [], [], 0.015f)).ShouldBeNull();

    private static SceneSound? Step(DemoTimeline timeline) =>
        MainForm.Footstep(
            new Footsteps(),
            100,
            new ScenePlayer(7, 0f, 0f, 0f, Team: 2, Health: 125, PlayerClass: 1, Speed: 350f, Flags: OnGround | Bit7, WaterLevel: 0, MaxSpeed: 400f),
            Concrete,
            static _ => null,
            Scripts(),
            timeline);

    private static Dictionary<string, SoundScriptEntry> Scripts()
    {
        Dictionary<string, SoundScriptEntry> scripts = new(StringComparer.OrdinalIgnoreCase);

        foreach ((string foot, string side) in (ReadOnlySpan<(string, string)>)[("Left", "left"), ("Right", "right")])
        {
            string name = $"Concrete.Step{foot}";

            scripts[name] = new SoundScriptEntry(
                name, 4, new SoundRange(0.9f, 0.9f), new SoundRange(100f, 100f), new SoundRange(75f, 75f),[$"player/footsteps/concrete_{side}.wav"]);
        }

        return scripts;
    }
}
