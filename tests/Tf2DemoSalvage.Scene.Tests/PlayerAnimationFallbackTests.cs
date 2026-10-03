using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// What a player is drawn doing when the model has no sequence for it — the engine's answer (B437).
/// </summary>
/// <remarks>
/// **`ComputeMainSequence` has one fallback, and it is sequence 0** (`multiplayer_animstate.cpp:1170-1177`):
/// <code>
/// int animDesired = SelectWeightedSequence( TranslateActivity( idealActivity ) );
/// if ( animDesired &lt; 0 ) animDesired = 0;
/// </code>
/// This file used to test a four-level ladder of our own — the primary form of the activity, then running or
/// standing, then the label <c>Stand_PRIMARY</c> — none of which the engine has. What it does have, and what that ladder
/// was standing in for, is <c>CTFPlayerAnimState::HandleDucking</c>'s own check (`tf_playeranimstate.cpp:1341-1358`):
/// a ducking player whose model lacks the translated crouch walk is not ducking, unless he is a loser, and a ducking
/// loser crouch-idles whatever his speed. Both are ported here; nothing else falls back.
///
/// A synthetic model is what makes any of this reachable — see <see cref="SyntheticSkinnedModel"/>.
/// </remarks>
public sealed class PlayerAnimationFallbackTests
{
    /// <summary>Faster than the standing threshold, so the state machine chooses running.</summary>
    private const float Running = 200f;

    /// <summary>Slow enough to be standing still.</summary>
    private const float Still = 0f;

    [Test]
    public void For_AModelWithTheExactActivity_TakesIt()
    {
        // The control for the whole file: the translated activity, found.
        PropModels.SkinnedModel model = SyntheticSkinnedModel.With(
            "ACT_MP_STAND_PRIMARY", "ACT_MP_RUN_SECONDARY", "Stand_PRIMARY");

        PlayerAnimation.For(model, Running, flags: null, alive: true, slot: "SECONDARY").ShouldBe(1);
    }

    [Test]
    public void For_AnActivityTheModelLacks_PlaysSequenceZero()
    {
        // Running with a secondary the model has no run for: the engine plays sequence 0, not the primary run at 1.
        PropModels.SkinnedModel model = SyntheticSkinnedModel.With(
            "ACT_MP_STAND_PRIMARY", "ACT_MP_RUN_PRIMARY", "Stand_PRIMARY");

        PlayerAnimation.For(model, Running, flags: null, alive: true, slot: "SECONDARY").ShouldBe(0);
    }

    [Test]
    public void For_AModelOfferingNothing_PlaysSequenceZero()
    {
        PropModels.SkinnedModel model = SyntheticSkinnedModel.With("ACT_MP_SWIM_PRIMARY", "Stand_PRIMARY");

        PlayerAnimation.For(model, Still, flags: null, alive: true).ShouldBe(0);
    }

    /// <remarks>
    /// **`HandleDucking` drops the duck when the model has no crouch walk for what is held** (`:1343-1347`), so the
    /// player runs; with the crouch walk present he crouch-walks — the control.
    /// </remarks>
    [Test]
    public void For_ADuckingPlayerWhoseModelLacksTheCrouchWalk_Runs()
    {
        const int Crouched = PlayerActivityState.Ducking | PlayerActivityState.OnGround;
        PropModels.SkinnedModel lacking = SyntheticSkinnedModel.With("ACT_MP_STAND_PRIMARY", "ACT_MP_RUN_PRIMARY");
        PropModels.SkinnedModel having = SyntheticSkinnedModel.With("ACT_MP_RUN_PRIMARY", "ACT_MP_CROUCHWALK_PRIMARY");

        PlayerAnimation.For(lacking, Running, Crouched, alive: true).ShouldBe(1);
        PlayerAnimation.For(having, Running, Crouched, alive: true).ShouldBe(1);
        PlayerAnimation.For(having, Still, Crouched, alive: true).ShouldBe(0, "no crouch idle on this model: sequence 0");
    }

    /// <remarks>
    /// **A loser keeps his duck without the crouch walk, and crouch-idles moving or not** (`:1344`, `:1351`) — so a
    /// humiliated player crouching while he runs is drawn in `ACT_MP_CROUCH_LOSERSTATE`, not running.
    /// </remarks>
    [Test]
    public void For_ADuckingLoser_CrouchIdlesEvenWhileMoving()
    {
        const int Crouched = PlayerActivityState.Ducking | PlayerActivityState.OnGround;
        PropModels.SkinnedModel model = SyntheticSkinnedModel.With("ACT_MP_RUN_LOSERSTATE", "ACT_MP_CROUCH_LOSERSTATE");

        PlayerAnimation.For(model, Running, Crouched, alive: true, table: PlayerActivityOverride.LoserState).ShouldBe(1);
        PlayerAnimation.For(model, Running, PlayerActivityState.OnGround, alive: true, table: PlayerActivityOverride.LoserState)
            .ShouldBe(0, "the control: standing, he runs");
    }

    [Test]
    public void For_ANullModel_IsRefused()
    {
        // A null model is a caller bug rather than a missing animation.
        Should.Throw<System.ArgumentNullException>(
            () => PlayerAnimation.For(null!, Still, flags: null, alive: true));
    }
}
