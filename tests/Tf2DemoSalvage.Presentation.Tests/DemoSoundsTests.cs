using System;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// Which sounds a demo will ask for, gathered before playback starts.
/// </summary>
/// <remarks>
/// **This was the interesting half of <c>MainForm.PrecacheSounds</c>** (B188, D90), and it had no
/// test because reaching it meant constructing a form, an audio device and a TF2 install.
/// </remarks>
public sealed class DemoSoundsTests
{
    [Test]
    public void ToPrecache_WithNoDemoList_Refuses()
    {
        Should.Throw<ArgumentNullException>(() => DemoSounds.ToPrecache(null!, Soundscape()).ToList());
    }

    [Test]
    public void ToPrecache_WithNoSoundscape_Refuses()
    {
        // The ambience is half the answer, so a caller without one has a mistake rather than an
        // empty set — see the class remarks for why the demo's own list is not enough.
        Should.Throw<ArgumentNullException>(() => DemoSounds.ToPrecache([], null!).ToList());
    }

    [Test]
    public void ToPrecache_WithNoCatalog_IsJustTheDemosOwnSounds()
    {
        // **A machine with no TF2 installed has no catalog**, and null there rather than empty is
        // the honest value — so this has to degrade to the demo's own list rather than throwing on
        // the `??`. The list is returned intact, not swallowed.
        DemoSounds.ToPrecache(["weapons/shotgun_shoot.wav"], Soundscape())
            .ShouldBe(["weapons/shotgun_shoot.wav"]);
    }

    [Test]
    public void ToPrecache_TheDemosOwnSounds_ComeFirst()
    {
        // Order matters to the decode, not to correctness: the demo's own sounds are the ones a
        // viewer hears first, and a precache that reached them last would stall on exactly the
        // sounds it was meant to have ready.
        DemoSounds.ToPrecache(["a.wav", "b.wav"], Soundscape()).First().ShouldBe("a.wav");
    }

    private static SoundscapeSystem Soundscape() =>
        new(new ActiveLoops(), _ => null, NullLogger.Instance);
}
