using System;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Audio.Tests;

/// <summary>
/// Which channels <c>SND_Spatialize</c> treats as the listener's own, which it sums into both ears, and what the
/// <c>SND_SPEAKER</c> flag takes away (B724).
/// </summary>
/// <remarks>
/// Read in the decompile and disassembly, x64 <c>engine.dll</c> build 11191711.
/// <code>
/// FUN_18002e6e0   S_StartDynamicSound, and FUN_18002f4c0, S_StartStaticSound: the channel's bit 3 of +0x13c is bit 6
///                 of the start's flags, SND_SPEAKER (soundflags.h:125), which CL_DispatchSound (FUN_18008caf0) passes
///                 as the message carried it. A soundlevel (params +0x38) of 256 or more sets bit 3 of +0x13d and has
///                 256 taken off it (0x18002e9f9-0x18002ea10); a soundlevel then 0 leaves dist_mult (+0x100) 0.
/// FUN_18002dc10   its first loop: '!' '*' '?' (mask 0x8000040200000000), ')' '&lt;' '&gt;' '@' '^' (mask
///                 0x20000000a80001 from ')') and '}' are stepped over; '#' sets mask 4 of +0x13c; any other byte
///                 ends the walk with it clear.
/// FUN_180029440   SND_Spatialize. Local = (soundsource == -2 || soundsource == GetViewEntity()) outside tool mode;
///                 a channel with mask 4 and dist_mult &lt;= 0 (0x18002950a) is dry instead and is not local. A local
///                 channel is 12 units along the listener's forward and its gain is taken there. In the branch for a
///                 channel WITHOUT bit 3 of +0x13d, a local channel on entchannel 1, CHAN_WEAPON (0x18002a095), has its
///                 gain × 1.2589254 (the float at 0x18036137c); the other branch is 1 − attenuation × 0.001 × distance
///                 clamped 0..1 and nothing else.
/// FUN_180027560   the sum: only for a local or dry channel WITHOUT bit 3 of +0x13c (0x18002757e); on a device of
///                 fewer than 3 speakers, when the mono weight is 1 or more, both ears take the sum of the panner's
///                 outputs × 1.
/// </code>
/// The -2 half of local, <c>SOUND_FROM_UI_PANEL</c> (IEngineSound.h:29), is not ported: no demo carries it and nothing
/// here emits it (D180; <c>SpeakerPan.Traits</c>).
/// </remarks>
public sealed class LocalChannelConformanceTests
{
    private const int Entity = 3;
    private const int Voice = 7;
    private const int WeaponChannel = 1;
    private const int VoiceChannel = 2;

    /// <summary>A listener looking down +X, whose right is −Y.</summary>
    private static readonly (float X, float Y, float Z) Right = (0f, -1f, 0f);

    [TestCase(Entity, Entity, false, false, ChannelTraits.Local)]
    [TestCase(Entity, 5, false, false, ChannelTraits.None)]
    [TestCase(Entity, null, false, false, ChannelTraits.None)]
    [TestCase(-1, null, false, false, ChannelTraits.None)]
    [TestCase(Entity, Entity, true, false, ChannelTraits.Dry)]
    [TestCase(Entity, Entity, false, true, ChannelTraits.Local | ChannelTraits.Speaker)]
    [TestCase(Entity, 5, false, true, ChannelTraits.Speaker)]
    [TestCase(Entity, 5, true, true, ChannelTraits.Dry | ChannelTraits.Speaker)]
    public void Traits_ASourceAgainstTheViewEntity_AreTheEngines(int entity, int? viewEntity, bool dry, bool speaker, ChannelTraits expected)
    {
        Assert.That(SpeakerPan.Traits(entity, viewEntity, dry, speaker), Is.EqualTo(expected));
    }

    /// <remarks>
    /// The walk is `SoundGain.IsMusic`'s; the soundlevels differ. 256 is compatibility mode's 0: no distance multiplier,
    /// so dry, where music (the `snd_musicvolume` branch) is soundlevel 0 alone.
    /// </remarks>
    [TestCase("#music/song.wav", 0, true, true)]
    [TestCase("#music/song.wav", 256, true, false)]
    [TestCase("#music/song.wav", 75, false, false)]
    [TestCase("#music/song.wav", 331, false, false)]
    [TestCase("#music/song.wav", -1, false, false)]
    [TestCase("!*?)<>@^}#music/a.wav", 256, true, false)]
    [TestCase("x#music/a.wav", 0, false, false)]
    [TestCase("$#music/a.wav", 256, false, false)]
    [TestCase("ui/#b.wav", 0, false, false)]
    [TestCase("music/song.wav", 256, false, false)]
    [TestCase("", 0, false, false)]
    public void IsDry_ANameAtASoundLevel_IsTheDryBitWithNoDistanceMultiplier(string name, int soundLevel, bool dry, bool music)
    {
        Assert.Multiple(() =>
        {
            Assert.That(SoundGain.IsDry(name, soundLevel), Is.EqualTo(dry));
            Assert.That(SoundGain.IsMusic(name, soundLevel), Is.EqualTo(music), "the control: music is soundlevel 0 alone");
        });
    }

