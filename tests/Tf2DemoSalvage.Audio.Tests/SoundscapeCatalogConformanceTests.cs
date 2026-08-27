using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Audio.Tests;

/// <summary>
/// The soundscape list, checked against one a running TF2 client printed.
/// </summary>
/// <remarks>
/// **A demo carries an index, so this list's ORDER is the feature** (B173). Get it wrong and the
/// viewer plays the wrong ambience rather than none — a plausible sound instead of an error, which
/// is the failure this project is least able to notice.
///
/// **So the expectation comes from the engine, not from this project's reading of it.** The owner
/// ran `cl_soundscape_printdebuginfo` in TF2, which prints every entry as `- %d: %s`
/// (`c_soundscape.cpp:146`), and the result is committed beside this test. That makes it a
/// differential rather than a fixture: it can disagree with us, which a list written from the same
/// SDK pages as the implementation could not.
///
/// Two independent confirmations came with it. The client reported **153** entries, 0 through 152,
/// and separately reported `soundscape index: 0` while the owner stood in cp_process's respawn room
/// — which is the entry this list independently puts at 0.
/// </remarks>
public sealed class SoundscapeCatalogConformanceTests
{
    private static string Game => GameInstall.Require();

    /// <summary>The client's own dump, as captured from `cl_soundscape_printdebuginfo`.</summary>
    private static string[] Expected =>
        File.ReadAllLines(
            Path.Combine(TestContext.CurrentContext.TestDirectory, "Data", "client-soundscapes.txt"));

    private static SoundscapeCatalog? Catalog()
    {
        if (!Directory.Exists(Game))
        {
            Assert.Ignore("Team Fortress 2 is not installed");
            return null;
        }

        // No map name: cp_process ships no soundscapes_cp_process.txt, and the client's dump was
        // taken on that map — so the list under test is the manifest's alone, as the client's was.
        return SoundscapeCatalog.Load(GameArchives.Open(Game).Read);
    }

    [Test]
    public void Load_TheShippedManifest_ReproducesTheClientsListExactly()
    {
        if (Catalog() is not { } catalog)
        {
            return;
        }

        string[] expected = Expected;

        TestContext.Out.WriteLine(
            $"client {expected.Length}, ours {catalog.Count.ToString(CultureInfo.InvariantCulture)}");

        // **The count first, because a mismatch here explains every name mismatch below.** A file
        // the manifest names but that will not open shortens our list and shifts every index after
        // it — the one silent failure this design has.
        catalog.Count.ShouldBe(
            expected.Length,
            "a different count means every index past the divergence points at the wrong soundscape");

        // **Every entry, not a sample.** Spot-checking the ends would pass while the middle was
        // rotated, and a rotation is exactly what a mis-ordered manifest produces.
        List<string> mismatches = [];

        for (int index = 0; index < expected.Length; index++)
        {
            string want = expected[index];
            string got = $"{index.ToString(CultureInfo.InvariantCulture)}: {catalog.Soundscapes[index].Name}";

            if (!want.Equals(got, StringComparison.OrdinalIgnoreCase))
            {
                mismatches.Add($"client '{want}' vs ours '{got}'");
            }
        }

        mismatches.ShouldBeEmpty(
            $"{mismatches.Count} of {expected.Length} entries differ from the client's own list");
    }

    [Test]
    public void At_IndexZero_IsTheRespawnRoomTheClientReported()
    {
        if (Catalog() is not { } catalog)
        {
            return;
        }

        // **The one index confirmed twice**: the owner stood in cp_process's respawn room, and
        // `soundscape_dumpclient` answered "soundscape index: 0". Pinned by name so a reordering
        // that kept the count fails here even if the diff above were ever relaxed.
        catalog.At(0).ShouldNotBeNull();
        catalog.At(0)!.Name.ShouldBe("tf2.respawn_room");
    }

