using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

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
                .Select(playerClass => $"{playerClass}:{(classes.ScriptOf(playerClass).DontDoAirwalk ? "no" : "yes")}"));

        TestContext.Out.WriteLine($"AIRWALK {reported}");

        // Asserted as a set rather than one class, so a reader that answered a constant fails: at
        // least one class must opt out and at least one must not.
        bool[] answers =
        [
            .. Enumerable
                .Range(PlayerClassModels.FirstClass, PlayerClassModels.LastPlayingClass)
                .Select(playerClass => !classes.ScriptOf(playerClass).DontDoAirwalk),
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
                .Select(playerClass => !classes.ScriptOf(playerClass).DontDoNewJump),
        ];

        TestContext.Out.WriteLine(
            $"LANDS {string.Join(", ", answers.Select(one => one ? "yes" : "no"))}");

        // Asserted as a set rather than by class number, for the same reason as its neighbour: a
        // reader answering a constant fails here, where naming one class would not.
        answers.ShouldContain(true, "most classes play a landing gesture");

        answers.Count(one => !one).ShouldBe(
            2,
            "the soldier and the medic set DontDoNewJump — if this count moves, TF2 has changed " +
            "and the timeline's bNewJump gate now applies to a different set of classes");
    }

    /// <summary>Which class models have the crouch walk each player table translates to (B437).</summary>
    /// <remarks>
    /// **`bInDuck` is the model's as well as the flag's** (`tf_playeranimstate.cpp:971-975`, `:1429-1433`). The loser's
    /// table rewrites the crouch walk to `ACT_MP_CROUCHWALK_LOSERSTATE`, which no class model declares, so a humiliated
    /// player never ducks; the carrier's rewrites it to `ACT_MP_CROUCHWALK_BUILDING_DEPLOYED`, which the engineer's
    /// model does declare — the control. A table that leaves the crouch walk to the weapon is the next test's.
    /// </remarks>
    [Test]
    public void HasCrouchWalk_TheShippedClassModels_LackOnlyTheLosersCrouchWalk()
    {
        if (Reader() is not { } read)
        {
            Assert.Ignore("the game is not installed");
            return;
        }

        PlayerClassModels models = PlayerClassModels.Read(read);
        ClassAnimation classes = new(models, items: null, read);
        int[] all = [.. Enumerable.Range(PlayerClassModels.FirstClass, PlayerClassModels.LastPlayingClass)];
        const int Engineer = 9;

        all.ShouldAllBe(playerClass => models.Model(playerClass) != null, "every class script must be read");
        all.ShouldAllBe(playerClass => !classes.HasCrouchWalk(playerClass, PlayerActivityOverride.LoserState, null, null, 0));
        classes.HasCrouchWalk(Engineer, PlayerActivityOverride.BuildingDeployed, null, null, 0).ShouldBeTrue();
    }

    /// <summary>The weapon in hand, through its script's role and its item's `anim_slot` (B437).</summary>
    /// <remarks>
    /// **Real scripts, real items, real models.** A rocket launcher's script says primary, and the spy's model has no
    /// `ACT_MP_CROUCHWALK_PRIMARY`; the Gunslinger's `anim_slot` is `item2`, which the engineer's model has. The
    /// soldier holding the launcher is the control on the class.
    /// </remarks>
    [Test]
    public void HasCrouchWalk_TheHeldWeapon_IsTranslatedThroughItsRoleAndItem()
    {
        // items_game.txt ships loose in the tf folder, not in a VPK.
        string loose = Path.Combine(Game, "scripts", "items", "items_game.txt");

        if (Reader() is not { } read || !File.Exists(loose))
        {
            Assert.Ignore("the game is not installed");
            return;
        }

        ClassAnimation classes = new(PlayerClassModels.Read(read), ItemSchema.Read(File.ReadAllBytes(loose)), read);
        const int Soldier = 3;
        const int Spy = 8;
        const int Engineer = 9;
        const int RocketLauncher = 18;
        const int Gunslinger = 142;
        const int StickybombLauncher = 20;

        classes.HasCrouchWalk(Spy, PlayerActivityOverride.None, "CTFRocketLauncher", RocketLauncher, 2).ShouldBeFalse();
        classes.HasCrouchWalk(Soldier, PlayerActivityOverride.None, "CTFRocketLauncher", RocketLauncher, 2).ShouldBeTrue();
        classes.HasCrouchWalk(Engineer, PlayerActivityOverride.None, "CTFRobotArm", Gunslinger, 2).ShouldBeTrue();

        // The item outranks the script: the stickybomb launcher's script says secondary, which the spy's model has,
        // and its `anim_slot` says primary, which it does not. Without the item the answer would be yes.
        classes.HasCrouchWalk(Spy, PlayerActivityOverride.None, "CTFPipebombLauncher", StickybombLauncher, 2).ShouldBeFalse();
        classes.HasCrouchWalk(Spy, PlayerActivityOverride.None, "CTFPipebombLauncher", null, 2).ShouldBeTrue();
        classes.HasCrouchWalk(Spy, PlayerActivityOverride.None, null, null, 2)
            .ShouldBeFalse("no weapon: ACT_MP_CROUCHWALK itself, which no class model names");
    }

    /// <summary>Which class models lack the crouch walk a weapon role translates to (B437).</summary>
    /// <remarks>
    /// **Measured, and the check is far from dead**: 49 of the 117 class-and-role pairs have no sequence for the
    /// role's crouch walk — every class but the engineer lacks `ACT_MP_CROUCHWALK_PDA` and `_BUILDING`, the spy lacks
    /// `_PRIMARY`, the soldier and medic lack `_ITEM1`. Asserted on the pairs that decide something, with controls:
    /// the all-class melee every class has, and the engineer's own PDA and building tables.
    /// </remarks>
    [Test]
    public void DeclaresActivity_EveryRolesCrouchWalk_MeasuredPerClass()
    {
        if (Reader() is not { } read)
        {
            Assert.Ignore("the game is not installed");
            return;
        }

        PlayerClassModels classes = PlayerClassModels.Read(read);
        List<string> missing = [];

        foreach (int playerClass in Enumerable.Range(PlayerClassModels.FirstClass, PlayerClassModels.LastPlayingClass))
        {
            foreach (string role in WeaponActivityTable.Roles.Order(StringComparer.Ordinal))
            {
                string crouchWalk = WeaponActivityTable.Override(role, "ACT_MP_CROUCHWALK");

                if (!classes.DeclaresActivity(playerClass, crouchWalk))
                {
                    missing.Add($"{playerClass}:{role}");
                }
            }
        }

        TestContext.Out.WriteLine($"MISSING {missing.Count}: {string.Join(", ", missing)}");

        missing.Count.ShouldBe(49, "if this moves, TF2's class models have changed");
        missing.ShouldContain("8:PRIMARY");
        missing.ShouldContain("3:ITEM1");
        missing.ShouldContain("1:PDA");
        missing.ShouldNotContain("9:PDA", "the control: the engineer crouch-walks with his PDA");
        missing.ShouldNotContain("9:BUILDING");
        missing.ShouldNotContain(item => item.EndsWith(":MELEEALLCLASS", StringComparison.Ordinal));
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
