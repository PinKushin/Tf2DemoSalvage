namespace Tf2DemoSalvage.Core.Scene;

/// <summary>
/// The local player's anim-state feet, advanced once per rendered frame from his own view (B450).
/// </summary>
/// <remarks>
/// <c>C_TFPlayer::UpdateClientSideAnimation</c> hands the local player's anim state <c>EyeAngles()</c>
/// (<c>c_tf_player.cpp:4279-4284</c>) — <c>InterpolateViewpoint</c>'s local angles (B56) — and
/// <c>ComputePoseParam_AimYaw</c> converges the feet by <c>gpGlobals-&gt;frametime</c>
/// (<c>multiplayer_animstate.cpp:1759</c>). Everyone else keeps the timeline's per-tick feet.
///
/// **One per playing viewer, never on the timeline**, for <see cref="DemoPlayer"/>'s reason: it is per-client state.
/// The frame time is the playback moved since the last frame. **Departure:** the engine's state runs from spawn; this
/// one starts, and restarts after a backward move, from the timeline's feet at that tick — the nearest carried state,
/// as a backward move restarts <see cref="DemoPlayer"/>'s reader.
/// </remarks>
public sealed class RecorderFeet
{
    private FeetYaw _feet;

    private double _lastTick = double.NaN;

    /// <summary>Advances the feet to <paramref name="tick"/> and says where they point.</summary>
    /// <param name="tick">The fractional tick this frame shows.</param>
    /// <param name="eyeYaw">The local yaw <c>InterpolateViewpoint</c> set this frame.</param>
    /// <param name="speed">His velocity's three-dimensional length (<c>:1709</c>).</param>
    /// <param name="timelineFeet">The timeline's feet at this tick, started from when there is no earlier frame.</param>
    /// <param name="intervalPerTick">Seconds a tick.</param>
    /// <returns><c>m_flCurrentFeetYaw</c>.</returns>
    internal float Advance(double tick, float eyeYaw, float speed, float timelineFeet, float intervalPerTick)
    {
        // NaN (no frame yet) and a backward move both fail this.
        if (!(tick >= _lastTick))
        {
            _feet = FeetYaw.Planted(timelineFeet);
            _lastTick = tick;
        }

        _feet.Advance(eyeYaw, speed, (float)(tick - _lastTick) * intervalPerTick);
        _lastTick = tick;

        return _feet.Current;
    }
}
