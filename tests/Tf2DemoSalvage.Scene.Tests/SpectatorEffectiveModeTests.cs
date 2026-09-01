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

    [Test]
    public void Effective_WhenTheDeadRecorderIsNotTheChosenTarget_StillFallsToThirdPerson()
    {
        // **B225's actual mechanism, and it is neither thing anyone guessed.** On a point-of-view
        // demo the eye comes from the RECORDER's own camera, but this rule was asking
        // `Target(tick)` — which is `SpectatorTarget.Choose`, the lowest entity index on a playing
        // team. So the mode was decided by the liveness of a DIFFERENT PLAYER: the recorder died,
        // somebody else was alive, and the viewer stayed in first person drawing the dead man's
        // weapon.
        //
        // Measured on the demo the owner was watching: a 30-second autoplay run straight through
        // the death at tick 2008 drew the viewmodel 30 times and skipped once, and logged exactly
        // one mode line — "first person on" — with no fall to third person anywhere.
        //
        // **`Followed(tick)` already resolved this correctly** and says why in its own remarks:
        // *"Asked in one place so the two decisions cannot disagree."* This rule simply did not ask
        // it. The control below is the same roster with the recorder ALIVE.
        WatchingPointOfView(new PovEyes(recorderAlive: false)).Effective(0, CameraMode.FirstPerson)
            .ShouldBe(CameraMode.ThirdPerson);
    }

    [Test]
    public void Effective_WhenTheRecorderIsAliveAndAnotherPlayerIsDead_StaysInFirstPerson()
    {
        // **The control, and it is the half that makes the test above mean something.** Without it,
        // a rule that consulted "is ANY player dead" would satisfy the case above while being just
        // as wrong — it would drop into third person whenever anyone in the server died.
        WatchingPointOfView(new PovEyes(recorderAlive: true)).Effective(0, CameraMode.FirstPerson)
            .ShouldBe(CameraMode.FirstPerson);
    }

    /// <summary>A view over a point-of-view roster, with no spectate override.</summary>
    /// <remarks>
    /// **Named apart from <c>Watching</c> rather than overloading it**, because S4136 requires
    /// overloads to be adjacent and the two belong at opposite ends of this file — one beside the
    /// liveness cases, one beside the roster it needs. A distinct name also says which fixture is
    /// in play at the call site, which an overload would hide.
    /// </remarks>
    private static SpectatorView WatchingPointOfView(IEyeSource eyes) =>
        new(new RecordingLogger()) { Eyes = eyes };

    /// <summary>
    /// A point-of-view roster: a recorder with his own camera, plus another player of the opposite
    /// liveness who is the one <c>SpectatorTarget.Choose</c> would pick.
    /// </summary>
    /// <remarks>
    /// **The other player has the LOWER entity index deliberately**, because that is what `Choose`
    /// selects — so "asks the recorder" and "asks the chosen target" give opposite answers here.
    /// With the recorder at the lower index the two would agree and the test could not fail.
    /// </remarks>
    private sealed class PovEyes(bool recorderAlive) : IEyeSource
    {
        private const int Other = 2;

        private const int Recorder = 7;

        public bool HasRecordedView => true;

        public int? RecorderEntityIndex => Recorder;

        public RecordedView? RecordedViewAt(int tick) =>
            new((100f, 200f, 64f), (0f, 90f, 0f), IsCut: false);

        public IReadOnlyList<ScenePlayer> PlayersAt(int tick) =>
        [
            new ScenePlayer
            {
                EntityIndex = Other,
                X = 10f,
                Y = 20f,
                Z = 0f,
                Team = SceneTeams.Red,
                LifeState = recorderAlive ? 2 : 0,
            },
            new ScenePlayer
            {
                EntityIndex = Recorder,
                X = 100f,
                Y = 200f,
                Z = 0f,
                Team = SceneTeams.Red,
                LifeState = recorderAlive ? 0 : 2,
            },
        ];
    }

    /// <summary>A roster of one player, alive or not.</summary>
    private sealed class FakeEyes(bool alive) : IEyeSource
    {
        public const int Player = 3;

        public bool HasRecordedView => false;

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
