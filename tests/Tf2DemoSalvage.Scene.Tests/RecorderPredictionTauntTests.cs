using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Prediction;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// A moving taunt's speed off its item, as <c>C_TFPlayer::UpdateTauntItem</c> (<c>c_tf_player.cpp:4899-4926</c>) and
/// <c>ParseSharedTauntDataFromEconItemView</c> (<c>tf_player_shared.cpp:13156-13171</c>) read it.
/// </summary>
/// <remarks>
/// With <c>m_nActiveTauntSlot</c> at <c>LOADOUT_POSITION_INVALID</c> the view is <c>m_iTauntItemDefIndex</c>'s definition
/// (<c>Init( m_iTauntItemDefIndex, AE_UNIQUE, 1 )</c>), so <c>FindAttribute</c> finds its static attributes; an attribute
/// the item lacks leaves the value 0. A loadout slot is the GC inventory's item, which no demo carries. The schema's
/// attributes are the real ones (<c>items_game.txt</c> 600, 688, 689); item 1157 is invented.
/// </remarks>
public sealed class RecorderPredictionTauntTests
{
    private const string ItemsGame = """
        "items_game"
        {
            "attributes"
            {
                "600" { "name" "taunt force move forward" "attribute_class" "taunt_force_move_forward" "description_format" "value_is_additive" "stored_as_integer" "0" }
                "688" { "name" "taunt move acceleration time" "attribute_class" "taunt_move_acceleration" "description_format" "value_is_additive" }
                "689" { "name" "taunt move speed" "attribute_class" "taunt_move_speed" "description_format" "value_is_additive" }
            }
            "items"
            {
                "1157"
                {
                    "name" "a moving taunt"
                    "attributes"
                    {
                        "taunt force move forward" { "attribute_class" "taunt_force_move_forward" "value" "1" }
                        "taunt move speed" { "attribute_class" "taunt_move_speed" "value" "50" }
                        "taunt move acceleration time" { "attribute_class" "taunt_move_acceleration" "value" "0.5" }
                    }
                }
                "1158" { "name" "a still taunt" }
            }
        }
        """;

    private static readonly AttributeHooks Hooks = new(ItemSchema.Read(Encoding.UTF8.GetBytes(ItemsGame)));

    [Test]
    public void TauntMovementOf_ATauntItemByDefinition_ReadsItsThreeAttributes() =>
        RecorderPrediction.TauntMovementOf(Taunting(slot: -1, definition: 1157), Hooks)
            .ShouldBe(new TauntMovement(ForceForward: true, Speed: 50f, Acceleration: 0.5f));

    [Test]
    public void TauntMovementOf_AnItemWithoutTheAttributes_ReadsZeros() =>
        RecorderPrediction.TauntMovementOf(Taunting(slot: -1, definition: 1158), Hooks)
            .ShouldBe(new TauntMovement(ForceForward: false, Speed: 0f, Acceleration: 0f));

    [Test]
    public void TauntMovementOf_ATauntFromALoadoutSlot_IsNotKnown() =>
        RecorderPrediction.TauntMovementOf(Taunting(slot: 11, definition: 1157), Hooks).ShouldBeNull();

    [Test]
    public void TauntMovementOf_NoTauntItem_IsNotKnown() =>
        RecorderPrediction.TauntMovementOf(Taunting(slot: -1, definition: 65535), Hooks).ShouldBeNull();

    private static ScenePlayer Taunting(int slot, int definition) =>
        new(1, 0f, 0f, 0f, 2, 125, 1) { ActiveTauntSlot = slot, TauntItemDefIndex = definition };
}
