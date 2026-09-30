using System.Text.RegularExpressions;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// `m_bInAirWalk`, every write and every read, against <c>tf_playeranimstate.cpp</c> (B112).
/// </summary>
/// <remarks>
/// **Written down before the port, which is the order this project keeps.** The latch has twelve uses in
/// the file and they fall into three groups — the one function that SETS it, the functions that CLEAR it,
/// and the three reload cases that READ it — and this suite pins each group's text in the SDK and then
/// states, branch by branch, what the engine does with a given input.
///
/// **Where it is set** (<c>CTFPlayerAnimState::HandleJumping</c>, <c>tf_playeranimstate.cpp:1427</c>):
///
/// <code>
/// if ( heavy &amp;&amp; InCond( TF_COND_AIMING ) ) return false;                                   // :1439-1440
/// if ( bValidAirWalkClass &amp;&amp; ( vecVelocity.z &gt; 300.0f || m_bInAirWalk || grapple ) &amp;&amp; !bInDuck )  // :1446
/// {
///     if ( ( flags &amp; FL_ONGROUND ) &amp;&amp; m_bInAirWalk ) m_bInAirWalk = false;              // :1449-1451
///     else if ( GetWaterLevel() &gt;= WL_Waist )       m_bInAirWalk = false;              // :1455-1458
///     else if ( ( flags &amp; FL_ONGROUND ) == 0 )      m_bInAirWalk = true;               // :1461-1472
/// }
/// </code>
///
/// **Where it is cleared besides**: <c>ClearAnimationState</c> (<c>:114</c>), which runs on
/// <c>PLAYERANIMEVENT_SPAWN</c> and on every frame the player is not being animated at all; and the
/// double jump, which forces it off (<c>:1193</c>).
///
/// **Where it is read**: the three reload cases of <c>DoAnimationEvent</c> (<c>:1141</c>, <c>:1154</c>,
/// <c>:1167</c>), each of which plays its <c>ACT_MP_RELOAD_AIRWALK</c> form instead of deferring to the
/// base class's stand, crouch or swim choice.
///
/// **The duck is the branch that surprises.** It gates the whole block, so a latched player who ducks
/// keeps the latch — in the air and after landing — until they stand up on the ground. A reload begun
/// crouched after a rocket jump is therefore the air-walking one, which is what the engine plays.
///
/// **<c>bValidAirWalkClass</c> is the class script's, not the demo's**, and it is not applied here: a
/// class whose script sets <c>DontDoAirwalk</c> never reaches the block at all, and the scene, which has
/// the installed game, applies that half (see <c>PlayerPropsTests</c>).
/// </remarks>
public sealed class AirWalkConformanceTests
{
    private const string TfAnimState = "src/game/shared/tf/tf_playeranimstate.cpp";
    private const string BaseAnimState = "src/game/shared/Multiplayer/multiplayer_animstate.cpp";

    /// <summary>`m_fFlags` of a player in the air, standing.</summary>
    private const int InAir = 0;

    /// <summary>`FL_ONGROUND`.</summary>
    private const int OnGround = PlayerActivityState.OnGround;

    /// <summary>`FL_DUCKING`.</summary>
    private const int Ducking = PlayerActivityState.Ducking;

    /// <summary>A player's entity index for the table below.</summary>
    private const int Player = 7;

    [Test]
    public void HandleJumping_TheAirWalkBlock_IsTheShapeThePortReads()
    {
        // :1446-1472 as one ordered pattern: the condition, then the three branches in their order.
        // The order is the specification — landing is tested before the water, and both before the
        // air — so a reading that took them in another order would find a different latch.
        Text(TfAnimState).ShouldMatch(
            @"(?s)if\s*\(\s*bValidAirWalkClass\s*&&\s*\(\s*vecVelocity\.z\s*>\s*300\.0f\s*\|\|\s*m_bInAirWalk\s*\|\|\s*" +
            @"m_pTFPlayer->GetGrapplingHookTarget\(\)\s*!=\s*NULL\s*\)\s*&&\s*!bInDuck\s*\)" +
            @".{0,200}?if\s*\(\s*\(\s*GetBasePlayer\(\)->GetFlags\(\)\s*&\s*FL_ONGROUND\s*\)\s*&&\s*m_bInAirWalk\s*\)" +
            @"\s*\{\s*m_bInAirWalk\s*=\s*false;" +
            @".{0,200}?else\s+if\s*\(\s*GetBasePlayer\(\)->GetWaterLevel\(\)\s*>=\s*WL_Waist\s*\)" +
            @".{0,120}?m_bInAirWalk\s*=\s*false;" +
            @".{0,200}?else\s+if\s*\(\s*\(\s*GetBasePlayer\(\)->GetFlags\(\)\s*&\s*FL_ONGROUND\s*\)\s*==\s*0\s*\)" +
            @".{0,400}?m_bInAirWalk\s*=\s*true;");
    }

