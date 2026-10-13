using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Tests;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>What the HUD reads of the local player: <see cref="HudStates.From"/>.</summary>
/// <remarks>`GetMaxBuffedHealth` (tf_player_shared.cpp:2235): the buffing base × 1.5, floored to a multiple of 5.</remarks>
public sealed class HudStatesTests
{
    [TestCase(175, 260)] // 262.5 → 260
    [TestCase(125, 185)] // 187.5 → 185
    [TestCase(300, 450)]
    public void From_TheBuffingBase_IsBoostedAndFlooredToAFive(int buffing, int expected) =>
        HudStates.From(Player() with { MaxHealthForBuffing = buffing }, 0).MaxBuffedHealth.ShouldBe(expected);

    [Test]
    public void From_TheHealth_IsTheEntitysNotTheResources()
    {
        HudState state = HudStates.From(Player() with { EntityHealth = 88, MaxHealth = 175, HideHud = 8 }, 66);

        (state.Health, state.MaxHealth, state.HideHud, state.Alive).ShouldBe((88, 175, 8, true));
        state.CurTime.ShouldBe((float)(66 * ScenePropTrack.Tf2TickInterval));
    }

    [Test]
    public void From_ADeadLifeState_IsNotAlive() =>
        HudStates.From(Player() with { LifeState = 2 }, 0).Alive.ShouldBeFalse();

    [Test]
    public void From_TheFlags_AreThePlayersMFFlags() =>
        HudStates.From(Player() with { Flags = (1 << 6) | 1 }, 0).Flags.ShouldBe(65, "FL_FROZEN | FL_ONGROUND (const.h:148, :158)");

    /// <remarks>
    /// A HUD element that places itself on a player asks his `GetAbsOrigin()` — `CFloatingHealthIcon::CalculatePosition`
    /// (tf_hud_target_id.cpp:1520) — which is where the frame drew him, an interpolation delay behind the newest
    /// recorded frame. His health is not interpolated, so only the position is taken from the drawn sample (B531).
    /// </remarks>
    [Test]
    public void WithDrawnPositions_APlayerTheFrameDrew_TakesHisDrawnPositionAndKeepsHisNewestState()
    {
        HudState state = default(HudState) with
        {
            Players = [Player() with { X = 100f, Health = 50 }, Player() with { EntityIndex = 2, X = 7f }],
        };

        HudState placed = HudStates.WithDrawnPositions(state, [Player() with { X = 60f, Y = 5f, Z = 9f, Health = 80 }]);

        placed.Players.ShouldBe(
            [Player() with { X = 60f, Y = 5f, Z = 9f, Health = 50 }, Player() with { EntityIndex = 2, X = 7f }]);
    }

    [Test]
    public void WithDrawnPositions_NoDrawnSample_ChangesNothing()
    {
        HudState state = default(HudState) with { Players = [Player() with { X = 100f }] };

        HudStates.WithDrawnPositions(state, null).Players.ShouldBeSameAs(state.Players);
    }

    private static readonly DemoTimeline NoRecordedView = SyntheticEffects.TimelineWrittenWith(PlayerFlagLayout.Current);

    private static readonly DemoTimeline Pov = DemoTimeline.Build(SyntheticPlayer.DemoWithRecordedViews(
        (0, (0f, 0f, 64f)), (200, (0f, 0f, 64f))));

    private static HudView View(DemoTimeline timeline, CameraMode mode = CameraMode.Free, int? followed = null) =>
        new(timeline, 100, null, null, null, static name => name == "cl_hud" ? "1" : null, 12.5f, 106f, mode, followed);

    /// <remarks>
    /// What the window adds to a tick's HUD state: its own clock (`gpGlobals->realtime`), the local player's `GetFOV()`,
    /// `engine->WorldToScreenMatrix()` and the client's convars — as it left the view (D214).
    /// </remarks>
    [Test]
    public void At_ATick_CarriesTheWindowsClockFovProjectionAndClientConVars()
    {
        float[] projection = [1f, 2f, 3f];
        HudState state = HudStates.At(View(NoRecordedView), null, projection);

        state.InGame.ShouldBeTrue("the control: a demo is open");
        state.RealTime.ShouldBe(12.5f);
        state.Fov.ShouldBe(106f);
        state.WorldToScreen.ShouldBeSameAs(projection);
        state.ConVars.Client.ShouldNotBeNull()("cl_hud").ShouldBe("1");
    }

    /// <remarks>
    /// On SourceTV the local player's observer mode and target are the HLTV camera's (`C_BasePlayer::GetObserverMode`,
    /// c_baseplayer.cpp:645): this viewer's camera — first person is in-eye, the shoulder camera chase, free roaming.
    /// </remarks>
    [TestCase(CameraMode.FirstPerson, ObserverModes.InEye, 7)]
    [TestCase(CameraMode.ThirdPerson, ObserverModes.Chase, 7)]
    [TestCase(CameraMode.Free, ObserverModes.Roaming, 0)]
    public void At_ADemoWithNoRecordedView_TakesItsObserverModeAndTargetFromTheCamera(CameraMode mode, int observerMode, int target)
    {
        HudState state = HudStates.At(View(NoRecordedView, mode, followed: 7), null, []);

        state.ObserverMode.ShouldBe(observerMode);
        state.ObserverTarget.ShouldBe(target, "a target only while the camera follows somebody");
    }

    [Test]
    public void At_ACameraFollowingNobodyYet_HasNoObserverTarget() =>
        HudStates.At(View(NoRecordedView, CameraMode.FirstPerson, followed: null), null, []).ObserverTarget.ShouldBe(0);

    [Test]
    public void At_APointOfViewDemo_LeavesTheObserverModeTheDemoGave()
    {
        Pov.HasRecordedView.ShouldBeTrue("the control");

        HudState state = HudStates.At(View(Pov, CameraMode.ThirdPerson, followed: 7), null, []);

        state.ObserverMode.ShouldBe(HudStates.For(Pov, 100).ObserverMode);
        state.ObserverTarget.ShouldBe(HudStates.For(Pov, 100).ObserverTarget);
    }

    [Test]
    public void At_PlayersTheFrameDrew_ArePlacedWhereItDrewThem()
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticEffects.Demo(
            [new(1, SyntheticEffects.PlayerClass, SyntheticEffects.Standing(100f, 0f, 0f, 2))],
            [],
            [],
            [],
            new SyntheticEffects.Effect(300, "CTETFBlood", new System.Collections.Generic.Dictionary<string, Core.Schema.PropertyValue>(System.StringComparer.Ordinal))));
        ScenePlayer recorded = timeline.PlayersAt(100).ShouldHaveSingleItem();

        HudState state = HudStates.At(View(timeline), [recorded with { X = 60f }], []);

        state.Players.ShouldNotBeNull().ShouldHaveSingleItem().X.ShouldBe(60f);
    }

    private static ScenePlayer Player() => new(1, 0f, 0f, 0f, 2, Health: 99, PlayerClass: 3);
}
