using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// `CALL_ATTRIB_HOOK_*` over an item definition's attributes: every attribute whose `attribute_class` matches is applied to
/// the running value by its `description_format` — `ApplyAttribute` (`econ/attribute_manager.cpp:580`).
/// </summary>
/// <remarks>
/// The Quick-Fix's `set_weapon_mode` 2 arrives through an attribute named "lunchbox adds minicrits", whose format is
/// additive — so the hook's 0 becomes 2, and `CWeaponMedigun::GetHealSound` indexes its table with it.
/// </remarks>
public sealed class ItemSchemaHookConformanceTests
{
    private const string Schema = """
        "items_game"
        {
            "prefabs"
            {
                "medigun"
                {
                    "attributes"
                    {
                        "heal rate bonus" { "attribute_class" "mult_medigun_healrate" "value" "1.25" }
                    }
                }
            }
            "items"
            {
                "411"
                {
                    "prefab" "medigun"
                    "attributes"
                    {
                        "lunchbox adds minicrits" { "attribute_class" "set_weapon_mode" "value" "2" }
                        "heal rate bonus" { "attribute_class" "mult_medigun_healrate" "value" "1.4" }
                        "overheal penalty" { "attribute_class" "mult_medigun_healrate" "value" "0.5" }
                        "flags" { "attribute_class" "or_flags" "value" "4" }
                    }
                }
            }
            "attributes"
            {
                "1" { "name" "lunchbox adds minicrits" "attribute_class" "set_weapon_mode" "description_format" "value_is_additive" "stored_as_integer" "0" }
                "2" { "name" "heal rate bonus" "attribute_class" "mult_medigun_healrate" "description_format" "value_is_percentage" }
                "3" { "name" "overheal penalty" "attribute_class" "mult_medigun_healrate" "description_format" "value_is_inverted_percentage" }
                "4" { "name" "flags" "attribute_class" "or_flags" "description_format" "value_is_or" "stored_as_integer" "1" }
            }
        }
        """;

    [Test]
    public void HookValue_AnAdditiveAttribute_AddsToTheInitialValue() =>
        Read().HookValue(411, "set_weapon_mode", 0f).ShouldBe(2f);

    [Test]
    public void HookValue_TwoPercentages_MultiplyAndTheItemOverridesItsPrefab()
    {
        // The item restates "heal rate bonus", so the prefab's 1.25 is not applied: 1 · 1.4 · 0.5.
        Read().HookValue(411, "mult_medigun_healrate", 1f).ShouldBe(0.7f, 1e-6f);
    }

    [Test]
    public void HookValue_AnOrAttribute_SetsItsBits() => Read().HookValue(411, "or_flags", 1f).ShouldBe(5f);

    [Test]
    public void HookValue_NoMatchingClass_IsTheInitialValue() => Read().HookValue(411, "set_charge_type", 7f).ShouldBe(7f);

    private static ItemSchema Read() => ItemSchema.Read(Encoding.UTF8.GetBytes(Schema));
}
