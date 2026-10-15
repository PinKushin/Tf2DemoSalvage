using System.Linq;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Audio.Tests;

/// <summary>
/// Which sounds <c>snd_musicvolume</c> scales, as TF2's x64 <c>engine.dll</c> decides it (B545).
/// </summary>
/// <remarks>
/// Read from the binary, 2026-10-15 (project <c>tf2enginex64</c>; the SDK has no engine sound code):
///
/// - <c>FUN_18002d5c0</c>, called from the sound-start path (<c>FUN_18002dd10</c> at 18002e0c1), walks the
///   sfx name's leading sound characters and sets bit <c>0x4</c> of the channel byte at <c>+0x13c</c> when one is
///   <c>'#'</c> (<c>CHAR_DRYMIX</c>, soundchars.h), clearing it otherwise: the channel's dry flag.
/// - <c>FUN_1800292f0</c> (the channel's gain): <c>if ((flags+0x13c &amp; 4) &amp;&amp; dist_mult &lt;= 0) music = true</c>
///   (1800293ba), then <c>gain = snd_gain; if (music) gain *= snd_musicvolume * duck</c> (180029f14 reads
///   <c>snd_musicvolume</c>'s parent at ConVar <c>1804a3cc0+0x38</c>, value at <c>+0x54</c>).
/// - <c>dist_mult</c> (+0x100) is zero exactly for <c>SNDLVL_NONE</c>, so "music" is a <c>#</c> sound with no
///   attenuation — no path, mixer group or soundscript flag is consulted.
///
/// <c>duck</c> (<c>DAT_1804600e8</c>, eased toward a target in <c>FUN_18002fbe0</c>) is not ported: it is 1 unless
/// the engine is ducking music under speech. *Read from the binary; the duck is the one term left out.*
/// </remarks>
public sealed class SoundMusicVolumeConformanceTests
{
    [TestCase("#music/hl2_song1.mp3", 0, 0.25f)]
    [TestCase(")#ui/gamestartup1.mp3", 0, 0.25f)]
    [TestCase("#ui/gamestartup1.mp3", 75, 1f)]
    [TestCase("music/hl2_song1.mp3", 0, 1f)]
    [TestCase("*#music/a.wav", 0, 0.25f)]
    [TestCase("ui/#b.wav", 0, 1f)]
    public void Music_DryAndUnattenuated_IsMusicVolume(string name, int soundLevel, float expected) =>
        SoundGain.Music(name, soundLevel, 0.25f).ShouldBe(expected);

    [Test]
    public void GainsAt_AMusicLoop_IsScaledByMusicVolume()
    {
        ActiveLoops loops = new() { MusicVolume = 0.5f };
        loops.Track(new SceneSound(1, "#music/loop.wav", 1, 1, 6, 0.8f, 0, 100, 0f, 500f, 0f, 0f));

        loops.GainsAt(0f, 0f, 0f).Single().Gain.ShouldBe(0.4f);
        loops.GainAt(1, 6, (0f, 0f, 0f)).ShouldBe(0.4f);
    }
}