    [Test]
    public void HandleJumping_AFiringHeavy_ReturnsBeforeTheAirWalk()
    {
        // :1438-1440 — "Don't allow a firing heavy to jump or air walk." — ahead of the block above, so
        // the latch is neither set nor cleared while a heavy spins his minigun.
        Text(TfAnimState).ShouldMatch(
            @"(?s)IsClass\(\s*TF_CLASS_HEAVYWEAPONS\s*\)\s*&&\s*m_pTFPlayer->m_Shared\.InCond\(\s*TF_COND_AIMING\s*\)\s*\)" +
            @"\s*return\s+false;.{0,400}?bValidAirWalkClass\s*&&");
    }

    [TestCase("PLAYERANIMEVENT_RELOAD", "ACT_MP_RELOAD_AIRWALK")]
    [TestCase("PLAYERANIMEVENT_RELOAD_LOOP", "ACT_MP_RELOAD_AIRWALK_LOOP")]
    [TestCase("PLAYERANIMEVENT_RELOAD_END", "ACT_MP_RELOAD_AIRWALK_END")]
    public void DoAnimationEvent_EachReloadCase_PlaysItsAirWalkFormWhileTheLatchHolds(string anEvent, string activity)
    {
        // :1136-1172 — the TF override tests the latch before deferring to the base's duck and swim
        // choice (multiplayer_animstate.cpp:174-264), so the air-walking form wins over a crouch.
        Text(TfAnimState).ShouldMatch(
            @"(?s)case\s+" + Regex.Escape(anEvent) + @":\s*\{\s*(//[^\n]*\s*)?if\s*\(\s*m_bInAirWalk\s*\)\s*\{\s*" +
            @"RestartGesture\(\s*GESTURE_SLOT_ATTACK_AND_RELOAD\s*,\s*" + Regex.Escape(activity) + @"\s*\);\s*\}\s*" +
            @"else\s*\{\s*BaseClass::DoAnimationEvent\(\s*event\s*,\s*nData\s*\);");
    }

    [Test]
    public void DoAnimationEvent_TheDoubleJump_StartsTheJumpAndForcesTheAirWalkOff()
    {
        // :1177-1203 — a double jump sets m_bJumping only when it is not already set, then clears the
        // latch unconditionally, then picks the loser's gesture or the ordinary one.
        Text(TfAnimState).ShouldMatch(
            @"(?s)case\s+PLAYERANIMEVENT_DOUBLEJUMP:.{0,200}?if\s*\(\s*!m_bJumping\s*\)\s*\{\s*m_bJumping\s*=\s*true;" +
            @"\s*m_bFirstJumpFrame\s*=\s*true;\s*m_flJumpStartTime\s*=\s*gpGlobals->curtime;" +
            @".{0,200}?//\s*Force the air walk off\.\s*m_bInAirWalk\s*=\s*false;" +
            @".{0,200}?if\s*\(\s*pPlayer->m_Shared\.IsLoser\(\)\s*\)\s*\{\s*RestartGesture\(\s*GESTURE_SLOT_JUMP\s*,\s*ACT_MP_DOUBLEJUMP_LOSERSTATE\s*\);");
    }

    [Test]
    public void ClearAnimationState_TheTfOverride_ClearsTheLatchAndThenTheBase()
    {
        // :112-117, and the base it chains to (multiplayer_animstate.cpp:136-146): the jump and every
        // gesture slot go with it.
        Text(TfAnimState).ShouldMatch(
            @"void\s+CTFPlayerAnimState::ClearAnimationState\(\s*void\s*\)\s*\{\s*m_bInAirWalk\s*=\s*false;\s*" +
            @"BaseClass::ClearAnimationState\(\);");

        Text(BaseAnimState).ShouldMatch(
            @"(?s)void\s+CMultiPlayerAnimState::ClearAnimationState\(\)\s*\{.{0,60}?m_bJumping\s*=\s*false;" +
            @".{0,300}?ResetGestureSlots\(\);\s*\}");
    }

