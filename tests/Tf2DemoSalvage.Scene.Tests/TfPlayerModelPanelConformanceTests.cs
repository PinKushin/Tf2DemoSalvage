using System;
using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`CTFPlayerModelPanel` (tf_playermodelpanel.cpp) dressing the class model, on synthetic models and a synthetic schema.</summary>
public sealed class TfPlayerModelPanelConformanceTests
{
    private const string ScoutModel = "models/player/scout.mdl";
    private const string Scattergun = "models/weapons/w_scattergun.mdl";
    private const string Pistol = "models/weapons/w_pistol.mdl";
    private const string Hat = "models/player/items/scout/hat.mdl";

    private const string Schema = """
        "items_game"
        {
            "items"
            {
                "13" { "item_slot" "primary" "baseitem" "1" "used_by_classes" { "scout" "1" } "model_player" "models/weapons/w_scattergun.mdl" }
                "23" { "item_slot" "secondary" "baseitem" "1" "used_by_classes" { "scout" "1" } "model_player" "models/weapons/w_pistol.mdl" }
                "50"
                {
                    "item_slot" "head"
                    "used_by_classes" { "scout" "1" }
                    "model_player" "models/player/items/scout/hat.mdl"
                    "visuals" { "use_per_class_bodygroups" "1" "player_bodygroups" { "hat" "1" } }
                }
                "51" { "item_slot" "misc" "used_by_classes" { "scout" "1" } "visuals" { "skin" "3" } "model_player" "models/player/items/scout/hat.mdl" }
            }
            "attributes"
            {
                "142" { "name" "set item tint rgb" }
                "261" { "name" "set item tint rgb 2" }
                "1000" { "name" "player skin override" }
            }
        }
        """;

    private static ItemSchema Items() => ItemSchema.Read(Encoding.UTF8.GetBytes(Schema));

    private static (TfPlayerModelPanel Panel, FakeCache Cache) Panel(params string[] loaded)
    {
        HudViewport viewport = new()
        {
            Items = Items(),
            ClassModels = PlayerClassModels.Read(path => path == "scripts/playerclasses/scout.txt"
                ? Encoding.UTF8.GetBytes("\"PlayerClass\" { \"model\" \"models/player/scout.mdl\" }")
                : null),
        };

        FakeCache cache = new(loaded);

        return (new TfPlayerModelPanel(viewport, "classmodelpanel", cache), cache);
    }

    private static TfItemView Item(int definition, params EconAttributeValue[] attributes)
    {
        Dictionary<int, EconAttributeValue> byIndex = [];

        foreach (EconAttributeValue attribute in attributes)
        {
            byIndex[attribute.DefinitionIndex] = attribute;
        }

        return new TfItemView(definition, 6, byIndex);
    }

    [Test]
    public void UpdateModelPanel_AHeldScattergun_IsTheClassModelWithItMergedAndStandingPrimary()
    {
        (TfPlayerModelPanel panel, _) = Panel(ScoutModel, Scattergun, Pistol);

        // tf_hud_playerstatus.cpp:479-512.
        panel.ClearCarriedItems();
        panel.SetToPlayerClass(1);
        panel.SetTeam(3);
        panel.AddCarriedItem(Item(23));
        panel.AddCarriedItem(Item(13));
        panel.HoldItemInSlot(ItemSchema.LoadoutSlotPrimary).ShouldBeTrue();

        panel.ModelName.ShouldBe(ScoutModel);
        panel.MergeMdls.ShouldHaveSingleItem().Path.ShouldBe(Scattergun, "only the held weapon; a weapon is not a wearable");
        panel.MergeMdls[0].Skin.ShouldBe(1, "no item skin: SetMDLSkinForTeam's team skin for BLU (:1185)");
        panel.Skin.ShouldBe(1, "UpdatePreviewVisuals: BLU's player skin");
        panel.Sequence.ShouldBe(0, "ACT_MP_STAND_PRIMARY is sequence 0 (:1015)");
        panel.CurrentSlotIndex.ShouldBe(ItemSchema.LoadoutSlotPrimary);
    }

    [Test]
    public void HoldFirstValidItem_NothingCarried_HoldsTheBasePrimary()
    {
        (TfPlayerModelPanel panel, _) = Panel(ScoutModel, Scattergun, Pistol);

        panel.SetToPlayerClass(1);

        panel.HeldItem.ShouldNotBeNull().DefinitionIndex.ShouldBe(13, "GetBaseItemForClass( scout, PRIMARY ) (:291)");
        panel.MergeMdls.ShouldHaveSingleItem().Path.ShouldBe(Scattergun);
        panel.Skin.ShouldBe(0, "SetToPlayerClass ends on SetTeam( TF_TEAM_RED ) (:250)");
    }

