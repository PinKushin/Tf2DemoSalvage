using System;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// How solid an areaportal window is from where you are standing (B358).
/// </summary>
/// <remarks>
/// **This is the black rectangle in a spawn window.** A <c>func_areaportalwindow</c> takes the
/// brush model of whatever its <c>target</c> names, marks that entity <c>EF_NODRAW</c> — *"we will
/// draw for it"* — and draws the brush itself at a distance blend
/// (<c>func_areaportalwindow.cpp:81</c>, server):
///
/// <code>
///   SetModel( STRING(pTarget->GetModelName()) );
///   SetAbsOrigin( pTarget->GetAbsOrigin() );
///   pTarget->AddEffects( EF_NODRAW ); // we will draw for it.
/// </code>
///
/// That happens on the SERVER, so a demo carries the target already hidden and the window already
/// holding its model. Nothing was missing from the decode — what was missing was the blend
/// (<c>c_func_areaportalwindow.cpp:79</c>):
///
/// <code>
///   render->SetBlend( GetDistanceBlend() );
///   render->DrawBrushModelEx( this, (model_t *)GetModel(), ... );
/// </code>
///
/// **The brushes are `TOOLS/TOOLSBLACK`** — measured on `koth_harvest_final`, six faces each on all
/// six of its windows — so a window drawn at its own render amount is a solid black panel filling
/// the opening. That is what a broken areaportal looks like in game, which is why the symptom reads
/// as a map fault rather than a viewer one.
///
/// **The panel is not scenery; it is the mechanism.** Far away it is opaque so the areaportal can
/// cull the room behind it, and close up it clears so you can see through. Drawing it always-opaque
/// keeps the half that hides and loses the half that reveals.
///
/// **Not routed through <see cref="FxBlend"/>, and that is Valve's own arrangement rather than a
/// shortcut.** <c>C_FuncAreaPortalWindow::ComputeFxBlend</c> sets <c>m_nRenderFXBlend = 255</c> with
/// the comment *"We reset our blend down below"*, and <c>IsTransparent</c> returns true
/// unconditionally — so the entity's <c>renderamt</c>, <c>rendermode</c> and <c>renderfx</c> decide
/// nothing here. Harvest's own brushes say <c>rendermode 0</c> and <c>renderamt 255</c>, which would
/// otherwise mean fully opaque.
/// </remarks>
public static class AreaPortalWindow
{
    /// <summary>How solid the window is, from 0 (invisible) to 1 (fully opaque).</summary>
    /// <param name="distance">Eye to the nearest point of the brush, already FOV-scaled.</param>
    /// <param name="fadeStart">The entity's <c>FadeStartDist</c>; at or inside it, the limit.</param>
    /// <param name="fadeEnd">Its <c>FadeDist</c>; at or beyond it, fully solid.</param>
    /// <param name="limit">Its <c>TranslucencyLimit</c> — the most transparent it may become.</param>
    /// <returns>A blend in 0..1.</returns>
    /// <remarks>
    /// <c>RemapValClamped( flDist, m_flFadeStartDist, m_flFadeDist, m_flTranslucencyLimit, 1 )</c>,
    /// and the remap itself is <c>mathlib.h:619</c>:
    ///
    /// <code>
    ///   if ( A == B )
    ///       return val >= B ? D : C;
    ///   float cVal = (val - A) / (B - A);
    ///   cVal = clamp( cVal, 0.0f, 1.0f );
    ///   return C + (D - C) * cVal;
    /// </code>
    ///
    /// **The equal-distances branch is carried rather than treated as impossible.** It is a
    /// division by zero otherwise, and a map that wants a window to switch rather than fade writes
    /// exactly that pair.
    /// </remarks>
    public static float Blend(float distance, float fadeStart, float fadeEnd, float limit)
    {
        // **Valve's own exact comparison, and a tolerance here would be wrong rather than safer.**
        // `if ( A == B )` guards a division by `B - A`, so the only value that must take this
        // branch is the one that would divide by zero; a window whose two distances differ by a
        // thousandth is a fade, and treating it as a step would draw a hard edge where the map
        // asked for a gradient.
#pragma warning disable S1244 // Valve's own exact comparison; see the remarks above.
        if (fadeStart == fadeEnd)
#pragma warning restore S1244
        {
            return distance >= fadeEnd ? 1f : limit;
        }

        float fraction = Math.Clamp((distance - fadeStart) / (fadeEnd - fadeStart), 0f, 1f);

        return limit + ((1f - limit) * fraction);
    }

    /// <summary>Eye to the nearest point of a brush's box, which is what the engine measures.</summary>
    /// <param name="eye">The view origin.</param>
    /// <param name="minimum">The brush model's world-space minimum.</param>
    /// <param name="maximum">Its world-space maximum.</param>
    /// <returns>Zero inside the box, otherwise the distance to its surface.</returns>
    /// <remarks>
    /// <c>CCollisionProperty::CalcDistanceFromPoint</c> (<c>collisionproperty.cpp:949</c>) closes
    /// the point onto the AABB before measuring:
    ///
    /// <code>
    ///   CalcClosestPointOnAABB( m_vecMins, m_vecMaxs, localPt, localClosestPt );
    ///   return localPt.DistTo( localClosestPt );
    /// </code>
    ///
    /// **To the nearest FACE, not to the centre**, and the difference is the whole window: these
    /// brushes are a couple of units thick and eight feet across, so a centre measurement fades
    /// glass the player is standing against.
    ///
    /// **The engine works in collision space**, which for a brush entity with no rotation is world
    /// space. Every areaportal window on the shipped maps read here is axis-aligned; a rotated one
    /// would need the entity's transform, and that is stated rather than silently assumed.
    /// </remarks>
    public static float Distance(
        (float X, float Y, float Z) eye,
        (float X, float Y, float Z) minimum,
        (float X, float Y, float Z) maximum)
    {
        float x = eye.X - Math.Clamp(eye.X, minimum.X, maximum.X);
        float y = eye.Y - Math.Clamp(eye.Y, minimum.Y, maximum.Y);
        float z = eye.Z - Math.Clamp(eye.Z, minimum.Z, maximum.Z);

        return MathF.Sqrt((x * x) + (y * y) + (z * z));
    }
}
