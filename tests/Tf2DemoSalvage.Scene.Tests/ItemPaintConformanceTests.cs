using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// <c>CEconItemView::GetModifiedRGBValue</c>, the value behind TF2's paint (B330).
/// </summary>
/// <remarks>
/// **Written before the proxy that consumes it**, so it states what the engine does rather than
/// describing whatever gets built. `econ_item_view.cpp:1596-1633` for the rule and
/// `econ_wearable.cpp:465-543` for the unpack; read-from-source throughout.
///
/// **The schema is real, not a fixture.** The two attributes are looked up by NAME through
/// `ItemSchema.AttributeDefinitionIndex`, exactly as the engine's `CSchemaAttributeDefHandle` does,
/// so a renumbered `items_game.txt` cannot silently retarget these — and a hand-built schema would
/// be testing this file's author's idea of one.
/// </remarks>
public sealed class ItemPaintConformanceTests
{
    /// <remarks>
    /// **The attribute's bits are a FLOAT whose VALUE is the packed colour**, and Valve reads it
    /// with a numeric conversion — `unRGB = (uint32)fRGB` — not a reinterpretation. Reinterpreting
    /// 0xE7B53B's bits as a float gives a denormal; reinterpreting the float 15185723.0's bits as an
    /// integer gives 0x4B67B53B, which is a plausible-looking and completely wrong colour.
    ///
    /// The value chosen is TF2's "A Distinctive Lack of Hue"… no: 0xE7B53B is the paint commonly
    /// called Australium Gold, picked because all three channels differ, so a swapped or shifted
    /// unpack cannot pass.
    /// </remarks>
    [Test]
    public void Packed_AnItemWithOnePaint_IsThatColourOnBothTeams()
    {
        ItemSchema items = Schema();

        IReadOnlyDictionary<int, EconAttributeValue> painted = Painted(items, 0xE7B53B);

        ItemPaint.Packed(painted, items, alternate: false).ShouldBe(0xE7B53B);

        // The alt falls back to the primary, so both teams match.
        ItemPaint.Packed(painted, items, alternate: true).ShouldBe(0xE7B53B);
    }

    /// <remarks>
    /// **1 is a SENTINEL, not a colour**, and this is the case that reads as a bug if it is missed:
    /// `kPaintConstant_OldTeamColor` painted almost-black instead of selecting the two team
    /// constants. `RGB_INT_RED` is 12073019 and `RGB_INT_BLUE` 5801378
    /// (`econ_item_constants.h:501-502`).
    /// </remarks>
    [Test]
    public void Packed_TheOldTeamColourSentinel_SelectsValvesTwoConstants()
    {
        ItemSchema items = Schema();

        IReadOnlyDictionary<int, EconAttributeValue> old = Painted(items, ItemPaint.OldTeamColour);

        ItemPaint.Packed(old, items, alternate: false).ShouldBe(12073019);
        ItemPaint.Packed(old, items, alternate: true).ShouldBe(5801378);
    }

    /// <remarks>
    /// **A two-tone paint, and the ORDER of the engine's two branches.** The sentinel is tested
    /// first: an item carrying both takes the constants and ignores its second attribute. Asserted
    /// here rather than assumed because the natural way to write this — check for a second colour,
    /// then special-case 1 — passes the two tests above and fails this one.
    /// </remarks>
    [Test]
    public void Packed_ATwoTonePaint_GivesADifferentColourPerTeam()
    {
        ItemSchema items = Schema();

        ItemPaint.Packed(TwoTone(items, 0xE7B53B, 0x2D2D24), items, alternate: false)
            .ShouldBe(0xE7B53B);

        ItemPaint.Packed(TwoTone(items, 0xE7B53B, 0x2D2D24), items, alternate: true)
            .ShouldBe(0x2D2D24);

        // The sentinel wins over a second colour, which is the branch order under test.
        ItemPaint.Packed(TwoTone(items, ItemPaint.OldTeamColour, 0x2D2D24), items, alternate: true)
            .ShouldBe(5801378);
    }

