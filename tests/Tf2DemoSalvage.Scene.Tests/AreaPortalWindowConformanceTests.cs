namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// How near you have to be before an areaportal window stops hiding the room beyond (B358).
/// </summary>
/// <remarks>
/// **This is the black rectangle in a spawn window.** A <c>func_areaportalwindow</c> steals the
/// brush model of the entity its <c>target</c> names and draws it itself
/// (<c>func_areaportalwindow.cpp:81</c>, server, in <c>Activate</c>):
///
/// <code>
///   CBaseEntity *pTarget = gEntList.FindEntityByName( NULL, m_target );
///   if( pTarget )
///   {
///       SetModel( STRING(pTarget->GetModelName()) );
///       SetAbsOrigin( pTarget->GetAbsOrigin() );
///       pTarget->AddEffects( EF_NODRAW ); // we will draw for it.
///   }
/// </code>
///
/// so the brush arrives in a demo already <c>EF_NODRAW</c> — correctly absent from our draw list —
/// and the WINDOW carries its model. The window then draws it at a distance blend rather than at
/// its own render amount (<c>c_func_areaportalwindow.cpp:79</c>):
///
/// <code>
///   render->SetBlend( GetDistanceBlend() );
///   render->DrawBrushModelEx( this, (model_t *)GetModel(), ... );
/// </code>
///
/// **On `koth_harvest_final` those brushes are six faces of `TOOLS/TOOLSBLACK`** — measured — so
/// drawing one opaque puts a solid black panel exactly in the window opening, which is what a
/// misconfigured areaportal looks like in game. Both BLU spawn windows and both RED ones are built
/// this way, and so are four more at the yard windows.
///
/// **The blend, verbatim** (<c>c_func_areaportalwindow.cpp:129</c>):
///
/// <code>
///   float flDist = CollisionProp()->CalcDistanceFromPoint( CurrentViewOrigin() );
///   flDist *= local->GetFOVDistanceAdjustFactor();
///   return RemapValClamped( flDist, m_flFadeStartDist, m_flFadeDist, m_flTranslucencyLimit, 1 );
/// </code>
/// </remarks>
public sealed class AreaPortalWindowConformanceTests
{
    /// <remarks>
    /// **Harvest's own numbers, and the case that produces the defect.** Its spawn windows carry
    /// <c>FadeStartDist 1200</c>, <c>FadeDist 1500</c> and <c>TranslucencyLimit 0.0</c>, and a
    /// player standing in spawn is a few hundred units from the glass — so the engine draws the
    /// black brush at alpha ZERO and the window is a hole you can see through.
    /// </remarks>
    [Test]
    public void Blend_CloserThanTheFadeStart_IsTheTranslucencyLimit()
    {
        AreaPortalWindow.Blend(distance: 300f, fadeStart: 1200f, fadeEnd: 1500f, limit: 0f)
            .ShouldBe(0f);
    }

    /// <remarks>
    /// The other end, and the half that gives the mechanism its purpose: past the fade distance the
    /// panel is fully solid, which is what lets the areaportal cull the room behind it.
    /// </remarks>
    [Test]
    public void Blend_BeyondTheFadeDistance_IsFullySolid()
    {
        AreaPortalWindow.Blend(distance: 4000f, fadeStart: 1200f, fadeEnd: 1500f, limit: 0f)
            .ShouldBe(1f);
    }

    /// <remarks>
    /// **Sampled between the knots**, because every interpolation agrees at its own endpoints
    /// (`docs/memory/sample-between-the-knots.md`). Half way from 1200 to 1500 is 1350, and
    /// <c>RemapValClamped</c> is linear in the clamped fraction: <c>0 + (1 - 0) * 0.5</c>.
    /// </remarks>
    [Test]
    public void Blend_HalfWayThroughTheFade_IsHalfSolid()
    {
        AreaPortalWindow.Blend(distance: 1350f, fadeStart: 1200f, fadeEnd: 1500f, limit: 0f)
            .ShouldBe(0.5f, 0.0001d);
    }

