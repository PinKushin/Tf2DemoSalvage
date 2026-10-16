using System.Linq;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Audio.Tests;

/// <summary>
/// Which sounds <c>snd_musicvolume</c> scales, as TF2's x64 <c>engine.dll</c> decides it (B545).
/// </summary>
/// <remarks>
/// Read from the binary, 2026-10-15 (Ghidra project <c>tf2enginex64</c>; the SDK has no engine sound code):
///
/// - **The dry bit.** <c>FUN_18002d5c0</c>, called by both sound-start paths (<c>FUN_18002dd10</c> at 18002e0c1,
///   <c>FUN_18002e990</c> at 18002eefe), walks the sfx name from its first byte. <c>'!'</c>, <c>'*'</c>,
///   <c>'?'</c> (mask <c>0x8000040200000000</c>) and <c>')'</c>, <c>'&lt;'</c>, <c>'&gt;'</c>, <c>'@'</c>,
///   <c>'^'</c> (mask <c>0x20000000a80001</c> from <c>0x29</c>) and <c>'}'</c> are skipped, with no length cap;
///   <c>'#'</c> sets bit <c>0x4</c> at <c>+0x13c</c> and ends the walk; any other byte clears it and ends the walk.
///   That set is exactly <c>soundchars.h</c>'s ten.
/// - **The distance multiplier** at <c>+0x100</c> is written only by those two paths (18002e0b5, 18002eef2): 0
///   when the soundlevel is 0, otherwise <c>10^((snd_refdb - sndlvl)/20) / snd_refdist</c>, which is positive for
///   any soundlevel, negative ones included. A soundlevel of 256 or more has 256 taken off but sets bit 0x8 at
///   <c>+0x13d</c> (18002e032), and <c>FUN_1800292f0</c> then takes the branch that never reads the music volume.
/// - **The gain.** <c>FUN_1800292f0</c>, every mix: <c>music = (bit 0x4) &amp;&amp; dist_mult &lt;= 0</c>
///   (1800293ba), then <c>gain = snd_gain; if (music) gain *= snd_musicvolume * duck</c> (180029f14 reads
///   <c>snd_musicvolume</c>'s parent at ConVar <c>1804a3cc0+0x38</c>, value at <c>+0x54</c>).
///
/// So music is a <c>'#'</c> among the leading sound characters at soundlevel exactly 0. <c>duck</c>
/// (<c>DAT_1804600e8</c>, eased in <c>FUN_18002fbe0</c>) is not ported. NaN is ours: the engine would multiply it
/// through, and what its mixer does with a NaN gain is not read, so it is silence here as a NaN <c>volume</c> is.
/// </remarks>
public sealed class SoundMusicVolumeConformanceTests
{
    [TestCase("#music/hl2_song1.mp3", 0, true)]
    [TestCase(")#ui/gamestartup1.mp3", 0, true)]
    [TestCase("*)@#music/a.wav", 0, true)]
    [TestCase("!*?)<>@^}#music/a.wav", 0, true)]
    [TestCase("x#music/a.wav", 0, false)]
    [TestCase("$#music/a.wav", 0, false)]
    [TestCase("#ui/gamestartup1.mp3", 75, false)]
    [TestCase("#ui/gamestartup1.mp3", -1, false)]
    [TestCase("#ui/gamestartup1.mp3", 256, false)]
    [TestCase("music/hl2_song1.mp3", 0, false)]
    [TestCase("ui/#b.wav", 0, false)]
    [TestCase("", 0, false)]
    [TestCase("*)", 0, false)]
    public void IsMusic_ByLeadingCharactersAndSoundLevel_IsTheEnginesDryBitAtZeroDistance(string name, int soundLevel, bool expected) =>
        SoundGain.IsMusic(name, soundLevel).ShouldBe(expected);

    [TestCase(true, 0.25f, 0.25f)]
    [TestCase(true, 0f, 0f)]
    [TestCase(true, float.NaN, 0f)]
    [TestCase(false, 0.25f, 1f)]
    [TestCase(false, float.NaN, 1f)]
    public void Music_ForAMusicVolume_IsTheMultiplier(bool isMusic, float musicVolume, float expected) =>
        SoundGain.Music(isMusic, musicVolume).ShouldBe(expected);

    [Test]
    public void GainsAt_AMusicLoop_IsScaledByMusicVolume()
    {
        ActiveLoops loops = new();
        loops.Track(new SceneSound(1, "#music/loop.wav", 1, 1, 6, 0.8f, 0, 100, 0f, 500f, 0f, 0f));

        loops.GainsAt(0f, 0f, 0f, 0.5f).Single().Gain.ShouldBe(0.4f);
        loops.GainAt(1, 6, (0f, 0f, 0f), 0.5f).ShouldBe(0.4f);
    }

    [Test]
    public void GainOf_AMusicSoundscapeLoop_IsScaledByMusicVolume()
    {
        SoundscapeVoice voice = new(1, "#music/room.wav", 0.8f, 100, Position: null, SoundLevel: 0);

        SoundscapeSystem.GainOf(voice, (0f, 0f, 0f), 0.5f).ShouldBe(0.4f);
    }

    /// <remarks>
    /// An unpositioned soundscape loop is `EmitAmbientSound` (`c_soundscape.cpp:1161`); engine.dll FUN_180017150 fills
    /// StartSoundParams_t with soundlevel 0 and CHAN_STATIC, not the script's level, so a `#` ambient is music. A
    /// positioned one is `EmitSound` with `ep.m_SoundLevel = soundlevel` (`:1173`), so at 75 it is not.
    /// </remarks>
    [TestCase(false, 0.4f)]
    [TestCase(true, 0.8f)]
    public void GainOf_AMusicSoundscapeLoopAtTheScriptsLevel_IsMusicOnlyWhenAmbient(bool positioned, float expected)
    {
        SoundscapeVoice voice = new(1, "#music/room.wav", 0.8f, 100, positioned ? (0f, 0f, 0f) : null, SoundLevel: 75);

        SoundscapeSystem.GainOf(voice, (0f, 0f, 0f), 0.5f).ShouldBe(expected);
    }
}
