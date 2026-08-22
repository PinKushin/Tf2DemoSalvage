using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Tf2DemoSalvage.Audio.Tests;

/// <summary>
/// Opening a sound a demo named, when the install now ships it in another container.
/// </summary>
/// <remarks>
/// **Synthetic, and the cases come from a real measurement.** The corpus probe found 63 distinct
/// played sounds that could not be opened by their stated path and 60 of them present as MP3 under
/// the identical stem — `sound/vo/scout_BattleCry01.wav` shipping today as
/// `sound/vo/scout_BattleCry01.mp3`. Those names are used verbatim below.
///
/// Written this way deliberately: the corpus version of this check needs a TF2 install, so on CI
/// and the measurement boxes it would skip and verify nothing. A fake reader runs everywhere in
/// microseconds and can express cases no install contains — a stem present in BOTH containers, a
/// path with no extension — which is the whole argument for preferring synthetic here.
/// </remarks>
public sealed class SoundFileTests
{
    [Test]
    public void Candidates_AWavPath_TriesTheMp3Sibling()
    {
        List<string> tried = [.. SoundFile.Candidates("sound/vo/scout_BattleCry01.wav")];

        tried.ShouldBe(["sound/vo/scout_BattleCry01.wav", "sound/vo/scout_BattleCry01.mp3"]);
    }

    [Test]
    public void Candidates_AnMp3Path_TriesTheWavSibling()
    {
        // The reverse direction has to work too: a modern demo may name an MP3 while a user's
        // custom content ships the WAV. Handling only one direction would be an assumption about
        // which way the re-encoding went, and the corpus only measured one of them.
        List<string> tried = [.. SoundFile.Candidates("sound/vo/announcer_time_added.mp3")];

        tried.ShouldBe(["sound/vo/announcer_time_added.mp3", "sound/vo/announcer_time_added.wav"]);
    }

    [Test]
    public void Candidates_TheStatedPath_IsAlwaysFirst()
    {
        // A file that still exists under its own name is the one the demo meant. If the fallback
        // ever came first, every sound present in both containers would play the re-encode — which
        // sounds almost right and so would never be noticed.
        SoundFile.Candidates("a/b.wav").First().ShouldBe("a/b.wav");
        SoundFile.Candidates("a/b.mp3").First().ShouldBe("a/b.mp3");
    }

    [Test]
    public void Candidates_APathWithNoExtension_HasNoAlternatives()
    {
        // Appending ".mp3" to something that was never a sound file turns a resolution failure into
        // a wrong read, which is strictly worse: one is reported, the other plays.
        List<string> tried = [.. SoundFile.Candidates("sound/vo/scout_BattleCry01")];

        tried.ShouldBe(["sound/vo/scout_BattleCry01"]);
    }

    [Test]
    public void Open_AWavAbsentButShippingAsMp3_ReturnsTheMp3()
    {
        // The measured case, and the reason this type exists: 60 of the corpus's 63 unopenable
        // sounds are exactly this.
        (byte[] Bytes, string Path)? found = SoundFile.Open(
            "sound/vo/scout_BattleCry01.wav",
            Fake(new() { ["sound/vo/scout_BattleCry01.mp3"] = "mp3 bytes" }));

        found.ShouldNotBeNull();
        found.Value.Path.ShouldBe("sound/vo/scout_BattleCry01.mp3");
        Encoding.UTF8.GetString(found.Value.Bytes).ShouldBe("mp3 bytes");
    }

    [Test]
    public void Open_AStemInBothContainers_PrefersTheStatedOne()
    {
        // **The control**, and without it the test above cannot distinguish "fell back correctly"
        // from "always uses the mp3". No install contains this case for these names, which is
        // exactly why it is written rather than hunted for.
        (byte[] Bytes, string Path)? found = SoundFile.Open(
            "sound/vo/scout_BattleCry01.wav",
            Fake(new()
            {
                ["sound/vo/scout_BattleCry01.wav"] = "wav bytes",
                ["sound/vo/scout_BattleCry01.mp3"] = "mp3 bytes",
            }));

        found.ShouldNotBeNull();
        found.Value.Path.ShouldBe("sound/vo/scout_BattleCry01.wav");
        Encoding.UTF8.GetString(found.Value.Bytes).ShouldBe("wav bytes");
    }

    [Test]
    public void Open_NeitherContainerPresent_IsNull()
    {
        // Three of the corpus's 63 are genuinely absent under any extension —
        // player/pl_fallpain4, 8 and 10 — so this is a real state, not a defensive branch.
        SoundFile.Open("sound/player/pl_fallpain4.wav", Fake([])).ShouldBeNull();
    }

    [Test]
    public void Open_AReaderThatIsNull_Throws()
    {
        Should.Throw<ArgumentNullException>(
            () => SoundFile.Open("a/b.wav", null!));
    }

    /// <summary>A reader over an in-memory set of files, standing in for the game's archives.</summary>
    /// <remarks>
    /// <c>byte[]?</c> rather than <c>ReadOnlyMemory&lt;byte&gt;?</c>, because the latter converts a
    /// null array into an EMPTY memory that reads as present — see
    /// `docs/memory/nullable-pattern-on-a-struct-is-dead-code.md`, which was written after that bug
    /// reached this repository.
    /// </remarks>
    private static Func<string, byte[]?> Fake(Dictionary<string, string> files) =>
        path => files.TryGetValue(path, out string? text) ? Encoding.UTF8.GetBytes(text) : null;
}
