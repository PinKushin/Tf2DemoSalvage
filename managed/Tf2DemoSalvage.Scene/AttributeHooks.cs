using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>`CALL_ATTRIB_HOOK_*` on a player or on one of its weapons, over what the demo says the player carries.</summary>
/// <remarks>
/// game/shared/econ/attribute_manager.cpp:
/// <list type="bullet">
/// <item>On the PLAYER (`CAttributeContainerPlayer::ApplyAttributeFloat`, :751): the player's own `m_AttributeList`, then
/// every provider — each weapon and wearable, through its item's attributes (:730).</item>
/// <item>On a WEAPON (`CAttributeContainer::ApplyAttributeFloat`): the weapon's item attributes, then its owner's — the
/// player's own list and its providers, skipping the weapon itself and, because the initiator is a weapon, every other
/// weapon ("Don't allow weapons to provide to other weapons being carried by the same person", :467).</item>
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
        Owner(player, attributeClass, value, initiator: null);

    /// <summary>`CALL_ATTRIB_HOOK_FLOAT` on one of the player's weapons.</summary>
    /// <param name="player">The weapon's owner.</param>
    /// <param name="weapon">The weapon.</param>
    /// <param name="attributeClass">The hook, such as `mult_clipsize`.</param>
    /// <param name="value">The value hooked.</param>
    /// <returns>The hooked value.</returns>
    public float OnWeapon(ScenePlayer player, SceneItem weapon, string attributeClass, float value)
    {
        ArgumentNullException.ThrowIfNull(weapon);

        return Owner(player, attributeClass, schema.Apply(Attributes(weapon), attributeClass, value), weapon);
    }

    /// <summary>`CALL_ATTRIB_HOOK_INT`: the float result rounded as `cvtss2si` rounds.</summary>
    /// <param name="value">The hooked float.</param>
    /// <returns>The integer.</returns>
    public static int RoundFloatToInt(float value) => (int)MathF.Round(value, MidpointRounding.ToEven);

    /// <summary>The player's own list, then its providers — less the initiator, and less every weapon when it is one.</summary>
    private float Owner(ScenePlayer player, string attributeClass, float value, SceneItem? initiator)
    {
        ArgumentNullException.ThrowIfNull(attributeClass);

        value = schema.Apply(player.OwnAttributes ?? [], attributeClass, value);

        foreach (SceneItem provider in player.Items ?? [])
        {
            if (provider.EntityIndex == initiator?.EntityIndex || (initiator is not null && provider.IsWeapon))
            {
                continue;
            }

            value = schema.Apply(Attributes(provider), attributeClass, value);
        }

        return value;
    }

    private IReadOnlyList<EconAttributeValue> Attributes(SceneItem item) =>
        EconAttributes.Resolve(
            item.Wire.Local,
            item.Wire.NetworkedForDemos,
            item.Wire.HasValidItemId,
            item.DefinitionIndex is { } definition ? schema.DefinitionAttributesFor(definition) : []);
}
