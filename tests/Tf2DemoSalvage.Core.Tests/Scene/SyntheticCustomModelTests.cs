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
    }
}
