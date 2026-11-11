using System.Collections.Generic;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>An emitted name that is a raw wave plays as itself, not as a script (B569).</summary>
/// <remarks>
/// **Read from published source.** `CSoundEmitterSystem::EmitSound` (`SoundEmitterSystem.cpp:556-602`) hands a name
/// containing `.wav` or `.mp3` (both `Q_stristr`, so either case) or starting with `!` straight to
/// `enginesound->EmitSound` with the `EmitSound_t`'s own fields, and returns before the script lookup.
/// `CBaseEntity::EmitSound( filter, ent, name, &amp;origin )` (`:1190-1206`) sets only the name, time and origin, so
/// those fields are the constructor's (`shareddefs.h:832-848`): channel 0 (`CHAN_AUTO`), `VOL_NORM`, `SNDLVL_NONE`,
/// `PITCH_NORM`. `GetParametersForSound` has no such branch, so a footstep or an impact by a raw name plays nothing.
/// </remarks>
public sealed class EntitySoundsConformanceTests
{
    private static readonly Dictionary<string, SoundScriptEntry> NoScripts = [];

    [Test]
    public void Emit_ARawWaveName_PlaysItWithTheEmitSoundDefaults()
    {
        SceneSound sound = EntitySounds.Emit(
            40, 7, ")weapons/test_fire.wav", (1f, 2f, 3f), NoScripts, emitted: true, ClientSoundPhase.Simulate)!.Value;

        sound.Name.ShouldBe(")weapons/test_fire.wav");
        sound.Tick.ShouldBe(40);
        sound.EntityIndex.ShouldBe(7);
        sound.Channel.ShouldBe(0);
        sound.Volume.ShouldBe(1f);
        sound.SoundLevel.ShouldBe(0);
        sound.Pitch.ShouldBe(100);
        (sound.OriginX, sound.OriginY, sound.OriginZ).ShouldBe((1f, 2f, 3f));
        sound.WaveDraw.ShouldBeNull();
        sound.Order.ShouldBe(new ClientSoundOrder(ClientSoundPhase.Simulate, 0, 0));
    }

    [Test]
    public void Emit_ARawNameAScriptAlsoDeclares_PlaysTheRawName()
    {
        // The raw test comes before `GetSoundIndex` (`SoundEmitterSystem.cpp:556` against `:604-607`).
        const string Name = "weapons/test_fire.wav";
        Dictionary<string, SoundScriptEntry> scripts = new()
        {
            [Name] = new SoundScriptEntry(Name, 1, new SoundRange(1f, 1f), new SoundRange(100f, 100f), new SoundRange(75f, 75f), ["weapons/other.wav"]),
        };

        EntitySounds.Emit(1, 2, Name, (0f, 0f, 0f), scripts, emitted: true, ClientSoundPhase.Simulate)!.Value.Name.ShouldBe(Name);
    }

    [Test]
    public void HudEmit_ARawWaveName_PlaysItFromTheLocalPlayerAtNetworkTime()
    {
        // A HUD's `EmitSound( filter, SOUND_FROM_LOCAL_PLAYER, name )` takes the same branch (`tf_hud_deathnotice.cpp:751`).
        SceneSound sound = HudSounds.Emit(9, "ui/test_ping.wav", NoScripts)!.Value;

        (sound.Name, sound.EntityIndex, sound.Channel, sound.Volume, sound.SoundLevel, sound.Pitch)
            .ShouldBe(("ui/test_ping.wav", HudSounds.FromLocalPlayer, 0, 1f, 0, 100));
        sound.Order.ShouldBe(new ClientSoundOrder(ClientSoundPhase.Network, 0, 0));
    }

    [Test]
    public void Emit_ABangInsideTheName_IsLookedUpAsAScript()
    {
        EntitySounds.Emit(1, 2, "Weapon!Test", (0f, 0f, 0f), NoScripts, emitted: true, ClientSoundPhase.Simulate).ShouldBeNull();
    }

    [TestCase("vo/null.MP3")]
    [TestCase("ambient/Hum.WAV")]
    [TestCase("!HG_ALERT1")]
    public void Emit_ANameEmitSoundPlaysDirectly_IsNotLookedUpAsAScript(string name)
    {
        EntitySounds.Emit(1, 2, name, (0f, 0f, 0f), NoScripts, emitted: true, ClientSoundPhase.Simulate)!.Value.Name
            .ShouldBe(name);
    }

    [TestCase("player/footsteps/test.wav")]
    [TestCase("vo/null.mp3")]
    [TestCase("!HG_ALERT1")]
    public void Emit_ARawNameReadByGetParametersForSound_PlaysNothing(string name)
    {
        EntitySounds.Emit(1, 2, name, (0f, 0f, 0f), NoScripts, emitted: false, ClientSoundPhase.Simulate).ShouldBeNull();
    }

    [Test]
    public void Emit_AScriptNameNoScriptDeclares_PlaysNothing()
    {
        EntitySounds.Emit(1, 2, "Weapon_Test.Single", (0f, 0f, 0f), NoScripts, emitted: true, ClientSoundPhase.Simulate)
            .ShouldBeNull();
    }
}
