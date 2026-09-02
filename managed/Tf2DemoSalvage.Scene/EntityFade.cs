using System;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// How far away an entity has to be before it fades out, and by how much.
/// </summary>
/// <remarks>
/// **<c>ComputeDistanceFade</c>, <c>client/cdll_util.cpp:1074</c>**, reached from
/// <c>C_BaseAnimating::GetClientSideFade</c> (<c>c_baseanimating.cpp:6532</c>), whose result
/// <c>C_BaseEntity::ComputeFxBlend</c> multiplies into the render blend:
///
/// <code>
///   unsigned char nFadeAlpha = GetClientSideFade();
///   if ( nFadeAlpha != 255 )
///   {
///       float flBlend = blend / 255.0f;
///       float flFade = nFadeAlpha / 255.0f;
///       blend = (int)( flBlend * flFade * 255.0f + 0.5f );
///       blend = clamp( blend, 0, 255 );
///   }
/// </code>
///
/// **`FxBlend.Compute` already took this as a parameter and nobody ever supplied it** (B268). The
/// multiply was implemented, correct, and reached only with the default 255 — so every entity drew
/// at full alpha however far away it was. `docs/memory/decoding-a-field-is-not-honouring-it.md` is
/// the same shape one step along: here the CONSUMER existed and nothing fed it.
///
/// **This is used by real content, which is why it is worth having.** Measured on the 2013
/// SourceTV foundry demo: 8 entities declare an 826→900 fade band and 28 declare
/// <c>m_fadeMinDist -1</c>, the branch that derives the minimum from the maximum.
///
/// **What is absent, and the reason is MEASURED rather than assumed.**
/// <c>UTIL_ComputeEntityFade</c> (<c>cdll_util.cpp:1103</c>) takes the minimum of this and two
/// SCREEN-SIZE fades, <c>ComputeLevelScreenFade</c> and <c>ComputeViewScreenFade</c>. Both are off:
///
/// - The **view** fade's range is <c>r_screenfademinsize</c>/<c>r_screenfademaxsize</c>
///   (<c>viewrender.cpp:166</c>), both declared <c>"0"</c> — client convars a demo does not carry,
///   at a default that disables them.
/// - The **level** fade's range IS on the wire: <c>CWorld</c> networks
///   <c>m_flMinPropScreenSpaceWidth</c> and <c>m_flMaxPropScreenSpaceWidth</c>
///   (<c>world.cpp:406</c>), and <c>C_World::OnDataChanged</c> hands them to
///   <c>SetLevelScreenFadeRange</c> (<c>c_world.cpp:121</c>). **Nine corpus maps of nine that can be
///   read — 2007 to the present, five protocols — send min 0 and max −1**, a maximum below the
///   minimum, which is a disabled sentinel rather than a narrow band.
///
/// **So the first version of this note was wrong in its reasoning while right in its outcome**: it
/// called both halves unknowable, and one of them is on the wire. That is the difference between
/// "we cannot" and "we measured, and it does nothing" — the second can be re-checked when a map
/// turns up that sets it, and the first is the kind of claim nobody re-reads.
///
/// <c>m_flFadeScale</c> is unread for the same reason. It is passed to <c>UTIL_ComputeEntityFade</c>
/// as its fourth argument and reaches only those two screen fades, never
/// <c>ComputeDistanceFade</c> — so it has no consumer here, and that is the whole of it.
/// </remarks>
public static class EntityFade
{
    /// <summary>Distance between two world points.</summary>
    /// <param name="from">One point.</param>
    /// <param name="to">The other.</param>
    /// <returns>The distance.</returns>
    /// <remarks>
    /// The engine compares SQUARED distances throughout and never takes this root; the root is
    /// taken here because <see cref="DistanceAlpha"/> squares its own inputs, so handing it a
    /// squared distance would square it twice.
    /// </remarks>
    public static float Distance(
        (float X, float Y, float Z) from, (float X, float Y, float Z) to)
    {
        float x = to.X - from.X;
        float y = to.Y - from.Y;
        float z = to.Z - from.Z;

        return MathF.Sqrt((x * x) + (y * y) + (z * z));
    }

    /// <summary>How far below the maximum a negative minimum starts fading.</summary>
    /// <remarks>Valve's literal: <c>flMinDist = flMaxDist - 400</c>.</remarks>
    public const float NegativeMinimumBand = 400f;

    /// <summary>The alpha a distance fade leaves an entity at, 0 to 255.</summary>
    /// <param name="minimum">The entity's <c>m_fadeMinDist</c>.</param>
    /// <param name="maximum">The entity's <c>m_fadeMaxDist</c>.</param>
    /// <param name="distance">How far the view is from the entity's world-space centre.</param>
    /// <returns>255 when it does not fade here, 0 when it is past the maximum.</returns>
    /// <remarks>
    /// **The falloff is computed on SQUARED distances**, and that is not a shortcut for the same
    /// curve: the engine squares both bounds and the current distance, then interpolates between
    /// them, so the alpha is not linear in distance. Halfway between 826 and 900 units is 130
    /// rather than 128 — small here, and larger the wider the band.
    ///
    /// **The swap and the negative-minimum branches are the engine's**, not defensive additions. A
    /// model with its bounds the wrong way round still fades over the same band instead of
    /// producing a negative falloff, and a negative minimum means "start 400 units short of the
    /// maximum", clamped at zero when the maximum is closer than that.
    ///
    /// The view's FOV factor (<c>GetFOVDistanceAdjustFactor</c>, which scales the distance when a
    /// player is zoomed) is not applied: it is the LOCAL player's field of view, and a recording
    /// has no local player whose zoom this viewer is looking through.
    /// </remarks>
    public static byte DistanceAlpha(float minimum, float maximum, float distance)
    {
        if (minimum <= 0f && maximum <= 0f)
        {
            return 255;
        }

        if (minimum > maximum)
        {
            (minimum, maximum) = (maximum, minimum);
        }

        if (minimum < 0f)
        {
            minimum = Math.Max(maximum - NegativeMinimumBand, 0f);
        }

        float near = minimum * minimum;
        float far = maximum * maximum;
        float here = distance * distance;

        if (here <= near)
        {
            return 255;
        }

        if (here >= far)
        {
            return 0;
        }

        // "NOTE: Because of the if-checks above, flMinDist != flMinDist here" — Valve's own comment,
        // and it is what makes this division safe rather than an unguarded one.
        float falloff = 255f / (far - near);

        return (byte)Math.Clamp((int)(falloff * (far - here)), 0, 255);
    }
}
