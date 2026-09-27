using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>What `CTFPlayerModelPanel` (tf_playermodelpanel.cpp) asks of an item definition, off a synthetic items_game.</summary>
public sealed class ItemSchemaModelPanelTests
{
    private const string Schema = """
        "items_game"
        {
            "prefabs"
            {
                "hat"
                {
                    "item_slot" "head"
                    "visuals" { "player_bodygroups" { "hat" "1" } }
                }
            }
            "items"
            {
                "1"
                {
                    "item_slot" "primary"
                    "baseitem" "1"
                    "used_by_classes" { "scout" "1" }
                    "anim_slot" "melee_allclass"
                    "model_world" "models/w.mdl"
                    "extra_wearable" "models/extra.mdl"
                    "extra_wearable_vm" "models/extra_vm.mdl"
                    "default_skin" "4"
                    "visuals_blu" { "skin" "7" "use_per_class_bodygroups" "1" "player_poseparam" { "r_hand_grip" "13.5" } }
                }
                "2"
                {
                    "item_slot" "secondary"
                    "baseitem" "1"
                    "used_by_classes" { "scout" "1" }
                }
                "3"
                {
                    "prefab" "hat"
                    "anim_slot" "FORCE_NOT_USED"
                    "model_player" "models/hat.mdl"
                    "visuals"
                    {
                        "headphones" "0"
                        "player_bodygroups" { "headphones" "1" }
                        "styles"
                        {
                            "0" { "skin_red" "2" "skin_blu" "3" "model_player_per_class" { "basename" "models/%s_hat.mdl" } "model_player_per_class_blue" { "scout" "models/scout_blue_hat.mdl" } }
                            "1" { "skin" "5" "model_player" "models/common_hat.mdl" "additional_hidden_bodygroups" { "hat" "1" "dogtags" "1" } }
                        }
                    }
                    "visuals_red" { "player_bodygroups" { "backpack" "1" } }
                }
                "4"
                {
                    "item_slot" "primary"
                    "act_as_wearable" "1"
                }
                "5"
                {
                    "item_slot" "misc"
                    "act_as_weapon" "1"
                }
                "6"
                {
                    "item_slot" "taunt"
                    "used_by_classes" { "scout" "1" "soldier" "1" }
                    "visuals_red" { "animation_sequence" { "taunt_concept" "taunt_sequence" } }
                }
                "8"
                {
                    "item_slot" "primary"
                    "baseitem" "1"
                    "used_by_classes" { "scout" "1" }
                }
                "7"
                {
                    "item_slot" "taunt"
                    "used_by_classes" { "scout" "1" }
                    "taunt" { "custom_taunt_scene_per_class" { "scout" "a.vcd" } }
                }
            }
        }
        """;

    private static ItemSchema Read() => ItemSchema.Read(Encoding.UTF8.GetBytes(Schema));

    private const string Levels = """
        "items_game"
        {
            "item_levels"
            {
                "KillEaterRank" { "0" { "score" "10" } "1" { "score" "25" } "2" { "score" "45" } }
                "KillEater_Kills" { "0" { "score" "5" } }
                "Empty" { }
            }
            "kill_eater_score_types"
            {
                "0" { "type_name" "Kills" }
                "7" { "type_name" "Ubers" "level_data" "KillEater_Kills" }
            }
        }
        """;

    private const string Particles = """
        "items_game"
        {
            "items" { "9" { "particle_suffix" "sword" } }
            "attribute_controlled_attached_particles"
            {
                "cosmetic_unusual_effects"
                {
                    "13" { "system" "superrare_burning1" "attachment" "bip_head" "control_point_2" "eyes" }
                    "0" { "system" "never" }
                }
                "other_particles"
                {
                    "701" { "system" "killstreak_t1_teamcolor_red" "use_suffix_name" "1" "refire_time" "2.5" "attach_to_rootbone" "1" }
                    "702" { "system" "killstreak_t1_teamcolor_blue" }
                }
            }
        }
        """;