    [TestCase(0, 1f)]
    [TestCase(256, 1f)]
    [TestCase(75, 0f)]
    [TestCase(257, 0f)]
    public void Mono_ASoundLevelWithNoDistanceMultiplier_IsFullyMono(int soundLevel, float expected)
    {
        Assert.That(SpeakerPan.Mono(soundLevel, "npc/beep.wav", stereo: false, local: false), Is.EqualTo(expected));
    }

    [Test]
    public void Spatialize_ALocalChannelWithTheSpeakerFlag_IsAheadAndNotSummed()
    {
        // Radius 48: 12 is inside half of it, so the weight is 1; FUN_180027560 returns at its first test for bit 3 of
        // +0x13c, so each ear keeps the panner's 0.9.
        (float distance, float left, float right) = SpeakerPan.Spatialize(
            (0f, -500f, 0f), (0f, 0f, 0f), Right, mono: 0f, ChannelTraits.Local | ChannelTraits.Speaker, radius: 48f);

        Assert.Multiple(() =>
        {
            Assert.That(distance, Is.EqualTo(12f));
            Assert.That((left, right), Is.EqualTo((0.9f, 0.9f)));
        });
    }

    [Test]
    public void Spatialize_ADryChannel_IsWhereItStandsWithTheEarsSummed()
    {
        // Dry is not local, so the direction and distance are the origin's; its weight is 1 (no distance multiplier), so
        // the panner gives 0.9 and 0.9 and FUN_180027560 writes 1.8 into both.
        (float distance, float left, float right) = SpeakerPan.Spatialize(
            (0f, -500f, 0f), (0f, 0f, 0f), Right, mono: 1f, ChannelTraits.Dry);

        Assert.Multiple(() =>
        {
            Assert.That(distance, Is.EqualTo(500f));
            Assert.That(left, Is.EqualTo(1.8f).Within(1e-6f));
            Assert.That(right, Is.EqualTo(1.8f).Within(1e-6f));
        });
    }

    [Test]
    public void Spatialize_ADryChannelWithTheSpeakerFlag_IsNotSummed()
    {
        (_, float left, float right) = SpeakerPan.Spatialize(
            (0f, -500f, 0f), (0f, 0f, 0f), Right, mono: 1f, ChannelTraits.Dry | ChannelTraits.Speaker);

        Assert.That((left, right), Is.EqualTo((0.9f, 0.9f)));
    }

    [Test]
    public void Spatialize_AFullyMonoChannelThatIsNeitherLocalNorDry_IsNotSummed()
    {
        // The control: a global sound (soundlevel 0) from another entity is 0.9 in each ear and stays so.
        (_, float left, float right) = SpeakerPan.Spatialize((0f, -500f, 0f), (0f, 0f, 0f), Right, mono: 1f, ChannelTraits.None);

        Assert.That((left, right), Is.EqualTo((0.9f, 0.9f)));
    }

    [TestCase(true, WeaponChannel, 75, 1.2589254f)]
    [TestCase(true, WeaponChannel, 0, 1.2589254f)]
    [TestCase(true, WeaponChannel, 255, 1.2589254f)]
    [TestCase(true, WeaponChannel, 256, 1f)]
    [TestCase(true, WeaponChannel, 331, 1f)]
    [TestCase(true, VoiceChannel, 75, 1f)]
    [TestCase(true, 0, 75, 1f)]
    [TestCase(false, WeaponChannel, 75, 1f)]
    public void LocalWeapon_AChannel_IsTwoDecibelsUpOnlyForALocalChanWeaponBelowCompatibilityMode(
        bool local, int channel, int soundLevel, float expected)
    {
        Assert.That(SoundGain.LocalWeapon(local, channel, soundLevel), Is.EqualTo(expected));
    }

