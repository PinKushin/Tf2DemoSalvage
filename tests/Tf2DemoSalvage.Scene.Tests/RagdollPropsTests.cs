using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// Corpses reach the scene's prop buffer, at the right ticks and without evicting anything (B315).
/// </summary>
/// <remarks>
/// **`RagdollAppearanceConformanceTests` proves the derivation and says nothing about whether
/// anything draws it.** That is the gap this project has shipped three no-ops through, every one
/// with a green suite — a decoded field, a unit-tested helper, and no production caller. The
/// questions here are the ones a derivation cannot answer: does a corpse become a `SceneProp`, does
/// it appear only while the demo spoke about it, and does appending it destroy the props that were
/// already in the buffer.
/// </remarks>
public sealed class RagdollPropsTests
{
    [Test]
    public void Fill_ForACorpseAtItsOwnTick_AddsAPropWithTheClassModelAndTeamSkin()
    {
        List<SceneProp> scene = [];

        RagdollProps.Fill([Corpse(team: SceneTeams.Blu)], tick: 150d, Classes, scene).ShouldBe(1);

        SceneProp corpse = scene.ShouldHaveSingleItem();

        corpse.ModelPath.ShouldBe("models/player/medic.mdl");
        corpse.Pose.Skin.ShouldBe(1);
        corpse.Pose.X.ShouldBe(-5446f);

        // **NOT the corpse's own entity index of 40** (B318). A drawn corpse takes an index of its
        // own, above every networked one, because `EntityModelSet` keys its pose and skinning
        // caches by index and a slot is reused — a corpse inheriting the caches of whatever held
        // slot 40 before crashed the viewer on the first frame one came into view. The first corpse
        // in the list draws as the first of the reserved range.
        corpse.EntityIndex.ShouldBe(RagdollProps.FirstCorpseEntityIndex);

        // **The yaw, because a corpse that does not carry it faces north** — and every body in a
        // match facing the same way is the kind of defect that reads as "the models are broken".
        corpse.Pose.Yaw.ShouldBe(137f);

        // The control on that: pitch is deliberately NOT carried, since a player's pitch lives in
        // the head's pose parameters and applying it here tips the whole body over.
        corpse.Pose.Pitch.ShouldBe(0f);
    }

    /// <remarks>
    /// **Both sides of the window, because one is satisfied by any lower bound.** A corpse drawn
    /// from tick zero would pass a test that only checked it was gone afterwards, and the visible
    /// defect — every corpse of the match lying on the floor from the opening whistle — is the one
    /// a viewer would notice first.
    /// </remarks>
    [Test]
    public void Fill_BeforeTheDeathAndAfterTheLastWord_AddsNothing()
    {
        List<SceneProp> scene = [];

        RagdollProps.Fill([Corpse(team: SceneTeams.Red)], tick: 99d, Classes, scene).ShouldBe(0);
        RagdollProps.Fill([Corpse(team: SceneTeams.Red)], tick: 201d, Classes, scene).ShouldBe(0);

        scene.ShouldBeEmpty();
    }

    /// <remarks>
    /// **The control that this APPENDS.** `DemoTimeline.PropsAt` clears the buffer as its first act
    /// and runs immediately before this; a second clear here would empty the whole scene and leave a
    /// match containing nothing but its dead — which would look like a catastrophic rendering bug
    /// rather than like a corpse feature.
    /// </remarks>
    [Test]
    public void Fill_IntoABufferHoldingProps_KeepsThem()
    {
        SceneProp bystander = new(
            7,
            "models/props_gameplay/resupply_locker.mdl",
            SceneModelKind.Studio,
            new ScenePose());

        List<SceneProp> scene = [bystander];

        RagdollProps.Fill([Corpse(team: SceneTeams.Red)], tick: 150d, Classes, scene);

        scene.Count.ShouldBe(2);
        scene[0].ShouldBe(bystander);
    }

    /// <remarks>
    /// **A class with no model must not become a prop with no model.** `SceneProp.ModelPath` is not
    /// nullable, so the alternative to skipping is an empty path — which every downstream resolver
    /// would then try and fail to open, once per frame, for the life of the demo.
    /// </remarks>
    [Test]
    public void Fill_ForACorpseWhoseClassNamesNoModel_AddsNothing()
    {
        List<SceneProp> scene = [];

        RagdollProps.Fill(
            [Corpse(team: SceneTeams.Red) with { PlayerClass = 42 }], 150d, Classes, scene)
            .ShouldBe(0);

        scene.ShouldBeEmpty();
    }

