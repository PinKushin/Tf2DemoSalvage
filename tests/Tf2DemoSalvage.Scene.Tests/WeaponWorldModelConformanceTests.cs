using System;
using System.Collections.Generic;
using System.Text;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// What a TF weapon draws in the world: its valid item's `model_world` when it names one, else its `model_player` as it
/// is — so an empty one draws nothing (B105).
/// </summary>
/// <remarks>
/// **The engine, written down before the port, with its lines:**
///
/// <code>
///   const char *CTFWeaponBase::GetWorldModel( void ) const                       // tf_weaponbase.cpp:681
///   {
///       if ( pItem->IsValid() )
///       {
///           if ( pItem->GetWorldDisplayModel() ) return pItem->GetWorldDisplayModel();
///           ...
///           return pItem->GetPlayerDisplayModel( iClass, iTeam );                  // :698, "" included
///       }
///       return BaseClass::GetWorldModel();                                         // the script: an invalid item only
///   }
///
///   int CTFWeaponBase::GetWorldModelIndex( void )                                  // :3557
///       if ( m_iCachedModelIndex == 0 )
///           m_iCachedModelIndex = modelinfo->GetModelIndex( GetWorldModel() );     // :3600
///       if ( m_iWorldModelIndex != m_iCachedModelIndex )
///           m_iWorldModelIndex = m_iCachedModelIndex;                              // :3606, over the networked value
/// </code>
///
/// and `UpdateModelIndex` draws a third-person weapon with that index (:3462-3484). The item's `model_player` is the
/// merged KeyValues string — the nearest declaration wins whatever it holds (econ_item_schema.cpp:2909, :2962, :2967), an
/// empty token parses as a string (KeyValues.cpp:2537-2540), and `BInitFromKV` reads it with a NULL default (:3158) — and
/// `GetPlayerDisplayModel` returns it as it is (econ_item_view.cpp:969). So the four spellbooks and the two fists, whose
/// nearest value is "", index no model, and a weapon indexing none is not drawn: 0 fails `C_BaseCombatWeapon::ShouldDraw`
/// (c_basecombatweapon.cpp:401) and -1 is the invalid index `SetModelIndex` looks up (c_baseentity.cpp:1775-1779). Which
/// of the two the closed engine's `GetModelIndex` gives an empty name is not read here. The wire does not enter into it.
/// </remarks>
public sealed class WeaponWorldModelConformanceTests
{
    /// <remarks>
    /// **The shipped shape, read off `items_game.txt`**: `halloween2013_spellbook` declares `"model_player" ""` and no
    /// parent, and the Basic Spellbook and the stock spellbook name it and nothing else. The wire's model is what the
    /// `item-props` census measured for the one spellbook in the corpus whose wire names a model — `c_engineer_arms.mdl`,
    /// the carrier's hands out of `m_nModelIndex` with no world index sent — so the rule is asked against the model it has
    /// to refuse, on a weapon in hand.
    /// </remarks>
    [Test]
    public void Resolve_ASpellbookWhoseItemsModelPlayerIsEmpty_DrawsNoWorldModel()
    {
        WeaponModels weapons = new(_ => Encoding.UTF8.GetBytes(Schema), new RecordingLogger());

        List<SceneProp> drawn =
        [
            new(
                EntityIndex: 40,
                ModelPath: "models/weapons/c_models/c_engineer_arms.mdl",
                Kind: SceneModelKind.Studio,
                Pose: default,
                AttachedTo: Engineer,
                OwnedBy: Engineer,
                WeaponState: EntityState.WeaponActive,
                BoneMerged: true,
                ItemDefinitionIndex: 1070,
                ClassName: "CTFSpellBook"),
        ];

        new WeaponPropModels().Resolve(
            drawn,
            [new ScenePlayer(Engineer, 0f, 0f, 0f, Team: SceneTeams.Red, Health: 125, PlayerClass: 9)],
            weapons.For,
            weapons.WorldDisplayModel);

        drawn[0].ModelPath.ShouldBe(string.Empty, "GetWorldModel hands back the item's \"\", which indexes no model");
    }

