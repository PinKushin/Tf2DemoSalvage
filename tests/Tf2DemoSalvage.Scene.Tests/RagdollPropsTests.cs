using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
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
    /// **A cloaked corpse fades over its first second** — `m_bCloaked`, `ClientThink`'s ramp (c_tf_player.cpp:1392-1399).
    /// Created at tick 100 at 0.015 s a tick: 20 ticks in is 0.3, 150 ticks in is past 1. The control is the same corpse
    /// uncloaked.
    /// </remarks>
    [Test]
    public void Fill_ACloakedCorpse_CarriesItsFadeFromCreation()
    {
        List<SceneProp> scene = [];

        RagdollProps.Fill([Corpse(SceneTeams.Red) with { Cloaked = true }], tick: 120d, Classes, scene, intervalPerTick: 0.015f);
        RagdollProps.Fill([Corpse(SceneTeams.Red) with { Cloaked = true }], tick: 200d, Classes, scene, intervalPerTick: 0.015f);
        RagdollProps.Fill([Corpse(SceneTeams.Red)], tick: 200d, Classes, scene, intervalPerTick: 0.015f);

        scene.Select(prop => prop.CorpseInvisibility).ShouldBe([0.3f, 1f, 0f], tolerance: 1e-5f);
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

    /// <remarks>
    /// **A corpse keeps what it was wearing, bone-merged onto itself** (B320) — the engine's
    /// `CreateBoneAttachmentsFromWearables` builds one attachment per item and moves them across
    /// (`c_tf_player.cpp:10169-10251`). Merged rather than placed, because a worn item carries no
    /// transform: `FollowEntity` zeroes its local origin and angles, and the wearer's bones put it
    /// where it belongs.
    ///
    /// **Each item needs its own index too**, for B318's reason — they are not the wearable
    /// entities the demo sent, which belong to a player who is alive again by now and wearing them.
    /// </remarks>
    [Test]
    public void Fill_ForACorpseThatDiedWearingThings_HangsThemOnItBoneMerged()
    {
        List<SceneProp> scene = [];

        SceneRagdoll dressed = Corpse(team: SceneTeams.Blu) with
        {
            Worn =
            [
                new SceneWornItem("models/player/items/soldier/summer_hat.mdl", ItemDefinitionIndex: null),
                new SceneWornItem("models/player/items/all/beard.mdl", ItemDefinitionIndex: null),
            ],
        };

        RagdollProps.Fill([dressed], tick: 150d, Classes, scene).ShouldBe(3);

        SceneProp body = scene[0];

        foreach (SceneProp item in scene.GetRange(1, scene.Count - 1))
        {
            item.BoneMerged.ShouldBeTrue("a worn item takes the corpse's bones, not a position");
            item.AttachedTo.ShouldBe(body.EntityIndex);
            item.EntityIndex.ShouldNotBe(body.EntityIndex);
        }

        scene[1].EntityIndex.ShouldNotBe(scene[2].EntityIndex, "one index per item");
    }

    /// <summary>A corpse of a medic, spoken about between ticks 100 and 200.</summary>
    /// <remarks>
    /// **A gibbed corpse draws its PIECES and no body** (B371). `CreateTFGibs` spawns the gibs and
    /// then removes the ragdoll — `EndFadeOut()`, or `SetRenderMode( kRenderNone )`
    /// (`c_tf_player.cpp:1124-1133`) — so the body appearing alongside them would be a whole corpse
    /// standing inside its own remains.
    ///
    /// **Both halves are asserted, because either alone would pass while the feature is broken**: a
    /// count of nine says nothing if the body is there too, and "no body" says nothing if nothing
    /// replaced it.
    /// </remarks>
    [Test]
    public void Fill_ForAGibbedCorpse_DrawsThePiecesAndNotTheBody()
    {
        List<SceneProp> scene = [];

        int drawn = RagdollProps.Fill(
            [Corpse(SceneTeams.Blu) with { Gib = true }],
            tick: 150d,
            Classes,
            scene,
            gibsOf: Gibs,
            intervalPerTick: 0.015f);

        drawn.ShouldBe(3);

        scene.Count.ShouldBe(3);
        scene.ShouldAllBe(prop => prop.ClassName == RagdollProps.GibClassName);

        scene.ShouldNotContain(
            prop => prop.ClassName == RagdollProps.RagdollClassName,
            "the engine removes the ragdoll for a gib death");

        scene[0].ModelPath.ShouldBe("models/player/gibs/medicgib001.mdl");
    }

    /// <remarks>
    /// **Two gibbed corpses of one ragdoll slot are two sets of pieces** (B727): the gib index is the slot's, so it repeats,
    /// and the corpse's serial tells the second set's objects from the first's. Serials 3 and 4 in slot 5 give pieces under
    /// the same three indices carrying 3 and 4.
    /// </remarks>
    [Test]
    public void Fill_TwoGibbedCorpsesOfOneSlot_GiveTheirPiecesEachCorpsesSerial()
    {
        List<SceneProp> first = [];
        List<SceneProp> second = [];
        SceneRagdoll gibbed = Corpse(SceneTeams.Blu) with { Gib = true };

        RagdollProps.Fill([gibbed with { Serial = 3 }], 150d, Classes, first, gibsOf: Gibs, intervalPerTick: 0.015f);
        RagdollProps.Fill([gibbed with { Serial = 4 }], 150d, Classes, second, gibsOf: Gibs, intervalPerTick: 0.015f);

        second.Select(static prop => prop.EntityIndex).ShouldBe(first.Select(static prop => prop.EntityIndex), "the control: one slot");
        first.Select(static prop => prop.SerialNumber).ShouldBe([3, 3, 3]);
        second.Select(static prop => prop.SerialNumber).ShouldBe([4, 4, 4]);
    }

    /// <remarks>
    /// **A gib leaves on its OWN fade, which is shorter than a corpse's.** `fadetime` is 10 on every
    /// TF2 gib against `cl_ragdoll_fade_time`'s 15 for a body, so pieces go first. Fifty seconds
    /// after death is past every piece's fade.
    /// </remarks>
    [Test]
    public void Fill_ForAGibbedCorpsePastItsFade_DrawsNothing()
    {
        List<SceneProp> scene = [];

        // 100 ticks at 0.015 is 1.5s and inside the fade; 4000 is 60s and well past it. The corpse
        // window itself is widened so the fade is what ends this, not the entity's lifetime.
        RagdollProps.Fill(
            [Corpse(SceneTeams.Blu) with { Gib = true, LastTick = 100000 }],
            tick: 4100d,
            Classes,
            scene,
            gibsOf: Gibs,
            intervalPerTick: 0.015f)
            .ShouldBe(0);
    }

    /// <remarks>
    /// **No gib list means no pieces AND still no body**, because the engine has already removed
    /// the body — falling back to it would be a divergence rather than a graceful degradation. This
    /// is the state while a class model is still being read.
    /// </remarks>
    [Test]
    public void Fill_ForAGibbedCorpseWithNoListYet_DrawsNothing()
    {
        List<SceneProp> scene = [];

        RagdollProps.Fill(
            [Corpse(SceneTeams.Blu) with { Gib = true }], tick: 150d, Classes, scene)
            .ShouldBe(0);

        scene.ShouldBeEmpty();
    }

    /// <remarks>
    /// **The background pass simulates bodies, not what they wear** (D181). A dressed corpse is one body, drawn under the same index
    /// <see cref="RagdollProps.Fill"/> gives it, recorded to the end of its entity's window — the fade shortens what is drawn, not
    /// what is simulated.
    /// </remarks>
    [Test]
    public void Recorded_ACorpseWearingThings_IsOneBodyToItsLastTick()
    {
        SceneRagdoll dressed = Corpse(team: SceneTeams.Blu) with
        {
            Worn = [new SceneWornItem("models/player/items/all/beard.mdl", ItemDefinitionIndex: null)],
        };

        IReadOnlyList<RecordedCorpse> recorded = RagdollProps.Recorded([dressed], Classes);

        recorded.Count.ShouldBe(1);
        recorded[0].Corpse.ClassName.ShouldBe(RagdollProps.RagdollClassName);
        recorded[0].Corpse.EntityIndex.ShouldBe(RagdollProps.FirstCorpseEntityIndex);
        recorded[0].Corpse.FirstTick.ShouldBe(100);
        recorded[0].LastTick.ShouldBe(200);
    }

    /// <remarks>
    /// **A piece's window closes where <see cref="RagdollProps.Fill"/> stops emitting it**, at its own ten-second <c>fadetime</c>
    /// plus the one-second fade (B409): 11 / 0.015 is 733.3 ticks, so 833 is its last. Kept in the world past that, it would collide with corpses that straight-through
    /// play no longer has beside it. Fill itself is the oracle: it draws the piece at the recorded end and not one tick after.
    /// </remarks>
    [Test]
    public void Recorded_AGibbedCorpse_EndsEachPieceWhereFillStopsEmittingIt()
    {
        SceneRagdoll gibbed = Corpse(SceneTeams.Blu) with { Gib = true, LastTick = 100000 };

        IReadOnlyList<RecordedCorpse> recorded = RagdollProps.Recorded([gibbed], Classes, gibsOf: Gibs, intervalPerTick: 0.015f);

        recorded.Count.ShouldBe(3);
        recorded.ShouldAllBe(piece => piece.Corpse.ClassName == RagdollProps.GibClassName && piece.LastTick == 833);

        List<SceneProp> scene = [];
        RagdollProps.Fill([gibbed], 833d, Classes, scene, gibsOf: Gibs, intervalPerTick: 0.015f).ShouldBe(3);
        RagdollProps.Fill([gibbed], 834d, Classes, scene, gibsOf: Gibs, intervalPerTick: 0.015f).ShouldBe(0);
    }

    /// <remarks>
    /// **A piece's window is read from the list it was thrown from** (B766): a spy drawn as the dispenser throws the
    /// dispenser's pieces, so their `fadetime` is the dispenser's, not his class's. Here the dispenser's list has five
    /// pieces of 5 seconds against the medic's three of 10: (5 + 1) / 0.015 is 400 ticks, so 500 is each one's last. Read
    /// from the body's list, the fourth piece indexed past its end. Fill is the oracle, as above.
    /// </remarks>
    [Test]
    public void Recorded_ACorpseGibbedAsTheDispenser_EndsEachPieceAtTheDispensersFade()
    {
        SceneRagdoll gibbed = Corpse(SceneTeams.Blu) with { Gib = true, GibDispenser = true, LastTick = 100000 };
        IReadOnlyList<PhysicsBreakPiece> DispenserGibs(string model) => model == Disguise.DispenserModel
            ? [.. Enumerable.Range(1, 5).Select(piece => new PhysicsBreakPiece($@"buildables\gibs\dispenser_gib{piece}", 0f, 5f))]
            : Gibs(model);

        IReadOnlyList<RecordedCorpse> recorded =
            RagdollProps.Recorded([gibbed], Classes, gibsOf: DispenserGibs, intervalPerTick: 0.015f);

        recorded.Select(piece => piece.LastTick).ShouldBe([500, 500, 500, 500, 500]);

        List<SceneProp> scene = [];
        RagdollProps.Fill([gibbed], 500d, Classes, scene, gibsOf: DispenserGibs, intervalPerTick: 0.015f).ShouldBe(5);
        RagdollProps.Fill([gibbed], 501d, Classes, scene, gibsOf: DispenserGibs, intervalPerTick: 0.015f).ShouldBe(0);
    }

    /// <remarks>
    /// **A gib fades over one second after its `fadetime`, it does not vanish** (B409). `C_PhysPropClientside::StartFadeOut`
    /// sets `m_fDeathTime = curtime + fadeTime + FADEOUT_TIME` (1.0, `physpropclientside.cpp:21,431`), and `ClientThink` then
    /// draws it `kRenderTransTexture` at `alpha = (m_fDeathTime - curtime) / FADEOUT_TIME`, `SetRenderColorA( alpha * 256 )`
    /// (`:420-424`). 700 ticks at 0.015 is 10.5 s: half a second into the fade, alpha 0.5, so 128.
    /// </remarks>
    [Test]
    public void Fill_AGibHalfASecondIntoItsFade_DrawsTranslucentAtHalfAlpha()
    {
        List<SceneProp> scene = [];

        RagdollProps.Fill(
            [Corpse(SceneTeams.Blu) with { Gib = true, LastTick = 100000 }],
            tick: 800d,
            Classes,
            scene,
            gibsOf: Gibs,
            intervalPerTick: 0.015f)
            .ShouldBe(3);

        scene.ShouldAllBe(gib => gib.Pose.RenderMode == 2 && gib.Pose.RenderAlpha == 128);
    }

    /// <remarks>The control: before its `fadetime` a gib is drawn opaque, in the normal render mode.</remarks>
    [Test]
    public void Fill_AGibBeforeItsFade_DrawsOpaque()
    {
        List<SceneProp> scene = [];

        RagdollProps.Fill(
            [Corpse(SceneTeams.Blu) with { Gib = true, LastTick = 100000 }], tick: 150d, Classes, scene, gibsOf: Gibs, intervalPerTick: 0.015f);

        scene.ShouldAllBe(gib => gib.Pose.RenderMode == 0 && gib.Pose.RenderAlpha == 255);
    }

    /// <remarks>
    /// **A decapitation throws the class's head beside a headless body** (B746): <c>CreateTFHeadGib</c> after
    /// <c>CreateTFRagdoll</c> when <c>IsDecapitation()</c> (<c>c_tf_player.cpp:1240-1245</c>), the one piece named by
    /// <c>g_pszHeadGibs[TF_CLASS_MEDIC]</c>, <c>medicgib007</c>. Its index is its place in the break list, the third.
    /// </remarks>
    [Test]
    public void Fill_ADecapitatedCorpse_ThrowsItsHeadBesideAHeadlessBody()
    {
        List<SceneProp> scene = [];
        SceneRagdoll decapitated = Corpse(SceneTeams.Blu) with { DamageCustom = Decapitation };

        RagdollProps.Fill([decapitated], 150d, Classes, scene, gibsOf: HeadedGibs, intervalPerTick: 0.015f).ShouldBe(2);

        Body(scene).Pose.Decapitated.ShouldBeTrue();
        Head(scene).EntityIndex.ShouldBe(RagdollProps.GibEntityIndex(decapitated, 2));
        Head(scene).Pose.RenderMode.ShouldBe(0, "another player's head is drawn");
    }

    /// <remarks>
    /// **The head outlives its body** (B746 review): a <c>C_PhysPropClientside</c> of its own, released by its ten-second
    /// <c>fadetime</c> plus a second, whatever ends the ragdoll entity. Tick 700 is 9 s after the death at 100, past the
    /// body's window ending at 200; tick 900 is 12 s, past the head's fade.
    /// </remarks>
    [Test]
    public void Fill_ADecapitatedCorpsePastItsBody_StillThrowsItsHeadUntilItFades()
    {
        SceneRagdoll decapitated = Corpse(SceneTeams.Blu) with { DamageCustom = Decapitation };
        List<SceneProp> during = [];
        List<SceneProp> after = [];

        RagdollProps.Fill([decapitated], 700d, Classes, during, gibsOf: HeadedGibs, intervalPerTick: 0.015f).ShouldBe(1);
        RagdollProps.Fill([decapitated], 900d, Classes, after, gibsOf: HeadedGibs, intervalPerTick: 0.015f).ShouldBe(0);

        during.ShouldHaveSingleItem().ClassName.ShouldBe(RagdollProps.HeadGibClassName);
    }

    /// <remarks>
    /// **The head is not the body's to fade** (B746 review): a body expired by <c>RagdollFade</c> takes its wearables with
    /// it, not its head. Seen at tick 150 (2.25 s), the body leaves view and goes 4.95 s later, by tick 490 (7.35 s); the
    /// head, 5.85 s into its ten, is still thrown.
    /// </remarks>
    [Test]
    public void Fill_ADecapitatedCorpseWhoseBodyHasFaded_StillThrowsItsHead()
    {
        SceneRagdoll decapitated = Corpse(SceneTeams.Blu) with { DamageCustom = Decapitation, LastTick = 100000 };
        RagdollFade fade = new(0.015f);
        HashSet<int> seen = [RagdollProps.FirstCorpseEntityIndex];

        RagdollProps.Fill([decapitated], 150d, Classes, [], fade, seen, gibsOf: HeadedGibs, intervalPerTick: 0.015f)
            .ShouldBe(2, "the control: the body and the head while seen");

        List<SceneProp> scene = [];
        RagdollProps.Fill([decapitated], 490d, Classes, scene, fade, new HashSet<int>(), gibsOf: HeadedGibs, intervalPerTick: 0.015f);

        scene.ShouldHaveSingleItem().ClassName.ShouldBe(RagdollProps.HeadGibClassName);
    }

    /// <remarks>
    /// **A death right after a feign death's gibs throws nothing** (B746 review): the feign death set
    /// <c>m_hFirstGib</c>, no respawn cleared it, so <c>CreateTFGibs</c>' guard <c>m_hFirstGib == NULL</c> fails
    /// (<c>c_tf_player.cpp:1102</c>). The feign death's pieces at tick 100 fade at 833; a real death at 300 throws none, one
    /// at 900 throws its three.
    /// </remarks>
    [Test]
    public void Fill_ADeathWhileAFeignDeathsPiecesLast_ThrowsNothing()
    {
        SceneRagdoll feigned = Corpse(SceneTeams.Blu) with { Gib = true, FeignDeath = true, PlayerIndex = 3, LastTick = 100000 };
        SceneRagdoll soon = feigned with { EntityIndex = 41, FirstTick = 300, FeignDeath = false };
        SceneRagdoll later = soon with { FirstTick = 900 };

        RagdollProps.Fill([feigned, soon], 310d, Classes, [], gibsOf: Gibs, intervalPerTick: 0.015f).ShouldBe(3, "the feign death's own");
        RagdollProps.Fill([feigned, later], 910d, Classes, [], gibsOf: Gibs, intervalPerTick: 0.015f).ShouldBe(3, "the control: the real death's");

        List<SceneProp> scene = [];
        RagdollProps.Fill([feigned, soon], 310d, Classes, scene, gibsOf: Gibs, intervalPerTick: 0.015f);
        scene.ShouldAllBe(piece => piece.SerialNumber == feigned.Serial && piece.EntityIndex < RagdollProps.GibEntityIndex(soon, 0));
    }

    /// <remarks>The guard is a feign death's alone: a respawn clears the handle between two real deaths (<c>:7992-7994</c>).</remarks>
    [Test]
    public void Fill_ADeathRightAfterARealGibbedDeath_ThrowsItsOwn()
    {
        SceneRagdoll first = Corpse(SceneTeams.Blu) with { Gib = true, PlayerIndex = 3, LastTick = 100000 };
        SceneRagdoll second = first with { EntityIndex = 41, FirstTick = 300 };

        RagdollProps.Fill([first, second], 310d, Classes, [], gibsOf: Gibs, intervalPerTick: 0.015f).ShouldBe(6);
    }

    /// <remarks>
    /// **A feign death that throws nothing leaves the handle where it was** (B746 re-review): a feign decapitation's head
    /// at 100, a plain feign death at 200, then a real gibbing at 300 while the head lasts. The head still holds
    /// <c>m_hFirstGib</c>, so the gibbing throws nothing. The control: the first feign death plain too, and the gibbing
    /// throws its three.
    /// </remarks>
    [Test]
    public void Fill_ADeathAfterAFeignHeadAndAPlainFeign_ThrowsNothing()
    {
        SceneRagdoll head = Corpse(SceneTeams.Blu) with
        {
            DamageCustom = Decapitation, FeignDeath = true, PlayerIndex = 3, LastTick = 100000,
        };
        SceneRagdoll plain = head with { EntityIndex = 41, FirstTick = 200, DamageCustom = null };
        SceneRagdoll real = plain with { EntityIndex = 42, FirstTick = 300, FeignDeath = false, Gib = true };

        Thrown([head, plain, real], real).ShouldBe(0);
        Thrown([head with { DamageCustom = null }, plain, real], real).ShouldBe(3, "the control: no feign death threw");
    }

    /// <remarks>
    /// **With no clock a feign death's pieces last its ragdoll entity's window** (B746 re-review), as every piece does:
    /// the feign gibbing ends at 250, so a death at 200 throws nothing and one at 300 throws.
    /// </remarks>
    [TestCase(200, 0, TestName = "Fill_WithNoClockADeathInsideAFeignGibbingsWindow_ThrowsNothing")]
    [TestCase(300, 3, TestName = "Fill_WithNoClockADeathAfterAFeignGibbingsWindow_ThrowsItsOwn")]
    public void Fill_WithNoClock_TheFeignGuardLastsTheFeignDeathsWindow(int death, int thrown)
    {
        SceneRagdoll feigned = Corpse(SceneTeams.Blu) with { Gib = true, FeignDeath = true, PlayerIndex = 3, LastTick = 250 };
        SceneRagdoll real = feigned with { EntityIndex = 41, FirstTick = death, LastTick = 100000, FeignDeath = false };

        Thrown([feigned, real], real, intervalPerTick: 0f).ShouldBe(thrown);
    }

    /// <remarks>With no clock a head lasts its ragdoll entity's window: past it, nothing.</remarks>
    [Test]
    public void Fill_WithNoClockPastTheRagdollsWindow_ThrowsNoHead()
    {
        SceneRagdoll decapitated = Corpse(SceneTeams.Blu) with { DamageCustom = Decapitation };

        RagdollProps.Fill([decapitated], 150d, Classes, [], gibsOf: HeadedGibs).ShouldBe(2, "the control: inside it");
        RagdollProps.Fill([decapitated], 250d, Classes, [], gibsOf: HeadedGibs).ShouldBe(0);
    }

    /// <remarks>Two of one player's corpses made on one tick are ordered by their place in the list.</remarks>
    [Test]
    public void Before_TwoCorpsesOnOneTick_IsTheOneListedFirst()
    {
        SceneRagdoll first = Corpse(SceneTeams.Blu) with { PlayerIndex = 3 };
        SceneRagdoll second = first with { EntityIndex = 41 };

        (RagdollProps.Before([first, second], 1, 3), RagdollProps.Before([first, second], 0, 3)).ShouldBe((0, null));
    }

    /// <summary>How many pieces one corpse throws at a tick ten after its death, among others.</summary>
    private static int Thrown(SceneRagdoll[] corpses, SceneRagdoll of, float intervalPerTick = 0.015f)
    {
        List<SceneProp> scene = [];
        RagdollProps.Fill(corpses, of.FirstTick + 10, Classes, scene, gibsOf: HeadedGibs, intervalPerTick: intervalPerTick);

        int first = RagdollProps.GibEntityIndex(of, 0);

        return scene.Count(prop => RagdollProps.IsGib(prop.ClassName) && prop.EntityIndex >= first && prop.EntityIndex < first + 32);
    }

    private static SceneProp Body(List<SceneProp> scene) => scene.Single(prop => prop.ClassName == RagdollProps.RagdollClassName);

    private static SceneProp Head(List<SceneProp> scene) => scene.Single(prop => prop.ClassName == RagdollProps.HeadGibClassName);

    /// <remarks>
    /// **A guitar smash compresses the corpse's neck, by its class** (B762): <c>IsHeadSmash</c> on
    /// <c>TF_DMG_CUSTOM_TAUNTATK_ENGINEER_GUITAR_SMASH</c> (<c>c_tf_player.cpp:1313-1316</c>), then
    /// <c>BuildNeckScaleTransformations( …, GetClass() )</c> (<c>:8842-8846</c>). A decapitation is the control: it takes the
    /// head off and leaves the neck.
    /// </remarks>
    [TestCase(GuitarSmash, 5, TestName = "Fill_AGuitarSmashedCorpse_CompressesItsClasssNeck")]
    [TestCase(Decapitation, null, TestName = "Fill_ADecapitatedCorpse_LeavesItsNeck")]
    public void Fill_ACorpse_CompressesItsNeckOnlyForAGuitarSmash(int damageCustom, int? headSmashClass)
    {
        List<SceneProp> scene = [];
        SceneRagdoll corpse = Corpse(SceneTeams.Blu) with { DamageCustom = damageCustom };

        RagdollProps.Fill([corpse], 150d, Classes, scene, gibsOf: HeadedGibs, intervalPerTick: 0.015f);

        Body(scene).Pose.HeadSmashClass.ShouldBe(headSmashClass);
    }

    /// <remarks>The control: an ordinary death keeps its head and throws nothing.</remarks>
    [Test]
    public void Fill_AnOrdinaryCorpse_KeepsItsHead()
    {
        List<SceneProp> scene = [];

        RagdollProps.Fill([Corpse(SceneTeams.Blu)], 150d, Classes, scene, gibsOf: HeadedGibs, intervalPerTick: 0.015f).ShouldBe(1);

        Body(scene).Pose.Decapitated.ShouldBeFalse();
    }

    /// <remarks>
    /// **The recorder's own head is not drawn** (B746): his death cam views from it and sets <c>EF_NODRAW</c> on it
    /// (<c>CalcDeathCamView</c>, <c>c_tf_player.cpp:7210</c>), never cleared. It is still thrown and simulated, so it is
    /// <c>kRenderNone</c>, which the draw lists drop, rather than absent.
    /// </remarks>
    [Test]
    public void Fill_TheRecordersOwnDecapitatedHead_IsNotDrawn()
    {
        List<SceneProp> scene = [];
        SceneRagdoll decapitated = Corpse(SceneTeams.Blu) with { DamageCustom = Decapitation, PlayerIndex = 3 };

        RagdollProps.Fill([decapitated], 150d, Classes, scene, gibsOf: HeadedGibs, intervalPerTick: 0.015f, recorder: 3);

        Head(scene).Pose.RenderMode.ShouldBe(RenderModes.None);
    }

    /// <remarks>
    /// **A headshot decapitation leaves no head view**: <c>CreateTFHeadGib</c> clears <c>m_hHeadGib</c> for
    /// <c>TF_DMG_CUSTOM_HEADSHOT_DECAPITATION</c> (<c>c_tf_player.cpp:1077-1081</c>), so the death cam never sets
    /// <c>EF_NODRAW</c> on it and the recorder sees his own head.
    /// </remarks>
    [Test]
    public void Fill_TheRecordersHeadshotDecapitation_DrawsHisHead()
    {
        List<SceneProp> scene = [];
        SceneRagdoll decapitated = Corpse(SceneTeams.Blu) with { DamageCustom = HeadshotDecapitation, PlayerIndex = 3 };

        RagdollProps.Fill([decapitated], 150d, Classes, scene, gibsOf: HeadedGibs, intervalPerTick: 0.015f, recorder: 3);

        Head(scene).Pose.RenderMode.ShouldBe(0);
    }

    private const int Decapitation = DamageCustoms.Decapitation;

    private const int GuitarSmash = DamageCustoms.GuitarSmash;

    private const int HeadshotDecapitation = DamageCustoms.HeadshotDecapitation;

    /// <summary>Three pieces, the third the medic's head; the robot's list carries both heads, the robot's second.</summary>
    private static IReadOnlyList<PhysicsBreakPiece> HeadedGibs(string model) =>
        model switch
        {
            "models/player/medic.mdl" =>
            [
                new PhysicsBreakPiece(@"player\gibs\medicgib001", 0f, 10f),
                new PhysicsBreakPiece(@"player\gibs\medicgib002", 0f, 10f),
                new PhysicsBreakPiece(@"player\gibs\medicgib007", 0f, 10f),
            ],
            RobotMedic =>
            [
                new PhysicsBreakPiece(@"bots\gibs\medicbot_gib_arm", 0f, 10f),
                new PhysicsBreakPiece(@"bots\gibs\medicbot_gib_head", 0f, 10f),
                new PhysicsBreakPiece(@"player\gibs\medicgib007", 0f, 10f),
            ],
            _ => [],
        };

    private const string RobotMedic = "models/bots/medic/bot_medic.mdl";

    /// <remarks>
    /// **A robot is drawn as its own model and throws its own head** (B763): `CreateTFRagdoll` takes the custom model
    /// (`c_tf_player.cpp:688-691`), and `CreatePlayerGibs` picks `g_pszBotHeadGibs` when `HasCustomModel()` from that
    /// model's list (`:7453-7476`). The robot's list here holds a human head too, third, so only the table decides.
    /// </remarks>
    [Test]
    public void Fill_ADecapitatedRobot_IsItsModelAndThrowsTheRobotsHead()
    {
        List<SceneProp> scene = [];
        SceneRagdoll robot = Corpse(SceneTeams.Blu) with { DamageCustom = Decapitation, CustomModel = RobotMedic };

        RagdollProps.Fill([robot], 150d, Classes, scene, gibsOf: HeadedGibs, intervalPerTick: 0.015f).ShouldBe(2);

        Body(scene).ModelPath.ShouldBe(RobotMedic);
        Head(scene).EntityIndex.ShouldBe(RagdollProps.GibEntityIndex(robot, 1));
    }

    /// <remarks>
    /// **Drawn as his disguise, a robot still breaks as a robot**: `CreateTFRagdoll` gives the body `m_iClass`'s model
    /// (`c_tf_player.cpp:688-699`), while `CreatePlayerGibs` takes the custom model and `g_pszBotHeadGibs` on
    /// `HasCustomModel()` alone (`:7453-7476`). The human head is in the human list, third, so a body-model lookup throws
    /// it instead.
    /// </remarks>
    [Test]
    public void Fill_ADecapitatedRobotDrawnAsHisDisguise_IsTheClasssBodyAndTheRobotsHead()
    {
        List<SceneProp> scene = [];
        SceneRagdoll robot = Corpse(SceneTeams.Blu) with { DamageCustom = Decapitation, CustomModel = RobotMedic, DrawnAsDisguise = true };

        RagdollProps.Fill([robot], 150d, Classes, scene, gibsOf: HeadedGibs, intervalPerTick: 0.015f).ShouldBe(2);

        Body(scene).ModelPath.ShouldBe("models/player/medic.mdl");
        Head(scene).EntityIndex.ShouldBe(RagdollProps.GibEntityIndex(robot, 1));
    }

    /// <remarks>
    /// **A non-custom player breaks his drawn model** (B766): `CreatePlayerGibs` breaks `GetModelIndex()`
    /// (`c_tf_player.cpp:7453-7454`), which `ValidateModelIndex` set to his disguise's class to an enemy, or to the ghost
    /// (`:8999-9014`). A medic drawn as a soldier throws the soldier's pieces; a BLU ghost the BLU ghost's (none, here, as
    /// the stand-in list has none); with neither he throws his own, the control.
    /// </remarks>
    [TestCase(3, false, SceneTeams.Blu, null, "soldiergib", "medicgib", TestName = "Fill_AGibbedPlayerDrawnAsADisguise_ThrowsTheDisguisesPieces")]
    [TestCase(null, true, SceneTeams.Blu, null, "blughost", "redghost", TestName = "Fill_AGibbedBluGhost_ThrowsTheBluGhostsPieces")]
    [TestCase(null, true, SceneTeams.Red, null, "redghost", "blughost", TestName = "Fill_AGibbedRedGhost_ThrowsTheRedGhostsPieces")]
    [TestCase(3, true, SceneTeams.Blu, null, "soldiergib", "blughost", TestName = "Fill_AGibbedGhostDisguisedToAnEnemy_ThrowsTheDisguisesPiecesFirst")]
    [TestCase(3, false, SceneTeams.Blu, RobotMedic, "robotgib", "soldiergib", TestName = "Fill_AGibbedDisguisedRobot_ThrowsTheRobotsPieces")]
    [TestCase(null, false, SceneTeams.Blu, null, "medicgib", "soldiergib", TestName = "Fill_AGibbedPlayerDrawnAsHimself_ThrowsHisOwn")]
    public void Fill_AGibbedPlayer_ThrowsTheModelHeWasDrawnAs(
        int? disguiseClass, bool ghost, int team, string? custom, string piece, string notPiece)
    {
        List<SceneProp> scene = [];
        SceneRagdoll corpse = Corpse(team) with
        {
            Gib = true, GibDisguiseClass = disguiseClass, GibGhost = ghost, CustomModel = custom,
        };

        RagdollProps.Fill([corpse], 150d, DisguiseClasses, scene, gibsOf: DrawnGibs, intervalPerTick: 0.015f);

        scene.ShouldContain(prop => prop.ModelPath.Contains(piece, StringComparison.Ordinal));
        scene.ShouldNotContain(prop => prop.ModelPath.Contains(notPiece, StringComparison.Ordinal));
    }

    /// <remarks>
    /// **A disguise's class with no model breaks into nothing** (`GetPlayerClassData( TF_CLASS_UNDEFINED )` names no model):
    /// class 0, never sent, throws none of the body's pieces.
    /// </remarks>
    [Test]
    public void Fill_AGibbedPlayerDisguisedAsNoClass_ThrowsNothing()
    {
        List<SceneProp> scene = [];

        RagdollProps.Fill(
            [Corpse(SceneTeams.Blu) with { Gib = true, GibDisguiseClass = 0 }], 150d, DisguiseClasses, scene,
            gibsOf: DrawnGibs, intervalPerTick: 0.015f).ShouldBe(0);

        scene.ShouldBeEmpty();
    }

    /// <remarks>
    /// **A disguised spy's head is looked for by his own class in the disguise's list** (`c_tf_player.cpp:7466-7486`):
    /// the soldier's list holds no medic head, so a decapitated medic drawn as a soldier throws no head. Undisguised, he
    /// throws the medic's, the control.
    /// </remarks>
    [TestCase(3, 1, TestName = "Fill_ADecapitatedPlayerDrawnAsADisguise_ThrowsNoHead")]
    [TestCase(null, 2, TestName = "Fill_ADecapitatedPlayerDrawnAsHimself_ThrowsHisHead")]
    public void Fill_ADecapitation_SearchesTheDrawnModelsListForHisOwnHead(int? disguiseClass, int drawn)
    {
        List<SceneProp> scene = [];
        SceneRagdoll corpse = Corpse(SceneTeams.Blu) with { DamageCustom = Decapitation, GibDisguiseClass = disguiseClass };

        RagdollProps.Fill([corpse], 150d, DisguiseClasses, scene, gibsOf: HeadedGibs, intervalPerTick: 0.015f).ShouldBe(drawn);
    }

    /// <summary>The medic, and the soldier a disguise names.</summary>
    private static string? DisguiseClasses(int playerClass) =>
        playerClass == 3 ? "models/player/soldier.mdl" : Classes(playerClass);

    /// <summary>One stand-in piece for each model a player can be drawn as.</summary>
    private static IReadOnlyList<PhysicsBreakPiece> DrawnGibs(string model) => model switch
    {
        "models/player/soldier.mdl" => [new PhysicsBreakPiece(@"player\gibs\soldiergib001", 0f, 10f)],
        PlayerProps.GhostBlu => [new PhysicsBreakPiece(@"props_halloween\blughost_gib", 0f, 10f)],
        PlayerProps.GhostRed => [new PhysicsBreakPiece(@"props_halloween\redghost_gib", 0f, 10f)],
        RobotMedic => [new PhysicsBreakPiece(@"bots\gibs\robotgib001", 0f, 10f)],
        Disguise.DispenserModel => [new PhysicsBreakPiece(@"buildables\gibs\dispenser_gib1", 0f, 10f)],
        _ => Gibs(model),
    };

    /// <remarks>
    /// **The dispenser is `ValidateModelIndex`'s first branch** (`c_tf_player.cpp:8990-8998`, B766), so a spy gibbed while
    /// drawn as it throws the dispenser's pieces ahead of his disguise's and his ghost's; `CreatePlayerGibs` takes a custom
    /// model ahead of all (`:7453-7454`). The same corpse not drawn as the dispenser throws his disguise's, the control.
    /// </remarks>
    [TestCase(true, 3, false, null, "dispenser_gib", "soldiergib", TestName = "Fill_AGibbedSpyDrawnAsTheDispenser_ThrowsTheDispensersPieces")]
    [TestCase(true, null, true, null, "dispenser_gib", "blughost", TestName = "Fill_AGibbedGhostDrawnAsTheDispenser_ThrowsTheDispensersPieces")]
    [TestCase(true, 3, false, RobotMedic, "robotgib", "dispenser_gib", TestName = "Fill_AGibbedRobotDrawnAsTheDispenser_ThrowsTheRobotsPieces")]
    [TestCase(false, 3, false, null, "soldiergib", "dispenser_gib", TestName = "Fill_AGibbedSpyNotDrawnAsTheDispenser_ThrowsHisDisguisesPieces")]
    public void Fill_ACorpseCapturedAsTheDispenser_ThrowsTheDrawnModelsPieces(
        bool dispenser, int? disguiseClass, bool ghost, string? custom, string piece, string notPiece)
    {
        List<SceneProp> scene = [];
        SceneRagdoll corpse = Corpse(SceneTeams.Blu) with
        {
            Gib = true, GibDispenser = dispenser, GibDisguiseClass = disguiseClass, GibGhost = ghost, CustomModel = custom,
        };

        RagdollProps.Fill([corpse], 150d, DisguiseClasses, scene, gibsOf: DrawnGibs, intervalPerTick: 0.015f);

        scene.ShouldContain(prop => prop.ModelPath.Contains(piece, StringComparison.Ordinal));
        scene.ShouldNotContain(prop => prop.ModelPath.Contains(notPiece, StringComparison.Ordinal));
    }

    /// <summary>Three pieces, standing in for a class model's nine.</summary>
    private static IReadOnlyList<PhysicsBreakPiece> Gibs(string model) =>
        model == "models/player/medic.mdl"
            ?
            [
                new PhysicsBreakPiece(@"player\gibs\medicgib001", 0f, 10f),
                new PhysicsBreakPiece(@"player\gibs\medicgib002", 0f, 10f),
                new PhysicsBreakPiece(@"player\gibs\random_organ", 0f, 10f),
            ]
            : [];

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
