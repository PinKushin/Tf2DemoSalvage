using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Tests;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// The owner of the camera's mode (D214) — `C_HLTVCamera`'s mode beside its target (`hltvcamera.cpp`), and the D128
/// lock on a point-of-view recording — over demos a test wrote and a window that writes down what it was asked.
/// </summary>
/// <remarks>
/// **Two demos, because the camera's rules differ by kind**: a SourceTV demo of two players, one of whom dies, where
/// every mode can be asked for; and a point-of-view demo, which has the recorder's view and takes no other (D188).
///
/// **Not here:** a wheel notch or a drag, in either direction. Both ask first whether a map with an outline is
/// drawn, and the map a test can write has no faces, so the moving half cannot be reached; the refusing half is a
/// guard the code does not compile without, and a test of it cannot fail. The UI suite drives both.
/// </remarks>
public sealed class CameraPresenterTests
{
    private const int First = 1;
    private const int Second = 2;
    private const int Alive = 0;
    private const int Dead = 2;
    private const int BothAlive = 20;
    private const int FirstDead = 60;

    private static DemoTimeline SourceTv() => DemoTimeline.Build(SyntheticPlayer.DemoOfPlayersOverTicks(
        0.015f,
        (10, [(First, 100f, Alive), (Second, 400f, Alive)]),
        (40, [(First, 110f, Dead), (Second, 400f, Alive)]),
        (80, [(First, 110f, Dead), (Second, 410f, Alive)])));

    private static DemoTimeline Pov() => DemoTimeline.Build(SyntheticPlayer.DemoWithRecordedViews(
        (0, (0f, 0f, 64f)), (200, (0f, 0f, 64f))));

    private static (CameraPresenter Camera, FakeLoadView Window, SpectatorView Spectator) Camera(DemoTimeline? timeline, int tick = BothAlive)
    {
        FakeLoadView window = new() { Window = new CameraWindow(tick, tick, 1280, 720, false) };
        SpectatorView spectator = new(NullLogger.Instance)
        {
            Eyes = timeline is null ? null : new TimelineEyes(timeline, new DemoPlayer(timeline)),
        };

        CameraPresenter camera = new(
            window, spectator, NullLoggerFactory.Instance, () => window.Window, () => timeline, static () => null, static () => new ViewerSettings());

        return (camera, window, spectator);
    }

    [Test]
    public void Mode_OfANewCamera_IsFreeAndFollowsNobody()
    {
        (CameraPresenter camera, FakeLoadView window, _) = Camera(SourceTv());

        camera.Mode.ShouldBe(CameraMode.Free);
        camera.FreeLook.ShouldBeTrue();
        camera.FirstPerson.ShouldBeFalse();
        camera.EffectiveMode.ShouldBe(CameraMode.Free);
        window.Names.ShouldBeEmpty("asking changes nothing");
    }

    [Test]
    public void EnterCamera_WithNobodyToFollow_SaysWhyAndLeavesTheModeAlone()
    {
        (CameraPresenter camera, FakeLoadView window, _) = Camera(timeline: null);

        camera.EnterCamera(CameraMode.ThirdPerson).ShouldBeTrue("the key was the camera's whether or not it could be entered");

        camera.Mode.ShouldBe(CameraMode.Free);
        window.Names.ShouldBe(["Say: No first-person view here: nothing to follow at this tick."]);
    }

    [Test]
    public void EnterCamera_ThirdPersonOnASourceTvDemo_ChasesAndTellsTheWindowOnce()
    {
        (CameraPresenter camera, FakeLoadView window, _) = Camera(SourceTv());

        camera.EnterCamera(CameraMode.ThirdPerson).ShouldBeTrue();

        camera.Mode.ShouldBe(CameraMode.ThirdPerson);
        camera.EffectiveMode.ShouldBe(CameraMode.ThirdPerson);
        camera.FirstPerson.ShouldBeFalse("the viewmodel and the hidden player are first person's alone");
        camera.FollowedEntity().ShouldNotBeNull();
        window.Names.ShouldBe([nameof(ICameraView.CameraChanged)]);
    }

    [Test]
    public void CycleCameraMode_FromFree_GoesThroughTheEyesThenTheChaseThenBack()
    {
        (CameraPresenter camera, FakeLoadView window, _) = Camera(SourceTv());

        camera.CycleCameraMode().ShouldBeTrue();
        camera.Mode.ShouldBe(CameraMode.FirstPerson);

        camera.CycleCameraMode().ShouldBeTrue();
        camera.Mode.ShouldBe(CameraMode.ThirdPerson);

        camera.CycleCameraMode().ShouldBeTrue();
        camera.Mode.ShouldBe(CameraMode.Free);

        window.Names.Count(static name => name == nameof(ICameraView.CameraChanged)).ShouldBe(3);
    }

