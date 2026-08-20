using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// How far above a player's origin their eyes sit, against TF2's own table.
/// </summary>
/// <remarks>
/// **A demo carries where a player IS, not where they LOOK FROM.** The recorded view in
/// <c>democmdinfo_t</c> is the recorder's <c>GetAbsOrigin()</c> — measured, see
/// <c>docs/findings/01-container.md</c> — and the client adds the view offset when it draws:
///
/// <code>
/// Vector CBaseEntity::EyePosition( void )
/// {
///     return GetAbsOrigin() + GetViewOffset();
/// }
/// </code>
///
/// **The offset is per class, and that is the part worth a table rather than a constant.**
/// <c>tf_gamerules.cpp:1326</c>:
///
/// <code>
/// Vector g_TFClassViewVectors[11] =
/// {
///     Vector( 0, 0, 72 ),  // TF_CLASS_UNDEFINED
///     Vector( 0, 0, 65 ),  // TF_CLASS_SCOUT
///     Vector( 0, 0, 75 ),  // TF_CLASS_SNIPER
///     Vector( 0, 0, 68 ),  // TF_CLASS_SOLDIER
///     Vector( 0, 0, 68 ),  // TF_CLASS_DEMOMAN
///     Vector( 0, 0, 75 ),  // TF_CLASS_MEDIC
///     Vector( 0, 0, 75 ),  // TF_CLASS_HEAVYWEAPONS
///     Vector( 0, 0, 68 ),  // TF_CLASS_PYRO
///     Vector( 0, 0, 75 ),  // TF_CLASS_SPY
///     Vector( 0, 0, 68 ),  // TF_CLASS_ENGINEER
///     Vector( 0, 0, 65 ),  // TF_CLASS_CIVILIAN
/// };
/// </code>
///
/// Ten units separate a scout from a sniper. A camera using one number for everyone is visibly
/// wrong for most of the roster and wrong in a way that reads as "the view feels low" rather than
/// as a defect with a cause.
///
/// **Ducking and death are separate heights and come from a different structure.**
/// <c>g_TFViewVectors</c> gives <c>VEC_DUCK_VIEW</c> as 45 and <c>VEC_DEAD_VIEWHEIGHT</c> as 14 —
/// both flat across classes, unlike the standing table. A crouched sniper is not a short sniper.
/// </remarks>
public sealed class PlayerEyeConformanceTests
{
    [TestCase(0, 72f)]
    [TestCase(1, 65f)]
    [TestCase(2, 75f)]
    [TestCase(3, 68f)]
    [TestCase(4, 68f)]
    [TestCase(5, 75f)]
    [TestCase(6, 75f)]
    [TestCase(7, 68f)]
    [TestCase(8, 75f)]
    [TestCase(9, 68f)]
    [TestCase(10, 65f)]
    public void Standing_EveryClass_MatchesValvesTable(int playerClass, float expected)
    {
        // Every row, transcribed from g_TFClassViewVectors. Spot-checking two or three would leave
        // a transposed pair undetected, and the classes that share a value are exactly the ones a
        // transposition hides between.
        PlayerEye.Standing(playerClass).ShouldBe(expected);
    }

    [Test]
    public void Standing_AClassOutsideTheTable_TakesTheUndefinedHeight()
    {
        // **A demo can name a class this build has never heard of** — a later game version, or a
        // corrupt field — and the engine's own row zero is what it falls back to. Guessing a
        // middle value instead would put the camera somewhere no class ever sits.
        PlayerEye.Standing(11).ShouldBe(72f);
        PlayerEye.Standing(-1).ShouldBe(72f);
    }

    [Test]
    public void Ducking_IsFlatAcrossClasses_UnlikeStanding()
    {
        // VEC_DUCK_VIEW is a single vector on g_TFViewVectors rather than a per-class table, so a
        // crouched sniper and a crouched scout have the same eye height. Asserted across the
        // roster because "flat" is the claim, and one sample cannot make it.
        for (int playerClass = 0; playerClass <= 10; playerClass++)
        {
            PlayerEye.Ducking(playerClass).ShouldBe(45f);
        }
    }

    [Test]
    public void Spectated_IsTheFlatHeight_NotThePerClassTable()
    {
        // **Two different numbers, and which applies depends on WHOSE camera it is.**
        // C_HLTVCamera::CalcInEyeCamView (hltvcamera.cpp:314) adds VEC_VIEW — a single 72 — while
        // a player's own client adds GetClassEyeHeight() via
        // SetViewOffset( IsDucked() ? VEC_DUCK_VIEW_SCALED(this) : GetClassEyeHeight() )
        // at tf_player.cpp:16932.
        //
        // So spectating a scout sits seven units above where that scout's own view was, and a
        // sniper three below. That is the engine's behaviour, not an approximation of it.
        PlayerEye.Spectated(ducking: false).ShouldBe(72f);

        // The disagreement with the per-class table is the point, so it is asserted rather than
        // left implied.
        PlayerEye.Standing(1).ShouldBe(65f);
        PlayerEye.Standing(2).ShouldBe(75f);
    }

    [Test]
    public void Spectated_Ducking_IsTheOneHeightBothPathsShare()
    {
        // VEC_DUCK_VIEW, used by both the spectator camera and a player's own view. Pinned because
        // an edit that unified the two paths would break standing and leave this looking correct.
        PlayerEye.Spectated(ducking: true).ShouldBe(45f);
        PlayerEye.Ducking(1).ShouldBe(45f);
    }

    [Test]
    public void DeadChaseTarget_IsAChaseHeight_NotAnInEyeOne()
    {
        // VEC_DEAD_VIEWHEIGHT, 14 units. **This is not a first-person height**, which is the
        // mistake this test exists to prevent — CalcInEyeCamView does not use it, it abandons
        // first person entirely:
        //
        //     if ( !pPlayer->IsAlive() ) { CalcChaseCamView( ... ); return; }
        //
        // and the chase camera then raises its TARGET by this much, commented "look over ragdoll,
        // not through". An in-eye camera dropped to 14 would sit inside the corpse.
        PlayerEye.DeadChaseTarget.ShouldBe(14f);
    }

    [Test]
    public void Standing_IsAlwaysAboveDucking_ForEveryClass()
    {
        // **The property, not another transcription.** A table typed in with two rows swapped
        // still satisfies every equality above if I transcribed the expectations from the same
        // mistaken reading; this holds whatever the individual numbers are, and it is the thing
        // that would actually look wrong on screen.
        for (int playerClass = 0; playerClass <= 10; playerClass++)
        {
            PlayerEye.Standing(playerClass).ShouldBeGreaterThan(PlayerEye.Ducking(playerClass));
            PlayerEye.Ducking(playerClass).ShouldBeGreaterThan(PlayerEye.DeadChaseTarget);
        }
    }
}