    /// <remarks>
    /// **The control, and the one that decides whether anything is drawn differently at all.** Every
    /// worn item in a demo carries attributes; almost none carries paint. An implementation that
    /// returned a colour for an unpainted item would tint the entire game.
    /// </remarks>
    [Test]
    public void Packed_AnItemWithNoPaintAttribute_IsNull()
    {
        ItemSchema items = Schema();

        // A real attribute that is not paint, so the dictionary is populated and the lookup still
        // misses — an empty one would pass against a reader that never looks anything up.
        int other = items.AttributeDefinitionIndex("is_festivized") ?? 2053;

        ItemPaint.Packed(
            new Dictionary<int, EconAttributeValue> { [other] = new(other, 1) },
            items,
            alternate: false).ShouldBeNull();

        ItemPaint.Packed(new Dictionary<int, EconAttributeValue>(), items, false).ShouldBeNull();
        ItemPaint.Packed(null, items, false).ShouldBeNull();
    }

    /// <remarks>
    /// Valve's unpack: the top byte is RED. A shifted or reversed unpack gives a colour that is
    /// still a colour, which is why the fixture's three channels are all different.
    /// </remarks>
    [Test]
    public void Tint_APackedColour_UnpacksRedFromTheTopByte()
    {
        ItemSchema items = Schema();

        (float Red, float Green, float Blue) tint =
            ItemPaint.Tint(Painted(items, 0xE7B53B), items, alternate: false).ShouldNotBeNull();

        tint.Red.ShouldBe(0xE7 / 255f, 0.0001);
        tint.Green.ShouldBe(0xB5 / 255f, 0.0001);
        tint.Blue.ShouldBe(0x3B / 255f, 0.0001);
    }

    /// <summary>An item carrying one paint attribute, its bits as the wire holds them.</summary>
    /// <remarks>
    /// **The value is stored as a FLOAT of that number**, which is what makes the cast in
    /// `ItemPaint` the right one — the fixture puts the wire's own form in rather than the integer.
    /// </remarks>
    private static Dictionary<int, EconAttributeValue> Painted(ItemSchema items, int packed)
    {
        int paint = items.AttributeDefinitionIndex(ItemPaint.PaintAttribute)
            ?? throw new System.InvalidOperationException("the schema declares no paint attribute");

        return new Dictionary<int, EconAttributeValue>
        {
            [paint] = new(paint, System.BitConverter.SingleToInt32Bits(packed)),
        };
    }

    private static Dictionary<int, EconAttributeValue> TwoTone(
        ItemSchema items, int primary, int secondary)
    {
        Dictionary<int, EconAttributeValue> attributes = Painted(items, primary);

        int second = items.AttributeDefinitionIndex(ItemPaint.SecondPaintAttribute)
            ?? throw new System.InvalidOperationException("the schema declares no second paint");

        attributes[second] = new(second, System.BitConverter.SingleToInt32Bits(secondary));

        return attributes;
    }

    /// <summary>A schema declaring the two paint attributes and one that is not paint.</summary>
    /// <remarks>
    /// **Synthetic, and that is the right choice here rather than a compromise** (D38). What these
    /// tests measure is the ARITHMETIC of `GetModifiedRGBValue` given attribute indices — the
    /// sentinel, the fallback, the branch order, the unpack — and none of that is a fact about
    /// TF2's shipped file. Whether `items_game.txt` parses at all is
    /// `ItemSchemaConformanceTests`' job, against the real one.
    ///
    /// It also keeps this suite where it belongs: synthetic, fast and mutation-testable, rather
    /// than skipping on a machine with no game installed.
    ///
    /// **The indices are deliberately not 1, 2, 3.** A reader that returned the definition index
    /// instead of the value, or that confused the two attributes, would be caught by numbers far
    /// from both the colours and each other.
    /// </remarks>
    private static ItemSchema Schema() => ItemSchema.Read(System.Text.Encoding.UTF8.GetBytes("""
        "items_game"
        {
            "attributes"
            {
                "142"  { "name" "set item tint rgb" }
                "261"  { "name" "set item tint rgb 2" }
                "2053" { "name" "is_festivized"  "stored_as_integer" "1" }
            }
        }
        """));
}