    /// <remarks>
    /// **`model_world` comes first, and it is one string per item** (RISKS B105's open item, 2026-09-30):
    ///
    /// <code>
    ///   if ( pItem->GetWorldDisplayModel() )                                   // tf_weaponbase.cpp:686, a valid item only
    ///       return pItem->GetWorldDisplayModel();                              // :687
    ///   const char *CEconItemView::GetWorldDisplayModel() const               // econ_item_view.cpp:1034
    ///       return pData->GetWorldDisplayModel();                              // :1040, m_pszWorldDisplayModel (econ_item_schema.h:1342)
    ///   m_pszWorldDisplayModel = m_pKVItem->GetString( "model_world", NULL ); // econ_item_schema.cpp:3160
    /// </code>
    ///
    /// No class, team or style reaches it, so it outranks even a per-class `model_player`. **Only the third-person weapon
    /// asks**: the first-person attachment is `GetPlayerDisplayModel` (econ_entity.cpp:1167) and a wearable draws its
    /// networked index (tf_item_wearable.cpp:453-509). The two builder classes, which every sapper is, change nothing for
    /// these items: `C_TFWeaponSapper::GetWorldModel` skips the builder's own to reach this one (c_tf_weapon_builder.cpp:456-460),
    /// and `C_TFWeaponBuilder::GetWorldModel` returns the object's `Playermodel` from `scripts/objects.txt` only when it
    /// knows its `m_iObjectType` (:406-419), which `DT_BuilderLocalData` sends to the owner alone (tf_weapon_builder.cpp:32-40,
    /// `SendProxy_SendLocalWeaponDataTable`, basecombatweapon_shared.cpp:2739-2755) — for every other player it falls
    /// through to this rule.
    ///
    /// **The Hot Hand is the one shipped item it changes**: of the four that declare a `model_world`, the three sappers
    /// name their `model_player` again, and the glove names `w_slapping_glove.mdl` beside a first-person `c_slapping_glove.mdl`.
    /// Its block is copied from `items_game.txt`; the wire is the pyro's arms, the model `m_nModelIndex` holds for an
    /// attach-to-hands weapon (econ_entity.cpp:398-402), so the rule is asked against both models it must refuse.
    /// </remarks>
    [Test]
    public void Resolve_AHotHandInAPyrosHand_DrawsItsModelWorld()
    {
        WeaponModels weapons = new(_ => Encoding.UTF8.GetBytes(HotHandSchema), new RecordingLogger());

        List<SceneProp> drawn =
        [
            new(
                EntityIndex: 41,
                ModelPath: "models/weapons/c_models/c_pyro_arms.mdl",
                Kind: SceneModelKind.Studio,
                Pose: default,
                AttachedTo: Holder,
                OwnedBy: Holder,
                WeaponState: EntityState.WeaponActive,
                BoneMerged: true,
                ItemDefinitionIndex: 1181,
                ClassName: "CTFSlap"),
        ];

        new WeaponPropModels().Resolve(
            drawn,
            [new ScenePlayer(Holder, 0f, 0f, 0f, Team: SceneTeams.Blu, Health: 175, PlayerClass: Pyro)],
            weapons.For,
            weapons.WorldDisplayModel);

        drawn[0].ModelPath.ShouldBe("models/weapons/c_models/c_slapping_glove/w_slapping_glove.mdl");
    }

    /// <remarks>
    /// **The four shipped `model_world` declarations, in a hand, off the installed game** — each row read by hand out of
    /// `items_game.txt` (2026-09-30: four `"model_world"` keys in the file, every one an item's own, none empty). The three
    /// sappers name their `model_player` again, so only the glove's row can tell `model_world` from `model_player`; the
    /// sappers' rows say the rule reaches the class every sapper is drawn as. The wire is the carrier's arms, as
    /// <see cref="Resolve_AHotHandInAPyrosHand_DrawsItsModelWorld"/> gives.
    /// </remarks>
    [TestCase(933, "CTFWeaponSapper", Spy, SpyArms, "models/weapons/c_models/c_p2rec/c_p2rec.mdl")]
    [TestCase(1080, "CTFWeaponSapper", Spy, SpyArms, "models/weapons/c_models/c_sapper/c_sapper_xmas.mdl")]
    [TestCase(1102, "CTFWeaponSapper", Spy, SpyArms, "models/weapons/c_models/c_breadmonster_sapper/c_breadmonster_sapper.mdl")]
    [TestCase(1181, "CTFSlap", Pyro, PyroArms, "models/weapons/c_models/c_slapping_glove/w_slapping_glove.mdl")]
    public void Resolve_AShippedItemNamingAModelWorldInHand_DrawsIt(
        int item, string weaponClass, int playerClass, string wire, string modelWorld)
    {
        GameInstall.Require();

        ResolveHeld(item, weaponClass, playerClass, wire).ShouldBe(modelWorld);
    }

