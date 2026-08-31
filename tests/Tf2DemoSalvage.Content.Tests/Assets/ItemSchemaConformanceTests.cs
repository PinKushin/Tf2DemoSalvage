using System;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// How the game turns an item definition index into the model in a player's hands.
/// </summary>
/// <remarks>
/// **This is the half of the weapon lookup the scripts stopped answering.** Six of the nine weapon
/// classes the corpus contains have a weapon script whose <c>viewmodel</c> key holds a note rather
/// than a path — "viewmodel is now defined in _items_main.txt" — and the demo names the item
/// instead: <c>DT_ScriptCreatedItem.m_iItemDefinitionIndex</c>, networked from the 2009 build on.
///
/// **The engine's own route, in order** (<c>CEconItemView::GetPlayerDisplayModel</c>,
/// <c>econ_item_view.cpp:924</c>):
///
/// <code>
/// if ( pDef->GetNumStyles() )           ... style's per-class model, then its base model
/// if ( iClass is inside LOADOUT_COUNT )   ... CTFItemDefinition::GetPlayerDisplayModel( iClass )
/// return pDef->GetBasePlayerDisplayModel();
/// </code>
///
/// The per-class array comes from <c>model_player_per_class</c> and falls back to the base when the
/// class has no entry of its own (<c>tf_item_schema.cpp:1058</c>), and the base is
/// <c>model_player</c> (<c>econ_item_schema.cpp:2376</c>).
///
/// **Styles are not implemented and this says so rather than leaving it silent.** A style is a
/// per-item variant the owner chooses; the demo does carry the choice, but no weapon in the corpus
/// uses one and building it untested would be a guess. A styled item draws its base model here.
///
/// **The definition itself usually holds none of this.** A stock weapon is four lines and a
/// <c>prefab</c>, and the model lives in the prefab — so resolution is a chain, and a reader that
/// looked only at the item would find nothing for every stock weapon in the game.
/// </remarks>
public sealed class ItemSchemaConformanceTests
{
    /// <summary>The shipped item schema, when the game is installed.</summary>
    /// <remarks>
    /// Nullable and not skipping, because the tests below already check for it and report which
    /// ones they could run. <c>GameInstall.RequireFile</c> would be wrong here for the same reason
    /// it is wrong in a survey: it decides for the caller.
    /// </remarks>
    private static string? SchemaPath => GameInstall.Find("scripts/items/items_game.txt");

    /// <summary>The stock scattergun, and the first item a scout holds.</summary>
    private const string StockScattergun = "13";

    [Test]
    public void StockWeapon_ItsDefinition_NamesAPrefabRatherThanAModel()
    {
        // Measured on the shipped file: item 13 is
        //
        //     "13" { "name" "TF_WEAPON_SCATTERGUN"  "prefab" "weapon_scattergun"  "baseitem" "1" }
        //
        // — four keys and no model at all. Any lookup that reads `model_player` off the definition
        // answers null for every stock weapon, which is most of what a demo contains.
        if (!File.Exists(SchemaPath))
        {
            Assert.Ignore("the game is not installed");
            return;
        }

        Dictionary<string, string?> item = Block("items", StockScattergun);

        item.ShouldContainKey("prefab");
        item["prefab"].ShouldBe("weapon_scattergun");
        item.ShouldNotContainKey("model_player");
    }

    [Test]
    public void WeaponPrefab_CarriesTheModelAndTheAttachFlag()
    {
        // The other end of the chain, and both keys matter. `model_player` is what
        // GetBasePlayerDisplayModel returns; `attach_to_hands` is ShouldAttachToHands, which is
        // what decides in econ_entity.cpp whether a separate viewmodel attachment exists at all
        // rather than the viewmodel itself being the weapon.
        if (!File.Exists(SchemaPath))
        {
            Assert.Ignore("the game is not installed");
            return;
        }

        Dictionary<string, string?> prefab = Block("prefabs", "weapon_scattergun");

        prefab["model_player"].ShouldBe("models/weapons/c_models/c_scattergun.mdl");
        prefab["attach_to_hands"].ShouldBe("1");
    }