    [Test]
    public void AttributeControlledParticleSystem_ByIndex_CarriesItsKeys()
    {
        ItemSchema schema = ItemSchema.Read(Encoding.UTF8.GetBytes(Particles));

        AttributeParticleSystem burning = schema.AttributeControlledParticleSystem(13).ShouldNotBeNull();
        burning.SystemName.ShouldBe("superrare_burning1");
        burning.ControlPoints.ShouldBe(["bip_head", null, "eyes", null, null, null, null]);
        burning.UseSuffixName.ShouldBeFalse();

        AttributeParticleSystem eyes = schema.AttributeControlledParticleSystem(701).ShouldNotBeNull();
        (eyes.UseSuffixName, eyes.RefireTime, eyes.FollowRootBone).ShouldBe((true, 2.5f, true));

        schema.AttributeControlledParticleSystem(0).ShouldBeNull("an index must be positive (econ_item_schema.cpp:6133)");
        schema.FindAttributeControlledParticleSystem("KILLSTREAK_T1_TEAMCOLOR_BLUE").ShouldNotBeNull().Id.ShouldBe(702);
        schema.ParticleSuffix(9).ShouldBe("sword");
    }

    [Test]
    public void ItemLevelForScore_BelowEachThreshold_IsTheFirstLevelNotReached()
    {
        ItemSchema schema = ItemSchema.Read(Encoding.UTF8.GetBytes(Levels));

        schema.ItemLevelForScore("KillEaterRank", 9).ShouldBe(0u);
        schema.ItemLevelForScore("KillEaterRank", 10).ShouldBe(1u, "score < required, strictly (econ_item_schema.cpp:6336)");
        schema.ItemLevelForScore("KillEaterRank", 1000).ShouldBe(2u, "past every threshold: the last level (:6340)");
        schema.ItemLevelForScore("Empty", 1).ShouldBeNull();
        schema.ItemLevelForScore("Missing", 1).ShouldBeNull();
    }

    [Test]
    public void KillEaterLevelingDataName_ByScoreType_IsItsLevelDataOrKillEaterRank()
    {
        ItemSchema schema = ItemSchema.Read(Encoding.UTF8.GetBytes(Levels));

        schema.KillEaterLevelingDataName(0).ShouldBe("KillEaterRank", "GetString( \"level_data\", \"KillEaterRank\" ) (:6226)");
        schema.KillEaterLevelingDataName(7).ShouldBe("KillEater_Kills");
        schema.KillEaterLevelingDataName(3).ShouldBeNull();
    }

    [Test]
    public void AnimSlot_FromTheWeaponTypeTable_IsItsIndex()
    {
        ItemSchema schema = Read();

        schema.AnimSlot(1).ShouldBe(10, "MELEE_ALLCLASS, matched without case (tf_item_schema.cpp:1024)");
        schema.AnimSlot(3).ShouldBe(ItemSchema.AnimSlotNotUsed);
        schema.AnimSlot(2).ShouldBe(-1, "no anim_slot: the constructor's -1");
    }

    [Test]
    public void IsAWearable_BySlotAndActAsKeys_IsValvesPredicate()
    {
        ItemSchema schema = Read();

        schema.IsAWearable(3).ShouldBeTrue("head rewritten to misc: a wearable slot");
        schema.IsAWearable(1).ShouldBeFalse();
        schema.IsAWearable(4).ShouldBeTrue("act_as_wearable");
        schema.IsAWearable(5).ShouldBeFalse("a misc item acting as a weapon");
    }

    [Test]
    public void DisplayModels_TheItemKeys_AreReturned()
    {
        ItemSchema schema = Read();

        schema.WorldDisplayModel(1).ShouldBe("models/w.mdl");
        schema.ExtraWearableModel(1).ShouldBe("models/extra.mdl");
        schema.ExtraWearableViewModel(1).ShouldBe("models/extra_vm.mdl");
        schema.WorldDisplayModel(2).ShouldBeNull();
    }

