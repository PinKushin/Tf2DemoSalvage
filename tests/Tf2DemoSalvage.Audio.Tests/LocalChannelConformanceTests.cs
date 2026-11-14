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
///                 as the message carried it. A channel with it whose wave loops is freed and never plays
///                 ("Speaker ignored looping sound", the test at 0x18002ec0a).
/// FUN_180029440   SND_Spatialize. Local = (soundsource == -2 || soundsource == GetViewEntity()) outside tool mode;
///                 a '#' channel (bit 2 of +0x13c, FUN_18002dc10) with no distance multiplier is dry instead and is
///                 not local. A local channel is 12 units along the listener's forward and its gain is taken there;
///                 on entchannel 1, CHAN_WEAPON (0x18002a095), its gain is × 1.2589254 (the float at 0x18036137c).
/// FUN_180027560   the sum: only for a local or dry channel WITHOUT bit 3 of +0x13c (0x18002757e); on a device of
///                 fewer than 3 speakers, when the mono weight is 1 or more, both ears take the sum of the panner's
///                 outputs × 1.
/// FUN_18002db70   a stereo wave is made omni ('@') only when its source is neither -2 nor the view entity.
/// </code>
/// -2 is <c>SOUND_FROM_UI_PANEL</c> (IEngineSound.h:29).
/// </remarks>
public sealed class LocalChannelConformanceTests
{
    private const int Entity = 3;
    private const int Voice = 7;
    private const int UiPanel = -2;
    private const int WeaponChannel = 1;
    private const int VoiceChannel = 2;

    /// <summary>A listener looking down +X, whose right is −Y.</summary>
    private static readonly (float X, float Y, float Z) Right = (0f, -1f, 0f);

    [TestCase(UiPanel, null, false, false, ChannelTraits.Local)]
    [TestCase(UiPanel, 5, false, false, ChannelTraits.Local)]
    [TestCase(Entity, Entity, false, false, ChannelTraits.Local)]
    [TestCase(Entity, 5, false, false, ChannelTraits.None)]
    [TestCase(Entity, null, false, false, ChannelTraits.None)]
    [TestCase(-1, null, false, false, ChannelTraits.None)]
    [TestCase(Entity, Entity, true, false, ChannelTraits.Dry)]
    [TestCase(UiPanel, null, true, false, ChannelTraits.Dry)]
    [TestCase(Entity, Entity, false, true, ChannelTraits.Local | ChannelTraits.Speaker)]
    [TestCase(Entity, 5, false, true, ChannelTraits.Speaker)]
    [TestCase(Entity, 5, true, true, ChannelTraits.Dry | ChannelTraits.Speaker)]
    public void Traits_ASourceAgainstTheViewEntity_AreTheEngines(int entity, int? viewEntity, bool dry, bool speaker, ChannelTraits expected)
    {
        Assert.That(SpeakerPan.Traits(entity, viewEntity, dry, speaker), Is.EqualTo(expected));
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

    [TestCase(true, WeaponChannel, 1.2589254f)]
    [TestCase(true, VoiceChannel, 1f)]
    [TestCase(true, 0, 1f)]
    [TestCase(false, WeaponChannel, 1f)]
    public void LocalWeapon_AChannel_IsTwoDecibelsUpOnlyForALocalChanWeapon(bool local, int channel, float expected)
    {
        Assert.That(SoundGain.LocalWeapon(local, channel), Is.EqualTo(expected));
    }

    [Test]
    public void LocalWeapon_ALocalChanWeapon_IsTheFloatAt18036137c()
    {
        Assert.That(BitConverter.SingleToInt32Bits(SoundGain.LocalWeapon(local: true, WeaponChannel)), Is.EqualTo(0x3fa12478));
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

    [Test]
    public void PansAt_AUiPanelChannelAtNoSoundLevel_IsSummedInBothEarsWithNoViewEntity()
    {
        // Soundlevel 0: weight 1. -2 is local whoever the view entity is, so the sum is taken.
        ActiveLoops loops = new();
        loops.Track(Beep(UiPanel, 0), Voice);

        (float left, float right) = PanOf(loops);

        left.ShouldBe(1.8f, 1e-6f);
        right.ShouldBe(1.8f, 1e-6f);
    }

    [Test]
    public void PansAt_AUiPanelStereoWave_IsNotOmni()
    {
        // FUN_18002db70 tests -2 before the view entity: no '@', so with no radius the weight is 0 and each ear is
        // forward's share, 1 − FastPow(0.5, 1.5).
        ActiveLoops loops = new();
        loops.Track(Beep(UiPanel, 75), Voice, stereo: true);

        (float left, float right) = PanOf(loops);

        left.ShouldBe(1f - BitConverter.Int32BitsToSingle(0x3eb52100), 1e-6f);
        right.ShouldBe(left);
    }

    [TestCase(UiPanel, null, 1f)]
    [TestCase(Entity, Entity, 1f)]
    [TestCase(Entity, null, 0f)]
    public void GainsAt_AChannelFarAway_IsTakenAtTwelveUnitsWhenLocal(int entity, int? viewEntity, float expected)
    {
        // 50,000 units at soundlevel 75 is silent; a local channel's gain is taken 12 units ahead, which is full.
        ActiveLoops loops = new() { ViewEntity = viewEntity };
        loops.Track(new SceneSound(1, "npc/beep.wav", 1, entity, 0, 1f, 75, 100, 0f, 0f, -50000f, 0f), Voice);

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

    [Test]
    public void PansAt_AMusicChannel_IsSummedWhereverItsEntityIs()
    {
        // '#' at soundlevel 0 is dry: summed without being local.
        ActiveLoops loops = new();
        loops.Track(new SceneSound(1, "#music/song.wav", 1, Entity, 0, 1f, 0, 100, 0f, 0f, -10f, 0f), Voice);

        (float left, float right) = PanOf(loops);

        left.ShouldBe(1.8f, 1e-6f);
        right.ShouldBe(1.8f, 1e-6f);
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
