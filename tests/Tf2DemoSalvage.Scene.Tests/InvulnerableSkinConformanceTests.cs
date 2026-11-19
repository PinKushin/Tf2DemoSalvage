using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`C_TFPlayer::GetSkin`'s invulnerable step (c_tf_player.cpp:7827-7833), B776.</summary>
/// <remarks>
/// The engine, verbatim:
///
/// <code>
///   // 3 and 4 are invulnerable
///   if ( m_Shared.IsInvulnerable() &amp;&amp;
///        ( !m_Shared.InCond( TF_COND_INVULNERABLE_HIDE_UNLESS_DAMAGED ) || gpGlobals-&gt;curtime &lt; GetLastDamageTimeMvMOnly() + 2.0f ) )
///   {
///       nSkin += 2;
///       bCheckSpyMask = false;
///   }
/// </code>
///
/// `IsInvulnerable` is `TF_COND_INVULNERABLE` (5), `_USER_BUFF` (52), `_HIDE_UNLESS_DAMAGED` (51) or `_CARD_EFFECT` (57)
/// (tf_player_shared.cpp:8099-8107). The step sits after the team skin and before the zombie and the mask, so a zombie
/// adds to the übered skin and an übered spy shows no mask.
/// </remarks>
public sealed class InvulnerableSkinConformanceTests
{
    private const int Spy = 8;
    private const int Heavy = 6;
    private const int Medic = 5;

    /// <summary>Conditions with one bit set, in whichever of the five variables carries it.</summary>
    private static PlayerConditions With(params int[] conditions)
    {
        int[] bits = new int[5];
        foreach (int condition in conditions)
        {
            bits[condition / 32] |= 1 << (condition % 32);
        }

        return new PlayerConditions(bits[0], bits[1], bits[2], bits[3], bits[4]);
    }

    private static ScenePlayer Player(int team, int playerClass, PlayerConditions conditions) =>
        new(1, 0f, 0f, 0f, Team: team, Health: 100, PlayerClass: playerClass, Conditions: conditions);

    [TestCase(SceneTeams.Red, PlayerConditions.Invulnerable, 2, TestName = "VisibleSkin_ARedPlayerUbercharged_IsSkinTwo")]
    [TestCase(SceneTeams.Blu, PlayerConditions.Invulnerable, 3, TestName = "VisibleSkin_ABluPlayerUbercharged_IsSkinThree")]
    [TestCase(SceneTeams.Blu, 52, 3, TestName = "VisibleSkin_ABluPlayerWithTheUserBuff_IsSkinThree")]
    [TestCase(SceneTeams.Red, 57, 2, TestName = "VisibleSkin_ARedPlayerWithTheCardEffect_IsSkinTwo")]
    [TestCase(SceneTeams.Blu, PlayerConditions.Stealthed, 1, TestName = "VisibleSkin_ABluPlayerWithAnotherCondition_KeepsHisTeamSkin")]
    public void VisibleSkin_AnInvulnerableCondition_AddsTwo(int team, int condition, int expected) =>
        Disguise.VisibleSkin(Player(team, Heavy, With(condition))).ShouldBe(expected);

    /// <remarks>
    /// `TF_COND_INVULNERABLE_HIDE_UNLESS_DAMAGED` (51) shows the übered skin only within two seconds of the last damage,
    /// `m_flMvMLastDamageTime`, which the client receives (c_tf_player.cpp:3803-3804).
    /// </remarks>
    [TestCase(11f, 3, TestName = "VisibleSkin_HiddenUnlessDamagedOneSecondAfterDamage_IsTheUberedSkin")]
    [TestCase(11.99f, 3, TestName = "VisibleSkin_HiddenUnlessDamagedJustInsideTwoSeconds_IsTheUberedSkin")]
    [TestCase(12f, 1, TestName = "VisibleSkin_HiddenUnlessDamagedAtTwoSeconds_IsTheTeamSkin")]
    [TestCase(30f, 1, TestName = "VisibleSkin_HiddenUnlessDamagedLongAfterDamage_IsTheTeamSkin")]
    public void VisibleSkin_HiddenUnlessDamaged_ShowsOnlyForTwoSecondsAfterDamage(float curTime, int expected)
    {
        ScenePlayer bot = Player(SceneTeams.Blu, Heavy, With(51)) with { LastDamageTimeMvMOnly = 10f };

        Disguise.VisibleSkin(bot, curTime: curTime).ShouldBe(expected);
    }

    [Test]
    public void VisibleSkin_HiddenUnlessDamagedBesideAnUbercharge_StillWaitsForDamage()
    {
        // The gate is `InCond( HIDE_UNLESS_DAMAGED )` alone, not which condition made him invulnerable: with both set and
        // no recent damage, a full übercharge stays hidden too.
        ScenePlayer bot = Player(SceneTeams.Blu, Heavy, With(51, PlayerConditions.Invulnerable)) with { LastDamageTimeMvMOnly = 10f };

        Disguise.VisibleSkin(bot, curTime: 30f).ShouldBe(1, "InCond( HIDE_UNLESS_DAMAGED ) gates every invulnerability");
        Disguise.VisibleSkin(bot, curTime: 10.5f).ShouldBe(3);
    }

    /// <remarks>`bCheckSpyMask = false`: an übered disguised spy shows no mask offset, to a teammate or an enemy.</remarks>
    [Test]
    public void VisibleSkin_AnUberchargedDisguisedSpy_HasNoMaskOffset()
    {
        ScenePlayer spy = Player(SceneTeams.Red, Spy, With(PlayerConditions.Disguised, PlayerConditions.Invulnerable)) with
        {
            DisguiseClass = Medic,
            DisguiseTeam = SceneTeams.Blu,
        };

        Disguise.VisibleSkin(spy).ShouldBe(2, "a teammate sees his own team, übered, and no mask");
        Disguise.VisibleSkin(spy with { IsEnemy = true }).ShouldBe(3, "an enemy sees the disguise's team, übered");
        Disguise.VisibleSkin(spy with { IsEnemy = true, DisguiseClass = Spy, DisguiseMaskClass = Medic })
            .ShouldBe(3, "an enemy disguised as a spy: no mask either");

        // The control: the same spy without the übercharge wears the mask.
        Disguise.VisibleSkin(spy with { Conditions = With(PlayerConditions.Disguised) }).ShouldBe(4 + ((Medic - 1) * 2));
    }

    /// <remarks>The zombie step comes after: `AdjustSkinIndexForZombie` adds 4 to the übered skin (c_tf_player.cpp:7836-7847).</remarks>
    [Test]
    public void VisibleSkin_AnUberchargedZombie_AddsTheZombieOffsetToTheUberedSkin()
    {
        ScenePlayer zombie = Player(SceneTeams.Blu, Heavy, With(PlayerConditions.Invulnerable)) with { PlayerSkinOverride = 1 };

        Disguise.VisibleSkin(zombie, viewerHalloweenVision: true).ShouldBe(1 + 2 + 4);
    }
}
