using System.Collections.Generic;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// First person requires an observer mode of NONE or IN_EYE —
/// <c>C_BasePlayer::LocalPlayerInFirstPersonView</c>.
/// </summary>
/// <remarks>
/// **Written off <c>game/client/c_baseplayer.cpp:1919</c> before anything decoded the field**, so
/// what it asserts is the engine's rule rather than a description of what got built:
///
/// <code>
///   int ObserverMode = pLocalPlayer->GetObserverMode();
///   if ( ( ObserverMode == OBS_MODE_NONE ) || ( ObserverMode == OBS_MODE_IN_EYE ) )
///   {
///       return !input->CAM_IsThirdPerson() &amp;&amp; ( !ToolsEnabled() || !ToolFramework_IsThirdPersonCamera() );
///   }
///
///   // Not looking at the local player, e.g. in a replay in third person mode or freelook.
///   return false;
/// </code>
///
/// and <c>ShouldDrawLocalPlayer()</c> is <c>!LocalPlayerInFirstPersonView()</c> when
/// <c>cl_first_person_uses_world_model</c> is off, which <c>ShouldDrawViewModel</c>
/// (<c>viewrender.cpp:974</c>) then refuses on. So an observer mode outside those two draws no
/// viewmodel, in the engine, unconditionally.
///
/// **This is B225, and the owner found it by watching the demo PLAY** — which had never happened
/// before, because autoplay had been broken (B223) and the viewer had never run a demo forward
/// unattended:
///
/// > *"i think i went spectator in this demo and we are either drawing viewmodels for spectators if
/// > that spec use to be a player, or the free cam which a player is put into after death, because
/// > when a player goes spec, after playing, they get put into the ingame free cam and the pov demo
/// > actually records what cam i looked at and what i was looking at, so it follows whatever cam i
/// > picked."*
///
/// The second reading is the engine's own: going to spectator puts the player in
/// <c>OBS_MODE_ROAMING</c>, and dying puts them through <c>OBS_MODE_DEATHCAM</c> and
/// <c>OBS_MODE_FREEZECAM</c>. All three are outside the pair above.
///
/// **This does not replace the life-state rule, it joins it** (<see cref="SpectatorEffectiveModeTests"/>).
/// They come from two different systems that this viewer imitates at once:
/// <c>C_HLTVCamera::CalcInEyeCamView</c> hands a DEAD target to the chase camera, and the client
/// rule above refuses first person for an observing one. Keeping both also covers the case the new
/// field cannot: <c>m_iObserverMode</c> absent from a recording reads as <c>OBS_MODE_NONE</c>,
/// because zero is the default and a delta-compressed format sends only what changed — so on a demo
/// that never sent it, liveness is the only thing left that can answer.
/// </remarks>
public sealed class ObserverModeConformanceTests
{
    /// <summary>The enum, <c>shareddefs.h:492</c>, in order.</summary>
    /// <remarks>
    /// Sent as 3 bits unsigned (<c>player.cpp:8184</c>), which is exactly enough for 0..7 — so
    /// every value the enum defines fits and none is unrepresentable.
    ///
    /// <c>OBS_MODE_POI</c> is 6 and was inserted mid-enum; the SDK comment says why, and it is a
    /// warning against hardcoding any of these: *"added in the middle of the enum due to tons of
    /// hard-coded '&lt;ROAMING' enum compares"*.
    /// </remarks>
    [TestCase(0, true, TestName = "OBS_MODE_NONE is first person")]
    [TestCase(1, false, TestName = "OBS_MODE_DEATHCAM is not")]
    [TestCase(2, false, TestName = "OBS_MODE_FREEZECAM is not")]
    [TestCase(3, false, TestName = "OBS_MODE_FIXED is not")]
    [TestCase(4, true, TestName = "OBS_MODE_IN_EYE is first person")]
    [TestCase(5, false, TestName = "OBS_MODE_CHASE is not")]
    [TestCase(6, false, TestName = "OBS_MODE_POI is not")]
    [TestCase(7, false, TestName = "OBS_MODE_ROAMING is not")]
    public void Effective_AtEachObserverMode_IsFirstPersonOnlyForNoneAndInEye(
        int observerMode, bool firstPerson)
    {
        // **Every mode, not a sample.** The rule is a two-value allowlist, so a test covering only
        // ROAMING would pass against an implementation that refused DEATHCAM and FREEZECAM and let
        // FIXED through — and FIXED is what an arena or a tournament camera puts a spectator in.
        CameraMode effective = Watching(observerMode).Effective(0, CameraMode.FirstPerson);

        effective.ShouldBe(
            firstPerson ? CameraMode.FirstPerson : CameraMode.ThirdPerson,
            $"observer mode {observerMode}: LocalPlayerInFirstPersonView allows only "
            + "OBS_MODE_NONE and OBS_MODE_IN_EYE");
    }

    [Test]
    public void Effective_WithNoObserverModeSent_StaysInFirstPerson()
    {
        // **Absent means OBS_MODE_NONE, not "unknown".** Zero is the default and the format sends
        // only what changed, so a recording that never mentions the field is a recording of someone
        // who never observed. Treating absence as unknown-and-therefore-refuse would drop every
        // demo into third person for ever.
        Watching(observerMode: null).Effective(0, CameraMode.FirstPerson)
            .ShouldBe(CameraMode.FirstPerson);
    }

    [Test]
    public void Effective_InTheFreeCameraWhileObserving_IsUnchanged()
    {
        // The free camera watches nobody, so nobody's observer mode may move it — the same case
        // that fails if the test is applied before the requested mode is examined.
        Watching(observerMode: 7).Effective(0, CameraMode.Free).ShouldBe(CameraMode.Free);
    }

    [Test]
    public void Effective_ObservingWhileAlive_StillFallsToThirdPerson()
    {
        // **The case that separates this rule from the life-state one**, and without it the two are
        // indistinguishable. A player who goes to spectator is ALIVE by `m_lifeState` — spectating
        // is not dying — so the existing liveness check answers "first person is fine" and only the
        // observer mode says otherwise. That is exactly the demo the owner was watching.
        Watching(observerMode: 7, alive: true).Effective(0, CameraMode.FirstPerson)
            .ShouldBe(CameraMode.ThirdPerson);
    }

    private static SpectatorView Watching(int? observerMode, bool alive = true) =>
        new(new RecordingLogger())
        {
            Eyes = new FakeObservingEyes(observerMode, alive),
            Spectating = FakeObservingEyes.Player,
        };

    /// <summary>A roster of one player, observing or not.</summary>
    private sealed class FakeObservingEyes(int? observerMode, bool alive) : IEyeSource
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
                ObserverMode = observerMode,
            },
        ];
    }
}
