using System;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// Which model each player class wears, read from the game's own class scripts.
/// </summary>
/// <remarks>
/// **A player's model is not in the demo, and this is why.**
/// <c>CTFPlayerClassShared::GetModelName</c> returns
/// <c>GetPlayerClassData(m_iClass)->GetModelName()</c> — the client looks it up locally from the
/// class number. Only <c>m_iszCustomModel</c> travels on the wire. So a recording carries the
/// class and nothing else, and the model has to come from the installed game.
///
/// **Read rather than hardcoded.** <c>tf_classdata.cpp</c> parses
/// <c>scripts/playerclasses/&lt;class&gt;.txt</c> and takes the <c>"model"</c> key; hardcoding the
/// nine paths would be a table that silently goes stale when Valve changes one, and would also be
/// wrong for any mod. The files live inside a VPK rather than loose on disk.
///
/// The class numbering is the engine's, from <c>tf_shareddefs.h</c>, and it is **not** the order
/// the class-selection menu shows: Sniper is 2 and Soldier is 3.
/// </remarks>
public sealed class PlayerClassModelsTests
{
    private static string? GameFolder
    {
        get
        {
            if (Environment.GetEnvironmentVariable("TF2_FOLDER") is { Length: > 0 } configured &&
                Directory.Exists(configured))
            {
                return configured;
            }

            string[] candidates =
            [
                @"C:\Program Files (x86)\Steam\steamapps\common\Team Fortress 2\tf",
                @"F:\SteamLibrary\steamapps\common\Team Fortress 2\tf",
                @"D:\SteamLibrary\steamapps\common\Team Fortress 2\tf",
            ];

            foreach (string candidate in candidates)
            {
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }
    }

    private string _tf = string.Empty;

    [SetUp]
    public void RequireTheGame()
    {
        if (GameFolder is not { } folder)
        {
            Assert.Ignore("Team Fortress 2 is not installed; set TF2_FOLDER to run these.");
            return;
        }

        _tf = folder;
    }

    [Test]
    public void PlayerClassModels_EveryPlayingClass_ResolvesToAModelTheGameHas()
    {
        VpkArchive misc = VpkArchive.Open(Path.Combine(_tf, "tf2_misc_dir.vpk"));

        PlayerClassModels models = PlayerClassModels.Read(misc.ReadFile);

        for (int playerClass = PlayerClassModels.FirstClass;
             playerClass <= PlayerClassModels.LastPlayingClass;
             playerClass++)
        {
            string? model = models.Model(playerClass);

            model.ShouldNotBeNull($"class {playerClass} has no model");
            model.ShouldStartWith("models/", customMessage: $"class {playerClass}");

            // The model has to exist, not merely be named. A script naming a file the install does
            // not carry would otherwise pass here and draw nothing later.
            misc.ReadFile(model).ShouldNotBeNull($"class {playerClass} names a missing model: {model}");
        }
    }

    [Test]
    public void PlayerClassModels_TheClassNumbering_IsTheEnginesNotTheMenus()
    {
        // **The trap this test exists for.** tf_shareddefs.h orders the enum Scout, Sniper,
        // Soldier, Demoman, Medic, Heavy, Pyro, Spy, Engineer - which is not the order the class
        // menu shows and not the order anyone would guess. Getting it wrong labels every player
        // with the wrong class while looking entirely plausible, and both numbers are valid, so
        // nothing errors.
        VpkArchive misc = VpkArchive.Open(Path.Combine(_tf, "tf2_misc_dir.vpk"));

        PlayerClassModels models = PlayerClassModels.Read(misc.ReadFile);

        models.Model(1).ShouldBe("models/player/scout.mdl");
        models.Model(2).ShouldBe("models/player/sniper.mdl");
        models.Model(3).ShouldBe("models/player/soldier.mdl");
        models.Model(9).ShouldBe("models/player/engineer.mdl");
    }

    [Test]
    public void AClassTheGameDoesNotDefine_HasNoModel()
    {
        // Null rather than a fallback to Scout. The engine does default the undefined class to
        // scout.mdl - "Undefined players still need a model" - but that is a rendering decision
        // the caller should make knowingly, not one hidden in a lookup that then reports every
        // unknown class as a Scout.
        VpkArchive misc = VpkArchive.Open(Path.Combine(_tf, "tf2_misc_dir.vpk"));

        PlayerClassModels models = PlayerClassModels.Read(misc.ReadFile);

        models.Model(0).ShouldBeNull();
        models.Model(99).ShouldBeNull();
    }

    [Test]
    public void PlayerClassModels_EveryClass_GetsADistinctModel()
    {
        // The control against a reader that finds one script and reuses it: nine classes wearing
        // the Scout model would satisfy every assertion above.
        VpkArchive misc = VpkArchive.Open(Path.Combine(_tf, "tf2_misc_dir.vpk"));

        PlayerClassModels models = PlayerClassModels.Read(misc.ReadFile);

        HashSet<string> distinct = [];

        for (int playerClass = PlayerClassModels.FirstClass;
             playerClass <= PlayerClassModels.LastPlayingClass;
             playerClass++)
        {
            distinct.Add(models.Model(playerClass)!);
        }

        distinct.Count.ShouldBe(PlayerClassModels.LastPlayingClass);
    }
}
