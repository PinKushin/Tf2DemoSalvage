using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// Whose eyes the first-person view uses, and which of the two mechanisms answers.
/// </summary>
/// <remarks>
/// **This was <c>MainForm.FollowedEntity</c>, <c>Spectated</c>, <c>FirstPersonCamera</c>,
/// <c>PlayerAt</c> and <c>Ducking</c>** (B188, D90), so none of it had a test — reaching it meant an
/// STA thread, a device and the desktop lock. The only thing it wanted from a window was the
/// viewport's aspect ratio.
///
/// **Valve computes a view on the PLAYER, dispatching on observer mode** —
/// <c>C_BasePlayer::CalcView</c> (<c>c_baseplayer.h:112</c>) to <c>CalcObserverView</c> (<c>:455</c>)
/// to <c>CalcInEyeCamView</c>/<c>CalcChaseCamView</c>/<c>CalcRoamingView</c> (<c>:463</c>). Not in
/// the window there either.
///
/// **Every case here is a PAIR of demo kinds**, because the two mechanisms are the whole subject: a
/// POV demo carries a recorded camera and an STV demo does not, and a test using only one cannot
/// tell "picked the right mechanism" from "only ever does one thing".
/// </remarks>
public sealed class SpectatorViewTests
{
    [Test]
    public void Followed_OnAPovDemo_IsTheRecorder()
    {
        // A point-of-view demo carries the camera the recording client computed, so the entity to
        // hide is the one that did the recording — whatever the roster says.
        SpectatorView view = View(Eyes(recorder: 7, recorded: true, players: [Player(3), Player(7)]));

        view.Followed(tick: 100).ShouldBe(7);
    }

    [Test]
    public void Followed_OnASourceTvDemo_IsTheChosenPlayer()
    {
        // **The control, and the case that found a real bug.** An STV recording carries no camera,
        // so a target has to be chosen — and taking the first player in the list took the SourceTV
        // camera entity instead (docs/findings/29).
        SpectatorView view = View(Eyes(recorder: null, recorded: false, players: [Player(3)]));

        view.Followed(tick: 100).ShouldBe(3);
    }

    [Test]
    public void Followed_WithAnOverride_IsTheNamedEntity()
    {
        // `--spectate 4`, which is how a capture is aimed at one player.
        SpectatorView view = View(Eyes(recorder: null, recorded: false, players: [Player(3), Player(4)]));

        view.Spectating = 4;

        view.Followed(tick: 100).ShouldBe(4);
    }

    [Test]
    public void Followed_WithAnOverrideNobodyMatches_FallsBackAndSaysSo()
    {
        // **Falls back rather than failing.** A spy is dead, in the lobby, or another class for most
        // of a match, and a viewer that went black for those stretches would be worse than one that
        // shows somebody. It says so, because "I asked for entity 11 and got somebody else" is
        // exactly the kind of thing that reads as a decode bug.
        RecordingLogger log = new();
        SpectatorView view = new(log)
        {
            Eyes = Eyes(recorder: null, recorded: false, players: [Player(3)]),
            Spectating = 11,
        };

        view.Followed(tick: 100).ShouldBe(3);
        log.Count("--spectate 11 is not playing").ShouldBe(1);
    }

    [Test]
    public void Followed_WithNoDemoLoaded_IsNull()
    {
        // Every frame of a freshly opened viewer.
        new SpectatorView(new RecordingLogger()).Followed(tick: 0).ShouldBeNull();
    }

    [Test]
    public void Eye_OnAPovDemo_UsesTheRecordedCamera()
    {
        // **Used as it stands, because it already accounts for death, spectating and every observer
        // mode.** Rebuilding it from the recorder's entity would be right while they lived and wrong
        // for the rest — measured, the two part company by 169 units the moment the recorder dies.
        SpectatorView view = View(
            Eyes(recorder: 7, recorded: true, players: [Player(7, x: 500f)], viewX: 100f));

        FreeCamera eye = view.Eye(tick: 100, aspect: 16f / 9f).ShouldNotBeNull();

        eye.Origin.X.ShouldBe(100f);
    }

    [Test]
    public void Eye_OnASourceTvDemo_UsesTheSpectatedPlayersOwnPosition()
    {
        // **The control for the pair**, and it is what shows the two mechanisms are actually
        // different: same tick, same aspect, and the camera comes from the player rather than from a
        // recorded view that does not exist.
        SpectatorView view = View(
            Eyes(recorder: null, recorded: false, players: [Player(3, x: 500f)]));

        FreeCamera eye = view.Eye(tick: 100, aspect: 16f / 9f).ShouldNotBeNull();

        eye.Origin.X.ShouldBe(500f);
    }

    [Test]
    public void Eye_ForACrouchingPlayer_IsLowerThanForAStandingOne()
    {
        // `FL_DUCKING` on `m_fFlags` lowers the eye by more than a foot. Two players who differ only
        // in that flag, because one height on its own cannot say whether the flag was read.
        SpectatorView standing = View(Eyes(recorder: null, recorded: false, players: [Player(3)]));
        SpectatorView ducking = View(
            Eyes(
                recorder: null,
                recorded: false,
                players: [Player(3) with { Flags = PlayerActivityState.Ducking }]));

        float up = standing.Eye(100, 1.6f).ShouldNotBeNull().Origin.Z;
        float down = ducking.Eye(100, 1.6f).ShouldNotBeNull().Origin.Z;

        down.ShouldBeLessThan(up);
    }

    [Test]
    public void Eye_WithNobodyToFollow_IsNull()
    {
        // An STV demo seeked to a tick before anyone spawned. Null rather than a camera at the world
        // origin, which would look like a rendering fault rather than the end of the material.
        View(Eyes(recorder: null, recorded: false, players: [])).Eye(100, 1.6f).ShouldBeNull();
    }

    [Test]
    public void Constructor_WithNoLogger_Refuses()
    {
        Should.Throw<ArgumentNullException>(() => new SpectatorView(spectate: null!));
    }

    private static SpectatorView View(IEyeSource eyes) =>
        new(new RecordingLogger()) { Eyes = eyes };

    private static Demo Eyes(
        int? recorder, bool recorded, IReadOnlyList<ScenePlayer> players, float viewX = 0f) =>
        new(recorder, recorded, players, viewX);

    private static ScenePlayer Player(int entity, float x = 0f) =>
        new(
            EntityIndex: entity,
            X: x,
            Y: 0f,
            Z: 0f,
            Team: SceneTeams.Red,
            Health: 125,
            PlayerClass: 3);

    /// <summary>A stand-in demo, which is three members rather than a file.</summary>
    private sealed class Demo(
        int? recorder, bool recorded, IReadOnlyList<ScenePlayer> players, float viewX) : IEyeSource
    {
        public int? RecorderEntityIndex => recorder;

        public RecordedView? RecordedViewAt(int tick) =>
            recorded
                ? new RecordedView((viewX, 0f, 0f), (0f, 0f, 0f), IsCut: false)
                : null;

        public IReadOnlyList<ScenePlayer> PlayersAt(int tick) => players;
    }
}
