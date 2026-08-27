using System;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// Which classes air-walk, read from the game's own class scripts.
/// </summary>
/// <remarks>
/// **<c>ACT_MP_AIRWALK</c> supersedes the jump for a fast-rising player**, in
/// <c>CTFPlayerAnimState::HandleJumping</c> — but only for a class whose script does not set
/// <c>DontDoAirwalk</c> (<c>tf_classdata.cpp:187</c>). So this decides which of two animations a
/// rocket-jumping soldier is drawn with, and it is the game's data rather than a choice.
///
/// **Measured before it was asserted**, because guessing which classes opt out is exactly the kind
/// of plausible-sounding assumption that reads correctly and animates wrongly.
/// </remarks>
public sealed class ClassAirwalkTests
{
    private static string Game => GameInstall.Require();

    [Test]
    public void ClassScripts_AirWalkingClasses_AreDeclared()
    {
        if (Reader() is not { } read)
        {
            Assert.Ignore("the game is not installed");
            return;
        }

        PlayerClassModels classes = PlayerClassModels.Read(read);

        // The control: the scripts must actually have been found and decrypted, or every answer
        // below is the default rather than a reading.
        classes.Model(PlayerClassModels.FirstClass)
            .ShouldNotBeNull("the class scripts must be readable for this to measure anything");

        string reported = string.Join(
            ", ",
            Enumerable
                .Range(PlayerClassModels.FirstClass, PlayerClassModels.LastPlayingClass)
                .Select(playerClass => $"{playerClass}:{(classes.Airwalks(playerClass) ? "yes" : "no")}"));

        TestContext.Out.WriteLine($"AIRWALK {reported}");

        // Asserted as a set rather than one class, so a reader that answered a constant fails: at
        // least one class must opt out and at least one must not.
        bool[] answers =
        [
            .. Enumerable
                .Range(PlayerClassModels.FirstClass, PlayerClassModels.LastPlayingClass)
                .Select(classes.Airwalks),
        ];

        answers.ShouldContain(true, "some classes air-walk");
        answers.ShouldContain(false, "and some opt out, or DontDoAirwalk is not being read");
    }

    /// <summary>Reads a file out of the installed game, or null when it is absent.</summary>
    private static Func<string, byte[]?>? Reader()
    {
        if (!Directory.Exists(Game))
        {
            return null;
        }

        VpkArchive[] archives =
        [
            .. new[] { "tf2_misc_dir.vpk", "tf2_textures_dir.vpk" }
                .Select(name => Path.Combine(Game, name))
                .Where(File.Exists)
                .Select(VpkArchive.Open),
        ];

        return archives.Length == 0
            ? null
            : path => archives.Select(archive => archive.ReadFile(path)).FirstOrDefault(f => f is not null);
    }
}
