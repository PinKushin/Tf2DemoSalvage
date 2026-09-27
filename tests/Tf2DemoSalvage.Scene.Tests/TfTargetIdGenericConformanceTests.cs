using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// `CTargetID::UpdateID`'s generic branch (tf_hud_target_id.cpp:926-998) and `IsValidIDTarget`'s `IsVisibleToTargetID`
/// (:487): a dropped weapon through `CTFPlayer::CanPickupDroppedWeapon` (tf_player_shared.cpp:14723) and
/// `GetDroppedWeaponInRange` (:14754), and a revive marker with `GetNextRespawnWave` (teamplayroundbased_gamerules.cpp:561).
/// </summary>
/// <remarks>
/// The local player is entity 1, a RED soldier holding shotgun 9 (definition 10, secondary). Entity 2 "Owner" has account 555.
/// Dropped weapon 60 is definition 20, a soldier secondary; marker 61 is RED, owned by entity 2.
/// </remarks>
public sealed class TfTargetIdGenericConformanceTests
{
    private const string ItemsGame = """
        "items_game"
        {
            "items"
            {
                "10" { "item_slot" "secondary" "item_class" "tf_weapon_shotgun_soldier" "used_by_classes" { "soldier" "1" } }
                "20" { "item_slot" "secondary" "item_class" "tf_weapon_shotgun_soldier" "item_rarity" "uncommon" "used_by_classes" { "soldier" "1" } }
                "21" { "item_slot" "melee" "item_class" "tf_weapon_shovel" "used_by_classes" { "soldier" "1" } }
                "22" { "item_slot" "primary" "item_class" "tf_weapon_medigun" "used_by_classes" { "soldier" "secondary" } }
            }
            "rarities" { "uncommon" { "value" "2" "color" "desc_uncommon" } }
            "colors" { "desc_uncommon" { "color_name" "ItemRarityUncommon" } }
        }
        """;

    [Test]
    public void UpdateId_ADroppedWeaponTheLocalPlayerCanTake_ShowsItsNameOwnerAndRarityColor()
    {
        TfMainTargetId id = Thought(Playing(Dropped()));

        id.TargetName.ShouldBe("Item20q6", "the full name, `%s1` (:957)");
        id.TargetData.ShouldBe("Owner dropped it", "#TF_WhoDropped with GetPlayerByAccountID's name (:966)");
        ((VguiLabel)id.FindChildByName("TargetNameLabel")!).FgColor.ShouldBe(((byte)10, (byte)20, (byte)30, (byte)255), "the rarity color (:972-974)");
    }

    [Test]
    public void UpdateId_NoPlayerHoldsTheAccount_LeavesTheDataLineAndDefaultColor()
    {
        TfMainTargetId id = Thought(Playing(Dropped() with { AccountId = 777u }));

        id.TargetName.ShouldBe("Item20q6");
        id.TargetData.ShouldBe(string.Empty, "\"Bots will not work here, so don't fill this out.\" (:962)");
        ((VguiLabel)id.FindChildByName("TargetNameLabel")!).FgColor.ShouldBe(((byte)255, (byte)255, (byte)255, (byte)255));
    }

    [Test]
    public void UpdateId_ADroppedMedigun_ShowsItsChargeRoundedHalfToEven()
    {
        // 0.625 * 100 = 62.5 exactly; `%.0f` rounds the tie to even (:950).
        TfMainTargetId id = Thought(Playing(Dropped() with { ItemDefinition = 22, ChargeLevel = 0.625f }));

        id.TargetName.ShouldBe("Item22q6 (62%)");
    }

    [Test]
    public void IsValidIdTarget_ADroppedWeaponForASlotTheLocalPlayerHasNoWeaponIn_IsNotATarget() =>
        Hidden(Playing(Dropped() with { ItemDefinition = 21 })).ShouldBeFalse("GetEntityForLoadoutSlot( melee ) is null (:14745)");

    [Test]
    public void IsValidIdTarget_AnInvalidItem_IsNotATarget() =>
        Hidden(Playing(Dropped() with { ItemValid = false })).ShouldBeFalse("!GetItem()->IsValid() (:14725)");

    [Test]
    public void IsValidIdTarget_ADisguisedSpy_CannotTakeIt() =>
        Hidden(Playing(Dropped(), playerClass: 8, conditions: new PlayerConditions(1 << 3, 0, 0, 0, 0)))
            .ShouldBeFalse("a spy disguised (:14729)");

    [Test]
    public void IsValidIdTarget_ATauntingPlayer_CannotTakeIt() =>
        Hidden(Playing(Dropped(), conditions: new PlayerConditions(1 << 7, 0, 0, 0, 0))).ShouldBeFalse("IsTaunting() (:14732)");

