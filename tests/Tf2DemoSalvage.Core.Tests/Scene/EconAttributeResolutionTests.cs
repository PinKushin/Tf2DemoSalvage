using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// Which attribute list answers, in <c>CEconItemView::IterateAttributes</c>'s order.
/// </summary>
/// <remarks>
/// **The engine's chain, `econ_item_view.cpp:523`, and every branch is here because every branch
/// decides something a demo shows:**
///
/// <code>
///   pAttrList->IterateAttributes( pIterator );            // 1. local copies — always, and first
///   if ( pEconItem ) …                                    // 2. SOC data — NEVER in a demo
///   else if ( GetItemID() != INVALID_ITEM_ID
///             &amp;&amp; bHasNetworkedAttribsForDemos )
///       m_NetworkedDynamicAttributesForDemos.Iterate…     // 3. networked FOR demos
///   else if ( GetStaticData() )
///       GetStaticData()->IterateAttributes( … );          // 4. the definition's own
/// </code>
///
/// The wrapper (<c>CEconItemAttributeIterator_EconItemViewWrapper</c>) suppresses any definition
/// index branch 1 already produced, so the local list OVERRIDES rather than duplicates. Branches 3
/// and 4 are an <c>else if</c> chain: taking the demo list forecloses the static one, even for
/// attributes the demo list does not carry.
///
/// **`INVALID_ITEM_ID` is <c>(itemid_t)-1</c>** (<c>econ_item_constants.h:443</c>) — both networked
/// halves all-ones. An item that never sent an id is treated as invalid, which routes an era demo
/// (no econ system at all) to the definition's own attributes rather than to a list it cannot have.
/// </remarks>
public sealed class EconAttributeResolutionTests
{
    private static readonly EconAttributeValue LocalFive = new(5, 100);
    private static readonly EconAttributeValue DemosFive = new(5, 200);
    private static readonly EconAttributeValue StaticFive = new(5, 300);
    private static readonly EconAttributeValue DemosSeven = new(7, 700);
    private static readonly EconAttributeValue StaticNine = new(9, 900);

    [Test]
    public void Resolve_ADefinitionInBothLocalAndDemos_TakesTheLocalValue()
    {
        // The wrapper's whole purpose: branch 1 wins per definition, branches below fill gaps.
        IReadOnlyList<EconAttributeValue> resolved = EconAttributes.Resolve(
            local: [LocalFive],
            networkedForDemos: [DemosFive, DemosSeven],
            hasValidItemId: true,
            definitionAttributes: []);

        resolved.Single(attribute => attribute.DefinitionIndex == 5).RawBits.ShouldBe(100);
        resolved.Single(attribute => attribute.DefinitionIndex == 7).RawBits.ShouldBe(700);
    }

    [Test]
    public void Resolve_WithAValidItemIdAndADemosList_NeverReadsTheDefinition()
    {
        // **The else-if, which is easy to flatten into "merge everything".** Taking the demo list
        // forecloses the static branch even for definitions the demo list lacks — attribute 9
        // exists only on the definition and must NOT appear.
        IReadOnlyList<EconAttributeValue> resolved = EconAttributes.Resolve(
            local: [],
            networkedForDemos: [DemosFive],
            hasValidItemId: true,
            definitionAttributes: [StaticNine]);

        resolved.ShouldHaveSingleItem().DefinitionIndex.ShouldBe(5);
    }

    [Test]
    public void Resolve_WithAnInvalidItemId_SkipsTheDemosListEntirely()
    {
        // `GetItemID() != INVALID_ITEM_ID` guards branch 3 even when the list has content — the
        // definition's attributes answer instead.
        IReadOnlyList<EconAttributeValue> resolved = EconAttributes.Resolve(
            local: [],
            networkedForDemos: [DemosFive],
            hasValidItemId: false,
            definitionAttributes: [StaticFive, StaticNine]);

        resolved.Count.ShouldBe(2);
        resolved.Single(attribute => attribute.DefinitionIndex == 5).RawBits.ShouldBe(300);
        resolved.Single(attribute => attribute.DefinitionIndex == 9).RawBits.ShouldBe(900);
    }

    [Test]
    public void Resolve_WithAValidItemIdButAnEmptyDemosList_FallsToTheDefinition()
    {
        // `bHasNetworkedAttribsForDemos` is `GetNumAttributes() > 0`, noted BEFORE branch 1 runs —
        // an empty demo list does not satisfy the else-if and the chain continues.
        IReadOnlyList<EconAttributeValue> resolved = EconAttributes.Resolve(
            local: [LocalFive],
            networkedForDemos: [],
            hasValidItemId: true,
            definitionAttributes: [StaticFive, StaticNine]);

        resolved.Single(attribute => attribute.DefinitionIndex == 5).RawBits.ShouldBe(
            100, "local still overrides the definition");
        resolved.Single(attribute => attribute.DefinitionIndex == 9).RawBits.ShouldBe(900);
    }

    [Test]
    public void Resolve_WithNothingAnywhere_IsEmpty()
    {
        EconAttributes.Resolve([], [], hasValidItemId: false, definitionAttributes: [])
            .ShouldBeEmpty();
    }
}
