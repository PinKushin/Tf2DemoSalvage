using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>One econ attribute as an entity carries it: which definition, and its 32 raw bits.</summary>
/// <param name="DefinitionIndex">
/// <c>m_iAttributeDefinitionIndex</c> — the row in <c>items_game.txt</c>'s <c>attributes</c>
/// section. 2053 is <c>is_festivized</c>.
/// </param>
/// <param name="RawBits">
/// The value exactly as the union holds it. Valve's comment at <c>econ_item_view.cpp:62</c>: *"we
/// are networking the value as an int, even though it's a 'float', because really it isn't a
/// float. It's 32 raw bits."* Most attributes keep a float's bits here; a <c>stored_as_integer</c>
/// attribute keeps an integer, and reading it as a float produces a denormal rather than the
/// number — which is why the bits are the stored form and <see cref="Value"/> is derived.
/// </param>
public readonly record struct EconAttributeValue(int DefinitionIndex, int RawBits)
{
    /// <summary>The bits read as the float most attributes are.</summary>
    public float Value => BitConverter.Int32BitsToSingle(RawBits);

    /// <summary>The bits read as the integer a <c>stored_as_integer</c> attribute is.</summary>
    public int AsInteger => RawBits;
}

/// <summary>Everything the WIRE contributes to an entity's attribute resolution.</summary>
/// <param name="Local">Branch 1's list — <c>m_AttributeList</c>, which overrides.</param>
/// <param name="NetworkedForDemos">Branch 3's list.</param>
/// <param name="HasValidItemId">
/// Branch 3's guard: whether <c>m_iItemIDHigh</c>/<c>m_iItemIDLow</c> arrived and are not the
/// all-ones <c>INVALID_ITEM_ID</c>. Never-sent counts as invalid, which routes an era demo — no
/// econ system at all — to the definition's own attributes.
/// </param>
/// <remarks>
/// **Carried unresolved, because branch 4 lives in another layer.** The definition's attributes
/// come from <c>items_game.txt</c>, which Core cannot read — so the track carries the wire's three
/// inputs and the consumer completes <see cref="EconAttributes.Resolve"/> with the schema's list.
/// Resolving here with an empty branch 4 would bake "no definition attributes" into every prop and
/// look finished.
/// </remarks>
public sealed record EconAttributeWire(
    IReadOnlyList<EconAttributeValue> Local,
    IReadOnlyList<EconAttributeValue> NetworkedForDemos,
    bool HasValidItemId);

/// <summary>Resolving which attribute list answers, as the engine resolves it.</summary>
public static class EconAttributes
{
    /// <summary>Merges the sources in <c>CEconItemView::IterateAttributes</c>'s order.</summary>
    /// <param name="local">Branch 1 — <c>m_AttributeList</c>, which always runs and overrides.</param>
    /// <param name="networkedForDemos">Branch 3's list.</param>
    /// <param name="hasValidItemId">
    /// Branch 3's other guard: <c>GetItemID() != INVALID_ITEM_ID</c>, where invalid is
    /// <c>(itemid_t)-1</c>. An item that never sent an id counts as invalid.
    /// </param>
    /// <param name="definitionAttributes">Branch 4 — the item definition's own attributes.</param>
    /// <returns>The resolved attributes, first-writer-wins per definition index.</returns>
    /// <remarks>
    /// **The chain, `econ_item_view.cpp:523`, with branch 2 deliberately absent** — SOC data is the
    /// local player's own inventory and cannot exist in a recording, which is the whole reason
    /// branch 3's list is networked. The wrapper
    /// (`CEconItemAttributeIterator_EconItemViewWrapper`) suppresses definitions branch 1 already
    /// produced, so this is first-writer-wins per definition index. Branches 3 and 4 are an
    /// `else if`: taking the demo list forecloses the definition's attributes even for indices the
    /// demo list does not carry.
    /// </remarks>
    public static IReadOnlyList<EconAttributeValue> Resolve(
        IReadOnlyList<EconAttributeValue> local,
        IReadOnlyList<EconAttributeValue> networkedForDemos,
        bool hasValidItemId,
        IReadOnlyList<EconAttributeValue> definitionAttributes)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(networkedForDemos);
        ArgumentNullException.ThrowIfNull(definitionAttributes);

        List<EconAttributeValue> resolved = [];
        HashSet<int> seen = [];

        foreach (EconAttributeValue attribute in local)
        {
            if (seen.Add(attribute.DefinitionIndex))
            {
                resolved.Add(attribute);
            }
        }

        // `bHasNetworkedAttribsForDemos` is noted from the list's own count; the else-if chain
        // means exactly one of the two fallbacks runs.
        IReadOnlyList<EconAttributeValue> fallback =
            hasValidItemId && networkedForDemos.Count > 0
                ? networkedForDemos
                : definitionAttributes;

        foreach (EconAttributeValue attribute in fallback)
        {
            if (seen.Add(attribute.DefinitionIndex))
            {
                resolved.Add(attribute);
            }
        }

        return resolved;
    }
}

/// <summary>Which of an item's two networked attribute lists is being asked about.</summary>
/// <remarks>
/// **<c>DT_ScriptCreatedItem</c> embeds <c>DT_AttributeList</c> twice**
/// (<c>econ_item_view.cpp:191,193</c>), and <c>CEconItemView::IterateAttributes</c> (<c>:523</c>)
/// fixes their precedence: the local list is read first and overrides; the networked-for-demos
/// list is the fallback when there is no SOC data — which, in a recording, is always.
/// </remarks>
public enum EconAttributeList
{
    /// <summary><c>m_AttributeList</c> — local copies, which override everything else.</summary>
    Local,

    /// <summary>
    /// <c>m_NetworkedDynamicAttributesForDemos</c> — networked so a DEMO can resolve attributes
    /// without the inventory backend the live client asks. The name is Valve's, and it is this
    /// project's whole reason for reading the list.
    /// </summary>
    NetworkedForDemos,
}