    [Test]
    public void PlayerDisplayModel_WithAStyle_PrefersTheStylesModel()
    {
        ItemSchema schema = Read();

        schema.PlayerDisplayModel(3, 1, 2, 0).ShouldBe("models/scout_hat.mdl", "red: the basename expanded");
        schema.PlayerDisplayModel(3, 1, 3, 0).ShouldBe("models/scout_blue_hat.mdl", "blue: its own block");
        schema.PlayerDisplayModel(3, 2, 3, 0).ShouldBe("models/sniper_hat.mdl", "blue without an entry falls to red");
        schema.PlayerDisplayModel(3, 4, 2, 0).ShouldBe("models/demo_hat.mdl");
        schema.PlayerDisplayModel(3, 1, 2, 1).ShouldBe("models/common_hat.mdl");
        schema.PlayerDisplayModel(3, 1, 2, null).ShouldBe("models/hat.mdl", "INVALID_STYLE_INDEX: the base model");
    }

    [Test]
    public void Skin_StylesVisualsAndDefault_FollowGetSkin()
    {
        ItemSchema schema = Read();

        schema.Skin(3, 2, 0).ShouldBe(2);
        schema.Skin(3, 3, 0).ShouldBe(3);
        schema.Skin(3, 3, 1).ShouldBe(5, "skin sets every team");
        schema.Skin(3, 2, null).ShouldBe(-1, "styles but no valid style: default_skin, unset");
        schema.Skin(1, 3, null).ShouldBe(7, "the blue block's skin");
        schema.Skin(1, 2, null).ShouldBe(4, "no red or base block: default_skin");
        schema.Skin(1, 5, null).ShouldBe(0, "outside TEAM_VISUAL_SECTIONS");
    }

    [Test]
    public void UsesPerClassBodygroups_ByBestTeamBlock_IsThatBlocksFlag()
    {
        ItemSchema schema = Read();

        schema.UsesPerClassBodygroups(1, 3).ShouldBeTrue();
        schema.UsesPerClassBodygroups(1, 2).ShouldBeFalse();
    }

    [Test]
    public void Bodygroups_BaseBlockStylesAndDefaults_AreSeparated()
    {
        ItemSchema schema = Read();

        schema.BasePlayerBodygroupsFor(3).ShouldBe(new Dictionary<string, int> { ["headphones"] = 1, ["hat"] = 1 }, ignoreOrder: true);
        schema.StyleHiddenBodygroups(3, 1).ShouldBe(["hat", "dogtags"]);
        schema.StyleHiddenBodygroups(3, 0).ShouldBeEmpty();
        schema.DefaultBodygroupStates.ShouldBe(
            new Dictionary<string, int> { ["headphones"] = 0, ["hat"] = 0, ["backpack"] = 0 }, ignoreOrder: true);
    }

    [Test]
    public void PlayerPoseParametersFor_TheBestBlock_AreItsPairs()
    {
        ItemSchema schema = Read();

        schema.PlayerPoseParametersFor(1, 3).ShouldBe([("r_hand_grip", 13.5f)]);
        schema.PlayerPoseParametersFor(1, 2).ShouldBeEmpty();
    }

    [Test]
    public void IsTauntItem_TauntSlotWithDataOrConcept_IsTrue()
    {
        ItemSchema schema = Read();

        schema.IsTauntItem(7, 2, 1).ShouldBeTrue("a taunt block");
        schema.IsTauntItem(6, 2, 1).ShouldBeTrue("taunt_concept in the red block");
        schema.IsTauntItem(6, 3, 1).ShouldBeFalse("blue has no block, base has no concept");
        schema.IsTauntItem(3, 2, 1).ShouldBeFalse("not a taunt slot");
    }

    [Test]
    public void BaseItemForClass_BySlot_IsTheLowestBaseItem()
    {
        ItemSchema schema = Read();

        schema.BaseItemForClass(1, ItemSchema.LoadoutSlotPrimary).ShouldBe(1);
        schema.BaseItemForClass(1, ItemSchema.LoadoutSlotSecondary).ShouldBe(2);
        schema.BaseItemForClass(3, ItemSchema.LoadoutSlotPrimary).ShouldBeNull("a soldier uses neither");
        schema.BaseItemForClass(1, ItemSchema.LoadoutSlotHead).ShouldBeNull("head and up: m_pDefaultItem (:705)");
    }
}
