using System;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>A left-handed viewmodel — <c>cl_flipviewmodels</c> — as the engine decides and draws it (B515).</summary>
/// <remarks>
/// **Whose flag, read from published source.** `C_BaseViewModel::ShouldFlipViewModel`
/// (`c_baseviewmodel.cpp:215-236`), TF branch:
///
/// <code>
/// CBaseCombatWeapon *pWeapon = m_hWeapon.Get();
/// if ( pWeapon )
///     return pWeapon->m_bFlipViewModel != TeamFortress_ShouldFlipClientViewModel();
/// return false;
/// </code>
///
/// and `TeamFortress_ShouldFlipClientViewModel` (`:96-109`):
///
/// <code>
/// if ( IsLocalPlayerSpectator() )
/// {
///     // Use spectated client's handedness preference
///     C_TFPlayer *pSpecTarget = ToTFPlayer( UTIL_PlayerByIndex( GetSpectatorTarget() ) );
///     if ( pSpecTarget )
///         return pSpecTarget->m_bFlipViewModels;
/// }
/// return cl_flipviewmodels.GetBool();
/// </code>
///
/// So it is an XOR of two things: the WEAPON's own flag (the item schema's `flip_viewmodel`, read
/// at `econ_item_schema.cpp:3169` — the Huntsman is the one shipped item that sets it, because its
/// model is built left-handed) and the CLIENT's preference, which is the spectated player's
/// networked `m_bFlipViewModels` when the local player is an observer — always, on SourceTV, where
/// `GetObserverTarget` is the HLTV camera's target (`c_baseplayer.cpp:544`) — and the WATCHER's
/// own `cl_flipviewmodels` otherwise.
///
/// **How it is drawn.** `ApplyBoneMatrixTransform` (`c_baseviewmodel.cpp:239-272`) takes the bone
/// into the player's VIEW space, negates row 1 — view-space Y, which is left — and takes it back
/// out: a reflection through the plane the view's forward and up span. `BuildTransformations` calls
/// it on ROOT bones only (`c_baseanimating.cpp:1599-1603`), so every child inherits it through its
/// parent and the bone-merged weapon inherits it through the merge. A reflection reverses winding,
/// so `InternalDrawModel` culls clockwise for the duration (`c_baseviewmodel.cpp:371-382`), and the
/// attached `c_` weapon does the same against its own item's flag (`econ_entity.cpp:856-872`).
/// </remarks>
public sealed class ViewmodelFlipConformanceTests
{
    [Test]
    public void ShouldFlip_ARightHandedWeaponAndAPlayerWhoFlips_Flips()
    {
        // gummo on f12, spectated in-eye on SourceTV: an ordinary rocket launcher, `cl_flipviewmodels 1`.
        ViewmodelFlip.ShouldFlip(weaponFlips: false, spectatedFlips: true, watcherFlips: false).ShouldBeTrue();
    }

    [Test]
    public void ShouldFlip_ARightHandedWeaponAndAPlayerWhoDoesNot_DoesNotFlip()
    {
        ViewmodelFlip.ShouldFlip(weaponFlips: false, spectatedFlips: false, watcherFlips: true).ShouldBeFalse(
            "the spectated player's preference replaces the watcher's, it does not combine with it");
    }

    [Test]
    public void ShouldFlip_ALeftHandedWeaponAndAPlayerWhoFlips_DoesNotFlip()
    {
        // The Huntsman for a left-handed player: two mirrors, which is the model as built.
        ViewmodelFlip.ShouldFlip(weaponFlips: true, spectatedFlips: true, watcherFlips: false).ShouldBeFalse();
    }

    [Test]
    public void ShouldFlip_ALeftHandedWeaponAndAPlayerWhoDoesNot_Flips()
    {
        ViewmodelFlip.ShouldFlip(weaponFlips: true, spectatedFlips: false, watcherFlips: false).ShouldBeTrue();
    }

    [Test]
    public void ShouldFlip_NoSpectatedPlayer_TakesTheWatchersOwnSetting()
    {
        // A point-of-view recording of someone not spectating: `cl_flipviewmodels.GetBool()`, the
        // watching client's own cvar, and never the recording's.
        ViewmodelFlip.ShouldFlip(weaponFlips: false, spectatedFlips: null, watcherFlips: true).ShouldBeTrue();
        ViewmodelFlip.ShouldFlip(weaponFlips: false, spectatedFlips: null, watcherFlips: false).ShouldBeFalse();
    }

    [Test]
    public void Reflection_APointToTheRight_LandsTheSameDistanceToTheLeft()
    {
        // Eye at (100, 200, 50) looking down +X, so right is -Y. A point 5 right and 10 ahead lands
        // 5 LEFT and still 10 ahead, at the same height.
        float[] mirror = ViewmodelFlip.Reflection((100f, 200f, 50f), (0f, -1f, 0f));

        (float X, float Y, float Z) point = Apply(mirror, (110f, 195f, 53f));

        point.X.ShouldBe(110f, 1e-4f);
        point.Y.ShouldBe(205f, 1e-4f);
        point.Z.ShouldBe(53f, 1e-4f);
    }

    [Test]
    public void Reflection_AnyView_IsItsOwnInverseAndReversesHandedness()
    {
        // A yawed, pitched view, so the test cannot pass on an axis-aligned special case. Applying
        // the mirror twice is the identity; its determinant is -1, which is WHY the winding flips and
        // the cull has to follow it (`c_baseviewmodel.cpp:374`).
        (float X, float Y, float Z) right = (MathF.Sin(0.7f), -MathF.Cos(0.7f), 0f);
        float[] mirror = ViewmodelFlip.Reflection((-1300f, 450f, 260f), right);

        (float X, float Y, float Z) start = (-1250f, 470f, 230f);
        (float X, float Y, float Z) back = Apply(mirror, Apply(mirror, start));

        back.X.ShouldBe(start.X, 1e-2f);
        back.Y.ShouldBe(start.Y, 1e-2f);
        back.Z.ShouldBe(start.Z, 1e-2f);

        float determinant =
            (mirror[0] * ((mirror[5] * mirror[10]) - (mirror[6] * mirror[9]))) -
            (mirror[1] * ((mirror[4] * mirror[10]) - (mirror[6] * mirror[8]))) +
            (mirror[2] * ((mirror[4] * mirror[9]) - (mirror[5] * mirror[8])));

        determinant.ShouldBe(-1f, 1e-5f);
    }

    [Test]
    public void Reflection_ThePointOnTheViewAxis_DoesNotMove()
    {
        // The plane of the mirror passes through the eye and contains forward and up, so anything
        // dead ahead or straight above stays put — the gun crosses the crosshair, not the eye.
        float[] mirror = ViewmodelFlip.Reflection((100f, 200f, 50f), (0f, -1f, 0f));

        (float X, float Y, float Z) ahead = Apply(mirror, (140f, 200f, 70f));

        ahead.X.ShouldBe(140f, 1e-4f);
        ahead.Y.ShouldBe(200f, 1e-4f);
        ahead.Z.ShouldBe(70f, 1e-4f);
    }

    /// <summary>A row-major 3x4 (matrix3x4_t) applied to a point.</summary>
    private static (float X, float Y, float Z) Apply(float[] m, (float X, float Y, float Z) p) => (
        (m[0] * p.X) + (m[1] * p.Y) + (m[2] * p.Z) + m[3],
        (m[4] * p.X) + (m[5] * p.Y) + (m[6] * p.Z) + m[7],
        (m[8] * p.X) + (m[9] * p.Y) + (m[10] * p.Z) + m[11]);
}
