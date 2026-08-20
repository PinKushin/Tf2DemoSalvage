using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// The weapon in the followed player's hands, as the timeline reports it.
/// </summary>
/// <remarks>
/// **A viewmodel has no origin and no angles, so it cannot be a prop track like anything else.**
/// Its table is <c>BEGIN_NETWORK_TABLE_NOBASE</c>; the demo names the model and the pose and the
/// client works out where it goes, which for a viewmodel is the camera. That makes it a question a
/// caller asks about a tick rather than an entry in the scene — the viewer already knows where the
/// camera is, and nothing else in the scene wants a model with no position.
///
/// **Two cases, both measured rather than assumed** (<c>docs/findings/04-entities.md</c>): a
/// point-of-view recording carries exactly ONE viewmodel and never says whose it is, because you
/// only ever receive your own; a modern SourceTV recording carries one per player and every owner
/// handle resolves. So the lookup takes the player being followed and answers with theirs.
/// </remarks>
public sealed class TimelineViewmodelTests
{
    /// <summary>The player being followed in these fixtures.</summary>
    private const int Follower = 1;

    [Test]
    public void ViewmodelAt_APointOfViewDemo_AnswersWithTheSingleUnownedViewmodel()
    {
        // **The POV case, and the one an owner join would get wrong.** The demo carries one
        // viewmodel and no owner handle at all, so a lookup that required a match would find
        // nothing and the weapon would simply never appear — which is exactly how this feature
        // would have failed silently.
        DemoTimeline timeline = DemoTimeline.Build(
            SyntheticPlayer.DemoWithViewmodel(owner: null));

        SceneViewmodel weapon = timeline.ViewmodelAt(66, Follower).ShouldNotBeNull();

        weapon.ModelPath.ShouldBe("models/weapons/v_scattergun.mdl");
        weapon.Sequence.ShouldBe(7);
    }

    [Test]
    public void ViewmodelAt_ASourceTvDemo_AnswersWithTheOneOwnedByThatPlayer()
    {
        // The STV case: several viewmodels, each naming its owner, and the follower gets theirs.
        DemoTimeline timeline = DemoTimeline.Build(
            SyntheticPlayer.DemoWithViewmodel(owner: Follower));

        timeline.ViewmodelAt(66, Follower).ShouldNotBeNull()
            .ModelPath.ShouldBe("models/weapons/v_scattergun.mdl");
    }

    [Test]
    public void ViewmodelAt_AnotherPlayersViewmodel_IsNotOffered()
    {
        // **The control, and it is the difference between a weapon and somebody else's weapon.**
        // A lookup that ignored the owner would satisfy both tests above and put the wrong gun in
        // frame whenever a SourceTV demo carries more than one.
        DemoTimeline timeline = DemoTimeline.Build(
            SyntheticPlayer.DemoWithViewmodel(owner: 9));

        timeline.ViewmodelAt(66, Follower).ShouldBeNull();
    }

    [Test]
    public void ViewmodelAt_APlayerCarryingBoth_AnswersWithTheMainHand()
    {
        // **The defect this test was written for, and it shipped a spy watch into a soldier's
        // hands.** A player has TWO viewmodels — `MAX_VIEWMODELS` is 2 — and TF2 uses slot 1 for
        // the off hand: `CTFPlayer::GetOffHandViewModel` is `return GetViewModel( 1 )`, set by
        // `CTFWeaponInvis::Spawn` for the spy's watch and by `tf_weaponbase_grenade`.
        //
        // A lookup that ignores the slot keeps whichever entity it walked past last, which on the
        // corpus's 2009 badlands recording is the off hand — so the weapon on screen stayed
        // `v_watch_spy` while the recorder's networked class went soldier, then scout.
        //
        // The off hand is recorded SECOND here on purpose: with it first, a reader that just keeps
        // the last one would answer correctly by accident and this test could not fail.
        DemoTimeline timeline = DemoTimeline.Build(
            SyntheticPlayer.DemoWithViewmodel(owner: null, offHandModelIndex: 3));

        timeline.ViewmodelAt(66, Follower).ShouldNotBeNull()
            .ModelPath.ShouldBe("models/weapons/v_scattergun.mdl");
    }

    [Test]
    public void ViewmodelAt_WhenTheDemoNamesOwnersAtAll_DoesNotHandOutAnUnownedOne()
    {
        // **The SourceTV defect, measured on z1800 before it was written down.** Following a sniper
        // drew a demoman's arms, because the lookup treats an unowned viewmodel as belonging to
        // whoever asks — a rule written for a point-of-view recording, which carries exactly one
        // and never names an owner.
        //
        // A SourceTV demo carries one per player and names them. When one of thirty-seven fails to
        // resolve an owner, that rule hands it to every player who has none of their own:
        //
        //     player 4 class 3: c_soldier_arms owner 4     <- right
        //     player 2 class 2: c_demo_arms    owner none  <- wrong
        //
        // So an unowned viewmodel is only anybody's when the demo names NO owners at all.
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoWithViewmodel(
            owner: 9, offHandModelIndex: 3, secondUnowned: true));

        timeline.ViewmodelAt(66, Follower).ShouldBeNull(
            "an unowned viewmodel belongs to nobody once the demo has named an owner");

        // The control: the player who IS named still gets theirs, so the fix cannot have simply
        // stopped answering.
        timeline.ViewmodelAt(66, 9).ShouldNotBeNull()
            .ModelPath.ShouldBe("models/weapons/v_scattergun.mdl");
    }

    [Test]
    public void ViewmodelAt_ADemoWithNone_IsNothing()
    {
        // Every era demo before the modern ones carries a viewmodel, but a recording that does not
        // must not produce an empty model path that a loader would then report as missing.
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.Demo(
            new System.Collections.Generic.Dictionary<string, PropertyValue>
            {
                ["m_lifeState"] = PropertyValue.FromInt(0),
            }));

        timeline.ViewmodelAt(66, Follower).ShouldBeNull();
    }
}
