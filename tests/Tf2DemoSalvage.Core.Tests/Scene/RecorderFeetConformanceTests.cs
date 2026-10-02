using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// The local player's feet advance once per rendered frame, from his own view (B450).
/// </summary>
/// <remarks>
/// <c>C_TFPlayer::UpdateClientSideAnimation</c> hands the local player's anim state <c>EyeAngles()</c>
/// (<c>c_tf_player.cpp:4279-4284</c>) — for him <c>pl.v_angle</c>, what <c>InterpolateViewpoint</c> set this frame
/// (B56, B442) — and <c>CMultiPlayerAnimState::Update</c> (<c>multiplayer_animstate.cpp:1327-1361</c>; TF's override,
/// <c>tf_playeranimstate.cpp:325-499</c>, calls the same <c>ComputePoseParam_AimYaw</c>) converges the feet by
/// <c>gpGlobals-&gt;frametime</c> (<c>:1759</c>), then measures the torso against those same feet (<c>:1765-1772</c>).
/// So both the feet and the twist read one per-frame angle; neither is the server's <c>m_angEyeAngles</c>.
/// </remarks>
public sealed class RecorderFeetConformanceTests
{
    private const string Player = "src/game/client/tf/c_tf_player.cpp";

    private const string AnimState = "src/game/shared/Multiplayer/multiplayer_animstate.cpp";

    [Test]
    public void UpdateClientSideAnimation_ForTheLocalPlayer_PassesEyeAnglesNotTheNetworkedOnes()
    {
        Source(Player).ShouldMatch(
            @"QAngle\s+LocalEyeAngles\s*=\s*EyeAngles\(\);\s*" +
            @"m_PlayerAnimState->Update\(\s*LocalEyeAngles\[YAW\]");
    }

    [Test]
    public void ComputePoseParamAimYaw_TheFeet_ConvergeByTheFrameTime()
    {
        Source(AnimState).ShouldMatch(
            @"ConvergeYawAngles\(\s*m_flGoalFeetYaw,[^;]*gpGlobals->frametime,\s*m_flCurrentFeetYaw\s*\)");
    }

    [Test]
    public void ComputePoseParamAimYaw_TheTwist_IsMeasuredAgainstTheFeetJustConverged()
    {
        Source(AnimState).ShouldMatch(
            @"m_angRender\[YAW\]\s*=\s*m_flCurrentFeetYaw;[^}]*flAimYaw\s*=\s*m_flEyeYaw\s*-\s*m_flCurrentFeetYaw;");
    }

    private static string Source(string path)
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore("the Source SDK is not available");
        }

        return SourceSdk.Text(path).ShouldNotBeNull();
    }
}
