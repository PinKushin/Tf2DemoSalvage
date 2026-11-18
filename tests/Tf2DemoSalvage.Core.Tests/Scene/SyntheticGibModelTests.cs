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
    /// `TF_COND_DISGUISED_AS_DISPENSER`, crouched on worldspawn, dies before the RED recorder and breaks as the dispenser.
    /// Each control takes one input away: the invalid ground handle, a RED recorder's teammate, the condition itself.
    /// </remarks>
    [TestCase(true, true, SceneTeams.Blu, true, TestName = "Build_ACrouchedDispenserEnemyOnTheGround_BreaksAsTheDispenser")]
    [TestCase(true, false, SceneTeams.Blu, false, TestName = "Build_ADispenserEnemyHoldingNoGround_DoesNot")]
    [TestCase(true, true, SceneTeams.Red, false, TestName = "Build_ADispenserTeammate_DoesNot")]
    [TestCase(false, true, SceneTeams.Blu, false, TestName = "Build_ACrouchedEnemyOutOfTheCondition_DoesNot")]
    public void Build_ADispenserDisguisedCorpse_BreaksAsTheDispenserOnlyAsTheEngineDrawsIt(
        bool dispenser, bool onGround, int deadTeam, bool breaksAsDispenser)
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoOfACorpse(
            Spy, string.Empty, deadTeam: deadTeam, dispenser: dispenser, onGround: onGround));

        timeline.Corpses.ShouldHaveSingleItem().GibDispenser.ShouldBe(breaksAsDispenser);
    }
}
