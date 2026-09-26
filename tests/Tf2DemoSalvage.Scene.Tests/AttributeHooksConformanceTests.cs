using System;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`CALL_ATTRIB_HOOK_*` (game/shared/econ/attribute_manager.cpp) over what a player carries.</summary>
/// <remarks>
/// Item 100 is a gun whose definition multiplies `mult_clipsize` by 1.5; item 200 is a hat that doubles it. A weapon's
/// hook takes its own attributes, the player's, and every wearable's, but no other weapon's; a player's hook takes
/// everything. An item's own `m_AttributeList` overrides its definition.
/// </remarks>
public sealed class AttributeHooksConformanceTests
{
    private const string ItemsGame = """
        "items_game"
        {
            "attributes"
            {
                "1" { "name" "clip size bonus" "attribute_class" "mult_clipsize" "description_format" "value_is_percentage" }
                "2" { "name" "clip size add" "attribute_class" "mult_clipsize" "description_format" "value_is_additive" }
            }
            "items"
            {
                "100" { "name" "gun" "attributes" { "clip size bonus" { "attribute_class" "mult_clipsize" "value" "1.5" } } }
                "200" { "name" "hat" "static_attrs" { "clip size bonus" "2" } }
            }
        }
        """;

    private static readonly AttributeHooks Hooks = new(ItemSchema.Read(Encoding.UTF8.GetBytes(ItemsGame)));

    [Test]
    public void OnWeapon_ItsOwnAndTheWearables_ButNoOtherWeapon()
    {
        SceneItem gun = Item(1, 100, weapon: true);
        ScenePlayer player = Player(gun, Item(2, 100, weapon: true), Item(3, 200, weapon: false));

        Hooks.OnWeapon(player, gun, "mult_clipsize", 6f).ShouldBe(6f * 1.5f * 2f);
    }

    [Test]
    public void OnPlayer_EveryProvider()
    {
        ScenePlayer player = Player(Item(1, 100, weapon: true), Item(2, 100, weapon: true), Item(3, 200, weapon: false));

        Hooks.OnPlayer(player, "mult_clipsize", 6f).ShouldBe(6f * 1.5f * 1.5f * 2f);
    }

    [Test]
    public void OnWeapon_TheItemsOwnList_OverridesItsDefinition()
    {
        SceneItem gun = Item(1, 100, weapon: true, local: [new EconAttributeValue(1, BitConverter.SingleToInt32Bits(1.25f))]);

        Hooks.OnWeapon(Player(gun), gun, "mult_clipsize", 8f).ShouldBe(10f);
    }

    [Test]
    public void OnPlayer_ThePlayersOwnList_AppliesFirst()
    {
        ScenePlayer player = Player(Item(3, 200, weapon: false)) with
        {
            OwnAttributes = [new EconAttributeValue(2, BitConverter.SingleToInt32Bits(1f))],
        };

        Hooks.OnPlayer(player, "mult_clipsize", 6f).ShouldBe((6f + 1f) * 2f, "add first, then the hat's double");
    }

    [Test]
    public void OnWeapon_NoDefinition_IsTheDefaultItemWithNone()
    {
        SceneItem bare = new(1, "CTFScatterGun", null, new EconAttributeWire([], [], false), IsWeapon: true);

        Hooks.OnWeapon(Player(bare), bare, "mult_clipsize", 6f).ShouldBe(6f);
    }

    [TestCase(2.5f, 2)]
    [TestCase(3.5f, 4)]
    [TestCase(-2.5f, -2)]
    public void RoundFloatToInt_Halves_GoToEven(float value, int expected) => AttributeHooks.RoundFloatToInt(value).ShouldBe(expected);

    private static SceneItem Item(int entity, int definition, bool weapon, EconAttributeValue[]? local = null) =>
        new(entity, weapon ? "CTFScatterGun" : "CTFWearable", definition, new EconAttributeWire(local ?? [], [], false), weapon);

    private static ScenePlayer Player(params SceneItem[] items) => new(10, 0f, 0f, 0f, 2, 125, 1) { Items = items };
}
