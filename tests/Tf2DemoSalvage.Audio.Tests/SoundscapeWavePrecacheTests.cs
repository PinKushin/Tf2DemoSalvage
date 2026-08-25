using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Audio;

namespace Tf2DemoSalvage.Audio.Tests;

/// <summary>
/// The waves a map's soundscapes can loop, which the sound precache needs and a demo never names.
/// </summary>
/// <remarks>
/// **These are invisible to the timeline, and that is the whole point of this list.** A demo carries
/// the sounds the server told the client to play; a soundscape's loops come from the map's
/// <c>env_soundscape</c> entities via <c>scripts/soundscapes.txt</c> and appear in no demo message.
///
/// Measured on cp_process 2026-08-25: after the timeline's 395 sounds were precached,
/// `ambient/indoors.wav` still cost **103 ms in one frame** — the largest single stall left, and a
/// soundscape wave every time.
///
/// Device-free and install-free, so it runs where the gate runs.
/// </remarks>
public sealed class SoundscapeWavePrecacheTests
{
    [Test]
    public void WaveNames_ForOneSoundscape_NamesEveryLoopItPlays()
    {
        SoundscapeCatalog catalog = SoundscapeCatalog.ForSoundscapes(
            [Room("Gorge.Inside", "ambient/indoors.wav", "ambient/machine_hum.wav")]);

        catalog.WaveNames().OrderBy(name => name, System.StringComparer.Ordinal).ShouldBe(
            ["ambient/indoors.wav", "ambient/machine_hum.wav"]);
    }

    [Test]
    public void WaveNames_AcrossSeveralSoundscapes_NamesThemAll()
    {
        // **Every soundscape, not only the one the listener is in.** Which soundscape is active is a
        // runtime fact that changes as a player walks, and a seek can land anywhere — so a precache
        // that loaded only the current one would still hitch on the next doorway.
        SoundscapeCatalog catalog = SoundscapeCatalog.ForSoundscapes(
        [
            Room("Gorge.Inside", "ambient/indoors.wav"),
            Room("Gorge.Outside", "ambient/outdoors.wav"),
        ]);

        catalog.WaveNames().OrderBy(name => name, System.StringComparer.Ordinal).ShouldBe(
            ["ambient/indoors.wav", "ambient/outdoors.wav"]);
    }

    [Test]
    public void WaveNames_WithOneWaveSharedByTwoSoundscapes_NamesItOnce()
    {
        // cp_process has 21 entities naming `Gorge.Inside`, and neighbouring soundscapes share
        // waves. Decoding one twice is the cost this list exists to remove.
        SoundscapeCatalog catalog = SoundscapeCatalog.ForSoundscapes(
        [
            Room("Gorge.Inside", "ambient/indoors.wav"),
            Room("Gorge.Tunnel", "ambient/indoors.wav"),
        ]);

        catalog.WaveNames().ShouldBe(["ambient/indoors.wav"]);
    }

    [Test]
    public void WaveNames_ForASoundscapeThatLoopsNothing_NamesNothingForIt()
    {
        // **The control.** A soundscape may carry only rules this reader does not implement
        // (`playrandom`, `playsoundscape`), leaving `Looping` empty. It must contribute nothing
        // rather than an empty string, which would reach the decoder as a file to open.
        SoundscapeCatalog catalog = SoundscapeCatalog.ForSoundscapes(
        [
            new Soundscape("Gorge.Silent", 1, [], ["playrandom"]),
            Room("Gorge.Inside", "ambient/indoors.wav"),
        ]);

        catalog.WaveNames().ShouldBe(["ambient/indoors.wav"]);
    }

    [Test]
    public void WaveNames_ForAnEmptyCatalog_IsEmpty()
    {
        // A viewer with no TF2 install loads an empty catalog rather than throwing, so the precache
        // must survive it. This is the state every machine without the game is in.
        SoundscapeCatalog.ForSoundscapes([]).WaveNames().ShouldBeEmpty();
    }

    private static Soundscape Room(string name, params string[] waves) =>
        new(name, 1, [.. waves.Select(wave => new SoundscapeSound(wave))], []);
}
