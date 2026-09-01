using System.Collections.Generic;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// A point-of-view demo is locked to the recorder's view — D128.
/// </summary>
/// <remarks>
/// **The owner, settling B256:** *"the pov demo lock is going to be demo-kind, we do exactly what
/// tf2 does, because tryign to do anything else whould be creating information we dont have."*
///
/// **The reason is the data, not taste.** A POV demo is PVS-limited: entities outside the
/// recorder's visibility were never transmitted, so a free camera pointed away from the recorder
/// shows a world that was never recorded — offering the camera is offering to fabricate. TF2's own
/// playback of a POV demo is the recorded view and nothing else: no spectator UI, no roaming, no
/// chase. A SourceTV demo carries the whole server and keeps every mode.
/// </remarks>
public sealed class PovCameraLockTests
{
    private static SpectatorView Spectator(bool pov) => new(NullLogger.Instance)
    {
        Eyes = new KindedEyes(pov),
    };

    [Test]
    public void Refuses_TheFreeCameraOnAPovDemo_NamesTheWorldThatWasNeverRecorded()
    {
        CameraRefusal? refusal = Spectator(pov: true).Refuses(CameraMode.Free);

        refusal.ShouldNotBeNull();

        refusal.Value.Message.ShouldContain(
            "never", customMessage: "the refusal must say the data does not exist, not just no");
    }

    [Test]
    public void Refuses_ThirdPersonOnAPovDemo_IsRefusedLikeTheFreeCamera()
    {
        Spectator(pov: true).Refuses(CameraMode.ThirdPerson).ShouldNotBeNull();
    }

    [Test]
    public void Refuses_FirstPersonOnAPovDemo_IsAllowed()
    {
        Spectator(pov: true).Refuses(CameraMode.FirstPerson).ShouldBeNull();
    }

    /// <remarks>
    /// The control: without it, `Refuses` could return a refusal unconditionally and every test
    /// above would pass while SourceTV playback lost its free camera.
    /// </remarks>
    [Test]
    public void Refuses_EveryModeOnASourceTvDemo_IsAllowed()
    {
        SpectatorView spectator = Spectator(pov: false);

        spectator.Refuses(CameraMode.Free).ShouldBeNull();
        spectator.Refuses(CameraMode.FirstPerson).ShouldBeNull();
        spectator.Refuses(CameraMode.ThirdPerson).ShouldBeNull();
    }

    [Test]
    public void Refuses_WithNoDemoOpen_IsAllowed()
    {
        SpectatorView spectator = new(NullLogger.Instance);

        spectator.Refuses(CameraMode.Free).ShouldBeNull();
    }

    /// <remarks>
    /// **Cycling targets is the same fabrication one step smaller**: the demo follows its
    /// recorder, and "spectate somebody else" asks for a view the file cannot answer — the other
    /// players exist only where the recorder saw them.
    /// </remarks>
    [Test]
    public void Cycle_OnAPovDemo_RefusesToLeaveTheRecorder()
    {
        SpectatorSwitch result = Spectator(pov: true).Cycle(tick: 5, reverse: false);

        result.Switched.ShouldBeFalse();
        result.Message.ShouldContain("recorder");
    }

    /// <remarks>
    /// The control for the guard above: a SourceTV roster still cycles, so the refusal cannot be
    /// implemented as "Cycle never switches".
    /// </remarks>
    [Test]
    public void Cycle_OnASourceTvDemo_StillWalksTheRoster()
    {
        Spectator(pov: false).Cycle(tick: 5, reverse: false).Switched.ShouldBeTrue();
    }

    /// <summary>Eyes whose demo kind is the test's choice, with two followable players.</summary>
    private sealed class KindedEyes(bool pov) : IEyeSource
    {
        public bool HasRecordedView => pov;

        public int? RecorderEntityIndex => pov ? 1 : null;

        public RecordedView? RecordedViewAt(int tick) =>
            pov ? new RecordedView((0f, 0f, 0f), (0f, 0f, 0f), IsCut: false) : null;

        public IReadOnlyList<ScenePlayer> PlayersAt(int tick) =>
        [
            new ScenePlayer(1, 0f, 0f, 0f, Team: 2, Health: 100, PlayerClass: 3),
            new ScenePlayer(2, 10f, 0f, 0f, Team: 3, Health: 100, PlayerClass: 1),
        ];
    }
}