    [Test]
    public void ToggleFirstPerson_Twice_EntersTheEyesAndLeavesToTheFreeCamera()
    {
        (CameraPresenter camera, FakeLoadView window, _) = Camera(SourceTv());

        camera.ToggleFirstPerson().ShouldBeTrue();
        camera.Mode.ShouldBe(CameraMode.FirstPerson);
        camera.FirstPerson.ShouldBeTrue();

        camera.ToggleFirstPerson().ShouldBeTrue();
        camera.Mode.ShouldBe(CameraMode.Free);
        window.Names.ShouldBe([nameof(ICameraView.CameraChanged), nameof(ICameraView.CameraChanged)]);
    }

    [Test]
    public void EffectiveMode_InTheEyesOfADeadTarget_IsTheChaseWhileTheModeAskedForStays()
    {
        (CameraPresenter camera, FakeLoadView window, SpectatorView spectator) = Camera(SourceTv());
        spectator.Spectating = First;
        camera.ToggleFirstPerson();

        camera.EffectiveMode.ShouldBe(CameraMode.FirstPerson, "the control: alive at this tick");

        window.Window = new CameraWindow(FirstDead, FirstDead, 1280, 720, false);

        camera.Mode.ShouldBe(CameraMode.FirstPerson);
        camera.EffectiveMode.ShouldBe(CameraMode.ThirdPerson, "CalcInEyeCamView hands a dead target to the chase camera");
        camera.FirstPerson.ShouldBeFalse();

        // Leaving asks the mode that was ASKED FOR: a dead target must not make the key enter a mode already on.
        camera.ToggleFirstPerson();
        camera.Mode.ShouldBe(CameraMode.Free);
    }

    [Test]
    public void DemoOpening_OfAPointOfViewDemo_OpensOnTheRecordersCamera()
    {
        (CameraPresenter camera, FakeLoadView window, _) = Camera(Pov());

        camera.DemoOpening(spectateWho: null);

        camera.Mode.ShouldBe(CameraMode.FirstPerson);
        window.Names.ShouldBeEmpty("opening on it is not a change the window is told of; the load shows the demo next");
    }

    /// <remarks>
    /// **The headless instrument is exempt from the POV lock** (B378), as at `RefusedByTheDemo` and `DemoOpened`: a
    /// placed `TF2VIEW_CAMERA` is a measurement. Without the exemption here a POV capture logged the placed camera and
    /// photographed the recorder's eyes. The test above is the control: same demo, no variable, first person.
    /// </remarks>
    [Test]
    [NonParallelizable]
    public void DemoOpening_OfAPointOfViewDemoWithAPlacedCamera_KeepsTheFreeCamera()
    {
        string? before = System.Environment.GetEnvironmentVariable(FreeCameraController.CameraVariable);

        try
        {
            System.Environment.SetEnvironmentVariable(FreeCameraController.CameraVariable, "0 -152 600 -35 235");

            (CameraPresenter camera, _, _) = Camera(Pov());

            camera.DemoOpening(spectateWho: null);

            camera.Mode.ShouldBe(CameraMode.Free);
        }
        finally
        {
            System.Environment.SetEnvironmentVariable(FreeCameraController.CameraVariable, before);
        }
    }

    [Test]
    public void DemoOpening_OfASourceTvDemo_KeepsTheFreeCamera()
    {
        (CameraPresenter camera, _, _) = Camera(SourceTv());

        camera.DemoOpening(spectateWho: null);

        camera.Mode.ShouldBe(CameraMode.Free, "a SourceTV recording has no recorder: the choice of camera is real");
    }

    [Test]
    public void DemoOpening_WithAPlayerNamedByEntity_FollowsThemInFirstPerson()
    {
        (CameraPresenter camera, FakeLoadView window, SpectatorView spectator) = Camera(SourceTv());

        camera.DemoOpening(spectateWho: "2");

        spectator.Spectating.ShouldBe(Second);
        camera.Mode.ShouldBe(CameraMode.FirstPerson, "naming somebody is asking to watch them");
        window.Names.ShouldBeEmpty();
    }

    [Test]
    public void DemoOpening_WithANameNobodyHas_EndsAHeadlessRunAndChoosesNormally()
    {
        (CameraPresenter camera, FakeLoadView window, SpectatorView spectator) = Camera(SourceTv());

        camera.DemoOpening(spectateWho: "nobody by this name");

        spectator.Spectating.ShouldBeNull();
        camera.Mode.ShouldBe(CameraMode.Free);
        window.Names.ShouldBe([nameof(ICameraView.FailHeadless)]);
    }

    [Test]
    public void EnterCamera_TheFreeCameraOnAPointOfViewDemo_IsRefusedAndSaysSo()
    {
        (CameraPresenter camera, FakeLoadView window, _) = Camera(Pov());
        camera.DemoOpening(spectateWho: null);

        camera.EnterCamera(CameraMode.Free).ShouldBeTrue();
        camera.EnterCamera(CameraMode.ThirdPerson).ShouldBeTrue();
        camera.ToggleFirstPerson().ShouldBeTrue();
        camera.ResetCamera().ShouldBeTrue();

        camera.Mode.ShouldBe(CameraMode.FirstPerson, "a point-of-view demo takes no manual camera (D128, D188)");
        window.Names.ShouldBe(Enumerable.Repeat("Say: POV demo: the camera is the recorder's own.", 4));
    }

