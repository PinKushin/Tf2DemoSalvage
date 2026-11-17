using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>Where each overhead icon stands, and since when (B765).</summary>
/// <remarks>
/// `C_TFPlayer::UpdateOverheadEffects` (`c_tf_player.cpp:11616-11647`), each `ClientThink`, for anyone but the local
/// player:
/// <code>
///   Vector vecHeadToHead = EyePosition() - pLocalPlayer-&gt;EyePosition();
///   const float flEffectGap = 24.f;
///   Vector vecRightOffset = CrossProduct( vecHeadToHead, Vector( 0, 0, 1 ) ).Normalized();
///   float flFirstEffectOffset = -flEffectGap * 0.5f * ( nOverheadEffectCount - 1 );
///   ... vecOffset = vecOverheadEffectPosition + ( flFirstEffectOffset + flEffectGap * i ) * vecRightOffset;
/// </code>
/// with `GetOverheadEffectPosition()` = `GetClassEyeHeight() + Vector( 0, 0, 20 )` (`:11653-11656`), the class's
/// `g_TFClassViewVectors` height times the model scale, `VEC_VIEW_SCALED` outside scout to engineer
/// (tf_player_shared.cpp:12729-12748). With no local player the offset is the one the effect was made with.
/// </remarks>
public sealed class OverheadIconsConformanceTests
{
    /// <remarks>A soldier (68) at scale 1, seen from the south (local below him on Y): right is +X.</remarks>
    [TestCase(0, 1, 0f, TestName = "Offset_OneIcon_StandsOverHisHead")]
    [TestCase(0, 2, -12f, TestName = "Offset_TheFirstOfTwo_StandsTwelveLeft")]
    [TestCase(1, 2, 12f, TestName = "Offset_TheSecondOfTwo_StandsTwelveRight")]
    [TestCase(2, 3, 24f, TestName = "Offset_TheLastOfThree_StandsTwentyFourRight")]
    public void Offset_IconsInARow_AreTwentyFourApartAcrossTheView(int index, int count, float across)
    {
        Vector3 offset = OverheadIcons.Offset(index, count, eyeHeight: 68f, headToHead: new Vector2(0f, 100f));

        offset.X.ShouldBe(across, 1e-4f);
        offset.Y.ShouldBe(0f, 1e-4f);
        offset.Z.ShouldBe(88f, 1e-4f);
    }

    [Test]
    public void Offset_WithNoLocalPlayer_IsTheOffsetItWasMadeWith()
    {
        OverheadIcons.Offset(1, 2, eyeHeight: 68f, headToHead: null).ShouldBe(new Vector3(0f, 0f, 88f));
    }

    [TestCase(3, 1f, 68f, TestName = "EyeHeight_ASoldier_Is68")]
    [TestCase(1, 2f, 130f, TestName = "EyeHeight_AGiantScout_IsScaled")]
    [TestCase(10, 1f, 72f, TestName = "EyeHeight_ACivilian_IsVecView")]
    [TestCase(0, 1f, 72f, TestName = "EyeHeight_NoClass_IsVecView")]
    public void EyeHeight_PerClassAndScale_IsGetClassEyeHeight(int playerClass, float scale, float height)
    {
        OverheadIcons.EyeHeight(playerClass, scale).ShouldBe(height, 1e-4f);
    }

    /// <remarks>
    /// **An icon runs from the tick its effect was made** (<see cref="OverheadIcon.Since"/>), each its own, in the walk's
    /// order with its place and the count; a dormant player's after the players'.
    /// </remarks>
    [Test]
    public void At_IconsMadeAtDifferentTicks_RunFromTheirOwn()
    {
        SceneDormantPlayer asleep = new(9, 0f, 0f, 0f, IsAlive: true) { OverheadEffects = [new OverheadIcon("mark_for_death", 95)] };

        IReadOnlyList<OverheadIconRun> runs = new OverheadIcons().At(
            103,
            [Carrying(new OverheadIcon("mark_for_death", 100), new OverheadIcon("particle_nemesis_red", 102))],
            [asleep]);

        runs.ShouldBe(
        [
            new OverheadIconRun(7, "mark_for_death", 100, 0, 2),
            new OverheadIconRun(7, "particle_nemesis_red", 102, 1, 2),
            new OverheadIconRun(9, "mark_for_death", 95, 0, 1),
        ]);
    }

    /// <remarks>A seek lands on a mark made before the window, which is taken as its start: the bound.</remarks>
    [Test]
    public void At_ASeekIntoALongRun_StartsItAtTheWindow()
    {
        new OverheadIcons().At(1000, [Carrying(new OverheadIcon("mark_for_death", 10))], [])
            .ShouldHaveSingleItem().From.ShouldBe(1000 - OverheadIcons.ReplayTicks);
    }

    private static ScenePlayer Carrying(params OverheadIcon[] icons) =>
        new(EntityIndex: 7, X: 0f, Y: 0f, Z: 0f, Team: SceneTeams.Red, Health: 200, PlayerClass: 3) { OverheadEffects = icons };
}
