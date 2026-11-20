using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>`CALL_ATTRIB_HOOK_*` on a player or on one of its items, over what the demo says the player carries.</summary>
/// <remarks>
/// game/shared/econ/attribute_manager.cpp:
/// <list type="bullet">
/// <item>On the PLAYER (`CAttributeContainerPlayer::ApplyAttributeFloat`, :751): the player's own `m_AttributeList`, then
/// every provider — each weapon and wearable, through its item's attributes (:730).</item>
/// <item>On an ITEM, weapon or wearable (`CAttributeContainer::ApplyAttributeFloat`, :730-745): the item's attributes, then
/// its owner's — the player's own list, and none of his providers: "Don't allow weapons to provide to other weapons"
/// (:467-473) compares provider TYPES, and every econ entity is `PROVIDER_WEAPON` (econ_entity.cpp:240). Before B69 f a
/// wearable here provided to a weapon, and a canteen's hook skipped the weapons.</item>
/// <item>An item's attributes are `CEconItemView::IterateAttributes` (<see cref="EconAttributes.Resolve"/>); an item
/// with no definition index is the `default` item, which carries none.</item>
/// <item>`CALL_ATTRIB_HOOK_INT` rounds the float result with `RoundFloatToInt` — `cvtss2si`, to nearest, ties to even.</item>
/// </list>
/// **Interpolated:** the order providers are visited (`m_Providers`, filled by `ProvideTo` as items equip) — weapons
/// then wearables here. It matters only when one hook mixes multiplying and adding attributes.
/// </remarks>
/// <param name="schema">`items_game.txt`.</param>
public sealed class AttributeHooks(ItemSchema schema)
{
    /// <summary>`CALL_ATTRIB_HOOK_FLOAT` on the player.</summary>
    /// <param name="player">The player; its <see cref="ScenePlayer.Items"/> are the providers.</param>
    /// <param name="attributeClass">The hook, such as `mult_maxammo_primary`.</param>
    /// <param name="value">The value hooked.</param>
    /// <returns>The hooked value.</returns>
    public float OnPlayer(ScenePlayer player, string attributeClass, float value) =>
        Owner(player, attributeClass, value, itemInitiated: false);

    /// <summary>`CALL_ATTRIB_HOOK_FLOAT` on one of the player's items, weapon or wearable.</summary>
    /// <param name="player">The item's owner.</param>
    /// <param name="item">The item.</param>
    /// <param name="attributeClass">The hook, such as `mult_clipsize`.</param>
    /// <param name="value">The value hooked.</param>
    /// <returns>The hooked value.</returns>
    public float OnItem(ScenePlayer player, SceneItem item, string attributeClass, float value)
    {
        ArgumentNullException.ThrowIfNull(item);

        return Owner(player, attributeClass, schema.Apply(Attributes(item), attributeClass, value), itemInitiated: true);
    }

    /// <summary>
    /// `CALL_ATTRIB_HOOK_FLOAT` on an econ entity of the player's that carries no item — a weapon's extra wearable, which
    /// `CTFWeaponBase` makes with a model and no `SetItem` (tf_weaponbase.cpp:972-1026): its own (empty) item, then its
    /// owner's own list, as on any item (B69 review 3).
    /// </summary>
    /// <param name="player">The entity's owner, <c>GetAttributeOwner()</c> (econ_entity.h:144).</param>
    /// <param name="attributeClass">The hook.</param>
    /// <param name="value">The value hooked.</param>
    /// <returns>The hooked value.</returns>
    public float OnItemWithoutAttributes(ScenePlayer player, string attributeClass, float value) =>
        Owner(player, attributeClass, value, itemInitiated: true);

    /// <summary>
    /// `CEconItemView::FindAttribute` on a view `Init` from a definition alone (`AE_UNIQUE`, no item id), which holds only
    /// the definition's static attributes: the named attribute's 32 raw bits, or null when it does not carry it.
    /// </summary>
    /// <param name="definitionIndex">The item definition.</param>
    /// <param name="attributeName">The attribute's `name`, as a `CSchemaAttributeDefHandle` names it.</param>
    /// <returns>The raw bits, or null.</returns>
    public int? DefinitionAttribute(int definitionIndex, string attributeName)
    {
        if (schema.AttributeDefinitionIndex(attributeName) is not { } attribute)
        {
            return null;
        }

        foreach (EconAttributeValue value in schema.DefinitionAttributesFor(definitionIndex))
        {
            if (value.DefinitionIndex == attribute)
            {
                return value.RawBits;
            }
        }

        return null;
    }

    /// <summary>`CALL_ATTRIB_HOOK_INT`: the float result rounded as `cvtss2si` rounds.</summary>
    /// <param name="value">The hooked float.</param>
    /// <returns>The integer.</returns>
    public static int RoundFloatToInt(float value) => (int)MathF.Round(value, MidpointRounding.ToEven);

    /// <summary>The player's own list, then its providers — none of them when an item initiated the hook.</summary>
    /// <remarks>
    /// "Don't allow weapons to provide to other weapons" skips a provider when both it and the initiator are
    /// `PROVIDER_WEAPON` (attribute_manager.cpp:467-473), and every econ entity is one: `CEconEntity::InitializeAttributes`
    /// sets it (econ_entity.cpp:240), run by `OnDataChanged` on creation (:1025) for wearables as for weapons. Every
    /// provider is an econ item (`ProvideTo` from `ReapplyProvision`), so an item's hook reaches no other item (B69 f).
    /// </remarks>
    private float Owner(ScenePlayer player, string attributeClass, float value, bool itemInitiated)
    {
        ArgumentNullException.ThrowIfNull(attributeClass);

        value = schema.Apply(player.OwnAttributes ?? [], attributeClass, value);

        if (itemInitiated)
        {
            return value;
        }

        foreach (SceneItem provider in player.Items ?? [])
        {

            value = schema.Apply(Attributes(provider), attributeClass, value);
        }

        return value;
    }

    private ResolvedAttributes Attributes(SceneItem item) =>
        EconAttributes.Iterate(
            item.Wire.Local,
            item.Wire.NetworkedForDemos,
            item.Wire.HasValidItemId,
            item.DefinitionIndex is { } definition ? schema.DefinitionAttributesFor(definition) : []);
}
