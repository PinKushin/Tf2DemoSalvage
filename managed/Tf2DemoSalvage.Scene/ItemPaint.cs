using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// The colour a painted item is tinted with — <c>CEconItemView::GetModifiedRGBValue</c> (B330).
/// </summary>
/// <remarks>
/// **The value half of TF2's `ItemTintColor` material proxy.** The proxy itself reads the entity
/// being drawn and writes a vec3 into the material
/// (<c>CProxyItemTintColor::OnBind</c>, `econ_wearable.cpp:465-543`); this is the arithmetic behind
/// it, which is where all the rules are:
///
/// <code>
/// static CSchemaAttributeDefHandle pAttr_Paint ( "set item tint rgb" );
/// static CSchemaAttributeDefHandle pAttr_Paint2( "set item tint rgb 2" );
///
/// if ( FindAttribute…( this, pAttr_Paint, &amp;fRGB ) ) { unRGB = (uint32)fRGB; unRGBAlt = unRGB; }
///
/// if ( unRGB == kPaintConstant_OldTeamColor )       { unRGB = RGB_INT_RED; unRGBAlt = RGB_INT_BLUE; }
/// else if ( FindAttribute…( this, pAttr_Paint2, &amp;fRGBAlt ) ) { unRGBAlt = (uint32)fRGBAlt; }
/// else                                               unRGBAlt = unRGB;
///
/// return bAltColor ? m_unAltRGB : m_unRGB;
/// </code>
///
/// `econ_item_view.cpp:1596-1633`. Read-from-source. Four things in it are easy to get wrong:
///
/// - **The attribute's 32 bits are a FLOAT that is cast to an integer**, not an integer. Valve reads
///   it through `FindAttribute_UnsafeBitwiseCast&lt;attrib_value_t&gt;` into a `float` and then
///   writes `(uint32)fRGB` — a numeric conversion, not a reinterpretation. A paint of 0xE7B53B
///   travels as the float 15185723.0 and truncating it is what recovers the number.
/// - **1 is not a colour.** `kPaintConstant_OldTeamColor` is the sentinel for the old team-coloured
///   paints, and it selects two constants — `RGB_INT_RED` 12073019 and `RGB_INT_BLUE` 5801378
///   (`econ_item_constants.h:501-502`) — rather than painting an item almost-black.
/// - **The alt colour falls back to the primary**, so a singly-painted item is the same colour on
///   both teams. Only `set item tint rgb 2` or the old-team sentinel makes them differ.
/// - **No paint means ZERO, not white.** `CProxyItemTintColor` starts its result at `Vector(0,0,0)`
///   and leaves it there when the item is unpainted, which is why painted materials pair the proxy
///   with `SelectFirstIfNonZero`: the tint is used when it is non-zero and the material's own colour
///   otherwise. Returning white here would be a reasonable-looking value that breaks that pairing.
/// </remarks>
public static class ItemPaint
{
    /// <summary>Valve's <c>kPaintConstant_OldTeamColor</c>: a sentinel, not a colour.</summary>
    public const int OldTeamColour = 1;

    /// <summary><c>RGB_INT_RED</c>, <c>econ_item_constants.h:501</c>.</summary>
    public const int TeamRed = 12073019;

    /// <summary><c>RGB_INT_BLUE</c>, <c>econ_item_constants.h:502</c>.</summary>
    public const int TeamBlue = 5801378;

    /// <summary>The primary paint attribute's name, as the schema spells it.</summary>
    public const string PaintAttribute = "set item tint rgb";

    /// <summary>The secondary one, for a two-tone paint.</summary>
    public const string SecondPaintAttribute = "set item tint rgb 2";

    /// <summary>The packed colour an item is painted, or null for an unpainted one.</summary>
    /// <param name="attributes">The item's resolved attributes, by definition index.</param>
    /// <param name="items">The schema, which names the two attributes.</param>
    /// <param name="alternate">True for the BLU-team colour, as <c>bAltColor</c> is.</param>
    /// <returns>0xRRGGBB, or null when the item carries no paint.</returns>
    /// <remarks>
    /// **Null rather than zero for "unpainted"**, and the two are different questions. Valve's
    /// function returns the integer 0 there and the PROXY turns that into a zero vector; a null here
    /// says the attribute was absent, which is what a caller needs to decide whether to tint at all.
    /// Conflating them would make a hypothetical paint of pure black indistinguishable from no paint
    /// — which is Valve's own behaviour, and worth reproducing at the proxy rather than baking in
    /// here (`docs/memory/sentinels-conflate-unknown-with-answer.md`).
    /// </remarks>
    public static int? Packed(
        IReadOnlyDictionary<int, EconAttributeValue>? attributes, ItemSchema? items, bool alternate)
    {
        if (attributes is null || attributes.Count == 0 || items is null)
        {
            return null;
        }

        if (Value(attributes, items, PaintAttribute) is not { } primary)
        {
            return null;
        }

        // **The old team-coloured paints, which are a sentinel and not a colour.** Checked before
        // the second attribute, exactly as the engine orders it: an item with both this and a
        // `set item tint rgb 2` takes the constants and ignores the second.
        if (primary == OldTeamColour)
        {
            return alternate ? TeamBlue : TeamRed;
        }

        if (!alternate)
        {
            return primary;
        }

        // The alt falls back to the primary, so a singly-painted item matches on both teams.
        return Value(attributes, items, SecondPaintAttribute) ?? primary;
    }

    /// <summary>The packed colour as three channels in 0..1, or null.</summary>
    /// <param name="attributes">The item's resolved attributes, by definition index.</param>
    /// <param name="items">The schema.</param>
    /// <param name="alternate">True for the BLU-team colour.</param>
    /// <returns>The tint, or null for an unpainted item.</returns>
    /// <remarks>
    /// Valve's unpack, verbatim — <c>Color( ((iModifiedRGB &amp; 0xFF0000) &gt;&gt; 16), …)</c> then
    /// <c>clamp( clr.r() * (1.f / 255.0f), 0.f, 1.0f )</c> (`econ_wearable.cpp:528-534`). The clamp
    /// cannot fire on a byte and is kept because the engine keeps it.
    /// </remarks>
    public static (float Red, float Green, float Blue)? Tint(
        IReadOnlyDictionary<int, EconAttributeValue>? attributes, ItemSchema? items, bool alternate)
    {
        if (Packed(attributes, items, alternate) is not { } packed)
        {
            return null;
        }

        return (
            Math.Clamp(((packed & 0xFF0000) >> 16) / 255f, 0f, 1f),
            Math.Clamp(((packed & 0xFF00) >> 8) / 255f, 0f, 1f),
            Math.Clamp((packed & 0xFF) / 255f, 0f, 1f));
    }

    /// <summary>One paint attribute's value, truncated the way the engine truncates it.</summary>
    /// <remarks>
    /// **`(uint32)fRGB` is a numeric conversion of a FLOAT**, which is the trap. The attribute's 32
    /// bits hold a float whose VALUE is the packed colour — 15185723.0 for 0xE7B53B — so the number
    /// is recovered by truncating, and reinterpreting the bits instead gives a nonsense colour that
    /// still looks like a colour.
    /// </remarks>
    private static int? Value(
        IReadOnlyDictionary<int, EconAttributeValue> attributes, ItemSchema items, string name)
    {
        if (items.AttributeDefinitionIndex(name) is not { } definition ||
            !attributes.TryGetValue(definition, out EconAttributeValue attribute))
        {
            return null;
        }

        return (int)attribute.Value;
    }
}
