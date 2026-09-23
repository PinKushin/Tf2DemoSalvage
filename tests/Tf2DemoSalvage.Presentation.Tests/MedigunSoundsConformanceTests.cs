using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// The medigun's heal loop and its detach: `CWeaponMedigun::ClientThink` starts `GetHealSound()` when a target appears
/// and none is playing; `OnDataChanged` with healing over runs `StopHealSound`, which ends the loop and plays
/// `GetDetachSound()` (`tf_weapon_medigun.cpp:2036`, `:2229`, `:2326`).
/// </summary>
public sealed class MedigunSoundsConformanceTests
{
    [Test]
    public void For_OneHeal_StartsTheLoopAndEndsItWithTheDetach()
    {
        IReadOnlyList<SceneSound> sounds = MedigunSounds.For([Beam(target: 3, start: 100, end: 160)], At, Scripts());

        sounds.Count.ShouldBe(3);
        sounds[0].ShouldBe(sounds[0] with { Tick = 100, Name = "weapons/medigun_heal.wav", EntityIndex = 40, Channel = 1, IsStop = false });
        sounds[1].ShouldBe(sounds[1] with { Tick = 160, Name = "weapons/medigun_heal.wav", EntityIndex = 40, IsStop = true });
        sounds[2].ShouldBe(sounds[2] with { Tick = 160, Name = "weapons/medigun_heal_detach.wav", EntityIndex = 40, IsStop = false });
    }

    /// <remarks>A new target on the same tick keeps `m_bHealing` true: the loop runs on and nothing detaches.</remarks>
    [Test]
    public void For_ASwitchOfTarget_KeepsTheLoop()
    {
        IReadOnlyList<SceneSound> sounds = MedigunSounds.For(
            [Beam(target: 3, start: 100, end: 160), Beam(target: 5, start: 160, end: 200)], At, Scripts());

        sounds.Count.ShouldBe(3);
        sounds[1].Tick.ShouldBe(200);
        sounds[1].IsStop.ShouldBeTrue();
    }

    [Test]
    public void For_AHealStillRunningAtTheEnd_NeverDetaches()
    {
        MedigunSounds.For([Beam(target: 3, start: 100, end: null)], At, Scripts()).Count.ShouldBe(1);
    }

    private static SceneHealBeam Beam(int target, int start, int? end) => new(40, target, false, 2, start, end) { Owner = 7 };

    private static (float X, float Y, float Z) At(int entity, int tick) => (1f, 2f, 3f);

    private static Dictionary<string, SoundScriptEntry> Scripts() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["WeaponMedigun.HealingWorld"] = new("WeaponMedigun.HealingWorld", 1, new SoundRange(0.75f, 0.75f), new SoundRange(100f, 100f), 64, ["weapons/medigun_heal.wav"]),
        ["WeaponMedigun.HealingDetachWorld"] = new("WeaponMedigun.HealingDetachWorld", 1, new SoundRange(0.75f, 0.75f), new SoundRange(100f, 100f), 64, ["weapons/medigun_heal_detach.wav"]),
    };
}