    [Test]
    public void DemoOpened_OfAPointOfViewDemoNotYetInItsEyes_EntersThem()
    {
        (CameraPresenter camera, FakeLoadView window, _) = Camera(Pov());

        camera.Mode.ShouldBe(CameraMode.Free, "the control: nothing told the camera a demo was opening");

        camera.DemoOpened();

        camera.Mode.ShouldBe(CameraMode.FirstPerson);
        window.Names.ShouldBe([nameof(ICameraView.CameraChanged)]);
    }

    [Test]
    public void DemoOpened_OfASourceTvDemo_ChangesNothing()
    {
        (CameraPresenter camera, FakeLoadView window, _) = Camera(SourceTv());

        camera.DemoOpened();

        camera.Mode.ShouldBe(CameraMode.Free);
        window.Names.ShouldBeEmpty();
    }

    [Test]
    public void ResetCamera_FromTheChase_ReturnsToAnUnplacedFreeCameraAndLetsGoOfHeldKeys()
    {
        (CameraPresenter camera, FakeLoadView window, _) = Camera(SourceTv());
        camera.EnterCamera(CameraMode.ThirdPerson);
        camera.Free.Origin = (1f, 2f, 3f);

        camera.ResetCamera().ShouldBeTrue();

        camera.Mode.ShouldBe(CameraMode.Free);
        camera.Free.Origin.ShouldBeNull("the overhead placement is computed when the camera is next asked for");
        window.Names.ShouldBe(
            [nameof(ICameraView.CameraChanged), nameof(ICameraView.ReleaseHeldKeys), nameof(ICameraView.CameraChanged)],
            "the keys are let go before the window is told, so a held key cannot move the re-placed camera");
    }

    [Test]
    public void Fly_OutsideTheFreeCamera_NeverReadsTheInput()
    {
        (CameraPresenter camera, _, _) = Camera(SourceTv());
        camera.EnterCamera(CameraMode.ThirdPerson);
        int read = 0;

        bool moved = camera.Fly(
            () =>
            {
                read++;

                return default;
            },
            seconds: 0.016);

        moved.ShouldBeFalse();
        read.ShouldBe(0, "reading a button consumes its impulse bits; a chase frame must not spend them");
    }

    [Test]
    public void Fly_InTheFreeCamera_ReadsTheInputExactlyOnce()
    {
        (CameraPresenter camera, _, _) = Camera(SourceTv());
        int read = 0;

        camera.Fly(
            () =>
            {
                read++;

                return default;
            },
            seconds: 0.016);

        read.ShouldBe(1);
    }

    [Test]
    public void CycleTarget_InTheFreeCamera_DoesNothing()
    {
        (CameraPresenter camera, FakeLoadView window, SpectatorView spectator) = Camera(SourceTv());
        spectator.Spectating = First;

        camera.CycleTarget(reverse: false);

        spectator.Spectating.ShouldBe(First, "in the free camera the button is the look-around drag (D135)");
        window.Names.ShouldBeEmpty();
    }

    [Test]
    public void CycleTarget_WhileChasing_FollowsTheNextPlayerAndTellsTheWindow()
    {
        (CameraPresenter camera, FakeLoadView window, SpectatorView spectator) = Camera(SourceTv());
        spectator.Spectating = First;
        camera.EnterCamera(CameraMode.ThirdPerson);

        camera.CycleTarget(reverse: false);

        spectator.Spectating.ShouldBe(Second);
        window.Names.ShouldBe([nameof(ICameraView.CameraChanged), nameof(ICameraView.CameraChanged)]);
    }

    [Test]
    public void Aspect_BeforeTheViewportHasASize_IsSixteenByNine()
    {
        (CameraPresenter camera, FakeLoadView window, _) = Camera(SourceTv());

        camera.Aspect.ShouldBe(1280f / 720f);

        window.Window = new CameraWindow(BothAlive, BothAlive, 0, 0, false);

        camera.Aspect.ShouldBe(16f / 9f);
    }

    [Test]
    public void ReadPosition_WithTheReadoutOff_IsNothing()
    {
        (CameraPresenter camera, _, _) = Camera(SourceTv());

        camera.ReadPosition().ShouldBe(default);
    }

    [Test]
    public void Spectate_FromTheLaunchOptions_SetsWhoAndWhetherTheDirectorChooses()
    {
        (CameraPresenter camera, _, SpectatorView spectator) = Camera(SourceTv());

        camera.Spectate(Second, autoDirector: false);

        spectator.Spectating.ShouldBe(Second);
        spectator.AutoDirector.ShouldBeFalse();
        camera.Followed(BothAlive).ShouldBe(Second);
    }
}