    [Test]
    public void ModelFor_TheStockWeaponsTheCorpusHolds_ResolveThroughTheRealSchema()
    {
        // **The end-to-end assertion, on the shipped eight-megabyte file.** The unit tests run
        // against a fixture small enough to reason about, and a fixture is written by the same
        // person as the code — this is the one that can disagree with it. Every path below was
        // read out of items_game.txt rather than predicted.
        if (!File.Exists(SchemaPath))
        {
            Assert.Ignore("the game is not installed");
            return;
        }

        ItemSchema schema = ItemSchema.Read(File.ReadAllBytes(SchemaPath));

        // (definition index, player class, expected model) — the stock loadout items for the
        // classes z1800 actually contains.
        (int Item, int Class, string Model)[] expected =
        [
            (13, 1, "models/weapons/c_models/c_scattergun.mdl"),
            (18, 3, "models/weapons/c_models/c_rocketlauncher/c_rocketlauncher.mdl"),
            (19, 4, "models/weapons/c_models/c_grenadelauncher/c_grenadelauncher.mdl"),
            (21, 7, "models/weapons/c_models/c_flamethrower/c_flamethrower.mdl"),
            (14, 2, "models/weapons/c_models/c_sniperrifle/c_sniperrifle.mdl"),
        ];

        foreach ((int item, int playerClass, string model) in expected)
        {
            schema.ModelFor(item, playerClass).ShouldBe(
                model, $"item {item} in class {playerClass}'s hands");
        }

        // And the flag that decides whether it is a separate model at all.
        schema.AttachesToHands(13).ShouldBeTrue("the scattergun attaches to the hands");
    }

    [Test]
    public void ModelFor_ThePerClassBasenamesInTheShippedSchema_Expand()
    {
        // **`basename` appears 5,518 times in the shipped file** and this project read none of
        // them, because the reader stored the key as though "basename" were a class nobody plays.
        // Measured on a real match afterwards: 48 of 252 distinct (item, class) pairs resolved to
        // no model at all, and every one was a cosmetic.
        //
        // Both patterns below were read out of items_game.txt, not predicted:
        //
        //     "261" Mann Co. Cap      "basename" "models/player/items/%s/%s_cap.mdl"
        //     "126" Bill's Hat        "basename" "models/player/items/%s/%s_bill.mdl"
        //
        // Neither item carries a `model_player` of its own, so before this the answer was nothing.
        if (!File.Exists(SchemaPath))
        {
            Assert.Ignore("the game is not installed");
            return;
        }

        ItemSchema schema = ItemSchema.Read(File.ReadAllBytes(SchemaPath));

        schema.ModelFor(261, playerClass: 1)
            .ShouldBe("models/player/items/scout/scout_cap.mdl");

        // The demoman, whose files say `demo` where the schema says `demoman` — Valve forces the
        // substitution and apologises for it in `tf_item_schema.cpp:519`. Without that line this
        // names `models/player/items/demoman/demoman_cap.mdl`, which does not exist and is
        // indistinguishable on screen from naming nothing.
        schema.ModelFor(261, playerClass: 4)
            .ShouldBe("models/player/items/demo/demo_cap.mdl");

        schema.ModelFor(126, playerClass: 3)
            .ShouldBe("models/player/items/soldier/soldier_bill.mdl");
    }

    /// <summary>Every key directly inside a named block, at a stated depth.</summary>
    /// <remarks>
    /// Depth is checked as well as the name because <c>items_game.txt</c> reuses names freely —
    /// "13" is an item, an operation and a condition-logic entry in three different blocks, and a
    /// search by name alone finds whichever comes first. That is what this project's own probe did
    /// before this was written, and it read an operation's start date as a weapon.
    /// </remarks>
    private static Dictionary<string, string?> Block(string parent, string name)
    {
        // Every caller has already gated on the schema being there; this says so in a form the
        // compiler accepts, and skips rather than throwing if one ever forgets to.
        byte[] schema = File.ReadAllBytes(Skip.Unless(SchemaPath, GameInstall.Missing));

        Dictionary<string, string?> inside = [];
        bool inParent = false;
        bool within = false;

        KeyValuesReader.Read(
            schema,
            (key, value, at) =>
            {
                // The file's shape: "items_game" at 0, "items" and "prefabs" at 1, an item or a
                // prefab at 2, and its keys at 3.
                if (at == 1)
                {
                    inParent = string.Equals(key, parent, StringComparison.Ordinal);
                    within = false;
                }
                else if (at == 2 && inParent)
                {
                    if (within)
                    {
                        // The next sibling started, so the block is complete.
                        return false;
                    }

                    within = string.Equals(key, name, StringComparison.Ordinal);
                }
                else if (at == 3 && within && value is not null)
                {
                    inside[key] = value;
                }

                return true;
            });

        return inside;
    }
}
