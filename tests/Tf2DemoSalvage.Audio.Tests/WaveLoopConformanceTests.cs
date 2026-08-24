using System;
using System.IO;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Audio.Tests;

/// <summary>
/// Whether a wave loops, which Source marks with a <c>cue </c> chunk.
/// </summary>
/// <remarks>
/// **`WAVE_CUE` is the marker** — <c>tier2/riff.h:187</c> and <c>soundcombiner.cpp:361</c>. Its
/// presence is the loop; the first cue point's sample offset is where playback returns to.
///
/// **Without it the map goes silent, and the failure looks like a fixed bug.** Ambience is started
/// once and expected to run for the whole match: six `)ambient/machine_hum` on cp_process, all at
/// the recording's first tick. Played once each they are over in seconds. That was masked while
/// ambience was wrongly played at full volume everywhere, and surfaced the moment attenuation was
/// corrected — the owner heard the corrected version as "now i dont hear the ambient at all"
/// (B169).
///
/// Measured against the shipped files rather than a fixture, because the claim is about what Valve
/// authored: a synthetic wave with a cue chunk would only prove the reader parses what this test
/// wrote.
/// </remarks>
public sealed class WaveLoopConformanceTests
{
    private const string Game = "F:/SteamLibrary/steamapps/common/Team Fortress 2/tf";

    /// <summary>An ambient loop, and the sound the owner reported missing.</summary>
    private const string Looping = "sound/ambient/machine_hum.wav";

    /// <summary>A one-shot, as the control.</summary>
    private const string OneShot = "sound/items/gunpickup2.wav";

    private static byte[]? Read(string path)
    {
        if (!Directory.Exists(Game))
        {
            Assert.Ignore("Team Fortress 2 is not installed");
            return null;
        }

        return GameArchives.Open(Game).Read(path);
    }

    [Test]
    public void Read_AnAmbientLoop_IsMarkedLooping()
    {
        if (Read(Looping) is not { } file)
        {
            Assert.Ignore($"{Looping} is not in this install");
            return;
        }

        RiffWave? wave = RiffWave.Read(file);

        wave.ShouldNotBeNull("the ambient hum should read as a wave");

        TestContext.Out.WriteLine(
            $"{Looping}: loops {wave.Value.Loops}, loop start {wave.Value.LoopStart}");

        wave.Value.Loops.ShouldBeTrue(
            "ambient/machine_hum is an ambient_generic loop; without the cue chunk being read it " +
            "plays once and the map falls silent");
    }

    [Test]
    public void Read_AOneShot_IsNotMarkedLooping()
    {
        if (Read(OneShot) is not { } file)
        {
            Assert.Ignore($"{OneShot} is not in this install");
            return;
        }

        RiffWave? wave = RiffWave.Read(file);

        wave.ShouldNotBeNull();

        TestContext.Out.WriteLine(
            $"{OneShot}: loops {wave.Value.Loops}, loop start {wave.Value.LoopStart}");

        // **The control, and it is what separates "reads the cue chunk" from "returns true".** A
        // reader that always looped would satisfy the test above and turn every gunshot in the game
        // into a drone — which is the loudest possible way to be wrong, and would still pass.
        wave.Value.Loops.ShouldBeFalse("a one-shot effect carries no cue chunk and must not loop");
    }
}