    /// <remarks>
    /// **A limit above zero is a window that never fully clears**, and it is what distinguishes
    /// this from an ordinary distance fade. The remap's floor is the limit, not zero, so at close
    /// range the panel keeps that much opacity — a smoked-glass window rather than an open hole.
    /// </remarks>
    [Test]
    public void Blend_WithATranslucencyLimit_NeverClearsBelowIt()
    {
        AreaPortalWindow.Blend(distance: 0f, fadeStart: 1200f, fadeEnd: 1500f, limit: 0.25f)
            .ShouldBe(0.25f);
    }

    /// <remarks>
    /// **`if ( A == B ) return val >= B ? D : C;`** — `RemapValClamped`'s own first line
    /// (<c>mathlib.h:621</c>). Equal distances would divide by zero, and Valve's answer is a step
    /// rather than a NaN. A map that sets both to the same value is not hypothetical: it is what
    /// a mapper writes for a window meant to switch rather than fade.
    /// </remarks>
    [Test]
    public void Blend_WhenTheTwoDistancesAreEqual_StepsRatherThanDividingByZero()
    {
        AreaPortalWindow.Blend(distance: 1200f, fadeStart: 1200f, fadeEnd: 1200f, limit: 0f)
            .ShouldBe(1f, "val >= B takes D");

        AreaPortalWindow.Blend(distance: 1199f, fadeStart: 1200f, fadeEnd: 1200f, limit: 0f)
            .ShouldBe(0f, "below B takes C, which is the limit");
    }

    /// <remarks>
    /// **The distance is to the nearest point of the box, not to its centre** —
    /// <c>CCollisionProperty::CalcDistanceFromPoint</c> (<c>collisionproperty.cpp:949</c>) runs
    /// <c>CalcClosestPointOnAABB</c> first. For a window brush two units thick and ninety-six
    /// across, the difference between the two readings is the whole width of the glass, and using
    /// the centre would fade a window the player is standing against.
    /// </remarks>
    [Test]
    public void Distance_ToAPointBesideTheBox_IsMeasuredFromTheNearestFace()
    {
        AreaPortalWindow.Distance(
            (200f, 2064f, 87f),
            minimum: (198f, 1916f, 0f),
            maximum: (202f, 2012f, 128f)).ShouldBe(52f, 0.0001d);
    }

    /// <remarks>
    /// Inside the box the closest point is the point itself, so the distance is zero rather than
    /// negative — which matters because the remap clamps and a negative would read as "very close"
    /// by accident rather than by rule.
    /// </remarks>
    [Test]
    public void Distance_ToAPointInsideTheBox_IsZero()
    {
        AreaPortalWindow.Distance(
            (200f, 1964f, 64f),
            minimum: (198f, 1916f, 0f),
            maximum: (202f, 2012f, 128f)).ShouldBe(0f);
    }

    /// <remarks>
    /// **The FOV factor, which is `localFOV / defaultFOV`** (`baseplayer_shared.cpp:1786`) and
    /// exactly 1 when they match. A zoomed sniper sees a smaller FOV, so distances shorten and a
    /// window clears sooner — the engine scales the distance rather than the blend.
    /// </remarks>
    [Test]
    public void Blend_WhenTheViewIsZoomed_UsesTheShortenedDistance()
    {
        // 2000 units at a quarter of the default field of view reads as 500, which is inside the
        // fade start and therefore clear.
        AreaPortalWindow.Blend(
            distance: 2000f * 0.25f, fadeStart: 1200f, fadeEnd: 1500f, limit: 0f).ShouldBe(0f);

        // The control: unzoomed, the same 2000 units is past the fade distance and solid.
        AreaPortalWindow.Blend(
            distance: 2000f, fadeStart: 1200f, fadeEnd: 1500f, limit: 0f).ShouldBe(1f);
    }
}