    [Test]
    public void LocalWeapon_ALocalChanWeapon_IsTheFloatAt18036137c()
    {
        Assert.That(
            BitConverter.SingleToInt32Bits(SoundGain.LocalWeapon(local: true, WeaponChannel, soundLevel: 75)),
            Is.EqualTo(0x3fa12478));
    }

    /// <remarks>Volume × distance gain × the weapon's 2 dB × the music volume: the one formula a start and a re-gain share.</remarks>
    [TestCase(0.5f, 75, 12f, true, WeaponChannel, false, 0.25f, 0.5f * 1.2589254f)]
    [TestCase(0.5f, 75, 12f, false, WeaponChannel, false, 0.25f, 0.5f)]
    [TestCase(0.8f, 0, 5000f, false, 0, true, 0.25f, 0.2f)]
    [TestCase(0.8f, 75, 50000f, false, 0, false, 1f, 0f)]
    public void OfChannel_AChannel_IsVolumeTimesDistanceTimesWeaponTimesMusic(
        float volume, int soundLevel, float distance, bool local, int channel, bool music, float musicVolume, float expected)
    {
        Assert.That(SoundGain.OfChannel(volume, soundLevel, distance, local, channel, music, musicVolume), Is.EqualTo(expected));
    }

    [TestCase(false, 1.8f)]
    [TestCase(true, 0.9f)]
    public void PansAt_TheViewEntitysChannelInsideItsRadius_IsSummedUnlessASpeakerPlaysIt(bool speaker, float each)
    {
        ActiveLoops loops = new() { ViewEntity = Entity };
        loops.Track(Beep(Entity, 75) with { ThroughSpeaker = speaker }, Voice, radius: 48f);

        (float left, float right) = PanOf(loops);

        left.ShouldBe(each, 1e-6f);
        right.ShouldBe(each, 1e-6f);
    }

    [TestCase(Entity, 1f)]
    [TestCase(null, 0f)]
    public void GainsAt_AChannelFarAway_IsTakenAtTwelveUnitsWhenLocal(int? viewEntity, float expected)
    {
        // 50,000 units at soundlevel 75 is silent; a local channel's gain is taken 12 units ahead, which is full.
        ActiveLoops loops = new() { ViewEntity = viewEntity };
        loops.Track(new SceneSound(1, "npc/beep.wav", 1, Entity, 0, 1f, 75, 100, 0f, 0f, -50000f, 0f), Voice);

        loops.GainsAt(0f, 0f, 0f, musicVolume: 1f).ShouldHaveSingleItem().Gain.ShouldBe(expected);
    }

    [TestCase(WeaponChannel, Entity, 1.2589254f)]
    [TestCase(VoiceChannel, Entity, 1f)]
    [TestCase(WeaponChannel, null, 1f)]
    public void GainsAt_AChannelOnChanWeapon_IsTwoDecibelsUpOnlyForTheViewEntity(int channel, int? viewEntity, float expected)
    {
        ActiveLoops loops = new() { ViewEntity = viewEntity };
        loops.Track(new SceneSound(1, "weapons/shot.wav", 1, Entity, channel, 1f, 75, 100, 0f, 0f, -10f, 0f), Voice);

        loops.GainsAt(0f, 0f, 0f, musicVolume: 1f).ShouldHaveSingleItem().Gain.ShouldBe(expected);
    }

    /// <remarks>'#' at soundlevel 0 or 256 is dry: summed without being local. At 75 it has a distance multiplier and is not.</remarks>
    [TestCase(0, 1.8f, 1.8f)]
    [TestCase(256, 1.8f, 1.8f)]
    [TestCase(75, 0f, 1f)]
    public void PansAt_ADryNamedChannel_IsSummedOnlyWithNoDistanceMultiplier(int soundLevel, float left, float right)
    {
        ActiveLoops loops = new();
        loops.Track(new SceneSound(1, "#music/song.wav", 1, Entity, 0, 1f, soundLevel, 100, 0f, 0f, -10f, 0f), Voice);

        (float l, float r) = PanOf(loops);

        l.ShouldBe(left, 1e-6f);
        r.ShouldBe(right, 1e-6f);
    }

    private static SceneSound Beep(int entity, int soundLevel) =>
        new(1, "npc/beep.wav", 1, entity, 0, 1f, soundLevel, 100, 0f, 0f, -10f, 0f);

    private static (float Left, float Right) PanOf(ActiveLoops loops)
    {
        (int voice, float left, float right) = loops.PansAt((0f, 0f, 0f), Right).ShouldHaveSingleItem();
        voice.ShouldBe(Voice);

        return (left, right);
    }
}
