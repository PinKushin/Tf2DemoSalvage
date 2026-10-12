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

    /// <summary>Every shipped class air-walks and jumps the new way: both opt-outs are commented out (B523).</summary>
    /// <remarks>
    /// **This asserted the opposite for months, and the reader was what was wrong.** It "measured" the medic opting
    /// out of the air-walk and the soldier and medic setting `DontDoNewJump` — from a scan that found a quoted key
    /// anywhere in the text. The shipped `medic.ctx` reads `//"DontDoAirwalk"	"1"` and both it and `soldier.ctx`
    /// read `//"DontDoNewJump"	"1"`: comments, which `KeyValues` never sees (`ClassScriptConformanceTests`). The
    /// two tests that stood here asserted "some class opts out" as a guard against a reader answering a constant, so
    /// the guard itself held the wrong reading in place; the control is the model key instead, which no constant
    /// answers. What should have stopped it: `soldier.mdl` has no sequence for `ACT_MP_JUMP_PRIMARY`
    /// (`class-activities 3 ACT_MP_JUMP_PRIMARY`), so the reading meant every soldier jump in TF2 is a reference pose.
    /// </remarks>
    [Test]
    public void ClassScripts_EveryShippedClass_AirWalksAndJumpsTheNewWay()
    {
        if (Reader() is not { } read)
        {
            Assert.Ignore("the game is not installed");
            return;
        }

        PlayerClassModels classes = PlayerClassModels.Read(read);
        int[] all = [.. Enumerable.Range(PlayerClassModels.FirstClass, PlayerClassModels.LastPlayingClass)];

        // The control: the scripts were found, decrypted and read key by key.
        all.ShouldAllBe(playerClass => classes.Model(playerClass) != null, "every class script must be read");

        TestContext.Out.WriteLine($"SCRIPTS {string.Join(", ", all.Select(one => $"{one}:{classes.ScriptOf(one)}"))}");

        all.ShouldAllBe(playerClass => !classes.ScriptOf(playerClass).DontDoAirwalk, "TF2 has changed a class script");
        all.ShouldAllBe(playerClass => !classes.ScriptOf(playerClass).DontDoNewJump, "TF2 has changed a class script");
    }

    /// <summary>Which class models have the crouch walk each player table translates to (B437).</summary>
    /// <remarks>
    /// **`bInDuck` is the model's as well as the flag's** (`tf_playeranimstate.cpp:971-975`, `:1429-1433`). The loser's
    /// table rewrites the crouch walk to `ACT_MP_CROUCHWALK_LOSERSTATE`, which no class model declares, so a humiliated
    /// player never ducks; the carrier's rewrites it to `ACT_MP_CROUCHWALK_BUILDING_DEPLOYED`, which the engineer's
    /// model does declare — the control. A table that leaves the crouch walk to the weapon is the next test's.
    /// </remarks>
    [Test]
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
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
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
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
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
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