    [Test]
    public void Update_APlayerNotAnimated_ClearsTheAnimationState()
    {
        // tf_playeranimstate.cpp:340-374 — a custom model without the class animations, or
        // ShouldUpdateAnimState false, clears and returns; the base's ShouldUpdateAnimState
        // (multiplayer_animstate.cpp:1381-1395) is false for EF_NODRAW, a dormant player and the dead.
        // And a respawn clears it through the event (:310-313).
        Text(TfAnimState).ShouldMatch(
            @"(?s)if\s*\(\s*!pTFPlayer->GetPlayerClass\(\)->CustomModelUsesClassAnimations\(\)\s*\).{0,700}?" +
            @"ClearAnimationState\(\);\s*return;\s*\}\s*\}.{0,200}?if\s*\(\s*!ShouldUpdateAnimState\(\)\s*\)\s*\{\s*" +
            @"ClearAnimationState\(\);\s*return;");

        Text(BaseAnimState).ShouldMatch(
            @"(?s)bool\s+CMultiPlayerAnimState::ShouldUpdateAnimState\(\)\s*\{.{0,120}?IsEffectActive\(\s*EF_NODRAW\s*\)\s*\)\s*" +
            @"return\s+false;.{0,300}?IsDormant\(\)\s*\)\s*return\s+false;.{0,60}?return\s*\(\s*GetBasePlayer\(\)->IsAlive\(\)\s*\|\|\s*m_bDying\s*\);");

        Text(BaseAnimState).ShouldMatch(
            @"(?s)case\s+PLAYERANIMEVENT_SPAWN:\s*\{\s*//[^\n]*\s*ClearAnimationState\(\);");
    }

    // **The engine's branches, one row each.** The expected latch is read off the lines named, by hand,
    // for the input the row gives — never from running the port. A row whose latch starts SET is set by
    // one rising step first, which is itself the first row.
    [TestCase(false, 400f, InAir, false, false, false, true, TestName = "AirWalk_RisingPast300InTheAir_Latches", Description = ":1446, :1472")]
    [TestCase(false, 268f, InAir, false, false, false, false, TestName = "AirWalk_AnOrdinaryJumpsRise_DoesNotLatch", Description = ":1446")]
    [TestCase(false, 300f, InAir, false, false, false, false, TestName = "AirWalk_Exactly300_DoesNotLatch", Description = ":1446 is strict")]
    [TestCase(true, 50f, InAir, false, false, false, true, TestName = "AirWalk_LatchedAndSlowing_Holds", Description = ":1446 m_bInAirWalk")]
    [TestCase(true, -400f, InAir, false, false, false, true, TestName = "AirWalk_LatchedAndFalling_Holds", Description = ":1446 m_bInAirWalk")]
    [TestCase(true, 0f, OnGround, false, false, false, false, TestName = "AirWalk_LatchedAndLanded_Clears", Description = ":1449-1451")]
    [TestCase(false, 400f, OnGround, false, false, false, false, TestName = "AirWalk_RisingOnTheGround_DoesNotLatch", Description = ":1449, :1455 and :1461 all false")]
    [TestCase(false, 400f, InAir | Ducking, false, false, false, false, TestName = "AirWalk_RisingWhileDucked_DoesNotLatch", Description = ":1446 !bInDuck")]
    [TestCase(true, 50f, InAir | Ducking, false, false, false, true, TestName = "AirWalk_LatchedThenDuckedInTheAir_Holds", Description = ":1446 !bInDuck")]
    [TestCase(true, 0f, OnGround | Ducking, false, false, false, true, TestName = "AirWalk_LatchedAndLandedDucked_Holds", Description = ":1446 !bInDuck")]
    [TestCase(true, 50f, InAir, true, false, false, false, TestName = "AirWalk_LatchedAndWaistDeep_Clears", Description = ":1455-1458")]
    [TestCase(false, 400f, InAir, true, false, false, false, TestName = "AirWalk_RisingIntoWaistDeepWater_DoesNotLatch", Description = ":1455 before :1461")]
    [TestCase(false, 0f, InAir, false, true, false, true, TestName = "AirWalk_GrappledInTheAir_Latches", Description = ":1446 GetGrapplingHookTarget")]
    [TestCase(false, 0f, OnGround, false, true, false, false, TestName = "AirWalk_GrappledOnTheGround_DoesNotLatch", Description = ":1461")]
    [TestCase(true, 0f, OnGround, false, false, true, true, TestName = "AirWalk_AFiringHeavyLanded_Holds", Description = ":1439-1440")]
    [TestCase(false, 400f, InAir, false, false, true, false, TestName = "AirWalk_AFiringHeavyRising_DoesNotLatch", Description = ":1439-1440")]
    public void AirWalk_EachOfTheEnginesBranches_LeavesTheLatchAsTheEngineDoes(
        bool latchedBefore, float risingSpeed, int flags, bool waistDeep, bool grappling, bool firingHeavy, bool expected)
    {
        PlayerGestureFeed feed = new();

        if (latchedBefore)
        {
            feed.AirWalk(Player, 400f, InAir, waistDeep: false, grappling: false, firingHeavy: false)
                .ShouldBeTrue("the precondition: one rising step in the air sets the latch");
        }

        feed.AirWalk(Player, risingSpeed, flags, waistDeep, grappling, firingHeavy).ShouldBe(expected);
        feed.InAirWalk(Player).ShouldBe(expected, "the step's answer is the latch the feed now holds");
    }

    private static string Text(string path)
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }

        return SourceSdk.Text(path).ShouldNotBeNull(path);
    }
}
