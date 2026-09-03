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

    /// <summary>Which classes play a landing gesture — <c>DontDoNewJump</c>.</summary>
    /// <remarks>
    /// **A branch built on an assumed flag is worse than no branch**, so this measures rather than
    /// assumes — and the measurement corrected the assumption. `DontDoNewJump`
    /// (`tf_classdata.cpp:188`) gates `RestartGesture( GESTURE_SLOT_JUMP, ACT_MP_JUMP_LAND )` and
    /// nothing else (`tf_playeranimstate.cpp:1507`).
    ///
    /// **Two classes set it: the soldier and the medic.** The guess written here first was that
    /// none did, which would have made the gate unreachable and the code that reads it dead. It is
    /// neither: a soldier who rocket-jumps and a medic never play the landing gesture, and a viewer
    /// that gave them one would be adding an animation TF2 does not.
    ///
    /// The medic appearing in both this list and the air-walk one is not a coincidence to smooth
    /// over — it is the same class script saying it does neither, and reading both from one pass is
    /// what makes each answer checkable against the other.
    /// </remarks>
    [Test]
    public void ClassScripts_TheSoldierAndMedic_PlayNoLandingGesture()
    {
        if (Reader() is not { } read)
        {
            Assert.Ignore("the game is not installed");
            return;
        }

        PlayerClassModels classes = PlayerClassModels.Read(read);

        classes.Model(PlayerClassModels.FirstClass)
            .ShouldNotBeNull("the class scripts must be readable for this to measure anything");

        bool[] answers =
        [
            .. Enumerable
                .Range(PlayerClassModels.FirstClass, PlayerClassModels.LastPlayingClass)
                .Select(classes.Lands),
        ];

        TestContext.Out.WriteLine(
            $"LANDS {string.Join(", ", answers.Select(one => one ? "yes" : "no"))}");

        // Asserted as a set rather than by class number, for the same reason as its neighbour: a
        // reader answering a constant fails here, where naming one class would not.
        answers.ShouldContain(true, "most classes play a landing gesture");

        answers.Count(one => !one).ShouldBe(
            2,
            "the soldier and the medic set DontDoNewJump — if this count moves, TF2 has changed " +
            "and the gate in PlayerProps.Landing now applies to a different set of classes");
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
