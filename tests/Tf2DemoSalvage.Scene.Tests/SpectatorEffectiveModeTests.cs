using System.Collections.Generic;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// A dead target is watched in third person — <c>C_HLTVCamera::CalcInEyeCamView</c>.
/// </summary>
/// <remarks>
/// <code>
/// if ( !pPlayer->IsAlive() )
/// {
///     // if dead, show from 3rd person
///     CalcChaseCamView( eyeOrigin, eyeAngles, fov );
///     return;
/// }
/// </code>
///
/// **The subject is the MODE, and that is what makes this a fix rather than a fourth attempt**
/// (D116). Two earlier goes at this citation swapped a camera or emptied a pair of hands, leaving
/// the viewer believing it was in first person while showing something else. The viewmodel rule
/// keys off that belief — <c>ShouldDrawViewModel</c> refuses whenever
/// <c>ShouldDrawLocalPlayer()</c> is true — so the mode has to change, and both consequences follow
/// from it: no viewmodel, and the followed player becomes visible.
/// </remarks>
public sealed class SpectatorEffectiveModeTests
{
    private static SpectatorView Watching(bool alive) =>
        new(new RecordingLogger())
        {
            Eyes = new FakeEyes(alive),
            Spectating = FakeEyes.Player,
        };

    [Test]
    public void Effective_WatchingALivingPlayerInFirstPerson_StaysInFirstPerson()
    {
        // The control. Without it a rule that always answered ThirdPerson would pass every case
        // below, and first person would be unreachable while looking like it worked.
        Watching(alive: true).Effective(0, CameraMode.FirstPerson)
            .ShouldBe(CameraMode.FirstPerson);
    }

    [Test]
    public void Effective_WatchingADeadPlayerInFirstPerson_FallsToThirdPerson()
    {
        Watching(alive: false).Effective(0, CameraMode.FirstPerson)
            .ShouldBe(CameraMode.ThirdPerson);
    }

    [Test]
    public void Effective_InThirdPersonWithADeadTarget_IsUnchanged()
    {
        // Chase already accepts a dead target — it is what CalcInEyeCamView bails TO — so there is
        // nothing to change here, and a rule that "corrected" it would be inventing a fourth mode.
        Watching(alive: false).Effective(0, CameraMode.ThirdPerson)
            .ShouldBe(CameraMode.ThirdPerson);
    }

    [Test]
    public void Effective_InTheFreeCameraWithADeadTarget_IsUnchanged()
    {
        // **The free camera is not watching anybody**, so nobody's liveness may move it. This is the
        // case that fails if the liveness test is applied before the mode is examined.
        Watching(alive: false).Effective(0, CameraMode.Free)
            .ShouldBe(CameraMode.Free);
    }

    /// <summary>A roster of one player, alive or not.</summary>
    private sealed class FakeEyes(bool alive) : IEyeSource
    {
        public const int Player = 3;

        public int? RecorderEntityIndex => null;

        public RecordedView? RecordedViewAt(int tick) => null;

        public IReadOnlyList<ScenePlayer> PlayersAt(int tick) =>
        [
            new ScenePlayer
            {
                EntityIndex = Player,
                X = 100f,
                Y = 200f,
                Z = 0f,
                Yaw = 90f,
                LifeState = alive ? 0 : 2,
            },
        ];
    }
}
