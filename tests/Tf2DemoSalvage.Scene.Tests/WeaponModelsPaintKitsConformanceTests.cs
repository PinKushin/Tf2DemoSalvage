using System;
using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Hud;
using Tf2DemoSalvage.TestSupport;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// A war painted item's forced material and its `WeaponSkin` request (B754): `CEconEntity::OnDataChanged`
/// (econ_entity.cpp:1020-1074) and `CWeaponSkinProxy::OnBind` (c_tf_player.cpp:3396-3707), over a hand-built schema and
/// `proto_defs.vpd`, through <see cref="MomentScene"/> to the drawn instance.
/// </summary>
public sealed class WeaponModelsPaintKitsConformanceTests
{
    private const int Kit = 7;
    private const int Item = 200;
    private const string KitMaterial = "models/paintkits/test/item200";

    [Test]
    public void MaterialOverrideFor_AKitNamingAMaterial_StompsTheDefinitionsOwn()
    {
        // "if we have paintkit material override, stomp all material override" (:1045); without a kit, the definition's
        // `material_override` for the team (econ_item_schema.h:1966).
        WeaponModels weapons = Weapons();

        (weapons.MaterialOverrideFor(Painted(), 3), weapons.MaterialOverrideFor(Unpainted(), 3), weapons.MaterialOverrideFor(Unpainted(), 2))
            .ShouldBe((KitMaterial + ".vmt", "models/blu_own.vmt", "models/own.vmt"));
    }

    [Test]
    public void WeaponSkinFor_APaintedItem_NamesItsOwnIndexKitAndWearAndTakesItsTeam()
    {
        // `"%d_%d_wear_%02d"` of `GetItemDefIndex()` (:3657); wear 0.5 is category 3; the item view's team 3 (:3654).
        WeaponSkin skin = Weapons().WeaponSkinFor(Painted(team: 3), fullSize: false).ShouldNotBeNull();

        (skin.Name, skin.Team, skin.Seed, skin.FullSize).ShouldBe(("200_7_wear_03", 3, 0ul, false));
    }

    [Test]
    public void WeaponSkinFor_ADecoratedItemWithNoWire_TakesItsKitFromTheDefinitionAndItsTeamFromTheItemView()
    {
        // A decorated weapon names its kit in `static_attrs` and sends no attribute list, so its prop has no wire; the
        // composite's team is still the item view's own `m_iTeamNumber` (:3654), not red. No wear attribute is wear 0,
        // category 1 (`GetPaintKitWear`, econ_item_interface.cpp:457-481). Item 210 names 200's recipe.
        SceneProp decorated = Painted(item: 210, team: 3) with { Econ = null };

        WeaponSkin skin = Weapons().WeaponSkinFor(decorated, false).ShouldNotBeNull();

        (skin.Name, skin.Team).ShouldBe(("210_7_wear_01", 3));
    }

    [Test]
    public void WeaponSkinFor_AnUnassignedTeam_CompositesAsRed()
    {
        // `pItem->GetTeamNumber() != TEAM_UNASSIGNED ? … : TF_TEAM_RED` (:3654).
        Weapons().WeaponSkinFor(Painted(team: 0), fullSize: false).ShouldNotBeNull().Team.ShouldBe(2);
    }

    [Test]
    public void WeaponSkinFor_BothCustomSeedHalves_SeedsFromThem()
    {
        // `((uint64)unHighVal << 32) | unLowVal` when both are present (:3646-3649); one alone leaves `GetOriginalID()`, 0 here.
        WeaponModels weapons = Weapons();

        (weapons.WeaponSkinFor(Painted(seedLow: 5, seedHigh: 2), false).ShouldNotBeNull().Seed,
                weapons.WeaponSkinFor(Painted(seedLow: 5), false).ShouldNotBeNull().Seed)
            .ShouldBe(((2ul << 32) | 5ul, 0ul));
    }

    [Test]
    public void WeaponSkinFor_AnItemTheKitDoesNotList_IsNull()
    {
        // `GetItemPaintKitDefinitionKV` answers nothing for an item the kit has no entry for, and the proxy returns (:3629-3633).
        Weapons().WeaponSkinFor(Painted(item: 999), false).ShouldBeNull();
    }