    [Test]
    public void EquipAllWearables_AHatWithBodygroups_HidesTheHatGroupAndSetsItsClassBodygroup()
    {
        (TfPlayerModelPanel panel, _) = Panel(ScoutModel, Scattergun, Hat);

        panel.SetToPlayerClass(1);
        panel.SetTeam(2);
        panel.AddCarriedItem(Item(13));
        panel.AddCarriedItem(Item(50));
        panel.HoldItemInSlot(ItemSchema.LoadoutSlotPrimary);

        // The fake's radix is 16 per group; "hat" is group 1 on the class model: 1 * 16.
        panel.Body.ShouldBe(16);
        VguiMdl hat = panel.GetMergeMDL(Hat).ShouldNotBeNull();
        hat.Body.ShouldBe(0 + (1 - 1) * 16, "SetBodygroup( merge, 0, 1, class - 1 ): the scout is class 1 (:1223)");
        hat.Skin.ShouldBe(0, "no visuals skin: the RED team skin");
    }

    [Test]
    public void SetMDLSkinForTeam_AVisualsSkin_IsTheItemsSkin()
    {
        (TfPlayerModelPanel panel, _) = Panel(ScoutModel, Scattergun, Hat);

        panel.SetToPlayerClass(1);
        panel.SetTeam(3);
        panel.AddCarriedItem(Item(13));
        panel.AddCarriedItem(Item(51));
        panel.HoldItemInSlot(ItemSchema.LoadoutSlotPrimary);

        panel.GetMergeMDL(Hat).ShouldNotBeNull().Skin.ShouldBe(3);
    }

    [Test]
    public void AddCarriedItem_OnBlu_PaintsWithTheSecondColor()
    {
        (TfPlayerModelPanel panel, _) = Panel(ScoutModel, Scattergun, Hat);

        panel.SetToPlayerClass(1);
        panel.SetTeam(3);
        panel.AddCarriedItem(Item(13));

        // 0xFF0000 and 0x0000FF as floats — the attribute's VALUE is the packed color.
        panel.AddCarriedItem(Item(51, new EconAttributeValue(142, BitConverter.SingleToInt32Bits(0xFF0000)),
            new EconAttributeValue(261, BitConverter.SingleToInt32Bits(0x0000FF))));
        panel.HoldItemInSlot(ItemSchema.LoadoutSlotPrimary);

        panel.ItemsToCarry[1].ForceBlueTeam.ShouldBeTrue("kEconItemFlagClient_ForceBlueTeam (:1097)");
        panel.GetMergeMDL(Hat).ShouldNotBeNull().Paint.ShouldBe((0f, 0f, 1f));
    }

    [Test]
    public void UpdatePreviewVisuals_PlayerSkinOverride_IsTheZombieSkin()
    {
        (TfPlayerModelPanel panel, _) = Panel(ScoutModel, Scattergun, Hat);

        panel.SetToPlayerClass(1);
        panel.AddCarriedItem(Item(51, new EconAttributeValue(1000, BitConverter.SingleToInt32Bits(1f))));
        panel.SetTeam(3);

        panel.Skin.ShouldBe(5, "BLU's 1 + 4 (c_tf_player.cpp:7747)");
        TfPlayerModelPanel.AdjustSkinIndexForZombie(8, 1).ShouldBe(23, "the spy's mask skins, +22");
    }

    [Test]
    public void OnModelLoadComplete_AModelNotYetLoaded_MergesTheFrameItLoads()
    {
        (TfPlayerModelPanel panel, FakeCache cache) = Panel(ScoutModel);

        panel.SetToPlayerClass(1);

        panel.MergeMdls.ShouldBeEmpty("the callback waits for the model");
        panel.ModelsToPrecache().ShouldBe([ScoutModel, Scattergun], "the waiting model is asked for, or it never loads");

        cache.Load(Scattergun);
        panel.Paint(new VguiModelPanelConformanceTests.RecordingModelSurface(), VguiModelPanelConformanceTests.Context());

        panel.MergeMdls.ShouldHaveSingleItem().Path.ShouldBe(Scattergun);
    }

    /// <summary>Models by path with the activities the panel asks for, and bodygroups as radix-16 digits.</summary>
    private sealed class FakeCache(IEnumerable<string> loaded) : IMdlCache
    {
        private readonly HashSet<string> _loaded = new(loaded, StringComparer.Ordinal);

        public void Load(string path) => _loaded.Add(path);

        public PropModels.ModelFrames? FindMdl(string path) =>
            _loaded.Contains(path)
                ? new PropModels.ModelFrames(
                    [], new Dictionary<int, (int, int, float)>(), [], [],
                    Skinned: SyntheticSkinnedModel.WithActivities(("stand_primary", "ACT_MP_STAND_PRIMARY"), ("stand_secondary", "ACT_MP_STAND_SECONDARY")))
                : null;

        public int FindBodygroup(string modelPath, string group) => group switch
        {
            "headphones" => 0,
            "hat" => 1,
            _ => -1,
        };

        public int SetBodygroup(string modelPath, int group, int value, int body)
        {
            int scale = 1 << (4 * group);

            return body - ((body / scale) % 16 * scale) + (value * scale);
        }
    }
}