    [Test]
    public void IsValidIdTarget_ABowMidDraw_CannotTakeIt()
    {
        SceneItem bow = new(9, "CTFCompoundBow", 10, Wire, IsWeapon: true) { ChargeBeginTime = 3f };

        Hidden(Playing(Dropped(), active: bow)).ShouldBeFalse("CanPickupOtherWeapon: charge begin time non-zero (tf_weapon_compound_bow.h:81)");
    }

    [Test]
    public void UpdateId_TheWeaponInPickupRange_ShowsThePickupPrompt()
    {
        TfMainTargetId id = Thought(Playing(Dropped()) with { WeaponPickupTraceHit = 60, EyePosition = (0f, 0f, 150f) });

        id.FindChildByName("MoveableSubPanel")!.Visible.ShouldBeTrue("150 units is not `>` the range (:14771)");
        ((VguiIconPanel)id.FindChildByName("MoveableIcon", recurseDown: true)!).Icon!.TextureFile.ShouldBe("obj_weapon_pickup.vmt");
        ((VguiEditablePanel)id.FindChildByName("MoveableSubPanel")!).DialogVariable("movekey").ShouldBe("H", "+use_action_slot_item (:944)");
    }

    [Test]
    public void UpdateId_TheWeaponBeyondPickupRange_HidesThePrompt() =>
        Thought(Playing(Dropped()) with { WeaponPickupTraceHit = 60, EyePosition = (0f, 0f, 150.5f) })
            .FindChildByName("MoveableSubPanel")!.Visible.ShouldBeFalse("\"too far?\" (:14771)");

    [Test]
    public void UpdateId_ATeammatesReviveMarker_ShowsItsOwnerHealthAndRespawnWave()
    {
        // The next wave (120.5) is past the soonest spawn — death 0 + 2 + 0.4 + 4 + one unscaled 6 s wave — so it stands.
        TfMainTargetId id = Thought(Marker(serverTime: 100f, nextWave: 120.5f));

        id.TargetName.ShouldBe("Owner", "the owner's name (:994)");
        Level(id).ShouldBe("20", "(int)( 120.5 - 100 ) (:991-992)");
        id.TargetHealth.HealthImage.Health.ShouldBe(37f / 85f, 1e-6);
    }

    [Test]
    public void UpdateId_ARespawnWaveBeforeTheSoonestSpawn_AddsWholeWaves()
    {
        // 10 < 12.4, so one 6 s wave (eight players scale it by 1) is added: 16, less server time 5 (:592-595).
        Level(Thought(Marker(serverTime: 5f, nextWave: 10f))).ShouldBe("11");
    }

    [Test]
    public void GetRespawnWaveMaxLength_FewerPlayersAndRobotDestruction_ScaleTheWave()
    {
        HudState state = Marker(serverTime: 0f, nextWave: 0f);
        HudState two = state with { Teams = [new SceneTeam(2) { Players = [1, 2] }] };

        // RemapValClamped( 2, 1, 8, 0.25, 1 ) = 0.25 + 0.75/7; 6 × that is under 5, so 5 (:3479).
        TfRespawnWave.GetRespawnWaveMaxLength(two, 2, scaleWithNumPlayers: true).ShouldBe(5f);
        TfRespawnWave.GetRespawnWaveMaxLength(two, 2, scaleWithNumPlayers: false).ShouldBe(6f);
        TfRespawnWave.GetRespawnWaveMaxLength(state with { Rules = state.Rules with { RobotDestructionRespawnScale = (0.25f, 0.5f) } }, 2, true)
            .ShouldBe(4.5f, "× ( 1 − RED's scale ) (tf_gamerules.cpp:3649)");
        TfRespawnWave.GetRespawnWaveMaxLength(state with { RoundState = 5 }, 2, true).ShouldBe(0f, "only GR_STATE_RND_RUNNING (:3464)");
    }

    private static readonly EconAttributeWire Wire = new([], [], HasValidItemId: false);

    private static SceneIdEntity Dropped() => new(60, SceneIdEntityKind.DroppedWeapon)
    {
        ItemValid = true,
        ItemDefinition = 20,
        ItemQuality = 6,
        AccountId = 555u,
        Position = (0f, 0f, 0f),
        SolidType = 6,
    };

    private static HudState Playing(
        SceneIdEntity entity, int playerClass = 3, PlayerConditions conditions = default, SceneItem? active = null) =>
        new(true, true, 0, 100, true, CurTime: 1f, Team: 2, ObserverMode: ObserverModes.None, LocalIndex: 1, PlayerClass: playerClass,
            RoundState: 4, IdTarget: entity.EntityIndex,
            Players:
            [
                new(1, 0f, 0f, 0f, 2, 100, playerClass, LifeState: 0, Conditions: conditions, ActiveWeapon: 9)
                {
                    Items = [active ?? new SceneItem(9, "CTFShotgun_Soldier", 10, Wire, IsWeapon: true)],
                },
                new(2, 500f, 0f, 0f, 2, 100, 3, LifeState: 0),
            ],
            Names: new Dictionary<int, string> { [1] = "Me", [2] = "Owner" },
            AccountIds: new Dictionary<int, uint> { [1] = 111u, [2] = 555u },
            IdEntities: [entity],
            KeyLookupBinding: TestConVars.Binding("+use_action_slot_item", "H"));

