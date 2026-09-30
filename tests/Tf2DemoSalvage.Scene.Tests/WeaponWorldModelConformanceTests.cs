using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// What a TF weapon draws in the world when its item's `model_player` is empty: nothing (B105's open item).
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
            weapons.For);

        drawn[0].ModelPath.ShouldBe(string.Empty, "GetWorldModel hands back the item's \"\", which indexes no model");
    }

    /// <summary>The spellbook's carrier, an engineer.</summary>
    private const int Engineer = 3;

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
