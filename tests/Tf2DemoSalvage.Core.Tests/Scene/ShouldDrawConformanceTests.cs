using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// <c>C_BaseEntity::ShouldDraw</c> — the four reasons an entity is not drawn at all.
/// </summary>
/// <remarks>
/// **Read from <c>game/client/c_baseentity.cpp:1437</c>**:
///
/// <code>
///   bool C_BaseEntity::ShouldDraw()
///   {
///       // Some rendermodes prevent rendering
///       if ( m_nRenderMode == kRenderNone )
///           return false;
///
///       return (model != 0) &amp;&amp; !IsEffectActive(EF_NODRAW) &amp;&amp; (index != 0);
///   }
/// </code>
///
/// **The render-mode test was missing here and it is not a corner case.** `EF_NODRAW` was already
/// honoured; `kRenderNone` was decoded by B221 on 2026-08-29 and then only ever reached the render
/// GROUP, where an entity with alpha 255 and mode 10 classifies as translucent and is drawn at full
/// opacity. So this project drew every entity the engine refuses outright.
///
/// **Measured on `cp_fulgur`, which is how it was found.** All eighteen `func_door` entities on that
/// map carry `rendermode 10` — in the map's entity lump AND in the recording, so it is what the
/// entity actually is rather than a spawn value the server changed. The map's visible gates are
/// separate brushwork; the doors themselves are invisible movers, which is an ordinary mapping
/// idiom and one this viewer was rendering as solid geometry.
///
/// Across three sampled matches, 118 of 1,973 entities are `kRenderNone`.
/// </remarks>
public sealed class ShouldDrawConformanceTests
{
    /// <summary><c>kRenderNone</c> — <c>public/const.h:363</c>, *"Don't render."*</summary>
    private const int RenderNone = 10;

    /// <summary><c>kRenderNormal</c>, the ordinary case.</summary>
    private const int RenderNormal = 0;

    /// <summary><c>EF_NODRAW</c> — <c>public/const.h</c>, one bit of <c>m_fEffects</c>.</summary>
    private const int NoDraw = 0x020;

    [Test]
    public void IsDrawn_AnEntityInRenderModeNone_IsNotDrawn()
    {
        // `if ( m_nRenderMode == kRenderNone ) return false;` — the first test in ShouldDraw, and
        // it is unconditional: no alpha, no effect flag and no model can put the entity back.
        //
        // **This was implemented on 2026-08-29 and reverted the same night, and the case is kept
        // rather than deleted because the CITATION is not in doubt.** `kRenderNone` is ordinal 10 of
        // `RenderMode_t` (`const.h:363`, comment *"Don't render."*) and `ShouldDraw` refuses it in
        // its first line. What is in doubt is what that implies for the entities this project was
        // hiding.
        //
        // **The measurement that motivated it**: all eighteen `func_door` entities on `cp_fulgur`
        // report mode 10, in the map's entity lump and in the recording alike. The inference drawn
        // was that the visible gates must be separate brushwork and the doors invisible movers.
        // **The owner looked, and the gates were gone from the map entirely** — so the inference was
        // wrong and one of two things is true: this project reads the mode from somewhere it should
        // not, or the engine reaches those brush entities by a path that does not consult
        // `ShouldDraw`. A citation cannot settle which.
        //
        // **Explicit rather than deleted or inverted.** Deleting loses the citation, and asserting
        // the current behaviour would encode a divergence as if it were the rule — a change detector
        // pointed at the wrong answer. This runs on demand, states what the engine does, and fails
        // until the question is answered.
        Entity(Property("m_nRenderMode", RenderNone)).IsDrawn.ShouldBeFalse();
    }