    private static HudState Marker(float serverTime, float nextWave) =>
        Playing(new SceneIdEntity(61, SceneIdEntityKind.ReviveMarker) { Team = 2, Health = 37, MaxHealth = 85, OwnerEntityIndex = 2 }) with
        {
            ServerTime = serverTime,
            Rules = new SceneGameRules(false, 0, false) { NextRespawnWave = (nextWave, 0f), TeamRespawnWaveTimes = (6f, -1f) },
            Teams = [new SceneTeam(2) { Players = [1, 2, 3, 4, 5, 6, 7, 8] }],
        };

    private static string? Level(TfMainTargetId id) =>
        ((VguiLabel)id.TargetHealth.FindChildByName("PlayerStatusPlayerLevel")!).Text;

    private static bool Hidden(HudState state)
    {
        TfMainTargetId id = Built();

        ((HudViewport)id.Parent!).Think(state);
        return id.Visible;
    }

    private static TfMainTargetId Thought(HudState state)
    {
        TfMainTargetId id = Built();

        ((HudViewport)id.Parent!).Think(state);
        id.Visible.ShouldBeTrue();
        return id;
    }

    private static TfMainTargetId Built()
    {
        Dictionary<string, byte[]> files = new()
        {
            ["resource/UI/TargetID.res"] = Encoding.UTF8.GetBytes("""
                "Resource/UI/TargetID.res"
                {
                    "TargetIDBG" { "ControlName" "CTFImagePanel" "fieldName" "TargetIDBG" "wide" "100" "tall" "39" }
                    "SpectatorGUIHealth" { "fieldName" "SpectatorGUIHealth" "wide" "40" "tall" "40" }
                    "TargetNameLabel" { "ControlName" "Label" "fieldName" "TargetNameLabel" "wide" "720" "tall" "27" "labelText" "%targetname%" }
                    "TargetDataLabel" { "ControlName" "Label" "fieldName" "TargetDataLabel" "wide" "315" "tall" "17" "labelText" "%targetdata%" }
                    "MoveableSubPanel"
                    {
                        "ControlName" "EditablePanel"
                        "fieldName" "MoveableSubPanel"
                        "wide" "40"
                        "tall" "40"
                        "visible" "0"
                        "MoveableIcon" { "ControlName" "CIconPanel" "fieldName" "MoveableIcon" "wide" "20" "tall" "20" "visible" "0" }
                        "MoveableKeyLabel" { "ControlName" "Label" "fieldName" "MoveableKeyLabel" "wide" "20" "tall" "10" "labelText" "%movekey%" }
                    }
                }
                """),
            ["resource/UI/SpectatorGUIHealth.res"] = Encoding.UTF8.GetBytes("""
                "Resource/UI/SpectatorGUIHealth.res"
                {
                    "PlayerStatusPlayerLevel" { "ControlName" "CExLabel" "fieldName" "PlayerStatusPlayerLevel" "wide" "20" "tall" "10" }
                }
                """),
            ["scripts/hud_textures.txt"] = Encoding.UTF8.GetBytes("""
                "sprites/640_hud"
                {
                    TextureData
                    {
                        "obj_weapon_pickup" { "file" "obj_weapon_pickup.vmt" "x" "0" "y" "0" "width" "20" "height" "20" }
                    }
                }
                """),
        };
        Dictionary<string, string> strings = new(System.StringComparer.OrdinalIgnoreCase)
        {
            ["TF_WhoDropped"] = "%s1 dropped it",
        };

        KeyValuesTree scheme = KeyValuesTree.Load(
            Encoding.UTF8.GetBytes("Scheme { Colors { \"ItemRarityUncommon\" \"10 20 30 255\" } Borders { } Fonts { } }"), "scheme.res", _ => null);
        VguiScheme colours = VguiScheme.Load(scheme);
        VguiContext context = new(colours, VguiBorders.Load(scheme, colours, 480), scheme.Find("Fonts")!, 640, 480, "english")
        {
            Surface = new TextRecorder(),
            Read = files.GetValueOrDefault,
            Localize = strings.GetValueOrDefault,
        };
        HudViewport viewport = new()
        {
            Wide = 640,
            Tall = 480,
            Context = context,
            Icons = HudTextures.Load(context),
            Items = ItemSchema.Read(Encoding.UTF8.GetBytes(ItemsGame)),
            ItemName = (definition, quality) => $"Item{definition}q{quality}",
        };
        TfMainTargetId id = new(viewport);

        id.PerformApplySchemeSettings(context);
        id.TargetHealth.PerformApplySchemeSettings(context);
        return id;
    }
}
