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
    public void OffHandViewmodelAt_APlayerCarryingBoth_AnswersWithTheOtherOne()
    {
        // **Both are on screen at once, which is the point.** The owner, who has played the class:
        // "main viewmodel doesnt get hidden when a spy goes invis, the watch just comes up and
        // everything goes transparent". So the off hand is not an alternative to the weapon — it is
        // a second model beside it, and answering with only the main hand draws a cloaking spy one
        // model short.
        DemoTimeline timeline = DemoTimeline.Build(
            SyntheticPlayer.DemoWithViewmodel(owner: null, offHandModelIndex: 3));

        timeline.ViewmodelAt(66, Follower).ShouldNotBeNull()
            .ModelPath.ShouldBe("models/weapons/v_scattergun.mdl");

        timeline.OffHandViewmodelAt(66, Follower).ShouldNotBeNull()
            .ModelPath.ShouldBe("models/weapons/v_watch.mdl");
    }

    [Test]
    public void OffHandViewmodelAt_APlayerCarryingOnlyAWeapon_IsNothing()
    {
        // **The control, and it is most of the game.** Only the spy's watch uses slot 1 — the SDK
        // suggests grenades do too, but TF2's were cut before release and no shipped item names
        // them. So every other class must answer nothing here rather than being handed the weapon
        // a second time.
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoWithViewmodel(owner: null));

        timeline.ViewmodelAt(66, Follower).ShouldNotBeNull();
        timeline.OffHandViewmodelAt(66, Follower).ShouldBeNull();
    }

    [Test]
    public void OffHandViewmodelAt_AWatchFlaggedNoDraw_IsNothing()
    {
        // **Every player carries a slot-1 viewmodel at all times, whether or not anything is in
        // that hand.** Measured on z1800: 23 entities sending `m_nViewModelIndex 1` in the first
        // 400 snapshots, in a Highlander match with one spy. So "there is an off-hand entity" is
        // not "draw an off-hand model" — the engine decides with EF_NODRAW, set by
        // `CTFWeaponInvis::SetWeaponVisible` on the viewmodel itself.
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoWithViewmodel(
            owner: null, offHandModelIndex: 3, offHandHidden: true));

        timeline.OffHandViewmodelAt(66, Follower).ShouldBeNull();

        // The control: hiding the watch must not take the weapon with it.
        timeline.ViewmodelAt(66, Follower).ShouldNotBeNull()
            .ModelPath.ShouldBe("models/weapons/v_scattergun.mdl");
    }

    [Test]
    public void OffHandViewmodelAt_AfterTheWatchIsPutAway_StopsAnsweringWithIt()
    {
        // **The case that separates recording the flag from filtering on it.** With one tick, a
        // reader that skips hidden viewmodels at record time and one that records them as hidden
        // are indistinguishable — both answer nothing. They differ here: skipping leaves the last
        // recorded sample saying "visible", so the lookup keeps handing out a watch that was put
        // away, for the rest of the demo.
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoWithViewmodel(
            owner: null, offHandModelIndex: 3, offHandHiddenLater: true));

        timeline.OffHandViewmodelAt(66, Follower).ShouldNotBeNull(
            "the watch is out at the first tick");

        timeline.OffHandViewmodelAt(SyntheticPlayer.HiddenTick, Follower).ShouldBeNull(
            "it was put away, and the last state is the one that counts");
    }

    [Test]
    public void OffHandViewmodelAt_AfterTheWatchsModelIsCleared_StopsAnsweringWithIt()
    {
        // **The second way a viewmodel leaves the screen, and it needs the same treatment.** Index
        // 0 means "no model"; all 22 off-hand viewmodels in z1800's opening snapshots send exactly
        // that, so the empty case is the common one and skipping it at record time looks harmless.
        //
        // It is not, for the reason the EF_NODRAW case is not: a viewmodel that HELD a model and
        // then stops keeps its last recorded sample, and the lookup goes on answering with a watch
        // that is no longer there.
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoWithViewmodel(
            owner: null, offHandModelIndex: 3, offHandStowedLater: true));

        timeline.OffHandViewmodelAt(66, Follower).ShouldNotBeNull();
        timeline.OffHandViewmodelAt(SyntheticPlayer.HiddenTick, Follower).ShouldBeNull();

        // The control: an empty model path must not be offered for loading either, or the viewer
        // asks the archive for "" on every demo in the corpus.
        timeline.ViewmodelModels.ShouldNotContain(string.Empty);
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