    [Test]
    public void WeaponSkinFor_ARemappedItem_LooksTheRecipeUpForTheItemItNames()
    {
        // `GetRemappedItemDefIndex()` (:3629): item 202 names item 200, which the kit lists; the NAME keeps 202's own index.
        WeaponSkin skin = Weapons().WeaponSkinFor(Painted(item: 202), false).ShouldNotBeNull();

        skin.Name.ShouldBe("202_7_wear_03");
    }

    [TestCase(0.2f, 1)]
    [TestCase(0.2001f, 2)]
    [TestCase(0.8f, 4)]
    [TestCase(1.0f, 5)]
    [TestCase(1.5f, 3)]
    public void WearCategory_AWear_IsItsFifth(float wear, int category)
    {
        // `EconWear_ToIntCategory` (econ_item_constants.cpp:210-235): `<=` each fifth, and 3 above 1.
        WeaponModels.WearCategory(wear).ShouldBe(category);
    }

    [Test]
    public void SizeFor_TheLocalPlayersAndAnotherPlayers_IsFullOrTwoMipsSmaller()
    {
        // `desiredW >> 2` unless high res or the local player's (:3500-3505), never below 1.
        WeaponSkin full = Weapons().WeaponSkinFor(Painted(), fullSize: true).ShouldNotBeNull();

        (full.SizeFor(2048, 1024), (full with { FullSize = false }).SizeFor(2048, 1024), (full with { FullSize = false }).SizeFor(2, 2))
            .ShouldBe(((2048, 1024), (512, 256), (1, 1)));
    }

    [Test]
    public void PaintedIn_ADemoWithAPaintedItem_ListsItsMaterialsAndTheTexturesItsCompositeReads()
    {
        // Loaded before playback (D86): the kit's material, the definition's per team, and the recipe's one lookup texture.
        ScenePropTrack weapon = new(entityIndex: 9, "models/weapons/c_models/c_test.mdl")
        {
            ItemDefinitionIndex = Item,
            Econ = Painted().Econ,
        };

        weapon.Add(0, new ScenePose());

        (IReadOnlyList<string> materials, IReadOnlyList<string> textures) = Weapons().PaintedIn(DemoTimeline.ForEverything(props: [weapon]));

        materials.ShouldBe(["models/own.vmt", "models/blu_own.vmt", KitMaterial + ".vmt"], ignoreOrder: true);
        textures.ShouldBe(["patterns/test"]);
    }

    [Test]
    public void Build_APaintedItem_ReachesTheDrawnInstanceWithItsMaterialAndItsSkin()
    {
        // The last hop: `MomentScene.Build` supplies both delegates, and the instance carries their answers.
        EntityModelSet models = new() { Geometry = _ => Frames() };
        MomentScene scene = new(models, new ViewmodelScene(), new RecordingLogger()) { Weapons = Weapons() };
        SceneProp painted = Painted();

        scene.Build([], [], default);
        List<ModelInstance> instances = [];
        models.Add([painted], _ => Frames());
        models.Instances([painted], instances);

        ModelInstance drawn = instances.ShouldHaveSingleItem();
        (drawn.MaterialOverride, drawn.WeaponSkin?.Name).ShouldBe((KitMaterial + ".vmt", "200_7_wear_03"));
    }

