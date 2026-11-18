using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>The model a non-custom player's pieces break from: his drawn one, captured at death (B766).</summary>
/// <remarks>
/// `CreatePlayerGibs` breaks `GetModelIndex()` for a player with no custom model (`c_tf_player.cpp:7453-7454`), and
/// `InitPlayerGibs` built his list from it when the model last changed (`:7355-7363`, `:5343-5348`), which
/// `C_TFPlayer::ValidateModelIndex` sets: the disguise's class to an enemy, then the ghost, then his class (`:8988-9022`),
/// ending in `SetModelByIndex` and so `OnNewModel` (`c_baseentity.cpp:2531`, `:1775-1792`). The dead player is BLU, the
/// recorder RED: an enemy.
/// </remarks>
public sealed class SyntheticGibModelTests
{
    private const int Spy = 8;
    private const int Soldier = 3;

    [TestCase(true, false, Soldier, false, TestName = "Build_ASpyDisguisedToTheRecorder_BreaksAsHisDisguisesClass")]
    [TestCase(false, false, null, false, TestName = "Build_AnUndisguisedSpy_BreaksAsHimself")]
    [TestCase(false, true, null, true, TestName = "Build_AGhost_BreaksAsTheGhost")]
    [TestCase(true, true, Soldier, true, TestName = "Build_ADisguisedGhost_CarriesBothForTheDisguiseToComeFirst")]
    public void Build_ACorpse_CapturesTheModelItsPlayerWasDrawnAs(
        bool disguised, bool ghost, int? breaksAsClass, bool breaksAsGhost)
    {
        DemoTimeline timeline = DemoTimeline.Build(
            SyntheticPlayer.DemoOfACorpse(Spy, string.Empty, disguised, disguiseClass: disguised ? Soldier : null, ghost: ghost));

        SceneRagdoll corpse = timeline.Corpses.ShouldHaveSingleItem();
        (corpse.GibDisguiseClass, corpse.GibGhost).ShouldBe((breaksAsClass, breaksAsGhost));
    }

    /// <remarks>
    /// **Only to an enemy** (`IsEnemyPlayer()`, `:8999`): a RED spy disguised before the RED recorder is drawn as himself,
    /// and so is any spy before a recorder who is no player in the demo (slot 9), as a SourceTV recording's is none. The
    /// BLU spy before the RED recorder is the control.
    /// </remarks>
    [TestCase(SceneTeams.Blu, 0, Soldier, TestName = "Build_ADisguisedEnemy_BreaksAsHisDisguise")]
    [TestCase(SceneTeams.Red, 0, null, TestName = "Build_ADisguisedTeammate_BreaksAsHimself")]
    [TestCase(SceneTeams.Blu, 9, null, TestName = "Build_ADisguisedSpyWithNoRecorder_BreaksAsHimself")]
    public void Build_ADisguisedCorpse_BreaksAsHisDisguiseOnlyToAnEnemy(int deadTeam, int recorderSlot, int? breaksAsClass)
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoOfACorpse(
            Spy, string.Empty, disguised: true, disguiseClass: Soldier, deadTeam: deadTeam, recorderSlot: (byte)recorderSlot));

        timeline.Corpses.ShouldHaveSingleItem().GibDisguiseClass.ShouldBe(breaksAsClass);
    }

    /// <remarks>
    /// **The dispenser, `ValidateModelIndex`'s first branch** (`c_tf_player.cpp:8990-8998`): a BLU spy in
    /// `TF_COND_DISGUISED_AS_DISPENSER`, crouched on a held ground entity, dies before the RED recorder and breaks as the
    /// dispenser. Each control takes ONE input away: the condition, the crouch, the ground (the invalid handle, and a
    /// handle whose serial names a slot's earlier occupant, which `CHandle::Get` does not resolve), the enemy.
    /// </remarks>
    [TestCase(true, true, true, 1, SceneTeams.Blu, true, TestName = "Build_ACrouchedDispenserEnemyOnTheGround_GibsAsTheDispenser")]
    [TestCase(false, true, true, 1, SceneTeams.Blu, false, TestName = "Build_ACrouchedEnemyOutOfTheCondition_GibsAsHimself")]
    [TestCase(true, false, true, 1, SceneTeams.Blu, false, TestName = "Build_AStandingDispenserEnemy_GibsAsHimself")]
    [TestCase(true, true, false, 1, SceneTeams.Blu, false, TestName = "Build_ADispenserEnemyHoldingNoGround_GibsAsHimself")]
    [TestCase(true, true, true, 2, SceneTeams.Blu, false, TestName = "Build_ADispenserEnemyOnAStaleHandle_GibsAsHimself")]
    [TestCase(true, true, true, 1, SceneTeams.Red, false, TestName = "Build_ADispenserTeammate_GibsAsHimself")]
    public void Build_ADispenserDisguisedCorpse_GibsAsTheDispenserOnlyAsTheEngineDrawsIt(
        bool dispenser, bool crouched, bool onGround, int groundSerial, int deadTeam, bool gibsAsDispenser)
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoOfACorpse(
            Spy, string.Empty, deadTeam: deadTeam, dispenser: dispenser, crouched: crouched, onGround: onGround,
            groundSerial: groundSerial));

        timeline.Corpses.ShouldHaveSingleItem().GibDispenser.ShouldBe(gibsAsDispenser);
    }

    /// <remarks>
    /// **"Enemy" by the living players' own route** (`LocalTeam`, `PlayerTeam`): the resource's team before the entity's.
    /// His entity says RED, the RED recorder's teammate, but the player resource says BLU, so he is the enemy the living
    /// timeline draws as the dispenser, and his corpse gibs as it. The same corpse with no resource is the control.
    /// </remarks>
    [TestCase(SceneTeams.Blu, true, TestName = "Build_ADispenserTheResourceCallsAnEnemy_GibsAsTheDispenser")]
    [TestCase(null, false, TestName = "Build_ADispenserWhoseEntityIsATeammateWithNoResource_GibsAsHimself")]
    public void Build_ADispenserCorpse_JudgesTheEnemyAsTheLivingPlayerIsJudged(int? resourceTeam, bool gibsAsDispenser)
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoOfACorpse(
            Spy, string.Empty, deadTeam: SceneTeams.Red, dispenser: true, crouched: true, resourceTeam: resourceTeam));

        timeline.Corpses.ShouldHaveSingleItem().GibDispenser.ShouldBe(gibsAsDispenser);
    }

    /// <remarks>
    /// **Captured at death and kept**: `InitPlayerGibs` built the list when his model last changed (`:7355-7363`), and the
    /// corpse is made once. He leaves the condition, stands and loses the ground on the next tick; the corpse still breaks
    /// as the dispenser.
    /// </remarks>
    [Test]
    public void Build_ADispenserCorpseWhosePlayerStandsAfter_StillGibsAsTheDispenser()
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoOfACorpse(
            Spy, string.Empty, dispenser: true, crouched: true, standsAfter: true));

        timeline.Corpses.ShouldHaveSingleItem().GibDispenser.ShouldBeTrue();
    }
}