    /// <remarks>
    /// **The fade must be CONSULTED, not merely constructible.** `RagdollFadeConformanceTests`
    /// proves the rule and says nothing about whether `Fill` asks it — and a `Fill` that ignored it
    /// would pass every other test in this file while putting 57 bodies on the map.
    ///
    /// **Ticks are seconds here** (interval 1), so a corpse created at tick 100 and never looked at
    /// expires at 115. The window below runs to 20,000, which is the point: without the fade the
    /// corpse is drawn for all of it.
    /// </remarks>
    [Test]
    public void Fill_ForALongLivedCorpseNobodyLooksAt_StopsDrawingItAfterFifteenSeconds()
    {
        List<SceneProp> scene = [];
        RagdollFade fade = new(1f);

        SceneRagdoll lingering = Corpse(team: SceneTeams.Red) with { LastTick = 20_000 };

        RagdollProps.Fill([lingering], 114d, Classes, scene, fade, visible: null).ShouldBe(1);
        RagdollProps.Fill([lingering], 116d, Classes, scene, fade, visible: null).ShouldBe(0);
    }

    /// <remarks>
    /// **The control that the VISIBLE set reaches the fade.** The engine's whole rule is that a
    /// corpse on screen never expires; a `Fill` that passed the fade a constant false would pass the
    /// test above and quietly delete every body a viewer was looking at.
    /// </remarks>
    [Test]
    public void Fill_ForACorpseInTheVisibleSet_KeepsDrawingItPastFifteenSeconds()
    {
        List<SceneProp> scene = [];
        RagdollFade fade = new(1f);

        SceneRagdoll lingering = Corpse(team: SceneTeams.Red) with { LastTick = 20_000 };

        // **The DRAWN index, not the corpse's own** (B318). The renderer's visible set holds what it
        // drew, so a fade asking under the networked slot would find no corpse ever visible and
        // expire every one of them on the long timer — a plausible-looking fade that is never right.
        HashSet<int> watched = [RagdollProps.FirstCorpseEntityIndex];

        for (double at = 100d; at <= 300d; at += 1d)
        {
            RagdollProps.Fill([lingering], at, Classes, scene, fade, watched).ShouldBe(1);
        }
    }

    /// <remarks>
    /// **No corpse may draw under an index the demo can also use** (B318). `EntityModelSet` keys
    /// its pose, its skinning buffers and its visible set by entity index, and sharing one with a
    /// networked entity crashed the viewer on the first frame a corpse came into view — through two
    /// green gate runs and 31 UI tests, because nothing in the suites renders a scene where one
    /// index carries two models.
    ///
    /// **Every corpse also needs its OWN index, not merely a shifted slot.** Offsetting the
    /// entity index by the base would still give the second occupant of a reused slot the first
    /// one's caches, and two class models do not have the same bone count — the same crash, rarer,
    /// which is the kind of fix that survives the measurement that found the bug.
    /// </remarks>
    [Test]
    public void Fill_ForCorpsesSharingAnEntitySlot_DrawsThemUnderDistinctReservedIndices()
    {
        List<SceneProp> scene = [];

        // Two corpses that reused one slot — the same entity index, different serials.
        SceneRagdoll first = Corpse(team: SceneTeams.Red);
        SceneRagdoll second = Corpse(team: SceneTeams.Blu) with { Serial = 2 };

        RagdollProps.Fill([first, second], tick: 150d, Classes, scene).ShouldBe(2);

        scene[0].EntityIndex.ShouldNotBe(scene[1].EntityIndex, "one index per corpse, not per slot");

        foreach (SceneProp corpse in scene)
        {
            corpse.EntityIndex.ShouldBeGreaterThanOrEqualTo(
                RagdollProps.FirstCorpseEntityIndex,
                "above every index a demo can send");

            corpse.EntityIndex.ShouldBeLessThan(
                ViewmodelScene.ArmsEntityIndex,
                "and below the range the viewmodel already reserved");
        }
    }

    /// <summary>A corpse of a medic, spoken about between ticks 100 and 200.</summary>
    private static SceneRagdoll Corpse(int team) =>
        new(EntityIndex: 40,
            Serial: 1,
            PlayerClass: 5,
            Team: team,
            X: -5446f,
            Y: 4055f,
            Z: 21f,
            Gib: false,
            Burning: false,
            FeignDeath: false,
            WasDisguised: false,
            FirstTick: 100,
            LastTick: 200,
            Yaw: 137f);

    /// <summary>A synthetic class table — see the conformance suite for why it is not the real one.</summary>
    private static string? Classes(int playerClass) =>
        playerClass == 5 ? "models/player/medic.mdl" : null;
}