    /// <remarks>
    /// `CEmbeddedItemModelPanel::GetOverrideMaterial` forces the ITEM's material for the local team, red when unassigned
    /// (item_model_panel.cpp:1326-1350), and the panel's `CMDL` carries the item view as its proxy data, so its composite is
    /// the view's — at the view's own team and never full size. The control is the unpainted item, which takes the
    /// definition's own red material and no composite.
    /// </remarks>
    [Test]
    public void SetItem_AWarPaintedWeapon_ForcesTheKitsMaterialAndCompositesTheItemView()
    {
        WeaponModels weapons = Weapons();
        ItemSchema schema = weapons.Items.ShouldNotBeNull();
        HudViewport viewport = new() { Wide = 1366, Tall = 768, Items = schema, Weapons = weapons };
        TfEmbeddedItemModelPanel panel = new(viewport, "itemmodelpanel");

        panel.SetItem(TfItemView.From(new SceneItem(9, "CTFScatterGun", Item, Painted().Econ!, IsWeapon: true) { TeamNumber = 3 }, schema));
        (string? Material, string? Name, int? Team, bool? FullSize) painted =
            (panel.RootMdl.MaterialOverride, panel.RootMdl.WeaponSkin?.Name, panel.RootMdl.WeaponSkin?.Team, panel.RootMdl.WeaponSkin?.FullSize);

        panel.SetItem(TfItemView.From(new SceneItem(9, "CTFScatterGun", Item, Unpainted().Econ!, IsWeapon: true), schema));

        (painted, panel.RootMdl.MaterialOverride, panel.RootMdl.WeaponSkin).ShouldBe(
            ((KitMaterial + ".vmt", "200_7_wear_03", 3, false), "models/own.vmt", (WeaponSkin?)null));
    }

    private static WeaponModels Weapons() =>
        new(
            path => path switch
            {
                _ when path.EndsWith("items_game.txt", StringComparison.OrdinalIgnoreCase) => Encoding.UTF8.GetBytes(Schema),
                _ when path.EndsWith("proto_defs.vpd", StringComparison.OrdinalIgnoreCase) =>
                    SyntheticPaintKits.File(Kit, Item, KitMaterial, "patterns/test"),
                _ => null,
            },
            new RecordingLogger());

    private static SceneProp Painted(int item = Item, int team = 2, int? seedLow = null, int? seedHigh = null)
    {
        List<EconAttributeValue> local =
        [
            new(KitIndex, Kit),
            new(WearIndex, BitConverter.SingleToInt32Bits(0.5f)),
        ];

        if (seedLow is { } low)
        {
            local.Add(new(SeedLowIndex, low));
        }

        if (seedHigh is { } high)
        {
            local.Add(new(SeedHighIndex, high));
        }

        return new SceneProp(
            9,
            "models/weapons/c_models/c_test.mdl",
            SceneModelKind.Studio,
            new ScenePose { Scale = 1f },
            null,
            ItemDefinitionIndex: item,
            Econ: new EconAttributeWire(local, [], HasValidItemId: true),
            ItemTeam: team);
    }

    private static SceneProp Unpainted() =>
        Painted() with { Econ = new EconAttributeWire([], [], HasValidItemId: true) };

    private const int KitIndex = 834;
    private const int WearIndex = 725;
    private const int SeedLowIndex = 866;
    private const int SeedHighIndex = 867;

    private const string Schema = """
        "items_game"
        {
            "attributes"
            {
                "834" { "name" "paintkit_proto_def_index"  "stored_as_integer" "1" }
                "725" { "name" "set_item_texture_wear" }
                "866" { "name" "custom_paintkit_seed_lo"  "stored_as_integer" "1" }
                "867" { "name" "custom_paintkit_seed_hi"  "stored_as_integer" "1" }
            }
            "items"
            {
                "200"
                {
                    "name" "Two Hundred"
                    "model_player" "models/weapons/c_models/c_test.mdl"
                    "visuals"
                    {
                        "material_override" "models/own"
                    }
                    "visuals_blu"
                    {
                        "material_override" "models/blu_own"
                    }
                }
                "202"
                {
                    "remapped_item_def_index" "Two Hundred"
                }
                "210"
                {
                    "remapped_item_def_index" "Two Hundred"
                    "static_attrs" { "paintkit_proto_def_index" "7" }
                }
            }
        }
        """;

    private static PropModels.ModelFrames Frames() =>
        new(
            [[
                new PropVertex(0f, 0f, 0f, 0f, 0f, 0),
                new PropVertex(1f, 0f, 0f, 1f, 0f, 0),
                new PropVertex(1f, 1f, 0f, 1f, 1f, 0),
            ]],
            new Dictionary<int, (int, int, float)>(),
            [0, 1],
            [false, false]);
}