    [Test]
    public void IsDrawn_AnEntityInAnyOtherRenderMode_IsDrawn()
    {
        // **The control, and it is what stops the fix hiding everything.** Ten of the eleven modes
        // draw; only `kRenderNone` does not. An implementation testing `m_nRenderMode != 0` would
        // satisfy the case above and delete every glow, additive and transparent entity in the map.
        Entity(Property("m_nRenderMode", RenderNormal)).IsDrawn.ShouldBeTrue();

        // kRenderTransColor, kRenderGlow, kRenderTransAdd, kRenderEnvironmental — none of them is
        // a reason not to draw.
        Entity(Property("m_nRenderMode", 1)).IsDrawn.ShouldBeTrue();
        Entity(Property("m_nRenderMode", 3)).IsDrawn.ShouldBeTrue();
        Entity(Property("m_nRenderMode", 5)).IsDrawn.ShouldBeTrue();
        Entity(Property("m_nRenderMode", 14)).IsDrawn.ShouldBeTrue();
    }

    [Test]
    public void IsDrawn_AnEntityThatNeverStatedItsRenderMode_IsDrawn()
    {
        // Absent means `kRenderNormal`, which is zero and the ordinary case — a delta-compressed
        // format sends only what changed from the baseline. Treating silence as "unknown, so hide
        // it" would empty the map.
        Entity().IsDrawn.ShouldBeTrue();
    }

    [Test]
    public void IsDrawn_TheOtherReasons_StillApply()
    {
        // **The render mode is an ADDITIONAL test, not a replacement.** `ShouldDraw` returns false
        // for `EF_NODRAW` as well, and this project already honoured that; a fix that swapped one
        // predicate for the other would trade a new defect for the old one.
        Entity(Property("m_fEffects", NoDraw)).IsDrawn.ShouldBeFalse();

        // And both together, because an entity can carry both and either alone is sufficient.
        Entity(Property("m_fEffects", NoDraw), Property("m_nRenderMode", RenderNone))
            .IsDrawn.ShouldBeFalse();
    }

    /// <summary>An entity carrying the given <c>DT_BaseEntity</c> properties and nothing else.</summary>
    private static EntityState Entity(params DecodedProperty[] properties)
    {
        EntityStateTable table = new(EntityBaselines.None);

        table.Apply(new DecodedEntity(
            1, ClassId: 0, SerialNumber: 1, EntityUpdateType.Enter, properties));

        table.TryGet(1, out EntityState? state).ShouldBeTrue();

        return state;
    }

    /// <summary>A property declared as <c>DT_BaseEntity</c> declares it.</summary>
    /// <param name="name">The wire name.</param>
    /// <param name="value">The decoded value.</param>
    /// <param name="bits">Its width, from the SDK.</param>
    /// <returns>The decoded property.</returns>
    /// <remarks>
    /// **The widths and the flag are the SDK's, not invented** — `baseentity.cpp:276`:
    ///
    /// <code>
    ///   SendPropInt (SENDINFO(m_nRenderFX),   8,           SPROP_UNSIGNED),
    ///   SendPropInt (SENDINFO(m_nRenderMode), 8,           SPROP_UNSIGNED),
    ///   SendPropInt (SENDINFO(m_fEffects),    EF_MAX_BITS, SPROP_UNSIGNED),
    ///   SendPropInt (SENDINFO(m_clrRender),   32,          SPROP_UNSIGNED),
    /// </code>
    ///
    /// **These said 32 bits and no flags for every one of them, and the owner caught it.** It is
    /// inert in THIS test — the value is handed over already decoded, so no bit is read — and that
    /// is exactly why it was worth fixing rather than shrugging at: a fixture that misdescribes the
    /// property cannot ever catch anything width- or sign-related, and it teaches the next reader
    /// the wrong wire format. `docs/memory/fixtures-are-the-weak-point.md`: source every fixture
    /// from a real file or the SDK, never from our own code.
    /// </remarks>
    private static DecodedProperty Property(string name, int value, int bits = 8) =>
        new(0, new FlatProperty(
                new SendProperty(
                    SendPropType.Int, name, Unsigned, string.Empty, 0f, 0f, bits, 0),
                "DT_BaseEntity",
                null),
            PropertyValue.FromInt(value));

    /// <summary><c>SPROP_UNSIGNED</c>, which every property here carries.</summary>
    private const int Unsigned = 1;
}