    /// <remarks>
    /// **The control: the stock sapper names no `model_world`, and draws its `model_player`.** Item 735 takes
    /// `"item_class" "tf_weapon_builder"` from the `weapon_sapper` prefab, so its weapon is a `CTFWeaponBuilder`; asked for
    /// another player, it has no object type and reaches its item (c_tf_weapon_builder.cpp:406-419), which names
    /// `c_sapper.mdl` — as the object's own `Playermodel` in `scripts/objects.txt` does for the owner, so neither route differs.
    /// </remarks>
    [Test]
    public void Resolve_TheStockSapperInHand_DrawsItsModelPlayer()
    {
        GameInstall.Require();

        ResolveHeld(735, "CTFWeaponBuilder", Spy, SpyArms).ShouldBe("models/weapons/c_models/c_sapper/c_sapper.mdl");
    }

    /// <summary>What production resolves a held weapon to, with the installed game's schema.</summary>
    private static string ResolveHeld(int item, string weaponClass, int playerClass, string wire)
    {
        WeaponModels weapons = Installed.Value.Weapons;

        List<SceneProp> drawn =
        [
            new(
                EntityIndex: 41,
                ModelPath: wire,
                Kind: SceneModelKind.Studio,
                Pose: default,
                AttachedTo: Holder,
                OwnedBy: Holder,
                WeaponState: EntityState.WeaponActive,
                BoneMerged: true,
                ItemDefinitionIndex: item,
                ClassName: weaponClass),
        ];

        new WeaponPropModels().Resolve(
            drawn,
            [new ScenePlayer(Holder, 0f, 0f, 0f, Team: SceneTeams.Blu, Health: 125, PlayerClass: playerClass)],
            weapons.For,
            weapons.WorldDisplayModel);

        return drawn[0].ModelPath;
    }

    /// <summary>The installed game, opened as the viewer opens it; once, since the schema is eight megabytes.</summary>
    private static readonly Lazy<GameContent> Installed = new(() => GameContent.Open(GameInstall.Root, NullLoggerFactory.Instance));

    /// <summary>The spellbook's carrier, an engineer.</summary>
    private const int Engineer = 3;

    /// <summary>Whoever holds the weapon in the model_world cases.</summary>
    private const int Holder = 5;

    /// <summary>`TF_CLASS_PYRO`.</summary>
    private const int Pyro = 7;

    /// <summary>`TF_CLASS_SPY`.</summary>
    private const int Spy = 8;

    private const string SpyArms = "models/weapons/c_models/c_spy_arms.mdl";

    private const string PyroArms = "models/weapons/c_models/c_pyro_arms.mdl";

    /// <summary>The Hot Hand's block as `items_game.txt` ships it, less its sounds, attributes and descriptions.</summary>
    private const string HotHandSchema = """
        "items_game"
        {
            "items"
            {
                "1181"
                {
                    "name"              "The Hot Hand"
                    "item_class"        "tf_weapon_slap"
                    "item_slot"         "melee"
                    "anim_slot"         "MELEE_ALLCLASS"
                    "model_player"      "models/weapons/c_models/c_slapping_glove/c_slapping_glove.mdl"
                    "model_world"       "models/weapons/c_models/c_slapping_glove/w_slapping_glove.mdl"
                    "attach_to_hands"   "1"
                    "used_by_classes"
                    {
                        "pyro"          "1"
                    }
                }
            }
        }
        """;

    /// <summary>`halloween2013_spellbook` and two of its items, as the shipped file has them.</summary>
    private const string Schema = """
        "items_game"
        {
            "prefabs"
            {
                "halloween2013_spellbook"
                {
                    "item_class"        "tf_weapon_spellbook"
                    "item_slot"         "action"
                    "attach_to_hands"   "1"
                    "model_player"      ""
                }
            }
            "items"
            {
                "1070"
                {
                    "name"      "Basic Spellbook"
                    "prefab"    "halloween2013_spellbook"
                }
                "1132"
                {
                    "name"      "TF_WEAPON_SPELLBOOK"
                    "prefab"    "halloween2013_spellbook"
                    "baseitem"  "1"
                }
            }
        }
        """;
}
