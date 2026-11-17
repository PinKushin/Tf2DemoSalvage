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
    /// **An icon runs from the tick it was added, kept across frames**, so its particle is stepped from there rather than
    /// rebuilt; a seek looks back at most <see cref="OverheadIcons.ReplayTicks"/>. The mark from tick 100, the nemesis
    /// joining at 102, each keeping its own start; the mark then leaves and is not reported.
    /// </remarks>
    [Test]
    public void At_IconsAddedAtDifferentTicks_RunFromTheirOwn()
    {
        Dictionary<int, IReadOnlyList<ScenePlayer>> frames = new()
        {
            [100] = [Carrying("mark_for_death")],
            [101] = [Carrying("mark_for_death")],
            [102] = [Carrying("mark_for_death", "particle_nemesis_red")],
            [103] = [Carrying("particle_nemesis_red")],
        };
        OverheadIcons icons = new();

        icons.At(100, Frame(frames), NobodyDormant);
        icons.At(101, Frame(frames), NobodyDormant);
        IReadOnlyList<OverheadIconRun> both = [.. icons.At(102, Frame(frames), NobodyDormant)];
        IReadOnlyList<OverheadIconRun> one = icons.At(103, Frame(frames), NobodyDormant);

        both.ShouldBe([new OverheadIconRun(7, "mark_for_death", 100, 0, 2), new OverheadIconRun(7, "particle_nemesis_red", 102, 1, 2)]);
        one.ShouldBe([new OverheadIconRun(7, "particle_nemesis_red", 102, 0, 1)]);
    }

    /// <remarks>A seek lands on a mark that began before the window, which is taken as its start: the bound.</remarks>
    [Test]
    public void At_ASeekIntoALongRun_StartsItAtTheWindow()
    {
        OverheadIcons icons = new();

        icons.At(1000, _ => [Carrying("mark_for_death")], NobodyDormant)
            .ShouldHaveSingleItem().From.ShouldBe(1000 - OverheadIcons.ReplayTicks);
    }

    /// <remarks>
    /// **A player going dormant keeps his run**: the mark is carried by the player at 100, by the dormant player at 101,
    /// and still runs from 100. Had dormant players been left out, it would have restarted at 102.
    /// </remarks>
    [Test]
    public void At_AMarkCarriedThroughDormancy_KeepsItsStart()
    {
        OverheadIcons icons = new();
        SceneDormantPlayer asleep = new(7, 0f, 0f, 0f, IsAlive: true) { OverheadEffects = ["mark_for_death"] };

        icons.At(100, tick => tick == 100 ? [Carrying("mark_for_death")] : [], NobodyDormant);
        icons.At(101, tick => tick == 100 ? [Carrying("mark_for_death")] : [], tick => tick == 101 ? [asleep] : []);

        icons.At(102, tick => tick == 101 ? [] : [Carrying("mark_for_death")], tick => tick == 101 ? [asleep] : [])
            .ShouldHaveSingleItem().From.ShouldBe(100);
    }

    private static readonly System.Func<int, IReadOnlyList<SceneDormantPlayer>> NobodyDormant = static _ => [];

    private static System.Func<int, IReadOnlyList<ScenePlayer>> Frame(Dictionary<int, IReadOnlyList<ScenePlayer>> frames) =>
        tick => frames.GetValueOrDefault(tick, []);

    private static ScenePlayer Carrying(params string[] icons) =>
        new(EntityIndex: 7, X: 0f, Y: 0f, Z: 0f, Team: SceneTeams.Red, Health: 200, PlayerClass: 3) { OverheadEffects = icons };
}
