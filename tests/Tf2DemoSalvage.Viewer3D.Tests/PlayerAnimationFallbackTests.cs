using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// What a player is drawn doing when the model has no sequence for it.
/// </summary>
/// <remarks>
/// **This is the code that decides whether a player stands or lies on their back**, and none of it
/// was covered. Every existing test of it builds its model from the installed game, so on a machine
/// without TF2 — every CI runner, and the one this was written on — the whole chain is skipped.
/// Measured 2026-08-19 on the first Windows mutation run: 20 of PlayerAnimation's 28 mutants had no
/// coverage, and the uncovered lines were exactly the fallbacks.
///
/// The chain has four levels and they are tried in this order:
///
/// 1. the activity with the held weapon's suffix — <c>ACT_MP_CROUCHWALK_SECONDARY</c>;
/// 2. the same activity in its primary form, because a class missing a crouch-walk for the slot it
///    is holding still has the primary one, and that is nearer to what the player is doing than a
///    different activity would be;
/// 3. running or standing for the slot, whichever the speed suggests;
/// 4. the label <c>Stand_PRIMARY</c>, looked up by name.
///
/// **Level 4 is the one with history.** It is a label lookup rather than an activity lookup, and it
/// was once written with <c>Contains</c> — which returned sequence 9, <c>AttackStand_PRIMARY</c>,
/// for a scout, while the real <c>stand_PRIMARY</c> at 175 was never reached. An attack sequence is
/// a fraction of a second long, so every player in the demo was drawn mid-swing and then dropped.
/// <c>Studio_LookupSequence</c> compares with <c>stricmp</c>; so does this now.
///
/// A synthetic model is what makes any of this reachable — see <see cref="SyntheticSkinnedModel"/>
/// for why that is legitimate here and where its limits are.
/// </remarks>
public sealed class PlayerAnimationFallbackTests
{
    /// <summary>Faster than the standing threshold, so the state machine chooses running.</summary>
    private const float Running = 200f;

    /// <summary>Slow enough to be standing still.</summary>
    private const float Still = 0f;

    [Test]
    public void For_AModelWithTheExactActivity_TakesItRatherThanAFallback()
    {
        // The control for the whole file. Every test below asserts that a fallback was reached, and
        // a selector that always fell through would satisfy all of them.
        PropModels.SkinnedModel model = SyntheticSkinnedModel.With(
            "ACT_MP_STAND_PRIMARY", "ACT_MP_RUN_SECONDARY", "Stand_PRIMARY");

        PlayerAnimation.For(model, Running, flags: null, alive: true, slot: "SECONDARY")
            .ShouldBe(1);
    }

    [Test]
    public void For_ASlotTheModelLacks_FallsBackToThePrimaryFormOfTheSameActivity()
    {
        // Level 2. The player is running and holding a secondary, and the model has only the
        // primary run — which is the same motion with a different weapon held, and much closer
        // than changing what the player is doing.
        PropModels.SkinnedModel model = SyntheticSkinnedModel.With(
            "ACT_MP_STAND_PRIMARY", "ACT_MP_RUN_PRIMARY", "Stand_PRIMARY");

        PlayerAnimation.For(model, Running, flags: null, alive: true, slot: "SECONDARY")
            .ShouldBe(1);
    }

    [Test]
    public void For_NeitherFormOfTheActivity_FallsBackToRunningWhenMoving()
    {
        // Level 3, moving. The model has no crouch-walk in any form, so the activity itself has to
        // change — and running is nearer to a crouch-walk than standing is.
        PropModels.SkinnedModel model = SyntheticSkinnedModel.With(
            "ACT_MP_STAND_PRIMARY", "ACT_MP_RUN_PRIMARY", "Stand_PRIMARY");

        int crouched = PlayerAnimation.For(
            model,
            Running,
            flags: PlayerActivityState.Ducking | PlayerActivityState.OnGround,
            alive: true);

        crouched.ShouldBe(1);
    }

    [Test]
    public void For_NeitherFormOfTheActivity_FallsBackToStandingWhenStill()
    {
        // Level 3, stationary — the other arm of the same branch. A test that only ever moved
        // would leave the speed comparison free to be written either way round.
        PropModels.SkinnedModel model = SyntheticSkinnedModel.With(
            "ACT_MP_STAND_PRIMARY", "ACT_MP_RUN_PRIMARY", "Stand_PRIMARY");

        int crouched = PlayerAnimation.For(
            model,
            Still,
            flags: PlayerActivityState.Ducking | PlayerActivityState.OnGround,
            alive: true);

        crouched.ShouldBe(0);
    }

    [Test]
    public void For_AModelWithNoActivitiesAtAll_FindsStandPrimaryByLabel()
    {
        // **Level 4, and the one that laid every player on their back.** No sequence here carries a
        // matching activity, so the last resort is a label lookup — and the label it wants is
        // present alongside a LONGER one that embeds it. A `Contains` match returns the wrong one;
        // an exact match returns index 1.
        //
        // The decoy is first on purpose: a lookup that scans in order and stops on a substring
        // finds it before ever reaching the real sequence, which is exactly how the original bug
        // presented.
        PropModels.SkinnedModel model = SyntheticSkinnedModel.With(
            "AttackStand_PRIMARY", "Stand_PRIMARY");

        PlayerAnimation.For(model, Still, flags: null, alive: true).ShouldBe(1);
    }

    [Test]
    public void For_TheLabelLookup_IsCaseInsensitiveLikeStudioLookupSequence()
    {
        // `Studio_LookupSequence` compares with stricmp, and real models spell it `stand_PRIMARY`
        // in lower case where this code asks for `Stand_PRIMARY`. An ordinal comparison finds
        // nothing and the player drops to the reference pose.
        PropModels.SkinnedModel model = SyntheticSkinnedModel.With(
            "AttackStand_PRIMARY", "stand_PRIMARY");

        PlayerAnimation.For(model, Still, flags: null, alive: true).ShouldBe(1);
    }

    [Test]
    public void For_AModelOfferingNothingSuitable_ReportsMinusOneRatherThanGuessing()
    {
        // The end of the chain. Returning 0 would be a plausible-looking answer that draws whatever
        // sequence happens to be first, which on a real model is rarely a standing pose; −1 lets
        // the caller leave the model in its rest pose and say so.
        PropModels.SkinnedModel model = SyntheticSkinnedModel.With("ACT_MP_SWIM_PRIMARY");

        PlayerAnimation.For(model, Still, flags: null, alive: true).ShouldBe(-1);
    }

    [Test]
    public void For_ANullModel_IsRefused()
    {
        // A null model is a caller bug rather than a missing animation, and the two want different
        // handling: one is a fallback, the other is a defect worth surfacing.
        Should.Throw<System.ArgumentNullException>(
            () => PlayerAnimation.For(null!, Still, flags: null, alive: true));
    }
}
