using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>A player's <c>m_iszCustomModel</c>, carried by an authored demo to the scene (B763).</summary>
/// <remarks>
/// `CTFPlayerClassShared::GetModelName` returns it in place of the class's model whenever it is not empty
/// (`tf_playerclass_shared.cpp:136`). A Mann vs. Machine robot is a player with one. The corpus has no MvM match, so the
/// specimen is authored: the real container, schema, entity decode and timeline, with the value put there by the test.
/// An empty string, the wire's own default, is no custom model: the control.
/// </remarks>
public sealed class SyntheticCustomModelTests
{
    [TestCase("models/bots/soldier/bot_soldier.mdl", "models/bots/soldier/bot_soldier.mdl", TestName = "Build_ACustomModelSent_ReachesTheScenePlayer")]
    [TestCase("", null, TestName = "Build_AnEmptyCustomModel_IsNone")]
    public void Build_ACustomModel_ReachesTheScenePlayer(string sent, string? expected)
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoOfGestures(
            0.015f, SceneTeams.Blu, 3, rules: null, alwaysLoser: false,
            new SyntheticPlayer.GestureSnapshot(100, 0f, PlayerActivityState.OnGround) { CustomModel = sent }));

        timeline.Frames.Single(frame => frame.Tick == 100).Players.Single().CustomModel.ShouldBe(expected);
    }

    /// <remarks>
    /// **A corpse takes its player's custom model at death** (`CreateTFRagdoll`, `c_tf_player.cpp:688-691`), captured
    /// with the cosmetics because the corpse itself carries none. An empty one is no custom model, the control.
    /// </remarks>
    [TestCase("models/bots/soldier/bot_soldier.mdl", "models/bots/soldier/bot_soldier.mdl", TestName = "Build_ACorpseOfACustomModelPlayer_CarriesTheModel")]
    [TestCase("", null, TestName = "Build_ACorpseOfAnOrdinaryPlayer_CarriesNone")]
    public void Build_ACorpse_CarriesItsPlayersCustomModel(string sent, string? expected)
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoOfACorpse(3, sent));

        timeline.Corpses.ShouldHaveSingleItem().CustomModel.ShouldBe(expected);
        timeline.CustomModels.ShouldBe(expected is null ? [] : [expected], "every custom model a player carried, to be loaded");
    }

    /// <remarks>
    /// **Captured once, as `CreateTFRagdoll` asks once.** The player's custom model changes a tick after the corpse was
    /// made, and the corpse keeps the one he died with. A capture of no custom model is kept just the same: the control.
    /// </remarks>
    [TestCase("models/bots/soldier/bot_soldier.mdl", "models/bots/heavy/bot_heavy.mdl", "models/bots/soldier/bot_soldier.mdl", TestName = "Build_APlayerWhoseModelChangesAfterDeath_KeepsHisCorpsesModel")]
    [TestCase("", "models/bots/heavy/bot_heavy.mdl", null, TestName = "Build_AHumanWhoBecomesARobotAfterDeath_LeavesHisCorpseHuman")]
    public void Build_ACorpseUpdatedAfterDeath_KeepsTheModelItWasMadeWith(string atDeath, string later, string? expected)
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoOfACorpse(3, atDeath, laterCustomModel: later));

        timeline.Corpses.ShouldHaveSingleItem().CustomModel.ShouldBe(expected);
    }

    /// <remarks>
    /// **A robot spy who died disguised to the recorder falls as his class's model** (`ShouldDrawSpyAsDisguised`,
    /// `c_tf_player.cpp:688-699`), read against the recorder's own team, RED, against the BLU spy. He still carries the
    /// custom model, which his pieces are broken from (`:7453-7476`). Undisguised is the control.
    /// </remarks>
    [TestCase(true, TestName = "Build_ARobotDisguisedToTheRecorder_IsDrawnAsHisDisguise")]
    [TestCase(false, TestName = "Build_ARobotUndisguised_IsNot")]
    public void Build_ADisguisedRobotsCorpse_IsDrawnAsShouldDrawSpyAsDisguisedSays(bool disguised)
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoOfACorpse(8, "models/bots/spy/bot_spy.mdl", disguised));

        SceneRagdoll corpse = timeline.Corpses.ShouldHaveSingleItem();
        (corpse.CustomModel, corpse.DrawnAsDisguise).ShouldBe(("models/bots/spy/bot_spy.mdl", disguised));
    }
}
