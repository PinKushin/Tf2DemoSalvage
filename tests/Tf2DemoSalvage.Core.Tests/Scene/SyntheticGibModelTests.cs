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
    public void Build_ACorpse_CapturesTheModelItsPlayerWasDrawnAs(
        bool disguised, bool ghost, int? breaksAsClass, bool breaksAsGhost)
    {
        DemoTimeline timeline = DemoTimeline.Build(
            SyntheticPlayer.DemoOfACorpse(Spy, string.Empty, disguised, disguiseClass: disguised ? Soldier : 0, ghost: ghost));

        SceneRagdoll corpse = timeline.Corpses.ShouldHaveSingleItem();
        (corpse.GibDisguiseClass, corpse.GibGhost).ShouldBe((breaksAsClass, breaksAsGhost));
    }
}
