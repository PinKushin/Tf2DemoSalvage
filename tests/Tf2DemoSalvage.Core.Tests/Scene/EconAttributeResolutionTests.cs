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

    /// <remarks>
    /// **The walk replaced a set (B599), and this holds it to the set's answer on every small shape**: each list 0-3
    /// long over two definition indices, so duplicates within the local list, within the fallback and across the two all
    /// occur, under both item-id answers. The oracle is the algorithm the walk replaced, written out here — first writer
    /// wins per definition over local then the chosen fallback — compared in order and by value.
    /// </remarks>
    [Test]
    public void Iterate_EveryShapeUpToThreeEntriesOverTwoDefinitions_MatchesTheFirstWriterWinsSet()
    {
        List<EconAttributeValue[]> shapes = Shapes(offset: 0);
        int compared = 0;

        foreach (EconAttributeValue[] local in shapes)
        {
            foreach (EconAttributeValue[] demos in Shapes(offset: 1000))
            {
                foreach (EconAttributeValue[] definition in Shapes(offset: 2000))
                {
                    foreach (bool valid in (bool[])[true, false])
                    {
                        List<EconAttributeValue> walked = [];

                        foreach (EconAttributeValue attribute in EconAttributes.Iterate(local, demos, valid, definition))
                        {
                            walked.Add(attribute);
                        }

                        walked.ShouldBe(FirstWriterWins(local, valid && demos.Length > 0 ? demos : definition));
                        compared++;
                    }
                }
            }
        }

        compared.ShouldBe(15 * 15 * 15 * 2);
    }

    /// <remarks>
    /// **What the walk is FOR: an item's attributes asked per prop per frame with nothing allocated** (B599). Warmed once
    /// so the JIT's own allocations are not counted, then walked again with the duplicates that made the old version
    /// need a set.
    /// </remarks>
    [Test]
    public void Iterate_WalkedWarm_AllocatesNothing()
    {
        EconAttributeValue[] local = [LocalFive, new(8, 1)];
        EconAttributeValue[] demos = [DemosFive, DemosSeven, DemosSeven];

        int Walk()
        {
            int sum = 0;

            foreach (EconAttributeValue attribute in EconAttributes.Iterate(local, demos, true, []))
            {
                sum += attribute.DefinitionIndex;
            }

            return sum;
        }

        Walk().ShouldBe(5 + 8 + 7);
        long before = System.GC.GetAllocatedBytesForCurrentThread();
        int again = Walk();
        long bytes = System.GC.GetAllocatedBytesForCurrentThread() - before;

        again.ShouldBe(5 + 8 + 7);
        bytes.ShouldBe(0L);
    }

    /// <summary>Every list 0-3 long whose entries name definition 1 or 2; each entry's bits are unique, so order shows.</summary>
    private static List<EconAttributeValue[]> Shapes(int offset)
    {
        List<EconAttributeValue[]> shapes = [];

        for (int length = 0; length <= 3; length++)
        {
            for (int pick = 0; pick < 1 << length; pick++)
            {
                EconAttributeValue[] shape = new EconAttributeValue[length];

                for (int at = 0; at < length; at++)
                {
                    shape[at] = new EconAttributeValue(1 + ((pick >> at) & 1), offset + (length * 100) + (pick * 10) + at);
                }

                shapes.Add(shape);
            }
        }

        return shapes;
    }

    private static List<EconAttributeValue> FirstWriterWins(EconAttributeValue[] local, EconAttributeValue[] fallback)
    {
        List<EconAttributeValue> resolved = [];
        HashSet<int> seen = [];

        foreach (EconAttributeValue attribute in local.Concat(fallback))
        {
            if (seen.Add(attribute.DefinitionIndex))
            {
                resolved.Add(attribute);
            }
        }

        return resolved;
    }
}