    [Test]
    public void At_IndexFortyTwo_IsTheGorgeInteriorTheClientReported()
    {
        if (Catalog() is not { } catalog)
        {
            return;
        }

        // **A second confirmed index, from a different place on the same map.** The owner ran
        // `soundscape_dumpclient` just outside cp_process's spawn — "soundscape index: 42" at
        // (-3469, -2034, 576) — and this list independently puts `Gorge.Inside` there.
        //
        // Worth keeping for what it shows about the data as much as the ordering: cp_process ships
        // no soundscape file of its own, so its `env_soundscape` entities name soundscapes authored
        // for other maps. An implementation that expected a map to use only its own would find
        // nothing here.
        Soundscape gorge = catalog.At(42)!;

        gorge.Name.ShouldBe("Gorge.Inside");

        foreach (SoundscapeSound sound in gorge.Looping)
        {
            TestContext.Out.WriteLine(
                $"  [42] {sound.Wave} vol {sound.Volume.ToString("0.##", CultureInfo.InvariantCulture)} " +
                $"position {sound.Position?.ToString(CultureInfo.InvariantCulture) ?? "-"}");
        }

        TestContext.Out.WriteLine(
            $"  [42] dsp {gorge.Dsp.ToString(CultureInfo.InvariantCulture)}, " +
            $"unimplemented rules: {(gorge.OtherRules.Count == 0 ? "none" : string.Join(", ", gorge.OtherRules))}");
    }

    [Test]
    public void At_AnIndexOutsideTheList_IsNothingRatherThanAnError()
    {
        if (Catalog() is not { } catalog)
        {
            return;
        }

        // -1 is the engine's own "no soundscape" — CEnvSoundscape initialises m_soundscapeIndex to
        // it (soundscape.cpp:105), so a player who has not entered one carries it. It must read as
        // silence rather than as a fault.
        catalog.At(-1).ShouldBeNull();
        catalog.At(catalog.Count).ShouldBeNull();
    }

    [Test]
    public void Load_TheRespawnRoom_CarriesItsThreeLoopsAndTheirVolumes()
    {
        if (Catalog() is not { } catalog)
        {
            return;
        }

        Soundscape room = catalog.At(0)!;

        foreach (SoundscapeSound sound in room.Looping)
        {
            TestContext.Out.WriteLine(
                $"  {sound.Wave} vol {sound.Volume.ToString("0.##", CultureInfo.InvariantCulture)} " +
                $"pitch {sound.Pitch.ToString(CultureInfo.InvariantCulture)} " +
                $"position {sound.Position?.ToString(CultureInfo.InvariantCulture) ?? "-"} " +
                $"attenuation {sound.Attenuation?.ToString("0.##", CultureInfo.InvariantCulture) ?? "-"}");
        }

        // **Three, and the count is the assertion that matters.** `playlooping` appears three times
        // in this one section, so a reader that keyed rule blocks by name would keep one and leave
        // the room with a third of its ambience — no error, just a thin sound. Exactly the trap a
        // streaming reader avoids and a dictionary-shaped one falls into.
        room.Looping.Count.ShouldBe(3, "tf2.respawn_room declares three playlooping blocks");

        room.Looping.Select(sound => sound.Wave).ShouldBe(
            new[]
            {
                "ambient/underground.wav",
                "ambient/machine_hum2.wav",
                "ambient/machine_hum.wav",
            },
            "in script order");

        // Volumes as written, including the leading-dot decimals the scripts use — ".6", ".30",
        // ".75". Reading those under a comma-decimal culture would give 6, 30 and 75.
        room.Looping[0].Volume.ShouldBe(0.6f, 0.001d);
        room.Looping[1].Volume.ShouldBe(0.30f, 0.001d);
        room.Looping[2].Volume.ShouldBe(0.75f, 0.001d);

        // The third carries the only position and attenuation in the section, which is what makes
        // it the one placed in the world rather than at the listener.
        room.Looping[2].Position.ShouldBe(0);
        room.Looping[2].Attenuation.ShouldNotBeNull();
        room.Looping[2].Attenuation!.Value.ShouldBe(0.7f, 0.001d);

        room.Dsp.ShouldBe(1, "the respawn room asks for DSP 1, 'Generic'");
    }
}
